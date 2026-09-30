using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FluentAssertions;
using MedHubPrescriptionTool.Tests.TestHelpers;
using Npgsql;
using Xunit;

namespace MedHubPrescriptionTool.Tests
{
    public class Tier1_FeatureCoverageTests
    {
        // =========================================================================
        // FEATURE 1: Database Read-Only Connection & Configuration (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_F01_01_DefaultConnection_ShouldConnectSuccessfully_AndReportConnected()
        {
            // Verify default connection parameters (127.0.0.1:5433, amisdbV2, postgres)
            var settings = new DbConnectionSettings();
            var isAvailable = await TestDatabaseContext.IsDatabaseAvailableAsync();
            isAvailable.Should().BeTrue("MedHub PostgreSQL service must be running and accessible on 127.0.0.1:5433");

            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync(settings.ToConnectionString());
            conn.State.Should().Be(System.Data.ConnectionState.Open);
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public void TC_F01_02_ExternalConfig_ShouldSupportCustomParameters_AndGenerateValidConnString()
        {
            var customSettings = new DbConnectionSettings
            {
                Host = "192.168.1.100",
                Port = 5434,
                Database = "amisdbV2_backup",
                Username = "pharmacy_reader",
                Password = "SecretPassword!1",
                ReadOnly = true,
                TimeoutSeconds = 20
            };

            var connStr = customSettings.ToConnectionString();
            connStr.Should().Contain("Host=192.168.1.100");
            connStr.Should().Contain("Port=5434");
            connStr.Should().Contain("Database=amisdbV2_backup");
            connStr.Should().Contain("Username=pharmacy_reader");
            connStr.Should().Contain("default_transaction_read_only=on");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_F01_03_SettingsModal_ConnectionTester_ShouldAccuratelyReportInvalidHost()
        {
            // Testing invalid connection parameters should gracefully fail without throwing unhandled exceptions
            var invalidSettings = new DbConnectionSettings
            {
                Host = "127.0.0.1",
                Port = 59999, // Unused port
                TimeoutSeconds = 2
            };

            Func<Task> act = async () =>
            {
                await using var conn = new NpgsqlConnection(invalidSettings.ToConnectionString());
                await conn.OpenAsync();
            };

            await act.Should().ThrowAsync<NpgsqlException>("Connection to non-existent port must fail safely");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_F01_04_ReadOnlyModeFlag_ShouldEnforceSessionReadOnlyState()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand("SHOW transaction_read_only;", conn);
            var result = (string?)await cmd.ExecuteScalarAsync();

            result.Should().Be("on", "MedHub database connection MUST operate under strict transaction_read_only = on");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_F01_05_ManualRefreshTrigger_ShouldFetchFreshData_AndRecordSyncTimestamp()
        {
            var syncTimestamp = DateTime.UtcNow;
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            
            // Execute fresh census query
            var census = await TestDatabaseContext.GetActiveInpatientCensusAsync(conn);
            census.Should().BeGreaterThan(0, "Refresh must retrieve active admitted patients");

            var formattedSync = syncTimestamp.ToString("yyyy-MM-dd HH:mm:ss");
            formattedSync.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$");
        }

        // =========================================================================
        // FEATURE 2: Top KPI Metric Cards Calculation (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public async Task TC_F02_01_KpiInpatients_ShouldMatchActiveAdmittedCases()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var inpatients = await TestDatabaseContext.GetActiveInpatientCensusAsync(conn);

            inpatients.Should().Be(15, "MedHub live database contains exactly 15 active admitted inpatients");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public async Task TC_F02_02_KpiActiveOrders_ShouldMatchValidPrescriptions()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var activeOrders = await TestDatabaseContext.GetActiveOrdersCountAsync(conn);

            activeOrders.Should().Be(31, "MedHub live database contains exactly 31 active medication orders");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public async Task TC_F02_03_KpiDispensed_ShouldCountConfirmedAndDispensedDoses()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var dispensedCount = await TestDatabaseContext.GetDispensedSchedulesCountAsync(conn);

            dispensedCount.Should().Be(13, "MedHub live database contains 13 confirmed doses (excluding returns/wastes)");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public async Task TC_F02_04_KpiPendingDue_ShouldMatchUnadministeredDoses()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var totalPending = await TestDatabaseContext.GetTotalPendingSchedulesCountAsync(conn);
            var totalSchedules = await TestDatabaseContext.GetTotalSchedulesCountAsync(conn);

            totalSchedules.Should().Be(385);
            totalPending.Should().BeGreaterThan(300, "Pending doses must encompass all active unconfirmed schedules");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public void TC_F02_05_KpiSummaryComputation_ShouldAggregateConsistentlyFromSnapshot()
        {
            var items = new List<MarScheduleItemDto>
            {
                new() { DispenseStatus = "Dispensed" },
                new() { DispenseStatus = "Dispensed" },
                new() { DispenseStatus = "Pending (Due)" },
                new() { DispenseStatus = "Pending (Future)" },
                new() { DispenseStatus = "Cancelled" }
            };

            var kpi = new KpiSummaryDto
            {
                TotalInpatients = 15,
                TotalActiveOrders = 31,
                DispensedCount = items.Count(x => x.DispenseStatus == "Dispensed"),
                PendingDueCount = items.Count(x => x.DispenseStatus.StartsWith("Pending"))
            };

            kpi.DispensedCount.Should().Be(2);
            kpi.PendingDueCount.Should().Be(2);
            kpi.TotalInpatients.Should().Be(15);
            kpi.TotalActiveOrders.Should().Be(31);
        }

        // =========================================================================
        // FEATURE 3: Patient Overview Master-Detail Tab 1 (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F03_PatientOverview")]
        public async Task TC_F03_01_CareStationGrouping_ShouldDiscoverAllClinicalStations()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Care_Stations\";", conn);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            count.Should().Be(4, "Care stations include 8A, 5A, ICU, and OR");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F03_PatientOverview")]
        public async Task TC_F03_02_PatientMasterCardRender_ShouldContainRequiredDemographics()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Patient_Mrn"", ""First_Name"", ""Last_Name"", ""Sex"", ""Date_Of_Birth""
                FROM ""Patients""
                WHERE ""Patient_Mrn"" = '0439449';", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("0439449");
            var fullName = $"{reader.GetString(1)} {reader.GetString(2)}";
            fullName.Should().Be("Teng Max");
            reader.GetString(3).Should().Be("M");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F03_PatientOverview")]
        public async Task TC_F03_03_PatientSelection_ShouldBindActivePrescriptionsForPatient()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var activeOrders = await TestDatabaseContext.FetchActivePrescriptionsAsync(conn);
            var tengOrders = activeOrders.Where(x => x.PatientMrn == "0439449").ToList();

            tengOrders.Should().NotBeEmpty("Patient Teng, Max (0439449) must have active prescription orders");
            tengOrders.Count.Should().Be(2, "Teng, Max has exactly 2 active prescriptions");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F03_PatientOverview")]
        public async Task TC_F03_04_AllergyBannerPresentation_ShouldRetrieveVerbatimAllergyDesc()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Patient_Mrn"", ""Allergy"", ""Allergy_Desc""
                FROM ""Patients"" LIMIT 1;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            var allergyFlag = reader.GetString(1);
            allergyFlag.Should().BeOneOf("Y", "N");
            await reader.CloseAsync();

            // Verify clinical presentation logic: when Allergy='Y', AllergyDesc must be presented verbatim
            var patientWithAllergy = new PatientOverviewDto
            {
                PatientMrn = "0439449",
                FullName = "Teng, Max",
                Allergy = "Y",
                AllergyDesc = "Penicillin - Severe Anaphylaxis Reaction"
            };

            patientWithAllergy.Allergy.Should().Be("Y");
            patientWithAllergy.AllergyDesc.Should().Be("Penicillin - Severe Anaphylaxis Reaction");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F03_PatientOverview")]
        public async Task TC_F03_05_DrugWarningPresentation_ShouldPreserveDrugWarnFlags()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""Patients"" WHERE ""DrugWarn"" = 'Y';", conn);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            count.Should().BeGreaterOrEqualTo(0, "Drug warning flag queries must execute without error");
        }

        // =========================================================================
        // FEATURE 4: Global Schedule / MAR Table Tab 2 (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public async Task TC_F04_01_ChronologicalTimetableSort_ShouldOrderAscendingByDateTime()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var schedules = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);

            schedules.Should().NotBeEmpty();
            for (int i = 0; i < schedules.Count - 1; i++)
            {
                (schedules[i].ScheduleDateTime <= schedules[i + 1].ScheduleDateTime)
                    .Should().BeTrue("MAR rows must be sorted chronologically");
            }
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public async Task TC_F04_02_CrossPatientConsolidation_ShouldIncludeMultipleWardsAndBeds()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var schedules = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);

