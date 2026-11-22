using System;
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
    /// </summary>
    internal sealed class SnapshotServer
    {
        private readonly int _port;
        private readonly Func<string> _getJson;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public SnapshotServer(int port, Func<string> getJson)
        {
            _port = port;
            _getJson = getJson;
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
                        Handle(client.GetStream());
                    }
                }
                catch (SocketException) when (!_running) { }
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
            string line;
            while (!string.IsNullOrEmpty(line = reader.ReadLine())) { } // ignore les en-têtes

            var parts = requestLine.Split(' ');
            var path = parts.Length > 1 ? parts[1] : "/";

            string status = "200 OK";
            string body;
            if (path == "/" || path.StartsWith("/snapshot"))
                body = _getJson();
            else
            {
                status = "404 Not Found";
                body = "{\"error\":\"not found\"}";
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
    }
}
