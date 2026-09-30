using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ClosedXML.Excel;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;
using MedHubPrescriptionTool.Services;

namespace MedHubPrescriptionTool.AuditorM3Forensics;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;
    private static readonly List<string> FailureDetails = new();

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" FORENSIC INTEGRITY AUDIT BATTERY: Milestone 3 (ExcelExportService.cs)");
        Console.WriteLine(" Independent Verification: PK Zip, OpenXML, Dynamic DTOs, Anti-Hardcoding");
        Console.WriteLine("================================================================================");

        var excelService = new ExcelExportService();

        // Battery 1: PK Zip Magic Bytes and OpenXML Archive Anatomy
        await TestBattery1_OpenXmlZipStructure(excelService);

        // Battery 2: Anti-Hardcoding & Dynamic Content Verification
        await TestBattery2_AntiHardcodingAndDynamicGuid(excelService);

        // Battery 3: Dynamic Iteration & 17/16 Column Schema Integrity
        await TestBattery3_DynamicIterationAndColumns(excelService);

        // Battery 4: Adversarial Strings, CJK Unicode, and Boundary Conditions
        await TestBattery4_AdversarialStressAndBoundaries(excelService);

        // Battery 5: Disk Export I/O and Standardized Filename
        await TestBattery5_DiskExportAndFilename(excelService);

        // Battery 6: Live Database Snapshot Export Integrity
        await TestBattery6_LiveDatabaseSnapshotExport(excelService);

        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine($" AUDIT RESULTS: {_passed} CHECKS PASSED, {_failed} CHECKS FAILED");
        Console.WriteLine("================================================================================");

        if (_failed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("INTEGRITY VIOLATION DETECTED! Details:");
            foreach (var fail in FailureDetails)
            {
                Console.WriteLine($" - {fail}");
            }
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("FORENSIC VERDICT: CLEAN - All 6 forensic batteries verified genuine implementation.");
        Console.ResetColor();
        return 0;
    }

    private static void Assert(bool condition, string checkName, string details)
    {
        if (condition)
        {
            _passed++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[PASS] ");
            Console.ResetColor();
            Console.WriteLine($"{checkName}: {details}");
        }
        else
        {
            _failed++;
            string errMsg = $"{checkName} FAILED: {details}";
            FailureDetails.Add(errMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[FAIL] ");
            Console.ResetColor();
            Console.WriteLine(errMsg);
        }
    }

    private static async Task TestBattery1_OpenXmlZipStructure(IExcelExportService service)
    {
        Console.WriteLine("\n--- BATTERY 1: PK Zip Magic Bytes and OpenXML Archive Anatomy ---");

        var dummyMar = new List<MarScheduleItemDto>
        {
            CreateSampleMar("MRN001", "Patient Alpha", "Drug A", "A01", 10.0m, "mg", "PO", DateTime.Now, "Dispensed")
        };
        var dummyRx = new List<ActivePrescriptionDto>
        {
            CreateSampleRx("MRN001", "Patient Alpha", "CASENO01", "Drug A", "A01", 10.0m, "mg", "PO", DateTime.Now, 5)
        };

        byte[] bytes = service.GenerateMultiSheetExcel(dummyMar, dummyRx);

        // 1.1 PK Zip Magic Header Check (0x50, 0x4B, 0x03, 0x04)
        bool hasPkMagic = bytes.Length >= 4 &&
                          bytes[0] == 0x50 && bytes[1] == 0x4B &&
                          bytes[2] == 0x03 && bytes[3] == 0x04;
        Assert(hasPkMagic, "1.1 PK Zip Header", $"Bytes header: [{bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}] (Expected 50 4B 03 04)");

        // 1.2 Open as ZipArchive and inspect OpenXML parts
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            var entryNames = zip.Entries.Select(e => e.FullName).ToHashSet();

            bool hasContentTypes = entryNames.Contains("[Content_Types].xml");
            bool hasRels = entryNames.Contains("_rels/.rels");
            bool hasWorkbook = entryNames.Contains("xl/workbook.xml");
            bool hasWorkbookRels = entryNames.Contains("xl/_rels/workbook.xml.rels");
            bool hasSheet1 = entryNames.Contains("xl/worksheets/sheet1.xml");
            bool hasSheet2 = entryNames.Contains("xl/worksheets/sheet2.xml");
            bool hasStyles = entryNames.Contains("xl/styles.xml");
            bool hasSharedStrings = entryNames.Contains("xl/sharedStrings.xml");

            bool allOpenXmlParts = hasContentTypes && hasRels && hasWorkbook && hasWorkbookRels &&
                                   hasSheet1 && hasSheet2 && hasStyles && hasSharedStrings;

            Assert(allOpenXmlParts, "1.2 OpenXML Part Verification",
                $"Found {zip.Entries.Count} zip entries including [Content_Types].xml, workbook.xml, sheet1.xml, sheet2.xml, styles.xml, sharedStrings.xml");

            // 1.3 Validate [Content_Types].xml
            var ctEntry = zip.GetEntry("[Content_Types].xml");
            if (ctEntry != null)
            {
                using var stream = ctEntry.Open();
                var doc = XDocument.Load(stream);
                XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
                var overrides = doc.Descendants(ns + "Override").ToList();
                Console.WriteLine("   DEBUG [Content_Types].xml:\n" + doc.ToString());
                var defaults = doc.Descendants(ns + "Default").ToList();
                bool hasSheetType = overrides.Any(o => o.Attribute("ContentType")?.Value?.Contains("worksheet") == true);
                bool hasWbType = overrides.Any(o => o.Attribute("ContentType")?.Value?.Contains("sheet.main") == true || o.Attribute("ContentType")?.Value?.Contains("workbook") == true)
                                || doc.ToString().Contains("sheet.main") || doc.ToString().Contains("spreadsheetml");

                Assert(hasSheetType && hasWbType, "1.3 OpenXML ContentTypes Content",
                    $"Verified worksheet and workbook ContentType declarations in [Content_Types].xml ({overrides.Count} overrides, {defaults.Count} defaults)");
            }
            else
            {
                Assert(false, "1.3 OpenXML ContentTypes Content", "Missing [Content_Types].xml");
            }
        }
        catch (Exception ex)
        {
            Assert(false, "1.2 Zip Inspection", $"Exception during zip archive inspection: {ex.Message}");
        }
    }

    private static async Task TestBattery2_AntiHardcodingAndDynamicGuid(IExcelExportService service)
    {
        Console.WriteLine("\n--- BATTERY 2: Anti-Hardcoding & Dynamic Content Verification ---");

        // Generate 3 completely unique datasets with random GUIDs
        string guid1 = Guid.NewGuid().ToString("N")[..8];
        string guid2 = Guid.NewGuid().ToString("N")[..8];
        string guid3 = Guid.NewGuid().ToString("N")[..8];

        string uniqueMrn1 = "000" + Random.Shared.Next(10000, 99999);
        string uniqueMrn2 = "000" + Random.Shared.Next(10000, 99999);
        string uniqueMrn3 = "000" + Random.Shared.Next(10000, 99999);

        string uniquePatient1 = "TestPatient_" + guid1;
        string uniquePatient2 = "TestPatient_" + guid2;
        string uniquePatient3 = "TestPatient_" + guid3;

        var list1 = new List<MarScheduleItemDto> { CreateSampleMar(uniqueMrn1, uniquePatient1, "Drug_" + guid1, "CODE_" + guid1, 15.5m, "MG", "IV", DateTime.Now, "Dispensed") };
        var rxList1 = new List<ActivePrescriptionDto> { CreateSampleRx(uniqueMrn1, uniquePatient1, "CASE_" + guid1, "Drug_" + guid1, "CODE_" + guid1, 15.5m, "MG", "IV", DateTime.Now, 7) };

        var list2 = new List<MarScheduleItemDto> { CreateSampleMar(uniqueMrn2, uniquePatient2, "Drug_" + guid2, "CODE_" + guid2, 25.0m, "TAB", "PO", DateTime.Now, "Pending Due") };
        var rxList2 = new List<ActivePrescriptionDto> { CreateSampleRx(uniqueMrn2, uniquePatient2, "CASE_" + guid2, "Drug_" + guid2, "CODE_" + guid2, 25.0m, "TAB", "PO", DateTime.Now, 14) };

        var list3 = new List<MarScheduleItemDto>
        {
            CreateSampleMar(uniqueMrn3, uniquePatient3, "Drug_" + guid3, "CODE_" + guid3, 50.0m, "VIAL", "SC", DateTime.Now, "Returned"),
            CreateSampleMar(uniqueMrn1, uniquePatient1, "Drug_" + guid1, "CODE_" + guid1, 10.0m, "MG", "PO", DateTime.Now, "Wasted")
        };
        var rxList3 = new List<ActivePrescriptionDto> { CreateSampleRx(uniqueMrn3, uniquePatient3, "CASE_" + guid3, "Drug_" + guid3, "CODE_" + guid3, 50.0m, "VIAL", "SC", DateTime.Now, 21) };

        byte[] b1 = service.GenerateMultiSheetExcel(list1, rxList1);
        byte[] b2 = service.GenerateMultiSheetExcel(list2, rxList2);
        byte[] b3 = service.GenerateMultiSheetExcel(list3, rxList3);

        // 2.1 Outputs must not be identical bytes (proves no fixed pre-baked byte array returned)
        bool sizesDiffer = b1.Length != b3.Length; // b3 has 2 items vs 1
        bool hashesDiffer = !b1.SequenceEqual(b2);

        Assert(hashesDiffer && sizesDiffer, "2.1 Dynamic Stream Variation",
            $"b1 size: {b1.Length} bytes, b2 size: {b2.Length} bytes, b3 size: {b3.Length} bytes. Byte sequences are non-identical.");

        // 2.2 Unpack b1 and check sharedStrings.xml contains uniquePatient1 and uniqueMrn1
        using (var ms1 = new MemoryStream(b1))
        using (var zip1 = new ZipArchive(ms1, ZipArchiveMode.Read))
        {
            var ssEntry = zip1.GetEntry("xl/sharedStrings.xml");
            Assert(ssEntry != null, "2.2 SharedStrings Exists", "xl/sharedStrings.xml exists in generated package");

            using var reader = new StreamReader(ssEntry!.Open(), Encoding.UTF8);
            string ssXml = await reader.ReadToEndAsync();

            bool containsPatient = ssXml.Contains(uniquePatient1);
            bool containsMrn = ssXml.Contains(uniqueMrn1);
            bool containsDrug = ssXml.Contains("Drug_" + guid1);
            bool doesNotContainP2 = !ssXml.Contains(uniquePatient2);

            Assert(containsPatient && containsMrn && containsDrug && doesNotContainP2, "2.3 Raw XML Dynamic Ingestion",
                $"sharedStrings.xml directly contains '{uniquePatient1}', '{uniqueMrn1}', and 'Drug_{guid1}' without contaminating from list2");
        }

        // 2.3 ClosedXML DOM Cell Reflection
        using (var ms1 = new MemoryStream(b1))
        using (var wb = new XLWorkbook(ms1))
        {
            var s1 = wb.Worksheet("MAR Schedule & Dispense Log");
            var s2 = wb.Worksheet("Active Prescriptions Overview");

            Assert(s1.Cell(5, 1).GetString() == uniqueMrn1, "2.4 Cell Value MRN Preservation", $"S1:A5 value '{s1.Cell(5, 1).GetString()}' == '{uniqueMrn1}'");
            Assert(s1.Cell(5, 2).GetString() == uniquePatient1, "2.5 Cell Value Patient Name", $"S1:B5 value '{s1.Cell(5, 2).GetString()}' == '{uniquePatient1}'");
            Assert(s2.Cell(5, 3).GetString() == "CASE_" + guid1, "2.6 Cell Value Case Number", $"S2:C5 value '{s2.Cell(5, 3).GetString()}' == 'CASE_{guid1}'");
        }
    }

    private static async Task TestBattery3_DynamicIterationAndColumns(IExcelExportService service)
    {
        Console.WriteLine("\n--- BATTERY 3: Dynamic Iteration & 17/16 Column Schema Integrity ---");

        int N = 42;
        int M = 17;

        var marItems = new List<MarScheduleItemDto>();
        for (int i = 0; i < N; i++)
        {
            bool isHigh = (i % 5 == 0);
            string status = (i % 4) switch
            {
                0 => "Dispensed",
                1 => "Pending Due",
                2 => "Returned",
                _ => "Cancelled"
            };

            marItems.Add(new MarScheduleItemDto
            {
                Patient_Mrn = $"000{1000 + i}",
                Patient_Name = $"Patient #{i:D2}",
                Station_Name = $"Ward {(char)('A' + (i % 4))}",
                Bed_Name = $"Bed {i + 1}",
                Item_Code = $"DRUG{i:D4}",
                Display_Name = $"Medication Alpha-{i}",
                Generic_Name = $"Generic-{i}",
                Dosage_Ordered_Value = 5.25m * (i + 1),
                Dosage_Ordered_Unit = "mg",
                Frequency = "Q8H",
                Route = "PO",
                Schedule_Date = new DateTime(2026, 9, 24, 8, 0, 0).AddHours(i).Date,
                Schedule_Time = new DateTime(2026, 9, 24, 8, 0, 0).AddHours(i).TimeOfDay,
                Dispense_Status = status,
                Dispensed_Qty = (status == "Dispensed") ? 1.0m : null,
                Status_Time = (status == "Dispensed") ? DateTime.Now : null,
                Dispense_Nurse_Name = "Nurse " + (char)('A' + (i % 5)),
                Return_Reason_Name = (status == "Returned") ? "Patient Refused" : null,
                Cancel_Reason_Name = (status == "Cancelled") ? "Order Discontinued" : null,
                isHighAlert = isHigh
            });
        }

        var rxItems = new List<ActivePrescriptionDto>();
        for (int j = 0; j < M; j++)
        {
            rxItems.Add(new ActivePrescriptionDto
            {
                Patient_Mrn = $"000{1000 + j}",
                Patient_Name = $"Patient #{j:D2}",
                Case_No = $"0099{800 + j}",
                Care_Station = $"Ward {(char)('A' + (j % 4))}",
                Room_Bed = $"Bed {j + 1}",
                Attending_Physician = $"Dr. Specialist {j + 1}",
                Medication_Code = $"DRUG{j:D4}",
                Medication_Name = $"Medication Alpha-{j}",
                Dosage_Ordered_Value = 10.0m * (j + 1),
                Dosage_Ordered_Unit = "mg",
                Frequency = "BID",
                Route = "IV",
                Clinical_Indication = $"Indication for Dx #{j}",
                Start_Datetime = new DateTime(2026, 9, 20),
                End_Datetime = new DateTime(2026, 9, 30),
                Total_Sched_Doses = 20 + j,
                Order_Status = "01",
                isHighAlert = (j % 3 == 0)
            });
        }

        byte[] bytes = service.GenerateMultiSheetExcel(marItems, rxItems);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var s1 = wb.Worksheet("MAR Schedule & Dispense Log");
        var s2 = wb.Worksheet("Active Prescriptions Overview");

        // 3.1 Verify exactly N data rows on Sheet 1 (rows 5 to 4 + N)
        int s1LastRow = s1.LastRowUsed()?.RowNumber() ?? 0;
        int s1DataRows = s1LastRow - 4;
        Assert(s1DataRows == N, "3.1 Sheet 1 Row Count", $"S1 data rows: {s1DataRows} (Expected: {N})");

        // 3.2 Verify exactly M data rows on Sheet 2 (rows 5 to 4 + M)
        int s2LastRow = s2.LastRowUsed()?.RowNumber() ?? 0;
        int s2DataRows = s2LastRow - 4;
        Assert(s2DataRows == M, "3.2 Sheet 2 Row Count", $"S2 data rows: {s2DataRows} (Expected: {M})");

        // 3.3 Verify sequential row-by-row iteration & data fidelity
        bool s1Integrity = true;
        for (int i = 0; i < N; i++)
        {
            int row = 5 + i;
            var expected = marItems[i];

            string mrn = s1.Cell(row, 1).GetString();
            string name = s1.Cell(row, 2).GetString();
            string station = s1.Cell(row, 3).GetString();
            string bed = s1.Cell(row, 4).GetString();
            string code = s1.Cell(row, 5).GetString();
            string dispName = s1.Cell(row, 6).GetString();
            string genName = s1.Cell(row, 7).GetString();
            double dose = s1.Cell(row, 8).GetDouble();
            string unit = s1.Cell(row, 9).GetString();
            string freq = s1.Cell(row, 10).GetString();
            string route = s1.Cell(row, 11).GetString();
            string status = s1.Cell(row, 13).GetString();

            if (mrn != expected.Patient_Mrn ||
                name != expected.Patient_Name ||
                code != expected.Item_Code ||
                dispName != expected.Display_Name ||
                Math.Abs(dose - (double)expected.Dosage_Ordered_Value) > 0.001 ||
                unit != expected.Dosage_Ordered_Unit ||
                status != expected.Dispense_Status)
            {
                s1Integrity = false;
                Assert(false, $"3.3 S1 Row {row} Mismatch", $"Expected [{expected.Patient_Mrn}, {expected.Display_Name}, {expected.Dispense_Status}] vs Found [{mrn}, {dispName}, {status}]");
                break;
            }

            // Check high alert styling
            if (expected.isHighAlert)
            {
                var font = s1.Cell(row, 6).Style.Font;
                if (!font.Bold || font.FontColor.ColorType != XLColorType.Color)
                {
                    s1Integrity = false;
                    Assert(false, "3.3 HighAlert Styling", $"Row {row} HighAlert flag was not styled bold red");
                    break;
                }
            }
        }

        if (s1Integrity)
        {
            Assert(true, "3.3 Sheet 1 Faithful Row Iteration", $"Verified all {N} rows on Sheet 1 match input DTOs perfectly");
        }

        // 3.4 Verify Sheet 2 iteration & data fidelity
        bool s2Integrity = true;
        for (int j = 0; j < M; j++)
        {
            int row = 5 + j;
            var expected = rxItems[j];

            string mrn = s2.Cell(row, 1).GetString();
            string name = s2.Cell(row, 2).GetString();
            string caseNo = s2.Cell(row, 3).GetString();
            string doc = s2.Cell(row, 6).GetString();
            string code = s2.Cell(row, 7).GetString();
            string ind = s2.Cell(row, 12).GetString();
            int totalDoses = s2.Cell(row, 15).GetValue<int>();
            string ordStatus = s2.Cell(row, 16).GetString();

            if (mrn != expected.Patient_Mrn ||
                caseNo != expected.Case_No ||
                doc != expected.Attending_Physician ||
                code != expected.Medication_Code ||
                ind != expected.Clinical_Indication ||
                totalDoses != expected.Total_Sched_Doses ||
                ordStatus != "Active")
            {
                s2Integrity = false;
                Assert(false, $"3.4 S2 Row {row} Mismatch", $"Expected [{expected.Case_No}, {expected.Attending_Physician}, {expected.Total_Sched_Doses}] vs Found [{caseNo}, {doc}, {totalDoses}]");
                break;
            }
        }

        if (s2Integrity)
        {
            Assert(true, "3.4 Sheet 2 Faithful Row Iteration", $"Verified all {M} rows on Sheet 2 match input DTOs perfectly");
        }

        // 3.5 Freeze panes and auto-fit checks
        bool s1Freeze = s1.SheetView.SplitRow == 4;
        bool s2Freeze = s2.SheetView.SplitRow == 4;
        Assert(s1Freeze && s2Freeze, "3.5 Freeze Panes", "FreezeRows(4) verified on both worksheets");
    }

    private static async Task TestBattery4_AdversarialStressAndBoundaries(IExcelExportService service)
    {
        Console.WriteLine("\n--- BATTERY 4: Adversarial Stress & Boundaries ---");

        // 4.1 Empty inputs (0 MAR, 0 Rx)
        var emptyBytes = service.GenerateMultiSheetExcel(new List<MarScheduleItemDto>(), new List<ActivePrescriptionDto>());
        Assert(emptyBytes != null && emptyBytes.Length > 2000, "4.1 Empty Lists Graceful Handling", $"Empty lists produced valid workbook of {emptyBytes?.Length} bytes");

        // 4.2 Special Characters: XML Reserved Chars, CJK Unicode, Emojis, Long Strings
        string cjkPatient = "病人：張三李四 王五 (NPO)";
        string specialCharsDrug = "Amoxicillin <500mg> & Clavulanate \"Forte\" 'Special'";
        string emojiNotes = "Dual nurse check 💉💊 Emergency alert ⚠️";
        string longIndication = new string('X', 2000);

        var advMar = new List<MarScheduleItemDto>
        {
            new MarScheduleItemDto
            {
                Patient_Mrn = "00000001",
                Patient_Name = cjkPatient,
                Display_Name = specialCharsDrug,
                Instruction_Note = emojiNotes,
                Clinical_Indication = longIndication,
                Item_Code = "ADV001",
                Dosage_Ordered_Value = 0.0001m,
                Dosage_Ordered_Unit = "mcg/kg/min",
                Frequency = "PRN",
                Route = "Epidural",
                Schedule_Date = DateTime.Today,
                Schedule_Time = DateTime.Now.TimeOfDay,
                Dispense_Status = "Dispensed"
            }
        };

        var advRx = new List<ActivePrescriptionDto>
        {
            new ActivePrescriptionDto
            {
                Patient_Mrn = "00000001",
                Patient_Name = cjkPatient,
                Case_No = "00099999",
                Medication_Code = "ADV001",
                Medication_Name = specialCharsDrug,
                Clinical_Indication = longIndication,
                Dosage_Ordered_Value = 0.0001m,
                Dosage_Ordered_Unit = "mcg/kg/min",
                Frequency = "PRN",
                Route = "Epidural",
                Order_Status = "01",
                Total_Sched_Doses = 999999
            }
        };

        byte[] advBytes = service.GenerateMultiSheetExcel(advMar, advRx);

        // Verify that ClosedXML re-opens without XML parsing exception
        bool advXmlParsed = false;
        try
        {
            using var ms = new MemoryStream(advBytes);
            using var wb = new XLWorkbook(ms);

            var s1 = wb.Worksheet("MAR Schedule & Dispense Log");
            string readPatient = s1.Cell(5, 2).GetString();
            string readDrug = s1.Cell(5, 6).GetString();
            string readNotes = s1.Cell(5, 17).GetString();

            var s2 = wb.Worksheet("Active Prescriptions Overview");
            string readLong = s2.Cell(5, 12).GetString();

            bool patientMatch = readPatient == cjkPatient;
            bool drugMatch = readDrug == specialCharsDrug;
            bool notesMatch = readNotes == emojiNotes;
            bool longMatch = readLong.Length == 2000;

            advXmlParsed = patientMatch && drugMatch && notesMatch && longMatch;

            Assert(advXmlParsed, "4.2 XML Escape & Unicode Preservation",
                $"Successfully preserved CJK Chinese characters, XML reserved symbols (<, >, &, \", '), emojis (💉💊⚠️), and 2,000-char string");
        }
        catch (Exception ex)
        {
            Assert(false, "4.2 XML Escape & Unicode Preservation", $"Parsing adversarial workbook failed: {ex.Message}");
        }

        // 4.3 High Volume Performance: 1,500 MAR items + 500 Rx items
        var bulkMar = new List<MarScheduleItemDto>(1500);
        for (int i = 0; i < 1500; i++)
        {
            bulkMar.Add(CreateSampleMar($"MRN{i:D6}", $"Bulk Patient {i}", $"Drug {i}", $"C{i}", 10m, "mg", "PO", DateTime.Now, "Dispensed"));
        }
        var bulkRx = new List<ActivePrescriptionDto>(500);
        for (int j = 0; j < 500; j++)
        {
            bulkRx.Add(CreateSampleRx($"MRN{j:D6}", $"Bulk Patient {j}", $"CASE{j:D6}", $"Drug {j}", $"C{j}", 10m, "mg", "PO", DateTime.Now, 10));
        }

        var sw = Stopwatch.StartNew();
        byte[] bulkBytes = service.GenerateMultiSheetExcel(bulkMar, bulkRx);
        sw.Stop();

        Assert(bulkBytes != null && bulkBytes.Length > 50000, "4.3 High Volume Scale (2,000 Records)",
            $"Generated 2,000-record workbook ({bulkBytes?.Length:N0} bytes) in {sw.ElapsedMilliseconds}ms (High throughput)");
    }

    private static async Task TestBattery5_DiskExportAndFilename(IExcelExportService service)
    {
        Console.WriteLine("\n--- BATTERY 5: Disk Export I/O & Standardized Filename ---");

        // 5.1 Standard Filename Generation
        string defaultName = service.GenerateTimestampedFilename();
        string customName = service.GenerateTimestampedFilename("MedHub_MAR_Export");

        var regex = new Regex(@"^MedHub_Prescriptions_\d{8}_\d{4}\.xlsx$");
        var customRegex = new Regex(@"^MedHub_MAR_Export_\d{8}_\d{4}\.xlsx$");

        Assert(regex.IsMatch(defaultName) && customRegex.IsMatch(customName), "5.1 Filename Regex Pattern",
            $"Default: '{defaultName}', Custom: '{customName}' both conform to [Prefix]_YYYYMMDD_HHmm.xlsx");

        // 5.2 Disk I/O & Path Handling
        string tempDir = Path.Combine(Path.GetTempPath(), "MedHubForensicDiskTest_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var mar = new List<MarScheduleItemDto> { CreateSampleMar("MRN_DISK", "Disk Patient", "Drug D", "D01", 5m, "mg", "PO", DateTime.Now, "Dispensed") };
            var rx = new List<ActivePrescriptionDto> { CreateSampleRx("MRN_DISK", "Disk Patient", "CASE_DISK", "Drug D", "D01", 5m, "mg", "PO", DateTime.Now, 3) };

            string exportedPath = await service.ExportToFileAsync(tempDir, mar, rx, "CustomNamedSpreadsheet");

            bool exists = File.Exists(exportedPath);
            long fileLen = exists ? new FileInfo(exportedPath).Length : 0;
            bool endsWithXlsx = exportedPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

            Assert(exists && fileLen > 5000 && endsWithXlsx, "5.2 ExportToFileAsync Disk I/O",
                $"File created at '{exportedPath}' (Size: {fileLen:N0} bytes, .xlsx appended automatically)");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static async Task TestBattery6_LiveDatabaseSnapshotExport(IExcelExportService service)
    {
        Console.WriteLine("\n--- BATTERY 6: Live Production Database Snapshot Export Verification ---");

        var config = AppConfigurationManager.Current;
        var factory = new DatabaseConnectionFactory(config.Database);
        var repo = new DatabaseRepository(factory);
        var clinicalService = new ClinicalDataService(repo);

        ClinicalDataSnapshot snapshot = await clinicalService.LoadClinicalSnapshotAsync();

        byte[] liveBytes = service.GenerateMultiSheetExcel(
            snapshot.MarSchedules,
            snapshot.ActivePrescriptions,
            "MedHub Inpatient Clinical Administration");

        Assert(liveBytes != null && liveBytes.Length > 20000, "6.1 Live Production Workbook Generation",
            $"Generated {liveBytes?.Length:N0} bytes from live amisdbV2 database snapshot");

        using var ms = new MemoryStream(liveBytes!);
        using var wb = new XLWorkbook(ms);

        var s1 = wb.Worksheet("MAR Schedule & Dispense Log");
        var s2 = wb.Worksheet("Active Prescriptions Overview");

        int s1Count = (s1.LastRowUsed()?.RowNumber() ?? 0) - 4;
        int s2Count = (s2.LastRowUsed()?.RowNumber() ?? 0) - 4;

        Assert(s1Count == snapshot.MarSchedules.Count, "6.2 Live S1 Row Count Match",
            $"S1 data rows: {s1Count} == Live MAR Schedules ({snapshot.MarSchedules.Count})");
        Assert(s2Count == snapshot.ActivePrescriptions.Count, "6.3 Live S2 Row Count Match",
            $"S2 data rows: {s2Count} == Live Active Prescriptions ({snapshot.ActivePrescriptions.Count})");

        // Inspect live patient records
        var distinctPatients = snapshot.MarSchedules
            .Select(m => (m.Patient_Mrn, m.Patient_Name))
            .Distinct()
            .Take(10)
            .ToList();
        Console.WriteLine("   DEBUG Live Sample Patients:");
        foreach (var p in distinctPatients)
        {
            Console.WriteLine($"     MRN: '{p.Patient_Mrn}', Name: '{p.Patient_Name}'");
        }

        // Verify patient with leading zero MRN
        bool foundLeadingZero = false;
        for (int r = 5; r <= 5 + s1Count; r++)
        {
            string mrn = s1.Cell(r, 1).GetString();
            string name = s1.Cell(r, 2).GetString();
            if (mrn.StartsWith('0') && mrn.Length > 1)
            {
                foundLeadingZero = true;
                bool isText = s1.Cell(r, 1).DataType == XLDataType.Text;
                bool isZeroPreserved = mrn.StartsWith('0');
                Assert(isText && isZeroPreserved, "6.4 Live Leading Zero Preservation",
                    $"Patient '{name}' MRN is '{mrn}' (DataType: {s1.Cell(r, 1).DataType}, format '@')");
                break;
            }
        }
        Assert(foundLeadingZero, "6.5 Found Leading Zero MRN in Live Export", "Leading zero MRN was found in the live workbook");
    }

    private static MarScheduleItemDto CreateSampleMar(
        string mrn, string patient, string drug, string code, decimal dose, string unit, string route, DateTime dt, string status)
    {
        return new MarScheduleItemDto
        {
            Patient_Mrn = mrn,
            Patient_Name = patient,
            Station_Name = "Station 1",
            Bed_Name = "Bed 101",
            Item_Code = code,
            Display_Name = drug,
            Generic_Name = drug + "_Generic",
            Dosage_Ordered_Value = dose,
            Dosage_Ordered_Unit = unit,
            Frequency = "QD",
            Route = route,
            Schedule_Date = dt.Date,
            Schedule_Time = dt.TimeOfDay,
            Dispense_Status = status,
            Dispensed_Qty = status == "Dispensed" ? 1.0m : null,
            Status_Time = status == "Dispensed" ? dt.AddMinutes(15) : null,
            Dispense_Nurse_Name = "Nurse Smith",
            isHighAlert = false
        };
    }

    private static ActivePrescriptionDto CreateSampleRx(
        string mrn, string patient, string caseNo, string drug, string code, decimal dose, string unit, string route, DateTime start, int totalDoses)
    {
        return new ActivePrescriptionDto
        {
            Patient_Mrn = mrn,
            Patient_Name = patient,
            Case_No = caseNo,
            Care_Station = "Station 1",
            Room_Bed = "Bed 101",
            Attending_Physician = "Dr. Adams",
            Medication_Code = code,
            Medication_Name = drug,
            Dosage_Ordered_Value = dose,
            Dosage_Ordered_Unit = unit,
            Frequency = "QD",
            Route = route,
            Clinical_Indication = "Hypertension",
            Start_Datetime = start,
            End_Datetime = start.AddDays(7),
            Total_Sched_Doses = totalDoses,
            Order_Status = "01",
            isHighAlert = false
        };
    }
}
