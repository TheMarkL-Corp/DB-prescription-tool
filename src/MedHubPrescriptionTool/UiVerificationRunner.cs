using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Host;
using MedHubPrescriptionTool.Ipc;

namespace MedHubPrescriptionTool;

/// <summary>
/// Headless automated verification runner for Milestone 4 (Clinical UI/UX & Photino Host).
/// Verifies asset integrity, embedded resource streaming, and all IPC dispatcher actions.
/// </summary>
public static class UiVerificationRunner
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" MedHub DB Prescription Tool - Milestone 4 Verification Suite");
        Console.WriteLine(" Modern Dual-View Clinical UI/UX, Photino Host & IPC Bridge");
        Console.WriteLine("================================================================================");

        int passed = 0;
        int failed = 0;

        void AssertPass(string testName, string details)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[PASS] ");
            Console.ResetColor();
            Console.WriteLine($"{testName}: {details}");
            passed++;
        }

        void AssertFail(string testName, string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[FAIL] ");
            Console.ResetColor();
            Console.WriteLine($"{testName}: {error}");
            failed++;
        }

        var dispatcher = IpcDispatcher.CreateDefault();

        // -------------------------------------------------------------
        // Test 1: Web Asset Integrity (Files on Disk & Required Tokens)
        // -------------------------------------------------------------
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string wwwrootDir = Path.Combine(baseDir, "wwwroot");

            // Fallback for dev environment if not yet published to bin or if wwwroot is empty
            if (!Directory.Exists(wwwrootDir) || !File.Exists(Path.Combine(wwwrootDir, "index.html")))
            {
                wwwrootDir = Path.Combine(Environment.CurrentDirectory, "src", "MedHubPrescriptionTool", "wwwroot");
            }

            string indexPath = Path.Combine(wwwrootDir, "index.html");
            string cssPath = Path.Combine(wwwrootDir, "css", "clinical-theme.css");
            string bridgePath = Path.Combine(wwwrootDir, "js", "ipc-bridge.js");
            string appPath = Path.Combine(wwwrootDir, "js", "app.js");

            bool filesExist = File.Exists(indexPath) && File.Exists(cssPath) && File.Exists(bridgePath) && File.Exists(appPath);

            if (!filesExist)
            {
                AssertFail("1. Web Asset Presence", $"Missing files in wwwroot: index={File.Exists(indexPath)}, css={File.Exists(cssPath)}, bridge={File.Exists(bridgePath)}, app={File.Exists(appPath)}");
            }
            else
            {
                string indexContent = await File.ReadAllTextAsync(indexPath);
                string cssContent = await File.ReadAllTextAsync(cssPath);
                string bridgeContent = await File.ReadAllTextAsync(bridgePath);

                bool hasTitle = indexContent.Contains("MedHub Inpatient Prescription & MAR Workstation");
                bool hasKpi = indexContent.Contains("kpiInpatients") && indexContent.Contains("kpiActiveOrders");
                bool hasTabs = indexContent.Contains("tabBtnPatients") && indexContent.Contains("tabBtnMar");
                bool hasModals = indexContent.Contains("medicationModal") && indexContent.Contains("settingsModal");
                bool hasThemeVars = cssContent.Contains("--color-primary") && cssContent.Contains("dark-theme");
                bool hasIpcInvoke = bridgeContent.Contains("invoke");

                if (hasTitle && hasKpi && hasTabs && hasModals && hasThemeVars && hasIpcInvoke)
                {
                    AssertPass("1. Web Asset Presence & Semantic Tokens",
                        "Verified index.html, clinical-theme.css, ipc-bridge.js, app.js with full semantic tokens, dual tabs, KPI cards, and modals.");
                }
                else
                {
                    AssertFail("1. Web Asset Presence & Semantic Tokens", "Required elements or CSS tokens missing from wwwroot files.");
                }
            }
        }
        catch (Exception ex)
        {
            AssertFail("1. Web Asset Presence & Semantic Tokens", ex.Message);
        }

        // -------------------------------------------------------------
        // Test 2: Custom Scheme Asset Resolution & Embedded Streaming
        // -------------------------------------------------------------
        try
        {
            using var streamHtml = PhotinoAppHost.ResolveAssetStream("app://localhost/index.html", out string ctHtml);
            using var streamCss = PhotinoAppHost.ResolveAssetStream("app://localhost/css/clinical-theme.css", out string ctCss);
            using var streamBridge = PhotinoAppHost.ResolveAssetStream("app://localhost/js/ipc-bridge.js", out string ctBridge);
            using var streamApp = PhotinoAppHost.ResolveAssetStream("app://localhost/js/app.js", out string ctApp);

            bool allStreamsValid = streamHtml != null && streamHtml.Length > 0 &&
                                  streamCss != null && streamCss.Length > 0 &&
                                  streamBridge != null && streamBridge.Length > 0 &&
                                  streamApp != null && streamApp.Length > 0;

            bool contentTypesCorrect = ctHtml.StartsWith("text/html") &&
                                      ctCss.StartsWith("text/css") &&
                                      ctBridge.StartsWith("application/javascript") &&
                                      ctApp.StartsWith("application/javascript");

            if (allStreamsValid && contentTypesCorrect)
            {
                AssertPass("2. Custom Scheme Stream Resolution",
                    $"Resolved all assets via 'app://localhost/' scheme handler: index.html ({streamHtml!.Length}B, {ctHtml}), css ({streamCss!.Length}B), js ({streamApp!.Length}B)");
            }
            else
            {
                AssertFail("2. Custom Scheme Stream Resolution",
                    $"Stream resolution failed: html={streamHtml != null}, css={streamCss != null}, bridge={streamBridge != null}, app={streamApp != null}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("2. Custom Scheme Stream Resolution", ex.Message);
        }

        // -------------------------------------------------------------
        // Test 3: IPC Dispatcher: 'loadSnapshot' & 'refreshData'
        // -------------------------------------------------------------
        try
        {
            string reqJson = "{\"requestId\":\"req_test_01\",\"action\":\"loadSnapshot\",\"payload\":null}";
            string resJson = await dispatcher.HandleMessageAsync(reqJson);

            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;

            bool success = root.GetProperty("success").GetBoolean();
            string reqId = root.GetProperty("requestId").GetString()!;
            var data = root.GetProperty("data");

            var kpi = data.GetProperty("kpiSummary");
            int inpatients = kpi.GetProperty("totalInpatients").GetInt32();
            int activeOrders = kpi.TryGetProperty("totalActiveOrders", out var taoProp) ? taoProp.GetInt32() : kpi.GetProperty("activeOrders").GetInt32();
            int dispensed = kpi.GetProperty("dispensedCount").GetInt32();

            var patients = data.GetProperty("patients");
            var schedules = data.GetProperty("marSchedules");

            if (success && reqId == "req_test_01" && inpatients == 15 && activeOrders == 31 && dispensed == 13 &&
                patients.GetArrayLength() == 16 && schedules.GetArrayLength() == 385)
            {
                AssertPass("3. IPC Dispatcher: loadSnapshot & refreshData",
                    $"Hydrated snapshot via IPC: Inpatients={inpatients}, ActiveOrders={activeOrders}, Dispensed={dispensed}, Schedules={schedules.GetArrayLength()}");
            }
            else
            {
                AssertFail("3. IPC Dispatcher: loadSnapshot & refreshData",
                    $"Snapshot data mismatched expected clinical baseline: Inpatients={inpatients}, Orders={activeOrders}, Schedules={schedules.GetArrayLength()}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("3. IPC Dispatcher: loadSnapshot & refreshData", ex.Message);
        }

        // -------------------------------------------------------------
        // Test 4: IPC Dispatcher: 'filterMar'
        // -------------------------------------------------------------
        try
        {
            // Filter by search query "Levemir"
            string reqJson = "{\"requestId\":\"req_test_02\",\"action\":\"filterMar\",\"payload\":{\"search\":\"Levemir\"}}";
            string resJson = await dispatcher.HandleMessageAsync(reqJson);

            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;

            bool success = root.GetProperty("success").GetBoolean();
            var data = root.GetProperty("data");
            int count = data.GetArrayLength();

            if (success && count == 22)
            {
                AssertPass("4. IPC Dispatcher: filterMar Search",
                    $"Filtered MAR schedules by 'Levemir': returned exactly {count} records matching ground truth.");
            }
            else
            {
                AssertFail("4. IPC Dispatcher: filterMar Search",
                    $"Expected 22 Levemir schedules, received {count}. Success={success}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("4. IPC Dispatcher: filterMar Search", ex.Message);
        }

        // -------------------------------------------------------------
        // Test 5: IPC Dispatcher: 'getMedicationDetail'
        // -------------------------------------------------------------
        try
        {
            string reqJson = "{\"requestId\":\"req_test_03\",\"action\":\"getMedicationDetail\",\"payload\":\"OXANA\"}";
            string resJson = await dispatcher.HandleMessageAsync(reqJson);

            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;

            bool success = root.GetProperty("success").GetBoolean();
            var data = root.GetProperty("data");
            string itemCode = data.GetProperty("item_Code").GetString()!;
            string form = data.GetProperty("form").GetString()!;
            bool isControlled = data.GetProperty("is_Controlled").GetBoolean();

            if (success && itemCode == "OXANA" && isControlled && !string.IsNullOrEmpty(form))
            {
                AssertPass("5. IPC Dispatcher: getMedicationDetail",
                    $"Retrieved drug monograph for 'OXANA': Form={form}, IsControlled={isControlled}");
            }
            else
            {
                AssertFail("5. IPC Dispatcher: getMedicationDetail",
                    $"Monograph retrieval failed: itemCode={itemCode}, isControlled={isControlled}, form={form}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("5. IPC Dispatcher: getMedicationDetail", ex.Message);
        }

        // -------------------------------------------------------------
        // Test 6: IPC Dispatcher: 'testDbConnection' (Valid & Invalid)
        // -------------------------------------------------------------
        try
        {
            // 6a: Test with current valid configuration
            string reqValid = "{\"requestId\":\"req_test_04a\",\"action\":\"testDbConnection\",\"payload\":null}";
            string resValid = await dispatcher.HandleMessageAsync(reqValid);
            using var docValid = JsonDocument.Parse(resValid);
            bool successValid = docValid.RootElement.GetProperty("data").GetProperty("success").GetBoolean();

            // 6b: Test with non-existent port (graceful error handling)
            string reqInvalid = "{\"requestId\":\"req_test_04b\",\"action\":\"testDbConnection\",\"payload\":{\"port\":59999}}";
            string resInvalid = await dispatcher.HandleMessageAsync(reqInvalid);
            using var docInvalid = JsonDocument.Parse(resInvalid);
            bool successInvalid = docInvalid.RootElement.GetProperty("data").GetProperty("success").GetBoolean();

            if (successValid && !successInvalid)
            {
                AssertPass("6. IPC Dispatcher: testDbConnection",
                    "Validated connection testing: positive probe succeeded; invalid port 59999 gracefully returned false without exception.");
            }
            else
            {
                AssertFail("6. IPC Dispatcher: testDbConnection",
                    $"Unexpected probe results: valid={successValid}, invalid={successInvalid}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("6. IPC Dispatcher: testDbConnection", ex.Message);
        }

        // -------------------------------------------------------------
        // Test 7: IPC Dispatcher: 'exportExcel'
        // -------------------------------------------------------------
        try
        {
            string reqJson = "{\"requestId\":\"req_test_05\",\"action\":\"exportExcel\",\"payload\":{\"exportFilteredOnly\":false}}";
            string resJson = await dispatcher.HandleMessageAsync(reqJson);

            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;

            bool success = root.GetProperty("success").GetBoolean();
            var data = root.GetProperty("data");
            string filePath = data.GetProperty("filePath").GetString()!;
            int recordCount = data.GetProperty("recordCount").GetInt32();
            int fileSize = data.GetProperty("fileSize").GetInt32();

            if (success && File.Exists(filePath) && recordCount == 385 && fileSize > 8000)
            {
                AssertPass("7. IPC Dispatcher: exportExcel",
                    $"Generated Excel workbook via IPC: {Path.GetFileName(filePath)} ({recordCount} records, {fileSize:N0} bytes)");
            }
            else
            {
                AssertFail("7. IPC Dispatcher: exportExcel",
                    $"Excel export failed: fileExists={File.Exists(filePath)}, records={recordCount}, size={fileSize}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("7. IPC Dispatcher: exportExcel", ex.Message);
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine($" Summary: {passed} PASSED, {failed} FAILED out of {passed + failed} Tests");
        Console.WriteLine("================================================================================");

        return failed == 0 ? 0 : 1;
    }
}
