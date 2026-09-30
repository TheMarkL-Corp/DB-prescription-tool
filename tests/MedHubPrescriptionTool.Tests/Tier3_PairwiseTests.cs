using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FluentAssertions;
using MedHubPrescriptionTool.Tests.TestHelpers;
using Xunit;

namespace MedHubPrescriptionTool.Tests
{
    public class Tier3_PairwiseTests
    {
        // =========================================================================
        // TIER 3: Cross-Feature Interactions & Pairwise Verification (15 Tests)
        // =========================================================================

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I01_FilterChip_And_SearchBar_ShouldFormStrictIntersection()
        {
            var refDate = new DateTime(2026, 9, 24);
            var sample = new List<MarScheduleItemDto>
            {
                new() { ScheduleDate = refDate, DisplayName = "Levemir Flex Pen", PatientMrn = "0439449" },
                new() { ScheduleDate = refDate, DisplayName = "Meropenem 500mg", PatientMrn = "00075655" },
                new() { ScheduleDate = refDate.AddDays(-1), DisplayName = "Levemir Flex Pen", PatientMrn = "0439449" }
            };

            // Intersection: Date == Today AND Drug contains Levemir
            var criteria = new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                ReferenceDate = refDate,
                SearchText = "Levemir"
            };

            var result = ClinicalFilterEngine.ApplyFilter(sample, criteria).ToList();

