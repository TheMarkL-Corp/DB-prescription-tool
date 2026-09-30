using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FluentAssertions;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;
using Xunit;
using Xunit.Abstractions;

namespace MedHubPrescriptionTool.Tests;

/// <summary>
/// Empirical Challenger verification test suite for Milestone 3 (Smart Multi-Sheet Excel Export Engine).
/// Validates ClosedXML workbook generation, 2-sheet structure, exact 17 & 16 column schema contracts,
/// text formatting preserving leading zeros (MRN and Case No), date/time & numeric formats,
/// clinical styling (Medical Teal banner, status badges, freeze panes, auto-fit),
/// OpenXML ZIP packaging integrity, disk I/O, and adversarial edge cases
/// (empty collections, single items, extreme doses, XSS/formula/SQL/Unicode injection, null safety, high volume).
/// </summary>
public class Milestone3_AdversarialExcelExportTests
{
    private readonly ITestOutputHelper _output;
    private readonly DbConnectionSettings _settings;
    private readonly DatabaseConnectionFactory _factory;
    private readonly DatabaseRepository _repository;
    private readonly ClinicalDataService _clinicalService;
    private readonly ExcelExportService _excelService;

    public Milestone3_AdversarialExcelExportTests(ITestOutputHelper output)
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
        _clinicalService = new ClinicalDataService(_repository);
        _excelService = new ExcelExportService();
    }

    #region 1. Live Database Snapshot Tests

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp01_LiveSnapshot_WorkbookGeneration_And_ZipIntegrity()
    {
        // 1. Load clinical snapshot from live PostgreSQL amisdbV2
        var snapshot = await _clinicalService.LoadClinicalSnapshotAsync();
        snapshot.Should().NotBeNull();
        snapshot.MarSchedules.Count.Should().Be(385);
        snapshot.ActivePrescriptions.Count.Should().Be(31);

        // 2. Generate multi-sheet workbook
        var sw = Stopwatch.StartNew();
        byte[] bytes = _excelService.GenerateMultiSheetExcel(
            snapshot.MarSchedules,
            snapshot.ActivePrescriptions,
            "MedHub Inpatient Clinical Administration");
        sw.Stop();

        _output.WriteLine($"Generated live Excel workbook: {bytes.Length:N0} bytes in {sw.ElapsedMilliseconds}ms");
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(8192, "Workbook for 385 MAR items + 31 prescriptions must be substantial (>8KB)");

        // 3. Verify OpenXML ZIP package structure and required XML parts
        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("[Content_Types].xml");
        entryNames.Should().Contain("_rels/.rels");
        entryNames.Should().Contain("xl/workbook.xml");
        entryNames.Should().Contain("xl/worksheets/sheet1.xml");
        entryNames.Should().Contain("xl/worksheets/sheet2.xml");
        entryNames.Should().Contain("xl/styles.xml");

        // Verify each XML part can be decompressed without errors
        foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
        {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            string content = reader.ReadToEnd();
            content.Should().NotBeNullOrWhiteSpace($"Entry '{entry.FullName}' must contain valid XML content");
        }

        // 4. Verify ClosedXML can open the workbook and discover both sheets
        using var workbookStream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(workbookStream);
        wb.Worksheets.Count.Should().Be(2);
        wb.Worksheets.Contains("MAR Schedule & Dispense Log").Should().BeTrue();
        wb.Worksheets.Contains("Active Prescriptions Overview").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp02_LiveSnapshot_Sheet1_MarSchedule_Exact17Headers_And_385Rows()
    {
        var snapshot = await _clinicalService.LoadClinicalSnapshotAsync();
        byte[] bytes = _excelService.GenerateMultiSheetExcel(snapshot.MarSchedules, snapshot.ActivePrescriptions);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        sheet1.Should().NotBeNull();

        // 1. Verify Top Title Banner (Row 1)
        var bannerCell = sheet1.Cell(1, 1);
        bannerCell.GetString().Should().Contain("MEDHUB CLINICAL INPATIENT MEDICATION ADMINISTRATION & DISPENSE LOG");
        bannerCell.Style.Font.Bold.Should().BeTrue();
        bannerCell.Style.Font.FontSize.Should().Be(14);
        bannerCell.Style.Font.FontName.Should().Be("Segoe UI");
        bannerCell.Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#0891B2")); // Medical Teal

        // 2. Verify Sub-Banner (Row 2)
        var subBannerCell = sheet1.Cell(2, 1);
        subBannerCell.GetString().Should().Contain("Export Generated:");
        subBannerCell.GetString().Should().Contain("Total Scheduled Administrations: 385");
        subBannerCell.Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#155E75"));

        // 3. Verify Exact 17 Column Headers (Row 4)
        string[] expectedHeaders =
        [
            "Patient MRN",
            "Patient Name",
            "Care Station",
            "Bed",
            "Medication Code",
            "Display Name",
            "Generic Name",
            "Dose Ordered",
            "Unit",
            "Frequency",
            "Route",
            "Scheduled Date & Time",
            "Dispense Status",
            "Dispensed Qty",
            "Status Time",
            "Dispensing Nurse",
            "Witnessing Nurse / Notes"
        ];

        expectedHeaders.Length.Should().Be(17);

        for (int col = 1; col <= 17; col++)
        {
            string actualHeader = sheet1.Cell(4, col).GetString().Trim();
            actualHeader.Should().Be(expectedHeaders[col - 1], $"Sheet 1 Column {col} header must match contract");
            sheet1.Cell(4, col).Style.Font.Bold.Should().BeTrue();
            sheet1.Cell(4, col).Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#0E7490"));
        }

        // 4. Verify Row Count: 4 header/banner rows + exactly 385 data rows = last row 389
        int lastRow = sheet1.LastRowUsed()?.RowNumber() ?? 0;
        int dataRowCount = lastRow - 4;
        dataRowCount.Should().Be(385, "Sheet 1 must contain exactly 385 administration schedule data rows");

        // 5. Verify Freeze Panes: Row 4 frozen
        sheet1.SheetView.SplitRow.Should().Be(4, "Rows 1-4 must be frozen for scrolling");
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp03_LiveSnapshot_Sheet2_ActivePrescriptions_Exact16Headers_And_31Rows()
    {
        var snapshot = await _clinicalService.LoadClinicalSnapshotAsync();
        byte[] bytes = _excelService.GenerateMultiSheetExcel(snapshot.MarSchedules, snapshot.ActivePrescriptions);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        sheet2.Should().NotBeNull();

        // 1. Verify Top Title Banner (Row 1)
        var bannerCell = sheet2.Cell(1, 1);
        bannerCell.GetString().Should().Contain("MEDHUB ACTIVE INPATIENT PRESCRIPTIONS & CLINICAL REGIMENS");
        bannerCell.Style.Font.Bold.Should().BeTrue();
        bannerCell.Style.Font.FontSize.Should().Be(14);
        bannerCell.Style.Font.FontName.Should().Be("Segoe UI");
        bannerCell.Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#0891B2"));

        // 2. Verify Sub-Banner (Row 2)
        var subBannerCell = sheet2.Cell(2, 1);
        subBannerCell.GetString().Should().Contain("Active Orders Snapshot:");
        subBannerCell.GetString().Should().Contain("Total Active Orders: 31");
        subBannerCell.Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#155E75"));

        // 3. Verify Exact 16 Column Headers (Row 4)
        string[] expectedHeaders =
        [
            "Patient MRN",
            "Patient Name",
            "Case Number",
            "Care Station",
            "Bed",
            "Attending Physician",
            "Medication Code",
            "Display Name",
            "Dosage Ordered & Unit",
            "Frequency",
            "Route",
            "Clinical Indication",
            "Order Start Time",
            "Order End Time",
            "Total Scheduled Doses",
            "Order Status"
        ];

        expectedHeaders.Length.Should().Be(16);

        for (int col = 1; col <= 16; col++)
        {
            string actualHeader = sheet2.Cell(4, col).GetString().Trim();
            actualHeader.Should().Be(expectedHeaders[col - 1], $"Sheet 2 Column {col} header must match contract");
            sheet2.Cell(4, col).Style.Font.Bold.Should().BeTrue();
            sheet2.Cell(4, col).Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#0E7490"));
        }

        // 4. Verify Row Count: 4 header/banner rows + exactly 31 data rows = last row 35
        int lastRow = sheet2.LastRowUsed()?.RowNumber() ?? 0;
        int dataRowCount = lastRow - 4;
        dataRowCount.Should().Be(31, "Sheet 2 must contain exactly 31 active prescription data rows");

        // 5. Verify Freeze Panes: Row 4 frozen
        sheet2.SheetView.SplitRow.Should().Be(4, "Rows 1-4 must be frozen for scrolling");
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp04_LiveSnapshot_LeadingZeros_PreservedOnMrn_And_CaseNo()
    {
        var snapshot = await _clinicalService.LoadClinicalSnapshotAsync();
        byte[] bytes = _excelService.GenerateMultiSheetExcel(snapshot.MarSchedules, snapshot.ActivePrescriptions);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        var sheet2 = wb.Worksheet("Active Prescriptions Overview");

        // Target known leading zero MRNs in DB:
        // Chen, Wei has MRN "00075655"
        // Teng, Max has MRN "0439449"
        // Huang, Zhi has MRN "0105313"
        var leadingZeroMrns = new HashSet<string> { "00075655", "0439449", "0105313" };
        var foundSheet1Mrns = new HashSet<string>();
        var foundSheet2Mrns = new HashSet<string>();

        // Check Sheet 1 (MAR)
        for (int row = 5; row <= 389; row++)
        {
            var cell = sheet1.Cell(row, 1);
            cell.DataType.Should().Be(XLDataType.Text, $"Sheet 1 Row {row} MRN must have DataType Text");
            cell.Style.NumberFormat.Format.Should().Be("@", $"Sheet 1 Row {row} MRN must have explicit text format '@'");

            string val = cell.GetString();
            val.Should().NotBeNullOrWhiteSpace();
            if (leadingZeroMrns.Contains(val))
            {
                foundSheet1Mrns.Add(val);
                val.StartsWith('0').Should().BeTrue("Leading zeros must not be stripped");
            }
        }

        _output.WriteLine($"Sheet 1 Found Leading-Zero MRNs: {string.Join(", ", foundSheet1Mrns)}");
        foundSheet1Mrns.Should().Contain("0439449", "Teng, Max's single-zero MRN must be present with leading zeros");
        foundSheet1Mrns.Should().Contain("0105313", "Huang, Zhi's single-zero MRN must be present with leading zeros");

        // Check Sheet 2 (Active Prescriptions)
        for (int row = 5; row <= 35; row++)
        {
            // Col 1: Patient MRN
            var mrnCell = sheet2.Cell(row, 1);
            mrnCell.DataType.Should().Be(XLDataType.Text, $"Sheet 2 Row {row} MRN must have DataType Text");
            mrnCell.Style.NumberFormat.Format.Should().Be("@");
            string mrnVal = mrnCell.GetString();
            if (leadingZeroMrns.Contains(mrnVal))
            {
                foundSheet2Mrns.Add(mrnVal);
            }

            // Col 3: Case Number
            var caseCell = sheet2.Cell(row, 3);
            caseCell.DataType.Should().Be(XLDataType.Text, $"Sheet 2 Row {row} Case No must have DataType Text");
            caseCell.Style.NumberFormat.Format.Should().Be("@");
            caseCell.GetString().Should().NotBeNullOrWhiteSpace();
        }

        _output.WriteLine($"Sheet 2 Found Leading-Zero MRNs: {string.Join(", ", foundSheet2Mrns)}");
        foundSheet2Mrns.Should().Contain("0439449");
        foundSheet2Mrns.Should().Contain("0105313");
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp05_LiveSnapshot_DataTypes_And_Formats_Inspection()
    {
        var snapshot = await _clinicalService.LoadClinicalSnapshotAsync();
        byte[] bytes = _excelService.GenerateMultiSheetExcel(snapshot.MarSchedules, snapshot.ActivePrescriptions);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        var sheet2 = wb.Worksheet("Active Prescriptions Overview");

        // Verify Sheet 1 cell types
        for (int row = 5; row <= 389; row++)
        {
            // Col 5: Medication Code (Text '@')
            sheet1.Cell(row, 5).DataType.Should().Be(XLDataType.Text);
            sheet1.Cell(row, 5).Style.NumberFormat.Format.Should().Be("@");

            // Col 8: Dose Ordered (Number '#,##0.00')
            var doseCell = sheet1.Cell(row, 8);
            doseCell.DataType.Should().Be(XLDataType.Number);
            doseCell.Style.NumberFormat.Format.Should().Be("#,##0.00");
            doseCell.GetDouble().Should().BeGreaterThan(0, "Prescribed dose must be positive");

            // Col 12: Scheduled Date & Time (DateTime 'yyyy-mm-dd hh:mm')
            var schedCell = sheet1.Cell(row, 12);
            schedCell.DataType.Should().Be(XLDataType.DateTime);
            schedCell.Style.DateFormat.Format.Should().Be("yyyy-mm-dd hh:mm");

            // Col 14: Dispensed Qty (Number when present, else "-")
            var qtyCell = sheet1.Cell(row, 14);
            if (qtyCell.GetString() != "-")
            {
                qtyCell.DataType.Should().Be(XLDataType.Number);
                qtyCell.Style.NumberFormat.Format.Should().Be("#,##0.00");
            }

            // Col 15: Status Time (DateTime when present, else "-")
            var stCell = sheet1.Cell(row, 15);
            if (stCell.GetString() != "-")
            {
                stCell.DataType.Should().Be(XLDataType.DateTime);
                stCell.Style.DateFormat.Format.Should().Be("yyyy-mm-dd hh:mm:ss");
            }
        }

        // Verify Sheet 2 cell types
        for (int row = 5; row <= 35; row++)
        {
            // Col 7: Medication Code (Text '@')
            sheet2.Cell(row, 7).DataType.Should().Be(XLDataType.Text);
            sheet2.Cell(row, 7).Style.NumberFormat.Format.Should().Be("@");

            // Col 13: Order Start Time (DateTime when present, else "-")
            var startCell = sheet2.Cell(row, 13);
            if (startCell.GetString() != "-")
            {
                startCell.DataType.Should().Be(XLDataType.DateTime);
                startCell.Style.DateFormat.Format.Should().Be("yyyy-mm-dd hh:mm");
            }

            // Col 14: Order End Time (DateTime when present, else "Ongoing")
            var endCell = sheet2.Cell(row, 14);
            if (endCell.GetString() != "Ongoing")
            {
                endCell.DataType.Should().Be(XLDataType.DateTime);
                endCell.Style.DateFormat.Format.Should().Be("yyyy-mm-dd hh:mm");
            }

            // Col 15: Total Scheduled Doses (Number '#,##0')
            var dosesCell = sheet2.Cell(row, 15);
            dosesCell.DataType.Should().Be(XLDataType.Number);
            dosesCell.Style.NumberFormat.Format.Should().Be("#,##0");
            dosesCell.GetDouble().Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp06_LiveSnapshot_StatusBadges_And_HighAlertStyling()
    {
        var snapshot = await _clinicalService.LoadClinicalSnapshotAsync();
        byte[] bytes = _excelService.GenerateMultiSheetExcel(snapshot.MarSchedules, snapshot.ActivePrescriptions);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        var sheet2 = wb.Worksheet("Active Prescriptions Overview");

        int dispensedCount = 0;
        int pendingCount = 0;
        int returnedCount = 0;
        int wastedCount = 0;
        int highAlertCountMar = 0;

        int oddRowDispensedWithBg = 0;
        int evenRowDispensedWithZebra = 0;
        int oddRowPendingWithBg = 0;
        int evenRowPendingWithZebra = 0;

        for (int row = 5; row <= 389; row++)
        {
            var statusCell = sheet1.Cell(row, 13);
            string status = statusCell.GetString();
            bool isEvenRow = (row % 2 == 0);

            if (status.Equals("Dispensed", StringComparison.OrdinalIgnoreCase))
            {
                dispensedCount++;
                // Font color is always preserved
                statusCell.Style.Font.FontColor.Should().Be(XLColor.FromHtml("#065F46"));
                statusCell.Style.Font.Bold.Should().BeTrue();

                if (isEvenRow)
                {
                    // Defect confirmed: On even rows, zebra striping overwrites status background
                    if (statusCell.Style.Fill.BackgroundColor == XLColor.FromHtml("#F8FAFC"))
                    {
                        evenRowDispensedWithZebra++;
                    }
                }
                else
                {
                    // On odd rows, badge background is intact
                    if (statusCell.Style.Fill.BackgroundColor == XLColor.FromHtml("#D1FAE5"))
                    {
                        oddRowDispensedWithBg++;
                    }
                }
            }
            else if (status.StartsWith("Pending", StringComparison.OrdinalIgnoreCase))
            {
                pendingCount++;
                statusCell.Style.Font.FontColor.Should().Be(XLColor.FromHtml("#92400E"));

                if (isEvenRow)
                {
                    if (statusCell.Style.Fill.BackgroundColor == XLColor.FromHtml("#F8FAFC"))
                    {
                        evenRowPendingWithZebra++;
                    }
                }
                else
                {
                    if (statusCell.Style.Fill.BackgroundColor == XLColor.FromHtml("#FEF3C7"))
                    {
                        oddRowPendingWithBg++;
                    }
                }
            }
            else if (status.Equals("Returned", StringComparison.OrdinalIgnoreCase))
            {
                returnedCount++;
                statusCell.Style.Font.FontColor.Should().Be(XLColor.FromHtml("#581C87"));
            }
            else if (status.Equals("Wasted", StringComparison.OrdinalIgnoreCase))
            {
                wastedCount++;
                statusCell.Style.Font.FontColor.Should().Be(XLColor.FromHtml("#7C2D12"));
            }

            // Check High-Alert highlighting on Col 6 (Display Name)
            var dispNameCell = sheet1.Cell(row, 6);
            if (dispNameCell.Style.Font.FontColor == XLColor.FromHtml("#DC2626"))
            {
                highAlertCountMar++;
                dispNameCell.Style.Font.Bold.Should().BeTrue();
            }
        }

        _output.WriteLine($"MAR Statuses: Dispensed={dispensedCount}, Pending={pendingCount}, Returned={returnedCount}, Wasted={wastedCount}, HighAlert={highAlertCountMar}");
        _output.WriteLine($"Dispensed Badges: Odd rows intact={oddRowDispensedWithBg}, Even rows overwritten by zebra={evenRowDispensedWithZebra}");
        _output.WriteLine($"Pending Badges: Odd rows intact={oddRowPendingWithBg}, Even rows overwritten by zebra={evenRowPendingWithZebra}");

        dispensedCount.Should().Be(13, "Must match ground truth of 13 dispensed doses");
        pendingCount.Should().Be(370, "Must match ground truth of 370 pending doses");
        returnedCount.Should().Be(1, "Must match ground truth of 1 returned dose");
        wastedCount.Should().Be(1, "Must match ground truth of 1 wasted dose");
        highAlertCountMar.Should().Be(0, "Live dataset MAR contains 0 isHighAlert=true medications (EVOR2 has no active prescriptions)");

        // Verify Sheet 2 Active Prescriptions status badges and zebra striping
        int sheet2ActiveOddWithBg = 0;
        int sheet2ActiveEvenWithZebra = 0;
        for (int row = 5; row <= 35; row++)
        {
            var statusCell = sheet2.Cell(row, 16);
            statusCell.GetString().Should().Be("Active");
            statusCell.Style.Font.FontColor.Should().Be(XLColor.FromHtml("#065F46"));
            statusCell.Style.Font.Bold.Should().BeTrue();

            if (row % 2 == 0)
            {
                if (statusCell.Style.Fill.BackgroundColor == XLColor.FromHtml("#F8FAFC"))
                {
                    sheet2ActiveEvenWithZebra++;
                }
            }
            else
            {
                if (statusCell.Style.Fill.BackgroundColor == XLColor.FromHtml("#D1FAE5"))
                {
                    sheet2ActiveOddWithBg++;
                }
            }
        }
        _output.WriteLine($"Sheet 2 Active Orders: Odd rows intact={sheet2ActiveOddWithBg}, Even rows overwritten by zebra={sheet2ActiveEvenWithZebra}");
        sheet2ActiveEvenWithZebra.Should().BeGreaterThan(0, "Confirms zebra striping overwritten badge background on even rows in Sheet 2");
    }

    #endregion

    #region 2. Adversarial Edge Cases & Boundary Tests

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp07_EdgeCase_EmptyCollections_GracefulHandling()
    {
        // Export 0 MAR items and 0 active prescriptions
        byte[] bytes = _excelService.GenerateMultiSheetExcel(
            new List<MarScheduleItemDto>(),
            new List<ActivePrescriptionDto>(),
            "Empty Ward Simulation");

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(4000, "Workbook with empty collections must still produce a valid Excel file");

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        wb.Worksheets.Count.Should().Be(2);

        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        sheet1.Cell(4, 1).GetString().Should().Be("Patient MRN");
        sheet1.Cell(4, 17).GetString().Should().Be("Witnessing Nurse / Notes");
        var emptyCell1 = sheet1.Cell(5, 1);
        emptyCell1.GetString().Should().Contain("No medication administration records found");
        emptyCell1.Style.Font.Italic.Should().BeTrue();

        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        sheet2.Cell(4, 1).GetString().Should().Be("Patient MRN");
        sheet2.Cell(4, 16).GetString().Should().Be("Order Status");
        var emptyCell2 = sheet2.Cell(5, 1);
        emptyCell2.GetString().Should().Contain("No active prescription records found");
        emptyCell2.Style.Font.Italic.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp08_EdgeCase_SingleFilteredItem()
    {
        var singleMar = new List<MarScheduleItemDto>
        {
            new()
            {
                Schedule_Id = Guid.NewGuid(),
                Patient_Mrn = "00012345",
                Patient_Name = "Smith, John",
                Station_Name = "Intensive Care Unit",
                Bed_Name = "ICU-01",
                Item_Code = "MED-999",
                Display_Name = "Norepinephrine 4mg/250ml",
                Generic_Name = "Norepinephrine",
                Dosage_Ordered_Value = 0.05m,
                Dosage_Ordered_Unit = "mcg/kg/min",
                Frequency = "CONTINUOUS",
                Route = "IV INFUSION",
                Schedule_Date = new DateTime(2026, 9, 24),
                Schedule_Time = new TimeSpan(8, 0, 0),
                Dispense_Status = "Dispensed",
                Dispensed_Qty = 1.0m,
                Status_Time = new DateTime(2026, 9, 24, 8, 5, 0),
                Dispense_Nurse_Name = "Nurse Nightingale",
                isHighAlert = true
            }
        };

        var singleRx = new List<ActivePrescriptionDto>
        {
            new()
            {
                Med_Order_Id = Guid.NewGuid(),
                Patient_Mrn = "00012345",
                Patient_Name = "Smith, John",
                Case_No = "CAS-202609-001",
                Care_Station = "Intensive Care Unit",
                Room_Bed = "ICU-01",
                Attending_Physician = "Dr. House",
                Medication_Code = "MED-999",
                Medication_Name = "Norepinephrine 4mg/250ml",
                Dosage_Ordered_Value = 0.05m,
                Dosage_Ordered_Unit = "mcg/kg/min",
                Frequency = "CONTINUOUS",
                Route = "IV INFUSION",
                Clinical_Indication = "Septic Shock Vasopressor Support",
                Start_Datetime = new DateTime(2026, 9, 24, 6, 0, 0),
                End_Datetime = null,
                Total_Sched_Doses = 24,
                Order_Status = "01",
                isHighAlert = true
            }
        };

        byte[] bytes = _excelService.GenerateMultiSheetExcel(singleMar, singleRx);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        sheet1.LastRowUsed()!.RowNumber().Should().Be(5, "Sheet 1 must have exactly 1 data row (row 5)");
        sheet1.Cell(5, 1).GetString().Should().Be("00012345");
        sheet1.Cell(5, 1).DataType.Should().Be(XLDataType.Text);
        sheet1.Cell(5, 2).GetString().Should().Be("Smith, John");
        sheet1.Cell(5, 6).GetString().Should().Be("Norepinephrine 4mg/250ml");
        sheet1.Cell(5, 6).Style.Font.FontColor.Should().Be(XLColor.FromHtml("#DC2626")); // High-alert
        sheet1.Cell(5, 8).GetDouble().Should().Be(0.05);

        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        sheet2.LastRowUsed()!.RowNumber().Should().Be(5, "Sheet 2 must have exactly 1 data row (row 5)");
        sheet2.Cell(5, 1).GetString().Should().Be("00012345");
        sheet2.Cell(5, 3).GetString().Should().Be("CAS-202609-001");
        sheet2.Cell(5, 14).GetString().Should().Be("Ongoing");
        sheet2.Cell(5, 15).GetDouble().Should().Be(24);
        sheet2.Cell(5, 16).GetString().Should().Be("Active");
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp09_EdgeCase_ExtremeDosageNumbers_And_ZeroValues()
    {
        var extremeMar = new List<MarScheduleItemDto>
        {
            new()
            {
                Patient_Mrn = "00000001",
                Patient_Name = "Micro Dose",
                Item_Code = "EX-001",
                Display_Name = "Fentanyl",
                Dosage_Ordered_Value = 0.0001m, // Very small decimal
                Dosage_Ordered_Unit = "mg",
                Frequency = "STAT",
                Route = "IV",
                Schedule_Date = DateTime.Today,
                Dispense_Status = "Dispensed",
                Dispensed_Qty = 0.0001m
            },
            new()
            {
                Patient_Mrn = "00000002",
                Patient_Name = "Macro Dose",
                Item_Code = "EX-002",
                Display_Name = "Albumin 20%",
                Dosage_Ordered_Value = 99999.99m, // Large decimal
                Dosage_Ordered_Unit = "mL",
                Frequency = "QD",
                Route = "IV",
                Schedule_Date = DateTime.Today,
                Dispense_Status = "Pending",
                Dispensed_Qty = null
            },
            new()
            {
                Patient_Mrn = "00000003",
                Patient_Name = "Zero Dose",
                Item_Code = "EX-003",
                Display_Name = "Placebo",
                Dosage_Ordered_Value = 0.0m, // Zero
                Dosage_Ordered_Unit = "tab",
                Frequency = "PRN",
                Route = "PO",
                Schedule_Date = DateTime.Today,
                Dispense_Status = "Cancelled",
                Dispensed_Qty = 0.0m
            }
        };

        var extremeRx = new List<ActivePrescriptionDto>
        {
            new()
            {
                Patient_Mrn = "00000001",
                Case_No = "00009999",
                Patient_Name = "Mega Doses",
                Medication_Code = "EX-001",
                Medication_Name = "High Count",
                Dosage_Ordered_Value = 99999.99m,
                Dosage_Ordered_Unit = "units",
                Total_Sched_Doses = 9999999, // 7-digit integer
                Order_Status = "Active"
            },
            new()
            {
                Patient_Mrn = "00000002",
                Case_No = "00000000",
                Patient_Name = "Zero Sched Doses",
                Medication_Code = "EX-002",
                Medication_Name = "Zero Count",
                Dosage_Ordered_Value = 0m,
                Dosage_Ordered_Unit = "tab",
                Total_Sched_Doses = 0, // Zero doses
                Order_Status = "Active"
            }
        };

        byte[] bytes = _excelService.GenerateMultiSheetExcel(extremeMar, extremeRx);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        sheet1.Cell(5, 8).GetDouble().Should().BeApproximately(0.0001, 0.00001);
        sheet1.Cell(6, 8).GetDouble().Should().Be(99999.99);
        sheet1.Cell(7, 8).GetDouble().Should().Be(0.0);

        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        sheet2.Cell(5, 15).GetDouble().Should().Be(9999999);
        sheet2.Cell(6, 15).GetDouble().Should().Be(0);
        sheet2.Cell(6, 3).GetString().Should().Be("00000000");
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp10_EdgeCase_SpecialCharacters_And_AdversarialInjectionSecurity()
    {
        // Adversarial input suite:
        // - XSS vectors: <script>, <img>
        // - Formula injection: =SUM(A1:A10), @CMD, +cmd, -1+1
        // - SQL injection fragments: '; DROP TABLE ...
        // - Multi-lingual / Unicode / CJK: Chinese, Japanese, Arabic, Emojis
        // - Special formatting: quotes, backslashes, tabs, newlines
        var adversarialMar = new List<MarScheduleItemDto>
        {
            new()
            {
                Patient_Mrn = "00099999",
                Patient_Name = "陳大文 <script>alert('xss')</script>",
                Station_Name = "Station & < > \" ' ` 7A",
                Bed_Name = "Bed #101/A\\B",
                Item_Code = "=SUM(A1:A10)", // Formula injection attempt
                Display_Name = "生理食鹽水 500毫升 💊",
                Generic_Name = "Sodium Chloride 0.9% \\ / ' \"",
                Dosage_Ordered_Value = 500m,
                Dosage_Ordered_Unit = "mL",
                Frequency = "@cmd|' /C calc'!A0", // DDE injection attempt
                Route = "IV 靜脈注射",
                Schedule_Date = DateTime.Today,
                Dispense_Status = "Dispensed",
                Dispensed_Qty = 500m,
                Dispense_Nurse_Name = "李護理師 💉 &amp; <test>",
                Instruction_Note = "Notes with\r\nmultiline\ttext and \"quotes\" and 'single' and ⚠️ emojis"
            }
        };

        var adversarialRx = new List<ActivePrescriptionDto>
        {
            new()
            {
                Patient_Mrn = "00099999",
                Case_No = "'+(SELECT 'pwn')+'",
                Patient_Name = "陳大文 <script>alert('xss')</script>",
                Care_Station = "Station & < > \" ' ` 7A",
                Room_Bed = "Bed #101/A\\B",
                Attending_Physician = "Dr. O'Connor & Dr. 张三",
                Medication_Code = "=1+1",
                Medication_Name = "生理食鹽水 500毫升 💊",
                Dosage_Ordered_Value = 500m,
                Dosage_Ordered_Unit = "mL",
                Frequency = "-cmd",
                Route = "IV 靜脈注射",
                Clinical_Indication = "Dehydration / 脫水症狀 / الجفاف / <xml>test</xml>",
                Total_Sched_Doses = 5,
                Order_Status = "Active"
            }
        };

        // Generation must succeed without crashing
        byte[] bytes = _excelService.GenerateMultiSheetExcel(adversarialMar, adversarialRx);
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(5000);

        // Workbook must open without corruption or parse errors
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        sheet1.Cell(5, 2).GetString().Should().Be("陳大文 <script>alert('xss')</script>");
        sheet1.Cell(5, 5).GetString().Should().Be("=SUM(A1:A10)");
        sheet1.Cell(5, 6).GetString().Should().Be("生理食鹽水 500毫升 💊");
        sheet1.Cell(5, 10).GetString().Should().Be("@cmd|' /C calc'!A0");
        sheet1.Cell(5, 16).GetString().Should().Be("李護理師 💉 &amp; <test>");
        sheet1.Cell(5, 17).GetString().Should().Contain("multiline");

        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        sheet2.Cell(5, 3).GetString().Should().Be("'+(SELECT 'pwn')+'");
        sheet2.Cell(5, 6).GetString().Should().Be("Dr. O'Connor & Dr. 张三");
        sheet2.Cell(5, 7).GetString().Should().Be("=1+1");
        sheet2.Cell(5, 12).GetString().Should().Be("Dehydration / 脫水症狀 / الجفاف / <xml>test</xml>");
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp11_EdgeCase_NullAndMissingFields_NullSafety()
    {
        // Object with every single nullable property explicitly set to null
        var sparseMar = new List<MarScheduleItemDto>
        {
            new()
            {
                Schedule_Id = Guid.NewGuid(),
                Patient_Mrn = "00055555",
                Patient_Name = "Minimal Patient",
                Station_Code = null,
                Station_Name = null,
                Bed_Code = null,
                Bed_Name = null,
                Item_Code = "MIN-01",
                Display_Name = "Minimal Med",
                Generic_Name = null,
                Dosage_Ordered_Value = 10m,
                Dosage_Ordered_Unit = "mg",
                Frequency = "QD",
                Route = "PO",
                Schedule_Date = DateTime.Today,
                Dispense_Status = "", // empty status
                Dispensed_Qty = null,
                Status_Time = null,
                Dispense_Nurse_Name = null,
                Confirmed_Nurse_Name = null,
                Return_Reason_Name = null,
                Waste_Reason_Name = null,
                Cancel_Reason_Name = null,
                Wintness_Required = null,
                Instruction_Note = null,
                isHighAlert = false
            }
        };

        var sparseRx = new List<ActivePrescriptionDto>
        {
            new()
            {
                Med_Order_Id = Guid.NewGuid(),
                Patient_Mrn = "00055555",
                Case_No = "CAS-000",
                Patient_Name = "Minimal Patient",
                Care_Station = "",
                Station_Code = "",
                Room_Bed = "",
                Bed_Code = "",
                Attending_Physician = null!,
                Medication_Code = "MIN-01",
                Medication_Name = "Minimal Med",
                Dosage_Ordered_Value = 10m,
                Dosage_Ordered_Unit = "mg",
                Frequency = "QD",
                Route = "PO",
                Clinical_Indication = null,
                Start_Datetime = null,
                End_Datetime = null,
                Total_Sched_Doses = 1,
                Order_Status = null!, // null status
                isHighAlert = false
            }
        };

        // Must not throw NullReferenceException
        var act = () => _excelService.GenerateMultiSheetExcel(sparseMar, sparseRx);
        act.Should().NotThrow();

        byte[] bytes = act();
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        sheet1.Cell(5, 3).GetString().Should().Be("-"); // Station fallback
        sheet1.Cell(5, 4).GetString().Should().Be("-"); // Bed fallback
        sheet1.Cell(5, 7).GetString().Should().Be("-"); // Generic Name fallback
        sheet1.Cell(5, 14).GetString().Should().Be("-"); // Dispensed Qty fallback
        sheet1.Cell(5, 15).GetString().Should().Be("-"); // Status Time fallback
        sheet1.Cell(5, 16).GetString().Should().Be("-"); // Nurse fallback
        sheet1.Cell(5, 17).GetString().Should().Be("-"); // Notes fallback

        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        sheet2.Cell(5, 4).GetString().Should().Be("-"); // Station fallback
        sheet2.Cell(5, 5).GetString().Should().Be("-"); // Bed fallback
        sheet2.Cell(5, 6).GetString().Should().Be("-"); // Physician fallback
        sheet2.Cell(5, 12).GetString().Should().Be("-"); // Indication fallback
        sheet2.Cell(5, 13).GetString().Should().Be("-"); // Start Date fallback
        sheet2.Cell(5, 14).GetString().Should().Be("Ongoing"); // End Date fallback
        sheet2.Cell(5, 16).GetString().Should().Be("Active"); // Null status defaults to Active
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public async Task Emp12_DiskExport_And_FilenameGeneration()
    {
        // 1. Filename pattern verification
        string defaultFilename = _excelService.GenerateTimestampedFilename();
        defaultFilename.Should().MatchRegex(@"^MedHub_Prescriptions_\d{8}_\d{4}\.xlsx$");

        string customFilename = _excelService.GenerateTimestampedFilename("MedHub_ICU_Report");
        customFilename.Should().MatchRegex(@"^MedHub_ICU_Report_\d{8}_\d{4}\.xlsx$");

        // 2. Disk I/O export verification
        string tempDir = Path.Combine(Path.GetTempPath(), "MedHub_Challenger_Test_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var marList = new List<MarScheduleItemDto>
            {
                new()
                {
                    Patient_Mrn = "00012345",
                    Patient_Name = "Test Patient",
                    Display_Name = "Test Med",
                    Dosage_Ordered_Value = 1.0m,
                    Dosage_Ordered_Unit = "tab",
                    Frequency = "QD",
                    Route = "PO",
                    Schedule_Date = DateTime.Today,
                    Dispense_Status = "Pending"
                }
            };
            var rxList = new List<ActivePrescriptionDto>
            {
                new()
                {
                    Patient_Mrn = "00012345",
                    Case_No = "00012345-01",
                    Patient_Name = "Test Patient",
                    Medication_Code = "TEST-01",
                    Medication_Name = "Test Med",
                    Dosage_Ordered_Value = 1.0m,
                    Dosage_Ordered_Unit = "tab",
                    Frequency = "QD",
                    Route = "PO",
                    Total_Sched_Doses = 1,
                    Order_Status = "01"
                }
            };

            // Test without .xlsx extension in custom filename (service must auto-append)
            string savedPath = await _excelService.ExportToFileAsync(
                tempDir,
                marList,
                rxList,
                "CustomTestExport");

            savedPath.Should().EndWith(".xlsx");
            File.Exists(savedPath).Should().BeTrue("Exported file must exist on disk");

            long fileSize = new FileInfo(savedPath).Length;
            fileSize.Should().BeGreaterThan(4000);

            // Verify file can be read as a valid ClosedXML workbook from disk
            using var fileWb = new XLWorkbook(savedPath);
            fileWb.Worksheets.Count.Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp13_HighVolumeStress_5000MarItems_PerformanceAndMemory()
    {
        // Stress test generation of 5,000 MAR items and 500 active prescriptions
        var largeMarList = new List<MarScheduleItemDto>(5000);
        for (int i = 1; i <= 5000; i++)
        {
            largeMarList.Add(new MarScheduleItemDto
            {
                Schedule_Id = Guid.NewGuid(),
                Patient_Mrn = (i % 1000).ToString("D8"),
                Patient_Name = $"Inpatient #{i}",
                Station_Name = $"Station {i % 10 + 1}",
                Bed_Name = $"Bed {i % 50 + 1}",
                Item_Code = $"DRUG-{(i % 200 + 1):D4}",
                Display_Name = $"Medication Formulation #{i % 200 + 1}",
                Generic_Name = $"Generic Formulation #{i % 200 + 1}",
                Dosage_Ordered_Value = (i % 50 + 1) * 2.5m,
                Dosage_Ordered_Unit = "mg",
                Frequency = "TID",
                Route = "PO",
                Schedule_Date = DateTime.Today.AddDays(i % 7),
                Schedule_Time = TimeSpan.FromHours(i % 24),
                Dispense_Status = (i % 3 == 0) ? "Dispensed" : "Pending",
                Dispensed_Qty = (i % 3 == 0) ? (decimal?)(i % 50 + 1) * 2.5m : null,
                isHighAlert = (i % 10 == 0)
            });
        }

        var largeRxList = new List<ActivePrescriptionDto>(500);
        for (int i = 1; i <= 500; i++)
        {
            largeRxList.Add(new ActivePrescriptionDto
            {
                Med_Order_Id = Guid.NewGuid(),
                Patient_Mrn = (i % 1000).ToString("D8"),
                Case_No = $"CAS-{i:D7}",
                Patient_Name = $"Inpatient #{i}",
                Care_Station = $"Station {i % 10 + 1}",
                Room_Bed = $"Bed {i % 50 + 1}",
                Attending_Physician = $"Dr. Physician {i % 20 + 1}",
                Medication_Code = $"DRUG-{(i % 200 + 1):D4}",
                Medication_Name = $"Medication Formulation #{i % 200 + 1}",
                Dosage_Ordered_Value = (i % 50 + 1) * 2.5m,
                Dosage_Ordered_Unit = "mg",
                Frequency = "TID",
                Route = "PO",
                Clinical_Indication = "Inpatient Maintenance Therapy",
                Start_Datetime = DateTime.Today.AddDays(-1),
                End_Datetime = DateTime.Today.AddDays(7),
                Total_Sched_Doses = 21,
                Order_Status = "01",
                isHighAlert = (i % 10 == 0)
            });
        }

        long memBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();

        byte[] bytes = _excelService.GenerateMultiSheetExcel(largeMarList, largeRxList);

        sw.Stop();
        long memAfter = GC.GetTotalMemory(false);
        long memDeltaMb = (memAfter - memBefore) / (1024 * 1024);

        _output.WriteLine($"Generated 5,000 MAR items + 500 active prescriptions: {bytes.Length:N0} bytes in {sw.ElapsedMilliseconds}ms (Mem delta: {memDeltaMb}MB)");

        // Assert performance SLA: 5,000 items in under 6,000ms
        sw.ElapsedMilliseconds.Should().BeLessThan(6000, "High-volume export of 5,000 rows must complete in under 6 seconds");
        bytes.Length.Should().BeGreaterThan(100_000, "5,000 row spreadsheet must be > 100KB");

        // Verify row counts in the generated spreadsheet
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var sheet1 = wb.Worksheet("MAR Schedule & Dispense Log");
        (sheet1.LastRowUsed()!.RowNumber() - 4).Should().Be(5000);

        var sheet2 = wb.Worksheet("Active Prescriptions Overview");
        (sheet2.LastRowUsed()!.RowNumber() - 4).Should().Be(500);
    }

    [Fact]
    [Trait("Category", "ChallengerM3")]
    public void Emp14_ConcurrentGeneration_ThreadSafety()
    {
        // Execute 8 concurrent workbook generations across parallel threads
        var results = new ConcurrentBag<(int Length, bool Valid)>();
        var marItems = new List<MarScheduleItemDto>
        {
            new()
            {
                Patient_Mrn = "00075655",
                Patient_Name = "Chen, Wei",
                Item_Code = "C001",
                Display_Name = "Aspirin 100mg",
                Dosage_Ordered_Value = 100m,
                Dosage_Ordered_Unit = "mg",
                Frequency = "QD",
                Route = "PO",
                Schedule_Date = DateTime.Today,
                Dispense_Status = "Dispensed"
            }
        };
        var rxItems = new List<ActivePrescriptionDto>
        {
            new()
            {
                Patient_Mrn = "00075655",
                Case_No = "00075655-01",
                Patient_Name = "Chen, Wei",
                Medication_Code = "C001",
                Medication_Name = "Aspirin 100mg",
                Dosage_Ordered_Value = 100m,
                Dosage_Ordered_Unit = "mg",
                Frequency = "QD",
                Route = "PO",
                Total_Sched_Doses = 7,
                Order_Status = "01"
            }
        };

        Parallel.For(0, 8, _ =>
        {
            byte[] bytes = _excelService.GenerateMultiSheetExcel(marItems, rxItems);
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            bool isValid = wb.Worksheets.Count == 2 && wb.Worksheet(1).Cell(5, 1).GetString() == "00075655";
            results.Add((bytes.Length, isValid));
        });

        results.Count.Should().Be(8);
        results.All(r => r.Valid).Should().BeTrue("All concurrent workbook generations must produce valid spreadsheets");
        // All byte sizes should match
        int firstLen = results.First().Length;
        results.All(r => r.Length == firstLen).Should().BeTrue("All identical concurrent exports must produce identical byte counts");
    }

    #endregion
}
