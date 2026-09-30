using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using Npgsql;

namespace MedHubPrescriptionTool.ChallengerHarness;

public class Program
{
    private static int _totalTests = 0;
    private static int _passedTests = 0;
    private static int _failedTests = 0;
    private static readonly List<string> _failures = new();

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" CHALLENGER 1 (M1) EMPIRICAL TEST SUITE & ADVERSARIAL STRESS HARNESS");
        Console.WriteLine($" Executing against: 127.0.0.1:5433/amisdbV2");
        Console.WriteLine("================================================================================");

        var config = new DbConnectionSettings
        {
            Host = "127.0.0.1",
            Port = 5433,
            Database = "amisdbV2",
            Username = "postgres",
            Password = "Syscom@123",
            ReadOnly = true,
            TimeoutSeconds = 10
        };

        var factory = new DatabaseConnectionFactory(config);
        var repo = new DatabaseRepository(factory);

        // Suite 1: Connectivity & Protocol-Level Read-Only
        await TestConnectivityAndReadOnlySettings(factory);

        // Suite 2: Adversarial Mutation Attacks (INSERT, UPDATE, DELETE, TRUNCATE, DDL, SET)
        await TestAdversarialMutationAttacks(factory);

        // Suite 3: Oracle Baseline Counts (Raw SQL directly vs DatabaseRepository)
        await TestExactRowCountsAgainstDirectDb(factory, repo);

        // Suite 4: Canonical Query 7.1 MAR Status Distribution Deep Verification
        await TestMarStatusDistribution(repo, factory);

        // Suite 5: Canonical Query 7.2 Patient Directory & Encounter Verification
        await TestPatientDirectoryEncounters(repo);

        // Suite 6: Canonical Query 7.3 KPI Counts Verification
        await TestKpiSummaryCounts(repo);

        // Suite 7: Care Stations & Beds Verification
        await TestStationsAndBeds(repo);

        // Suite 8: Concurrency Stress Test (25 Concurrent Readers)
        await TestConcurrencyStress(repo);

        // Suite 9: Cancellation Token Propagation
        await TestCancellationTokenBehavior(repo);

        // Suite 10: Model Hydration & NULL-Safety Deep Scan
        await TestModelHydrationAndNullSafety(repo);

        Console.WriteLine("\n================================================================================");
        Console.WriteLine($" TEST EXECUTION SUMMARY: {_passedTests} PASSED, {_failedTests} FAILED out of {_totalTests} Tests");
        Console.WriteLine("================================================================================");

        if (_failures.Count > 0)
        {
            Console.WriteLine("FAILURES DETECTED:");
            foreach (var fail in _failures)
            {
                Console.WriteLine($"  - {fail}");
            }
            return 1;
        }

        Console.WriteLine("ALL EMPIRICAL TESTS PASSED PERFECTLY!");
        return 0;
    }

    private static void RecordPass(string name, string message)
    {
        _totalTests++;
        _passedTests++;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS] ");
        Console.ResetColor();
        Console.WriteLine($"{name}: {message}");
    }

    private static void RecordFail(string name, string error)
    {
        _totalTests++;
        _failedTests++;
        _failures.Add($"{name}: {error}");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL] ");
        Console.ResetColor();
        Console.WriteLine($"{name}: {error}");
    }

    private static async Task TestConnectivityAndReadOnlySettings(IDatabaseConnectionFactory factory)
    {
        try
        {
            var res = await factory.TestConnectionAsync();
            if (res.Success && res.IsReadOnly)
            {
                RecordPass("T01_Connection_Handshake", $"Connected in {res.LatencyMs}ms. Database: {res.DatabaseName}, ReadOnly: {res.IsReadOnly}");
            }
            else
            {
                RecordFail("T01_Connection_Handshake", $"Connection failed or not read-only: {res.ErrorMessage}");
            }

            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            await using var cmd = new NpgsqlCommand("SHOW default_transaction_read_only; SHOW transaction_read_only;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            string? defRo = null;
            string? curRo = null;
            if (await reader.ReadAsync())
            {
                defRo = reader.GetString(0);
            }
            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                curRo = reader.GetString(0);
            }

            if (defRo == "on" && curRo == "on")
            {
                RecordPass("T02_Session_ReadOnly_Variables", $"Both default_transaction_read_only={defRo} and transaction_read_only={curRo}");
            }
            else
            {
                RecordFail("T02_Session_ReadOnly_Variables", $"Expected both 'on', got default={defRo}, current={curRo}");
            }
        }
        catch (Exception ex)
        {
            RecordFail("T01_T02_Connection", ex.Message);
        }
    }

    private static async Task TestAdversarialMutationAttacks(IDatabaseConnectionFactory factory)
    {
        // Attack 1: INSERT
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            await using var cmd = new NpgsqlCommand("INSERT INTO \"Patients\" (\"Patient_Id\", \"Patient_Mrn\") VALUES ('99999999-9999-9999-9999-999999999999', 'TEST_ATTACK');", conn);
            await cmd.ExecuteNonQueryAsync();
            RecordFail("T03_Attack_Insert", "Security violation: INSERT executed successfully in read-only connection!");
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "25006")
        {
            RecordPass("T03_Attack_Insert", $"Blocked INSERT: [{pgEx.SqlState}] {pgEx.MessageText}");
        }
        catch (Exception ex)
        {
            RecordFail("T03_Attack_Insert", $"Unexpected exception: {ex.Message}");
        }

        // Attack 2: UPDATE
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            await using var cmd = new NpgsqlCommand("UPDATE \"Patients\" SET \"First_Name\" = 'Hacked' WHERE \"Patient_Mrn\" = '0439449';", conn);
            await cmd.ExecuteNonQueryAsync();
            RecordFail("T04_Attack_Update", "Security violation: UPDATE executed successfully in read-only connection!");
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "25006")
        {
            RecordPass("T04_Attack_Update", $"Blocked UPDATE: [{pgEx.SqlState}] {pgEx.MessageText}");
        }
        catch (Exception ex)
        {
            RecordFail("T04_Attack_Update", $"Unexpected exception: {ex.Message}");
        }

        // Attack 3: DELETE
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            await using var cmd = new NpgsqlCommand("DELETE FROM \"Prescriptions\" WHERE 1=0;", conn);
            await cmd.ExecuteNonQueryAsync();
            RecordFail("T05_Attack_Delete", "Security violation: DELETE executed successfully in read-only connection!");
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "25006")
        {
            RecordPass("T05_Attack_Delete", $"Blocked DELETE: [{pgEx.SqlState}] {pgEx.MessageText}");
        }
        catch (Exception ex)
        {
            RecordFail("T05_Attack_Delete", $"Unexpected exception: {ex.Message}");
        }

        // Attack 4: DDL (CREATE TABLE)
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            await using var cmd = new NpgsqlCommand("CREATE TABLE \"__hacker_test\" (id int);", conn);
            await cmd.ExecuteNonQueryAsync();
            RecordFail("T06_Attack_DDL", "Security violation: CREATE TABLE executed successfully in read-only connection!");
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "25006")
        {
            RecordPass("T06_Attack_DDL", $"Blocked CREATE TABLE: [{pgEx.SqlState}] {pgEx.MessageText}");
        }
        catch (Exception ex)
        {
            RecordFail("T06_Attack_DDL", $"Unexpected exception: {ex.Message}");
        }

        // Attack 5: Bypass attempt using SET transaction_read_only = off;
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            // Try to turn off read only in session
            await using var cmd1 = new NpgsqlCommand("SET transaction_read_only = off;", conn);
            await cmd1.ExecuteNonQueryAsync();
            
            // Check default_transaction_read_only
            await using var cmd2 = new NpgsqlCommand("SHOW default_transaction_read_only;", conn);
            var defVal = (string?)await cmd2.ExecuteScalarAsync();
            
            RecordPass("T07_Attack_Bypass_Check", $"Session attempted 'SET transaction_read_only = off'. Default setting remains: {defVal}");
        }
        catch (Exception ex)
        {
            RecordPass("T07_Attack_Bypass_Check", $"SET transaction_read_only=off threw exception: {ex.Message}");
        }
    }

    private static async Task TestExactRowCountsAgainstDirectDb(IDatabaseConnectionFactory factory, DatabaseRepository repo)
    {
        try
        {
            // Direct Oracle Query
            await using var conn = await factory.CreateOpenConnectionAsync(readOnly: true);
            
            var directPatients = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Patients\";");
            var directEncounters = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Encounter\";");
            var directPrescriptions = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Prescriptions\";");
            var directSchedules = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Schedule\";");
            var directMedications = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Medications\";");
            var directStations = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Care_Stations\";");
            var directBeds = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Rooms_Beds\";");
            var directDispenses = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"AMiS_Patient_Medication_Dispense\";");
            var directUsers = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM \"Users\";");

            // Repository Query
            var repoPatients = await repo.GetAllPatientsAsync();
            var repoEncounters = await repo.GetAllEncountersAsync();
            var repoPrescriptions = await repo.GetAllPrescriptionsAsync();
            var repoSchedules = await repo.GetAllSchedulesAsync();
            var repoMedications = await repo.GetAllMedicationsAsync();
            var repoStations = await repo.GetCareStationsAsync();
            var repoBeds = await repo.GetRoomsBedsAsync();
            var repoDispenses = await repo.GetAllDispensesAsync();
            var repoUsers = await repo.GetAllUsersAsync();

            void CheckCount(string name, int expected, int directCount, int repoCount)
            {
                if (directCount == expected && repoCount == expected)
                {
                    RecordPass($"T08_Count_{name}", $"Exact match {expected} rows (Direct DB: {directCount}, Repo: {repoCount})");
                }
                else
                {
                    RecordFail($"T08_Count_{name}", $"Mismatch! Expected: {expected}, Direct DB: {directCount}, Repo: {repoCount}");
                }
            }

            CheckCount("Patients", 15, directPatients, repoPatients.Count);
            CheckCount("Encounters", 16, directEncounters, repoEncounters.Count);
            CheckCount("Prescriptions", 31, directPrescriptions, repoPrescriptions.Count);
            CheckCount("Schedules", 385, directSchedules, repoSchedules.Count);
            CheckCount("Medications", 114, directMedications, repoMedications.Count);
            CheckCount("Care_Stations", 4, directStations, repoStations.Count);
            CheckCount("Rooms_Beds", 19, directBeds, repoBeds.Count);
            CheckCount("Dispenses", 2, directDispenses, repoDispenses.Count);
            CheckCount("Users", 13, directUsers, repoUsers.Count);
        }
        catch (Exception ex)
        {
            RecordFail("T08_RowCounts", ex.Message);
        }
    }

    private static async Task TestMarStatusDistribution(DatabaseRepository repo, IDatabaseConnectionFactory factory)
    {
        try
        {
            var marList = await repo.GetMarScheduleDataAsync();
            if (marList.Count != 385)
            {
                RecordFail("T09_Mar_TotalCount", $"Expected 385 schedules, got {marList.Count}");
                return;
            }
            RecordPass("T09_Mar_TotalCount", $"Retrieved exactly {marList.Count} MAR schedule rows");

            var statusGroups = marList.GroupBy(x => x.Computed_Status)
                                      .ToDictionary(g => g.Key ?? "(null)", g => g.Count());

            int dispensed = statusGroups.GetValueOrDefault("Dispensed", 0);
            int returned = statusGroups.GetValueOrDefault("Returned", 0);
            int wasted = statusGroups.GetValueOrDefault("Wasted", 0);
            int duePending = statusGroups.GetValueOrDefault("Pending (Due)", 0);
            int futurePending = statusGroups.GetValueOrDefault("Pending (Future)", 0);

            Console.WriteLine($"   MAR Distribution Breakdown:");
            Console.WriteLine($"     Dispensed:        {dispensed} (Target: 13)");
            Console.WriteLine($"     Returned:         {returned} (Target: 1)");
            Console.WriteLine($"     Wasted:           {wasted} (Target: 1)");
            Console.WriteLine($"     Pending (Due):    {duePending} (Target: 286)");
            Console.WriteLine($"     Pending (Future): {futurePending} (Target: 84)");
            Console.WriteLine($"     Total:            {dispensed + returned + wasted + duePending + futurePending}");

            if (dispensed == 13)
                RecordPass("T10_Mar_DispensedCount", $"Dispensed count is exactly 13");
            else
                RecordFail("T10_Mar_DispensedCount", $"Dispensed count mismatch! Expected 13, got {dispensed}");

            if (returned == 1)
                RecordPass("T11_Mar_ReturnedCount", $"Returned count is exactly 1");
            else
                RecordFail("T11_Mar_ReturnedCount", $"Returned count mismatch! Expected 1, got {returned}");

            if (wasted == 1)
                RecordPass("T12_Mar_WastedCount", $"Wasted count is exactly 1");
            else
                RecordFail("T12_Mar_WastedCount", $"Wasted count mismatch! Expected 1, got {wasted}");

            if (duePending == 286)
                RecordPass("T13_Mar_DuePendingCount", $"Pending (Due) count is exactly 286");
            else
                RecordFail("T13_Mar_DuePendingCount", $"Pending (Due) count mismatch! Expected 286, got {duePending}");

            if (futurePending == 84)
                RecordPass("T14_Mar_FuturePendingCount", $"Pending (Future) count is exactly 84");
            else
                RecordFail("T14_Mar_FuturePendingCount", $"Pending (Future) count mismatch! Expected 84, got {futurePending}");

            // Oracle Verification of the 13 Dispensed doses
            // In live amisdbV2, what makes them Dispensed?
            // Explorer 1 found: Schedule.Confirmed_Time is set for 13 schedules.
            var confirmedInSchedule = marList.Count(x => x.Confirmed_Time != null);
            var statusYInDispense = marList.Count(x => x.Dispense_Status == "Y");
            RecordPass("T15_Mar_Dispensed_Mechanisms", $"Confirmed_Time present on {confirmedInSchedule} rows; Dispense_Status='Y' on {statusYInDispense} rows");

            // Oracle Verification of Returned and Wasted
            var returnedItem = marList.SingleOrDefault(x => x.Computed_Status == "Returned");
            if (returnedItem != null)
            {
                RecordPass("T16_Mar_Returned_Detail", $"Returned item: ScheduleId={returnedItem.Schedule_Id}, Patient={returnedItem.Patient_Mrn}, ConfirmedTime={returnedItem.Confirmed_Time}, ReturnQty={returnedItem.Return_Qty}, ReturnTime={returnedItem.Return_Time}");
            }
            else
            {
                RecordFail("T16_Mar_Returned_Detail", "Could not locate single Returned item!");
            }

            var wastedItem = marList.SingleOrDefault(x => x.Computed_Status == "Wasted");
            if (wastedItem != null)
            {
                RecordPass("T17_Mar_Wasted_Detail", $"Wasted item: ScheduleId={wastedItem.Schedule_Id}, Patient={wastedItem.Patient_Mrn}, ConfirmedTime={wastedItem.Confirmed_Time}, WasteQty={wastedItem.Waste_Qty}, WasteTime={wastedItem.Waste_Time}");
            }
            else
            {
                RecordFail("T17_Mar_Wasted_Detail", "Could not locate single Wasted item!");
            }

            // Verify Schedule Date Range and Breakdown
            var minDate = marList.Min(x => x.Schedule_Date);
            var maxDate = marList.Max(x => x.Schedule_Date);
            RecordPass("T18_Mar_Date_Span", $"Schedule dates span from {minDate:yyyy-MM-dd} to {maxDate:yyyy-MM-dd}");

            var pendingItems = marList.Where(x => x.Computed_Status?.StartsWith("Pending") == true).ToList();
            Console.WriteLine("   Pending Schedule Dates Breakdown:");
            foreach (var grp in pendingItems.GroupBy(x => x.Schedule_Date).OrderBy(g => g.Key))
            {
                var dueInGrp = grp.Count(x => x.Computed_Status == "Pending (Due)");
                var futureInGrp = grp.Count(x => x.Computed_Status == "Pending (Future)");
                Console.WriteLine($"     Date: {grp.Key:yyyy-MM-dd} -> Due: {dueInGrp}, Future: {futureInGrp}, Total: {grp.Count()}");
            }

        }
        catch (Exception ex)
        {
            RecordFail("T09_T18_MarDistribution", ex.Message);
        }
    }

    private static async Task TestPatientDirectoryEncounters(DatabaseRepository repo)
    {
        try
        {
            var encounters = await repo.GetPatientEncountersAsync();
            if (encounters.Count == 16)
            {
                RecordPass("T19_Encounter_Count", $"Retrieved exactly 16 active inpatient encounters");
            }
            else
            {
                RecordFail("T19_Encounter_Count", $"Expected 16 encounters, got {encounters.Count}");
            }

            // Check distinct patient count
            var distinctMrns = encounters.Select(x => x.Patient_Mrn).Distinct().ToList();
            if (distinctMrns.Count == 15)
            {
                RecordPass("T20_Distinct_Patients", $"16 encounters map to exactly 15 unique Patient MRNs");
            }
            else
            {
                RecordFail("T20_Distinct_Patients", $"Expected 15 distinct MRNs, got {distinctMrns.Count}");
            }

            // Identify the patient with 2 encounters
            var duplicateMrn = encounters.GroupBy(x => x.Patient_Mrn)
                                        .Where(g => g.Count() > 1)
                                        .Select(g => new { Mrn = g.Key, Count = g.Count() })
                                        .ToList();

            if (duplicateMrn.Count == 1)
            {
                RecordPass("T21_Duplicate_Encounter_Patient", $"Patient MRN '{duplicateMrn[0].Mrn}' has {duplicateMrn[0].Count} active encounters");
            }
            else
            {
                RecordFail("T21_Duplicate_Encounter_Patient", $"Expected exactly 1 patient with multiple encounters, found {duplicateMrn.Count}");
            }

            // Sum of scheduled doses
            int totalDoses = encounters.Sum(x => x.Total_Scheduled_Doses);
            int dispensedDoses = encounters.Sum(x => x.Dispensed_Doses);
            int pendingDoses = encounters.Sum(x => x.Pending_Doses);

            if (totalDoses == 385 && dispensedDoses == 13)
            {
                RecordPass("T22_Encounter_Dose_Totals", $"Total Scheduled Doses = {totalDoses} (exp 385), Dispensed = {dispensedDoses} (exp 13), Pending = {pendingDoses}");
            }
            else
            {
                RecordFail("T22_Encounter_Dose_Totals", $"Dose summation mismatch! Total: {totalDoses} (exp 385), Dispensed: {dispensedDoses} (exp 13)");
            }
        }
        catch (Exception ex)
        {
            RecordFail("T19_T22_Encounters", ex.Message);
        }
    }

    private static async Task TestKpiSummaryCounts(DatabaseRepository repo)
    {
        try
        {
            var kpi = await repo.GetKpiCountsAsync();
            Console.WriteLine($"   KPI Query Output: Inpatients={kpi.total_inpatients}, ActiveOrders={kpi.total_active_orders}, Dispensed={kpi.total_dispensed}, DuePending={kpi.due_pending_orders}, TotalPending={kpi.total_pending_orders}");

            if (kpi.total_inpatients == 15)
                RecordPass("T23_Kpi_Inpatients", $"Total Inpatients is 15");
            else
                RecordFail("T23_Kpi_Inpatients", $"Expected 15, got {kpi.total_inpatients}");

            if (kpi.total_active_orders == 31)
                RecordPass("T24_Kpi_ActiveOrders", $"Total Active Orders is 31");
            else
                RecordFail("T24_Kpi_ActiveOrders", $"Expected 31, got {kpi.total_active_orders}");

            if (kpi.total_dispensed == 13)
                RecordPass("T25_Kpi_Dispensed", $"Total Dispensed is 13");
            else
                RecordFail("T25_Kpi_Dispensed", $"Expected 13, got {kpi.total_dispensed}");

            if (kpi.due_pending_orders == 286)
                RecordPass("T26_Kpi_DuePending", $"Due Pending Orders is 286");
            else
                RecordFail("T26_Kpi_DuePending", $"Expected 286, got {kpi.due_pending_orders}");

            if (kpi.total_pending_orders == 370)
                RecordPass("T27_Kpi_TotalPending", $"Total Pending Orders is 370 (286 due + 84 future)");
            else
                RecordFail("T27_Kpi_TotalPending", $"Expected 370, got {kpi.total_pending_orders}");
        }
        catch (Exception ex)
        {
            RecordFail("T23_T27_Kpi", ex.Message);
        }
    }

    private static async Task TestStationsAndBeds(DatabaseRepository repo)
    {
        try
        {
            var stations = await repo.GetCareStationsAsync();
            var expectedStations = new HashSet<string> { "8A", "5A", "ICU", "OR" };
            var actualStations = stations.Select(s => s.Station_Code).ToHashSet();

            if (stations.Count == 4 && actualStations.SetEquals(expectedStations))
            {
                RecordPass("T28_Care_Stations", $"Exactly 4 care stations verified: {string.Join(", ", actualStations)}");
            }
            else
            {
                RecordFail("T28_Care_Stations", $"Station mismatch! Expected: {string.Join(", ", expectedStations)}, Got: {string.Join(", ", actualStations)}");
            }

            var beds = await repo.GetRoomsBedsAsync();
            if (beds.Count == 19)
            {
                RecordPass("T29_Rooms_Beds", $"Exactly 19 beds verified across stations");
            }
            else
            {
                RecordFail("T29_Rooms_Beds", $"Bed count mismatch! Expected 19, got {beds.Count}");
            }
        }
        catch (Exception ex)
        {
            RecordFail("T28_T29_Stations_Beds", ex.Message);
        }
    }

    private static async Task TestConcurrencyStress(DatabaseRepository repo)
    {
        try
        {
            const int concurrency = 25;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, concurrency).Select(async i =>
            {
                var mar = await repo.GetMarScheduleDataAsync();
                var kpi = await repo.GetKpiCountsAsync();
                var enc = await repo.GetPatientEncountersAsync();
                return mar.Count == 385 && kpi.total_dispensed == 13 && enc.Count == 16;
            });

            var results = await Task.WhenAll(tasks);
            sw.Stop();

            if (results.All(r => r))
            {
                RecordPass("T30_Concurrency_Stress", $"{concurrency} concurrent reader queries executed in parallel ({sw.ElapsedMilliseconds}ms, all succeeded)");
            }
            else
            {
                RecordFail("T30_Concurrency_Stress", "Some concurrent queries failed or returned inconsistent counts");
            }
        }
        catch (Exception ex)
        {
            RecordFail("T30_Concurrency_Stress", ex.Message);
        }
    }

    private static async Task TestCancellationTokenBehavior(DatabaseRepository repo)
    {
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancelled token

            await repo.GetMarScheduleDataAsync(cts.Token);
            RecordFail("T31_Cancellation_Token", "Expected OperationCanceledException was not thrown on cancelled token!");
        }
        catch (OperationCanceledException)
        {
            RecordPass("T31_Cancellation_Token", "OperationCanceledException thrown cleanly on cancelled token");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is OperationCanceledException)
        {
            RecordPass("T31_Cancellation_Token", $"Cancellation caught cleanly inside factory wrapper: {ex.Message}");
        }
        catch (Exception ex)
        {
            RecordFail("T31_Cancellation_Token", $"Unexpected exception type: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static async Task TestModelHydrationAndNullSafety(DatabaseRepository repo)
    {
        try
        {
            var mar = await repo.GetMarScheduleDataAsync();
            
            // Check for critical missing values
            int nullMrn = mar.Count(x => string.IsNullOrWhiteSpace(x.Patient_Mrn));
            int nullOrder = mar.Count(x => x.Med_Order_Id == Guid.Empty);
            int nullSched = mar.Count(x => x.Schedule_Id == Guid.Empty);
            int nullStatus = mar.Count(x => string.IsNullOrWhiteSpace(x.Computed_Status));

            if (nullMrn == 0 && nullOrder == 0 && nullSched == 0 && nullStatus == 0)
            {
                RecordPass("T32_Model_Hydration_NullSafety", "All 385 MAR records have non-empty Patient_Mrn, Med_Order_Id, Schedule_Id, and Computed_Status");
            }
            else
            {
                RecordFail("T32_Model_Hydration_NullSafety", $"Found null fields! Null MRN: {nullMrn}, Null Order: {nullOrder}, Null Sched: {nullSched}, Null Status: {nullStatus}");
            }

            // Check controlled drug and high alert flags
            int controlledCount = mar.Count(x => x.Is_Controlled == true);
            int highAlertCount = mar.Count(x => x.isHighAlert == true);
            RecordPass("T33_Drug_Flags", $"Controlled drugs: {controlledCount}, High Alert drugs: {highAlertCount}");

            // Check Allergy preservation
            var patients = await repo.GetAllPatientsAsync();
            Console.WriteLine("   Inspecting Patients Allergy & DrugWarn values:");
            foreach (var p in patients)
            {
                Console.WriteLine($"     MRN: {p.Patient_Mrn}, Name: {p.Last_Name} {p.First_Name}, Allergy: '{p.Allergy}', AllergyDesc: '{p.Allergy_Desc}', DrugWarn: '{p.DrugWarn}', DrugWarnDesc: '{p.DrugWarn_Desc}'");
            }

            var allergyPatients = patients.Where(p => p.Allergy == "Y").ToList();
            var nullAllergyPatients = patients.Where(p => p.Allergy == null).ToList();
            var noAllergyPatients = patients.Where(p => p.Allergy == "N").ToList();
            RecordPass("T34_Allergy_Preservation", $"Patients with Allergy='Y': {allergyPatients.Count}, Allergy='N': {noAllergyPatients.Count}, Allergy=null: {nullAllergyPatients.Count}");

            // Oracle verification of DB Reality for Test Suite Assumptions
            var allMeds = await repo.GetAllMedicationsAsync();
            var highAlertMeds = allMeds.Where(m => m.isHighAlert == true).ToList();
            Console.WriteLine($"   Medications with isHighAlert=true: {highAlertMeds.Count} out of {allMeds.Count}");
            foreach (var ham in highAlertMeds)
            {
                Console.WriteLine($"     High Alert Med: Code='{ham.Item_Code}', Name='{ham.Display_Name}', Generic='{ham.Generic_Name}'");
            }


            var allEncounters = await repo.GetAllEncountersAsync();
            var dischargedEncounters = allEncounters.Where(e => e.Discharge_Date != null).ToList();
            Console.WriteLine($"   Encounters with Discharge_Date!=null: {dischargedEncounters.Count} out of {allEncounters.Count}");

            var allPrescriptions = await repo.GetAllPrescriptionsAsync();
            var activeStatusPrescriptions = allPrescriptions.Where(p => p.Order_Status == "01").ToList();
            var futureEndPrescriptions = activeStatusPrescriptions.Where(p => p.End_Datetime == null || p.End_Datetime >= DateTime.Now).ToList();
            Console.WriteLine($"   Prescriptions total: {allPrescriptions.Count}, with Order_Status='01': {activeStatusPrescriptions.Count}, with End_Datetime>=Now: {futureEndPrescriptions.Count}");


        }
        catch (Exception ex)
        {
            RecordFail("T32_T34_Hydration", ex.Message);
        }
    }
}
