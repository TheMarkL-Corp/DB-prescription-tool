using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Models;
using Npgsql;

namespace MedHubPrescriptionTool.Data;

/// <summary>
/// PostgreSQL implementation of IDatabaseRepository.
/// Enforces multi-layer read-only semantics:
/// 1. Connection string: Options=-c default_transaction_read_only=on
/// 2. Session level: SET transaction_read_only = on;
/// 3. Transaction level: ReadCommitted read-only transactions
/// 4. Query level: Exclusively SELECT statements
/// </summary>
public class DatabaseRepository : IDatabaseRepository
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public DatabaseRepository(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    #region Connectivity & Testing

    public async Task<bool> TestConnectionAsync(DbConnectionSettings settings)
    {
        var result = await _connectionFactory.TestConnectionAsync(settings).ConfigureAwait(false);
        return result.Success;
    }

    public Task<ConnectionTestResult> TestConnectionDetailsAsync(DbConnectionSettings? settings = null, CancellationToken cancellationToken = default)
    {
        return _connectionFactory.TestConnectionAsync(settings, cancellationToken);
    }

    #endregion

    #region Canonical Queries (Explorer 1 § 7)

    private const string MarScheduleSql = @"
SELECT 
    p.""Patient_Id"",
    p.""Patient_Mrn"",
    p.""First_Name"",
    p.""Last_Name"",
    p.""Sex"",
    p.""Date_Of_Birth"",
    p.""Allergy"",
    p.""Allergy_Desc"",
    p.""DrugWarn"",
    p.""DrugWarn_Desc"",
    e.""Encounter_Id"",
    e.""Case_No"",
    e.""Admission_Date"",
    e.""Discharge_Date"",
    e.""Encounter_Status"",
    e.""Attending_Physician"",
    cs.""Station_Id"",
    cs.""Station_Code"",
    cs.""Station_Name"",
    rb.""Bed_Id"",
    rb.""Rb_Code"",
    rb.""Rb_Name"",
    pr.""Med_Order_Id"",
    pr.""Dosage_Ordered_Value"",
    pr.""Dosage_Ordered_Unit"",
    pr.""Frequency"",
    pr.""Route"",
    pr.""Order_Status"",
    pr.""Start_Datetime"",
    pr.""End_Datetime"",
    pr.""Clinical_Indication"",
    pr.""Instruction_Note"",
    m.""Medication_Id"",
    m.""Item_Code"",
    m.""Generic_Name"",
    m.""Display_Name"",
    m.""Atc_Code"",
    m.""Form"",
    m.""isHighAlert"",
    m.""isRefrigerationRequired"",
    m.""Is_Controlled"",
    m.""Control_Level"",
    m.""Wintness_Required"",
    s.""Schedule_Id"",
    s.""Schedule_Date"",
    s.""Schedule_Time"",
    s.""Calc_Dispense_Qty"",
    s.""Dispense_Qty_Unit"",
    s.""Schedule_Status"",
    s.""Processed_Time"",
    s.""Confirmed_Time"",
    d.""Id"" AS ""Dispense_Id"",
    d.""Status"" AS ""Dispense_Status"",
    d.""Status_Time"" AS ""Dispense_Time"",
    d.""Return_Time"",
    d.""Return_Qty"",
    d.""Waste_Time"",
    d.""Waste_Qty"",
    d.""Cancel_Reason_Id"",
    d.""Return_Reason_Id"",
    d.""Waste_Reason_Id"",
    u_disp.""Employee_Name"" AS ""Dispense_Nurse_Name"",
    u_conf.""Employee_Name"" AS ""Confirmed_Nurse_Name"",
    CASE
        WHEN d.""Status"" = 'N' AND (d.""Return_Time"" IS NOT NULL OR COALESCE(d.""Return_Qty"", 0) > 0) THEN 'Returned'
        WHEN d.""Status"" = 'N' AND (d.""Waste_Time"" IS NOT NULL OR COALESCE(d.""Waste_Qty"", 0) > 0) THEN 'Wasted'
        WHEN d.""Status"" = 'N' THEN 'Cancelled'
        WHEN s.""Schedule_Status"" = '99' OR pr.""Order_Status"" = '99' THEN 'Cancelled'
        WHEN d.""Status"" = 'Y' OR s.""Confirmed_Time"" IS NOT NULL THEN 'Dispensed'
        WHEN (s.""Schedule_Date"" + s.""Schedule_Time"") < CURRENT_TIMESTAMP THEN 'Pending (Due)'
        ELSE 'Pending (Future)'
    END AS ""Computed_Status""
FROM ""Schedule"" s
JOIN ""Prescriptions"" pr ON s.""Med_Order_Id"" = pr.""Med_Order_Id""
JOIN ""Encounter"" e ON pr.""Encounter_Id"" = e.""Encounter_Id""
JOIN ""Patients"" p ON e.""Patient_Id"" = p.""Patient_Id""
LEFT JOIN ""Medications"" m ON pr.""Medication_Id"" = m.""Medication_Id""
LEFT JOIN ""Care_Stations"" cs ON e.""Station_Id"" = cs.""Station_Id""
LEFT JOIN ""Rooms_Beds"" rb ON e.""Bed_Id"" = rb.""Bed_Id""
LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
LEFT JOIN ""Users"" u_disp ON d.""Nurse_Code"" = u_disp.""User_Id""
LEFT JOIN ""Users"" u_conf ON s.""Confirmed_User"" = u_conf.""User_Id""
ORDER BY s.""Schedule_Date"" DESC, s.""Schedule_Time"" DESC, p.""Patient_Mrn"";";

    public async Task<List<MarScheduleRawRecord>> GetMarScheduleDataAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var items = await conn.QueryAsync<MarScheduleRawRecord>(
            new CommandDefinition(MarScheduleSql, transaction: tx, cancellationToken: cancellationToken, commandTimeout: 30)
        ).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return items.AsList();
    }

    public Task<List<MarScheduleRawRecord>> GetRawMarScheduleAsync(CancellationToken cancellationToken = default)
        => GetMarScheduleDataAsync(cancellationToken);

    private const string PatientEncountersSql = @"
