using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;

namespace MedHubPrescriptionTool.ChallengerM2Harness;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" CHALLENGER 1 (M2) EMPIRICAL TEST SUITE & KPI VERIFICATION HARNESS");
        Console.WriteLine(" Target Database: 127.0.0.1:5433/amisdbV2");
        Console.WriteLine(" Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Console.WriteLine("================================================================================");

        var config = AppConfigurationManager.Current;
        var factory = new DatabaseConnectionFactory(config.Database);
        var repo = new DatabaseRepository(factory);
        var service = new ClinicalDataService(repo);

        // 1. Connection check
        await TestDatabaseConnectivity(repo, config.Database);

        // 2. Snapshot Loading
        var snapshot = await TestSnapshotLoading(service);

        // 3. Exact KPI Metrics Assertions
        await TestKpiMetrics(service, repo, snapshot);

        // 4. Station Patient Grouping Assertions
        TestStationGroupings(service, snapshot);

        // 5. Reference Time Drift Deep Dive (12:00:00 vs Wall Clock)
        await TestReferenceTimeDrift(repo, snapshot);

        // 6. Status Precedence Invariants
        TestStatusPrecedence();

        // 7. Verify Worker M2's Built-in Verification Runner
        await TestWorkerVerificationRunner();

        Console.WriteLine("================================================================================");
        Console.WriteLine($" SUMMARY: {_passed} PASSED, {_failed} FAILED");
        Console.WriteLine("================================================================================");

        return _failed > 0 ? 1 : 0;
    }

    private static void Pass(string testName, string details)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS] ");
        Console.ResetColor();
        Console.WriteLine($"{testName}: {details}");
        _passed++;
    }

    private static void Fail(string testName, string details)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL] ");
        Console.ResetColor();
        Console.WriteLine($"{testName}: {details}");
        _failed++;
    }

    private static async Task TestDatabaseConnectivity(DatabaseRepository repo, DbConnectionSettings settings)
    {
        var testRes = await repo.TestConnectionDetailsAsync(settings);
        if (testRes.Success && testRes.IsReadOnly)
        {
            Pass("T01_Connection_Handshake", $"Connected to {settings.Database} in {testRes.LatencyMs}ms (Version: {testRes.ServerVersion}, IsReadOnly: True)");
        }
        else
        {
            Fail("T01_Connection_Handshake", $"Connection failed or not read-only: {testRes.ErrorMessage}");
        }
    }

    private static async Task<ClinicalDataSnapshot> TestSnapshotLoading(ClinicalDataService service)
    {
        var sw = Stopwatch.StartNew();
        var snapshot = await service.LoadClinicalSnapshotAsync();
        sw.Stop();

        if (snapshot.Patients.Count == 16 &&
            snapshot.MarSchedules.Count == 385 &&
            snapshot.ActivePrescriptions.Count == 31 &&
            snapshot.Medications.Count == 114 &&
            snapshot.Stations.Count == 4 &&
            snapshot.Beds.Count == 19)
        {
            Pass("T02_Snapshot_Consolidation", $"Hydrated 16 encounters, 385 MAR schedules, 31 active prescriptions, 114 medications, 4 stations, 19 beds in {sw.ElapsedMilliseconds}ms");
        }
        else
        {
            Fail("T02_Snapshot_Consolidation", $"Entity counts mismatch: Patients={snapshot.Patients.Count} (exp 16), MAR={snapshot.MarSchedules.Count} (exp 385), Rx={snapshot.ActivePrescriptions.Count} (exp 31), Meds={snapshot.Medications.Count} (exp 114)");
        }

        return snapshot;
    }

    private static async Task TestKpiMetrics(ClinicalDataService service, DatabaseRepository repo, ClinicalDataSnapshot snapshot)
    {
        var kpi = service.ComputeKpiSummary(snapshot);
        var sqlKpi = await repo.GetKpiCountsAsync();

        // Check Inpatients: 15 distinct patients across 16 encounters
        if (kpi.TotalInpatients == 15 && sqlKpi.total_inpatients == 15)
            Pass("T03_Kpi_Inpatients", $"TotalInpatients = 15 (Service: {kpi.TotalInpatients}, SQL: {sqlKpi.total_inpatients})");
        else
            Fail("T03_Kpi_Inpatients", $"TotalInpatients mismatch! Exp: 15, Got: {kpi.TotalInpatients}");

        // Check ActiveOrders: 31 active prescriptions
        if (kpi.TotalActiveOrders == 31 && sqlKpi.total_active_orders == 31)
            Pass("T04_Kpi_ActiveOrders", $"TotalActiveOrders = 31 (Service: {kpi.TotalActiveOrders}, SQL: {sqlKpi.total_active_orders})");
        else
            Fail("T04_Kpi_ActiveOrders", $"TotalActiveOrders mismatch! Exp: 31, Got: {kpi.TotalActiveOrders}");

        // Check Dispensed: 13 confirmed doses
        if (kpi.DispensedCount == 13 && sqlKpi.total_dispensed == 13)
            Pass("T05_Kpi_Dispensed", $"DispensedCount = 13 (Service: {kpi.DispensedCount}, SQL: {sqlKpi.total_dispensed})");
        else
            Fail("T05_Kpi_Dispensed", $"DispensedCount mismatch! Exp: 13, Got: {kpi.DispensedCount}");

        // Check TotalPending: 370 total unadministered pending doses
        if (kpi.TotalPendingOrders == 370 && sqlKpi.total_pending_orders == 370)
            Pass("T06_Kpi_TotalPending", $"TotalPendingOrders = 370 (Service: {kpi.TotalPendingOrders}, SQL: {sqlKpi.total_pending_orders})");
        else
            Fail("T06_Kpi_TotalPending", $"TotalPendingOrders mismatch! Exp: 370, Got: {kpi.TotalPendingOrders}");

        // Check Returned: 1 dose
        if (kpi.ReturnedCount == 1)
            Pass("T07_Kpi_Returned", $"ReturnedCount = 1");
        else
            Fail("T07_Kpi_Returned", $"ReturnedCount mismatch! Exp: 1, Got: {kpi.ReturnedCount}");

        // Check Wasted: 1 dose
        if (kpi.WastedCount == 1)
            Pass("T08_Kpi_Wasted", $"WastedCount = 1");
        else
            Fail("T08_Kpi_Wasted", $"WastedCount mismatch! Exp: 1, Got: {kpi.WastedCount}");

        // Check Controlled: 143 scheduled doses
        if (kpi.ControlledCount == 143)
            Pass("T09_Kpi_Controlled", $"ControlledCount = 143");
        else
            Fail("T09_Kpi_Controlled", $"ControlledCount mismatch! Exp: 143, Got: {kpi.ControlledCount}");

        // Check Total Schedules Partition Invariant: Dispensed(13) + Returned(1) + Wasted(1) + TotalPending(370) = 385
        int totalCalculated = kpi.DispensedCount + kpi.ReturnedCount + kpi.WastedCount + kpi.TotalPendingOrders;
        if (totalCalculated == 385 && snapshot.MarSchedules.Count == 385)
            Pass("T10_Kpi_PartitionInvariant", $"Mutually exclusive status partition sum: {totalCalculated} == 385 total MAR schedules");
        else
            Fail("T10_Kpi_PartitionInvariant", $"Partition sum mismatch: {totalCalculated} != 385");

        // Report DuePending and FuturePending actual vs target
        Console.WriteLine($"   [DIAGNOSTIC] Current Dynamic DuePending: {kpi.DuePendingOrders}, FuturePending: {kpi.FuturePendingOrders} (Sum: {kpi.DuePendingOrders + kpi.FuturePendingOrders})");
        Console.WriteLine($"   [DIAGNOSTIC] Dispatch Target: DuePending=286, FuturePending=84");
        if ((kpi.DuePendingOrders + kpi.FuturePendingOrders) == 370)
        {
            Pass("T11_Kpi_DueAndFuture_Invariant", $"DuePending ({kpi.DuePendingOrders}) + FuturePending ({kpi.FuturePendingOrders}) == 370 invariant satisfied");
        }
        else
        {
            Fail("T11_Kpi_DueAndFuture_Invariant", $"Sum mismatch: {kpi.DuePendingOrders + kpi.FuturePendingOrders} != 370");
        }
    }

    private static void TestStationGroupings(ClinicalDataService service, ClinicalDataSnapshot snapshot)
    {
        var group8A = service.GetPatientOverviewList(snapshot, "8A");
        var group5A = service.GetPatientOverviewList(snapshot, "5A");
        var groupICU = service.GetPatientOverviewList(snapshot, "ICU");

        if (group8A.Count == 7)
            Pass("T12_Station_8A", $"Station 8A contains exactly 7 encounters");
        else
            Fail("T12_Station_8A", $"Station 8A count mismatch! Exp: 7, Got: {group8A.Count}");

        if (group5A.Count == 6)
            Pass("T13_Station_5A", $"Station 5A contains exactly 6 encounters");
        else
            Fail("T13_Station_5A", $"Station 5A count mismatch! Exp: 6, Got: {group5A.Count}");

        if (groupICU.Count == 3)
            Pass("T14_Station_ICU", $"Station ICU contains exactly 3 encounters");
        else
            Fail("T14_Station_ICU", $"Station ICU count mismatch! Exp: 3, Got: {groupICU.Count}");

        int sumEncounters = group8A.Count + group5A.Count + groupICU.Count;
        if (sumEncounters == 16)
            Pass("T15_Station_Sum", $"Sum of station encounters: 7 + 6 + 3 = 16 encounters");
        else
            Fail("T15_Station_Sum", $"Sum mismatch: {sumEncounters} != 16");

        // Case-insensitivity check
        var group8aLower = service.GetPatientOverviewList(snapshot, "8a");
        var groupIcuLower = service.GetPatientOverviewList(snapshot, "icu");
        if (group8aLower.Count == 7 && groupIcuLower.Count == 3)
            Pass("T16_Station_CaseInsensitive", $"Case-insensitive filtering: '8a' -> 7, 'icu' -> 3");
        else
            Fail("T16_Station_CaseInsensitive", $"Case-insensitive filtering failed");
    }

    private static async Task TestReferenceTimeDrift(DatabaseRepository repo, ClinicalDataSnapshot snapshot)
    {
        Console.WriteLine("\n--- EMPIRICAL DEEP DIVE: REFERENCE TIME DRIFT ANALYSIS ---");

        var rawMar = await repo.GetMarScheduleDataAsync();
        var baseTime12Noon = new DateTime(2026, 9, 24, 12, 0, 0);

        var statusesAt12 = rawMar.Select(r => ClinicalStatusEvaluator.EvaluateStatus(r, baseTime12Noon)).ToList();

        int dueAt12 = statusesAt12.Count(s => s == ClinicalStatusEvaluator.StatusPendingDue);
        int futureAt12 = statusesAt12.Count(s => s == ClinicalStatusEvaluator.StatusPendingFuture);

        Console.WriteLine($"When ReferenceTime = 2026-09-24 12:00:00:");
        Console.WriteLine($"   DuePending:    {dueAt12} (Matches target: 286)");
        Console.WriteLine($"   FuturePending: {futureAt12} (Matches target: 84)");
        Console.WriteLine($"   TotalPending:  {dueAt12 + futureAt12} (Matches target: 370)");

        if (dueAt12 == 286 && futureAt12 == 84)
        {
            Pass("T17_ReferenceTime_12Noon_Proof", $"Deterministic evaluation at 2026-09-24 12:00:00 produces EXACTLY DuePending=286 and FuturePending=84");
        }
        else
        {
            Fail("T17_ReferenceTime_12Noon_Proof", $"Evaluation at 12:00:00 failed: Due={dueAt12}, Future={futureAt12}");
        }

        // Show hourly shift for 2026-09-24
        var todayPending = snapshot.MarSchedules
            .Where(s => s.Schedule_Date.Date == new DateTime(2026, 9, 24).Date &&
                        s.Dispense_Status.StartsWith("Pending", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.ScheduledDateTime)
            .ToList();

        Console.WriteLine($"\nToday's (2026-09-24) Pending Administration Doses by Scheduled Hour:");
        foreach (var g in todayPending.GroupBy(s => s.ScheduledDateTime.Hour).OrderBy(g => g.Key))
        {
            Console.WriteLine($"   Hour {g.Key:D2}:00 -> {g.Count()} doses");
        }
        Console.WriteLine("----------------------------------------------------------\n");
    }

    private static void TestStatusPrecedence()
    {
        var testTime = new DateTime(2026, 9, 24, 12, 0, 0);

        // Precedence 1: Return overrides Confirmed_Time
        var s1 = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: "N", confirmedTime: testTime, scheduleStatus: "01", orderStatus: "01",
            scheduleDate: testTime.Date, scheduleTime: testTime.TimeOfDay, returnTime: testTime, returnQty: 1.0m);
        if (s1 == ClinicalStatusEvaluator.StatusReturned)
            Pass("T18_Precedence_ReturnOverridesConfirmed", "Return overrides Confirmed_Time");
        else
            Fail("T18_Precedence_ReturnOverridesConfirmed", $"Got {s1}");

        // Precedence 2: Waste overrides Confirmed_Time
        var s2 = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: "N", confirmedTime: testTime, scheduleStatus: "01", orderStatus: "01",
            scheduleDate: testTime.Date, scheduleTime: testTime.TimeOfDay, wasteTime: testTime, wasteQty: 1.0m);
        if (s2 == ClinicalStatusEvaluator.StatusWasted)
            Pass("T19_Precedence_WasteOverridesConfirmed", "Waste overrides Confirmed_Time");
        else
            Fail("T19_Precedence_WasteOverridesConfirmed", $"Got {s2}");

        // Precedence 3: Cancelled schedule (99) overrides Confirmed_Time
        var s3 = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: null, confirmedTime: testTime, scheduleStatus: "99", orderStatus: "01",
            scheduleDate: testTime.Date, scheduleTime: testTime.TimeOfDay);
        if (s3 == ClinicalStatusEvaluator.StatusCancelled)
            Pass("T20_Precedence_Schedule99OverridesConfirmed", "Schedule 99 overrides Confirmed_Time");
        else
            Fail("T20_Precedence_Schedule99OverridesConfirmed", $"Got {s3}");
    }

    private static async Task TestWorkerVerificationRunner()
    {
        Console.WriteLine("\n--- VERIFYING WORKER M2'S BUILT-IN VERIFICATION RUNNER ---");
        try
        {
            int runnerExitCode = await ClinicalDataVerificationRunner.RunAsync();
            if (runnerExitCode == 0)
            {
                Pass("T21_Worker_VerificationRunner", "ClinicalDataVerificationRunner completed with exit code 0");
            }
            else
            {
                Fail("T21_Worker_VerificationRunner", $"ClinicalDataVerificationRunner exited with code {runnerExitCode} (KPI assertion failure due to DuePending mismatch)");
            }
        }
        catch (Exception ex)
        {
            Fail("T21_Worker_VerificationRunner", $"ClinicalDataVerificationRunner threw exception: {ex.Message}");
        }
    }
}
