using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using Npgsql;

namespace MedHubPrescriptionTool.Data;

/// <summary>
/// Self-testing verification runner for Milestone 1.
/// Verifies live connectivity, strict read-only enforcement, table hydration across
/// all 8 core tables + Users, and the 3 canonical Explorer 1 queries.
/// </summary>
public static class DataVerificationRunner
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" MedHub DB Prescription Tool - Milestone 1 Verification Suite");
        Console.WriteLine(" Target Database: 127.0.0.1:5433/amisdbV2");
        Console.WriteLine("================================================================================");

        var config = AppConfigurationManager.Current;
        var factory = new DatabaseConnectionFactory(config.Database);
        var repo = new DatabaseRepository(factory);

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

        // Test 1: Connectivity & Server Metadata
        try
        {
            var testResult = await factory.TestConnectionAsync();
            if (testResult.Success && testResult.IsReadOnly)
            {
                AssertPass("1. Database Connectivity & Protocol Read-Only",
                    $"Connected to '{testResult.DatabaseName}' in {testResult.LatencyMs}ms (Version: {testResult.ServerVersion?.Split(',')[0] ?? "unknown"}, IsReadOnly: {testResult.IsReadOnly})");
            }
            else
            {
                AssertFail("1. Database Connectivity & Protocol Read-Only",
                    $"Connection failed or IsReadOnly is false: {testResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("1. Database Connectivity & Protocol Read-Only", ex.Message);
        }

        // Test 2: Mutation Blocking (Strict Read-Only Enforcement)
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO ""Care_Stations"" (""Station_Id"", ""Department_Id"", ""Station_Code"", ""Station_Name"")
                  VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'TEST', 'Test Station');",
                conn);

            await cmd.ExecuteNonQueryAsync();
            AssertFail("2. Mutation Blocking Assertion", "CRITICAL ERROR: INSERT query succeeded! Read-only mode was NOT enforced!");
        }
        catch (PostgresException pgEx)
        {
            // SQLSTATE 25006: read_only_sql_transaction
            if (pgEx.SqlState == "25006" || pgEx.Message.Contains("read-only"))
            {
                AssertPass("2. Mutation Blocking Assertion",
                    $"Successfully blocked mutation! Caught expected PostgresException [SqlState={pgEx.SqlState}]: \"{pgEx.MessageText}\"");
            }
            else
            {
                AssertFail("2. Mutation Blocking Assertion", $"Unexpected PostgresException code: {pgEx.SqlState} - {pgEx.Message}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("2. Mutation Blocking Assertion", $"Unexpected non-Postgres exception type: {ex.GetType().Name} - {ex.Message}");
        }

        // Test 3: Hydrate 8 Core Tables + Users
        try
        {
            var patients = await repo.GetAllPatientsAsync();
            var encounters = await repo.GetAllEncountersAsync();
            var prescriptions = await repo.GetAllPrescriptionsAsync();
            var schedules = await repo.GetAllSchedulesAsync();
            var medications = await repo.GetAllMedicationsAsync();
            var dispenses = await repo.GetAllDispensesAsync();
            var stations = await repo.GetCareStationsAsync();
            var beds = await repo.GetRoomsBedsAsync();
            var users = await repo.GetAllUsersAsync();

            bool allTablesPopulated = patients.Count > 0 &&
                                      encounters.Count > 0 &&
                                      prescriptions.Count > 0 &&
                                      schedules.Count > 0 &&
                                      medications.Count > 0 &&
                                      dispenses.Count > 0 &&
                                      stations.Count > 0 &&
                                      beds.Count > 0 &&
                                      users.Count > 0;

            if (allTablesPopulated)
            {
                AssertPass("3. Core 8 Tables + Users Hydration",
                    $"Retrieved {patients.Count} Patients, {encounters.Count} Encounters, {prescriptions.Count} Prescriptions, " +
                    $"{schedules.Count} Schedules, {medications.Count} Medications, {dispenses.Count} Dispenses, " +
                    $"{stations.Count} Stations, {beds.Count} Beds, {users.Count} Users");
            }
            else
            {
                AssertFail("3. Core 8 Tables + Users Hydration", "One or more tables returned 0 records.");
            }
        }
        catch (Exception ex)
        {
            AssertFail("3. Core 8 Tables + Users Hydration", ex.Message);
        }

        // Test 4: Canonical Query 7.1 (Unified MAR Timetable)
        try
        {
            var marSchedules = await repo.GetMarScheduleDataAsync();
            var dispensedCount = marSchedules.Count(x => x.Computed_Status == "Dispensed");
            var returnedCount = marSchedules.Count(x => x.Computed_Status == "Returned");
            var wastedCount = marSchedules.Count(x => x.Computed_Status == "Wasted");
            var duePendingCount = marSchedules.Count(x => x.Computed_Status == "Pending (Due)");
            var futurePendingCount = marSchedules.Count(x => x.Computed_Status == "Pending (Future)");
            var highAlertCount = marSchedules.Count(x => x.isHighAlert == true);
            var controlledCount = marSchedules.Count(x => x.Is_Controlled == true);

            if (marSchedules.Count == 385 && dispensedCount == 13 && returnedCount == 1 && wastedCount == 1)
            {
                AssertPass("4. Canonical Query 7.1 (Unified MAR Timetable)",
                    $"Retrieved {marSchedules.Count} schedules. Status: {dispensedCount} Dispensed, {returnedCount} Returned, " +
                    $"{wastedCount} Wasted, {duePendingCount} Due Pending, {futurePendingCount} Future Pending. " +
                    $"Flags: {highAlertCount} High-Alert, {controlledCount} Controlled");
            }
            else
            {
                AssertFail("4. Canonical Query 7.1 (Unified MAR Timetable)",
                    $"Row count or distribution mismatch. Total: {marSchedules.Count} (exp 385), Dispensed: {dispensedCount} (exp 13), Returned: {returnedCount} (exp 1), Wasted: {wastedCount} (exp 1)");
            }
        }
        catch (Exception ex)
        {
            AssertFail("4. Canonical Query 7.1 (Unified MAR Timetable)", ex.Message);
        }

        // Test 5: Canonical Query 7.2 (Patient Directory Master-Detail)
        try
        {
            var patientEncounters = await repo.GetPatientEncountersAsync();
            if (patientEncounters.Count > 0)
            {
                var totalDoses = patientEncounters.Sum(p => p.Total_Scheduled_Doses);
                var dispensedDoses = patientEncounters.Sum(p => p.Dispensed_Doses);
                AssertPass("5. Canonical Query 7.2 (Patient Directory Master-Detail)",
                    $"Retrieved {patientEncounters.Count} active inpatient encounters. Total Sched Doses: {totalDoses}, Dispensed: {dispensedDoses}");
            }
            else
            {
                AssertFail("5. Canonical Query 7.2 (Patient Directory Master-Detail)", "No active inpatient encounters returned.");
            }
        }
        catch (Exception ex)
        {
            AssertFail("5. Canonical Query 7.2 (Patient Directory Master-Detail)", ex.Message);
        }

        // Test 6: Canonical Query 7.3 (Top KPI Summary Counts)
        try
        {
            var kpi = await repo.GetKpiCountsAsync();
            if (kpi.total_inpatients > 0 && kpi.total_active_orders > 0 && kpi.total_dispensed == 13)
            {
                AssertPass("6. Canonical Query 7.3 (Top KPI Summary)",
                    $"Inpatients: {kpi.total_inpatients}, Active Orders: {kpi.total_active_orders}, Dispensed: {kpi.total_dispensed}, Due Pending: {kpi.due_pending_orders}, Total Pending: {kpi.total_pending_orders}");
            }
            else
            {
                AssertFail("6. Canonical Query 7.3 (Top KPI Summary)",
                    $"KPI values unexpected: Inpatients={kpi.total_inpatients}, Active={kpi.total_active_orders}, Dispensed={kpi.total_dispensed} (expected 13)");
            }
        }
        catch (Exception ex)
        {
            AssertFail("6. Canonical Query 7.3 (Top KPI Summary)", ex.Message);
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine($" Summary: {passed} PASSED, {failed} FAILED out of {passed + failed} Tests");
        Console.WriteLine("================================================================================");

        return failed == 0 ? 0 : 1;
    }
}