SELECT 
    p.""Patient_Id"",
    p.""Patient_Mrn"",
    p.""First_Name"",
    p.""Last_Name"",
    p.""Sex"",
    p.""Date_Of_Birth"",
    p.""Weight_Kg"",
    p.""Height_Cm"",
    p.""Allergy"",
    p.""Allergy_Desc"",
    p.""DrugWarn"",
    p.""DrugWarn_Desc"",
    e.""Encounter_Id"",
    e.""Case_No"",
    e.""Admission_Date"",
    e.""Discharge_Date"",
    e.""Encounter_Status"",
    e.""Attending_Physician"",
    cs.""Station_Id"",
    cs.""Station_Code"",
    cs.""Station_Name"",
    rb.""Bed_Id"",
    rb.""Rb_Code"",
    rb.""Rb_Name"",
    count(DISTINCT pr.""Med_Order_Id"")::int AS ""Total_Prescriptions"",
    count(s.""Schedule_Id"")::int AS ""Total_Scheduled_Doses"",
    count(s.""Schedule_Id"") FILTER (
        WHERE d.""Status"" = 'Y' OR (COALESCE(d.""Status"", '') <> 'N' AND s.""Confirmed_Time"" IS NOT NULL)
    )::int AS ""Dispensed_Doses"",
    count(s.""Schedule_Id"") FILTER (
        WHERE d.""Id"" IS NULL AND s.""Confirmed_Time"" IS NULL AND s.""Schedule_Status"" = '01'
    )::int AS ""Pending_Doses""
FROM ""Encounter"" e
JOIN ""Patients"" p ON e.""Patient_Id"" = p.""Patient_Id""
LEFT JOIN ""Care_Stations"" cs ON e.""Station_Id"" = cs.""Station_Id""
LEFT JOIN ""Rooms_Beds"" rb ON e.""Bed_Id"" = rb.""Bed_Id""
LEFT JOIN ""Prescriptions"" pr ON e.""Encounter_Id"" = pr.""Encounter_Id"" AND pr.""Order_Status"" = '01'
LEFT JOIN ""Schedule"" s ON pr.""Med_Order_Id"" = s.""Med_Order_Id""
LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
WHERE e.""Encounter_Status"" IN ('1', '01') AND e.""Discharge_Date"" IS NULL
GROUP BY 
    p.""Patient_Id"", p.""Patient_Mrn"", p.""First_Name"", p.""Last_Name"", p.""Sex"", p.""Date_Of_Birth"",
    p.""Weight_Kg"", p.""Height_Cm"", p.""Allergy"", p.""Allergy_Desc"", p.""DrugWarn"", p.""DrugWarn_Desc"",
    e.""Encounter_Id"", e.""Case_No"", e.""Admission_Date"", e.""Discharge_Date"", e.""Encounter_Status"",
    e.""Attending_Physician"", cs.""Station_Id"", cs.""Station_Code"", cs.""Station_Name"",
    rb.""Bed_Id"", rb.""Rb_Code"", rb.""Rb_Name""
