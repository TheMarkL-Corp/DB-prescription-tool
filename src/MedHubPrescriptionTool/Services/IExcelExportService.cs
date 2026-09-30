using System.Collections.Generic;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Models;

namespace MedHubPrescriptionTool.Services;

/// <summary>
/// Service contract for generating professional, clinical-grade multi-sheet Excel workbooks.
/// Fulfills Requirement R4 and PROJECT.md Feature Inventory F15, F16, and F17.
/// </summary>
public interface IExcelExportService
{
    /// <summary>
    /// Generates a styled, two-sheet Excel workbook in memory as an OpenXML byte array.
    /// Sheet 1: "MAR Schedule & Dispense Log" (17 columns)
    /// Sheet 2: "Active Prescriptions Overview" (16 columns)
    /// </summary>
    /// <param name="marItems">Collection of MAR administration schedule records.</param>
    /// <param name="activePrescriptions">Collection of active inpatient prescription orders.</param>
    /// <param name="hospitalTitle">Hospital or clinical department header title.</param>
    /// <param name="sheet1Name">Name for Sheet 1 (default: "MAR Schedule & Dispense Log").</param>
    /// <param name="sheet2Name">Name for Sheet 2 (default: "Active Prescriptions Overview").</param>
    /// <returns>Raw .xlsx file binary bytes.</returns>
    byte[] GenerateMultiSheetExcel(
        IReadOnlyList<MarScheduleItemDto> marItems,
        IReadOnlyList<ActivePrescriptionDto> activePrescriptions,
        string hospitalTitle = "MedHub Inpatient Clinical Administration",
        string sheet1Name = "MAR Schedule & Dispense Log",
        string sheet2Name = "Active Prescriptions Overview");

    /// <summary>
    /// Generates a standardized clinical timestamped filename: MedHub_Prescriptions_YYYYMMDD_HHmm.xlsx.
    /// </summary>
    /// <param name="prefix">Filename prefix (default: "MedHub_Prescriptions").</param>
    string GenerateTimestampedFilename(string prefix = "MedHub_Prescriptions");

    /// <summary>
    /// Generates the workbook and writes it directly to disk under the specified directory.
    /// </summary>
    /// <param name="outputDirectory">Target folder path (defaults to AppContext.BaseDirectory if empty).</param>
    /// <param name="marItems">Collection of MAR administration schedule records.</param>
    /// <param name="activePrescriptions">Collection of active inpatient prescription orders.</param>
    /// <param name="customFilename">Optional custom filename override.</param>
    /// <param name="hospitalTitle">Hospital or clinical department header title.</param>
    /// <returns>Absolute file path of the saved .xlsx spreadsheet.</returns>
    Task<string> ExportToFileAsync(
        string outputDirectory,
        IReadOnlyList<MarScheduleItemDto> marItems,
        IReadOnlyList<ActivePrescriptionDto> activePrescriptions,
        string? customFilename = null,
        string hospitalTitle = "MedHub Inpatient Clinical Administration");
}
