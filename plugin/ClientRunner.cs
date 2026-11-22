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

        public string LastError { get; private set; }
        /// <summary>Étape de la première mise en route en cours, sinon null.</summary>
        public string SetupStep { get; private set; }
        /// <summary>Dernière ligne de la mise en route (ex. « Téléchargement de la voix : 40 % »).</summary>
        public string SetupDetail => SetupStep != null ? _lastLine : null;
        /// <summary>Écrit dans le .venv par « engineer.py --prepare » quand Python et les modèles sont prêts :
        /// une mise en route interrompue reprend au démarrage suivant.</summary>
        private static string PreparedMarker => Path.Combine(ClientDir, ".venv", "air-prepared");
        /// <summary>Arrêts juste après le démarrage, d'affilée : au-delà de 3 on ne relance plus tout seul.</summary>
        public int QuickExits { get; private set; }
        public bool Installed => File.Exists(Path.Combine(ClientDir, "engineer.py"));
        public bool Running { get { var p = _process; return p != null && !p.HasExited; } }

        public static string FindUv()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(home, ".local", "bin", "uv.exe"),
                Path.Combine(home, ".cargo", "bin", "uv.exe"),
            }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(';').Where(p => p.Trim().Length > 0).Select(p => Path.Combine(p.Trim(), "uv.exe")));
            return candidates.FirstOrDefault(File.Exists);
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
                    Fail($"Client vocal introuvable ({ClientDir}). Lance l'installateur.");
                    return;
                }
                var uv = FindUv();
                ProcessStartInfo psi;
                if (uv == null)
                {
                    // uv (le gestionnaire Python d'Astral) installe ensuite Python lui-même, sans droits admin
                    _phase = Phase.InstallUv;
                    SetupStep = "Première mise en route (1/2) : installation de uv, le gestionnaire Python";
                    psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"irm https://astral.sh/uv/install.ps1 | iex\"");
                }
                else if (!File.Exists(PreparedMarker))
                {
                    _phase = Phase.Prepare;
                    SetupStep = "Première mise en route (2/2) : Python, dépendances et modèles de voix (~1 Go, quelques minutes)";
                    psi = new ProcessStartInfo(uv, "run --no-progress engineer.py --prepare");
                }
                else
                {
                    _phase = Phase.Client;
                    SetupStep = null;
                    psi = new ProcessStartInfo(uv, "run --no-progress engineer.py --managed");
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

                    var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    process.OutputDataReceived += (s, e) => Log(e.Data);
                    process.ErrorDataReceived += (s, e) => Log(e.Data);
                    process.Exited += (s, e) => OnExited(process);
                    _process = process;
                    _startedAt = DateTime.UtcNow;
                    LastError = null;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    SimHub.Logging.Current.Info($"[AI Race Engineer] {(_phase == Phase.Client ? "client vocal lancé" : "mise en route : " + _phase)} ({psi.FileName}, {ClientDir})");
                }
                catch (Exception ex)
                {
                    _process = null;
                    SetupStep = null;
                    Fail("Impossible de lancer le client : " + ex.Message);
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
                    SetupStep = null;
                    if (code == 0)
                    {
                        // Étape réussie : on enchaîne (étape suivante, puis le client)
                        _appendLog = true;
                        Start();
                        return;
                    }
                    QuickExits++;
                    Fail(step == Phase.InstallUv
                        ? $"Installation de uv impossible (code {code}). Vérifie la connexion internet puis clique Démarrer."
                        : $"Mise en route du client vocal impossible (code {code})" + (_lastLine != null ? " : " + _lastLine : ". Voir le journal."));
                    return;
                }
                QuickExits = DateTime.UtcNow - _startedAt < QuickExit ? QuickExits + 1 : 0;
                Fail($"Le client vocal s'est arrêté (code {code})" + (_lastLine != null ? " : " + _lastLine : "."));
            }
        }

        private void Fail(string message)
        {
            if (message != LastError) SimHub.Logging.Current.Warn("[AI Race Engineer] " + message);
            LastError = message;
        }

        public void Stop()
        {
            Process p;
            lock (_sync)
            {
                p = _process;
                _process = null;
                SetupStep = null;
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
