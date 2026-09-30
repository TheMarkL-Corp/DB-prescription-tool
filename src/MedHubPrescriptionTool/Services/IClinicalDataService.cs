using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Models;

namespace MedHubPrescriptionTool.Services;

/// <summary>
/// Clinical business logic and data aggregation service contract.
/// Fulfills PROJECT.md § Interface Contracts (ClinicalDataService ↔ UI IPC Bridge / ExcelExportService).
/// </summary>
public interface IClinicalDataService
{
    /// <summary>
    /// Loads and consolidates a fresh clinical data snapshot from MedHub database via IDatabaseRepository.
    /// Safely joins across Patients, Encounter, Prescriptions, Schedule, Medications, Dispenses, Stations, Beds, and Users.
    /// </summary>
    Task<ClinicalDataSnapshot> LoadClinicalSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes dashboard Top KPI metrics summary from the consolidated snapshot.
    /// </summary>
    KpiSummaryDto ComputeKpiSummary(ClinicalDataSnapshot snapshot);

    /// <summary>
    /// Returns patient directory grouped and filtered by Care Station and Bed for Tab 1 Master-Detail view.
    /// </summary>
    List<PatientOverviewDto> GetPatientOverviewList(ClinicalDataSnapshot snapshot, string? stationFilter = null);

    /// <summary>
    /// Executes fast, multi-criteria fuzzy filtering on the MAR administration schedule timetable for Tab 2.
    /// </summary>
    List<MarScheduleItemDto> FilterMarSchedule(ClinicalDataSnapshot snapshot, ClinicalFilterCriteria criteria);

    /// <summary>
    /// Retrieves full clinical monograph specifications for a medication by item code.
    /// </summary>
    MedicationDetailDto? GetMedicationDetails(ClinicalDataSnapshot snapshot, string itemCode);
}
