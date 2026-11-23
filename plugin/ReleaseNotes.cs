using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AIRaceEngineer
{
    /// <summary>
    /// Notes de version lues dans CHANGELOG.md / CHANGELOG.fr.md, intégrés à la DLL :
    /// elles s'affichent dans SimHub sans internet, dans la langue de la page.
    /// </summary>
    internal static class ReleaseNotes
    {
        internal sealed class Group
        {
            public string Title;
            public List<string> Items = new List<string>();
        }

        internal sealed class Release
        {
            public Version Version;
            public string Date;
            public List<Group> Groups = new List<Group>();
        }

        private static readonly Regex VersionLine = new Regex(@"^##\s+\[(?<v>\d+\.\d+\.\d+)\](?:\s*-\s*(?<d>\S+))?");

        public static Version Current
        {
            get
            {
                var v = typeof(ReleaseNotes).Assembly.GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static List<Release> All(bool fr)
        {
            try
            {
                using (var s = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(fr ? "CHANGELOG-fr.md" : "CHANGELOG.md"))
                    return s == null ? new List<Release>() : Parse(new StreamReader(s).ReadToEnd());
            }
            catch { return new List<Release>(); }
        }

        /// <summary>Versions plus récentes que <paramref name="since"/> jusqu'à la version installée ;
        /// sans <paramref name="since"/>, la version installée seule.</summary>
        public static List<Release> Since(bool fr, Version since)
        {
            var current = Current;
            return All(fr).Where(r => r.Version <= current && (since == null ? r.Version == current : r.Version > since)).ToList();
        }

        internal static List<Release> Parse(string markdown)
        {
            var releases = new List<Release>();
            Release release = null;
            Group group = null;
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var m = VersionLine.Match(line);
                if (m.Success)
                {
                    release = new Release { Version = Version.Parse(m.Groups["v"].Value), Date = m.Groups["d"].Success ? m.Groups["d"].Value : null };
                    releases.Add(release);
                    group = null;
                }
                else if (release == null || line.StartsWith("[")) continue;   // en-tête, liens de comparaison
                else if (line.StartsWith("### "))
                {
                    group = new Group { Title = line.Substring(4).Trim() };
                    release.Groups.Add(group);
                }
                else if (line.StartsWith("- ") && group != null) group.Items.Add(line.Substring(2).Trim());
                else if (line.StartsWith("  ") && group != null && group.Items.Count > 0 && line.Trim().Length > 0)
                    group.Items[group.Items.Count - 1] += " " + line.Trim();  // suite d'une puce sur plusieurs lignes
            }
            return releases;
        }
    }
}
