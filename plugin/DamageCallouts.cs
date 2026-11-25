using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using GameReaderCommon;

namespace AIRaceEngineer
{
    /// <summary>
    /// État de la voiture à un instant : dégâts et pneus crevés.
    /// SimHub donne à tous les jeux des dégâts de carrosserie (0 à 100) ; Automobilista 2 et Project CARS 2 en disent
    /// plus dans leurs données brutes : suspension par roue, moteur, aéro, pneu crevé ou roue arrachée (0 à 1).
    /// Les autres jeux : une crevaison se voit à un pneu bien moins gonflé que les autres.
    /// </summary>
    internal sealed class DamageState
    {
        public double Body = double.NaN;                         // carrosserie, 0 à 100 (tous les jeux)
        public double Aero = double.NaN, Engine = double.NaN;    // 0 à 1, null si le jeu ne les donne pas
        public double[] Suspension;                              // fl, fr, rl, rr, 0 à 1
        public bool[] Flat = new bool[4];                        // pneu crevé
        public bool[] Detached = new bool[4];                    // roue arrachée
        public bool FlatFromGame;                                // crevaison donnée par le jeu (sinon déduite des pressions)

        public bool Detailed => !double.IsNaN(Aero) || !double.IsNaN(Engine) || Suspension != null;

        public static DamageState Read(StatusDataBase d)
        {
            var s = new DamageState { Body = d.CarDamagesMax };
            object raw = null;
            try { raw = d.GetRawDataObject(); } catch { /* jeu sans données brutes */ }
            var r = Raw.For(raw?.GetType());
            if (r != null)
            {
                object root = r.Owner == null ? raw : r.Owner(raw);
                if (root != null)
                {
                    s.Aero = Num(r.Aero?.Invoke(root));
                    s.Engine = Num(r.Engine?.Invoke(root));
                    if (r.Suspension?.Invoke(root) is float[] susp && susp.Length >= 4)
                        s.Suspension = susp.Take(4).Select(v => (double)v).ToArray();
                    // Tout à zéro : données pas encore remplies (menu, chargement), pas quatre roues perdues
                    if (r.TyreFlags?.Invoke(root) is uint[] flags && flags.Length >= 4 && flags.Take(4).Any(f => f != 0))
                    {
                        s.FlatFromGame = true;
                        for (int i = 0; i < 4; i++)
                        {
                            bool attached = (flags[i] & 1) != 0, inflated = (flags[i] & 2) != 0;
                            s.Detached[i] = !attached;
                            s.Flat[i] = attached && !inflated;
                        }
                    }
                }
            }
            if (!s.FlatFromGame && d.IsInPitLane == 0 && d.SpeedKmh > 30)
            {
                // Pneu à moins de 60 % du plus gonflé des trois autres : écart de pression impossible sans crevaison
                var p = new[] { d.TyrePressureFrontLeft, d.TyrePressureFrontRight, d.TyrePressureRearLeft, d.TyrePressureRearRight };
                if (p.All(v => v > 0 && !double.IsNaN(v)))
                    for (int i = 0; i < 4; i++)
                        s.Flat[i] = p[i] < 0.6 * p.Where((_, j) => j != i).Max();
            }
            return s;
        }

        private static double Num(object v) => v is float f ? f : v is double x ? x : double.NaN;

        /// <summary>Accès aux champs des données brutes, trouvés une fois par type (par leur nom, sans dépendre du lecteur du jeu).</summary>
        private sealed class Raw
        {
            public Func<object, object> Owner, Aero, Engine, Suspension, TyreFlags;
            private static readonly ConcurrentDictionary<Type, Raw> Cache = new ConcurrentDictionary<Type, Raw>();

            public static Raw For(Type t)
            {
                if (t == null) return null;
                return Cache.GetOrAdd(t, type =>
                {
                    var direct = Build(type);
                    if (direct != null) return direct;
                    // Les données peuvent être un niveau plus bas (enveloppe autour de la structure du jeu)
                    foreach (var m in Members(type))
                    {
                        var inner = Build(m.Type);
                        if (inner == null) continue;
                        inner.Owner = m.Get;
                        return inner;
                    }
                    return null;
                });
            }

            private static Raw Build(Type t)
            {
                if (t == null || t.IsPrimitive || t == typeof(string)) return null;
                var members = Members(t).ToDictionary(m => m.Name, m => m.Get);
                Func<object, object> Get(string name) => members.TryGetValue(name, out var g) ? g : null;
                var r = new Raw { Aero = Get("mAeroDamage"), Engine = Get("mEngineDamage"), Suspension = Get("mSuspensionDamage"), TyreFlags = Get("mTyreFlags") };
                return r.Aero == null && r.Engine == null && r.Suspension == null && r.TyreFlags == null ? null : r;
            }