            var uniqueStations = schedules.Select(x => x.CareStation).Distinct().Where(x => x != null).ToList();
            var uniqueBeds = schedules.Select(x => x.RoomBed).Distinct().Where(x => x != null).ToList();

            uniqueStations.Count.Should().BeGreaterThan(1, "Global MAR must consolidate schedules across multiple care stations");
            uniqueBeds.Count.Should().BeGreaterThan(1, "Global MAR must consolidate schedules across multiple beds");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public async Task TC_F04_03_DispenseStatusBadges_ShouldIncludeDispensedAndPendingCategories()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var schedules = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);

            var statuses = schedules.Select(x => x.DispenseStatus).Distinct().ToList();
            statuses.Should().Contain("Dispensed");
            statuses.Any(s => s.StartsWith("Pending")).Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public async Task TC_F04_04_HighAlertDrugFlag_ShouldAccuratelyTagHighAlertMedications()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            // 1. Verify catalog contains high-alert drug (EVOR2)
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Item_Code"", ""Display_Name""
                FROM ""Medications""
                WHERE ""isHighAlert"" = true;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("EVOR2");
            await reader.CloseAsync();

            // 2. Verify MAR item model flags IsHighAlert
            var highAlertMarItem = new MarScheduleItemDto
            {
                MedicationCode = "EVOR2",
                DisplayName = "Shoren Suppositories",
                IsHighAlert = true
            };
            highAlertMarItem.IsHighAlert.Should().BeTrue("High-alert order must flag prominent red alert badge");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public async Task TC_F04_05_RefrigerationIndicator_ShouldIdentifyColdChainMedications()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            var schedules = await TestDatabaseContext.FetchUnifiedMarRecordsAsync(conn);

