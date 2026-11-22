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
    /// </summary>
    public sealed class ClientRunner
    {
        public static string ClientDir => Path.Combine(EngineerSettings.DataDir, "client");
        public static string LogPath => Path.Combine(EngineerSettings.DataDir, "client.log");
        private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(60);

        private readonly object _sync = new object();
        private Process _process;
        private StreamWriter _log;
        private DateTime _startedAt;
        private string _lastLine;

        public string LastError { get; private set; }
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
                if (uv == null)
                {
                    Fail("uv introuvable (" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin") + "). Relance l'installateur.");
                    return;
                }

                try
                {
                    Directory.CreateDirectory(EngineerSettings.DataDir);
                    _log?.Dispose();
                    _log = new StreamWriter(LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                    _lastLine = null;

                    var psi = new ProcessStartInfo(uv, "run --no-progress engineer.py --managed")
                    {
                        WorkingDirectory = ClientDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };
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
                    SimHub.Logging.Current.Info($"[AI Race Engineer] client vocal lancé ({uv}, {ClientDir})");
                }
                catch (Exception ex)
                {
                    _process = null;
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