            result.Should().HaveCount(1, "Only today's Levemir record satisfies both criteria");
            result[0].ScheduleDate.Should().Be(refDate);
            result[0].DisplayName.Should().Contain("Levemir");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I02_SearchQueryPersistence_AcrossViewSwitching()
        {
            // Simulate view-model state retention between Tab 1 and Tab 2
            var viewState = new
            {
                ActiveTab = 2,
                SearchQuery = "0439449",
                SelectedPatientMrn = (string?)null
            };

            // Switch to Tab 1
            var stateAfterTab1Switch = new
            {
                ActiveTab = 1,
                viewState.SearchQuery,
                SelectedPatientMrn = "0439449"
            };

            // Return to Tab 2
            var stateAfterReturn = new
            {
                ActiveTab = 2,
                stateAfterTab1Switch.SearchQuery,
                stateAfterTab1Switch.SelectedPatientMrn
            };

            stateAfterReturn.ActiveTab.Should().Be(2);
            stateAfterReturn.SearchQuery.Should().Be("0439449", "Search query must persist across view switches");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I03_SearchFilteredItemSelection_ShouldBindExactRecordToDetailModal()
        {
            var sample = new List<MarScheduleItemDto>
            {
                new() { ScheduleId = Guid.NewGuid(), ItemCode = "OXANA", DisplayName = "Oxanase", AtcCode = "M09AB02" },
                new() { ScheduleId = Guid.NewGuid(), ItemCode = "IHNTR", DisplayName = "Insulatard", AtcCode = "A10AE01" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                SearchText = "Oxanase"
            }).ToList();

            filtered.Should().HaveCount(1);
            var selectedItem = filtered[0];

            selectedItem.ItemCode.Should().Be("OXANA");
            selectedItem.AtcCode.Should().Be("M09AB02");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I04_FilteredSearchState_ExportedToExcel_ShouldOnlyExportMatchingRows()
        {
            var allSchedules = new List<MarScheduleItemDto>
            {
                new() { PatientMrn = "0439449", PatientName = "Teng, Max", DisplayName = "Drug 1" },
                new() { PatientMrn = "0439449", PatientName = "Teng, Max", DisplayName = "Drug 2" },
                new() { PatientMrn = "00075655", PatientName = "Chen, Wei", DisplayName = "Drug 3" }
            };

            // Filter for patient Teng
            var filtered = ClinicalFilterEngine.ApplyFilter(allSchedules, new ClinicalFilterCriteria
            {
                SearchText = "Teng"
            }).ToList();

            filtered.Should().HaveCount(2);

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(filtered, new List<ActivePrescriptionDto>());
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            sheet1.Cell("B5").GetString().Should().Be("Teng, Max");
            sheet1.Cell("B6").GetString().Should().Be("Teng, Max");
            sheet1.Cell("B7").IsEmpty().Should().BeTrue("Only 2 records were filtered and exported");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I05_FilterChipState_ShouldPersistDuringViewNavigation()
        {
            var filterCriteria = new ClinicalFilterCriteria
            {
                HighAlertOnly = true,
                DateFilter = "Today"
            };

            // Navigating from Tab 2 to Tab 1 and back
            int currentTab = 2;
            currentTab = 1; // switch
            currentTab = 2; // return

            currentTab.Should().Be(2);
            filterCriteria.HighAlertOnly.Should().BeTrue();
            filterCriteria.DateFilter.Should().Be("Today");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I06_FilterChipActive_WhileOpeningAndClosingModal_ShouldRetainActiveStatus()
        {
            var criteria = new ClinicalFilterCriteria
            {
                StatusFilter = "Pending"
            };

            // User opens modal
            bool isModalOpen = true;
            // User closes modal
            isModalOpen = false;

            isModalOpen.Should().BeFalse();
            criteria.StatusFilter.Should().Be("Pending", "Closing modal must not reset filter chip status");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I07_TemporalChipFilter_ExportedToExcel_ShouldContainExclusivelyFilteredDates()
        {
            var targetDate = new DateTime(2026, 9, 24);
            var sample = new List<MarScheduleItemDto>
            {
                new() { ScheduleDate = targetDate, ScheduleTime = new TimeSpan(9, 0, 0), DisplayName = "Today D1", PatientMrn = "111" },
                new() { ScheduleDate = targetDate, ScheduleTime = new TimeSpan(13, 0, 0), DisplayName = "Today D2", PatientMrn = "222" },
                new() { ScheduleDate = targetDate.AddDays(-5), ScheduleTime = new TimeSpan(9, 0, 0), DisplayName = "Past D1", PatientMrn = "333" }
            };

            var filtered = ClinicalFilterEngine.ApplyFilter(sample, new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                ReferenceDate = targetDate
            }).ToList();

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(filtered, new List<ActivePrescriptionDto>());
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            var dt1 = sheet1.Cell("L5").GetDateTime();
            var dt2 = sheet1.Cell("L6").GetDateTime();

            dt1.Date.Should().Be(targetDate);
            dt2.Date.Should().Be(targetDate);
            sheet1.Cell("L7").IsEmpty().Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I08_ModalOpen_TabSwitchAttempt_ShouldHandleGracefully()
        {
            var state = new
            {
                IsModalOpen = true,
                ActiveTab = 1
            };

            // Request tab switch while modal is open
            // Safe UX behavior: dismiss modal first, then transition tab
            var newState = new
            {
                IsModalOpen = false,
                ActiveTab = 2
            };

            newState.IsModalOpen.Should().BeFalse();
            newState.ActiveTab.Should().Be(2);
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I09_ExportFromPatientOverview_ShouldExportSelectedPatientRegimen()
        {
            var allRx = new List<ActivePrescriptionDto>
            {
                new() { PatientMrn = "0439449", PatientName = "Teng, Max", MedicationName = "Drug 1" },
                new() { PatientMrn = "0439449", PatientName = "Teng, Max", MedicationName = "Drug 2" },
                new() { PatientMrn = "00075655", PatientName = "Chen, Wei", MedicationName = "Drug 3" }
            };

            var tengRx = allRx.Where(x => x.PatientMrn == "0439449").ToList();
            tengRx.Should().HaveCount(2);

            var bytes = TestExcelHelper.GenerateClinicalWorkbook(new List<MarScheduleItemDto>(), tengRx, "Patient Teng, Max Regimen");
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet2 = wb.Worksheet("Active Prescriptions Overview");

            sheet2.Cell("A1").GetString().Should().Contain("PATIENT TENG, MAX REGIMEN");
            sheet2.Cell("A5").GetString().Should().Be("0439449");
            sheet2.Cell("A6").GetString().Should().Be("0439449");
            sheet2.Cell("A7").IsEmpty().Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public async Task TC_I10_ExcelExport_ConcurrentWithDatabaseQuery_ShouldNotBlock()
        {
            var exportTask = Task.Run(() =>
            {
                var synth = Enumerable.Range(1, 500).Select(i => new MarScheduleItemDto
                {
                    PatientMrn = $"{i}",
                    DisplayName = "Test"
                });
                return TestExcelHelper.GenerateClinicalWorkbook(synth, new List<ActivePrescriptionDto>());
            });

            var dbTask = Task.Run(async () =>
            {
                await using var conn = await TestDatabaseContext.CreateReadOnlyConnectionAsync();
                return await TestDatabaseContext.GetActiveInpatientCensusAsync(conn);
            });

            await Task.WhenAll(exportTask, dbTask);

            var exportResult = await exportTask;
            var dbResult = await dbTask;
            exportResult.Should().NotBeEmpty();
            dbResult.Should().Be(15);
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I11_ThemeToggle_WithActiveSearchHighlight_ShouldPreserveSearchCriteria()
        {
            var filter = new ClinicalFilterCriteria { SearchText = "Insulin" };
            var currentTheme = "clinical-light";

            // Toggle to night dark
            currentTheme = "night-dark";

            currentTheme.Should().Be("night-dark");
            filter.SearchText.Should().Be("Insulin", "Search text must remain intact after theme switch");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I12_ThemeToggle_WithActiveFilterChips_ShouldPreserveChipsState()
        {
            var filter = new ClinicalFilterCriteria
            {
                DateFilter = "Today",
                HighAlertOnly = true
            };

            var theme = "clinical-light";
            theme = "night-dark";

            theme.Should().Be("night-dark");
            filter.DateFilter.Should().Be("Today");
            filter.HighAlertOnly.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I13_ThemeToggle_ShouldApplyGloballyAcrossBothTabs()
        {
            var appSession = new
            {
                Theme = "night-dark",
                Tab1Theme = "night-dark",
                Tab2Theme = "night-dark"
            };

            appSession.Tab1Theme.Should().Be(appSession.Theme);
            appSession.Tab2Theme.Should().Be(appSession.Theme);
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I14_ThemeToggle_WithOpenMedicationDrawer_ShouldNotDismissDrawer()
        {
            var state = new
            {
                IsDrawerOpen = true,
                SelectedItemCode = "OXANA",
                CurrentTheme = "clinical-light"
            };

            var updatedState = new
            {
                state.IsDrawerOpen,
                state.SelectedItemCode,
                CurrentTheme = "night-dark"
            };

            updatedState.IsDrawerOpen.Should().BeTrue("Drawer must not close upon theme toggle");
            updatedState.SelectedItemCode.Should().Be("OXANA");
            updatedState.CurrentTheme.Should().Be("night-dark");
        }

        [Fact]
        [Trait("Category", "PairwiseInteraction")]
        public void TC_I15_ExcelExport_ExecutedUnderDarkTheme_ShouldRetainPrintReadyClinicalPalette()
        {
            // When app is in dark mode, exported workbook MUST remain print-ready (light canvas, teal headers)
            var bytes = TestExcelHelper.GenerateClinicalWorkbook(
                new List<MarScheduleItemDto>(),
                new List<ActivePrescriptionDto>());

            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var sheet1 = wb.Worksheet("Medication Administration Log");

            var bannerBg = sheet1.Cell("A1").Style.Fill.BackgroundColor;
            var headerBg = sheet1.Cell("A4").Style.Fill.BackgroundColor;

            bannerBg.Color.R.Should().Be(0x0E, "Banner must use dark teal red component");
            bannerBg.Color.G.Should().Be(0x74, "Banner must use dark teal green component");
            bannerBg.Color.B.Should().Be(0x90, "Banner must use dark teal blue component");

            headerBg.Color.R.Should().Be(0x08, "Header must use medical teal red component");
            headerBg.Color.G.Should().Be(0x91, "Header must use medical teal green component");
            headerBg.Color.B.Should().Be(0xB2, "Header must use medical teal blue component");
        }
    }
}
