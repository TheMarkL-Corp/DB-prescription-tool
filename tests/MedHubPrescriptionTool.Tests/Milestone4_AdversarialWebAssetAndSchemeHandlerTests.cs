using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using MedHubPrescriptionTool.Host;
using Xunit;
using Xunit.Abstractions;

namespace MedHubPrescriptionTool.Tests;

/// <summary>
/// Adversarial Challenger Verification Suite for Milestone 4:
/// Web Assets Structure, Photino Custom Scheme Handler Resolution,
/// Embedded Resource Fallback, and Path Traversal / Security Edge Cases.
/// </summary>
public class Milestone4_AdversarialWebAssetAndSchemeHandlerTests
{
    private readonly ITestOutputHelper _output;
    private static readonly Assembly ToolAssembly = typeof(PhotinoAppHost).Assembly;

    public Milestone4_AdversarialWebAssetAndSchemeHandlerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetWwwrootDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, "wwwroot");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        candidate = Path.Combine(Environment.CurrentDirectory, "src", "MedHubPrescriptionTool", "wwwroot");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        candidate = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "src", "MedHubPrescriptionTool", "wwwroot");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate wwwroot directory.");
    }

    #region Task 1: Scheme Handler Resolution & Embedded Fallback

    [Theory]
    [InlineData("app://localhost/index.html", "text/html; charset=utf-8", "MedHub Inpatient Prescription & MAR Workstation")]
    [InlineData("app://localhost/css/clinical-theme.css", "text/css; charset=utf-8", "--color-primary")]
    [InlineData("app://localhost/js/app.js", "application/javascript; charset=utf-8", "cacheElements")]
    [InlineData("app://localhost/js/ipc-bridge.js", "application/javascript; charset=utf-8", "window.ipc")]
    [InlineData("app://localhost/assets/icons.svg", "image/svg+xml", "<svg")]
    [Trait("Category", "ChallengerM4")]
    public void Task1_01_CustomScheme_CoreAssets_ResolveNonNullWithCorrectMimeAndContent(string url, string expectedMime, string expectedSubstring)
    {
        using var stream = PhotinoAppHost.ResolveAssetStream(url, out string contentType);

        stream.Should().NotBeNull($"URI '{url}' must resolve to a valid non-null stream");
        stream!.Length.Should().BeGreaterThan(0, $"Asset stream for '{url}' must not be empty");
        contentType.Should().Be(expectedMime, $"MIME type for '{url}' must match expected");

        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        content.Should().Contain(expectedSubstring, $"Resolved stream for '{url}' must contain expected payload");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task1_02_Assembly_ManifestResources_ContainAllCoreAssets()
    {
        var manifestNames = ToolAssembly.GetManifestResourceNames();
        _output.WriteLine($"Manifest Resources in MedHubPrescriptionTool ({manifestNames.Length}):");
        foreach (var name in manifestNames)
        {
            _output.WriteLine($"  - {name}");
        }

        string[] requiredResources =
        {
            "MedHubPrescriptionTool.wwwroot.index.html",
            "MedHubPrescriptionTool.wwwroot.css.clinical-theme.css",
            "MedHubPrescriptionTool.wwwroot.js.app.js",
            "MedHubPrescriptionTool.wwwroot.js.ipc-bridge.js",
            "MedHubPrescriptionTool.wwwroot.assets.icons.svg"
        };

        foreach (var req in requiredResources)
        {
            manifestNames.Should().Contain(req, $"Assembly must embed '{req}' as EmbeddedResource");
            using var stream = ToolAssembly.GetManifestResourceStream(req);
            stream.Should().NotBeNull($"Embedded resource stream '{req}' must be readable");
            stream!.Length.Should().BeGreaterThan(0, $"Embedded resource stream '{req}' must be non-empty");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task1_03_CustomScheme_FallbackMechanism_WhenDiskLookupRedirected_ResolvesFromEmbeddedResources()
    {
        // True fallback test: temporarily rename AppContext.BaseDirectory/wwwroot
        // AND point Environment.CurrentDirectory to empty temp directory
        string originalCwd = Environment.CurrentDirectory;
        string tempDir = Path.Combine(Path.GetTempPath(), "MedHubFallbackTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string binWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        string binWwwrootBak = Path.Combine(AppContext.BaseDirectory, "wwwroot_bak_" + Guid.NewGuid().ToString("N"));
        bool renamed = false;

        try
        {
            if (Directory.Exists(binWwwroot))
            {
                Directory.Move(binWwwroot, binWwwrootBak);
                renamed = true;
            }

            Environment.CurrentDirectory = tempDir;

            // At this point, neither localPath nor devPath exists on disk!
            // ResolveAssetStream MUST resolve purely from EmbeddedResource manifest streams.
            using var streamHtml = PhotinoAppHost.ResolveAssetStream("app://localhost/index.html", out string ctHtml);
            streamHtml.Should().NotBeNull("Pure embedded fallback must resolve index.html when all disk files are absent");
            streamHtml!.Length.Should().BeGreaterThan(0);
            ctHtml.Should().Be("text/html; charset=utf-8");

            using var streamCss = PhotinoAppHost.ResolveAssetStream("app://localhost/css/clinical-theme.css", out string ctCss);
            streamCss.Should().NotBeNull("Pure embedded fallback must resolve css/clinical-theme.css when all disk files are absent");
            streamCss!.Length.Should().BeGreaterThan(0);
            ctCss.Should().Be("text/css; charset=utf-8");

            using var streamJs = PhotinoAppHost.ResolveAssetStream("app://localhost/js/app.js", out string ctJs);
            streamJs.Should().NotBeNull("Pure embedded fallback must resolve js/app.js when all disk files are absent");
            streamJs!.Length.Should().BeGreaterThan(0);
            ctJs.Should().Be("application/javascript; charset=utf-8");

            using var streamBridge = PhotinoAppHost.ResolveAssetStream("app://localhost/js/ipc-bridge.js", out string ctBridge);
            streamBridge.Should().NotBeNull("Pure embedded fallback must resolve js/ipc-bridge.js when all disk files are absent");
            streamBridge!.Length.Should().BeGreaterThan(0);
            ctBridge.Should().Be("application/javascript; charset=utf-8");

            using var streamIcons = PhotinoAppHost.ResolveAssetStream("app://localhost/assets/icons.svg", out string ctIcons);
            streamIcons.Should().NotBeNull("Pure embedded fallback must resolve assets/icons.svg when all disk files are absent");
            streamIcons!.Length.Should().BeGreaterThan(0);
            ctIcons.Should().Be("image/svg+xml");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (renamed && Directory.Exists(binWwwrootBak))
            {
                try { Directory.Move(binWwwrootBak, binWwwroot); } catch { /* best effort restore */ }
            }
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
            }
        }
    }

    [Theory]
    [InlineData("app://localhost/INDEX.HTML")]
    [InlineData("app://localhost/CSS/CLINICAL-THEME.CSS")]
    [InlineData("app://localhost/JS/APP.JS")]
    [Trait("Category", "ChallengerM4")]
    public void Task1_04_CustomScheme_CaseInsensitive_ResourceResolution(string url)
    {
        using var stream = PhotinoAppHost.ResolveAssetStream(url, out string contentType);
        stream.Should().NotBeNull($"Case-insensitive resolution should resolve '{url}'");
        stream!.Length.Should().BeGreaterThan(0);
    }

    #endregion

    #region Task 2: Validate Web Assets Structure (DOM & CSS)

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_01_IndexHtml_ContainsAllRequiredKpiElements()
    {
        string wwwroot = GetWwwrootDirectory();
        string indexPath = Path.Combine(wwwroot, "index.html");
        string html = File.ReadAllText(indexPath);

        string[] requiredKpiIds =
        {
            "kpiInpatients",
            "kpiActiveOrders",
            "kpiDispensed",
            "kpiPendingDue",
            "kpiDuePendingCount",
            "kpiFuturePendingCount",
            "kpiControlledCount",
            "kpiReturnedWastedCount"
        };

        foreach (var id in requiredKpiIds)
        {
            html.Should().Contain($"id=\"{id}\"", $"index.html must contain KPI element with ID '{id}'");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_02_IndexHtml_ContainsAllRequiredDualViewAndNavigationElements()
    {
        string wwwroot = GetWwwrootDirectory();
        string indexPath = Path.Combine(wwwroot, "index.html");
        string html = File.ReadAllText(indexPath);

        string[] requiredNavIds =
        {
            "tabBtnPatients",
            "tabBtnMar",
            "panePatients",
            "paneMar",
            "patientTabCount",
            "marTabCount"
        };

        foreach (var id in requiredNavIds)
        {
            html.Should().Contain($"id=\"{id}\"", $"index.html must contain navigation element with ID '{id}'");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_03_IndexHtml_ContainsAllRequiredPatientMasterDetailElements()
    {
        string wwwroot = GetWwwrootDirectory();
        string indexPath = Path.Combine(wwwroot, "index.html");
        string html = File.ReadAllText(indexPath);

        string[] requiredMasterDetailIds =
        {
            "patientStationFilter",
            "patientSearchInput",
            "patientListContainer",
            "patientDetailContainer"
        };

        foreach (var id in requiredMasterDetailIds)
        {
            html.Should().Contain($"id=\"{id}\"", $"index.html must contain Master-Detail element with ID '{id}'");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_04_IndexHtml_ContainsAllRequiredMarTableAndFilterElements()
    {
        string wwwroot = GetWwwrootDirectory();
        string indexPath = Path.Combine(wwwroot, "index.html");
        string html = File.ReadAllText(indexPath);

        string[] requiredMarIds =
        {
            "marSearchInput",
            "marStationFilter",
            "marTableBody",
            "chipHighAlertOnly",
            "chipDateAll",
            "chipDateToday",
            "chipDatePast",
            "chipDateFuture",
            "chipStatusAll",
            "chipStatusDue",
            "chipStatusDispensed",
            "chipStatusReturned",
            "chipStatusWasted",
            "chipStatusCancelled"
        };

        foreach (var id in requiredMarIds)
        {
            html.Should().Contain($"id=\"{id}\"", $"index.html must contain MAR filter/table element with ID '{id}'");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_05_IndexHtml_ContainsAllRequiredModalsAndInputs()
    {
        string wwwroot = GetWwwrootDirectory();
        string indexPath = Path.Combine(wwwroot, "index.html");
        string html = File.ReadAllText(indexPath);

        string[] requiredModalElements =
        {
            // Medication Modal
            "medicationModal",
            "btnCloseMedModal",
            "btnDismissMedModal",
            "medModalBody",

            // Settings Modal
            "settingsModal",
            "btnCloseSettingsModal",
            "btnCloseSettings",
            "btnTestConnection",
            "btnSaveDbSettings",
            "cfgHost",
            "cfgPort",
            "cfgDatabase",
            "cfgUsername",
            "cfgPassword",
            "testConnectionStatus",

            // Export Modal
            "exportModal",
            "btnCloseExportModal",
            "btnCancelExport",
            "btnConfirmExport",
            "exportFilteredCount",
            "exportTotalCount"
        };

        foreach (var id in requiredModalElements)
        {
            html.Should().Contain($"id=\"{id}\"", $"index.html must contain modal element with ID '{id}'");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_06_IndexHtml_ContainsHeaderActionsAndToastContainer()
    {
        string wwwroot = GetWwwrootDirectory();
        string indexPath = Path.Combine(wwwroot, "index.html");
        string html = File.ReadAllText(indexPath);

        string[] requiredHeaderIds =
        {
            "dbStatusBadge",
            "dbStatusText",
            "syncTimestamp",
            "btnRefreshData",
            "btnExportExcel",
            "btnSettingsModal",
            "btnToggleTheme",
            "themeIcon",
            "themeLabel",
            "toastContainer"
        };

        foreach (var id in requiredHeaderIds)
        {
            html.Should().Contain($"id=\"{id}\"", $"index.html must contain header action element with ID '{id}'");
        }

        // Script loading order: ipc-bridge.js MUST precede app.js
        int idxBridge = html.IndexOf("js/ipc-bridge.js", StringComparison.OrdinalIgnoreCase);
        int idxApp = html.IndexOf("js/app.js", StringComparison.OrdinalIgnoreCase);
        idxBridge.Should().BeGreaterThan(-1, "ipc-bridge.js must be referenced in index.html");
        idxApp.Should().BeGreaterThan(-1, "app.js must be referenced in index.html");
        idxBridge.Should().BeLessThan(idxApp, "ipc-bridge.js must be loaded before app.js");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_07_ClinicalThemeCss_ContainsRequiredBrandAndSemanticTokens()
    {
        string wwwroot = GetWwwrootDirectory();
        string cssPath = Path.Combine(wwwroot, "css", "clinical-theme.css");
        string css = File.ReadAllText(cssPath);

        // Medical Teal brand palette
        css.Should().Contain("--color-primary: #0891b2", "Primary Medical Teal must match R3 specification");
        css.Should().Contain("--color-primary-hover", "Hover state for primary token must exist");
        css.Should().Contain("--color-primary-light", "Light background for primary token must exist");

        // Secondary Cyan
        css.Should().Contain("--color-secondary", "Secondary cyan color token must exist");

        // Status Semantics
        css.Should().Contain("--color-success: #059669", "Success green token must match R3 specification");
        css.Should().Contain("--color-warning: #d97706", "Warning amber token must match R3 specification");
        css.Should().Contain("--color-danger: #dc2626", "Danger/High-Alert red token must match R3 specification");
        css.Should().Contain("--color-purple: #7c3aed", "Purple controlled drug token must exist");
        css.Should().Contain("--color-orange: #ea580c", "Orange waste token must exist");

        // Surfaces
        css.Should().Contain("--color-bg-app", "Application canvas background variable must exist");
        css.Should().Contain("--color-bg-surface", "Surface container variable must exist");
        css.Should().Contain("--color-border", "Border color variable must exist");
        css.Should().Contain("--color-text-primary", "Primary text color variable must exist");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_08_ClinicalThemeCss_ContainsNightShiftDarkModeRules()
    {
        string wwwroot = GetWwwrootDirectory();
        string cssPath = Path.Combine(wwwroot, "css", "clinical-theme.css");
        string css = File.ReadAllText(cssPath);

        // Dark mode selectors
        css.Should().Contain("[data-theme=\"dark\"]", "CSS must declare dark theme attribute selector");
        css.Should().Contain("body.dark-theme", "CSS must declare dark theme class selector fallback");

        // Dark mode specific color overrides
        css.Should().Contain("--color-bg-app: #0b0f17", "Dark mode canvas must be deep slate/dark #0b0f17");
        css.Should().Contain("--color-bg-surface: #1e293b", "Dark mode surface must be slate-800 #1e293b");
        css.Should().Contain("--color-text-primary: #f8fafc", "Dark mode high-contrast text must be #f8fafc");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task2_09_ClinicalThemeCss_ContainsAllClinicalBadges()
    {
        string wwwroot = GetWwwrootDirectory();
        string cssPath = Path.Combine(wwwroot, "css", "clinical-theme.css");
        string css = File.ReadAllText(cssPath);

        string[] requiredBadges =
        {
            ".badge-dispensed",
            ".badge-pending",
            ".badge-due",
            ".badge-cancelled",
            ".badge-returned",
            ".badge-wasted",
            ".badge-high-alert",
            ".badge-controlled",
            ".badge-cold"
        };

        foreach (var badge in requiredBadges)
        {
            css.Should().Contain(badge, $"CSS must define clinical badge styling for '{badge}'");
        }
    }

    #endregion

    #region Task 3: Edge Cases, Path Traversal & Security Handling

    [Theory]
    [InlineData("app://localhost/nonexistent.html")]
    [InlineData("app://localhost/secret.txt")]
    [InlineData("app://localhost/sub/folder/missing.js")]
    [InlineData("app://localhost/images/doesnotexist.png")]
    [Trait("Category", "ChallengerM4")]
    public void Task3_01_CustomScheme_NonExistentFiles_ReturnNull(string url)
    {
        using var stream = PhotinoAppHost.ResolveAssetStream(url, out string contentType);
        stream.Should().BeNull($"Non-existent path '{url}' must return null stream");
    }

    [Theory]
    [InlineData("app://localhost/../../secret")]
    [InlineData("app://localhost/../../appsettings.json")]
    [InlineData("app://localhost/../appsettings.json")]
    [InlineData("app://localhost/css/../../appsettings.json")]
    [InlineData("app://localhost/js/../../MedHubPrescriptionTool.csproj")]
    [InlineData("app://localhost/..%2f..%2fsecret")]
    [InlineData("app://localhost/....//secret")]
    [InlineData("app://localhost/etc/passwd")]
    [InlineData("app://localhost/../../../Windows/System32/drivers/etc/hosts")]
    [Trait("Category", "ChallengerM4")]
    public void Task3_02_CustomScheme_RelativePathTraversal_SecurelyReturnNull(string maliciousUrl)
    {
        using var stream = PhotinoAppHost.ResolveAssetStream(maliciousUrl, out string contentType);
        stream.Should().BeNull($"Relative path traversal attempt '{maliciousUrl}' must not resolve or leak files outside wwwroot");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    [Trait("Category", "SecurityVulnerability")]
    public void Task3_02b_SecurityVulnerability_ArbitraryFileReadViaRootedPath_MustBePrevented()
    {
        // VULNERABILITY ASSERTION:
        // On Windows, if url contains a drive letter (e.g. app://localhost/C:/Windows/win.ini),
        // Path.Combine(AppContext.BaseDirectory, "wwwroot", @"C:\Windows\win.ini") evaluates to
        // @"C:\Windows\win.ini" because Path.Combine discards path1 when path2 is rooted.
        // This allows reading any file on the host machine via the Photino scheme handler.
        // A secure implementation MUST reject rooted paths (Path.IsPathRooted) and ensure
        // resolved file paths stay strictly within wwwroot.
        using var stream = PhotinoAppHost.ResolveAssetStream("app://localhost/C:/Windows/win.ini", out string contentType);
        stream.Should().BeNull("CRITICAL: Rooted path traversal 'app://localhost/C:/Windows/win.ini' must be blocked and return null to prevent arbitrary file read");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    [Trait("Category", "SecurityVulnerability")]
    public void Task3_02c_SecurityVulnerability_AppsettingsJsonLeakViaAbsoluteUri_MustBePrevented()
    {
        string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            string exploitUrl = $"app://localhost/{appSettingsPath.Replace('\\', '/')}";
            using var stream = PhotinoAppHost.ResolveAssetStream(exploitUrl, out string contentType);
            stream.Should().BeNull($"CRITICAL: Sensitive database credentials in '{exploitUrl}' must not be exposed via scheme handler");
        }
    }

    [Theory]
    [InlineData("app://localhost/", "text/html; charset=utf-8")]
    [InlineData("app://localhost", "text/html; charset=utf-8")]
    [InlineData("app://localhost/index.html?token=test1234", "text/html; charset=utf-8")]
    [InlineData("app://localhost/index.html#overview", "text/html; charset=utf-8")]
    [InlineData("app://localhost/index.html?a=1&b=2#section", "text/html; charset=utf-8")]
    [Trait("Category", "ChallengerM4")]
    public void Task3_03_CustomScheme_RootAndQueryVariants_ResolveToIndexHtml(string url, string expectedMime)
    {
        using var stream = PhotinoAppHost.ResolveAssetStream(url, out string contentType);
        stream.Should().NotBeNull($"Root/query variant '{url}' must resolve to index.html stream");
        stream!.Length.Should().BeGreaterThan(0);
        contentType.Should().Be(expectedMime);

        using var reader = new StreamReader(stream);
        string html = reader.ReadToEnd();
        html.Should().Contain("MedHub Inpatient Prescription & MAR Workstation");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not_a_valid_url")]
    [InlineData("http://")]
    [InlineData("app://")]
    [InlineData("ftp://localhost/index.html")]
    [Trait("Category", "ChallengerM4")]
    public void Task3_04_CustomScheme_MalformedUrls_HandleGracefullyWithoutException(string malformedUrl)
    {
        // Must never throw unhandled exception or crash application
        Action act = () =>
        {
            using var stream = PhotinoAppHost.ResolveAssetStream(malformedUrl, out string ct);
        };

        act.Should().NotThrow("Malformed URL input must be handled gracefully with null return");
    }

    [Fact]
    [Trait("Category", "ChallengerM4")]
    public void Task3_05_CustomScheme_NullUrl_HandlesGracefullyWithoutException()
    {
        Action act = () =>
        {
            using var stream = PhotinoAppHost.ResolveAssetStream(null!, out string ct);
            stream.Should().BeNull();
        };

        act.Should().NotThrow("Null URL input must not throw unhandled exception");
    }

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("index.htm", "text/html; charset=utf-8")]
    [InlineData("styles.css", "text/css; charset=utf-8")]
    [InlineData("bundle.js", "application/javascript; charset=utf-8")]
    [InlineData("module.mjs", "application/javascript; charset=utf-8")]
    [InlineData("config.json", "application/json; charset=utf-8")]
    [InlineData("icon.svg", "image/svg+xml")]
    [InlineData("image.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("anim.gif", "image/gif")]
    [InlineData("favicon.ico", "image/x-icon")]
    [InlineData("font.woff", "font/woff")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("font.ttf", "font/ttf")]
    [InlineData("binary.xyz", "application/octet-stream")]
    [InlineData("archive.zip", "application/octet-stream")]
    [InlineData("noext", "application/octet-stream")]
    [Trait("Category", "ChallengerM4")]
    public void Task3_06_GetContentType_ComprehensiveMimeMapping(string path, string expectedMime)
    {
        string actualMime = PhotinoAppHost.GetContentType(path);
        actualMime.Should().Be(expectedMime, $"File extension for '{path}' must map to MIME type '{expectedMime}'");
    }

    #endregion
}
