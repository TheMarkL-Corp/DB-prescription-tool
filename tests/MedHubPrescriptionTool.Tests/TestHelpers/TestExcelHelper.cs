using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using FluentAssertions;

namespace MedHubPrescriptionTool.Tests.TestHelpers
{
    public static class TestExcelHelper
    {
        public static readonly XLColor BannerDarkTeal = XLColor.FromHtml("#0E7490");
        public static readonly XLColor SubBannerTeal = XLColor.FromHtml("#155E75");
        public static readonly XLColor HeaderMedicalTeal = XLColor.FromHtml("#0891B2");
        public static readonly XLColor ZebraLightTeal = XLColor.FromHtml("#F0FDFA");
        public static readonly XLColor StatusDispensedBg = XLColor.FromHtml("#D1FAE5");
        public static readonly XLColor StatusDispensedText = XLColor.FromHtml("#065F46");
        public static readonly XLColor StatusPendingBg = XLColor.FromHtml("#FEF3C7");
        public static readonly XLColor StatusPendingText = XLColor.FromHtml("#92400E");
        public static readonly XLColor StatusCancelledBg = XLColor.FromHtml("#FEE2E2");
        public static readonly XLColor StatusCancelledText = XLColor.FromHtml("#991B1B");
        public static readonly XLColor HighAlertRed = XLColor.FromHtml("#DC2626");

