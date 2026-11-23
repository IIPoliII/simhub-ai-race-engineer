using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AIRaceEngineer
{
    /// <summary>
    /// Lance le client vocal Python (installé par l'installateur dans %LOCALAPPDATA%\AIRaceEngineer\client)
    /// en arrière-plan, sans fenêtre, et écrit sa sortie dans client.log.
    /// Au premier démarrage, il le met d'abord en route tout seul : installe uv s'il manque, puis Python,
    /// les dépendances et les modèles de voix (étapes affichées dans la page du plugin).
    /// </summary>
    public sealed class ClientRunner
    {
        public static string ClientDir => Path.Combine(EngineerSettings.DataDir, "client");
        public static string LogPath => Path.Combine(EngineerSettings.DataDir, "client.log");
        private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(60);

        private enum Phase { Client, InstallUv, Prepare }

        private readonly object _sync = new object();
        private Process _process;
        private Phase _phase;
        private bool _appendLog;
        private StreamWriter _log;
        private DateTime _startedAt;
        private string _lastLine;

        private Msg _lastError, _setupStep;
        public string LastError => _lastError;
        /// <summary>Étape de la première mise en route en cours, sinon null.</summary>
        public string SetupStep => _setupStep;
        /// <summary>Dernière ligne de la mise en route (ex. « Téléchargement de la voix : 40 % »).</summary>
        public string SetupDetail => _setupStep != null ? _lastLine : null;
        /// <summary>Écrit dans le .venv par « engineer.py --prepare » quand Python et les modèles sont prêts :
        /// une mise en route interrompue reprend au démarrage suivant.</summary>
        private static string PreparedMarker => Path.Combine(ClientDir, ".venv", "air-prepared");
        /// <summary>Arrêts juste après le démarrage, d'affilée : au-delà de 3 on ne relance plus tout seul.</summary>
        public int QuickExits { get; private set; }
        public bool Installed => File.Exists(Path.Combine(ClientDir, "engineer.py"));
        public bool Running
        {
            get
            {
                var p = _process;
                if (p == null) return false;
                // Processus encore en train de démarrer : HasExited lèverait une exception, il compte comme lancé
                try { return !p.HasExited; } catch (InvalidOperationException) { return true; }
            }
        }

        /// <summary>Dossier où on installe uv, le premier que FindUv regarde.</summary>
        private static string UvDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

        public static string FindUv()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dirs = new[] { UvDir, Path.Combine(home, ".cargo", "bin") }
                .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Select(p => p.Trim().Trim('"').Trim()));
            var invalid = Path.GetInvalidPathChars();
            foreach (var dir in dirs)
            {
                // Entrée du PATH vide ou avec < > | : Path.Combine lèverait une exception
                if (dir.Length == 0 || dir.IndexOfAny(invalid) >= 0) continue;
                try { var f = Path.Combine(dir, "uv.exe"); if (File.Exists(f)) return f; } catch { }
            }
            return null;
        }

        /// <param name="manual">clic sur Démarrer : remet à zéro le compteur de plantages</param>
        public void Start(bool manual = false)
        {
            lock (_sync)
            {
                if (Running) return;
                if (manual) QuickExits = 0;

                if (!Installed)
                {
                    Fail(new Msg($"Client vocal introuvable ({ClientDir}). Lance l'installateur.", $"Voice client not found ({ClientDir}). Run the installer."));
                    return;
                }
                var uv = FindUv();
                // --frozen : les versions du uv.lock livré avec le client, sans nouvelle résolution.
                // Seulement s'il est là : les clients d'avant 0.5.0 (one-liner, dev) n'en ont pas.
                string frozen = File.Exists(Path.Combine(ClientDir, "uv.lock")) ? "--frozen " : "";
                ProcessStartInfo psi;
                if (uv == null)
                {
                    // uv (le gestionnaire Python d'Astral) installe ensuite Python lui-même, sans droits admin
                    _phase = Phase.InstallUv;
                    _setupStep = new Msg("Première mise en route (1/2) : installation de uv, le gestionnaire Python", "First-time setup (1/2): installing uv, the Python manager");
                    psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"irm https://astral.sh/uv/install.ps1 | iex\"");
                    // Là où FindUv le cherche (XDG_BIN_HOME, XDG_DATA_HOME ou HOMEDRIVE l'enverraient ailleurs)
                    psi.EnvironmentVariables["UV_INSTALL_DIR"] = UvDir;
                }
                else if (!File.Exists(PreparedMarker))
                {
                    _phase = Phase.Prepare;
                    _setupStep = new Msg("Première mise en route (2/2) : Python, dépendances et modèles de voix (~1 Go, quelques minutes)", "First-time setup (2/2): Python, dependencies and voice models (~1 GB, a few minutes)");
                    psi = new ProcessStartInfo(uv, "run " + frozen + "--no-progress engineer.py --prepare");
                }
                else
                {
                    _phase = Phase.Client;
                    _setupStep = null;
                    psi = new ProcessStartInfo(uv, "run " + frozen + "--no-progress engineer.py --managed");
                }

                try
                {
                    Directory.CreateDirectory(EngineerSettings.DataDir);
                    _log?.Dispose();
                    // Après une étape de mise en route, on garde sa sortie dans le même journal
                    // Partagé : SimHub peut recharger le plugin (nouvelle instance) avant d'avoir arrêté l'ancienne
                    var stream = new FileStream(LogPath, _appendLog ? FileMode.Append : FileMode.Create, FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                    _log = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    _appendLog = false;
                    _lastLine = null;

                    psi.WorkingDirectory = ClientDir;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                    psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                    // Langue des messages de la mise en route (ensuite, le client suit la langue des réglages)
                    psi.EnvironmentVariables["AIR_LANG"] = L10n.Language;
                    // Le .venv reste dans le dossier du client : c'est là que PreparedMarker le cherche
                    psi.EnvironmentVariables.Remove("UV_PROJECT_ENVIRONMENT");

                    var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    process.OutputDataReceived += (s, e) => Log(e.Data);
                    process.ErrorDataReceived += (s, e) => Log(e.Data);
                    process.Exited += (s, e) => OnExited(process);
                    _startedAt = DateTime.UtcNow;
                    _lastError = null;
                    process.Start();
                    // Publié une fois lancé seulement (la page lit Running sans verrou) ; OnExited attend _sync, il le verra
                    _process = process;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    SimHub.Logging.Current.Info($"[AI Race Engineer] {(_phase == Phase.Client ? "client vocal lancé" : "mise en route : " + _phase)} ({psi.FileName}, {ClientDir})");
                }
                catch (Exception ex)
                {
                    _process = null;
                    _setupStep = null;
                    Fail(new Msg("Impossible de lancer le client : " + ex.Message, "Cannot start the client: " + ex.Message));
                }
            }
        }

        private void OnExited(Process process)
        {
            try { process.WaitForExit(); } catch { } // laisse arriver les dernières lignes de sortie
            lock (_sync)
            {
                if (!ReferenceEquals(process, _process)) return; // arrêt volontaire
                _process = null;
                int code;
                try { code = process.ExitCode; } catch { code = -1; }
                if (_phase != Phase.Client)
                {
                    var step = _phase;
                    _setupStep = null;
                    // Étape réussie et son résultat bien là : on enchaîne (étape suivante, puis le client).
                    // Sinon on s'arrête, pour ne pas relancer la même étape en boucle.
                    if (code == 0 && (step == Phase.InstallUv ? FindUv() != null : File.Exists(PreparedMarker)))
                    {
                        _appendLog = true;
                        Start();
                        return;
                    }
                    QuickExits++;
                    if (code == 0)
                        Fail(step == Phase.InstallUv
                            ? new Msg("uv est installé, mais introuvable. Redémarre SimHub.", "uv was installed but can't be found. Restart SimHub.")
                            : new Msg("Mise en route terminée, mais le client vocal n'est pas prêt. Voir le journal.",
                                      "Setup finished, but the voice client isn't ready. See the log."));
                    else
                        Fail(step == Phase.InstallUv
                            ? new Msg($"Installation de uv impossible (code {code}). Vérifie la connexion internet puis clique Démarrer.",
                                      $"Could not install uv (code {code}). Check the internet connection, then click Start.")
                            // La consigne d'abord (la case n'affiche que deux lignes), le détail technique ensuite
                            : new Msg($"Mise en route interrompue (code {code}). Vérifie la connexion internet puis clique Démarrer." + (_lastLine != null ? " Détail : " + _lastLine : " Voir le journal."),
                                      $"Setup interrupted (code {code}). Check the internet connection, then click Start." + (_lastLine != null ? " Details: " + _lastLine : " See the log.")));
                    return;
                }
                QuickExits = DateTime.UtcNow - _startedAt < QuickExit ? QuickExits + 1 : 0;
                Fail(new Msg($"Le client vocal s'est arrêté (code {code})" + (_lastLine != null ? " : " + _lastLine : "."),
                             $"The voice client stopped (code {code})" + (_lastLine != null ? ": " + _lastLine : ".")));
            }
        }

        private void Fail(Msg message)
        {
            if (message.FrText != _lastError?.FrText) SimHub.Logging.Current.Warn("[AI Race Engineer] " + message.FrText);
            _lastError = message;
        }

        public void Stop()
        {
            Process p;
            lock (_sync)
            {
                p = _process;
                _process = null;
                _setupStep = null;
                try { _log?.Dispose(); } catch { }
                _log = null;
            }
            if (p == null || p.HasExited) return;
            try
            {
                // uv lance python en sous-processus : on arrête tout l'arbre
                using (var kill = Process.Start(new ProcessStartInfo("taskkill", $"/PID {p.Id} /T /F") { CreateNoWindow = true, UseShellExecute = false }))
                    kill?.WaitForExit(3000);
            }
            catch { try { p.Kill(); } catch { } }
        }

        public void Restart()
        {
            Stop();
            Start(manual: true);
        }

        private void Log(string line)
        {
            if (line == null) return;
            lock (_sync)
            {
                if (line.Trim().Length > 0) _lastLine = line.Trim().Length > 160 ? line.Trim().Substring(0, 160) + "..." : line.Trim();
                try { _log?.WriteLine(line); } catch { }
            }
        }
    }
}