ORDER BY cs.""Station_Code"", rb.""Rb_Code"";";

    public async Task<List<PatientEncounterRawRecord>> GetPatientEncountersAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var items = await conn.QueryAsync<PatientEncounterRawRecord>(
            new CommandDefinition(PatientEncountersSql, transaction: tx, cancellationToken: cancellationToken, commandTimeout: 30)
        ).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return items.AsList();
    }

    public Task<List<PatientEncounterRawRecord>> GetRawPatientEncountersAsync(CancellationToken cancellationToken = default)
        => GetPatientEncountersAsync(cancellationToken);

    public async Task<List<PatientRawRecord>> GetPatientMasterDataAsync(CancellationToken cancellationToken = default)
    {
        return await GetAllPatientsAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string KpiCountsSql = @"
SELECT 
    (SELECT count(DISTINCT ""Patient_Id"")::int 
     FROM ""Encounter"" 
     WHERE ""Encounter_Status"" IN ('1', '01') AND ""Discharge_Date"" IS NULL) AS total_inpatients,
     
    (SELECT count(*)::int 
     FROM ""Prescriptions"" 
     WHERE ""Order_Status"" = '01') AS total_active_orders,
     
    (SELECT count(*)::int 
     FROM ""Schedule"" s 
     LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
     WHERE (d.""Status"" = 'Y' OR (COALESCE(d.""Status"", '') <> 'N' AND s.""Confirmed_Time"" IS NOT NULL))
       AND COALESCE(s.""Schedule_Status"", '') <> '99') AS total_dispensed,
       
    (SELECT count(*)::int 
     FROM ""Schedule"" s
     JOIN ""Prescriptions"" pr ON s.""Med_Order_Id"" = pr.""Med_Order_Id""
     LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
     WHERE d.""Id"" IS NULL AND s.""Confirmed_Time"" IS NULL 
       AND s.""Schedule_Status"" = '01' AND pr.""Order_Status"" = '01'
       AND (s.""Schedule_Date"" + s.""Schedule_Time"") < CURRENT_TIMESTAMP) AS due_pending_orders,

    (SELECT count(*)::int 
     FROM ""Schedule"" s
     JOIN ""Prescriptions"" pr ON s.""Med_Order_Id"" = pr.""Med_Order_Id""
     LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
     WHERE d.""Id"" IS NULL AND s.""Confirmed_Time"" IS NULL 
       AND s.""Schedule_Status"" = '01' AND pr.""Order_Status"" = '01') AS total_pending_orders;";

    public async Task<KpiRawCounts> GetKpiCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var counts = await conn.QuerySingleAsync<KpiRawCounts>(
            new CommandDefinition(KpiCountsSql, transaction: tx, cancellationToken: cancellationToken, commandTimeout: 30)
        ).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return counts;
    }

    public Task<KpiRawCounts> GetRawKpiCountsAsync(CancellationToken cancellationToken = default)
        => GetKpiCountsAsync(cancellationToken);

    #endregion

    #region Organizational Lookups

    public async Task<List<CareStationRawRecord>> GetCareStationsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Care_Stations"" ORDER BY ""Station_Code"";";
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var stations = await conn.QueryAsync<CareStationRawRecord>(
            new CommandDefinition(sql, transaction: tx, cancellationToken: cancellationToken, commandTimeout: 30)
        ).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return stations.AsList();
    }

    public async Task<List<RoomBedRawRecord>> GetRoomsBedsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Rooms_Beds"" ORDER BY ""Rb_Code"";";
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var beds = await conn.QueryAsync<RoomBedRawRecord>(
            new CommandDefinition(sql, transaction: tx, cancellationToken: cancellationToken, commandTimeout: 30)
        ).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return beds.AsList();
    }

    #endregion

    #region Direct 8 Core Tables + Users Inspection

    public async Task<List<PatientRawRecord>> GetAllPatientsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Patients"" ORDER BY ""Patient_Mrn"";";
        return await ExecuteListQueryAsync<PatientRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<EncounterRawRecord>> GetAllEncountersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Encounter"" ORDER BY ""Admission_Date"" DESC;";
        return await ExecuteListQueryAsync<EncounterRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<PrescriptionRawRecord>> GetAllPrescriptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Prescriptions"" ORDER BY ""Create_Time"" DESC;";
        return await ExecuteListQueryAsync<PrescriptionRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<ScheduleRawRecord>> GetAllSchedulesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Schedule"" ORDER BY ""Schedule_Date"" DESC, ""Schedule_Time"" DESC;";
        return await ExecuteListQueryAsync<ScheduleRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<MedicationRawRecord>> GetAllMedicationsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Medications"" ORDER BY ""Item_Code"";";
        return await ExecuteListQueryAsync<MedicationRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<DispenseRawRecord>> GetAllDispensesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""AMiS_Patient_Medication_Dispense"" ORDER BY ""Status_Time"" DESC;";
        return await ExecuteListQueryAsync<DispenseRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<UserRawRecord>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT * FROM ""Users"" ORDER BY ""Employee_Code"";";
        return await ExecuteListQueryAsync<UserRawRecord>(sql, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<T>> ExecuteListQueryAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(readOnly: true, cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var rows = await conn.QueryAsync<T>(
            new CommandDefinition(sql, transaction: tx, cancellationToken: cancellationToken, commandTimeout: 30)
        ).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return rows.AsList();
    }

    #endregion
}