        public static byte[] GenerateClinicalWorkbook(
            IEnumerable<MarScheduleItemDto> marItems,
            IEnumerable<ActivePrescriptionDto> activePrescriptions,
            string hospitalTitle = "MedHub Inpatient Clinical Administration")
        {
            using var workbook = new XLWorkbook();
            var marList = marItems.ToList();
            var rxList = activePrescriptions.ToList();

            // ==========================================
            // SHEET 1: Medication Administration Log
            // ==========================================
            var sheet1 = workbook.Worksheets.Add("Medication Administration Log");

            // Row 1: Banner Header
            var banner1 = sheet1.Range("A1:Q1");
            banner1.Merge();
            banner1.Value = $"MEDHUB CLINICAL INPATIENT MEDICATION ADMINISTRATION & DISPENSE LOG — {hospitalTitle.ToUpperInvariant()}";
            banner1.Style.Font.Bold = true;
            banner1.Style.Font.FontSize = 14;
            banner1.Style.Font.FontName = "Segoe UI";
            banner1.Style.Font.FontColor = XLColor.White;
            banner1.Style.Fill.BackgroundColor = BannerDarkTeal;
            banner1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            banner1.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet1.Row(1).Height = 32;

            // Row 2: Sub-banner
            var sub1 = sheet1.Range("A2:Q2");
            sub1.Merge();
            sub1.Value = $"Export Generated: {DateTime.Now:yyyy-MM-dd HH:mm} | System: MedHub Workstation amisdbV2 | Total Records: {marList.Count}";
            sub1.Style.Font.Italic = true;
            sub1.Style.Font.FontSize = 9;
            sub1.Style.Font.FontName = "Segoe UI";
            sub1.Style.Font.FontColor = XLColor.White;
            sub1.Style.Fill.BackgroundColor = SubBannerTeal;
            sub1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet1.Row(2).Height = 18;

            // Row 3: Spacer
            sheet1.Row(3).Height = 8;

            // Row 4: Column Headers
            string[] sheet1Headers = new[]
            {
                "Patient MRN", "Patient Name", "Care Station", "Room / Bed",
                "Medication Code", "Display Name", "Generic Name", "Ordered Dose",
                "Dose Unit", "Frequency", "Route", "Schedule DateTime",
                "Dispense Status", "Dispensed Qty", "Status Time", "Administering Nurse",
                "High Alert?"
            };

            for (int i = 0; i < sheet1Headers.Length; i++)
            {
                var cell = sheet1.Cell(4, i + 1);
                cell.Value = sheet1Headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontName = "Segoe UI";
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderMedicalTeal;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText = true;
            }
            sheet1.Row(4).Height = 26;

            // Row 5+: Data Rows
            int rowIdx = 5;
            if (marList.Count == 0)
            {
                var emptyRange = sheet1.Range(rowIdx, 1, rowIdx, 17);
                emptyRange.Merge();
                emptyRange.Value = "No matching medication administration records found.";
                emptyRange.Style.Font.Italic = true;
                emptyRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                rowIdx++;
            }
            else
            {
                foreach (var item in marList)
                {
                    sheet1.Row(rowIdx).Height = 22;

                    // Col A: Patient MRN (Text to preserve leading zeros)
                    var mrnCell = sheet1.Cell(rowIdx, 1);
                    mrnCell.SetValue(item.PatientMrn);
                    mrnCell.Style.NumberFormat.Format = "@";
                    mrnCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col B: Patient Name
                    sheet1.Cell(rowIdx, 2).SetValue(item.PatientName);

                    // Col C: Care Station
                    sheet1.Cell(rowIdx, 3).SetValue(item.CareStation ?? "-");

                    // Col D: Room / Bed
                    sheet1.Cell(rowIdx, 4).SetValue(item.RoomBed ?? "-");

                    // Col E: Medication Code (Text)
                    var codeCell = sheet1.Cell(rowIdx, 5);
                    codeCell.SetValue(item.MedicationCode);
                    codeCell.Style.NumberFormat.Format = "@";
                    codeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col F: Display Name
                    sheet1.Cell(rowIdx, 6).SetValue(item.DisplayName);

                    // Col G: Generic Name
                    sheet1.Cell(rowIdx, 7).SetValue(item.GenericName ?? "-");

                    // Col H: Ordered Dose (Number formatted)
                    var doseCell = sheet1.Cell(rowIdx, 8);
                    doseCell.SetValue((double)item.DosageOrderedValue);
                    doseCell.Style.NumberFormat.Format = "#,##0.00";
                    doseCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Col I: Dose Unit
                    var unitCell = sheet1.Cell(rowIdx, 9);
                    unitCell.SetValue(item.DosageOrderedUnit);
                    unitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col J: Frequency
                    var freqCell = sheet1.Cell(rowIdx, 10);
                    freqCell.SetValue(item.Frequency);
                    freqCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col K: Route
                    var routeCell = sheet1.Cell(rowIdx, 11);
                    routeCell.SetValue(item.Route);
                    routeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col L: Schedule DateTime
                    var schedCell = sheet1.Cell(rowIdx, 12);
                    schedCell.SetValue(item.ScheduleDateTime);
                    schedCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    schedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col M: Dispense Status (with highlight)
                    var statusCell = sheet1.Cell(rowIdx, 13);
                    statusCell.SetValue(item.DispenseStatus);
                    statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (item.DispenseStatus.Equals("Dispensed", StringComparison.OrdinalIgnoreCase))
                    {
                        statusCell.Style.Fill.BackgroundColor = StatusDispensedBg;
                        statusCell.Style.Font.FontColor = StatusDispensedText;
                        statusCell.Style.Font.Bold = true;
                    }
                    else if (item.DispenseStatus.StartsWith("Pending", StringComparison.OrdinalIgnoreCase))
                    {
                        statusCell.Style.Fill.BackgroundColor = StatusPendingBg;
                        statusCell.Style.Font.FontColor = StatusPendingText;
                    }
                    else
                    {
                        statusCell.Style.Fill.BackgroundColor = StatusCancelledBg;
                        statusCell.Style.Font.FontColor = StatusCancelledText;
                    }

                    // Col N: Dispensed Qty
                    var qtyCell = sheet1.Cell(rowIdx, 14);
                    if (item.DispensedQty.HasValue)
                    {
                        qtyCell.SetValue((double)item.DispensedQty.Value);
                        qtyCell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else
                    {
                        qtyCell.SetValue("-");
                    }
                    qtyCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Col O: Status Time
                    var stTimeCell = sheet1.Cell(rowIdx, 15);
                    if (item.StatusTime.HasValue)
                    {
                        stTimeCell.SetValue(item.StatusTime.Value);
                        stTimeCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    }
                    else
                    {
                        stTimeCell.SetValue("-");
                    }
                    stTimeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col P: Administering Nurse
                    sheet1.Cell(rowIdx, 16).SetValue(item.AdministeringNurse ?? "-");

                    // Col Q: High Alert?
                    var haCell = sheet1.Cell(rowIdx, 17);
                    haCell.SetValue(item.IsHighAlert ? "YES" : "NO");
                    haCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (item.IsHighAlert)
                    {
                        haCell.Style.Fill.BackgroundColor = StatusCancelledBg;
                        haCell.Style.Font.FontColor = HighAlertRed;
                        haCell.Style.Font.Bold = true;
                    }

                    // Zebra striping for even rows
                    if (rowIdx % 2 == 0)
                    {
                        sheet1.Range(rowIdx, 1, rowIdx, 17).Style.Fill.BackgroundColor = ZebraLightTeal;
                    }

                    rowIdx++;
                }
            }

            sheet1.SheetView.FreezeRows(4);
            sheet1.Columns(1, 17).AdjustToContents(10, 45);

            // ==========================================
            // SHEET 2: Active Prescriptions Overview
            // ==========================================
            var sheet2 = workbook.Worksheets.Add("Active Prescriptions Overview");

            // Row 1: Banner Header
            var banner2 = sheet2.Range("A1:P1");
            banner2.Merge();
            banner2.Value = $"MEDHUB ACTIVE INPATIENT PRESCRIPTIONS & CLINICAL REGIMENS — {hospitalTitle.ToUpperInvariant()}";
            banner2.Style.Font.Bold = true;
            banner2.Style.Font.FontSize = 14;
            banner2.Style.Font.FontName = "Segoe UI";
            banner2.Style.Font.FontColor = XLColor.White;
            banner2.Style.Fill.BackgroundColor = BannerDarkTeal;
            banner2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            banner2.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet2.Row(1).Height = 32;

            // Row 2: Sub-banner
            var sub2 = sheet2.Range("A2:P2");
            sub2.Merge();
            sub2.Value = $"Active Orders Snapshot: {DateTime.Now:yyyy-MM-dd HH:mm} | Total Active Orders: {rxList.Count}";
            sub2.Style.Font.Italic = true;
            sub2.Style.Font.FontSize = 9;
            sub2.Style.Font.FontName = "Segoe UI";
            sub2.Style.Font.FontColor = XLColor.White;
            sub2.Style.Fill.BackgroundColor = SubBannerTeal;
            sub2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet2.Row(2).Height = 18;

            // Row 3: Spacer
            sheet2.Row(3).Height = 8;

            // Row 4: Column Headers
            string[] sheet2Headers = new[]
            {
                "Patient MRN", "Case No", "Patient Name", "Care Station",
                "Room / Bed", "Attending Physician", "Medication Code", "Medication Name",
                "Dosage Instruction", "Clinical Indication", "Start Datetime", "End Datetime",
                "Total Sched Doses", "Dispensed Doses", "Pending Doses", "High Alert Warning"
            };

            for (int i = 0; i < sheet2Headers.Length; i++)
            {
                var cell = sheet2.Cell(4, i + 1);
                cell.Value = sheet2Headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontName = "Segoe UI";
                cell.Style.Font.FontSize = 11;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderMedicalTeal;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText = true;
            }
            sheet2.Row(4).Height = 26;

            int rxRowIdx = 5;
            if (rxList.Count == 0)
            {
                var emptyRange2 = sheet2.Range(rxRowIdx, 1, rxRowIdx, 16);
                emptyRange2.Merge();
                emptyRange2.Value = "No active prescription records found.";
                emptyRange2.Style.Font.Italic = true;
                emptyRange2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            else
            {
                foreach (var rx in rxList)
                {
                    sheet2.Row(rxRowIdx).Height = 22;

                    // Col A: Patient MRN (Text)
                    var mrnCell = sheet2.Cell(rxRowIdx, 1);
                    mrnCell.SetValue(rx.PatientMrn);
                    mrnCell.Style.NumberFormat.Format = "@";
                    mrnCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col B: Case No (Text)
                    var caseCell = sheet2.Cell(rxRowIdx, 2);
                    caseCell.SetValue(rx.CaseNo);
                    caseCell.Style.NumberFormat.Format = "@";
                    caseCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col C: Patient Name
                    sheet2.Cell(rxRowIdx, 3).SetValue(rx.PatientName);

                    // Col D: Care Station
                    sheet2.Cell(rxRowIdx, 4).SetValue(rx.CareStation ?? "-");

                    // Col E: Room / Bed
                    sheet2.Cell(rxRowIdx, 5).SetValue(rx.RoomBed ?? "-");

                    // Col F: Attending Physician
                    sheet2.Cell(rxRowIdx, 6).SetValue(rx.AttendingPhysician ?? "-");

                    // Col G: Medication Code
                    var codeCell = sheet2.Cell(rxRowIdx, 7);
                    codeCell.SetValue(rx.MedicationCode);
                    codeCell.Style.NumberFormat.Format = "@";
                    codeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col H: Medication Name
                    sheet2.Cell(rxRowIdx, 8).SetValue(rx.MedicationName);

                    // Col I: Dosage Instruction
                    sheet2.Cell(rxRowIdx, 9).SetValue(rx.DosageInstruction);

                    // Col J: Clinical Indication
                    sheet2.Cell(rxRowIdx, 10).SetValue(rx.ClinicalIndication ?? "-");

                    // Col K: Start Datetime
                    var startCell = sheet2.Cell(rxRowIdx, 11);
                    if (rx.StartDatetime.HasValue)
                    {
                        startCell.SetValue(rx.StartDatetime.Value);
                        startCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    }
                    else
                    {
                        startCell.SetValue("-");
                    }
                    startCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col L: End Datetime
                    var endCell = sheet2.Cell(rxRowIdx, 12);
                    if (rx.EndDatetime.HasValue)
                    {
                        endCell.SetValue(rx.EndDatetime.Value);
                        endCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    }
                    else
                    {
                        endCell.SetValue("Ongoing");
                    }
                    endCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // Col M: Total Sched Doses (Number)
                    var totCell = sheet2.Cell(rxRowIdx, 13);
                    totCell.SetValue(rx.TotalScheduledDoses);
                    totCell.Style.NumberFormat.Format = "#,##0";
                    totCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Col N: Dispensed Doses (Number)
                    var dispCell = sheet2.Cell(rxRowIdx, 14);
                    dispCell.SetValue(rx.DispensedDoses);
                    dispCell.Style.NumberFormat.Format = "#,##0";
                    dispCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Col O: Pending Doses (Number)
                    var pendCell = sheet2.Cell(rxRowIdx, 15);
                    pendCell.SetValue(rx.PendingDoses);
                    pendCell.Style.NumberFormat.Format = "#,##0";
                    pendCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    // Col P: High Alert Warning
                    var haCell = sheet2.Cell(rxRowIdx, 16);
                    haCell.SetValue(rx.IsHighAlert ? "YES" : "NO");
                    haCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (rx.IsHighAlert)
                    {
                        haCell.Style.Fill.BackgroundColor = StatusCancelledBg;
                        haCell.Style.Font.FontColor = HighAlertRed;
                        haCell.Style.Font.Bold = true;
                    }

                    if (rxRowIdx % 2 == 0)
                    {
                        sheet2.Range(rxRowIdx, 1, rxRowIdx, 16).Style.Fill.BackgroundColor = ZebraLightTeal;
                    }

                    rxRowIdx++;
                }
            }

            sheet2.SheetView.FreezeRows(4);
            sheet2.Columns(1, 16).AdjustToContents(10, 45);

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        public static string GenerateTimestampedFilename()
        {
            return $"MedHub_Prescriptions_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        }
    }
}
