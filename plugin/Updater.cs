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
    /// L'installateur de SimHub laisse d'habitude les utilisateurs écrire dans son dossier : la mise à jour se fait alors
    /// sans admin. La nouvelle DLL est préparée dans le profil, puis recopiée par-dessus l'ancienne quand SimHub se ferme
    /// (tant qu'il tourne, la DLL chargée est verrouillée), après vérification de son empreinte SHA-256.
    /// Si le dossier est protégé, on demande l'admin à chaque mise à jour, sans jamais assouplir ses droits.
    /// </summary>
    public sealed class Updater
    {
        public const string Repo = "IIPoliII/simhub-ai-race-engineer";
        public const string AssetName = "AIRaceEngineer.dll";

        public Version Current { get; } = Trim(typeof(Updater).Assembly.GetName().Version);
        public Version Latest { get; private set; }
        private Msg _state;
        public string State => _state?.ToString() ?? "";
        /// <summary>La dernière tentative a échoué ou a été annulée : State le dit.</summary>
        public bool Failed { get; private set; }
        public bool Busy { get; private set; }
        public bool Installed { get; private set; }
        /// <summary>La nouvelle DLL est prête dans le profil : elle sera copiée à la fermeture de SimHub.</summary>
        public bool Staged { get; private set; }
        public static string StagedDll => Path.Combine(EngineerSettings.DataDir, "update", AssetName);
        private bool _stagedFailed;
        /// <summary>Empreinte de la DLL préparée : le script de fermeture ne recopie qu'un fichier identique.</summary>
        private static string _stagedHash;
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

        private static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        /// <summary>
        /// Script PowerShell, sans admin : attend la fermeture de SimHub, vérifie que la DLL préparée est bien celle
        /// téléchargée et contrôlée (même empreinte), la recopie par-dessus l'ancienne (gardée en mémoire pour la remettre
        /// si la copie échoue), puis relance SimHub si demandé. Sans DLL préparée dans cette session, il ne copie rien.
        /// </summary>
        public static string ExitScript(int pid, string simhubExe, bool restart)
        {
            string Q(string v) => "'" + v.Replace("'", "''") + "'";
            var target = typeof(Updater).Assembly.Location;
            return
                $"$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue; if ($p) {{ $p.WaitForExit(120000) | Out-Null }}; " +
                $"$s = {Q(StagedDll)}; $t = {Q(target)}; $h = {Q(_stagedHash ?? "")}; " +
                "if ($h -and (Test-Path $s)) { for ($i = 0; $i -lt 20; $i++) { try { " +
                "$new = [IO.File]::ReadAllBytes($s); " +
                "$sha = [Security.Cryptography.SHA256]::Create(); if ([BitConverter]::ToString($sha.ComputeHash($new)).Replace('-', '') -ne $h) { Remove-Item $s -Force; break }; " +
                "$fs = [IO.File]::Open($t, 'Open', 'ReadWrite', 'None'); " +
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
            Failed = false;
            try { action(); }
            catch (Exception ex) { Failed = true; _state = new Msg("Erreur : " + ex.Message, "Error: " + ex.Message); }
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
            _state = new Msg("Recherche de mises à jour...", "Checking for updates...");
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
                _state = new Msg("Aucune version publiée pour l'instant.", "No version published yet.");
                return;
            }

            var release = JObject.Parse(json);
            var tag = ((string)release["tag_name"] ?? "").TrimStart('v', 'V');
            var asset = (release["assets"] as JArray)?.FirstOrDefault(a => (string)a["name"] == AssetName);
            if (!Version.TryParse(tag, out var latest) || asset == null)
            {
                _state = new Msg("Release GitHub illisible (tag ou DLL manquante).", "Unreadable GitHub release (tag or DLL missing).");
                return;
            }

            Latest = Trim(latest);
            _downloadUrl = (string)asset["browser_download_url"];
            _clientUrl = (string)(release["assets"] as JArray)?.FirstOrDefault(a => (string)a["name"] == ClientAssetName)?["browser_download_url"];
            _state = UpdateAvailable ? new Msg($"Version {Latest} disponible.", $"Version {Latest} available.") : new Msg("Tu as la dernière version.", "You have the latest version.");
        }

        private void Install(ClientRunner client)
        {
            _state = new Msg($"Téléchargement de la version {Latest}...", $"Downloading version {Latest}...");
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
                throw new InvalidOperationException(L10n.T($"fichier téléchargé inattendu ({name.Name} {name.Version}).", $"unexpected downloaded file ({name.Name} {name.Version})."));

            var target = typeof(Updater).Assembly.Location;

            if (!_stagedFailed && CanWriteWithoutAdmin(target))
            {
                // Sans admin : la DLL attend dans le profil et sera copiée à la fermeture de SimHub
                Directory.CreateDirectory(Path.GetDirectoryName(StagedDll));
                var bytes = File.ReadAllBytes(downloaded);
                File.WriteAllBytes(StagedDll, bytes);
                _stagedHash = Sha256(bytes);
                UpdateClient(client, clientZip);
                Staged = Installed = true;
                _state = new Msg($"Version {Latest} prête : elle s'installe au redémarrage de SimHub.", $"Version {Latest} ready: it installs when SimHub restarts.");
                InstallFinished?.Invoke();
                return;
            }

            _state = new Msg("Installation : accepte la demande d'autorisation Windows...", "Installing: accept the Windows permission prompt...");

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
            catch (Win32Exception) { Failed = true; _state = new Msg("Mise à jour annulée.", "Update cancelled."); return; }

            process.WaitForExit();
            if (process.ExitCode != 0 || Trim(AssemblyName.GetAssemblyName(target).Version) != Latest)
                throw new InvalidOperationException(L10n.T("la copie a échoué. Ferme SimHub et relance l'installateur.", "the copy failed. Close SimHub and run the installer again."));

            UpdateClient(client, clientZip);
            _stagedFailed = false;
            Installed = true;
            _state = new Msg($"Version {Latest} installée. Redémarre SimHub pour l'utiliser.", $"Version {Latest} installed. Restart SimHub to use it.");
            InstallFinished?.Invoke();
        }

        /// <summary>Le client vocal est dans le profil utilisateur : pas besoin de droits admin.</summary>
        private void UpdateClient(ClientRunner client, string clientZip)
        {
            if (_clientUrl == null) return;
            _state = new Msg("Mise à jour du client vocal...", "Updating the voice client...");
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
