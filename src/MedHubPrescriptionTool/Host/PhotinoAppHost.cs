using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Ipc;
using Photino.NET;

namespace MedHubPrescriptionTool.Host;

/// <summary>
/// Photino desktop application host that launches the WebView2 native container,
/// registers custom scheme asset handlers, and connects frontend IPC to IpcDispatcher.
/// </summary>
public static class PhotinoAppHost
{
    private static readonly Assembly CurrentAssembly = typeof(PhotinoAppHost).Assembly;

    /// <summary>
    /// Launches the Photino desktop window application.
    /// </summary>
    public static void Run(string[] args, EmbeddedWebServer? existingServer = null)
    {
        // Allow software rasterization fallback for virtualized displays / remote desktop sessions
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--disable-gpu --no-sandbox");

        var dispatcher = IpcDispatcher.CreateDefault();

        // 1. Start or reuse embedded local web server
        EmbeddedWebServer? ownedServer = null;
        EmbeddedWebServer webServer;
        if (existingServer != null)
        {
            webServer = existingServer;
        }
        else
        {
            ownedServer = EmbeddedWebServer.Start(dispatcher);
            webServer = ownedServer;
        }

        try
        {
            // 2. Isolate WebView2 profile to avoid locking conflicts with existing processes
            string tempProfile = Path.Combine(Path.GetTempPath(), "MedHubPrescriptionTool_WV2_" + Environment.ProcessId);
            try { Directory.CreateDirectory(tempProfile); } catch { }

            var window = new PhotinoWindow()
                .SetTitle("MedHub Inpatient Prescription & MAR Workstation")
                .SetSize(1440, 900)
                .SetMinSize(1024, 720)
                .Center()
                .SetResizable(true)
                .SetContextMenuEnabled(true)
                .SetDevToolsEnabled(true)
                .SetGrantBrowserPermissions(true)
                .SetBrowserControlInitParameters("--disable-gpu")
                .SetTemporaryFilesPath(tempProfile);

            // Register custom scheme handler 'app' (e.g. app://localhost/index.html) as fallback
            window.RegisterCustomSchemeHandler("app", (object sender, string scheme, string url, out string contentType) =>
            {
                return ResolveAssetStream(url, out contentType);
            });

            // Register bidirectional IPC message handler
            window.RegisterWebMessageReceivedHandler(async (sender, rawMessage) =>
            {
                try
                {
                    Console.WriteLine($"[PhotinoAppHost] IPC message received from frontend: {rawMessage.Substring(0, Math.Min(60, rawMessage.Length))}...");
                    string response = await dispatcher.HandleMessageAsync(rawMessage).ConfigureAwait(false);
                    window.SendWebMessage(response);
                }
                catch (Exception ex)
                {
                    string errResponse = $"{{\"requestId\":\"err\",\"success\":false,\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}";
                    window.SendWebMessage(errResponse);
                }
            });

            // Navigate to the workstation clinical portal via local HTTP server
            string startupUrl = $"{webServer.BaseUrl}index.html";
            Console.WriteLine($"[PhotinoAppHost] Navigating desktop browser to: {startupUrl}");
            window.Load(startupUrl);

            // Blocking loop until window is closed
            window.WaitForClose();
        }
        finally
        {
            ownedServer?.Dispose();
        }
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> EmbeddedAssetMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "MedHubPrescriptionTool.wwwroot.index.html", "index.html" },
        { "MedHubPrescriptionTool.wwwroot.css.clinical-theme.css", Path.Combine("css", "clinical-theme.css") },
        { "MedHubPrescriptionTool.wwwroot.js.ipc-bridge.js", Path.Combine("js", "ipc-bridge.js") },
        { "MedHubPrescriptionTool.wwwroot.js.app.js", Path.Combine("js", "app.js") },
        { "MedHubPrescriptionTool.wwwroot.assets.icons.svg", Path.Combine("assets", "icons.svg") },
    };

    /// <summary>
    /// Ensures that web assets (HTML/CSS/JS) exist on disk.
    /// If not found, extracts them from embedded assembly manifest resources.
    /// </summary>
    public static string EnsureWebAssetsPath()
    {
        // 1. Check if already present next to executable
        string appBase = AppContext.BaseDirectory;
        string appBaseIndex = Path.Combine(appBase, "wwwroot", "index.html");
        if (File.Exists(appBaseIndex))
        {
            return appBaseIndex;
        }

        // 2. Check development path
        string devBaseIndex = Path.Combine(Environment.CurrentDirectory, "src", "MedHubPrescriptionTool", "wwwroot", "index.html");
        if (File.Exists(devBaseIndex))
        {
            return devBaseIndex;
        }

        // 3. Extract embedded resources next to executable
        string targetDir = Path.Combine(appBase, "wwwroot");
        try
        {
            Directory.CreateDirectory(targetDir);
            ExtractEmbeddedAssets(targetDir);
            string extractedIndex = Path.Combine(targetDir, "index.html");
            if (File.Exists(extractedIndex))
            {
                return extractedIndex;
            }
        }
        catch
        {
            // 4. Fallback to LocalAppData if appBase is read-only (e.g. CD-ROM, Program Files)
            try
            {
                targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedHubPrescriptionTool", "wwwroot");
                Directory.CreateDirectory(targetDir);
                ExtractEmbeddedAssets(targetDir);
                string extractedIndex = Path.Combine(targetDir, "index.html");
                if (File.Exists(extractedIndex))
                {
                    return extractedIndex;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PhotinoAppHost] Failed to extract assets to AppData: {ex.Message}");
            }
        }

        return appBaseIndex;
    }

    /// <summary>
    /// Extracts embedded manifest resources to the target directory.
    /// </summary>
    public static void ExtractEmbeddedAssets(string targetDir)
    {
        foreach (var (resName, relPath) in EmbeddedAssetMap)
        {
            try
            {
                string destPath = Path.Combine(targetDir, relPath);
                string? dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var resStream = CurrentAssembly.GetManifestResourceStream(resName);
                if (resStream != null)
                {
                    using var fileStream = File.Create(destPath);
                    resStream.CopyTo(fileStream);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PhotinoAppHost] Error extracting asset '{resName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolves asset stream from either local disk (hot-reload) or embedded manifest resources.
    /// </summary>
    public static Stream? ResolveAssetStream(string url, out string contentType)
    {
        contentType = "application/octet-stream";

        try
        {
            string relativePath;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                relativePath = uri.AbsolutePath.TrimStart('/');
            }
            else
            {
                relativePath = url.TrimStart('/').TrimStart('\\');
            }

            if (string.IsNullOrEmpty(relativePath) || relativePath == "/")
            {
                relativePath = "index.html";
            }

            // Reject rooted paths and drive letters
            if (Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            {
                return null;
            }

            contentType = GetContentType(relativePath);

            // 1. Check local file system under AppContext.BaseDirectory/wwwroot/
            string wwwrootBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
            string localPath = Path.GetFullPath(Path.Combine(wwwrootBase, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!localPath.StartsWith(wwwrootBase, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (File.Exists(localPath))
            {
                return File.OpenRead(localPath);
            }

            // Also check relative to CurrentDirectory for development
            string devBase = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "src", "MedHubPrescriptionTool", "wwwroot"));
            string devPath = Path.GetFullPath(Path.Combine(devBase, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!devPath.StartsWith(devBase, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (File.Exists(devPath))
            {
                return File.OpenRead(devPath);
            }

            // 2. Check embedded manifest resource in assembly
            // Expected format: MedHubPrescriptionTool.wwwroot.index.html or MedHubPrescriptionTool.wwwroot.css.clinical-theme.css
            string resourceKey = relativePath.Replace('/', '.').Replace('\\', '.');
            string resourceName = $"MedHubPrescriptionTool.wwwroot.{resourceKey}";

            var stream = CurrentAssembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                return stream;
            }

            // Case-insensitive fallback lookup in manifest resources
            var allNames = CurrentAssembly.GetManifestResourceNames();
            foreach (var name in allNames)
            {
                if (name.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
                {
                    return CurrentAssembly.GetManifestResourceStream(name);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PhotinoAppHost] Error resolving asset '{url}': {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Resolves MIME content type based on file extension.
    /// </summary>
    public static string GetContentType(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            _ => "application/octet-stream"
        };
    }
}
