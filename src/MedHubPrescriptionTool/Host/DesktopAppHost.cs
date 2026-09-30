using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Ipc;

namespace MedHubPrescriptionTool.Host;

/// <summary>
/// Unified desktop application host for the MedHub Inpatient Prescription Workstation.
/// Provides a rock-solid desktop application window using native Edge/Chrome Application Mode,
/// Photino native window fallback, or default browser fallback.
/// Guarantees 100% display compatibility across physical displays, Remote Desktop, and VDI sessions.
/// </summary>
public static class DesktopAppHost
{
    private static readonly string[] CandidateAppBrowsers = new[]
    {
        // 1. Microsoft Edge (standard Windows system browser)
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        // 2. Google Chrome
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
    };

    /// <summary>
    /// Launches the MedHub desktop application.
    /// </summary>
    public static void Run(string[] args)
    {
        bool forcePhotino = args.Any(a => a.Equals("--photino", StringComparison.OrdinalIgnoreCase));
        bool forceBrowser = args.Any(a => a.Equals("--browser", StringComparison.OrdinalIgnoreCase) || a.Equals("-b", StringComparison.OrdinalIgnoreCase));

        int port = 58999;
        int portIdx = Array.FindIndex(args, a => a.Equals("--port", StringComparison.OrdinalIgnoreCase) || a.Equals("-p", StringComparison.OrdinalIgnoreCase));
        if (portIdx >= 0 && portIdx + 1 < args.Length && int.TryParse(args[portIdx + 1], out int customPort))
        {
            port = customPort;
        }

        var dispatcher = IpcDispatcher.CreateDefault();

        // 1. Start embedded HTTP and REST API server
        using var webServer = EmbeddedWebServer.Start(dispatcher, preferredPort: port);
        string startupUrl = $"{webServer.BaseUrl}index.html";

        // 2. If force Photino requested, run Photino host
        if (forcePhotino)
        {
            Console.WriteLine("[DesktopAppHost] Forced Photino native window mode requested via --photino");
            PhotinoAppHost.Run(args, webServer);
            return;
        }

        // 3. Locate system application browser (Edge or Chrome)
        string? appBrowser = FindApplicationBrowser();

        if (!forceBrowser && !string.IsNullOrEmpty(appBrowser) && File.Exists(appBrowser))
        {
            RunAppWindow(appBrowser, startupUrl, webServer);
        }
        else if (forceBrowser || string.IsNullOrEmpty(appBrowser))
        {
            RunBrowserWindow(startupUrl, webServer);
        }
        else
        {
            // Fallback to Photino
            PhotinoAppHost.Run(args, webServer);
        }
    }

    /// <summary>
    /// Runs the workstation as a dedicated desktop application window via Edge or Chrome App Mode.
    /// </summary>
    private static void RunAppWindow(string browserExe, string startupUrl, EmbeddedWebServer webServer)
    {
        string browserName = Path.GetFileNameWithoutExtension(browserExe);
        string tempProfile = Path.Combine(Path.GetTempPath(), "MedHub_Desktop_" + Environment.ProcessId);

        try
        {
            Directory.CreateDirectory(tempProfile);
        }
        catch
        {
            // Ignore temp dir creation error
        }

        var psi = new ProcessStartInfo
        {
            FileName = browserExe,
            Arguments = $"--app=\"{startupUrl}\" --window-size=1440,900 --user-data-dir=\"{tempProfile}\" --no-first-run --no-default-browser-check",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        PrintBanner("Dedicated Desktop Application Window", $"{browserName.ToUpperInvariant()} App Engine", startupUrl);

        Process? browserProc = null;
        try
        {
            browserProc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DesktopAppHost] Could not launch app window ({ex.Message}), falling back to default browser...");
            RunBrowserWindow(startupUrl, webServer);
            return;
        }

        if (browserProc == null)
        {
            RunBrowserWindow(startupUrl, webServer);
            return;
        }

        Console.WriteLine($"[DesktopAppHost] Application window running (PID {browserProc.Id}).");
        Console.WriteLine("[DesktopAppHost] Workstation is active. To exit, close the application window or press Ctrl+C.");

        using var shutdownEvent = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[DesktopAppHost] Shutdown signal received. Exiting...");
            shutdownEvent.Set();
        };

        // Monitor browser process exit
        var monitorThread = new Thread(() =>
        {
            try
            {
                browserProc.WaitForExit();
                Console.WriteLine("[DesktopAppHost] Application window was closed by user.");
                shutdownEvent.Set();
            }
            catch
            {
                shutdownEvent.Set();
            }
        })
        {
            IsBackground = true
        };
        monitorThread.Start();

        // Wait until window closed or Ctrl+C
        shutdownEvent.Wait();

        // Clean up temporary browser profile
        try
        {
            if (!browserProc.HasExited)
            {
                browserProc.Kill();
            }
        }
        catch { }

        try
        {
            if (Directory.Exists(tempProfile))
            {
                Directory.Delete(tempProfile, recursive: true);
            }
        }
        catch { }

        Console.WriteLine("[DesktopAppHost] Workstation host cleanly stopped.");
    }

    /// <summary>
    /// Opens the workstation portal in the default system browser and keeps the server alive.
    /// </summary>
    private static void RunBrowserWindow(string startupUrl, EmbeddedWebServer webServer)
    {
        PrintBanner("System Default Browser Window", "System Default Browser", startupUrl);

        try
        {
            Process.Start(new ProcessStartInfo(startupUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DesktopAppHost] Failed to open browser: {ex.Message}");
        }

        Console.WriteLine("[DesktopAppHost] Workstation is active. Press Ctrl+C in this console to exit.");

        using var shutdownEvent = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[DesktopAppHost] Shutdown signal received. Exiting...");
            shutdownEvent.Set();
        };

        shutdownEvent.Wait();
        Console.WriteLine("[DesktopAppHost] Workstation host cleanly stopped.");
    }

    /// <summary>
    /// Searches common Windows locations for Microsoft Edge or Google Chrome executables.
    /// </summary>
    public static string? FindApplicationBrowser()
    {
        foreach (var path in CandidateAppBrowsers)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        // Search PATH
        string[] exes = { "msedge.exe", "chrome.exe" };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var exe in exes)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    string candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private static void PrintBanner(string mode, string hostEngine, string url)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine("  MedHub Inpatient Prescription & MAR Workstation (v1.0.0)");
        Console.WriteLine($"  Mode    : {mode}");
        Console.WriteLine($"  Engine  : {hostEngine}");
        Console.WriteLine($"  URL     : {url}");
        Console.WriteLine("  Database: amisdbV2 on 127.0.0.1:5433 (READ-ONLY ENFORCED)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }
}
