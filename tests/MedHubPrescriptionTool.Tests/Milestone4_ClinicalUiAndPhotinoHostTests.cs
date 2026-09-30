using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Host;
using MedHubPrescriptionTool.Ipc;
using MedHubPrescriptionTool.Services;
using Xunit;
using Xunit.Abstractions;

namespace MedHubPrescriptionTool.Tests;

public class Milestone4_ClinicalUiAndPhotinoHostTests
{
    private readonly ITestOutputHelper _output;
    private readonly IpcDispatcher _dispatcher;

    public Milestone4_ClinicalUiAndPhotinoHostTests(ITestOutputHelper output)
    {
        _output = output;
        _dispatcher = IpcDispatcher.CreateDefault();
    }

    #region 1. Web Assets & Clinical Theme Verification

    [Fact]
    [Trait("Category", "Milestone4")]
    public void M4_01_WebAssets_FilesAndMarkup_Integrity()
    {
        string baseDir = AppContext.BaseDirectory;
        string wwwrootDir = Path.Combine(baseDir, "wwwroot");
        if (!Directory.Exists(wwwrootDir))
        {
            wwwrootDir = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "src", "MedHubPrescriptionTool", "wwwroot");
            if (!Directory.Exists(wwwrootDir))
            {
                wwwrootDir = Path.Combine(Environment.CurrentDirectory, "src", "MedHubPrescriptionTool", "wwwroot");
            }
        }

        string indexPath = Path.Combine(wwwrootDir, "index.html");
        string cssPath = Path.Combine(wwwrootDir, "css", "clinical-theme.css");
        string bridgePath = Path.Combine(wwwrootDir, "js", "ipc-bridge.js");
        string appPath = Path.Combine(wwwrootDir, "js", "app.js");

        File.Exists(indexPath).Should().BeTrue("index.html must exist in wwwroot");
        File.Exists(cssPath).Should().BeTrue("css/clinical-theme.css must exist in wwwroot");
        File.Exists(bridgePath).Should().BeTrue("js/ipc-bridge.js must exist in wwwroot");
        File.Exists(appPath).Should().BeTrue("js/app.js must exist in wwwroot");

        string html = File.ReadAllText(indexPath);
        html.Should().Contain("MedHub Inpatient Prescription & MAR Workstation", "Title must match specification");
        html.Should().Contain("kpiInpatients");
        html.Should().Contain("kpiActiveOrders");
        html.Should().Contain("kpiDispensed");
        html.Should().Contain("kpiPendingDue");
        html.Should().Contain("tabBtnPatients");
        html.Should().Contain("tabBtnMar");
        html.Should().Contain("medicationModal");
        html.Should().Contain("settingsModal");
        html.Should().Contain("exportModal");

        string css = File.ReadAllText(cssPath);
        css.Should().Contain("--color-primary: #0891b2");
        css.Should().Contain("dark-theme");
        css.Should().Contain("--color-success");
        css.Should().Contain("--color-danger");

