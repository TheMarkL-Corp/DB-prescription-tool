using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using MedHubPrescriptionTool.Models;

namespace MedHubPrescriptionTool.Services;

/// <summary>
/// High-performance ClosedXML multi-sheet clinical Excel export engine.
/// Produces audit-ready, beautifully styled .xlsx workbooks satisfying Requirement R4 and Feature Inventory F15-F17.
/// </summary>
public class ExcelExportService : IExcelExportService
{
    // Medical Clinical Theme Colors
    private static readonly XLColor BrandMedicalTeal = XLColor.FromHtml("#0891B2");
    private static readonly XLColor HeaderDarkTeal = XLColor.FromHtml("#0E7490");
    private static readonly XLColor SubBannerTeal = XLColor.FromHtml("#155E75");
    private static readonly XLColor ZebraLightGray = XLColor.FromHtml("#F8FAFC");
    private static readonly XLColor GridBorderColor = XLColor.FromHtml("#E2E8F0");

    // Status Badges Colors
    private static readonly XLColor StatusDispensedBg = XLColor.FromHtml("#D1FAE5");
    private static readonly XLColor StatusDispensedText = XLColor.FromHtml("#065F46");
    private static readonly XLColor StatusPendingBg = XLColor.FromHtml("#FEF3C7");
    private static readonly XLColor StatusPendingText = XLColor.FromHtml("#92400E");
    private static readonly XLColor StatusReturnedBg = XLColor.FromHtml("#F3E8FF");
    private static readonly XLColor StatusReturnedText = XLColor.FromHtml("#581C87");
    private static readonly XLColor StatusWastedBg = XLColor.FromHtml("#FFEDD5");
    private static readonly XLColor StatusWastedText = XLColor.FromHtml("#7C2D12");
    private static readonly XLColor StatusCancelledBg = XLColor.FromHtml("#FEE2E2");
    private static readonly XLColor StatusCancelledText = XLColor.FromHtml("#991B1B");
    private static readonly XLColor HighAlertRed = XLColor.FromHtml("#DC2626");

