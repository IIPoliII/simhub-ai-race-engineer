using System;
using System.Collections.Generic;
using System.Linq;
using GameReaderCommon;
using Newtonsoft.Json;
using SimHub.Plugins;

namespace AIRaceEngineer
{
    /// <summary>
    /// Ingénieur de course IA : expose la télémétrie unifiée de SimHub (tous les jeux) au client vocal,
    /// gère les réglages, le push-to-talk (Controls and events) et les annonces automatiques.
    /// </summary>
    [PluginDescription("Ingénieur de course IA vocal : pose tes questions à la radio, reçois les annonces de course")]
    [PluginAuthor("iipoliii")]
    [PluginName("AI Race Engineer")]
    public class AIRaceEnginePlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public const int Port = 8889;
        public const string PushToTalkAction = "PushToTalk";
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan LapRecordDelay = TimeSpan.FromSeconds(1.5);

        public PluginManager PluginManager { get; set; }
        public string LeftMenuTitle => "AI Race Engineer";
        public System.Windows.Media.ImageSource PictureIcon => SettingsControl.Icon;
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this);

        // État lu par le panneau
        public EngineerSettings Settings { get; private set; }
        public Updater Updater { get; } = new Updater();
        public ClientRunner Client { get; } = new ClientRunner();
        public string Status { get; private set; } = "En attente du jeu";
        public string ServerError { get; private set; }
        public string ClientStatus { get; private set; } = "";
        public bool PushToTalkDown => _ptt;
        public bool ClientConnected => (DateTime.UtcNow - _lastClientPoll).TotalSeconds < 2;
        public string Url => $"http://127.0.0.1:{Port}/snapshot";

        private SnapshotServer _server;
        private Callouts _callouts;
        private volatile bool _ptt;
        private DateTime _lastClientPoll = DateTime.MinValue;

        // Configuration envoyée au client (régénérée à chaque modification)
        private readonly object _configLock = new object();
        private int _configRev;
        private string _configJson = "{}";

        // Télémétrie
        private DateTime _lastSnapshot = DateTime.MinValue;
        private volatile string _json = Offline();

        // Annonces à prononcer par le client
        private readonly object _eventLock = new object();
        private readonly List<(int Id, DateTime At, string Text)> _events = new List<(int, DateTime, string)>();
        private int _lastEventId;

        // Historique des tours
        private readonly List<object> _laps = new List<object>();
        private readonly object _lapsLock = new object();
        private int _lastCompletedLaps = -1;
        private double _fuelAtLapStart = double.NaN;
        private (DateTime At, int Lap, double? FuelUsed)? _pendingLapRecord;
        private Guid _sessionId;

        public void Init(PluginManager pluginManager)
        {
            Settings = EngineerSettings.Load();
            PublishConfig();
            _callouts = new Callouts(() => Settings, Say);

            // Push-to-talk : à associer à un bouton dans Controls and events
            pluginManager.AddAction(PushToTalkAction, GetType(), (pm, a) => _ptt = true, (pm, a) => _ptt = false);

            _server = new SnapshotServer(Port, Route);
            try
            {
                _server.Start();
                SimHub.Logging.Current.Info($"[AI Race Engineer] Télémétrie disponible sur {Url}");
            }
            catch (Exception ex)
            {
                ServerError = $"Impossible d'ouvrir le port {Port} : {ex.Message}";
                SimHub.Logging.Current.Error("[AI Race Engineer] " + ServerError);
            }

            if (Settings.AutoStartClient && ServerError == null) Client.Start();
            Updater.CheckAsync(); // en arrière-plan, n'installe rien sans clic
        }

        public void End(PluginManager pluginManager)
        {
            Client.Stop();
            _server?.Stop();
        }

        /// <summary>À appeler après chaque modification des réglages dans le panneau.</summary>
        public void SettingsChanged()
        {
            try { Settings.Save(); }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[AI Race Engineer] sauvegarde : " + ex.Message); }
            PublishConfig();
        }

        private void PublishConfig()
        {
            lock (_configLock)
            {
                _configRev++;
                _configJson = JsonConvert.SerializeObject(Settings.ToClientConfig(_configRev));
            }
        }

        /// <summary>Met une phrase dans la file du client vocal.</summary>
        public void Say(string text)
        {
            lock (_eventLock)
            {
                _events.Add((++_lastEventId, DateTime.UtcNow, text));
                if (_events.Count > 20) _events.RemoveAt(0);
            }
        }

        // ---------- Serveur local ----------

        private string Route(string path, Dictionary<string, string> query)
        {
            switch (path)
            {
                case "/snapshot":
                    return _json;
                case "/config":
                    lock (_configLock) return _configJson;
                case "/poll":
                    _lastClientPoll = DateTime.UtcNow;
                    return Poll(query.TryGetValue("since", out var s) && int.TryParse(s, out var since) ? since : -1);
                case "/client-status":
                    if (query.TryGetValue("m", out var m)) ClientStatus = m.Length > 200 ? m.Substring(0, 200) : m;
                    return "{}";
                default:
                    return null;
            }
        }

        private string Poll(int since)
        {
            int rev;
            lock (_configLock) rev = _configRev;
            lock (_eventLock)
            {
                var now = DateTime.UtcNow;
                var events = since < 0
                    ? new object[0]
                    : _events.Where(e => e.Id > since)
                             .Select(e => (object)new { id = e.Id, text = e.Text, age_ms = (int)(now - e.At).TotalMilliseconds })
                             .ToArray();
                return JsonConvert.SerializeObject(new { ptt = _ptt, rev, last_id = _lastEventId, events });
            }
        }

        // ---------- Télémétrie ----------

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning || data.NewData == null)
            {
                _json = Offline();
                Status = "En attente du jeu";
                return;
            }

            var d = data.NewData;
            TrackLaps(data, d);

            var now = DateTime.UtcNow;
            if (now - _lastSnapshot < SnapshotInterval) return;
            _lastSnapshot = now;

            try
            {
                var n = Neighbours.From(d);
                _callouts.Update(d, n.MyPos, n.Ahead, n.Behind);
                _json = JsonConvert.SerializeObject(Build(data, d, n), Formatting.None);
                Status = $"Télémétrie OK : {data.GameName}, {d.TrackName}, P{n.MyPos}";
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Warn("[AI Race Engineer] snapshot: " + ex.Message);
            }
        }

        private static string Offline() => "{\"game_running\":false}";

        private void TrackLaps(GameData data, StatusDataBase d)
        {
            if (data.SessionId != _sessionId)
            {
                _sessionId = data.SessionId;
                lock (_lapsLock) _laps.Clear();
                _lastCompletedLaps = -1;
                _fuelAtLapStart = double.NaN;
                _pendingLapRecord = null;
                _callouts.Reset();
            }

            // Le temps du tour est lu un peu après la ligne : certains jeux le mettent à jour en retard
            if (_pendingLapRecord is var p && p.HasValue && DateTime.UtcNow >= p.Value.At)
            {
                _pendingLapRecord = null;
                lock (_lapsLock)
                {
                    _laps.Add(new { lap = p.Value.Lap, time = T(d.LastLapTime), fuel_used = p.Value.FuelUsed });
                    if (_laps.Count > 5) _laps.RemoveAt(0);
                }
            }

            if (d.CompletedLaps == _lastCompletedLaps) return;

            if (_lastCompletedLaps >= 0 && d.CompletedLaps == _lastCompletedLaps + 1)
            {
                double? fuelUsed = double.IsNaN(_fuelAtLapStart) ? (double?)null : R(_fuelAtLapStart - d.Fuel, 2);
                _pendingLapRecord = (DateTime.UtcNow + LapRecordDelay, d.CompletedLaps, fuelUsed);
                _callouts.OnLapCompleted();
            }
            _lastCompletedLaps = d.CompletedLaps;
            _fuelAtLapStart = d.Fuel;
        }

        private object[] RecentLaps()
        {
            lock (_lapsLock) return _laps.ToArray();
        }

        /// <summary>Le pilote et ses voisins directs au classement (dans sa classe en multiclasse).</summary>
        internal sealed class Neighbours
        {
            public bool MultiClass;
            public int MyPos;
            public Opponent Me, Ahead, Behind;

            public static Neighbours From(StatusDataBase d)
            {
                bool multiClass = d.HasMultipleClassOpponents;
                var field = (multiClass ? d.OpponentsPlayerClass : d.Opponents) ?? new List<Opponent>();
                int Pos(Opponent o) => multiClass ? o.PositionInClass : o.Position;

                var me = field.FirstOrDefault(o => o.IsPlayer);
                int myPos = me != null ? Pos(me) : (multiClass ? d.PlayerClassPosition : d.Position);
                return new Neighbours
                {
                    MultiClass = multiClass,
                    MyPos = myPos,
                    Me = me,
                    Ahead = field.FirstOrDefault(o => !o.IsPlayer && Pos(o) == myPos - 1),
                    Behind = field.FirstOrDefault(o => !o.IsPlayer && Pos(o) == myPos + 1),
                };
            }
        }

        private object Build(GameData data, StatusDataBase d, Neighbours n)
        {
            return new
            {
                game_running = true,
                game = data.GameName,
                session = new
                {
                    type = d.SessionTypeName,
                    track = d.TrackNameWithConfig,
                    car = d.CarModel,
                    car_class = d.CarClass,
                    time_left = T(d.SessionTimeLeft),
                    total_laps = d.TotalLaps > 0 ? d.TotalLaps : (int?)null,
                    remaining_laps = d.RemainingLaps > 0 ? d.RemainingLaps : (int?)null,
                    air_temp = R(d.AirTemperature, 1),
                    track_temp = R(d.RoadTemperature, 1),
                    temp_unit = d.TemperatureUnit,
                    flag = string.IsNullOrEmpty(d.Flag_Name) ? null : d.Flag_Name,
                },
                me = new
                {
                    position = n.MyPos,
                    field_size = n.MultiClass ? d.PlayerClassOpponentsCount : d.OpponentsCount,
                    lap = d.CurrentLap,
                    current_lap_time = T(d.CurrentLapTime),
                    last_lap = T(d.LastLapTime),
                    best_lap = T(d.BestLapTime),
                    session_best_lap = T(d.SessionFastestLapTime),
                    delta_to_best = R(d.DeltaToSessionBest, 2),
                    lap_valid = d.IsLapValid,
                    gap_to_leader = n.Me?.GaptoLeader is double g ? R(Math.Abs(g), 1) : null,
                    in_pit_lane = d.IsInPitLane == 1,
                    speed_kmh = R(d.SpeedKmh, 0),
                    gear = d.Gear,
                },
                fuel = new
                {
                    level = R(d.Fuel, 1),
                    capacity = R(d.MaxFuel, 1),
                    unit = d.FuelUnit,
                    estimated_laps_left = R(d.EstimatedFuelRemaingLaps, 1),
                },
                tyres = new
                {
                    compound = d.FrontTyreCompound,
                    pressure_unit = d.TyrePressureUnit,
                    pressure = Corners(d.TyrePressureFrontLeft, d.TyrePressureFrontRight, d.TyrePressureRearLeft, d.TyrePressureRearRight, 1),
                    temperature = Corners(d.TyreTemperatureFrontLeft, d.TyreTemperatureFrontRight, d.TyreTemperatureRearLeft, d.TyreTemperatureRearRight, 0),
                    wear_pct = Corners(d.TyreWearFrontLeft, d.TyreWearFrontRight, d.TyreWearRearLeft, d.TyreWearRearRight, 1),
                    brake_temperature = Corners(d.BrakeTemperatureFrontLeft, d.BrakeTemperatureFrontRight, d.BrakeTemperatureRearLeft, d.BrakeTemperatureRearRight, 0),
                },
                car = new
                {
                    damage_avg_pct = R(d.CarDamagesAvg, 1),
                    tc = d.TCLevel,
                    abs = d.ABSLevel,
                    brake_bias = R(d.BrakeBias, 1),
                    engine_map = d.EngineMap,
                },
                car_ahead = Rival(n.Ahead),
                car_behind = Rival(n.Behind),
                recent_laps = RecentLaps(),
            };
        }

        private static object Rival(Opponent o)
        {
            if (o == null) return null;
            return new
            {
                name = Clean(o.Name),
                car = Clean(o.CarName),
                position = o.Position,
                gap_s = o.GaptoPlayer is double g ? R(Math.Abs(g), 1) : null,
                laps_apart = o.LapsToPlayer is int l && l != 0 ? Math.Abs(l) : (int?)null,
                last_lap = T(o.LastLapTime),
                best_lap = T(o.BestLapTime),
                in_pit = o.IsCarInPitLane || o.IsCarInPit,
                pit_stops = o.PitCount,
            };
        }

        /// <summary>Les pseudos viennent des autres joueurs : texte court, sans caractères de contrôle.</summary>
        private static string Clean(string s, int max = 32)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.Where(c => !char.IsControl(c)).Take(max).ToArray();
            return new string(chars).Trim();
        }

        private static object Corners(double fl, double fr, double rl, double rr, int digits)
        {
            if (fl == 0 && fr == 0 && rl == 0 && rr == 0) return null; // non fourni par le jeu
            return new { fl = R(fl, digits), fr = R(fr, digits), rl = R(rl, digits), rr = R(rr, digits) };
        }

        private static double? R(double? v, int digits) =>
            v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value) ? Math.Round(v.Value, digits) : (double?)null;

        /// <summary>Temps au format m:ss.fff, null si absent.</summary>
        private static string T(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) return null;
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}";
        }
    }
}
