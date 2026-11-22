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
    ///
    /// SimHub est dans Program Files : la première installation demande les droits admin, et en profite pour
    /// donner à l'utilisateur le droit d'écrire sur ce seul fichier (AIRaceEngineer.dll). Ensuite, les mises à jour
    /// se font sans admin : la nouvelle DLL est préparée dans le profil, puis recopiée par-dessus l'ancienne
    /// quand SimHub se ferme (tant qu'il tourne, la DLL chargée est verrouillée).
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
        /// <summary>La nouvelle DLL est prête dans le profil : elle sera copiée à la fermeture de SimHub.</summary>
        public bool Staged { get; private set; }
        public static string StagedDll => Path.Combine(EngineerSettings.DataDir, "update", AssetName);
        private bool _stagedFailed;
        public bool UpdateAvailable => !Installed && Latest != null && Latest > Current;
        /// <summary>Déclenché quand la nouvelle version est copiée : il reste à redémarrer SimHub.</summary>
        public event Action InstallFinished;

        public const string ClientAssetName = "client.zip";

        private string _downloadUrl, _clientUrl;

        /// <summary>
        /// Au démarrage : une DLL préparée encore là et plus récente que la version chargée veut dire que la copie
        /// à la fermeture n'a pas eu lieu (SimHub tué, fichier verrouillé). La prochaine mise à jour passera par l'admin.
        /// </summary>
        public void CheckStaged()
        {
            if (!File.Exists(StagedDll)) return;
            try { _stagedFailed = Trim(AssemblyName.GetAssemblyName(StagedDll).Version) > Current; } catch { _stagedFailed = true; }
            try { File.Delete(StagedDll); } catch { }
        }

        /// <summary>
        /// L'utilisateur a-t-il le droit d'écrire sur la DLL ? SimHub la verrouille pendant qu'il tourne : un refus pour
        /// verrou (IOException) veut dire que le droit est là, un refus d'accès (UnauthorizedAccessException) qu'il manque.
        /// </summary>
        internal static bool CanWriteWithoutAdmin(string path)
        {
            try { using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { } return true; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return true; }
        }

        /// <summary>
        /// Script PowerShell, sans admin : attend la fermeture de SimHub, recopie la DLL préparée par-dessus l'ancienne
        /// (en gardant l'ancienne en mémoire pour la remettre si la copie échoue), puis relance SimHub si demandé.
        /// </summary>
        public static string ExitScript(int pid, string simhubExe, bool restart)
        {
            string Q(string v) => "'" + v.Replace("'", "''") + "'";
            var target = typeof(Updater).Assembly.Location;
            return
                $"$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue; if ($p) {{ $p.WaitForExit(120000) | Out-Null }}; " +
                $"$s = {Q(StagedDll)}; $t = {Q(target)}; " +
                "if (Test-Path $s) { for ($i = 0; $i -lt 20; $i++) { try { " +
                "$new = [IO.File]::ReadAllBytes($s); $fs = [IO.File]::Open($t, 'Open', 'ReadWrite', 'None'); " +
                "try { $old = New-Object byte[] $fs.Length; [void]$fs.Read($old, 0, $old.Length); $fs.SetLength(0); $fs.Write($new, 0, $new.Length) } " +
                "catch { $fs.SetLength(0); $fs.Write($old, 0, $old.Length); throw } finally { $fs.Close() }; " +
                "Remove-Item $s -Force; break } catch { Start-Sleep -Milliseconds 500 } } }; " +
                (restart ? $"Start-Process -FilePath {Q(simhubExe)} -WorkingDirectory {Q(Path.GetDirectoryName(simhubExe))}" : "");
        }

        public void CheckAsync()
        {
            if (Busy) return;
            Task.Run(() => Run(Check));
        }

        public void InstallAsync(ClientRunner client)
        {
            if (Busy || !UpdateAvailable) return;
            Task.Run(() => Run(() => Install(client)));
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
            _clientUrl = (string)(release["assets"] as JArray)?.FirstOrDefault(a => (string)a["name"] == ClientAssetName)?["browser_download_url"];
            State = UpdateAvailable ? $"Version {Latest} disponible." : "Tu as la dernière version.";
        }

        private void Install(ClientRunner client)
        {
            State = $"Téléchargement de la version {Latest}...";
            var dir = Path.Combine(Path.GetTempPath(), "AIRaceEngineer-update");
            Directory.CreateDirectory(dir);
            var downloaded = Path.Combine(dir, AssetName);
            var clientZip = Path.Combine(dir, ClientAssetName);
            using (var wc = Client())
            {
                wc.DownloadFile(_downloadUrl, downloaded);
                if (_clientUrl != null) wc.DownloadFile(_clientUrl, clientZip);
            }

            // Vérifie que c'est bien notre plugin, dans la version annoncée
            var name = AssemblyName.GetAssemblyName(downloaded);
            if (name.Name != "AIRaceEngineer" || Trim(name.Version) != Latest)
                throw new InvalidOperationException($"fichier téléchargé inattendu ({name.Name} {name.Version}).");

            var target = typeof(Updater).Assembly.Location;

            if (!_stagedFailed && CanWriteWithoutAdmin(target))
            {
                // Sans admin : la DLL attend dans le profil et sera copiée à la fermeture de SimHub
                Directory.CreateDirectory(Path.GetDirectoryName(StagedDll));
                File.Copy(downloaded, StagedDll, true);
                UpdateClient(client, clientZip);
                Staged = Installed = true;
                State = $"Version {Latest} prête : elle s'installe au redémarrage de SimHub.";
                InstallFinished?.Invoke();
                return;
            }

            State = "Installation : accepte la demande d'autorisation Windows (une seule fois)...";
            // Droit d'écriture sur ce seul fichier pour l'utilisateur : les prochaines mises à jour se passeront de l'admin.
            // Le SID est lu ici, hors admin : si l'admin est un autre compte, c'est quand même l'utilisateur qui reçoit le droit.
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;

            string Q(string s) => "'" + s.Replace("'", "''") + "'";
            var script =
                "$ErrorActionPreference = 'Stop'; " +
                $"$t = {Q(target)}; " +
                "Remove-Item \"$t.old\" -Force -ErrorAction SilentlyContinue; " +
                "Move-Item $t \"$t.old\" -Force; " +
                $"Copy-Item {Q(downloaded)} $t -Force; " +
                (sid == null ? "" : $"& icacls.exe $t /grant {Q("*" + sid + ":(M)")} | Out-Null");
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

            UpdateClient(client, clientZip);
            _stagedFailed = false;
            Installed = true;
            State = $"Version {Latest} installée. Redémarre SimHub pour l'utiliser.";
            InstallFinished?.Invoke();
        }

        /// <summary>Le client vocal est dans le profil utilisateur : pas besoin de droits admin.</summary>
        private void UpdateClient(ClientRunner client, string clientZip)
        {
            if (_clientUrl == null) return;
            State = "Mise à jour du client vocal...";
            client.Stop();
            ExtractOver(clientZip, ClientRunner.ClientDir);
        }

        /// <summary>Extrait l'archive par-dessus le dossier existant (garde .venv et les modèles téléchargés).</summary>
        internal static void ExtractOver(string zipPath, string targetDir)
        {
            var root = Path.GetFullPath(targetDir).TrimEnd('\\') + "\\";
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    var dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip slip
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, dest, true);
                }
            }
        }

        private static Version Trim(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
