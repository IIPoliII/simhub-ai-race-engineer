using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AIRaceEngineer
{
    /// <summary>
    /// Mini serveur HTTP sur 127.0.0.1 uniquement. TcpListener plutôt que HttpListener :
    /// aucun droit admin ni réservation d'URL nécessaire.
    /// Le routeur reçoit (chemin, paramètres) et renvoie du JSON, ou null pour un 404.
    /// </summary>
    internal sealed class SnapshotServer
    {
        private readonly int _port;
        private readonly Func<string, Dictionary<string, string>, string> _route;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public SnapshotServer(int port, Func<string, Dictionary<string, string>, string> route)
        {
            _port = port;
            _route = route;
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AIRaceEngineer.Http" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    using (var client = _listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 1000;
                        client.SendTimeout = 1000;
                        client.NoDelay = true;
                        Handle(client.GetStream());
                    }
                }
                catch (SocketException) when (!_running) { }
                catch (ObjectDisposedException) when (!_running) { }
                catch (Exception ex)
                {
                    if (_running) SimHub.Logging.Current.Warn("[AI Race Engineer] http: " + ex.Message);
                }
            }
        }

        private void Handle(NetworkStream stream)
        {
            var reader = new StreamReader(stream, Encoding.ASCII);
            var requestLine = reader.ReadLine() ?? "";
            string line, host = null;
            bool hasOrigin = false;
            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line.Substring(0, colon).Trim();
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) host = line.Substring(colon + 1).Trim();
                else if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) hasOrigin = true;
            }

            var parts = requestLine.Split(' ');
            var target = parts.Length > 1 ? parts[1] : "/";
            int q = target.IndexOf('?');
            var path = q >= 0 ? target.Substring(0, q) : target;
            var query = ParseQuery(q >= 0 ? target.Substring(q + 1) : "");

            string status = "200 OK";
            string body;
            // Anti DNS rebinding : seuls les appels locaux directs sont acceptés,
            // pas les requêtes JavaScript venant d'une page web (qui envoient Origin).
            bool localHost = host == $"127.0.0.1:{_port}" || host == $"localhost:{_port}";
            if (!localHost || hasOrigin)
            {
                status = "403 Forbidden";
                body = "{\"error\":\"forbidden\"}";
            }
            else
            {
                body = _route(path == "/" ? "/snapshot" : path, query);
                if (body == null)
                {
                    status = "404 Not Found";
                    body = "{\"error\":\"not found\"}";
                }
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {bytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((eq >= 0 ? pair.Substring(0, eq) : pair).Replace('+', ' '));
                var value = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')) : "";
                result[key] = value;
            }
            return result;
        }
    }
}
