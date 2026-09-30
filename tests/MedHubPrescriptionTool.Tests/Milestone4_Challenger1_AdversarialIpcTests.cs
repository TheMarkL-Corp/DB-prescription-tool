using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Ipc;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;
using Xunit;
using Xunit.Abstractions;

namespace MedHubPrescriptionTool.Tests;

/// <summary>
/// Milestone 4 Adversarial Challenger 1 Test Suite:
/// 1. Empirical verification across all 8 IPC actions.
/// 2. Stress testing with pathological requests (malformed JSON, missing properties, unknown actions, null payloads, extreme filters, unicode, SQL injection fragments).
/// 3. Multi-threaded concurrency testing with 50 simultaneous IPC requests (thread safety, race conditions, absence of deadlocks).
/// </summary>
public class Milestone4_Challenger1_AdversarialIpcTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IpcDispatcher _dispatcher;
    private readonly DbConnectionSettings _originalDbSettings;
    private readonly List<string> _tempFilesToCleanup = new();

    public Milestone4_Challenger1_AdversarialIpcTests(ITestOutputHelper output)
    {
        _output = output;
        _originalDbSettings = AppConfigurationManager.Current.Database.Clone();
        _dispatcher = IpcDispatcher.CreateDefault();
    }

    public void Dispose()
    {
        // Restore configuration to original baseline if altered during testing
        try
        {
            AppConfigurationManager.UpdateDatabaseSettings(_originalDbSettings, persist: true);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[Cleanup] Warning restoring db settings: {ex.Message}");
        }

        // Clean up any test export files
        foreach (var path in _tempFilesToCleanup)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[Cleanup] Could not delete {path}: {ex.Message}");
            }
        }
    }

    #region Part 1: All 8 IPC Actions Verification

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_01_AllActions_LoadSnapshot_HydratesCompleteDataset()
    {
        string reqJson = JsonSerializer.Serialize(new
        {
            requestId = "c1_req_load_01",
            action = "loadSnapshot",
            payload = (object?)null
        });

        string resJson = await _dispatcher.HandleMessageAsync(reqJson);
        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("requestId").GetString().Should().Be("c1_req_load_01");
        if (root.TryGetProperty("error", out var err))
        {
            err.ValueKind.Should().Be(JsonValueKind.Null);
        }

        var data = root.GetProperty("data");
        data.TryGetProperty("kpiSummary", out var kpi).Should().BeTrue();
        kpi.GetProperty("totalInpatients").GetInt32().Should().Be(15);
        kpi.GetProperty("totalActiveOrders").GetInt32().Should().Be(31);
        kpi.GetProperty("dispensedCount").GetInt32().Should().Be(13);
        kpi.GetProperty("duePendingOrders").GetInt32().Should().BeGreaterThan(0);
        kpi.GetProperty("totalPendingOrders").GetInt32().Should().Be(370);

        data.GetProperty("patients").GetArrayLength().Should().Be(16);
        data.GetProperty("marSchedules").GetArrayLength().Should().Be(385);
        data.GetProperty("activePrescriptions").GetArrayLength().Should().Be(31);
        data.GetProperty("medications").GetArrayLength().Should().Be(114);
        data.GetProperty("stations").GetArrayLength().Should().Be(4);
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_02_AllActions_RefreshData_ForcesSnapshotReload()
    {
        string reqJson = JsonSerializer.Serialize(new
        {
            requestId = "c1_req_refresh_01",
            action = "refreshData",
            payload = (object?)null
        });

        string resJson = await _dispatcher.HandleMessageAsync(reqJson);
        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("requestId").GetString().Should().Be("c1_req_refresh_01");

        var data = root.GetProperty("data");
        data.GetProperty("patients").GetArrayLength().Should().Be(16);
        data.GetProperty("marSchedules").GetArrayLength().Should().Be(385);
        data.TryGetProperty("lastRefreshedAt", out var refreshedAt).Should().BeTrue();
        refreshedAt.GetDateTime().Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(2));
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_03_AllActions_FilterMar_MultiFieldCriteria()
    {
        // 1. Search Drug "Levemir"
        var req1 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_01",
            action = "filterMar",
            payload = new { search = "Levemir" }
        });
        var res1 = await _dispatcher.HandleMessageAsync(req1);
        using var doc1 = JsonDocument.Parse(res1);
        doc1.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc1.RootElement.GetProperty("data").GetArrayLength().Should().Be(22);

        // 2. Search MRN "0439449"
        var req2 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_02",
            action = "filterMar",
            payload = new { search = "0439449" }
        });
        var res2 = await _dispatcher.HandleMessageAsync(req2);
        using var doc2 = JsonDocument.Parse(res2);
        doc2.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc2.RootElement.GetProperty("data").GetArrayLength().Should().Be(11);

        // 3. Station filter "8A"
        var req3 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_03",
            action = "filterMar",
            payload = new { station = "8A" }
        });
        var res3 = await _dispatcher.HandleMessageAsync(req3);
        using var doc3 = JsonDocument.Parse(res3);
        doc3.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc3.RootElement.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);

        // 4. Status filter "Dispensed"
        var req4 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_04",
            action = "filterMar",
            payload = new { status = "Dispensed" }
        });
        var res4 = await _dispatcher.HandleMessageAsync(req4);
        using var doc4 = JsonDocument.Parse(res4);
        doc4.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc4.RootElement.GetProperty("data").GetArrayLength().Should().Be(13);

        // 5. Status filter "Returned" (1 item in baseline)
        var req5 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_05",
            action = "filterMar",
            payload = new { status = "Returned" }
        });
        var res5 = await _dispatcher.HandleMessageAsync(req5);
        using var doc5 = JsonDocument.Parse(res5);
        doc5.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc5.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);

        // 6. High Alert / Controlled Only
        var req6 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_06",
            action = "filterMar",
            payload = new { highAlertOnly = true }
        });
        var res6 = await _dispatcher.HandleMessageAsync(req6);
        using var doc6 = JsonDocument.Parse(res6);
        doc6.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc6.RootElement.GetProperty("data").GetArrayLength().Should().Be(143);

        // 7. Empty criteria returns all 385 items
        var req7 = JsonSerializer.Serialize(new
        {
            requestId = "c1_filter_07",
            action = "filterMar",
            payload = new { }
        });
        var res7 = await _dispatcher.HandleMessageAsync(req7);
        using var doc7 = JsonDocument.Parse(res7);
        doc7.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc7.RootElement.GetProperty("data").GetArrayLength().Should().Be(385);
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_04_AllActions_GetMedicationDetail_StringAndObjectPayloads()
    {
        // 1. String payload "OXANA"
        var req1 = JsonSerializer.Serialize(new
        {
            requestId = "c1_med_01",
            action = "getMedicationDetail",
            payload = "OXANA"
        });
        var res1 = await _dispatcher.HandleMessageAsync(req1);
        using var doc1 = JsonDocument.Parse(res1);
        doc1.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var med1 = doc1.RootElement.GetProperty("data");
        med1.GetProperty("item_Code").GetString().Should().Be("OXANA");
        med1.GetProperty("is_Controlled").GetBoolean().Should().BeTrue();
        med1.GetProperty("control_Level").GetString().Should().Be("4");

        // 2. Object payload { itemCode: "OXANA" }
        var req2 = JsonSerializer.Serialize(new
        {
            requestId = "c1_med_02",
            action = "getMedicationDetail",
            payload = new { itemCode = "OXANA" }
        });
        var res2 = await _dispatcher.HandleMessageAsync(req2);
        using var doc2 = JsonDocument.Parse(res2);
        doc2.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc2.RootElement.GetProperty("data").GetProperty("item_Code").GetString().Should().Be("OXANA");

        // 3. Case-insensitive lookup "oxana"
        var req3 = JsonSerializer.Serialize(new
        {
            requestId = "c1_med_03",
            action = "getMedicationDetail",
            payload = "oxana"
        });
        var res3 = await _dispatcher.HandleMessageAsync(req3);
        using var doc3 = JsonDocument.Parse(res3);
        doc3.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc3.RootElement.GetProperty("data").GetProperty("item_Code").GetString().Should().Be("OXANA");

        // 4. Non-existent drug code (data omitted or null due to WhenWritingNull)
        var req4 = JsonSerializer.Serialize(new
        {
            requestId = "c1_med_04",
            action = "getMedicationDetail",
            payload = "NON_EXISTENT_ITEM_9999"
        });
        var res4 = await _dispatcher.HandleMessageAsync(req4);
        using var doc4 = JsonDocument.Parse(res4);
        doc4.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        if (doc4.RootElement.TryGetProperty("data", out var data4))
        {
            data4.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_05_AllActions_TestDbConnection_ValidAndInvalidProbes()
    {
        // 1. Positive probe (null payload uses current valid config)
        var req1 = JsonSerializer.Serialize(new
        {
            requestId = "c1_testdb_01",
            action = "testDbConnection",
            payload = (object?)null
        });
        var res1 = await _dispatcher.HandleMessageAsync(req1);
        using var doc1 = JsonDocument.Parse(res1);
        doc1.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc1.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeTrue();
        doc1.RootElement.GetProperty("data").GetProperty("message").GetString().Should().Contain("Connection successful");

        // 2. Positive probe with explicit valid payload
        var req2 = JsonSerializer.Serialize(new
        {
            requestId = "c1_testdb_02",
            action = "testDbConnection",
            payload = new
            {
                host = "127.0.0.1",
                port = 5433,
                database = "amisdbV2",
                username = "postgres",
                password = "Syscom@123"
            }
        });
        var res2 = await _dispatcher.HandleMessageAsync(req2);
        using var doc2 = JsonDocument.Parse(res2);
        doc2.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc2.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeTrue();

        // 3. Negative probe: invalid port (59999)
        var req3 = JsonSerializer.Serialize(new
        {
            requestId = "c1_testdb_03",
            action = "testDbConnection",
            payload = new { port = 59999 }
        });
        var res3 = await _dispatcher.HandleMessageAsync(req3);
        using var doc3 = JsonDocument.Parse(res3);
        doc3.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc3.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeFalse();

        // 4. Negative probe: invalid host
        var req4 = JsonSerializer.Serialize(new
        {
            requestId = "c1_testdb_04",
            action = "testDbConnection",
            payload = new { host = "256.256.256.256" }
        });
        var res4 = await _dispatcher.HandleMessageAsync(req4);
        using var doc4 = JsonDocument.Parse(res4);
        doc4.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc4.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeFalse();

        // 5. Negative probe: invalid credentials
        var req5 = JsonSerializer.Serialize(new
        {
            requestId = "c1_testdb_05",
            action = "testDbConnection",
            payload = new { password = "WrongPassword!999" }
        });
        var res5 = await _dispatcher.HandleMessageAsync(req5);
        using var doc5 = JsonDocument.Parse(res5);
        doc5.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc5.RootElement.GetProperty("data").GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_06_AllActions_SaveDbSettings_ValidExecutionAndRollback()
    {
        // 1. Missing payload -> fails with clear message
        var reqNull = JsonSerializer.Serialize(new
        {
            requestId = "c1_save_01",
            action = "saveDbSettings",
            payload = (object?)null
        });
        var resNull = await _dispatcher.HandleMessageAsync(reqNull);
        using var docNull = JsonDocument.Parse(resNull);
        docNull.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        docNull.RootElement.GetProperty("error").GetString().Should().Contain("Payload is required");

        // 2. Valid save with identical or safe parameters
        try
        {
            var reqValid = JsonSerializer.Serialize(new
            {
                requestId = "c1_save_02",
                action = "saveDbSettings",
                payload = new
                {
                    host = "127.0.0.1",
                    port = 5433,
                    database = "amisdbV2",
                    username = "postgres",
                    password = "Syscom@123"
                }
            });

            var resValid = await _dispatcher.HandleMessageAsync(reqValid);
            using var docValid = JsonDocument.Parse(resValid);
            docValid.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var data = docValid.RootElement.GetProperty("data");
            data.GetProperty("saved").GetBoolean().Should().BeTrue();
            data.TryGetProperty("snapshot", out var snapshot).Should().BeTrue();
            snapshot.GetProperty("marSchedules").GetArrayLength().Should().Be(385);

            // 3. Invalid credentials/port fails connection test and does NOT alter configuration on disk
            var baselinePort = AppConfigurationManager.Current.Database.Port;
            var reqInvalid = JsonSerializer.Serialize(new
            {
                requestId = "c1_save_03_invalid",
                action = "saveDbSettings",
                payload = new
                {
                    host = "127.0.0.1",
                    port = 59999,
                    database = "amisdbV2",
                    username = "postgres",
                    password = "BadPassword"
                }
            });
            var resInvalid = await _dispatcher.HandleMessageAsync(reqInvalid);
            using var docInvalid = JsonDocument.Parse(resInvalid);
            docInvalid.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            docInvalid.RootElement.GetProperty("error").GetString().Should().Contain("Database connection test failed. Settings were not saved.");
            AppConfigurationManager.Current.Database.Port.Should().Be(baselinePort);
        }
        finally
        {
            // Restore configuration baseline
            AppConfigurationManager.UpdateDatabaseSettings(_originalDbSettings, persist: true);
        }
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_07_AllActions_ExportExcel_FullAndFilteredScopes()
    {
        // 1. Full Export
        var reqFull = JsonSerializer.Serialize(new
        {
            requestId = "c1_export_01",
            action = "exportExcel",
            payload = new { exportFilteredOnly = false }
        });

        var resFull = await _dispatcher.HandleMessageAsync(reqFull);
        using var docFull = JsonDocument.Parse(resFull);
        docFull.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var dataFull = docFull.RootElement.GetProperty("data");
        string pathFull = dataFull.GetProperty("filePath").GetString()!;
        _tempFilesToCleanup.Add(pathFull);

        File.Exists(pathFull).Should().BeTrue();
        dataFull.GetProperty("recordCount").GetInt32().Should().Be(385);
        dataFull.GetProperty("fileSize").GetInt32().Should().BeGreaterThan(8000);

        // 2. Filtered Export (Count only fallback)
        var reqFiltered = JsonSerializer.Serialize(new
        {
            requestId = "c1_export_02",
            action = "exportExcel",
            payload = new { exportFilteredOnly = true, filteredCount = 22 }
        });

        var resFiltered = await _dispatcher.HandleMessageAsync(reqFiltered);
        using var docFiltered = JsonDocument.Parse(resFiltered);
        docFiltered.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var dataFiltered = docFiltered.RootElement.GetProperty("data");
        string pathFiltered = dataFiltered.GetProperty("filePath").GetString()!;
        _tempFilesToCleanup.Add(pathFiltered);

        File.Exists(pathFiltered).Should().BeTrue();
        dataFiltered.GetProperty("recordCount").GetInt32().Should().BeGreaterThan(0);
        dataFiltered.GetProperty("fileSize").GetInt32().Should().BeGreaterThan(8000);

        // 3. Filtered Export with Explicit FilteredScheduleIds
        var snapRes = await _dispatcher.HandleMessageAsync("{\"action\":\"loadSnapshot\"}");
        using var snapDoc = JsonDocument.Parse(snapRes);
        var schedules = snapDoc.RootElement.GetProperty("data").GetProperty("marSchedules");
        var selectedIds = new List<long>();
        for (int i = 0; i < Math.Min(5, schedules.GetArrayLength()); i++)
        {
            selectedIds.Add(schedules[i].GetProperty("scheduleId").GetInt64());
        }

        var reqWithIds = JsonSerializer.Serialize(new
        {
            requestId = "c1_export_03_ids",
            action = "exportExcel",
            payload = new
            {
                exportFilteredOnly = true,
                filteredCount = selectedIds.Count,
                filteredScheduleIds = selectedIds
            }
        });

        var resWithIds = await _dispatcher.HandleMessageAsync(reqWithIds);
        using var docWithIds = JsonDocument.Parse(resWithIds);
        docWithIds.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var dataWithIds = docWithIds.RootElement.GetProperty("data");
        string pathWithIds = dataWithIds.GetProperty("filePath").GetString()!;
        _tempFilesToCleanup.Add(pathWithIds);

        File.Exists(pathWithIds).Should().BeTrue();
        dataWithIds.GetProperty("recordCount").GetInt32().Should().Be(selectedIds.Count);
        dataWithIds.GetProperty("fileSize").GetInt32().Should().BeGreaterThan(8000);
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_08_AllActions_GetAppSettings_ReturnsLiveConfig()
    {
        var req = JsonSerializer.Serialize(new
        {
            requestId = "c1_settings_01",
            action = "getAppSettings",
            payload = (object?)null
        });

        var res = await _dispatcher.HandleMessageAsync(req);
        using var doc = JsonDocument.Parse(res);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("requestId").GetString().Should().Be("c1_settings_01");

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("host").GetString().Should().Be("127.0.0.1");
        data.GetProperty("port").GetInt32().Should().Be(5433);
        data.GetProperty("database").GetString().Should().Be("amisdbV2");
        data.GetProperty("username").GetString().Should().Be("postgres");
        data.GetProperty("timeoutSeconds").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("theme").GetString().Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Part 2: Pathological Requests Stress Testing

    [Theory]
    [InlineData("")]
    [InlineData("   \t\r\n   ")]
    [InlineData("{")]
    [InlineData("{\"requestId\": \"r1\", \"action\": \"loadSnapshot\"")]
    [InlineData("{ action: 'loadSnapshot' }")]
    [InlineData("Not JSON at all")]
    [InlineData("{\"action\": \"loadSnapshot\", \"payload\": ]")]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_09a_Pathological_MalformedJson_HandlesGracefully(string malformed)
    {
        string resJson = await _dispatcher.HandleMessageAsync(malformed);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("12345")]
    [InlineData("true")]
    [InlineData("null")]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_09b_Pathological_NonObjectJson_HandlesGracefully(string nonObjectJson)
    {
        string resJson = await _dispatcher.HandleMessageAsync(nonObjectJson);

        using var doc = JsonDocument.Parse(resJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_09c_Pathological_DeeplyNestedAndMassiveJson()
    {
        // 1. Deeply nested JSON
        string nested = "{\"a\":{\"b\":{\"c\":{\"d\":{\"requestId\":\"deep_01\",\"action\":\"loadSnapshot\"}}}}}";
        string resNested = await _dispatcher.HandleMessageAsync(nested);
        using var docNested = JsonDocument.Parse(resNested);
        // Root element lacks action at top level -> should fail cleanly
        docNested.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();

        // 2. Massive JSON with 2,000 dummy properties
        var dict = new Dictionary<string, object>
        {
            ["requestId"] = "massive_01",
            ["action"] = "getAppSettings"
        };
        for (int i = 0; i < 2000; i++)
        {
            dict[$"dummy_prop_{i}"] = $"dummy_value_{i}";
        }
        string massive = JsonSerializer.Serialize(dict);
        string resMassive = await _dispatcher.HandleMessageAsync(massive);
        using var docMassive = JsonDocument.Parse(resMassive);
        docMassive.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docMassive.RootElement.GetProperty("requestId").GetString().Should().Be("massive_01");
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_10_Pathological_MissingProperties_DefaultsGracefully()
    {
        // 1. Missing requestId -> defaults to req_unknown
        string reqNoId = "{\"action\":\"getAppSettings\"}";
        string resNoId = await _dispatcher.HandleMessageAsync(reqNoId);
        using var docNoId = JsonDocument.Parse(resNoId);
        docNoId.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docNoId.RootElement.GetProperty("requestId").GetString().Should().Be("req_unknown");

        // 2. Missing action -> unrecognized action error
        string reqNoAct = "{\"requestId\":\"req_no_act\"}";
        string resNoAct = await _dispatcher.HandleMessageAsync(reqNoAct);
        using var docNoAct = JsonDocument.Parse(resNoAct);
        docNoAct.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        docNoAct.RootElement.GetProperty("error").GetString().Should().Contain("Unrecognized IPC action: ''");

        // 3. Empty action string -> unrecognized action error
        string reqEmptyAct = "{\"requestId\":\"req_empty_act\",\"action\":\"\"}";
        string resEmptyAct = await _dispatcher.HandleMessageAsync(reqEmptyAct);
        using var docEmptyAct = JsonDocument.Parse(resEmptyAct);
        docEmptyAct.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();

        // 4. Missing payload on filterMar -> defaults to empty criteria and succeeds
        string reqNoPay = "{\"requestId\":\"req_no_pay\",\"action\":\"filterMar\"}";
        string resNoPay = await _dispatcher.HandleMessageAsync(reqNoPay);
        using var docNoPay = JsonDocument.Parse(resNoPay);
        docNoPay.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docNoPay.RootElement.GetProperty("data").GetArrayLength().Should().Be(385);

        // 5. Numeric requestId instead of string -> handled safely
        string reqNumId = "{\"requestId\":12345,\"action\":\"loadSnapshot\"}";
        string resNumId = await _dispatcher.HandleMessageAsync(reqNumId);
        using var docNumId = JsonDocument.Parse(resNumId);
        // Expect graceful error response (not host crash)
        docNumId.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("UNKNOWN_ACTION_XYZ")]
    [InlineData("DROP_TABLE_PATIENTS")]
    [InlineData("SELECT * FROM Encounter")]
    [InlineData(" loadSnapshot ")] // Leading/trailing whitespace
    [InlineData("LOADSNAPSHOT")]    // Uppercase
    [InlineData("loadsnapshot")]    // Lowercase
    [InlineData("filterMAR")]       // CamelCase mismatch
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_11_Pathological_UnknownActions_ReturnsClearErrors(string unknownAction)
    {
        string reqJson = JsonSerializer.Serialize(new
        {
            requestId = "c1_unknown_act",
            action = unknownAction,
            payload = (object?)null
        });

        string resJson = await _dispatcher.HandleMessageAsync(reqJson);
        using var doc = JsonDocument.Parse(resJson);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Unrecognized IPC action");
    }

    [Theory]
    [InlineData("loadSnapshot", true)]
    [InlineData("refreshData", true)]
    [InlineData("getAppSettings", true)]
    [InlineData("filterMar", true)]
    [InlineData("getMedicationDetail", true)]
    [InlineData("testDbConnection", true)]
    [InlineData("exportExcel", true)]
    [InlineData("saveDbSettings", false)] // saveDbSettings strictly requires payload
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_12_Pathological_NullPayloads_HandledPerActionSpecification(string action, bool expectedSuccess)
    {
        string reqJson = JsonSerializer.Serialize(new
        {
            requestId = $"c1_null_pay_{action}",
            action = action,
            payload = (object?)null
        });

        string resJson = await _dispatcher.HandleMessageAsync(reqJson);
        using var doc = JsonDocument.Parse(resJson);
        doc.RootElement.GetProperty("success").GetBoolean().Should().Be(expectedSuccess);

        if (action == "exportExcel")
        {
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var filePath = doc.RootElement.GetProperty("data").GetProperty("filePath").GetString();
                if (filePath != null) _tempFilesToCleanup.Add(filePath);
            }
        }
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_13_Pathological_ExtremeFilterCriteria_Resilient()
    {
        // 1. 10,000-character search query
        string hugeQuery = new string('A', 10000);
        var reqHuge = JsonSerializer.Serialize(new
        {
            requestId = "c1_huge_01",
            action = "filterMar",
            payload = new { search = hugeQuery }
        });
        var sw = Stopwatch.StartNew();
        var resHuge = await _dispatcher.HandleMessageAsync(reqHuge);
        sw.Stop();
        using var docHuge = JsonDocument.Parse(resHuge);
        docHuge.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docHuge.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);
        sw.ElapsedMilliseconds.Should().BeLessThan(500, "10,000-char query must evaluate quickly without regex catastrophic backtracking");

        // 2. Regex special characters
        string regexChars = @".*+?^$()[]{}|\\";
        var reqRegex = JsonSerializer.Serialize(new
        {
            requestId = "c1_regex_01",
            action = "filterMar",
            payload = new { search = regexChars }
        });
        var resRegex = await _dispatcher.HandleMessageAsync(reqRegex);
        using var docRegex = JsonDocument.Parse(resRegex);
        docRegex.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docRegex.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);

        // 3. XSS / HTML injection in search query
        string xssQuery = "<script>alert('XSS')</script><img src=x onerror=alert(1)>";
        var reqXss = JsonSerializer.Serialize(new
        {
            requestId = "c1_xss_01",
            action = "filterMar",
            payload = new { search = xssQuery }
        });
        var resXss = await _dispatcher.HandleMessageAsync(reqXss);
        using var docXss = JsonDocument.Parse(resXss);
        docXss.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docXss.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);

        // 4. Mismatched payload types: search as number
        string reqBadType1 = "{\"requestId\":\"bad_t1\",\"action\":\"filterMar\",\"payload\":{\"search\":12345}}";
        string resBadType1 = await _dispatcher.HandleMessageAsync(reqBadType1);
        using var docBadType1 = JsonDocument.Parse(resBadType1);
        // Expect graceful error response (not unhandled exception crash)
        docBadType1.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();

        // 5. Mismatched payload types: highAlertOnly as string instead of boolean
        string reqBadType2 = "{\"requestId\":\"bad_t2\",\"action\":\"filterMar\",\"payload\":{\"highAlertOnly\":\"true\"}}";
        string resBadType2 = await _dispatcher.HandleMessageAsync(reqBadType2);
        using var docBadType2 = JsonDocument.Parse(resBadType2);
        docBadType2.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("病人禁食", 0)]     // Chinese: Patient Fasting (not in searched fields)
    [InlineData("胰島素", 0)]       // Chinese: Insulin (not directly in English display name)
    [InlineData("アスピリン", 0)]    // Japanese Katakana: Aspirin
    [InlineData("الأنسولين", 0)]   // Arabic RTL
    [InlineData("💊", 0)]          // Pill Emoji
    [InlineData("🏥", 0)]          // Hospital Emoji
    [InlineData("Levemir\u200B", 0)] // Zero-width space
    [InlineData("Levemir", 22)]    // Valid baseline
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_14_Pathological_UnicodeAndInternationalSearchTerms(string query, int expectedCount)
    {
        var req = JsonSerializer.Serialize(new
        {
            requestId = "c1_unicode_01",
            action = "filterMar",
            payload = new { search = query }
        });

        var res = await _dispatcher.HandleMessageAsync(req);
        using var doc = JsonDocument.Parse(res);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetArrayLength().Should().Be(expectedCount);
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("'; DROP TABLE Prescriptions; --")]
    [InlineData("' UNION SELECT NULL, NULL, NULL, NULL --")]
    [InlineData("admin'--")]
    [InlineData("1; SELECT pg_sleep(5); --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("')) OR ((('1'='1")]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_15_Pathological_SqlInjectionFragments_SafelyEscapedAndNoMutation(string sqlInjection)
    {
        // 1. Send as search query via filterMar
        var reqSearch = JsonSerializer.Serialize(new
        {
            requestId = "c1_sqli_filter",
            action = "filterMar",
            payload = new { search = sqlInjection }
        });
        var resSearch = await _dispatcher.HandleMessageAsync(reqSearch);
        using var docSearch = JsonDocument.Parse(resSearch);
        docSearch.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        // Should match 0 records and not throw or execute SQL
        docSearch.RootElement.GetProperty("data").GetArrayLength().Should().Be(0);

        // 2. Send as itemCode via getMedicationDetail
        var reqMed = JsonSerializer.Serialize(new
        {
            requestId = "c1_sqli_med",
            action = "getMedicationDetail",
            payload = sqlInjection
        });
        var resMed = await _dispatcher.HandleMessageAsync(reqMed);
        using var docMed = JsonDocument.Parse(resMed);
        docMed.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        if (docMed.RootElement.TryGetProperty("data", out var medData))
        {
            medData.ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 3. Verify database integrity is completely intact
        var reqVerify = JsonSerializer.Serialize(new
        {
            requestId = "c1_sqli_verify",
            action = "loadSnapshot",
            payload = (object?)null
        });
        var resVerify = await _dispatcher.HandleMessageAsync(reqVerify);
        using var docVerify = JsonDocument.Parse(resVerify);
        docVerify.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        docVerify.RootElement.GetProperty("data").GetProperty("marSchedules").GetArrayLength().Should().Be(385);
        docVerify.RootElement.GetProperty("data").GetProperty("patients").GetArrayLength().Should().Be(16);
    }

    #endregion

    #region Part 3: Concurrency & Deadlock Stress Testing (50 Concurrent Requests)

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_16_Concurrency_50MixedRequests_ThreadSafetyAndNoDeadlock()
    {
        const int totalRequests = 50;
        var tasks = new List<Task<string>>(totalRequests);
        var expectedRequestIds = new HashSet<string>();

        // Warm up snapshot first to test steady-state concurrent dispatch
        await _dispatcher.HandleMessageAsync(JsonSerializer.Serialize(new
        {
            requestId = "warmup",
            action = "loadSnapshot",
            payload = (object?)null
        }));

        for (int i = 0; i < totalRequests; i++)
        {
            string reqId = $"concurrent_req_{i:D3}";
            expectedRequestIds.Add(reqId);

            int branch = i % 5;
            string json = branch switch
            {
                0 => JsonSerializer.Serialize(new
                {
                    requestId = reqId,
                    action = "filterMar",
                    payload = new { search = i % 2 == 0 ? "Levemir" : "0439449" }
                }),
                1 => JsonSerializer.Serialize(new
                {
                    requestId = reqId,
                    action = "getMedicationDetail",
                    payload = i % 2 == 0 ? "OXANA" : "LEVE"
                }),
                2 => JsonSerializer.Serialize(new
                {
                    requestId = reqId,
                    action = "getAppSettings",
                    payload = (object?)null
                }),
                3 => JsonSerializer.Serialize(new
                {
                    requestId = reqId,
                    action = "loadSnapshot",
                    payload = (object?)null
                }),
                _ => JsonSerializer.Serialize(new
                {
                    requestId = reqId,
                    action = "filterMar",
                    payload = new { highAlertOnly = true }
                })
            };

            // Dispatch on thread pool
            tasks.Add(Task.Run(() => _dispatcher.HandleMessageAsync(json)));
        }

        // Must complete within 20 seconds; failure indicates deadlock
        var allTask = Task.WhenAll(tasks);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20));
        var completed = await Task.WhenAny(allTask, timeoutTask);

        completed.Should().Be(allTask, "50 concurrent requests must complete without deadlocks within 20 seconds");

        string[] results = await allTask;
        results.Length.Should().Be(totalRequests);

        foreach (var resJson in results)
        {
            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;
            root.GetProperty("success").GetBoolean().Should().BeTrue();
            string reqId = root.GetProperty("requestId").GetString()!;
            expectedRequestIds.Should().Contain(reqId, "Response requestId must match the dispatched request");
        }
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_17_Concurrency_ColdStartStampede_ThreadSafeHydration()
    {
        // Test cold start cache stampede on an unhydrated dispatcher
        var freshDispatcher = IpcDispatcher.CreateDefault();
        const int concurrency = 50;

        using var startBarrier = new ManualResetEventSlim(false);
        var tasks = new List<Task<string>>(concurrency);

        for (int i = 0; i < concurrency; i++)
        {
            string reqId = $"stampede_{i:D3}";
            string reqJson = (i % 2 == 0)
                ? JsonSerializer.Serialize(new { requestId = reqId, action = "loadSnapshot", payload = (object?)null })
                : JsonSerializer.Serialize(new { requestId = reqId, action = "filterMar", payload = new { search = "Levemir" } });

            tasks.Add(Task.Run(async () =>
            {
                // Align all 50 threads to release simultaneously
                startBarrier.Wait();
                return await freshDispatcher.HandleMessageAsync(reqJson);
            }));
        }

        // Release all threads simultaneously
        startBarrier.Set();

        var allTask = Task.WhenAll(tasks);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(25));
        var completed = await Task.WhenAny(allTask, timeoutTask);

        completed.Should().Be(allTask, "Cold-start 50 concurrent requests must resolve cleanly without deadlock or race condition");

        string[] results = await allTask;
        results.Length.Should().Be(concurrency);

        int loadCount = 0;
        int filterCount = 0;
        foreach (var resJson in results)
        {
            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;
            root.GetProperty("success").GetBoolean().Should().BeTrue("All stampede requests must succeed");

            var data = root.GetProperty("data");
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("kpiSummary", out _))
            {
                loadCount++;
                data.GetProperty("marSchedules").GetArrayLength().Should().Be(385);
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                filterCount++;
                data.GetArrayLength().Should().Be(22);
            }
        }

        loadCount.Should().Be(25);
        filterCount.Should().Be(25);
    }

    [Fact]
    [Trait("Category", "Challenger_M4_1")]
    public async Task C1_18_Concurrency_PathologicalParallelFuzzing()
    {
        const int fuzzCount = 50;
        var tasks = new List<Task<string>>(fuzzCount);

        string[] pathologicalPayloads = new[]
        {
            "INVALID_JSON_RAW_STRING",
            "{\"requestId\":\"fuzz_01\",\"action\":\"UNKNOWN_ACTION\"}",
            "{\"requestId\":\"fuzz_02\",\"action\":\"filterMar\",\"payload\":{\"search\":\"' OR '1'='1\"}}",
            "{\"requestId\":\"fuzz_03\",\"action\":\"filterMar\",\"payload\":{\"search\":\"💊 🏥 💉\"}}",
            "{\"requestId\":\"fuzz_04\",\"action\":\"getMedicationDetail\",\"payload\":\"'; DROP TABLE Test; --\"}",
            "{\"requestId\":\"fuzz_05\",\"action\":\"testDbConnection\",\"payload\":{\"port\":59999}}",
            "{\"requestId\":\"fuzz_06\",\"action\":\"saveDbSettings\",\"payload\":null}",
            "{\"requestId\":\"fuzz_07\",\"action\":\"loadSnapshot\",\"payload\":null}",
            "{\"requestId\":\"fuzz_08\",\"action\":\"getAppSettings\"}",
            "[1, 2, 3, 4, 5]"
        };

        for (int i = 0; i < fuzzCount; i++)
        {
            string payload = pathologicalPayloads[i % pathologicalPayloads.Length];
            tasks.Add(Task.Run(() => _dispatcher.HandleMessageAsync(payload)));
        }

        var allTask = Task.WhenAll(tasks);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20));
        var completed = await Task.WhenAny(allTask, timeoutTask);

        completed.Should().Be(allTask, "Parallel fuzzing of 50 pathological messages must not hang or deadlock");

        string[] results = await allTask;
        results.Length.Should().Be(fuzzCount);

        // Verify each returned valid JSON response with a success or failure boolean
        foreach (var resJson in results)
        {
            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;
            root.TryGetProperty("success", out var successProp).Should().BeTrue();
            successProp.ValueKind.Should().Match(k => k == JsonValueKind.True || k == JsonValueKind.False);
        }
    }

    #endregion
}
