using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Ipc;

namespace MedHubPrescriptionTool.Host;

/// <summary>
/// Lightweight embedded HTTP server that serves static workstation assets and REST/IPC endpoints.
/// Guarantees seamless browser and WebView2 access without file:// sandbox or CORS restrictions.
/// </summary>
public sealed class EmbeddedWebServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly IpcDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    private EmbeddedWebServer(int port, IpcDispatcher dispatcher)
    {
        Port = port;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>
    /// Starts the embedded HTTP server on port 58999, falling back to an available ephemeral port.
    /// </summary>
    public static EmbeddedWebServer Start(IpcDispatcher dispatcher, int preferredPort = 58999)
    {
        int port = preferredPort;
        if (!IsPortAvailable(port))
        {
            port = GetAvailablePort();
        }

        var server = new EmbeddedWebServer(port, dispatcher);
        try
        {
            server._listener.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebServer] Preferred port {port} failed ({ex.Message}), trying ephemeral port...");
            port = GetAvailablePort();
            server = new EmbeddedWebServer(port, dispatcher);
            server._listener.Start();
        }

        server._listenTask = Task.Run(() => server.RunLoopAsync(server._cts.Token));
        Console.WriteLine($"[WebServer] Workstation HTTP server running at: {server.BaseUrl}");
        return server;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context), token);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WebServer] Error accepting request: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var req = context.Request;
            var resp = context.Response;
            Console.WriteLine($"[WebServer] Received request: {req.HttpMethod} {req.Url}");

            // Enable CORS for local cross-origin development / testing
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = (int)HttpStatusCode.NoContent;
                resp.Close();
                return;
            }

            string rawPath = req.Url?.AbsolutePath ?? "/";
            if (rawPath == "/" || string.IsNullOrWhiteSpace(rawPath))
            {
                rawPath = "/index.html";
            }

            // Handle API requests
            if (rawPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                string action = rawPath.Substring(5).Trim('/');
                string requestJson;

                if (req.HttpMethod == "POST" && req.HasEntityBody)
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                    requestJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
                else
                {
                    requestJson = $"{{\"requestId\":\"web_{Guid.NewGuid():N}\",\"action\":\"{action}\",\"payload\":null}}";
                }

                // If body does not specify action, inject action from URL
                if (!requestJson.Contains("\"action\":", StringComparison.OrdinalIgnoreCase))
                {
                    requestJson = $"{{\"requestId\":\"web_{Guid.NewGuid():N}\",\"action\":\"{action}\",\"payload\":{requestJson}}}";
                }

                string responseJson = await _dispatcher.HandleMessageAsync(requestJson).ConfigureAwait(false);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(responseJson);

                resp.StatusCode = (int)HttpStatusCode.OK;
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = jsonBytes.Length;
                await resp.OutputStream.WriteAsync(jsonBytes).ConfigureAwait(false);
                resp.Close();
                return;
            }

            // Handle Static Asset requests
            using var assetStream = PhotinoAppHost.ResolveAssetStream(rawPath, out string contentType);
            if (assetStream != null)
            {
                resp.StatusCode = (int)HttpStatusCode.OK;
                resp.ContentType = contentType;
                resp.ContentLength64 = assetStream.Length;
                await assetStream.CopyToAsync(resp.OutputStream).ConfigureAwait(false);
            }
            else
            {
                resp.StatusCode = (int)HttpStatusCode.NotFound;
                byte[] notFoundBytes = Encoding.UTF8.GetBytes("404 Not Found");
                resp.ContentType = "text/plain";
                resp.ContentLength64 = notFoundBytes.Length;
                await resp.OutputStream.WriteAsync(notFoundBytes).ConfigureAwait(false);
            }

            resp.Close();
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch
            {
                // Suppress secondary socket close errors
            }
            Console.Error.WriteLine($"[WebServer] Error handling {context.Request.Url}: {ex.Message}");
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var tcp = new TcpListener(IPAddress.Loopback, port);
            tcp.Start();
            tcp.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetAvailablePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
            _listener.Close();
        }
        catch
        {
            // Ignore listener teardown errors
        }
        _cts.Dispose();
    }
}
