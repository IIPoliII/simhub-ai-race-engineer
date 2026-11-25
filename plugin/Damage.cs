using System;
using System.Collections.Concurrent;
using System.Reflection;
using GameReaderCommon;

namespace AIRaceEngineer
{
    /// <summary>
    /// Détail des dégâts (aéro, moteur, suspension et freins par roue) lu dans les données brutes du jeu.
    /// SimHub ne donne qu'une moyenne normalisée : après un choc, 7 % de moyenne peuvent cacher une suspension
    /// tordue (volant plus droit). Seuls les jeux de la famille Project CARS (AMS2, PC2, PC1) exposent ces champs
    /// (mémoire partagée, valeurs de 0 à 1) ; pour les autres jeux, rien (null) plutôt qu'une donnée inventée.
    /// </summary>
    internal static class Damage
    {
        private sealed class Fields
        {
            public FieldInfo Aero, Engine, Suspension, Brakes;
            public bool Any => Aero != null || Engine != null || Suspension != null || Brakes != null;
        }

        private static readonly ConcurrentDictionary<Type, Fields> Cache = new ConcurrentDictionary<Type, Fields>();

        /// <summary>Données brutes du jeu : propriété RawData de StatusData&lt;T&gt;, que SimHub passe comme StatusDataBase.</summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo> RawProps = new ConcurrentDictionary<Type, PropertyInfo>();

        public static object Detail(StatusDataBase d)
        {
            if (d == null) return null;
            try
            {
                var raw = RawProps.GetOrAdd(d.GetType(), t => t.GetProperty("RawData", BindingFlags.Public | BindingFlags.Instance))?.GetValue(d);
                if (raw == null) return null;
                var f = Cache.GetOrAdd(raw.GetType(), t => new Fields
                {
                    Aero = Field(t, "mAeroDamage", typeof(float)),
                    Engine = Field(t, "mEngineDamage", typeof(float)),
                    Suspension = Field(t, "mSuspensionDamage", typeof(float[])),
                    Brakes = Field(t, "mBrakeDamage", typeof(float[])),
                });
                if (!f.Any) return null;
                return new
                {
                    aero_pct = f.Aero != null ? Pct((float)f.Aero.GetValue(raw)) : null,
                    engine_pct = f.Engine != null ? Pct((float)f.Engine.GetValue(raw)) : null,
                    suspension_pct = Wheels(f.Suspension?.GetValue(raw) as float[]),
                    brakes_pct = Wheels(f.Brakes?.GetValue(raw) as float[]),
                };
            }
            catch (Exception)
            {
                return null; // structure inattendue : la moyenne reste disponible
            }
        }

        private static FieldInfo Field(Type t, string name, Type type)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null && f.FieldType == type ? f : null;
        }

        /// <summary>Ordre pCars : avant gauche, avant droite, arrière gauche, arrière droite.</summary>
        private static object Wheels(float[] v)
        {
            if (v == null || v.Length < 4) return null;
            return new { fl = Pct(v[0]), fr = Pct(v[1]), rl = Pct(v[2]), rr = Pct(v[3]) };
        }

        private static double? Pct(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return null;
            return Math.Round(Math.Max(0, Math.Min(1, (double)v)) * 100, 0);
        }
    }
}
