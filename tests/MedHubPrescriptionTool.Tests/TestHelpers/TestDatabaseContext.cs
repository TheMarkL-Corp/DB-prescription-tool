using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace MedHubPrescriptionTool.Tests.TestHelpers
{
    public class TestDatabaseContext
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 5433;
        public const string DefaultDatabase = "amisdbV2";
        public const string DefaultUser = "postgres";
        public const string DefaultPassword = "Syscom@123";

        public static string DefaultConnectionString =>
            $"Host={DefaultHost};Port={DefaultPort};Database={DefaultDatabase};Username={DefaultUser};Password={DefaultPassword};Timeout=10;CommandTimeout=10;Options=-c default_transaction_read_only=on;";

        public static string BuildConnectionString(string host, int port, string database, string username, string password, bool readOnly = true)
        {
            var connStr = $"Host={host};Port={port};Database={database};Username={username};Password={password};Timeout=5;CommandTimeout=5;";
            if (readOnly)
            {
                connStr += "Options=-c default_transaction_read_only=on;";
            }
            return connStr;
        }

        public static async Task<NpgsqlConnection> CreateReadOnlyConnectionAsync(string? connectionString = null)
        {
            var conn = new NpgsqlConnection(connectionString ?? DefaultConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SET transaction_read_only = on;", conn);
            await cmd.ExecuteNonQueryAsync();
            return conn;
        }

        public static async Task<bool> IsDatabaseAvailableAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(DefaultConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) == 1;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<int> GetActiveInpatientCensusAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT e.""Patient_Id"")
                FROM ""Encounter"" e
                WHERE (e.""Encounter_Status"" = '1' OR e.""Encounter_Status"" = '01')
                  AND e.""Discharge_Date"" IS NULL;";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public static async Task<int> GetActiveOrdersCountAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT pr.""Med_Order_Id"")
                FROM ""Prescriptions"" pr
                WHERE pr.""Order_Status"" = '01';";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public static async Task<int> GetDispensedSchedulesCountAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT s.""Schedule_Id"")
                FROM ""Schedule"" s
                LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
                WHERE (d.""Status"" = 'Y' OR (COALESCE(d.""Status"", '') <> 'N' AND s.""Confirmed_Time"" IS NOT NULL))
                  AND COALESCE(s.""Schedule_Status"", '') <> '99';";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public static async Task<int> GetDuePendingSchedulesCountAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT s.""Schedule_Id"")
                FROM ""Schedule"" s
                JOIN ""Prescriptions"" pr ON s.""Med_Order_Id"" = pr.""Med_Order_Id""
                LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
                WHERE d.""Id"" IS NULL AND s.""Confirmed_Time"" IS NULL 
                  AND s.""Schedule_Status"" = '01' AND pr.""Order_Status"" = '01'
                  AND (s.""Schedule_Date"" + s.""Schedule_Time"") < CURRENT_TIMESTAMP;";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public static async Task<int> GetTotalPendingSchedulesCountAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT s.""Schedule_Id"")
                FROM ""Schedule"" s
                JOIN ""Prescriptions"" pr ON s.""Med_Order_Id"" = pr.""Med_Order_Id""
                LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
                WHERE d.""Id"" IS NULL AND s.""Confirmed_Time"" IS NULL 
                  AND s.""Schedule_Status"" = '01' AND pr.""Order_Status"" = '01';";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public static async Task<int> GetTotalSchedulesCountAsync(NpgsqlConnection conn)
        {
            const string sql = @"SELECT COUNT(*) FROM ""Schedule"";";
            return await conn.ExecuteScalarAsync<int>(sql);
        }

        public static async Task<List<MarScheduleItemDto>> FetchUnifiedMarRecordsAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT 
                    s.""Schedule_Id"" AS ScheduleId,
                    pr.""Med_Order_Id"" AS MedOrderId,
                    s.""Schedule_Date"" AS ScheduleDate,
                    s.""Schedule_Time"" AS ScheduleTime,
                    p.""Patient_Mrn"" AS PatientMrn,
                    p.""Last_Name"" || ', ' || p.""First_Name"" AS PatientName,
                    cs.""Station_Name"" AS CareStation,
                    rb.""Rb_Name"" AS RoomBed,
                    m.""Item_Code"" AS MedicationCode,
                    COALESCE(m.""Display_Name"", 'Unknown') AS DisplayName,
                    m.""Generic_Name"" AS GenericName,
                    m.""Atc_Code"" AS AtcCode,
                    pr.""Dosage_Ordered_Value"" AS DosageOrderedValue,
                    pr.""Dosage_Ordered_Unit"" AS DosageOrderedUnit,
                    pr.""Frequency"" AS Frequency,
                    pr.""Route"" AS Route,
                    d.""Qty"" AS DispensedQty,
                    d.""Status_Time"" AS StatusTime,
                    u_disp.""Employee_Name"" AS AdministeringNurse,
                    COALESCE(m.""isHighAlert"", false) AS IsHighAlert,
                    COALESCE(m.""isRefrigerationRequired"", false) AS IsRefrigerationRequired,
                    COALESCE(m.""Is_Controlled"", false) AS IsControlled,
                    m.""Control_Level"" AS ControlLevel,
                    COALESCE(m.""Wintness_Required"" = 'Y', false) AS WitnessRequired,
                    pr.""Clinical_Indication"" AS ClinicalIndication,
                    d.""Return_Reason_Id"" AS ReturnReason,
                    d.""Return_Qty"" AS ReturnQty,
                    d.""Waste_Qty"" AS WasteQty,
                    pr.""Order_Status"" AS OrderStatus,
                    s.""Schedule_Status"" AS ScheduleStatus,
                    CASE
                        WHEN d.""Status"" = 'N' AND (d.""Return_Time"" IS NOT NULL OR COALESCE(d.""Return_Qty"", 0) > 0) THEN 'Returned'
                        WHEN d.""Status"" = 'N' AND (d.""Waste_Time"" IS NOT NULL OR COALESCE(d.""Waste_Qty"", 0) > 0) THEN 'Wasted'
                        WHEN d.""Status"" = 'N' THEN 'Cancelled'
                        WHEN s.""Schedule_Status"" = '99' OR pr.""Order_Status"" = '99' THEN 'Cancelled'
                        WHEN d.""Status"" = 'Y' OR s.""Confirmed_Time"" IS NOT NULL THEN 'Dispensed'
                        WHEN (s.""Schedule_Date"" + s.""Schedule_Time"") < CURRENT_TIMESTAMP THEN 'Pending (Due)'
                        ELSE 'Pending (Future)'
                    END AS DispenseStatus
                FROM ""Schedule"" s
                JOIN ""Prescriptions"" pr ON s.""Med_Order_Id"" = pr.""Med_Order_Id""
                JOIN ""Encounter"" e ON pr.""Encounter_Id"" = e.""Encounter_Id""
                JOIN ""Patients"" p ON e.""Patient_Id"" = p.""Patient_Id""
                LEFT JOIN ""Medications"" m ON pr.""Medication_Id"" = m.""Medication_Id""
                LEFT JOIN ""Care_Stations"" cs ON e.""Station_Id"" = cs.""Station_Id""
                LEFT JOIN ""Rooms_Beds"" rb ON e.""Bed_Id"" = rb.""Bed_Id""
                LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
                LEFT JOIN ""Users"" u_disp ON d.""Nurse_Code"" = u_disp.""User_Id""
                ORDER BY s.""Schedule_Date"" ASC, s.""Schedule_Time"" ASC;";

            var records = await conn.QueryAsync<MarScheduleItemDto>(sql);
            return records.AsList();
        }

        public static async Task<List<ActivePrescriptionDto>> FetchActivePrescriptionsAsync(NpgsqlConnection conn)
        {
            const string sql = @"
                SELECT 
                    pr.""Med_Order_Id"" AS MedOrderId,
                    p.""Patient_Mrn"" AS PatientMrn,
                    e.""Case_No"" AS CaseNo,
                    p.""Last_Name"" || ' ' || p.""First_Name"" AS PatientName,
                    cs.""Station_Name"" AS CareStation,
                    rb.""Rb_Name"" AS RoomBed,
                    e.""Attending_Physician"" AS AttendingPhysician,
                    m.""Item_Code"" AS MedicationCode,
                    COALESCE(m.""Display_Name"", 'Unknown') AS MedicationName,
                    pr.""Dosage_Ordered_Value"" || ' ' || pr.""Dosage_Ordered_Unit"" || ' ' || pr.""Frequency"" || ' ' || pr.""Route"" AS DosageInstruction,
                    pr.""Clinical_Indication"" AS ClinicalIndication,
                    pr.""Start_Datetime"" AS StartDatetime,
                    pr.""End_Datetime"" AS EndDatetime,
                    COUNT(s.""Schedule_Id"") AS TotalScheduledDoses,
                    COUNT(CASE WHEN d.""Status"" = 'Y' OR (COALESCE(d.""Status"", '') <> 'N' AND s.""Confirmed_Time"" IS NOT NULL) THEN 1 END) AS DispensedDoses,
                    COUNT(CASE WHEN d.""Id"" IS NULL AND s.""Confirmed_Time"" IS NULL AND s.""Schedule_Status"" = '01' THEN 1 END) AS PendingDoses,
                    COALESCE(m.""isHighAlert"", false) AS IsHighAlert
                FROM ""Prescriptions"" pr
                JOIN ""Encounter"" e ON pr.""Encounter_Id"" = e.""Encounter_Id""
                JOIN ""Patients"" p ON e.""Patient_Id"" = p.""Patient_Id""
                LEFT JOIN ""Medications"" m ON pr.""Medication_Id"" = m.""Medication_Id""
                LEFT JOIN ""Care_Stations"" cs ON e.""Station_Id"" = cs.""Station_Id""
                LEFT JOIN ""Rooms_Beds"" rb ON e.""Bed_Id"" = rb.""Bed_Id""
                LEFT JOIN ""Schedule"" s ON pr.""Med_Order_Id"" = s.""Med_Order_Id""
                LEFT JOIN ""AMiS_Patient_Medication_Dispense"" d ON s.""Schedule_Id"" = d.""Schedule_Id""
                WHERE pr.""Order_Status"" = '01'
                  AND e.""Encounter_Status"" IN ('1', '01')
                  AND e.""Discharge_Date"" IS NULL
                GROUP BY 
                    pr.""Med_Order_Id"", p.""Patient_Mrn"", e.""Case_No"", p.""Last_Name"", p.""First_Name"",
                    cs.""Station_Name"", rb.""Rb_Name"", e.""Attending_Physician"", m.""Item_Code"",
                    m.""Display_Name"", pr.""Dosage_Ordered_Value"", pr.""Dosage_Ordered_Unit"",
                    pr.""Frequency"", pr.""Route"", pr.""Clinical_Indication"", pr.""Start_Datetime"",
                    pr.""End_Datetime"", m.""isHighAlert""
                ORDER BY p.""Patient_Mrn"";";

            var records = await conn.QueryAsync<ActivePrescriptionDto>(sql);
            return records.AsList();
        }
    }
}
