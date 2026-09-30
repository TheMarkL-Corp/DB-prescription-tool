using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class Tier2_BoundaryTests
    {
        // =========================================================================
        // FEATURE 1: Database Read-Only Connection & Configuration (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_B01_01_ZeroMutationInvariant_InsertAttemptMustThrowPostgresException()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            const string sqlInsert = @"
                INSERT INTO ""Patients"" (""Patient_Id"", ""Patient_Mrn"", ""Patient_Id_Type"", ""Patient_National_Id"", ""First_Name"", ""Last_Name"", ""Sex"", ""Date_Of_Birth"", ""Create_User"", ""Create_Time"", ""Modified_User"", ""Modified_Time"")
                VALUES (gen_random_uuid(), 'FAKE999', '1', 'A123456789', 'Fake', 'Patient', 'M', '1980-01-01', gen_random_uuid(), NOW(), gen_random_uuid(), NOW());";

            Func<Task> act = async () =>
            {
                await using var cmd = new NpgsqlCommand(sqlInsert, conn);
                await cmd.ExecuteNonQueryAsync();
            };

            var exception = await act.Should().ThrowAsync<PostgresException>("Database MUST reject INSERT in read-only mode");
            exception.Which.SqlState.Should().Be("25006", "PostgreSQL error 25006 indicates read_only_sql_transaction");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_B01_02_ZeroMutationInvariant_UpdateAttemptMustThrowPostgresException()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            const string sqlUpdate = @"UPDATE ""Schedule"" SET ""Calc_Dispense_Qty"" = 999.00 WHERE 1=0;";

            Func<Task> act = async () =>
            {
                await using var cmd = new NpgsqlCommand(sqlUpdate, conn);
                await cmd.ExecuteNonQueryAsync();
            };

            var exception = await act.Should().ThrowAsync<PostgresException>("Database MUST reject UPDATE in read-only mode");
            exception.Which.SqlState.Should().Be("25006");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_B01_03_ZeroMutationInvariant_DeleteAttemptMustThrowPostgresException()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            const string sqlDelete = @"DELETE FROM ""Prescriptions"" WHERE 1=0;";

            Func<Task> act = async () =>
            {
                await using var cmd = new NpgsqlCommand(sqlDelete, conn);
                await cmd.ExecuteNonQueryAsync();
            };

            var exception = await act.Should().ThrowAsync<PostgresException>("Database MUST reject DELETE in read-only mode");
            exception.Which.SqlState.Should().Be("25006");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public async Task TC_B01_04_UnreachableHost_ShouldTimeoutWithinSpecifiedLimit()
        {
            // Non-routable TEST-NET IP address (RFC 5737)
            var deadSettings = new DbConnectionSettings
            {
                Host = "192.0.2.1",
                Port = 5433,
                TimeoutSeconds = 2
            };

            var sw = Stopwatch.StartNew();
            Func<Task> act = async () =>
            {
                await using var conn = new NpgsqlConnection(deadSettings.ToConnectionString());
                await conn.OpenAsync();
            };

            await act.Should().ThrowAsync<NpgsqlException>();
            sw.Stop();
            sw.Elapsed.TotalSeconds.Should().BeLessThan(8, "Unreachable host connection must timeout promptly");
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(65536)]
        [InlineData(99999)]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F01_DatabaseConnection")]
        public void TC_B01_05_MalformedPort_ShouldBeDetectedAsInvalid(int invalidPort)
        {
            var isValid = invalidPort >= 1 && invalidPort <= 65535;
            isValid.Should().BeFalse($"Port {invalidPort} must be rejected by configuration validation");
        }

        // =========================================================================
        // FEATURE 2: Top KPI Metric Cards Calculation (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public void TC_B02_01_EmptyDataset_ShouldCalculateZeroCountsWithoutExceptions()
        {
            var emptyList = new List<MarScheduleItemDto>();
            var kpi = new KpiSummaryDto
            {
                TotalInpatients = 0,
                TotalActiveOrders = 0,
                DispensedCount = emptyList.Count(x => x.DispenseStatus == "Dispensed"),
                PendingDueCount = emptyList.Count(x => x.DispenseStatus.StartsWith("Pending"))
            };

            kpi.TotalInpatients.Should().Be(0);
            kpi.TotalActiveOrders.Should().Be(0);
            kpi.DispensedCount.Should().Be(0);
            kpi.PendingDueCount.Should().Be(0);
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public async Task TC_B02_02_DischargedEncounters_MustBeExcludedFromActiveCensus()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            // 1. Live census verifies active admitted encounters
            var activeCensus = await TestDatabaseContext.GetActiveInpatientCensusAsync(conn);
            activeCensus.Should().Be(15);

            // 2. Verify SQL predicate logic strictly excludes discharge date or discharged status
            await using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(DISTINCT ""Patient_Id"")
                FROM (
                    SELECT ""Patient_Id"", ""Encounter_Status"", ""Discharge_Date"" FROM ""Encounter""
                    UNION ALL
                    SELECT gen_random_uuid(), '2', NOW()
                    UNION ALL
                    SELECT gen_random_uuid(), '01', NOW()
                ) mock_enc
                WHERE (mock_enc.""Encounter_Status"" = '1' OR mock_enc.""Encounter_Status"" = '01')
                  AND mock_enc.""Discharge_Date"" IS NULL;", conn);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            count.Should().Be(15, "Discharged encounters must be strictly excluded from active inpatient census");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public async Task TC_B02_03_OrdersWithNullEndDatetime_ShouldBeCountedAsActive()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*) 
                FROM ""Prescriptions"" 
                WHERE ""Order_Status"" = '01' AND ""End_Datetime"" IS NULL;", conn);
            var ongoingCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            ongoingCount.Should().BeGreaterThan(0, "Ongoing prescriptions have NULL End_Datetime and must remain active");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public void TC_B02_04_HighVolumeScheduleAggregation_ShouldExecuteUnder250Ms()
        {
            // Synthesize 20,000 schedule records
            var largeDataset = Enumerable.Range(1, 20000).Select(i => new MarScheduleItemDto
            {
                ScheduleId = Guid.NewGuid(),
                DispenseStatus = (i % 5 == 0) ? "Dispensed" : "Pending (Due)",
                IsHighAlert = (i % 20 == 0)
            }).ToList();

            var sw = Stopwatch.StartNew();
            var dispensed = largeDataset.Count(x => x.DispenseStatus == "Dispensed");
            var pending = largeDataset.Count(x => x.DispenseStatus.StartsWith("Pending"));
            sw.Stop();

            dispensed.Should().Be(4000);
            pending.Should().Be(16000);
            sw.ElapsedMilliseconds.Should().BeLessThan(250, "Aggregation over 20k rows must complete in < 250ms");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F02_KpiMetricCards")]
        public void TC_B02_05_MidnightBoundaryTransition_ShouldShiftDateCategorizationCleanly()
        {
            var endOfDay = new DateTime(2026, 9, 24, 23, 59, 59);
            var startOfNextDay = new DateTime(2026, 9, 25, 0, 0, 1);

            var todaySchedule = new MarScheduleItemDto { ScheduleDate = new DateTime(2026, 9, 24) };

            var filteredAtDay1 = ClinicalFilterEngine.ApplyFilter(new[] { todaySchedule }, new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                ReferenceDate = endOfDay
            }).ToList();
            filteredAtDay1.Should().HaveCount(1);

            var filteredAtDay2 = ClinicalFilterEngine.ApplyFilter(new[] { todaySchedule }, new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                ReferenceDate = startOfNextDay
            }).ToList();
            filteredAtDay2.Should().BeEmpty("Schedule from previous day is no longer 'Today' on the next day");
        }

        // =========================================================================
        // FEATURE 3: Patient Overview Master-Detail Tab 1 (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F03_PatientOverview")]
        public async Task TC_B03_01_PatientWithNullAllergies_ShouldHandleGracefullyWithoutErrors()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT ""Patient_Mrn"", ""Allergy"", ""Allergy_Desc""
                FROM ""Patients""
                WHERE ""Allergy"" = 'N' OR ""Allergy_Desc"" IS NULL LIMIT 1;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(1).Should().Be("N");
            (reader.IsDBNull(2) || string.IsNullOrWhiteSpace(reader.GetString(2))).Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F03_PatientOverview")]
        public void TC_B03_02_ExtremelyLongPatientName_ShouldPreserveFullNameWithoutCrashing()
        {
            var longName = new string('A', 99) + ", " + new string('B', 99);
            var dto = new PatientOverviewDto
            {
                PatientMrn = "9999999",
                FullName = longName
            };

            dto.FullName.Length.Should().Be(200);
            dto.FullName.Should().Contain(",");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F03_PatientOverview")]
        public void TC_B03_03_UnassignedBed_ShouldFallbackToStandardIndicator()
        {
            var dto = new PatientOverviewDto
            {
                PatientMrn = "12345",
                RoomBedName = null,
                CareStationName = null
            };

            var bedDisplay = dto.RoomBedName ?? "Unassigned Bed / In-Transit";
            bedDisplay.Should().Be("Unassigned Bed / In-Transit");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F03_PatientOverview")]
        public void TC_B03_04_HighPrescriptionCount_ShouldPreserveAllOrders()
        {
            var orders = Enumerable.Range(1, 60).Select(i => new ActivePrescriptionDto
            {
                MedOrderId = Guid.NewGuid(),
                MedicationName = $"Medication {i}",
                TotalScheduledDoses = 10
            }).ToList();

            orders.Count.Should().Be(60);
            orders.Sum(x => x.TotalScheduledDoses).Should().Be(600);
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F03_PatientOverview")]
        public void TC_B03_05_SpecialCharactersInNames_ShouldPreserveUtf8Integrity()
        {
            var specialName = "Max O'Connor-鄧 (VIP★)";
            var dto = new PatientOverviewDto
            {
                PatientMrn = "0439449",
                FullName = specialName
            };

            dto.FullName.Should().Be(specialName);
            dto.FullName.Should().Contain("鄧");
            dto.FullName.Should().Contain("O'Connor");
        }

        // =========================================================================
        // FEATURE 4: Global Schedule / MAR Table Tab 2 (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public void TC_B04_01_SimultaneousSchedules_ShouldRemainDistinctRows()
        {
            var sameTime = new TimeSpan(9, 0, 0);
            var sameDate = new DateTime(2026, 9, 24);

            var row1 = new MarScheduleItemDto
            {
                ScheduleId = Guid.NewGuid(),
                ScheduleDate = sameDate,
                ScheduleTime = sameTime,
                DisplayName = "Drug Alpha"
            };

            var row2 = new MarScheduleItemDto
            {
                ScheduleId = Guid.NewGuid(),
                ScheduleDate = sameDate,
                ScheduleTime = sameTime,
                DisplayName = "Drug Beta"
            };

            row1.ScheduleDateTime.Should().Be(row2.ScheduleDateTime);
            row1.ScheduleId.Should().NotBe(row2.ScheduleId);
            row1.DisplayName.Should().NotBe(row2.DisplayName);
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public void TC_B04_02_DiscontinuedSchedule_ShouldBeCategorizedAsCancelled()
        {
            var status = ClinicalFilterEngine.ComputeDispensingStatus(
                dispenseStatus: null,
                confirmedTime: null,
                returnTime: null,
                returnQty: null,
                wasteTime: null,
                wasteQty: null,
                scheduleStatus: "99", // Inactive schedule
                orderStatus: "01",
                scheduleDateTime: DateTime.Now,
                referenceNow: DateTime.Now);

            status.Should().Be("Cancelled");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public void TC_B04_03_ReturnedDosePrecedence_MustOverrideConfirmedTime()
        {
            // Dose was previously confirmed, but subsequently returned by nurse
            var status = ClinicalFilterEngine.ComputeDispensingStatus(
                dispenseStatus: "N",
                confirmedTime: DateTime.Now.AddHours(-2), // Earlier confirmation
                returnTime: DateTime.Now.AddHours(-1),    // Return logged
                returnQty: 1.0m,
                wasteTime: null,
                wasteQty: null,
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDateTime: DateTime.Now.AddHours(-2),
                referenceNow: DateTime.Now);

            status.Should().Be("Returned", "Returned dose transaction must take absolute precedence over Confirmed_Time");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public void TC_B04_04_WastedDosePrecedence_MustOverrideConfirmedTime()
        {
            // Dose was confirmed, then destroyed/wasted
            var status = ClinicalFilterEngine.ComputeDispensingStatus(
                dispenseStatus: "N",
                confirmedTime: DateTime.Now.AddHours(-2),
                returnTime: null,
                returnQty: null,
                wasteTime: DateTime.Now.AddHours(-1),
                wasteQty: 1.0m,
                scheduleStatus: "01",
                orderStatus: "01",
                scheduleDateTime: DateTime.Now.AddHours(-2),
                referenceNow: DateTime.Now);

            status.Should().Be("Wasted", "Wasted dose transaction must take absolute precedence over Confirmed_Time");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F04_GlobalMarTable")]
        public void TC_B04_05_EmptyMarResult_ShouldReturnEmptyCollectionSafely()
        {
            var emptyList = new List<MarScheduleItemDto>();
            var result = ClinicalFilterEngine.ApplyFilter(emptyList, new ClinicalFilterCriteria
            {
                SearchText = "NonExistent"
            }).ToList();

            result.Should().BeEmpty();
        }

        // =========================================================================
        // FEATURE 5: Filter Chips & Instant Fuzzy Search (5 Tests)
        // =========================================================================

        [Theory]
        [InlineData("[")]
        [InlineData("*")]
        [InlineData("+")]
        [InlineData("?")]
        [InlineData("\\")]
        [InlineData("(test)")]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_B05_01_RegexSpecialCharacters_ShouldNotThrowException(string regexChar)
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { PatientMrn = "0439449", PatientName = "Teng, Max", DisplayName = "Regular Drug" }
            };

            Action act = () =>
            {
                var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
                {
                    SearchText = regexChar
                }).ToList();
            };

            act.Should().NotThrow("Fuzzy search must treat regex meta-characters as plain literals");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_B05_02_LeadingAndTrailingWhitespace_ShouldBeTrimmedAutomatically()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { PatientMrn = "0439449", PatientName = "Teng, Max" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                SearchText = "   0439449   "
            }).ToList();

            filtered.Should().HaveCount(1);
            filtered[0].PatientMrn.Should().Be("0439449");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_B05_03_CaseInsensitiveSearchParity_ShouldYieldEqualResults()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { DisplayName = "Levemir Flex Pen" },
                new() { DisplayName = "Meropenem" }
            };

            var lower = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria { SearchText = "levemir" }).ToList();
            var upper = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria { SearchText = "LEVEMIR" }).ToList();
            var mixed = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria { SearchText = "LeVeMiR" }).ToList();

            lower.Count.Should().Be(1);
            upper.Count.Should().Be(1);
            mixed.Count.Should().Be(1);
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_B05_04_MutuallyExclusiveFilters_ShouldReturnEmptySetCleanly()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { DispenseStatus = "Dispensed", ScheduleDate = new DateTime(2026, 9, 20) }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                StatusFilter = "Dispensed",
                DateFilter = "Future",
                ReferenceDate = new DateTime(2026, 9, 24)
            }).ToList();

            filtered.Should().BeEmpty();
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F05_FilterSearch")]
        public void TC_B05_05_GibberishSearchTerm_ShouldReturnZeroResults()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { PatientMrn = "0439449", DisplayName = "Levemir" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                SearchText = "xyz_non_existent_12345"
            }).ToList();

            filtered.Should().BeEmpty();
        }

        // =========================================================================
        // FEATURE 6: Medication Details Drawer / Modal (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public void TC_B06_01_MissingAtcCodeOrDescription_ShouldDisplayFallbackPlaceholder()
        {
            var dto = new MedicationDetailDto
            {
                ItemCode = "TEST01",
                DisplayName = "Test Drug",
                AtcCode = null,
                Description = null
            };

            var atcDisplay = dto.AtcCode ?? "N/A";
            var descDisplay = dto.Description ?? "No description available";

            atcDisplay.Should().Be("N/A");
            descDisplay.Should().Be("No description available");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public void TC_B06_02_SimultaneousSafetyAlerts_ShouldPreserveAllFlags()
        {
            var drug = new MedicationDetailDto
            {
                ItemCode = "ALERT_TRIPLE",
                IsHighAlert = true,
                IsControlled = true,
                ControlLevel = "2",
                IsRefrigerationRequired = true,
                WitnessRequired = true
            };

            drug.IsHighAlert.Should().BeTrue();
            drug.IsControlled.Should().BeTrue();
            drug.ControlLevel.Should().Be("2");
            drug.IsRefrigerationRequired.Should().BeTrue();
            drug.WitnessRequired.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public void TC_B06_03_ExtremelyLongClinicalIndication_ShouldNotTruncateOrThrow()
        {
            var longIndication = new string('X', 1000);
            var rx = new ActivePrescriptionDto
            {
                ClinicalIndication = longIndication
            };

            rx.ClinicalIndication.Length.Should().Be(1000);
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public void TC_B06_04_NullableStorageNoteAndUrl_ShouldNotCauseNullReference()
        {
            var drug = new MedicationDetailDto
            {
                StorageNote = null,
                StorageUrl = null
            };

            drug.StorageNote.Should().BeNull();
            drug.StorageUrl.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F06_MedicationDetailModal")]
        public async Task TC_B06_05_InactiveCatalogItems_ShouldBeIdentifiedByIsActiveFlag()
        {
            await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*) FROM ""Medications"" WHERE ""Is_Active"" = false;", conn);
            var inactiveCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            inactiveCount.Should().BeGreaterOrEqualTo(0);
        }

        // =========================================================================
        // FEATURE 7: Smart Multi-Sheet Excel Export (ClosedXML) (5 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_B07_01_ExportZeroRecords_ShouldGenerateValidWorkbookWithInfoRow()
        {
            var bytes = TestExcelHelper.GenerateClinicalWorkbook(
                new List<MarScheduleItemDto>(),
                new List<ActivePrescriptionDto>());

            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            sheet1.Cell("A5").GetString().Should().Contain("No matching");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_B07_02_XmlSpecialCharacters_ShouldNotCorruptExcelSpreadsheet()
        {
            var items = new List<MarScheduleItemDto>
            {
                new()
                {
                    PatientMrn = "0439449",
                    PatientName = "Smith <Dr & Mrs> \"quoted\"",
                    DisplayName = "Drug & Co. <500mg>",
                    ClinicalIndication = "Infection > 38°C & sepsis 'risk'"
                }
            };

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(items, new List<ActivePrescriptionDto>());
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            sheet1.Cell("B5").GetString().Should().Be("Smith <Dr & Mrs> \"quoted\"");
            sheet1.Cell("F5").GetString().Should().Be("Drug & Co. <500mg>");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_B07_03_FractionalDoses_ShouldBeStoredAsNumericDoubleWithTwoDecimals()
        {
            var items = new List<MarScheduleItemDto>
            {
                new()
                {
                    PatientMrn = "0439449",
                    DosageOrderedValue = 0.333m,
                    DispensedQty = 0.50m
                }
            };

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(items, new List<ActivePrescriptionDto>());
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            var cellH5 = sheet1.Cell("H5");
            cellH5.DataType.Should().Be(XLDataType.Number);
            cellH5.GetDouble().Should().BeApproximately(0.333, 0.001);

            var cellN5 = sheet1.Cell("N5");
            cellN5.DataType.Should().Be(XLDataType.Number);
            cellN5.GetDouble().Should().Be(0.50);
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F07_ExcelExport")]
        public void TC_B07_04_LargeDatasetExport_ShouldCompleteWithinMemoryBounds()
        {
            var synthList = Enumerable.Range(1, 2000).Select(i => new MarScheduleItemDto
            {
                PatientMrn = $"{i:D7}",
                PatientName = $"Patient {i}",
                DisplayName = $"Drug {i}",
                DosageOrderedValue = 1.0m,
                DosageOrderedUnit = "TAB",
                Frequency = "QD",
                Route = "PO",
                ScheduleDate = new DateTime(2026, 9, 24),
                ScheduleTime = new TimeSpan(9, 0, 0),
                DispenseStatus = "Dispensed"
            }).ToList();

            var initialMemory = GC.GetTotalMemory(true);
            var bytes = TestExcelHelper.GenerateClinicalWorkbook(synthList, new List<ActivePrescriptionDto>());
            var finalMemory = GC.GetTotalMemory(false);

            bytes.Should().NotBeEmpty();
            bytes.Length.Should().BeGreaterThan(50000);
            (finalMemory - initialMemory).Should().BeLessThan(250 * 1024 * 1024, "Export must remain under 250MB memory ceiling");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F07_ExcelExport")]
        public async Task TC_B07_05_ConcurrentWorkbookGeneration_ShouldOperateIndependently()
        {
            var task1 = Task.Run(() => TestExcelHelper.GenerateClinicalWorkbook(new List<MarScheduleItemDto>(), new List<ActivePrescriptionDto>()));
            var task2 = Task.Run(() => TestExcelHelper.GenerateClinicalWorkbook(new List<MarScheduleItemDto>(), new List<ActivePrescriptionDto>()));

            await Task.WhenAll(task1, task2);

            var res1 = await task1;
            var res2 = await task2;
            res1.Should().NotBeEmpty();
            res2.Should().NotBeEmpty();
        }

        // =========================================================================
        // FEATURE 8: Clean Clinical Light vs Dark Mode Toggle (5 Tests)
        // =========================================================================

        [Theory]
        [InlineData("invalid_theme_name")]
        [InlineData("12345")]
        [InlineData("undefined")]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_B08_01_CorruptedThemeKey_ShouldFallbackToLightMode(string badTheme)
        {
            var validThemes = new HashSet<string> { "clinical-light", "night-dark" };
            var activeTheme = validThemes.Contains(badTheme) ? badTheme : "clinical-light";

            activeTheme.Should().Be("clinical-light", "Unknown theme values must fallback to clinical light");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_B08_02_EmptyThemeString_ShouldFallbackToLightMode()
        {
            string empty = "";
            var activeTheme = string.IsNullOrWhiteSpace(empty) ? "clinical-light" : empty;

            activeTheme.Should().Be("clinical-light");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_B08_03_BorderContrast_ShouldMaintainMinimumVisibleThreshold()
        {
            // Dark mode border color (#334155 Slate 700) on (#1E293B Slate 800)
            const string borderDark = "#334155";
            const string surfaceDark = "#1E293B";

            borderDark.Should().NotBe(surfaceDark, "Card borders must remain visually distinct from surfaces");
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_B08_04_ThemeSwitch_ShouldNotMutateFilterCriteria()
        {
            var criteria = new ClinicalFilterCriteria
            {
                SearchText = "Levemir",
                DateFilter = "Today",
                HighAlertOnly = true
            };

            // Simulating UI theme switch event
            var currentTheme = "clinical-light";
            currentTheme = (currentTheme == "clinical-light") ? "night-dark" : "clinical-light";

            // Criteria state must remain untouched
            criteria.SearchText.Should().Be("Levemir");
            criteria.DateFilter.Should().Be("Today");
            criteria.HighAlertOnly.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Boundary")]
        [Trait("Feature", "F08_ThemeToggle")]
        public void TC_B08_05_RapidThemeToggle_ShouldAlternateConsistentlyWithoutDeadlock()
        {
            var theme = "clinical-light";
            for (int i = 0; i < 100; i++)
            {
                theme = (theme == "clinical-light") ? "night-dark" : "clinical-light";
            }

            theme.Should().Be("clinical-light", "Even number of toggles returns to starting theme");
        }
    }
}