        string bridge = File.ReadAllText(bridgePath);
        bridge.Should().Contain("window.ipc");
        bridge.Should().Contain("invoke");
    }

    [Theory]
    [InlineData("app://localhost/index.html", "text/html; charset=utf-8")]
    [InlineData("app://localhost/css/clinical-theme.css", "text/css; charset=utf-8")]
    [InlineData("app://localhost/js/ipc-bridge.js", "application/javascript; charset=utf-8")]
    [InlineData("app://localhost/js/app.js", "application/javascript; charset=utf-8")]
    [InlineData("app://localhost/assets/icons.svg", "image/svg+xml")]
    [Trait("Category", "Milestone4")]
    public void M4_02_PhotinoCustomScheme_AssetStreaming_ResolvesCorrectly(string url, string expectedContentType)
    {
        using var stream = PhotinoAppHost.ResolveAssetStream(url, out string contentType);
        stream.Should().NotBeNull($"Stream for '{url}' must be resolved from disk or embedded resources");
        stream!.Length.Should().BeGreaterThan(0);
        contentType.Should().Be(expectedContentType);
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public void M4_03_PhotinoCustomScheme_NonExistentAsset_ReturnsNull()
    {
        using var stream = PhotinoAppHost.ResolveAssetStream("app://localhost/non_existent_file_12345.xyz", out string contentType);
        stream.Should().BeNull();
    }

    #endregion

    #region 2. IPC Dispatcher Actions & JSON Protocol

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_04_IpcDispatcher_LoadSnapshot_ReturnsFullClinicalData()
    {
        string reqJson = "{\"requestId\":\"req_load_01\",\"action\":\"loadSnapshot\",\"payload\":null}";
        string resJson = await _dispatcher.HandleMessageAsync(reqJson);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("requestId").GetString().Should().Be("req_load_01");

        var data = root.GetProperty("data");
        var kpi = data.GetProperty("kpiSummary");
        kpi.GetProperty("totalInpatients").GetInt32().Should().Be(15);
        kpi.GetProperty("totalActiveOrders").GetInt32().Should().Be(31);
        kpi.GetProperty("dispensedCount").GetInt32().Should().Be(13);
        kpi.GetProperty("controlledCount").GetInt32().Should().Be(143);

        data.GetProperty("patients").GetArrayLength().Should().Be(16);
        data.GetProperty("marSchedules").GetArrayLength().Should().Be(385);
        data.GetProperty("activePrescriptions").GetArrayLength().Should().Be(31);
        data.GetProperty("medications").GetArrayLength().Should().Be(114);
        data.GetProperty("stations").GetArrayLength().Should().Be(4);
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_05_IpcDispatcher_FilterMar_AppliesCriteriaCorrectly()
    {
        // 1. Filter by Drug Name "Levemir"
        string reqLevemir = "{\"requestId\":\"req_filter_01\",\"action\":\"filterMar\",\"payload\":{\"search\":\"Levemir\"}}";
        string resLevemir = await _dispatcher.HandleMessageAsync(reqLevemir);
        using var docLevemir = JsonDocument.Parse(resLevemir);
        docLevemir.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docLevemir.RootElement.GetProperty("data").GetArrayLength().Should().Be(22);

        // 2. Filter by MRN "0439449"
        string reqMrn = "{\"requestId\":\"req_filter_02\",\"action\":\"filterMar\",\"payload\":{\"search\":\"0439449\"}}";
        string resMrn = await _dispatcher.HandleMessageAsync(reqMrn);
        using var docMrn = JsonDocument.Parse(resMrn);
        docMrn.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docMrn.RootElement.GetProperty("data").GetArrayLength().Should().Be(11);

        // 3. Filter by Status "Dispensed"
        string reqDisp = "{\"requestId\":\"req_filter_03\",\"action\":\"filterMar\",\"payload\":{\"status\":\"Dispensed\"}}";
        string resDisp = await _dispatcher.HandleMessageAsync(reqDisp);
        using var docDisp = JsonDocument.Parse(resDisp);
        docDisp.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docDisp.RootElement.GetProperty("data").GetArrayLength().Should().Be(13);
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_06_IpcDispatcher_GetMedicationDetail_RetrievesMonograph()
    {
        string reqJson = "{\"requestId\":\"req_med_01\",\"action\":\"getMedicationDetail\",\"payload\":\"OXANA\"}";
        string resJson = await _dispatcher.HandleMessageAsync(reqJson);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();

        var data = root.GetProperty("data");
        data.GetProperty("item_Code").GetString().Should().Be("OXANA");
        data.GetProperty("display_Name").GetString().Should().Contain("Alpraline");
        data.GetProperty("is_Controlled").GetBoolean().Should().BeTrue();
        data.GetProperty("control_Level").GetString().Should().Be("4");
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_07_IpcDispatcher_TestDbConnection_ValidAndInvalid()
    {
        // Positive probe
        string reqValid = "{\"requestId\":\"req_test_conn_01\",\"action\":\"testDbConnection\",\"payload\":null}";
        string resValid = await _dispatcher.HandleMessageAsync(reqValid);
        using var docValid = JsonDocument.Parse(resValid);
        docValid.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docValid.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeTrue();

        // Negative probe (invalid port)
        string reqInvalid = "{\"requestId\":\"req_test_conn_02\",\"action\":\"testDbConnection\",\"payload\":{\"port\":59999}}";
        string resInvalid = await _dispatcher.HandleMessageAsync(reqInvalid);
        using var docInvalid = JsonDocument.Parse(resInvalid);
        docInvalid.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docInvalid.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_08_IpcDispatcher_ExportExcel_ProducesValidFile()
    {
        string reqJson = "{\"requestId\":\"req_export_01\",\"action\":\"exportExcel\",\"payload\":{\"exportFilteredOnly\":false}}";
        string resJson = await _dispatcher.HandleMessageAsync(reqJson);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();

        var data = root.GetProperty("data");
        string filePath = data.GetProperty("filePath").GetString()!;
        int recordCount = data.GetProperty("recordCount").GetInt32();
        int fileSize = data.GetProperty("fileSize").GetInt32();

        File.Exists(filePath).Should().BeTrue();
        recordCount.Should().Be(385);
        fileSize.Should().BeGreaterThan(8000);
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_09_IpcDispatcher_UnknownAction_ReturnsFriendlyError()
    {
        string reqJson = "{\"requestId\":\"req_err_01\",\"action\":\"nonExistentActionXYZ\",\"payload\":null}";
        string resJson = await _dispatcher.HandleMessageAsync(reqJson);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Contain("Unrecognized IPC action");
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public async Task M4_10_IpcDispatcher_MalformedJson_HandlesGracefully()
    {
        string malformed = "{ this is not valid json! }";
        string resJson = await _dispatcher.HandleMessageAsync(malformed);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Milestone4")]
    public void M4_11_PhotinoAppHost_EnsureWebAssetsPath_ResolvesIndexHtml()
    {
        string indexPath = PhotinoAppHost.EnsureWebAssetsPath();
        File.Exists(indexPath).Should().BeTrue("EnsureWebAssetsPath must return an existing index.html path");
        string content = File.ReadAllText(indexPath);
        content.Should().Contain("MedHub Inpatient Prescription & MAR Workstation");

        var winType = typeof(Photino.NET.PhotinoWindow);
        var setTempMethod = winType.GetMethod("SetTemporaryFilesPath");
        _output.WriteLine($"SetTemporaryFilesPath method: {setTempMethod}");
        foreach (var m in winType.GetMethods().Where(m => m.Name.StartsWith("Set")))
        {
            _output.WriteLine($"Fluent method: {m.Name}");
        }
    }

    #endregion
}
