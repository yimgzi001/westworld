#if WINDOWS
using System.Net;

namespace Game.Westworld {
    // The HTTP worker reads immutable bytes. Only the game thread reads the world.
    sealed class WorldPanelServer {
        const int MaxSnapshotBytes = 8 * 1024 * 1024;
        readonly HttpListener m_listener = new();
        readonly byte[] m_page = ReadResource("index.html");
        readonly byte[] m_script = ReadResource("panel.js");
        byte[] m_snapshot = "{\"state\":\"noWorld\"}"u8.ToArray();

        internal WorldPanelServer(int port) {
            m_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try {
                m_listener.Start();
            }
            catch {
                m_listener.Close();
                throw;
            }
            _ = Serve();
        }

        static byte[] ReadResource(string file) {
            using Stream source = typeof(WorldPanelServer).Assembly.GetManifestResourceStream($"Westworld.Panel.{file}")
                ?? throw new InvalidOperationException("Missing world panel asset.");
            using MemoryStream buffer = new();
            source.CopyTo(buffer);
            return buffer.ToArray();
        }

        internal void Publish(byte[] snapshot) {
            Volatile.Write(ref m_snapshot, snapshot.Length <= MaxSnapshotBytes
                ? snapshot : "{\"state\":\"snapshotTooLarge\"}"u8.ToArray());
        }

        async Task Serve() {
            while (m_listener.IsListening) {
                HttpListenerContext context;
                try {
                    context = await m_listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) {
                    break;
                }
                using HttpListenerResponse response = context.Response;
                try {
                    await Respond(context.Request, response).ConfigureAwait(false);
                }
                catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException or OperationCanceledException) {
                    response.Abort();
                }
            }
        }

        async Task Respond(HttpListenerRequest request, HttpListenerResponse response) {
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'";
            if (request.HttpMethod != "GET") {
                response.StatusCode = 405;
                response.Headers["Allow"] = "GET";
                return;
            }
            (byte[] data, string type) = request.Url?.AbsolutePath switch {
                "/" => (m_page, "text/html; charset=utf-8"),
                "/panel.js" => (m_script, "text/javascript; charset=utf-8"),
                "/api/snapshot" => (Volatile.Read(ref m_snapshot), "application/json; charset=utf-8"),
                _ => (null, null)
            };
            if (data == null) {
                response.StatusCode = 404;
                return;
            }
            response.ContentType = type;
            response.ContentLength64 = data.Length;
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
            await response.OutputStream.WriteAsync(data.AsMemory(), deadline.Token).ConfigureAwait(false);
        }
    }
}
#endif