            private static System.Collections.Generic.IEnumerable<(string Name, Type Type, Func<object, object> Get)> Members(Type t)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    yield return (f.Name, f.FieldType, o => f.GetValue(o));
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.GetIndexParameters().Length == 0 && p.CanRead)
                        yield return (p.Name, p.PropertyType, o => p.GetValue(o));
            }
        }
    }

    /// <summary>Annonces de dégâts : une par choc, une fois la voiture stabilisée, et rien pour ce qu'on savait déjà.</summary>
    internal sealed class DamageCallouts
    {
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);      // le choc est fini : plus de hausse depuis 1,5 s
        private static readonly TimeSpan FlatConfirm = TimeSpan.FromSeconds(3);   // crevaison déduite des pressions : tenue 3 s

        private Level _body, _aero, _engine;
        private Level[] _susp;
        private bool[] _flatSaid, _detachedSaid;
        private DateTime?[] _flatSince;
        private bool _started;

        public DamageCallouts() => Reset();

        public void Reset()
        {
            _body = new Level(5, 30); _aero = new Level(0.1, 0.4); _engine = new Level(0.05, 0.25);
            _susp = Enumerable.Range(0, 4).Select(_ => new Level(0.05, 0.25)).ToArray();
            _flatSaid = new bool[4]; _detachedSaid = new bool[4];
            _flatSince = new DateTime?[4];
            _started = false;
        }

        /// <returns>la phrase à dire, ou null</returns>
        public string Update(DamageState s, bool fr, DateTime now)
        {
            var parts = new System.Collections.Generic.List<string>();
            bool baseline = !_started;   // première lecture : ce qui est déjà là n'est pas une nouvelle
            _started = true;

            for (int i = 0; i < 4; i++)
            {
                if (s.Detached[i])
                {
                    if (!_detachedSaid[i] && !baseline) parts.Add(fr ? $"Roue {Corner(i, fr)} perdue !" : $"{Cap(Corner(i, fr))} wheel gone!");
                    _detachedSaid[i] = true;
                }
                else _detachedSaid[i] = false;

                if (s.Flat[i])
                {
                    _flatSince[i] = _flatSince[i] ?? now;
                    bool confirmed = s.FlatFromGame || now - _flatSince[i].Value >= FlatConfirm;
                    if (confirmed && !_flatSaid[i])
                    {
                        if (!baseline) parts.Add(fr ? $"Crevaison {Corner(i, fr)} !" : $"Puncture, {Corner(i, fr)}!");
                        _flatSaid[i] = true;
                    }
                }
                else { _flatSince[i] = null; _flatSaid[i] = false; }
            }

            if (s.Suspension != null)
                for (int i = 0; i < 4; i++)
                    if (_susp[i].Step(s.Suspension[i], now, baseline) is bool heavy && !_detachedSaid[i])
                        parts.Add(fr ? $"Suspension {Corner(i, fr)} {(heavy ? "très touchée" : "touchée")}."
                                     : $"{Cap(Corner(i, fr))} suspension {(heavy ? "badly damaged" : "damaged")}.");

            if (_engine.Step(s.Engine, now, baseline) is bool eh)
                parts.Add(fr ? (eh ? "Moteur très touché." : "Moteur touché.") : (eh ? "Engine badly damaged." : "Engine damaged."));

            // Aéro quand le jeu le donne, sinon la carrosserie générale de SimHub (les deux diraient le même choc)
            if (!double.IsNaN(s.Aero))
            {
                if (_aero.Step(s.Aero, now, baseline) is bool ah)
                    parts.Add(fr ? (ah ? "Gros dégâts aéro." : "Dégâts aéro.") : (ah ? "Heavy aero damage." : "Aero damage."));
            }
            else if (_body.Step(s.Body, now, baseline) is bool bh)
                parts.Add(fr ? (bh ? "Gros dégâts sur la voiture." : "Dégâts sur la voiture.") : (bh ? "Heavy car damage." : "Car damage."));

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        internal static string Corner(int i, bool fr) => fr
            ? new[] { "avant gauche", "avant droite", "arrière gauche", "arrière droite" }[i]
            : new[] { "front left", "front right", "rear left", "rear right" }[i];

        private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Un niveau de dégâts : annoncé quand il a monté d'au moins un cran et ne bouge plus.</summary>
        private sealed class Level
        {
            private readonly double _step, _heavy;
            private double _said = double.NaN, _last = double.NaN;
            private DateTime _rose;

            public Level(double step, double heavy) { _step = step; _heavy = heavy; }

            /// <returns>null : rien à dire ; sinon true si les dégâts sont lourds</returns>
            public bool? Step(double v, DateTime now, bool baseline)
            {
                if (double.IsNaN(v) || v < 0) return null;
                if (baseline || double.IsNaN(_said)) { _said = _last = v; return null; }
                if (v < _said) _said = v;               // réparé au stand
                if (v > _last + 1e-6) _rose = now;
                _last = v;
                if (v - _said < _step || now - _rose < Settle) return null;
                _said = v;
                return v >= _heavy;
            }
        }
    }
}
