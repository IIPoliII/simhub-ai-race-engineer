using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AIRaceEngineer
{
    /// <summary>
    /// Vérifie la dernière release GitHub et installe la nouvelle DLL.
    /// La DLL chargée par SimHub ne peut pas être écrasée, mais elle peut être renommée :
    /// on la renomme en .old, on copie la nouvelle, et elle est utilisée au prochain démarrage.
    /// </summary>
    public sealed class Updater
    {
        public const string Repo = "IIPoliII/simhub-ai-race-engineer";
        public const string AssetName = "AIRaceEngineer.dll";

        public Version Current { get; } = Trim(typeof(Updater).Assembly.GetName().Version);
        public Version Latest { get; private set; }
        public string State { get; private set; } = "";
        public bool Busy { get; private set; }
        public bool Installed { get; private set; }
        public bool UpdateAvailable => !Installed && Latest != null && Latest > Current;

        private string _downloadUrl;

        public void CheckAsync()
        {
            if (Busy) return;
            Task.Run(() => Run(Check));
        }

        public void InstallAsync()
        {
            if (Busy || !UpdateAvailable) return;
            Task.Run(() => Run(Install));
        }

        private void Run(Action action)
        {
            Busy = true;
            try { action(); }
            catch (Exception ex) { State = "Erreur : " + ex.Message; }
            finally { Busy = false; }
        }

        private static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "AIRaceEngineer-SimHubPlugin";
            return wc;
        }

        private void Check()
        {
            State = "Recherche de mises à jour...";
            string json;
            try
            {
                using (var wc = Client())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString($"https://api.github.com/repos/{Repo}/releases/latest");
                }
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                State = "Aucune version publiée pour l'instant.";
                return;
            }

            var release = JObject.Parse(json);
            var tag = ((string)release["tag_name"] ?? "").TrimStart('v', 'V');
            var asset = (release["assets"] as JArray)?.FirstOrDefault(a => (string)a["name"] == AssetName);
            if (!Version.TryParse(tag, out var latest) || asset == null)
            {
                State = "Release GitHub illisible (tag ou DLL manquante).";
                return;
            }

            Latest = Trim(latest);
            _downloadUrl = (string)asset["browser_download_url"];
            State = UpdateAvailable ? $"Version {Latest} disponible." : "Tu as la dernière version.";
        }

        private void Install()
        {
            State = $"Téléchargement de la version {Latest}...";
            var dir = Path.Combine(Path.GetTempPath(), "AIRaceEngineer-update");
            Directory.CreateDirectory(dir);
            var downloaded = Path.Combine(dir, AssetName);
            using (var wc = Client())
                wc.DownloadFile(_downloadUrl, downloaded);

            // Vérifie que c'est bien notre plugin, dans la version annoncée
            var name = AssemblyName.GetAssemblyName(downloaded);
            if (name.Name != "AIRaceEngineer" || Trim(name.Version) != Latest)
                throw new InvalidOperationException($"fichier téléchargé inattendu ({name.Name} {name.Version}).");

            var target = typeof(Updater).Assembly.Location;
            State = "Installation : accepte la demande d'autorisation Windows...";

            string Q(string s) => "'" + s.Replace("'", "''") + "'";
            var script =
                "$ErrorActionPreference = 'Stop'; " +
                $"$t = {Q(target)}; " +
                "Remove-Item \"$t.old\" -Force -ErrorAction SilentlyContinue; " +
                "Move-Item $t \"$t.old\" -Force; " +
                $"Copy-Item {Q(downloaded)} $t -Force";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                UseShellExecute = true,
                Verb = "runas", // Program Files demande les droits admin
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process process;
            try { process = Process.Start(psi); }
            catch (Win32Exception) { State = "Mise à jour annulée."; return; }

            process.WaitForExit();
            if (process.ExitCode != 0 || Trim(AssemblyName.GetAssemblyName(target).Version) != Latest)
                throw new InvalidOperationException("la copie a échoué. Ferme SimHub et relance l'installateur.");

            Installed = true;
            State = $"Version {Latest} installée. Redémarre SimHub pour l'utiliser.";
        }

        private static Version Trim(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
