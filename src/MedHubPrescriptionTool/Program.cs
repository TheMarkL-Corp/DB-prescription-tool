using System;
using System.Linq;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Host;
using MedHubPrescriptionTool.Services;

namespace MedHubPrescriptionTool;

public class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        bool runExport = args.Any(a =>
            a.Equals("--export", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-e", StringComparison.OrdinalIgnoreCase));

        if (runExport)
        {
            return await RunExportCliAsync(args).ConfigureAwait(false);
        }

        bool runVerification = args.Any(a =>
            a.Equals("--verify", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--headless", StringComparison.OrdinalIgnoreCase));

        if (runVerification)
        {
            return await RunAllVerificationSuitesAsync().ConfigureAwait(false);
        }

        try
        {
            DesktopAppHost.Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Host Error] Failed to launch desktop application: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    /// <summary>
    /// Executes all 4 milestone verification runners in sequence.
    /// </summary>
    public static async Task<int> RunAllVerificationSuitesAsync()
    {
        Console.WriteLine("MedHub Inpatient Prescription & MAR Workstation (.NET 9)");
        Console.WriteLine("Automated Headless Clinical Verification Mode");
        Console.WriteLine();

        int m1 = await DataVerificationRunner.RunAsync().ConfigureAwait(false);
        if (m1 != 0) return m1;

        Console.WriteLine();
        int m2 = await ClinicalDataVerificationRunner.RunAsync().ConfigureAwait(false);
        if (m2 != 0) return m2;

        Console.WriteLine();
        int m3 = await ExcelExportVerificationRunner.RunAsync().ConfigureAwait(false);
        if (m3 != 0) return m3;

        Console.WriteLine();
        int m4 = await UiVerificationRunner.RunAsync().ConfigureAwait(false);
        return m4;
    }

    /// <summary>
    /// Exports all consolidated prescription schedules directly to an Excel file from CLI.
    /// </summary>
    public static async Task<int> RunExportCliAsync(string[] args)
    {
        try
        {
            Console.WriteLine("MedHub Inpatient Prescription & MAR Workstation (.NET 9)");
            Console.WriteLine("Connecting to MedHub database and exporting Excel report...");

            var config = AppConfigurationManager.Current;
            var factory = new DatabaseConnectionFactory(config.Database);
            var repo = new DatabaseRepository(factory);
            var clinicalService = new ClinicalDataService(repo);
            var excelService = new ExcelExportService();

            var snapshot = await clinicalService.LoadClinicalSnapshotAsync().ConfigureAwait(false);

            string targetDir = Environment.CurrentDirectory;
            int exportIdx = Array.FindIndex(args, a =>
                a.Equals("--export", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-e", StringComparison.OrdinalIgnoreCase));

            if (exportIdx >= 0 && exportIdx + 1 < args.Length && !args[exportIdx + 1].StartsWith("-"))
            {
                targetDir = args[exportIdx + 1];
            }

            string savedPath = await excelService.ExportToFileAsync(
                targetDir,
                snapshot.MarSchedules,
                snapshot.ActivePrescriptions).ConfigureAwait(false);

            Console.WriteLine($"[Export Success] Saved Excel report to: {savedPath} ({new FileInfo(savedPath).Length:N0} bytes)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Export Failed] {ex.Message}");
            return 1;
        }
    }
}