    /// <summary>
    /// Generates a styled, two-sheet Excel workbook in memory as an OpenXML byte array.
    /// Sheet 1: "MAR Schedule & Dispense Log" (17 columns)
    /// Sheet 2: "Active Prescriptions Overview" (16 columns)
    /// </summary>
    public byte[] GenerateMultiSheetExcel(
        IReadOnlyList<MarScheduleItemDto> marItems,
        IReadOnlyList<ActivePrescriptionDto> activePrescriptions,
        string hospitalTitle = "MedHub Inpatient Clinical Administration",
        string sheet1Name = "MAR Schedule & Dispense Log",
        string sheet2Name = "Active Prescriptions Overview")
    {
        ArgumentNullException.ThrowIfNull(marItems);
        ArgumentNullException.ThrowIfNull(activePrescriptions);

        using var workbook = new XLWorkbook();

        // -------------------------------------------------------------
        // SHEET 1: MAR Schedule & Dispense Log (17 Columns)
        // -------------------------------------------------------------
        BuildSheet1(workbook, marItems, hospitalTitle, sheet1Name);

        // -------------------------------------------------------------
        // SHEET 2: Active Prescriptions Overview (16 Columns)
        // -------------------------------------------------------------
        BuildSheet2(workbook, activePrescriptions, hospitalTitle, sheet2Name);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Generates a standardized clinical timestamped filename: MedHub_Prescriptions_YYYYMMDD_HHmm.xlsx.
    /// </summary>
    public string GenerateTimestampedFilename(string prefix = "MedHub_Prescriptions")
    {
        return $"{prefix}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
    }

    /// <summary>
    /// Generates the workbook and writes it directly to disk under the specified directory.
    /// </summary>
    public async Task<string> ExportToFileAsync(
        string outputDirectory,
        IReadOnlyList<MarScheduleItemDto> marItems,
        IReadOnlyList<ActivePrescriptionDto> activePrescriptions,
        string? customFilename = null,
        string hospitalTitle = "MedHub Inpatient Clinical Administration")
    {
        ArgumentNullException.ThrowIfNull(marItems);
        ArgumentNullException.ThrowIfNull(activePrescriptions);

        string targetDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? AppContext.BaseDirectory
            : outputDirectory;

        Directory.CreateDirectory(targetDir);

        string filename = string.IsNullOrWhiteSpace(customFilename)
            ? GenerateTimestampedFilename()
            : customFilename;

        if (!filename.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            filename += ".xlsx";
        }

        string fullPath = Path.Combine(targetDir, filename);
        byte[] bytes = GenerateMultiSheetExcel(marItems, activePrescriptions, hospitalTitle);
        await File.WriteAllBytesAsync(fullPath, bytes);

        return fullPath;
    }

    private static void BuildSheet1(
        XLWorkbook workbook,
        IReadOnlyList<MarScheduleItemDto> items,
        string hospitalTitle,
        string sheetName)
    {
        var sheet = workbook.Worksheets.Add(sheetName);

        // 1. Top Banner (Row 1)
        var bannerRange = sheet.Range("A1:Q1");
        bannerRange.Merge();
        bannerRange.Value = $"MEDHUB CLINICAL INPATIENT MEDICATION ADMINISTRATION & DISPENSE LOG — {hospitalTitle.ToUpperInvariant()}";
        bannerRange.Style.Font.Bold = true;
        bannerRange.Style.Font.FontSize = 14;
        bannerRange.Style.Font.FontName = "Segoe UI";
        bannerRange.Style.Font.FontColor = XLColor.White;
        bannerRange.Style.Fill.BackgroundColor = BrandMedicalTeal;
        bannerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        bannerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 34;

        // 2. Sub-banner with Export Timestamp and Metadata (Row 2)
        var subRange = sheet.Range("A2:Q2");
        subRange.Merge();
        subRange.Value = $"Export Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | System: MedHub Workstation amisdbV2 | Total Scheduled Administrations: {items.Count}";
        subRange.Style.Font.Italic = true;
        subRange.Style.Font.FontSize = 9;
        subRange.Style.Font.FontName = "Segoe UI";
        subRange.Style.Font.FontColor = XLColor.White;
        subRange.Style.Fill.BackgroundColor = SubBannerTeal;
        subRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        subRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(2).Height = 18;

        // 3. Spacer (Row 3)
        sheet.Row(3).Height = 8;

        // 4. Column Header Row (Row 4 - 17 Columns)
        string[] headers =
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

        for (int i = 0; i < headers.Length; i++)
        {
            var headerCell = sheet.Cell(4, i + 1);
            headerCell.Value = headers[i];
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Font.FontName = "Segoe UI";
            headerCell.Style.Font.FontSize = 11;
            headerCell.Style.Font.FontColor = XLColor.White;
            headerCell.Style.Fill.BackgroundColor = HeaderDarkTeal;
            headerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerCell.Style.Alignment.WrapText = true;
        }
        sheet.Row(4).Height = 26;

        // 5. Data Rows (Row 5+)
        int rowIdx = 5;
        if (items.Count == 0)
        {
            var emptyRange = sheet.Range(rowIdx, 1, rowIdx, 17);
            emptyRange.Merge();
            emptyRange.Value = "No medication administration records found matching current criteria.";
            emptyRange.Style.Font.Italic = true;
            emptyRange.Style.Font.FontName = "Segoe UI";
            emptyRange.Style.Font.FontColor = XLColor.FromHtml("#64748B");
            emptyRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            emptyRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Row(rowIdx).Height = 24;
            rowIdx++;
        }
        else
        {
            foreach (var item in items)
            {
                sheet.Row(rowIdx).Height = 22;

                // Col 1: Patient MRN (Text format '@' strictly preserving leading zeros)
                var mrnCell = sheet.Cell(rowIdx, 1);
                mrnCell.SetValue(item.Patient_Mrn);
                mrnCell.Style.NumberFormat.Format = "@";
                mrnCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 2: Patient Name
                var nameCell = sheet.Cell(rowIdx, 2);
                nameCell.SetValue(item.Patient_Name);
                nameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 3: Care Station
                var stationCell = sheet.Cell(rowIdx, 3);
                stationCell.SetValue(string.IsNullOrWhiteSpace(item.Station_Name) ? (string.IsNullOrWhiteSpace(item.Station_Code) ? "-" : item.Station_Code) : item.Station_Name);
                stationCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 4: Bed
                var bedCell = sheet.Cell(rowIdx, 4);
                bedCell.SetValue(string.IsNullOrWhiteSpace(item.Bed_Name) ? (string.IsNullOrWhiteSpace(item.Bed_Code) ? "-" : item.Bed_Code) : item.Bed_Name);
                bedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 5: Medication Code (Text format '@')
                var codeCell = sheet.Cell(rowIdx, 5);
                codeCell.SetValue(item.Item_Code);
                codeCell.Style.NumberFormat.Format = "@";
                codeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 6: Display Name
                var dispNameCell = sheet.Cell(rowIdx, 6);
                dispNameCell.SetValue(item.Display_Name);
                dispNameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                if (item.isHighAlert)
                {
                    dispNameCell.Style.Font.Bold = true;
                    dispNameCell.Style.Font.FontColor = HighAlertRed;
                }

                // Col 7: Generic Name
                var genNameCell = sheet.Cell(rowIdx, 7);
                genNameCell.SetValue(item.Generic_Name ?? "-");
                genNameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 8: Dose Ordered (Numeric with #,##0.00)
                var doseCell = sheet.Cell(rowIdx, 8);
                doseCell.SetValue((double)item.Dosage_Ordered_Value);
                doseCell.Style.NumberFormat.Format = "#,##0.00";
                doseCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                // Col 9: Unit
                var unitCell = sheet.Cell(rowIdx, 9);
                unitCell.SetValue(item.Dosage_Ordered_Unit);
                unitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 10: Frequency
                var freqCell = sheet.Cell(rowIdx, 10);
                freqCell.SetValue(item.Frequency);
                freqCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 11: Route
                var routeCell = sheet.Cell(rowIdx, 11);
                routeCell.SetValue(item.Route);
                routeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 12: Scheduled Date & Time
                var schedCell = sheet.Cell(rowIdx, 12);
                schedCell.SetValue(item.ScheduledDateTime);
                schedCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                schedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 13: Dispense Status (Visual Badges)
                var statusCell = sheet.Cell(rowIdx, 13);
                statusCell.SetValue(item.Dispense_Status);
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ApplyStatusColoring(statusCell, item.Dispense_Status);

                // Col 14: Dispensed Qty
                var qtyCell = sheet.Cell(rowIdx, 14);
                if (item.Dispensed_Qty.HasValue)
                {
                    qtyCell.SetValue((double)item.Dispensed_Qty.Value);
                    qtyCell.Style.NumberFormat.Format = "#,##0.00";
                    qtyCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                else
                {
                    qtyCell.SetValue("-");
                    qtyCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // Col 15: Status Time
                var stTimeCell = sheet.Cell(rowIdx, 15);
                if (item.Status_Time.HasValue)
                {
                    stTimeCell.SetValue(item.Status_Time.Value);
                    stTimeCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    stTimeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    stTimeCell.SetValue("-");
                    stTimeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // Col 16: Dispensing Nurse
                var nurseCell = sheet.Cell(rowIdx, 16);
                nurseCell.SetValue(item.Dispense_Nurse_Name ?? item.Confirmed_Nurse_Name ?? "-");
                nurseCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 17: Witnessing Nurse / Notes
                var noteCell = sheet.Cell(rowIdx, 17);
                string noteText = FormatMarNotes(item);
                noteCell.SetValue(noteText);
                noteCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                if (noteText.Contains("Witness", StringComparison.OrdinalIgnoreCase) ||
                    noteText.Contains("Return", StringComparison.OrdinalIgnoreCase) ||
                    noteText.Contains("Waste", StringComparison.OrdinalIgnoreCase) ||
                    noteText.Contains("High-Alert", StringComparison.OrdinalIgnoreCase))
                {
                    noteCell.Style.Font.Bold = true;
                }

                // Zebra striping for even data rows
                if (rowIdx % 2 == 0)
                {
                    sheet.Range(rowIdx, 1, rowIdx, 17).Style.Fill.BackgroundColor = ZebraLightGray;
                }

                // Subtle cell borders
                sheet.Range(rowIdx, 1, rowIdx, 17).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(rowIdx, 1, rowIdx, 17).Style.Border.InsideBorderColor = GridBorderColor;
                sheet.Range(rowIdx, 1, rowIdx, 17).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                sheet.Range(rowIdx, 1, rowIdx, 17).Style.Border.BottomBorderColor = GridBorderColor;

                rowIdx++;
            }
        }

        // Freeze headers and auto-fit columns
        sheet.SheetView.FreezeRows(4);
        sheet.Columns(1, 17).AdjustToContents(10, 50);
    }

    private static void BuildSheet2(
        XLWorkbook workbook,
        IReadOnlyList<ActivePrescriptionDto> activePrescriptions,
        string hospitalTitle,
        string sheetName)
    {
        var sheet = workbook.Worksheets.Add(sheetName);

        // 1. Top Banner (Row 1)
        var bannerRange = sheet.Range("A1:P1");
        bannerRange.Merge();
        bannerRange.Value = $"MEDHUB ACTIVE INPATIENT PRESCRIPTIONS & CLINICAL REGIMENS — {hospitalTitle.ToUpperInvariant()}";
        bannerRange.Style.Font.Bold = true;
        bannerRange.Style.Font.FontSize = 14;
        bannerRange.Style.Font.FontName = "Segoe UI";
        bannerRange.Style.Font.FontColor = XLColor.White;
        bannerRange.Style.Fill.BackgroundColor = BrandMedicalTeal;
        bannerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        bannerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 34;

        // 2. Sub-banner with Export Timestamp and Metadata (Row 2)
        var subRange = sheet.Range("A2:P2");
        subRange.Merge();
        subRange.Value = $"Active Orders Snapshot: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Status: OrderStat = 01 (Active) | Total Active Orders: {activePrescriptions.Count}";
        subRange.Style.Font.Italic = true;
        subRange.Style.Font.FontSize = 9;
        subRange.Style.Font.FontName = "Segoe UI";
        subRange.Style.Font.FontColor = XLColor.White;
        subRange.Style.Fill.BackgroundColor = SubBannerTeal;
        subRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        subRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(2).Height = 18;

        // 3. Spacer (Row 3)
        sheet.Row(3).Height = 8;

        // 4. Column Header Row (Row 4 - 16 Columns)
        string[] headers =
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

        for (int i = 0; i < headers.Length; i++)
        {
            var headerCell = sheet.Cell(4, i + 1);
            headerCell.Value = headers[i];
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Font.FontName = "Segoe UI";
            headerCell.Style.Font.FontSize = 11;
            headerCell.Style.Font.FontColor = XLColor.White;
            headerCell.Style.Fill.BackgroundColor = HeaderDarkTeal;
            headerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerCell.Style.Alignment.WrapText = true;
        }
        sheet.Row(4).Height = 26;

        // 5. Data Rows (Row 5+)
        int rowIdx = 5;
        if (activePrescriptions.Count == 0)
        {
            var emptyRange = sheet.Range(rowIdx, 1, rowIdx, 16);
            emptyRange.Merge();
            emptyRange.Value = "No active prescription records found matching current criteria.";
            emptyRange.Style.Font.Italic = true;
            emptyRange.Style.Font.FontName = "Segoe UI";
            emptyRange.Style.Font.FontColor = XLColor.FromHtml("#64748B");
            emptyRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            emptyRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Row(rowIdx).Height = 24;
            rowIdx++;
        }
        else
        {
            foreach (var rx in activePrescriptions)
            {
                sheet.Row(rowIdx).Height = 22;

                // Col 1: Patient MRN (Text format '@' preserving leading zeros)
                var mrnCell = sheet.Cell(rowIdx, 1);
                mrnCell.SetValue(rx.Patient_Mrn);
                mrnCell.Style.NumberFormat.Format = "@";
                mrnCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 2: Patient Name
                var nameCell = sheet.Cell(rowIdx, 2);
                nameCell.SetValue(rx.Patient_Name);
                nameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 3: Case Number (Text format '@' preserving leading zeros)
                var caseCell = sheet.Cell(rowIdx, 3);
                string caseVal = rx.Case_No ?? string.Empty;
                if (caseVal.StartsWith('\''))
                {
                    caseVal = "'" + caseVal;
                }
                caseCell.SetValue(caseVal);
                caseCell.Style.NumberFormat.Format = "@";
                caseCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 4: Care Station
                var stationCell = sheet.Cell(rowIdx, 4);
                stationCell.SetValue(string.IsNullOrWhiteSpace(rx.Care_Station) ? (string.IsNullOrWhiteSpace(rx.Station_Code) ? "-" : rx.Station_Code) : rx.Care_Station);
                stationCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 5: Bed
                var bedCell = sheet.Cell(rowIdx, 5);
                bedCell.SetValue(string.IsNullOrWhiteSpace(rx.Room_Bed) ? (string.IsNullOrWhiteSpace(rx.Bed_Code) ? "-" : rx.Bed_Code) : rx.Room_Bed);
                bedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 6: Attending Physician
                var doctorCell = sheet.Cell(rowIdx, 6);
                doctorCell.SetValue(string.IsNullOrWhiteSpace(rx.Attending_Physician) ? "-" : rx.Attending_Physician);
                doctorCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 7: Medication Code (Text format '@')
                var codeCell = sheet.Cell(rowIdx, 7);
                codeCell.SetValue(rx.Medication_Code);
                codeCell.Style.NumberFormat.Format = "@";
                codeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 8: Display Name
                var medNameCell = sheet.Cell(rowIdx, 8);
                medNameCell.SetValue(rx.Medication_Name);
                medNameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                if (rx.isHighAlert)
                {
                    medNameCell.Style.Font.Bold = true;
                    medNameCell.Style.Font.FontColor = HighAlertRed;
                }

                // Col 9: Dosage Ordered & Unit
                var doseUnitCell = sheet.Cell(rowIdx, 9);
                doseUnitCell.SetValue($"{rx.Dosage_Ordered_Value:0.##} {rx.Dosage_Ordered_Unit}".Trim());
                doseUnitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 10: Frequency
                var freqCell = sheet.Cell(rowIdx, 10);
                freqCell.SetValue(rx.Frequency);
                freqCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 11: Route
                var routeCell = sheet.Cell(rowIdx, 11);
                routeCell.SetValue(rx.Route);
                routeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Col 12: Clinical Indication
                var indCell = sheet.Cell(rowIdx, 12);
                indCell.SetValue(rx.Clinical_Indication ?? "-");
                indCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                // Col 13: Order Start Time
                var startCell = sheet.Cell(rowIdx, 13);
                if (rx.Start_Datetime.HasValue)
                {
                    startCell.SetValue(rx.Start_Datetime.Value);
                    startCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    startCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    startCell.SetValue("-");
                    startCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // Col 14: Order End Time
                var endCell = sheet.Cell(rowIdx, 14);
                if (rx.End_Datetime.HasValue)
                {
                    endCell.SetValue(rx.End_Datetime.Value);
                    endCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    endCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    endCell.SetValue("Ongoing");
                    endCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // Col 15: Total Scheduled Doses
                var totDosesCell = sheet.Cell(rowIdx, 15);
                totDosesCell.SetValue(rx.Total_Sched_Doses);
                totDosesCell.Style.NumberFormat.Format = "#,##0";
                totDosesCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                // Col 16: Order Status
                var statusCell = sheet.Cell(rowIdx, 16);
                string displayStatus = string.Equals(rx.Order_Status, "01", StringComparison.OrdinalIgnoreCase)
                    ? "Active"
                    : (string.IsNullOrWhiteSpace(rx.Order_Status) ? "Active" : rx.Order_Status);
                statusCell.SetValue(displayStatus);
                statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                if (string.Equals(displayStatus, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    statusCell.Style.Fill.BackgroundColor = StatusDispensedBg;
                    statusCell.Style.Font.FontColor = StatusDispensedText;
                    statusCell.Style.Font.Bold = true;
                }

                // Zebra striping for even data rows
                if (rowIdx % 2 == 0)
                {
                    sheet.Range(rowIdx, 1, rowIdx, 16).Style.Fill.BackgroundColor = ZebraLightGray;
                }

                // Subtle cell borders
                sheet.Range(rowIdx, 1, rowIdx, 16).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(rowIdx, 1, rowIdx, 16).Style.Border.InsideBorderColor = GridBorderColor;
                sheet.Range(rowIdx, 1, rowIdx, 16).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                sheet.Range(rowIdx, 1, rowIdx, 16).Style.Border.BottomBorderColor = GridBorderColor;

                rowIdx++;
            }
        }

        // Freeze headers and auto-fit columns
        sheet.SheetView.FreezeRows(4);
        sheet.Columns(1, 16).AdjustToContents(10, 50);
    }

    private static void ApplyStatusColoring(IXLCell cell, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        if (status.Equals("Dispensed", StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Fill.BackgroundColor = StatusDispensedBg;
            cell.Style.Font.FontColor = StatusDispensedText;
            cell.Style.Font.Bold = true;
        }
        else if (status.StartsWith("Pending", StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Fill.BackgroundColor = StatusPendingBg;
            cell.Style.Font.FontColor = StatusPendingText;
        }
        else if (status.Equals("Returned", StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Fill.BackgroundColor = StatusReturnedBg;
            cell.Style.Font.FontColor = StatusReturnedText;
            cell.Style.Font.Bold = true;
        }
        else if (status.Equals("Wasted", StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Fill.BackgroundColor = StatusWastedBg;
            cell.Style.Font.FontColor = StatusWastedText;
            cell.Style.Font.Bold = true;
        }
        else if (status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            cell.Style.Fill.BackgroundColor = StatusCancelledBg;
            cell.Style.Font.FontColor = StatusCancelledText;
        }
    }

    private static string FormatMarNotes(MarScheduleItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Return_Reason_Name))
        {
            return $"Returned: {item.Return_Reason_Name}";
        }

        if (!string.IsNullOrWhiteSpace(item.Waste_Reason_Name))
        {
            return $"Wasted: {item.Waste_Reason_Name}";
        }

        if (!string.IsNullOrWhiteSpace(item.Cancel_Reason_Name))
        {
            return $"Cancelled: {item.Cancel_Reason_Name}";
        }

        if (string.Equals(item.Wintness_Required, "Y", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(item.Confirmed_Nurse_Name) && item.Confirmed_Nurse_Name != item.Dispense_Nurse_Name
                ? $"Witness: {item.Confirmed_Nurse_Name} (Dual Verification)"
                : "Dual Nurse Verification Required";
        }

        if (!string.IsNullOrWhiteSpace(item.Instruction_Note))
        {
            return item.Instruction_Note;
        }

        if (item.isHighAlert)
        {
            return "High-Alert Safety Protocol";
        }

        return "-";
    }
}
