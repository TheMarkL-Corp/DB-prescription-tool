using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;

namespace MedHubPrescriptionTool.Services;

/// <summary>
/// Self-testing automated verification runner for Milestone 2.
/// Validates data consolidation, DTO hydration, NULL-safe status state machine,
/// KPI calculations, patient directory grouping, and multi-field fuzzy search performance.
/// </summary>
public static class ClinicalDataVerificationRunner
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" MedHub DB Prescription Tool - Milestone 2 Verification Suite");
        Console.WriteLine(" Clinical Data Consolidation, Status State Machine & Business Models");
        Console.WriteLine("================================================================================");

        var config = AppConfigurationManager.Current;
        var factory = new DatabaseConnectionFactory(config.Database);
        var repo = new DatabaseRepository(factory);
        var service = new ClinicalDataService(repo);

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

        ClinicalDataSnapshot? snapshot = null;

        // Test 1: Snapshot Hydration & Consolidation
        try
        {
            var sw = Stopwatch.StartNew();
            snapshot = await service.LoadClinicalSnapshotAsync();
            sw.Stop();

            if (snapshot.Patients.Count == 16 &&
                snapshot.MarSchedules.Count == 385 &&
                snapshot.ActivePrescriptions.Count == 31 &&
                snapshot.Medications.Count == 114 &&
                snapshot.Stations.Count == 4 &&
                snapshot.Beds.Count == 19)
            {
                AssertPass("1. Clinical Snapshot Consolidation",
                    $"Hydrated 16 Patients, 385 MAR Schedules, 31 Active Prescriptions, 114 Medications, 4 Stations, 19 Beds in {sw.ElapsedMilliseconds}ms (RefreshedAt: {snapshot.LastRefreshedAt:HH:mm:ss})");
            }
            else
            {
                AssertFail("1. Clinical Snapshot Consolidation",
                    $"Entity counts unexpected: Patients={snapshot.Patients.Count} (exp 16), MAR={snapshot.MarSchedules.Count} (exp 385), Rx={snapshot.ActivePrescriptions.Count} (exp 31), Meds={snapshot.Medications.Count} (exp 114)");
            }
        }
        catch (Exception ex)
        {
            AssertFail("1. Clinical Snapshot Consolidation", ex.Message);
            return 1;
        }

        // Test 2: KPI Metrics Calculation Invariants
        try
        {
            var kpi = service.ComputeKpiSummary(snapshot);

            bool kpiValid =
                kpi.TotalInpatients == 15 &&
                kpi.TotalActiveOrders == 31 &&
                kpi.DispensedCount == 13 &&
                kpi.TotalPendingOrders == 370 &&
                (kpi.DuePendingOrders + kpi.FuturePendingOrders) == 370 &&
                kpi.DuePendingOrders >= 0 &&
                kpi.FuturePendingOrders >= 0 &&
                kpi.ReturnedCount == 1 &&
                kpi.WastedCount == 1 &&
                kpi.CancelledCount == 0 &&
                kpi.ControlledCount == 143;

            if (kpiValid)
            {
                AssertPass("2. KPI Summary Calculation",
                    $"Inpatients={kpi.TotalInpatients}, ActiveOrders={kpi.TotalActiveOrders}, Dispensed={kpi.DispensedCount}, DuePending={kpi.DuePendingOrders}, TotalPending={kpi.TotalPendingOrders}, Returned={kpi.ReturnedCount}, Wasted={kpi.WastedCount}, Controlled={kpi.ControlledCount}");
            }
            else
            {
                AssertFail("2. KPI Summary Calculation",
                    $"KPI mismatch! Inpatients={kpi.TotalInpatients} (exp 15), Active={kpi.TotalActiveOrders} (exp 31), Dispensed={kpi.DispensedCount} (exp 13), TotalPending={kpi.TotalPendingOrders} (exp 370), DuePending+FuturePending={kpi.DuePendingOrders + kpi.FuturePendingOrders} (exp 370), Ret={kpi.ReturnedCount} (exp 1), Waste={kpi.WastedCount} (exp 1)");
            }
        }
        catch (Exception ex)
        {
            AssertFail("2. KPI Summary Calculation", ex.Message);
        }

        // Test 3: NULL-Safe Status State Machine & Precedence
        try
        {
            var testTime = new DateTime(2026, 9, 24, 12, 0, 0);

            // 3a. Return overrides Confirmed_Time
            var s1 = ClinicalStatusEvaluator.EvaluateStatus(
                dispenseStatus: "N",
                confirmedTime: testTime.AddHours(-1),
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDate: testTime.Date,
                scheduleTime: testTime.TimeOfDay,
                returnTime: testTime,
                returnQty: 1.0m,
                referenceTime: testTime);

            // 3b. Waste overrides Confirmed_Time
            var s2 = ClinicalStatusEvaluator.EvaluateStatus(
                dispenseStatus: "N",
                confirmedTime: testTime.AddHours(-1),
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDate: testTime.Date,
                scheduleTime: testTime.TimeOfDay,
                wasteTime: testTime,
                wasteQty: 1.0m,
                referenceTime: testTime);

            // 3c. Inactive schedule overrides Confirmed
            var s3 = ClinicalStatusEvaluator.EvaluateStatus(
                dispenseStatus: null,
                confirmedTime: testTime,
                scheduleStatus: "99",
                orderStatus: "01",
                scheduleDate: testTime.Date,
                scheduleTime: testTime.TimeOfDay,
                referenceTime: testTime);

            // 3d. Confirmed without dispense log entry is Dispensed (NULL-safe check)
            var s4 = ClinicalStatusEvaluator.EvaluateStatus(
                dispenseStatus: null,
                confirmedTime: testTime,
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDate: testTime.Date,
                scheduleTime: testTime.TimeOfDay,
                referenceTime: testTime);

            // 3e. Past schedule with no dispense/confirm is Pending (Due)
            var s5 = ClinicalStatusEvaluator.EvaluateStatus(
                dispenseStatus: null,
                confirmedTime: null,
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDate: testTime.Date,
                scheduleTime: new TimeSpan(8, 0, 0),
                referenceTime: testTime);

            // 3f. Future schedule with no dispense/confirm is Pending (Future)
            var s6 = ClinicalStatusEvaluator.EvaluateStatus(
                dispenseStatus: null,
                confirmedTime: null,
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDate: testTime.Date,
                scheduleTime: new TimeSpan(16, 0, 0),
                referenceTime: testTime);

            bool unitStatusValid =
                s1 == ClinicalStatusEvaluator.StatusReturned &&
                s2 == ClinicalStatusEvaluator.StatusWasted &&
                s3 == ClinicalStatusEvaluator.StatusCancelled &&
                s4 == ClinicalStatusEvaluator.StatusDispensed &&
                s5 == ClinicalStatusEvaluator.StatusPendingDue &&
                s6 == ClinicalStatusEvaluator.StatusPendingFuture;

            // Check against live database records
            var liveReturned = snapshot.MarSchedules.FirstOrDefault(s => s.Dispense_Status == ClinicalStatusEvaluator.StatusReturned);
            var liveWasted = snapshot.MarSchedules.FirstOrDefault(s => s.Dispense_Status == ClinicalStatusEvaluator.StatusWasted);

            bool liveStatusValid =
                liveReturned != null &&
                liveReturned.Return_Reason_Name!.Contains("病人禁食 (Patient Fasting / NPO)") &&
                liveWasted != null &&
                liveWasted.Waste_Qty == 1.0m;

            if (unitStatusValid && liveStatusValid)
            {
                AssertPass("3. NULL-Safe Status State Machine",
                    $"Verified precedence: Return/Waste overrides Confirmed, Schedule 99 cancels, NULL DispenseStatus correctly parsed. Live Returned: '{liveReturned!.Return_Reason_Name}', Live Wasted Qty: {liveWasted!.Waste_Qty}");
            }
            else
            {
                AssertFail("3. NULL-Safe Status State Machine",
                    $"State machine unit checks or live checks failed. s1={s1}, s2={s2}, s3={s3}, s4={s4}, s5={s5}, s6={s6}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("3. NULL-Safe Status State Machine", ex.Message);
        }

        // Test 4: Master-Detail Patient Directory Grouping & Ordering
        try
        {
            var allPatients = service.GetPatientOverviewList(snapshot);
            var station8APatients = service.GetPatientOverviewList(snapshot, "8A");
            var station5APatients = service.GetPatientOverviewList(snapshot, "5A");
            var stationIcuPatients = service.GetPatientOverviewList(snapshot, "ICU");

            // In our database: 16 active encounters across stations (8A: 7, 5A: 6, ICU: 3)
            bool stationCountsValid =
                allPatients.Count == 16 &&
                station8APatients.Count == 7 &&
                station5APatients.Count == 6 &&
                stationIcuPatients.Count == 3;

            // Find patient with allergy if any
            var allergyPatient = allPatients.FirstOrDefault(p => p.HasAllergy);
            var teng = allPatients.FirstOrDefault(p => p.Patient_Mrn == "0439449");

            bool tengValid =
                teng != null &&
                teng.ActivePrescriptions.Count == 2 &&
                teng.Total_Prescriptions == 2 &&
                teng.RecentSchedules.Count == 11;

            if (stationCountsValid && tengValid)
            {
                AssertPass("4. Master-Detail Patient Grouping",
                    $"Grouped 16 patients (8A: {station8APatients.Count}, 5A: {station5APatients.Count}, ICU: {stationIcuPatients.Count}). Patient Teng, Max (MRN 0439449) verified: 2 Active Rx Orders, Schedules={teng!.RecentSchedules.Count}. Allergy Patient: '{(allergyPatient != null ? allergyPatient.FullName + " - " + allergyPatient.Allergy_Desc : "None")}'");
            }
            else
            {
                AssertFail("4. Master-Detail Patient Grouping",
                    $"Grouping or patient details invalid. Total={allPatients.Count}, 8A={station8APatients.Count}, 5A={station5APatients.Count}, ICU={stationIcuPatients.Count}, TengFound={teng != null}, TengRx={teng?.ActivePrescriptions.Count}, TengTotalRx={teng?.Total_Prescriptions}, TengAllergy={teng?.HasAllergy}, TengAllergyDesc='{teng?.Allergy_Desc}', TengSchedules={teng?.RecentSchedules.Count}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("4. Master-Detail Patient Grouping", ex.Message);
        }

        // Test 5: Multi-Field Fuzzy Search & Instant Filtering
        try
        {
            // 5a. Search by MRN
            var mrnResults = service.FilterMarSchedule(snapshot, new ClinicalFilterCriteria { SearchQuery = "0439449" });

            // 5b. Search by Medication Name
            var medResults = service.FilterMarSchedule(snapshot, new ClinicalFilterCriteria { SearchQuery = "Levemir" });

            // 5c. Filter by Status (Dispensed)
            var dispensedResults = service.FilterMarSchedule(snapshot, new ClinicalFilterCriteria { StatusFilter = "Dispensed" });

            // 5d. Filter by Exceptions (Returned/Wasted/Cancelled)
            var exceptionResults = service.FilterMarSchedule(snapshot, new ClinicalFilterCriteria { StatusFilter = "Returned/Wasted/Cancelled" });

            // 5e. Filter by Controlled / High-Alert
            var controlledResults = service.FilterMarSchedule(snapshot, new ClinicalFilterCriteria { ControlledOnly = true });

            // 5f. Date Filter: Today (using latest active schedule date)
            var activeDate = snapshot.MarSchedules.Count > 0
                ? snapshot.MarSchedules.Max(s => s.Schedule_Date.Date)
                : DateTime.Today;
            var todayResults = service.FilterMarSchedule(snapshot, new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                ReferenceTime = activeDate.AddHours(12)
            });

            bool searchValid =
                mrnResults.All(r => r.Patient_Mrn == "0439449") && mrnResults.Count > 0 &&
                medResults.All(r => r.Display_Name.Contains("Levemir", StringComparison.OrdinalIgnoreCase) || r.Generic_Name!.Contains("Levemir", StringComparison.OrdinalIgnoreCase)) &&
                dispensedResults.Count == 13 &&
                exceptionResults.Count == 2 &&
                controlledResults.Count == 143 &&
                todayResults.Count == 140;

            if (searchValid)
            {
                AssertPass("5. Multi-Field Fuzzy Search & Filtering",
                    $"Search MRN (0439449): {mrnResults.Count} rows, Search Drug ('Levemir'): {medResults.Count} rows, Dispensed Filter: {dispensedResults.Count} rows, Exceptions Filter: {exceptionResults.Count} rows, Controlled Filter: {controlledResults.Count} rows, Today Date Filter: {todayResults.Count} rows");
            }
            else
            {
                AssertFail("5. Multi-Field Fuzzy Search & Filtering",
                    $"Search results mismatch: MRN count={mrnResults.Count}, Med count={medResults.Count}, Dispensed={dispensedResults.Count} (exp 13), Exceptions={exceptionResults.Count} (exp 2), Controlled={controlledResults.Count} (exp 143), Today={todayResults.Count} (exp 140)");
            }
        }
        catch (Exception ex)
        {
            AssertFail("5. Multi-Field Fuzzy Search & Filtering", ex.Message);
        }

        // Test 6: Medication Detail Monograph Retrieval
        try
        {
            var medOxana = service.GetMedicationDetails(snapshot, "OXANA");
            var medCaseInsensitive = service.GetMedicationDetails(snapshot, "oxana");
            var medUnknown = service.GetMedicationDetails(snapshot, "DOES_NOT_EXIST_XYZ");

            bool medValid =
                medOxana != null &&
                medCaseInsensitive != null &&
                medOxana.Medication_Id == medCaseInsensitive.Medication_Id &&
                medOxana.Is_Controlled &&
                medOxana.Control_Level == "4" &&
                medOxana.Display_Name.Contains("Alpraline") &&
                medUnknown == null;

            if (medValid)
            {
                AssertPass("6. Medication Monograph Retrieval",
                    $"Retrieved '{medOxana!.Item_Code}' ({medOxana.Display_Name}): Controlled Level={medOxana.Control_Level}, Strength={medOxana.StrengthDisplay}, Divisible={medOxana.Is_Divisible}, ActiveRxCount={medOxana.ActivePrescriptionsCount}");
            }
            else
            {
                AssertFail("6. Medication Monograph Retrieval",
                    $"Medication details lookup failed or case-insensitivity failed. OxanaNull={medOxana == null}, CaseInsensitiveNull={medCaseInsensitive == null}, IsControlled={medOxana?.Is_Controlled}, Level='{medOxana?.Control_Level}', DisplayName='{medOxana?.Display_Name}', UnknownNull={medUnknown == null}");
            }
        }
        catch (Exception ex)
        {
            AssertFail("6. Medication Monograph Retrieval", ex.Message);
        }

        // Test 7: Sub-16ms Instant Search Performance Benchmark
        try
        {
            var benchmarkCriteria = new ClinicalFilterCriteria
            {
                SearchQuery = "Teng Levemir",
                DateFilter = "All"
            };

            // Warmup
            service.FilterMarSchedule(snapshot, benchmarkCriteria);

            var sw = Stopwatch.StartNew();
            const int iterations = 1000;
            for (int i = 0; i < iterations; i++)
            {
                service.FilterMarSchedule(snapshot, benchmarkCriteria);
            }
            sw.Stop();

            double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            if (avgMs < 16.0)
            {
                AssertPass("7. Search Performance & Latency Benchmark",
                    $"{iterations} iterations completed in {sw.ElapsedMilliseconds}ms (Average: {avgMs:F3}ms per filter evaluation, << 16ms 60fps budget)");
            }
            else
            {
                AssertFail("7. Search Performance & Latency Benchmark",
                    $"Filter evaluation too slow: Average {avgMs:F2}ms (exceeds 16ms)");
            }
        }
        catch (Exception ex)
        {
            AssertFail("7. Search Performance & Latency Benchmark", ex.Message);
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine($" Summary: {passed} PASSED, {failed} FAILED out of {passed + failed} Tests");
        Console.WriteLine("================================================================================");

        return failed == 0 ? 0 : 1;
    }
}
