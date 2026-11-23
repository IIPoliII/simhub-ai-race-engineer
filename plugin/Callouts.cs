using System;
using System.Collections.Generic;
using System.Globalization;
using GameReaderCommon;

namespace AIRaceEngineer
{
    /// <summary>
    /// Annonces automatiques (sans IA, donc instantanées et gratuites) :
    /// temps au tour, meilleur tour, écarts, positions, carburant.
    /// Le texte est écrit pour être lu à voix haute.
    /// </summary>
    internal sealed class Callouts
    {
        private static readonly TimeSpan LapDelay = TimeSpan.FromSeconds(1.5);    // certains jeux mettent à jour LastLapTime en retard
        private static readonly TimeSpan PositionDebounce = TimeSpan.FromSeconds(3);

        private readonly Func<EngineerSettings> _settings;
        private readonly Action<string> _say;

        private TimeSpan _prevLap, _sessionBest;
        private DateTime? _pendingLap;
        private bool _pitThisLap, _pendingLapWasPit, _pendingLapValid;
        private int _announcedPos, _candidatePos;
        private DateTime _candidateSince;
        private int _fuelLevelAnnounced = int.MaxValue;
        private double _minFuel = double.MaxValue;   // plus bas niveau depuis le dernier ravitaillement

        public Callouts(Func<EngineerSettings> settings, Action<string> say)
        {
            _settings = settings;
            _say = say;
        }

        /// <param name="knownBest">meilleur tour déjà fait dans la session (plugin lancé en cours de route), sinon zéro</param>
        public void Reset(TimeSpan knownBest)
        {
            _prevLap = TimeSpan.Zero;
            _sessionBest = knownBest > TimeSpan.Zero ? knownBest : TimeSpan.Zero;
            _pendingLap = null;
            _pitThisLap = false;
            _announcedPos = _candidatePos = 0;
            _fuelLevelAnnounced = int.MaxValue;
            _minFuel = double.MaxValue;
        }

        /// <param name="valid">tour resté valide jusqu'à la ligne (suivi à chaque image par le plugin)</param>
        public void OnLapCompleted(bool valid)
        {
            _pendingLap = DateTime.UtcNow + LapDelay;
            _pendingLapWasPit = _pitThisLap;
            _pendingLapValid = valid;
            _pitThisLap = false;
        }

        public void Update(StatusDataBase d, int myPos, Opponent ahead, Opponent behind)
        {
            var s = _settings();
            bool fr = s.Language == "fr";
            bool race = IsRace(d.SessionTypeName);
            var now = DateTime.UtcNow;
            if (d.IsInPitLane == 1) _pitThisLap = true;

            // --- Fin de tour ---
            if (_pendingLap.HasValue && now >= _pendingLap.Value)
            {
                _pendingLap = null;
                var lap = d.LastLapTime;
                if (_pendingLapWasPit || lap <= TimeSpan.Zero)
                {
                    _prevLap = TimeSpan.Zero; // pas de comparaison avec un tour de stand
                }
                else
                {
                    var parts = new List<string>();
                    // Tour invalidé (coupé, hors limites) : jamais « meilleur tour », et il ne devient pas la référence
                    bool pb = _pendingLapValid && _sessionBest > TimeSpan.Zero && lap < _sessionBest;
                    if (s.CallPersonalBest && pb) parts.Add(fr ? "Meilleur tour !" : "Personal best!");
                    if (s.CallLapTime)
                    {
                        var text = (fr ? "Tour en " : "Lap time ") + SpokenLap(lap, fr) + ".";
                        if (_prevLap > TimeSpan.Zero)
                        {
                            // La phrase commence par un mot : « 9. 3 dixièmes » serait lu « 9,3 »
                            var delta = (lap - _prevLap).TotalSeconds;
                            text += " " + (delta < 0 ? (fr ? "Plus rapide de " : "Quicker by ") : (fr ? "Plus lent de " : "Slower by ")) +
                                    SpokenDelta(Math.Abs(delta), fr) + ".";
                        }
                        parts.Add(text);
                    }
                    if (s.CallGaps && race)
                    {
                        var gaps = SpokenGaps(ahead, behind, fr);
                        if (gaps != null) parts.Add(gaps);
                    }
                    if (parts.Count > 0) _say(string.Join(" ", parts));

                    if (_pendingLapValid && (_sessionBest == TimeSpan.Zero || lap < _sessionBest)) _sessionBest = lap;
                    _prevLap = lap;
                }
            }

            // --- Positions (course uniquement, après 3 s de stabilité) ---
            if (s.CallPosition && race && myPos > 0)
            {
                if (_announcedPos == 0) _announcedPos = myPos;
                else if (myPos == _announcedPos) _candidatePos = 0;
                else if (myPos != _candidatePos) { _candidatePos = myPos; _candidateSince = now; }
                else if (now - _candidateSince >= PositionDebounce)
                {
                    _say(SpokenPosition(myPos, _announcedPos - myPos, fr));
                    _announcedPos = myPos;
                    _candidatePos = 0;
                }
            }

            // --- Carburant ---
            // Ravitaillement, même progressif (quelques dixièmes de litre par appel) : comparé au plus bas niveau vu
            if (d.Fuel < _minFuel) _minFuel = d.Fuel;
            else if (d.Fuel > _minFuel + 0.5) { _fuelLevelAnnounced = int.MaxValue; _minFuel = d.Fuel; }
            if (s.CallFuel && d.IsInPitLane == 0 && d.EstimatedFuelRemaingLaps is double laps && laps > 0)
            {
                int threshold = Math.Max(1, s.FuelWarningLaps);
                int level = laps <= 1.2 ? 1 : laps <= threshold ? threshold : int.MaxValue;
                if (level < _fuelLevelAnnounced)
                {
                    // Arrondi : 2,98 tours au seuil de 3 se dit « environ 3 tours », comme le réglage
                    int n = (int)Math.Round(laps, MidpointRounding.AwayFromZero);
                    _say(level == 1
                        ? (fr ? "Attention carburant, il reste environ un tour." : "Fuel critical, about one lap left.")
                        : n == 1
                            ? (fr ? "Carburant : environ un tour restant." : "Fuel: about one lap left.")
                            : (fr ? $"Carburant : environ {n} tours restants." : $"Fuel: about {n} laps left."));
                    _fuelLevelAnnounced = level;
                }
            }
        }

