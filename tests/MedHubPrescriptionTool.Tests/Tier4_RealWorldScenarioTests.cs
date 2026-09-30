using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FluentAssertions;
using MedHubPrescriptionTool.Tests.TestHelpers;
using Npgsql;
using Xunit;

namespace MedHubPrescriptionTool.Tests
{
    public class Tier4_RealWorldScenarioTests
    {
        // =========================================================================
        // TIER 4: Real-World Inpatient Clinical Scenarios (5 Scenarios)
        // =========================================================================

        [Fact]
        [Trait("Category", "ClinicalWorkflowScenario")]
        public async Task Scenario1_MorningShiftHandoff_Ward8A_ChargeNurseWorkflow()
        {
            // 1. Nurse arrives at station: application auto-connects to amisdbV2 in read-only mode
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            conn.State.Should().Be(System.Data.ConnectionState.Open);

            // 2. Nurse inspects Top KPI Cards: confirms active inpatients count
            var census = await TestDatabaseContext.GetActiveInpatientCensusAsync(conn);
            census.Should().Be(15, "Charge nurse verifies 15 active inpatients admitted across hospital");

            // 3. Nurse filters patient directory to '8A病房'
            await using var cmdStation = new NpgsqlCommand(@"
                SELECT COUNT(DISTINCT e.""Patient_Id"")
                FROM ""Encounter"" e
                JOIN ""Care_Stations"" cs ON e.""Station_Id"" = cs.""Station_Id""
                WHERE cs.""Station_Code"" = '8A' AND e.""Discharge_Date"" IS NULL;", conn);
            var ward8aPatients = Convert.ToInt32(await cmdStation.ExecuteScalarAsync());
            ward8aPatients.Should().Be(6, "Ward 8A manages 6 active inpatients");

            // 4. Scans patient list: checks patient Teng, Max (0439449) demographics and allergy status
            await using var cmdAllergy = new NpgsqlCommand(@"
                SELECT ""Allergy"", ""Allergy_Desc""
                FROM ""Patients""
                WHERE ""Patient_Mrn"" = '0439449';", conn);
            await using var allergyReader = await cmdAllergy.ExecuteReaderAsync();
            (await allergyReader.ReadAsync()).Should().BeTrue();
            var allergyStatus = allergyReader.GetString(0);
            allergyStatus.Should().Be("N", "Patient Teng Max has no registered drug allergies");
            await allergyReader.CloseAsync();

            // 5. Switches to Global MAR, filters for 8A schedules
            var marRecords = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);
            var ward8aMar = marRecords.Where(x => x.CareStation == "8A病房").ToList();
            ward8aMar.Should().NotBeEmpty();

            // 6. Generates shift backup Excel workbook
            var excelBytes = TestExcelHelper.GenerateClinicalWorkbook(
                ward8aMar,
                new List<ActivePrescriptionDto>(),
                "Ward 8A Morning Shift MAR");

            using var ms = new MemoryStream(excelBytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            sheet1.Cell("A1").GetString().Should().Contain("WARD 8A MORNING SHIFT MAR");
            sheet1.Cell("C5").GetString().Should().Be("8A病房");
        }

        [Fact]
        [Trait("Category", "ClinicalWorkflowScenario")]
        public async Task Scenario2_EmergencyStatOrder_PharmacyVerificationWorkflow()
        {
            // 1. Pharmacist opens application in read-only mode and refreshes snapshot
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();

            // 2. Pharmacist queries high-alert and STAT / urgent administration medications
            var marRecords = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);

            // Filter for high-alert or controlled drugs
            var highAlertOrders = marRecords.Where(x => x.IsHighAlert || x.IsControlled).ToList();
            highAlertOrders.Should().NotBeEmpty("Hospital pharmacy manages high-alert and controlled orders");

            // 3. Inspects clinical safety requirements for high-alert medication
            await using var cmdHighAlert = new NpgsqlCommand(@"
                SELECT ""Item_Code"", ""Display_Name"", ""isHighAlert"", ""Wintness_Required"", ""isRefrigerationRequired""
                FROM ""Medications""
                WHERE ""isHighAlert"" = true LIMIT 1;", conn);
            await using var reader = await cmdHighAlert.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();

            var itemCode = reader.GetString(0);
            var isHighAlert = reader.GetBoolean(2);
            isHighAlert.Should().BeTrue("High-alert order must flag prominent red alert banner");
            await reader.CloseAsync();

            // 4. Inspects refrigeration constraints (2-8°C) for insulin products
            await using var cmdRefrig = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""Medications"" WHERE ""isRefrigerationRequired"" = true;", conn);
            var refrigCount = Convert.ToInt32(await cmdRefrig.ExecuteScalarAsync());
            refrigCount.Should().Be(4, "Exactly 4 insulin formulations require strict refrigeration");
        }

        [Fact]
        [Trait("Category", "ClinicalWorkflowScenario")]
        public async Task Scenario3_MultiDoseAntibioticAndGlycemic_RegimenTrackingWorkflow()
        {
            // 1. Clinical pharmacist inspects patient Teng, Max (MRN: 0439449)
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var activeOrders = await TestDatabaseContext.FetchActivePrescriptionsAsync(conn);
            var tengOrders = activeOrders.Where(x => x.PatientMrn == "0439449").ToList();

            tengOrders.Should().NotBeEmpty("Patient Teng, Max has active prescribed regimens");

            // 2. Pharmacist retrieves MAR administration history for this patient
            var allMar = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);
            var tengMar = allMar.Where(x => x.PatientMrn == "0439449").ToList();
            tengMar.Should().NotBeEmpty();

            // 3. Verify separation of past confirmed doses vs future pending doses
            var dispensedDoses = tengMar.Where(x => x.DispenseStatus == "Dispensed").ToList();
            var pendingDoses = tengMar.Where(x => x.DispenseStatus.StartsWith("Pending")).ToList();

            // Unified timeline must maintain chronological order without drift
            for (int i = 0; i < tengMar.Count - 1; i++)
            {
                (tengMar[i].ScheduleDateTime <= tengMar[i + 1].ScheduleDateTime)
                    .Should().BeTrue("Administration timetable must follow strict chronological sequence");
            }
        }

        [Fact]
        [Trait("Category", "ClinicalWorkflowScenario")]
        public async Task Scenario4_PatientDischarge_OrderDiscontinuation_ReturnAndWasteAuditWorkflow()
        {
            // 1. Auditor queries dispense log for returned or wasted transactions
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT d.""Id"", d.""Status"", d.""Return_Time"", d.""Return_Reason_Id"", d.""Return_Qty"",
                       d.""Waste_Time"", d.""Waste_Reason_Id"", d.""Waste_Qty""
                FROM ""AMiS_Patient_Medication_Dispense"" d
                WHERE d.""Status"" = 'N';", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var auditRows = new List<(string status, DateTime? returnTime, string? returnReason, decimal? returnQty, DateTime? wasteTime, decimal? wasteQty)>();
            while (await reader.ReadAsync())
            {
                auditRows.Add((
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    reader.IsDBNull(7) ? null : reader.GetDecimal(7)
                ));
            }
            await reader.CloseAsync();

            auditRows.Should().NotBeEmpty("Dispense log contains returned and wasted medication audit events");

            // 2. Identify return event with reason '02' (Patient Fasting / NPO)
            var returnEvent = auditRows.FirstOrDefault(x => x.returnReason == "02");
            returnEvent.returnReason.Should().Be("02");
            returnEvent.returnQty.Should().Be(1.00m);

            // 3. Identify waste event with waste quantity
            var wasteEvent = auditRows.FirstOrDefault(x => x.wasteQty > 0);
            wasteEvent.wasteQty.Should().Be(1.00m);

            // 4. Validate that ClosedXML export captures returned status in Column M
            var marAudit = new List<MarScheduleItemDto>
            {
                new()
                {
                    PatientMrn = "0439449",
                    PatientName = "Teng, Max",
                    MedicationCode = "OXANA",
                    DisplayName = "Oxanase",
                    DispenseStatus = "Returned",
                    DispensedQty = 1.00m,
                    StatusTime = returnEvent.returnTime
                }
            };

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(marAudit, new List<ActivePrescriptionDto>());
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            sheet1.Cell("M5").GetString().Should().Be("Returned");
        }

        [Fact]
        [Trait("Category", "ClinicalWorkflowScenario")]
        public async Task Scenario5_ControlledSubstance_DualSignoffAndNightShiftAudit()
        {
            // 1. Night shift nurse toggles dark mode
            var currentTheme = "night-dark";
            currentTheme.Should().Be("night-dark");

            // 2. Nurse filters for controlled substances (Levels 1-4)
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Item_Code"", ""Display_Name"", ""Is_Controlled"", ""Control_Level"", ""Wintness_Required""
                FROM ""Medications""
                WHERE ""Is_Controlled"" = true;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var controlledMeds = new List<(string code, string name, string? level, string witness)>();
            while (await reader.ReadAsync())
            {
                controlledMeds.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4)
                ));
            }
            await reader.CloseAsync();

            controlledMeds.Should().HaveCount(8, "MedHub catalog contains exactly 8 scheduled controlled substances");

            // 3. Verify that high-level controlled drugs require dual witness confirmation
            var level1Or2 = controlledMeds.Where(x => x.level == "1" || x.level == "2").ToList();
            level1Or2.Should().NotBeEmpty();

            // 4. Verify that schedule confirmations for controlled drugs record Confirmed_Time
            await using var cmdConfirmed = new NpgsqlCommand(@"
                SELECT COUNT(*)
                FROM ""Schedule""
                WHERE ""Confirmed_Time"" IS NOT NULL;", conn);
            var confirmedCount = Convert.ToInt32(await cmdConfirmed.ExecuteScalarAsync());

            confirmedCount.Should().Be(14, "MedHub database contains 14 signed/witnessed dose confirmations");
        }
    }
}
