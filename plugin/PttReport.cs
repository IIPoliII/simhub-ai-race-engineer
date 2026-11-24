using System;
using System.Collections.Generic;
using System.Globalization;

namespace AIRaceEngineer
{
    /// <summary>
    /// Dernier appui sur le push-to-talk : sa durée vue par SimHub, puis ce que le client vocal a enregistré
    /// et compris. La page s'en sert pour tester le bouton et le micro avant d'aller en piste.
    /// Jamais modifié une fois publié : chaque nouvelle étape en fait une copie.
    /// </summary>
    public sealed class PttReport
    {
        /// <summary>Plus court que ça, l'appui et le relâchement arrivent ensemble : mapping en ShortPress.</summary>
        public const int PulseMs = 40;

        public DateTime At;         // relâchement du bouton (UTC)
        public int PressMs = -1;    // durée de l'appui vue par SimHub (-1 : inconnue), ou de la radio ouverte
        public bool Toggle;         // mode appuyer / réappuyer : la durée n'est pas celle d'un appui
        public string Kind;         // null : rien du client ; "short" : trop court ; "rec" : enregistré ; "heard" : transcrit
        public double Seconds;      // durée enregistrée par le client
        public int Peak = -1;       // niveau maximal du micro, en %
        public string Device;       // micro utilisé
        public string Text;         // ce que Whisper a compris (Kind == "heard")

        public bool Pulse => !Toggle && PressMs >= 0 && PressMs < PulseMs;

        /// <summary>Nouvelle étape envoyée par le client sur /client-mic.</summary>
        internal static PttReport From(PttReport last, Dictionary<string, string> q)
        {
            var r = last == null ? new PttReport { At = DateTime.UtcNow } : (PttReport)last.MemberwiseClone();
            r.Kind = q.TryGetValue("k", out var k) ? k : null;
            if (r.Kind == "heard")
                r.Text = Clip(q.TryGetValue("t", out var t) ? t : "", 200);
            else
            {
                r.Seconds = q.TryGetValue("s", out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
                r.Peak = q.TryGetValue("p", out var p) && int.TryParse(p, out var i) ? Math.Max(0, Math.Min(100, i)) : -1;
                r.Device = q.TryGetValue("dev", out var dev) && dev.Length > 0 ? Clip(dev, 80) : null;
                r.Text = null;
            }
            return r;
        }

        private static string Clip(string s, int max) => s.Length > max ? s.Substring(0, max) : s;
    }
}
