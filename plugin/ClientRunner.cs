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

        private Process _process;
        private StreamWriter _log;
        private readonly object _logLock = new object();

        public string LastError { get; private set; }
        public bool Installed => File.Exists(Path.Combine(ClientDir, "engineer.py"));
        public bool Running => _process != null && !_process.HasExited;

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

        public void Start()
        {
            if (Running) return;
            LastError = null;
            if (!Installed) { LastError = "Client vocal non installé : lance l'installateur."; return; }
            var uv = FindUv();
            if (uv == null) { LastError = "uv introuvable : relance l'installateur."; return; }

            try
            {
                Directory.CreateDirectory(EngineerSettings.DataDir);
                lock (_logLock)
                {
                    _log?.Dispose();
                    _log = new StreamWriter(LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                }

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

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.OutputDataReceived += (s, e) => Log(e.Data);
                _process.ErrorDataReceived += (s, e) => Log(e.Data);
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                LastError = "Impossible de lancer le client : " + ex.Message;
            }
        }

        public void Stop()
        {
            var p = _process;
            _process = null;
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
            Start();
        }

        private void Log(string line)
        {
            if (line == null) return;
            lock (_logLock)
            {
                try { _log?.WriteLine(line); } catch { }
            }
        }
    }
}
