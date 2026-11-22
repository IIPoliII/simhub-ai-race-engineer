using System;
using System.Collections.Generic;
using System.Linq;
using GameReaderCommon;
using Newtonsoft.Json;
using SimHub.Plugins;

namespace AIRaceEngineer
{
    /// <summary>
    /// Expose la télémétrie unifiée de SimHub (tous les jeux) en JSON sur
    /// http://127.0.0.1:8889/snapshot pour le client vocal.
    /// </summary>
    [PluginDescription("Expose la télémétrie en JSON local pour un ingénieur de course IA vocal")]
    [PluginAuthor("iipoliii")]
    [PluginName("AI Race Engineer")]
    public class AIRaceEnginePlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        // Affichés dans le panneau SimHub
        public string Status { get; private set; } = "En attente du jeu";
        public string ServerError { get; private set; }
        public string Url => $"http://127.0.0.1:{Port}/snapshot";
        public Updater Updater { get; } = new Updater();

        public string LeftMenuTitle => "AI Race Engineer";
        public System.Windows.Media.ImageSource PictureIcon => SettingsControl.Icon;
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this);

        public const int Port = 8889;
        private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(250);

        private SnapshotServer _server;
        private DateTime _lastSnapshot = DateTime.MinValue;
        private volatile string _json = Offline();

        // Historique des tours, calculé par le plugin
        private readonly List<object> _laps = new List<object>();
        private int _lastCompletedLaps = -1;
        private double _fuelAtLapStart = double.NaN;
        private Guid _sessionId;

        public PluginManager PluginManager { get; set; }

        public void Init(PluginManager pluginManager)
        {
            _server = new SnapshotServer(Port, () => _json);
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
            Updater.CheckAsync(); // en arrière-plan, n'installe rien sans clic
        }

        public void End(PluginManager pluginManager)
        {
            _server?.Stop();
        }

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
                _json = JsonConvert.SerializeObject(Build(data, d), Formatting.None);
                Status = $"Télémétrie OK : {data.GameName}, {d.TrackName}, P{d.Position}";
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
                _laps.Clear();
                _lastCompletedLaps = -1;
                _fuelAtLapStart = double.NaN;
            }

            if (d.CompletedLaps == _lastCompletedLaps) return;

            if (_lastCompletedLaps >= 0 && d.CompletedLaps == _lastCompletedLaps + 1)
            {
                double? fuelUsed = double.IsNaN(_fuelAtLapStart) ? (double?)null : R(_fuelAtLapStart - d.Fuel, 2);
                _laps.Add(new
                {
                    lap = d.CompletedLaps,
                    time = T(d.LastLapTime),
                    fuel_used = fuelUsed,
                });
                if (_laps.Count > 5) _laps.RemoveAt(0);
            }
            _lastCompletedLaps = d.CompletedLaps;
            _fuelAtLapStart = d.Fuel;
        }

        private object Build(GameData data, StatusDataBase d)
        {
            bool multiClass = d.HasMultipleClassOpponents;
            var field = (multiClass ? d.OpponentsPlayerClass : d.Opponents) ?? new List<Opponent>();
            int Pos(Opponent o) => multiClass ? o.PositionInClass : o.Position;

            var me = field.FirstOrDefault(o => o.IsPlayer);
            int myPos = me != null ? Pos(me) : (multiClass ? d.PlayerClassPosition : d.Position);

            var ahead = field.FirstOrDefault(o => !o.IsPlayer && Pos(o) == myPos - 1);
            var behind = field.FirstOrDefault(o => !o.IsPlayer && Pos(o) == myPos + 1);

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
                    position = myPos,
                    field_size = multiClass ? d.PlayerClassOpponentsCount : d.OpponentsCount,
                    lap = d.CurrentLap,
                    current_lap_time = T(d.CurrentLapTime),
                    last_lap = T(d.LastLapTime),
                    best_lap = T(d.BestLapTime),
                    session_best_lap = T(d.SessionFastestLapTime),
                    delta_to_best = R(d.DeltaToSessionBest, 2),
                    lap_valid = d.IsLapValid,
                    gap_to_leader = me?.GaptoLeader is double g ? R(Math.Abs(g), 1) : null,
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
                car_ahead = Rival(ahead),
                car_behind = Rival(behind),
                recent_laps = _laps.ToArray(),
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