        private static bool IsRace(string session) =>
            !string.IsNullOrEmpty(session) &&
            (session.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0 ||
             session.IndexOf("course", StringComparison.OrdinalIgnoreCase) >= 0);

        // Formulations vérifiées en passant la voix Kokoro dans Whisper :
        // FR « 2 minutes 18 secondes 9 » (« 2 18 9 » est lu « 219 »), EN « 2 18 9 » = « two eighteen nine »
        internal static string SpokenLap(TimeSpan t, bool fr)
        {
            int tenths = t.Milliseconds / 100;
            int minutes = (int)t.TotalMinutes;
            if (!fr)
                return minutes >= 1 ? $"{minutes} {t.Seconds:00} {tenths}" : $"{t.Seconds} {tenths}";
            var sec = $"{t.Seconds} secondes {tenths}";
            return minutes >= 1 ? $"{minutes} minute{(minutes > 1 ? "s" : "")} {sec}" : sec;
        }

        private static readonly string[] TenthsFr = { "", "un dixième", "deux dixièmes", "trois dixièmes", "quatre dixièmes", "cinq dixièmes", "six dixièmes", "sept dixièmes", "huit dixièmes", "neuf dixièmes" };
        private static readonly string[] TenthsEn = { "", "a tenth", "two tenths", "three tenths", "four tenths", "five tenths", "six tenths", "seven tenths", "eight tenths", "nine tenths" };

        internal static string SpokenDelta(double seconds, bool fr)
        {
            if (seconds < 0.05) return fr ? "moins d'un dixième" : "less than a tenth";
            if (seconds < 0.95)
            {
                int n = Math.Max(1, Math.Min(9, (int)Math.Round(seconds * 10)));
                return fr ? TenthsFr[n] : TenthsEn[n];
            }
            return fr
                ? seconds.ToString("0.0", CultureInfo.GetCultureInfo("fr-FR")) + " secondes"
                : seconds.ToString("0.0", CultureInfo.InvariantCulture) + " seconds";
        }

        private static string SpokenGap(Opponent o, bool fr)
        {
            if (o == null) return null;
            if (o.LapsToPlayer is int l && l != 0)
                return Math.Abs(l) == 1 ? (fr ? "un tour" : "a lap") : (fr ? $"{Math.Abs(l)} tours" : $"{Math.Abs(l)} laps");
            if (!(o.GaptoPlayer is double g)) return null;
            var c = fr ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.InvariantCulture;
            return Math.Abs(g).ToString("0.0", c);
        }

        internal static string SpokenGaps(Opponent ahead, Opponent behind, bool fr)
        {
            var a = SpokenGap(ahead, fr);
            var b = SpokenGap(behind, fr);
            if (a == null && b == null) return null;
            if (fr)
                return a != null && b != null ? $"Devant à {a}, derrière à {b}." : a != null ? $"Devant à {a}." : $"Derrière à {b}.";
            return a != null && b != null ? $"Gap ahead {a}, behind {b}." : a != null ? $"Gap ahead {a}." : $"Gap behind {b}.";
        }

        internal static string SpokenPosition(int pos, int gained, bool fr)
        {
            int n = Math.Abs(gained);
            if (fr)
            {
                var places = n == 1 ? "une place" : $"{n} places";
                return gained > 0 ? $"P{pos}, tu gagnes {places}." : $"P{pos}, tu perds {places}.";
            }
            var p = n == 1 ? "a place" : $"{n} places";
            return gained > 0 ? $"P{pos}, you gained {p}." : $"P{pos}, you lost {p}.";
        }
    }
}
