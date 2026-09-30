using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;
using Xunit;

namespace MedHubPrescriptionTool.Tests;

/// <summary>
/// Empirical Challenger verification test suite for Milestone 2.
/// Validates clinical snapshot consolidation, KPI metrics calculations,
/// NULL-safe status state machine, and patient station distributions
/// against live PostgreSQL amisdbV2.
/// </summary>
public class Milestone2_AdversarialKpiAndDistributionTests
{
    private readonly DbConnectionSettings _settings;
    private readonly IDatabaseConnectionFactory _factory;
    private readonly IDatabaseRepository _repository;
    private readonly ClinicalDataService _service;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public Milestone2_AdversarialKpiAndDistributionTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _settings = new DbConnectionSettings
        {
            Host = "127.0.0.1",
            Port = 5433,
            Database = "amisdbV2",
            Username = "postgres",
            Password = "Syscom@123",
            ReadOnly = true,
            TimeoutSeconds = 10,
            CommandTimeoutSeconds = 10
        };
        _factory = new DatabaseConnectionFactory(_settings);
        _repository = new DatabaseRepository(_factory);
        _service = new ClinicalDataService(_repository);
    }

    #region 1. Exact KPI Metrics Verification

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp01_LoadSnapshot_AssertExactKpiMetrics()
    {
        // 1. Load clinical snapshot via ClinicalDataService
        var snapshot = await _service.LoadClinicalSnapshotAsync();
        snapshot.Should().NotBeNull("Clinical snapshot must load successfully");

        // 2. Compute KPI summary
        var kpi = _service.ComputeKpiSummary(snapshot);

        // 3. Exact Metric Assertions matching ground truth invariants
        kpi.TotalInpatients.Should().Be(15, "Inpatients must equal 15 distinct active patients");
        kpi.TotalActiveOrders.Should().Be(31, "ActiveOrders must equal 31 active prescriptions");
        kpi.DispensedCount.Should().Be(13, "Dispensed must equal 13 confirmed doses");
        kpi.TotalPendingOrders.Should().Be(370, "TotalPending must equal 370");
        kpi.ReturnedCount.Should().Be(1, "Returned must equal 1 returned dose");
        kpi.WastedCount.Should().Be(1, "Wasted must equal 1 wasted dose");
        kpi.ControlledCount.Should().Be(143, "Controlled must equal 143 scheduled doses of controlled medications");

        // DuePending and FuturePending partition the 370 unadministered doses based on PostgreSQL CURRENT_TIMESTAMP.
        // At 12:00:00 reference time, DuePending=286 and FuturePending=84 (verified in Emp11).
        // Under live wall-clock time, DuePending + FuturePending must ALWAYS strictly equal 370.
        (kpi.DuePendingOrders + kpi.FuturePendingOrders).Should().Be(370, "DuePending + FuturePending must strictly sum to TotalPending (370)");

        // 4. Assert snapshot.KpiSummary matches computed values
        snapshot.KpiSummary.TotalInpatients.Should().Be(15);
        snapshot.KpiSummary.TotalActiveOrders.Should().Be(31);
        snapshot.KpiSummary.DispensedCount.Should().Be(13);
        snapshot.KpiSummary.TotalPendingOrders.Should().Be(370);
        snapshot.KpiSummary.ReturnedCount.Should().Be(1);
        snapshot.KpiSummary.WastedCount.Should().Be(1);
        snapshot.KpiSummary.ControlledCount.Should().Be(143);
        (snapshot.KpiSummary.DuePendingOrders + snapshot.KpiSummary.FuturePendingOrders).Should().Be(370);

        // 5. Total Schedule Partition Invariant: 13 + 370 (Due + Future) + 1 + 1 = 385
        snapshot.MarSchedules.Count.Should().Be(385, "Total schedules across all statuses must equal 385");
        int sumOfAllStatuses = kpi.DispensedCount + kpi.DuePendingOrders + kpi.FuturePendingOrders + kpi.ReturnedCount + kpi.WastedCount + kpi.CancelledCount;
        sumOfAllStatuses.Should().Be(385, "Sum of all mutually exclusive status categories must equal total MAR schedules (385)");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp02_KpiConsistency_WithDirectSqlRepository()
    {
        // Cross-check ClinicalDataService calculations with direct SQL aggregates in DatabaseRepository
        var snapshot = await _service.LoadClinicalSnapshotAsync();
        var kpiFromService = _service.ComputeKpiSummary(snapshot);
        var kpiFromSql = await _repository.GetKpiCountsAsync();

        kpiFromService.TotalInpatients.Should().Be(kpiFromSql.total_inpatients);
        kpiFromService.TotalActiveOrders.Should().Be(kpiFromSql.total_active_orders);
        kpiFromService.DispensedCount.Should().Be(kpiFromSql.total_dispensed);
        kpiFromService.DuePendingOrders.Should().Be(kpiFromSql.due_pending_orders);
        kpiFromService.TotalPendingOrders.Should().Be(kpiFromSql.total_pending_orders);
    }

    #endregion

    #region 2. Patient Grouping Across Stations

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp03_AssertPatientGroupingAcrossStations_8A_5A_ICU()
    {
        var snapshot = await _service.LoadClinicalSnapshotAsync();

        // 1. Overall encounter count vs distinct patients
        snapshot.Patients.Count.Should().Be(16, "Snapshot must hydrate 16 active inpatient encounters");
        int distinctPatients = snapshot.Patients.Select(p => p.Patient_Id).Distinct().Count();
        distinctPatients.Should().Be(15, "16 encounters belong to 15 unique patients (1 patient has 2 encounters)");

        // 2. Exact station grouping counts
        var station8A = _service.GetPatientOverviewList(snapshot, "8A");
        var station5A = _service.GetPatientOverviewList(snapshot, "5A");
        var stationIcu = _service.GetPatientOverviewList(snapshot, "ICU");

        station8A.Count.Should().Be(7, "Care Station 8A must contain exactly 7 patient encounters");
        station5A.Count.Should().Be(6, "Care Station 5A must contain exactly 6 patient encounters");
        stationIcu.Count.Should().Be(3, "Care Station ICU must contain exactly 3 patient encounters");

        // 3. Sum of station groups equals total encounters
        (station8A.Count + station5A.Count + stationIcu.Count).Should().Be(16);

        // 4. Case-insensitive station code filtering resilience
        var station8aLower = _service.GetPatientOverviewList(snapshot, "8a");
        station8aLower.Count.Should().Be(7, "Filtering by lowercase '8a' must also return 7 encounters");

        var stationIcuLower = _service.GetPatientOverviewList(snapshot, "icu");
        stationIcuLower.Count.Should().Be(3, "Filtering by lowercase 'icu' must also return 3 encounters");

        var allFiltered = _service.GetPatientOverviewList(snapshot, "ALL");
        allFiltered.Count.Should().Be(16, "'ALL' filter must return all 16 encounters");
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp04_DeepDiveDuplicatePatientEncounter_Verification()
    {
        var snapshot = await _service.LoadClinicalSnapshotAsync();

        // Identify the exact patient with duplicate encounters
        var duplicateGroups = snapshot.Patients
            .GroupBy(p => p.Patient_Id)
            .Where(g => g.Count() > 1)
            .ToList();

        duplicateGroups.Should().HaveCount(1, "Exactly one patient has multiple active encounters");
        var duplicatePatient = duplicateGroups.Single();
        duplicatePatient.Count().Should().Be(2, "The patient has exactly 2 active encounters");

        var enc1 = duplicatePatient.First();
        var enc2 = duplicatePatient.Last();

        _output.WriteLine($"Duplicate Patient MRN: {enc1.Patient_Mrn}, Name: {enc1.FullName}");
        _output.WriteLine($"Enc 1: Case={enc1.Case_No}, Station={enc1.Station_Code}, Bed={enc1.Bed_Code}");
        _output.WriteLine($"Enc 2: Case={enc2.Case_No}, Station={enc2.Station_Code}, Bed={enc2.Bed_Code}");

        enc1.Patient_Mrn.Should().Be(enc2.Patient_Mrn, "Both encounters must share the same MRN");
        enc1.Case_No.Should().NotBe(enc2.Case_No, "The two encounters must have distinct Case numbers");
    }

    #endregion

    #region 3. Status State Machine & Precedence Stress Tests

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void Emp05_StatusPrecedence_AdversarialScenarios()
    {
        var baseTime = new DateTime(2026, 9, 24, 12, 0, 0);

        // Scenario 1: Return overrides Confirmed_Time and Dispense_Status = 'Y'
        var statusRet = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: "N",
            confirmedTime: baseTime.AddHours(-1),
            scheduleStatus: "01",
            orderStatus: "01",
            scheduleDate: baseTime.Date,
            scheduleTime: baseTime.TimeOfDay,
            returnTime: baseTime,
            returnQty: 1.0m,
            referenceTime: baseTime);
        statusRet.Should().Be(ClinicalStatusEvaluator.StatusReturned);

        // Scenario 2: Waste overrides Confirmed_Time
        var statusWaste = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: "N",
            confirmedTime: baseTime.AddHours(-1),
            scheduleStatus: "01",
            orderStatus: "01",
            scheduleDate: baseTime.Date,
            scheduleTime: baseTime.TimeOfDay,
            wasteTime: baseTime,
            wasteQty: 1.0m,
            referenceTime: baseTime);
        statusWaste.Should().Be(ClinicalStatusEvaluator.StatusWasted);

        // Scenario 3: Cancelled schedule (Schedule_Status = 99) overrides Confirmed_Time
        var statusSchedCancel = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: null,
            confirmedTime: baseTime.AddHours(-1),
            scheduleStatus: "99",
            orderStatus: "01",
            scheduleDate: baseTime.Date,
            scheduleTime: baseTime.TimeOfDay,
            referenceTime: baseTime);
        statusSchedCancel.Should().Be(ClinicalStatusEvaluator.StatusCancelled);

        // Scenario 4: Cancelled prescription (Order_Status = 99) overrides Confirmed_Time
        var statusOrderCancel = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: null,
            confirmedTime: baseTime.AddHours(-1),
            scheduleStatus: "01",
            orderStatus: "99",
            scheduleDate: baseTime.Date,
            scheduleTime: baseTime.TimeOfDay,
            referenceTime: baseTime);
        statusOrderCancel.Should().Be(ClinicalStatusEvaluator.StatusCancelled);

        // Scenario 5: Dispense_Status = 'Y' without Confirmed_Time is Dispensed
        var statusDispOnly = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: "Y",
            confirmedTime: null,
            scheduleStatus: "01",
            orderStatus: "01",
            scheduleDate: baseTime.Date,
            scheduleTime: baseTime.TimeOfDay,
            referenceTime: baseTime);
        statusDispOnly.Should().Be(ClinicalStatusEvaluator.StatusDispensed);

        // Scenario 6: Confirmed_Time without Dispense_Status is Dispensed
        var statusConfOnly = ClinicalStatusEvaluator.EvaluateStatus(
            dispenseStatus: null,
            confirmedTime: baseTime,
            scheduleStatus: "01",
            orderStatus: "01",
            scheduleDate: baseTime.Date,
            scheduleTime: baseTime.TimeOfDay,
            referenceTime: baseTime);
        statusConfOnly.Should().Be(ClinicalStatusEvaluator.StatusDispensed);
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp06_VerifyLiveExceptionRecords_ReturnedAndWasted()
    {
        var snapshot = await _service.LoadClinicalSnapshotAsync();

        // 1 Returned record
        var returnedItems = snapshot.MarSchedules
            .Where(s => s.Dispense_Status == ClinicalStatusEvaluator.StatusReturned)
            .ToList();
        returnedItems.Should().HaveCount(1, "Exactly 1 returned record in live database");
        var ret = returnedItems.Single();
        ret.Return_Time.Should().NotBeNull();
        ret.Return_Reason_Name.Should().Contain("病人禁食");

        // 1 Wasted record
        var wastedItems = snapshot.MarSchedules
            .Where(s => s.Dispense_Status == ClinicalStatusEvaluator.StatusWasted)
            .ToList();
        wastedItems.Should().HaveCount(1, "Exactly 1 wasted record in live database");
        var waste = wastedItems.Single();
        waste.Waste_Time.Should().NotBeNull();
        waste.Waste_Qty.Should().Be(1.00m);
    }

    #endregion

    #region 4. Concurrency & Determinism Stress Tests

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp07_ConcurrentSnapshotLoading_ThreadSafetyAndDeterminism()
    {
        // Fire 10 concurrent requests to load snapshot
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _service.LoadClinicalSnapshotAsync())
            .ToList();

        var snapshots = await Task.WhenAll(tasks);

        snapshots.Should().HaveCount(10);
        foreach (var s in snapshots)
        {
            s.Patients.Count.Should().Be(16);
            s.MarSchedules.Count.Should().Be(385);
            s.ActivePrescriptions.Count.Should().Be(31);
            s.Medications.Count.Should().Be(114);

            var kpi = _service.ComputeKpiSummary(s);
            kpi.TotalInpatients.Should().Be(15);
            kpi.TotalActiveOrders.Should().Be(31);
            kpi.DispensedCount.Should().Be(13);
            kpi.TotalPendingOrders.Should().Be(370);
            (kpi.DuePendingOrders + kpi.FuturePendingOrders).Should().Be(370);
            kpi.ControlledCount.Should().Be(143);
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void Emp08_NullSnapshot_ThrowsArgumentNullException()
    {
        var act = () => _service.ComputeKpiSummary(null!);
        act.Should().Throw<ArgumentNullException>();

        var act2 = () => _service.GetPatientOverviewList(null!);
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => _service.FilterMarSchedule(null!, new ClinicalFilterCriteria());
        act3.Should().Throw<ArgumentNullException>();

        var act4 = () => _service.GetMedicationDetails(null!, "OXANA");
        act4.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public void Emp09_EmptySnapshot_YieldsZeroKpiWithoutCrashing()
    {
        var emptySnapshot = new ClinicalDataSnapshot();
        var kpi = _service.ComputeKpiSummary(emptySnapshot);

        kpi.TotalInpatients.Should().Be(0);
        kpi.TotalActiveOrders.Should().Be(0);
        kpi.DispensedCount.Should().Be(0);
        kpi.DuePendingOrders.Should().Be(0);
        kpi.FuturePendingOrders.Should().Be(0);
        kpi.TotalPendingOrders.Should().Be(0);
        kpi.ReturnedCount.Should().Be(0);
        kpi.WastedCount.Should().Be(0);
        kpi.CancelledCount.Should().Be(0);
        kpi.ControlledCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp10_InspectScheduleTimestampsDistribution()
    {
        var snapshot = await _service.LoadClinicalSnapshotAsync();
        _output.WriteLine($"Current DateTime.Now: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var dates = snapshot.MarSchedules.Select(s => s.Schedule_Date.Date).Distinct().OrderBy(d => d).ToList();
        _output.WriteLine($"Distinct Schedule Dates: {string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd")))}");

        var schedulesByDate = snapshot.MarSchedules.GroupBy(s => s.Schedule_Date.Date).OrderBy(g => g.Key);
        foreach (var g in schedulesByDate)
        {
            _output.WriteLine($"Date {g.Key:yyyy-MM-dd}: {g.Count()} schedules");
        }

        var unadministered = snapshot.MarSchedules
            .Where(s => s.Dispense_Status.StartsWith("Pending", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.ScheduledDateTime)
            .ToList();

        _output.WriteLine($"Total Pending (Unadministered): {unadministered.Count}");

        // Group unadministered by hour
        var byHour = unadministered.GroupBy(s => s.ScheduledDateTime.Hour).OrderBy(g => g.Key);
        foreach (var h in byHour)
        {
            _output.WriteLine($"  Hour {h.Key:D2}:00 -> {h.Count()} doses (Status: {string.Join(", ", h.Select(x => x.Dispense_Status).Distinct())})");
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM2")]
    public async Task Emp11_EvaluateStatusWithFixedReferenceTime_12Noon()
    {
        // Fetch raw MAR schedule records directly from repository
        var rawMar = await _repository.GetMarScheduleDataAsync();
        var activeDate = rawMar.Max(r => r.Schedule_Date.Date);
        var referenceTime = activeDate.AddHours(12);

        // Re-evaluate each schedule using ClinicalStatusEvaluator with referenceTime = 12:00:00
        var evaluatedStatuses = rawMar.Select(r => ClinicalStatusEvaluator.EvaluateStatus(r, referenceTime)).ToList();

        int dispensed = evaluatedStatuses.Count(s => s == ClinicalStatusEvaluator.StatusDispensed);
        int returned = evaluatedStatuses.Count(s => s == ClinicalStatusEvaluator.StatusReturned);
        int wasted = evaluatedStatuses.Count(s => s == ClinicalStatusEvaluator.StatusWasted);
        int duePending = evaluatedStatuses.Count(s => s == ClinicalStatusEvaluator.StatusPendingDue);
        int futurePending = evaluatedStatuses.Count(s => s == ClinicalStatusEvaluator.StatusPendingFuture);
        int totalPending = duePending + futurePending;

        _output.WriteLine($"Evaluated at 12:00:00 -> Dispensed: {dispensed}, Ret: {returned}, Waste: {wasted}, Due: {duePending}, Future: {futurePending}, TotalPending: {totalPending}");

        dispensed.Should().Be(13);
        returned.Should().Be(1);
        wasted.Should().Be(1);
        duePending.Should().Be(286, "At 12:00:00, DuePending is exactly 286!");
        futurePending.Should().Be(84, "At 12:00:00, FuturePending is exactly 84!");
        totalPending.Should().Be(370);
    }

    #endregion
}


