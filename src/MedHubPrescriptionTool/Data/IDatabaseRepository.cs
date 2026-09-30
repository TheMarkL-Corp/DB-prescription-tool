using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Models;

namespace MedHubPrescriptionTool.Data;

/// <summary>
/// Data repository contract for accessing MedHub PostgreSQL database in strictly read-only mode.
/// Satisfies both PROJECT.md interface contract and DISPATCH.md specifications.
/// </summary>
public interface IDatabaseRepository
{
    // Connectivity & Health
    Task<bool> TestConnectionAsync(DbConnectionSettings settings);
    Task<ConnectionTestResult> TestConnectionDetailsAsync(DbConnectionSettings? settings = null, CancellationToken cancellationToken = default);

    // Canonical Clinical Queries (Explorer 1 § 7)
    Task<List<MarScheduleRawRecord>> GetMarScheduleDataAsync(CancellationToken cancellationToken = default);
    Task<List<MarScheduleRawRecord>> GetRawMarScheduleAsync(CancellationToken cancellationToken = default);

    Task<List<PatientEncounterRawRecord>> GetPatientEncountersAsync(CancellationToken cancellationToken = default);
    Task<List<PatientEncounterRawRecord>> GetRawPatientEncountersAsync(CancellationToken cancellationToken = default);
    Task<List<PatientRawRecord>> GetPatientMasterDataAsync(CancellationToken cancellationToken = default);

    Task<KpiRawCounts> GetKpiCountsAsync(CancellationToken cancellationToken = default);
    Task<KpiRawCounts> GetRawKpiCountsAsync(CancellationToken cancellationToken = default);

    // Organizational / Location Lookups
    Task<List<CareStationRawRecord>> GetCareStationsAsync(CancellationToken cancellationToken = default);
    Task<List<RoomBedRawRecord>> GetRoomsBedsAsync(CancellationToken cancellationToken = default);

    // Direct 8 Core Tables + Users Inspection
    Task<List<PatientRawRecord>> GetAllPatientsAsync(CancellationToken cancellationToken = default);
    Task<List<EncounterRawRecord>> GetAllEncountersAsync(CancellationToken cancellationToken = default);
    Task<List<PrescriptionRawRecord>> GetAllPrescriptionsAsync(CancellationToken cancellationToken = default);
    Task<List<ScheduleRawRecord>> GetAllSchedulesAsync(CancellationToken cancellationToken = default);
    Task<List<MedicationRawRecord>> GetAllMedicationsAsync(CancellationToken cancellationToken = default);
    Task<List<DispenseRawRecord>> GetAllDispensesAsync(CancellationToken cancellationToken = default);
    Task<List<UserRawRecord>> GetAllUsersAsync(CancellationToken cancellationToken = default);
}