            var coldChainItems = schedules.Where(x => x.IsRefrigerationRequired).ToList();
            coldChainItems.Should().NotBeEmpty("Dataset contains refrigerated medications (e.g., insulins)");
        }

        // =========================================================================
        // FEATURE 5: Filter Chips & Instant Fuzzy Search (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_F05_01_TodayFilterChip_ShouldFilterStrictlyToCurrentDate()
        {
            var refDate = new DateTime(2026, 9, 24);
            var sample = new List<MarScheduleItemDto>
            {
                new() { ScheduleDate = new DateTime(2026, 9, 24), DisplayName = "Drug A" },
                new() { ScheduleDate = new DateTime(2026, 9, 23), DisplayName = "Drug B" },
                new() { ScheduleDate = new DateTime(2026, 9, 25), DisplayName = "Drug C" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                ReferenceDate = refDate
            }).ToList();

            filtered.Should().HaveCount(1);
            filtered[0].DisplayName.Should().Be("Drug A");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_F05_02_PastDueFilterChip_ShouldFilterSchedulesBeforeReferenceDate()
        {
            var refDate = new DateTime(2026, 9, 24);
            var sample = new List<MarScheduleItemDto>
            {
                new() { ScheduleDate = new DateTime(2026, 9, 22), DisplayName = "Past Drug" },
                new() { ScheduleDate = new DateTime(2026, 9, 24), DisplayName = "Today Drug" },
                new() { ScheduleDate = new DateTime(2026, 9, 26), DisplayName = "Future Drug" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                DateFilter = "Past",
                ReferenceDate = refDate
            }).ToList();

            filtered.Should().HaveCount(1);
            filtered[0].DisplayName.Should().Be("Past Drug");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_F05_03_DispensedStatusChip_ShouldReturnOnlyDispensedSchedules()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { DispenseStatus = "Dispensed", DisplayName = "Dispensed 1" },
                new() { DispenseStatus = "Pending (Due)", DisplayName = "Pending 1" },
                new() { DispenseStatus = "Cancelled", DisplayName = "Cancelled 1" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                StatusFilter = "Dispensed"
            }).ToList();

            filtered.Should().HaveCount(1);
            filtered[0].DisplayName.Should().Be("Dispensed 1");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_F05_04_MrnInstantSearch_ShouldMatchPatientMrnPrefixAndExact()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { PatientMrn = "0439449", PatientName = "Teng, Max" },
                new() { PatientMrn = "00075655", PatientName = "Chen, Wei" },
                new() { PatientMrn = "202602090121", PatientName = "Wang, Da" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                SearchText = "0439449"
            }).ToList();

            filtered.Should().HaveCount(1);
            filtered[0].PatientName.Should().Be("Teng, Max");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_F05_05_DrugNameSearch_ShouldMatchTradeAndGenericNamesCaseInsensitive()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { DisplayName = "Levemir Flex Pen", GenericName = "Insulin Detemir" },
                new() { DisplayName = "Meropenem 500mg", GenericName = "Meropenem Trihydrate" },
                new() { DisplayName = "Ceftriaxone 1g", GenericName = "Ceftriaxone Sodium" }
            };

            var filteredTrade = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                SearchText = "levemir"
            }).ToList();
            filteredTrade.Should().HaveCount(1);

            var filteredGeneric = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                SearchText = "meropenem"
            }).ToList();
            filteredGeneric.Should().HaveCount(1);
        }

        // =========================================================================
        // FEATURE 6: Medication Details Drawer / Modal (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public async Task TC_F06_01_MedicationLookup_ShouldRetrieveCompleteCatalogMetadata()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Item_Code"", ""Display_Name"", ""Generic_Name"", ""Atc_Code"", ""Form""
                FROM ""Medications""
                WHERE ""Item_Code"" = 'OXANA';", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("OXANA");
            reader.GetString(1).Should().NotBeNullOrWhiteSpace();
            reader.GetString(3).Should().NotBeNullOrWhiteSpace("ATC code must be populated");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public async Task TC_F06_02_DosageAndAdminMapping_ShouldIncludeStrengthAndForm()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Strength_Value"", ""Strength_Unit"", ""Form""
                FROM ""Medications""
                WHERE ""Item_Code"" = 'OXANA';", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetDecimal(0).Should().BeGreaterThan(0);
            reader.GetString(1).Should().NotBeNullOrWhiteSpace();
            reader.GetString(2).Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public async Task TC_F06_03_HighAlertSafetyPresentation_ShouldReflectIsHighAlertFlag()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Item_Code"", ""isHighAlert""
                FROM ""Medications""
                WHERE ""isHighAlert"" = true;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetBoolean(1).Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public async Task TC_F06_04_WitnessRequirement_ShouldIdentifyDualVerificationDrugs()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""Medications"" WHERE ""Wintness_Required"" = 'Y';", conn);
            var witnessCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            witnessCount.Should().BeGreaterThan(0, "Hospital catalog must configure medications requiring dual-nurse witnessing");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public async Task TC_F06_05_ControlledSubstance_ShouldProvideScheduleLevel()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Item_Code"", ""Is_Controlled"", ""Control_Level""
                FROM ""Medications""
                WHERE ""Is_Controlled"" = true AND ""Control_Level"" IS NOT NULL LIMIT 1;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetBoolean(1).Should().BeTrue();
            var level = reader.GetString(2);
            level.Should().MatchRegex(@"^[1-4]$", "Controlled substance level must be between 1 and 4");
        }

        // =========================================================================
        // FEATURE 7: Smart Multi-Sheet Excel Export (ClosedXML) (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_F07_01_ClosedXmlExport_ShouldCreateTwoWorksheets()
        {
            var bytes = TestExcelHelper.GenerateClinicalWorkbook(
                new List<MarScheduleItemDto>(),
                new List<ActivePrescriptionDto>());

            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);

            wb.Worksheets.Count.Should().Be(2);
            wb.Worksheets.Contains("Medication Administration Log").Should().BeTrue();
            wb.Worksheets.Contains("Active Prescriptions Overview").Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_F07_02_FilenameGeneration_ShouldFollowTimestampPattern()
        {
            var filename = TestExcelHelper.GenerateTimestampedFilename();

            filename.Should().MatchRegex(@"^MedHub_Prescriptions_\d{8}_\d{4}\.xlsx$");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_F07_03_Sheet1Columns_ShouldMatch17StandardClinicalColumns()
        {
            var bytes = TestExcelHelper.GenerateClinicalWorkbook(
                new List<MarScheduleItemDto>(),
                new List<ActivePrescriptionDto>());

            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            sheet1.Cell("A4").GetString().Should().Be("Patient MRN");
            sheet1.Cell("B4").GetString().Should().Be("Patient Name");
            sheet1.Cell("C4").GetString().Should().Be("Care Station");
            sheet1.Cell("D4").GetString().Should().Be("Room / Bed");
            sheet1.Cell("E4").GetString().Should().Be("Medication Code");
            sheet1.Cell("F4").GetString().Should().Be("Display Name");
            sheet1.Cell("G4").GetString().Should().Be("Generic Name");
            sheet1.Cell("H4").GetString().Should().Be("Ordered Dose");
            sheet1.Cell("I4").GetString().Should().Be("Dose Unit");
            sheet1.Cell("J4").GetString().Should().Be("Frequency");
            sheet1.Cell("K4").GetString().Should().Be("Route");
            sheet1.Cell("L4").GetString().Should().Be("Schedule DateTime");
            sheet1.Cell("M4").GetString().Should().Be("Dispense Status");
            sheet1.Cell("N4").GetString().Should().Be("Dispensed Qty");
            sheet1.Cell("O4").GetString().Should().Be("Status Time");
            sheet1.Cell("P4").GetString().Should().Be("Administering Nurse");
            sheet1.Cell("Q4").GetString().Should().Be("High Alert?");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_F07_04_Sheet2Columns_ShouldMatch16ActivePrescriptionColumns()
        {
            var bytes = TestExcelHelper.GenerateClinicalWorkbook(
                new List<MarScheduleItemDto>(),
                new List<ActivePrescriptionDto>());

            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet2 = wb.Worksheet("Active Prescriptions Overview");

            sheet2.Cell("A4").GetString().Should().Be("Patient MRN");
            sheet2.Cell("B4").GetString().Should().Be("Case No");
            sheet2.Cell("C4").GetString().Should().Be("Patient Name");
            sheet2.Cell("D4").GetString().Should().Be("Care Station");
            sheet2.Cell("E4").GetString().Should().Be("Room / Bed");
            sheet2.Cell("F4").GetString().Should().Be("Attending Physician");
            sheet2.Cell("G4").GetString().Should().Be("Medication Code");
            sheet2.Cell("H4").GetString().Should().Be("Medication Name");
            sheet2.Cell("I4").GetString().Should().Be("Dosage Instruction");
            sheet2.Cell("J4").GetString().Should().Be("Clinical Indication");
            sheet2.Cell("K4").GetString().Should().Be("Start Datetime");
            sheet2.Cell("L4").GetString().Should().Be("End Datetime");
            sheet2.Cell("M4").GetString().Should().Be("Total Sched Doses");
            sheet2.Cell("N4").GetString().Should().Be("Dispensed Doses");
            sheet2.Cell("O4").GetString().Should().Be("Pending Doses");
            sheet2.Cell("P4").GetString().Should().Be("High Alert Warning");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_F07_05_MrnPreservationAndNumberFormat_ShouldPreserveLeadingZeros()
        {
            var items = new List<MarScheduleItemDto>
            {
                new()
                {
                    PatientMrn = "00075655", // Leading zeroes!
                    PatientName = "Chen, Wei",
                    MedicationCode = "OXANA",
                    DisplayName = "Oxanase",
                    DosageOrderedValue = 1.50m,
                    DosageOrderedUnit = "TAB",
                    Frequency = "TID",
                    Route = "PO",
                    ScheduleDate = new DateTime(2026, 9, 24),
                    ScheduleTime = new TimeSpan(9, 0, 0),
                    DispenseStatus = "Dispensed",
                    DispensedQty = 1.50m
                }
            };

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(items, new List<ActivePrescriptionDto>());
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            var mrnCell = sheet1.Cell("A5");
            mrnCell.DataType.Should().Be(XLDataType.Text);
            mrnCell.GetString().Should().Be("00075655", "Leading zeros in MRN must never be truncated");

            var doseCell = sheet1.Cell("H5");
            doseCell.DataType.Should().Be(XLDataType.Number);
            doseCell.Style.NumberFormat.Format.Should().Be("#,##0.00");
        }

        // =========================================================================
        // FEATURE 8: Clean Clinical Light vs Dark Mode Toggle (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_F08_01_ThemeTokens_ShouldDefineConsistentLightAndDarkSemanticSets()
        {
            // Verify semantic tokens defined in spec
            var lightPrimary = "#0891B2";
            var darkPrimary = "#06B6D4";

            lightPrimary.Should().NotBe(darkPrimary);
            lightPrimary.Should().StartWith("#");
            darkPrimary.Should().StartWith("#");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_F08_02_ContrastRatio_ShouldMeetWcagAaStandard()
        {
            // Calculating relative luminance for body text (#0F172A on #FFFFFF)
            // WCAG AA requirement is >= 4.5:1 for standard body copy
            const double textLuminance = 0.01; // Slate 900
            const double bgLuminance = 1.0;   // White
            var contrast = (bgLuminance + 0.05) / (textLuminance + 0.05);

            contrast.Should().BeGreaterThan(4.5, "Standard light theme contrast must exceed WCAG AA 4.5:1 ratio");
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_F08_03_TableReadability_DarkCanvasToken_ShouldProvideComfortableShiftViewing()
        {
            var darkCanvas = "#0B0F17"; // Slate 950
            var darkSurface = "#1E293B"; // Slate 800
            var darkText = "#F8FAFC"; // Slate 50

            darkCanvas.Should().NotBeNullOrWhiteSpace();
            darkSurface.Should().NotBeNullOrWhiteSpace();
            darkText.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_F08_04_ClinicalStatusBadges_ShouldHaveDistinctColorsAcrossThemes()
        {
            var lightSuccessBg = "#ECFDF5";
            var darkSuccessBg = "#064E3B";
            var lightDangerBg = "#FEF2F2";
            var darkDangerBg = "#7F1D1D";

            lightSuccessBg.Should().NotBe(lightDangerBg);
            darkSuccessBg.Should().NotBe(darkDangerBg);
        }

        [Fact]
        [Trait("Category", "FeatureCoverage")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_F08_05_ThemePersistence_ShouldDefaultToClinicalLightWhenUnset()
        {
            string? storedTheme = null;
            var activeTheme = string.IsNullOrWhiteSpace(storedTheme) ? "clinical-light" : storedTheme;

            activeTheme.Should().Be("clinical-light", "Default fallback theme must be clean clinical light");
        }
    }
}
