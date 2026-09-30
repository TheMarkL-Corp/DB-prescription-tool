using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedHubPrescriptionTool.Tests.TestHelpers
{
    public static class ColorExtensions
    {
        public static string ToHex(this Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public class DbConnectionSettings
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5433;
        public string Database { get; set; } = "amisdbV2";
        public string Username { get; set; } = "postgres";
        public string Password { get; set; } = "Syscom@123";
        public bool ReadOnly { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 15;

        public string ToConnectionString()
        {
            var baseConn = $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};Timeout={TimeoutSeconds};CommandTimeout={TimeoutSeconds};";
            if (ReadOnly)
            {
                baseConn += "Options=-c default_transaction_read_only=on;";
            }
            return baseConn;
        }
    }

    public class PatientOverviewDto
    {
        public Guid PatientId { get; set; }
        public string PatientMrn { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Sex { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public decimal? WeightKg { get; set; }
        public decimal? HeightCm { get; set; }
        public string Allergy { get; set; } = "N";
        public string? AllergyDesc { get; set; }
        public string DrugWarn { get; set; } = "N";
        public string? DrugWarnDesc { get; set; }
        public Guid EncounterId { get; set; }
        public string CaseNo { get; set; } = string.Empty;
        public DateTime AdmissionDate { get; set; }
        public DateTime? DischargeDate { get; set; }
        public string? AttendingPhysician { get; set; }
        public string? CareStationCode { get; set; }
        public string? CareStationName { get; set; }
        public string? RoomBedCode { get; set; }
        public string? RoomBedName { get; set; }
        public int ActiveOrderCount { get; set; }
        public int TotalScheduledDoses { get; set; }
        public int DispensedDoses { get; set; }
        public int PendingDoses { get; set; }
    }

    public class MarScheduleItemDto
    {
        public Guid ScheduleId { get; set; }
        public Guid MedOrderId { get; set; }
        public DateTime ScheduleDate { get; set; }
        public TimeSpan ScheduleTime { get; set; }
        public DateTime ScheduleDateTime => ScheduleDate.Date.Add(ScheduleTime);
        public string PatientMrn { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string? CareStation { get; set; }
        public string? RoomBed { get; set; }
        public string MedicationCode { get; set; } = string.Empty;
        public string ItemCode { get => MedicationCode; set => MedicationCode = value; }
        public string DisplayName { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string? AtcCode { get; set; }
        public decimal DosageOrderedValue { get; set; }
        public string DosageOrderedUnit { get; set; } = string.Empty;
        public string Frequency { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public string DispenseStatus { get; set; } = "Pending";
        public decimal? DispensedQty { get; set; }
        public DateTime? StatusTime { get; set; }
        public string? AdministeringNurse { get; set; }
        public bool IsHighAlert { get; set; }
        public bool IsRefrigerationRequired { get; set; }
        public bool IsControlled { get; set; }
        public string? ControlLevel { get; set; }
        public bool WitnessRequired { get; set; }
        public string? ClinicalIndication { get; set; }
        public string? ReturnReason { get; set; }
        public decimal? ReturnQty { get; set; }
        public decimal? WasteQty { get; set; }
        public string? OrderStatus { get; set; }
        public string? ScheduleStatus { get; set; }
    }

    public class ActivePrescriptionDto
    {
        public Guid MedOrderId { get; set; }
        public string PatientMrn { get; set; } = string.Empty;
        public string CaseNo { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string? CareStation { get; set; }
        public string? RoomBed { get; set; }
        public string? AttendingPhysician { get; set; }
        public string MedicationCode { get; set; } = string.Empty;
        public string MedicationName { get; set; } = string.Empty;
        public string DosageInstruction { get; set; } = string.Empty;
        public string? ClinicalIndication { get; set; }
        public DateTime? StartDatetime { get; set; }
        public DateTime? EndDatetime { get; set; }
        public int TotalScheduledDoses { get; set; }
        public int DispensedDoses { get; set; }
        public int PendingDoses { get; set; }
        public bool IsHighAlert { get; set; }
    }

    public class MedicationDetailDto
    {
        public Guid MedicationId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string? AtcCode { get; set; }
        public string Form { get; set; } = string.Empty;
        public decimal StrengthValue { get; set; }
        public string StrengthUnit { get; set; } = string.Empty;
        public bool IsHighAlert { get; set; }
        public bool IsControlled { get; set; }
        public string? ControlLevel { get; set; }
        public bool WitnessRequired { get; set; }
        public bool IsRefrigerationRequired { get; set; }
        public bool IsDivisible { get; set; }
        public string? StorageNote { get; set; }
        public string? StorageUrl { get; set; }
        public string? Description { get; set; }
    }

    public class KpiSummaryDto
    {
        public int TotalInpatients { get; set; }
        public int TotalActiveOrders { get; set; }
        public int DispensedCount { get; set; }
        public int PendingDueCount { get; set; }
    }

    public class ClinicalFilterCriteria
    {
        public string? SearchText { get; set; }
        public string DateFilter { get; set; } = "All"; // "Today", "Past", "Future", "All"
        public string StatusFilter { get; set; } = "All"; // "All", "Dispensed", "Pending", "Cancelled"
        public bool HighAlertOnly { get; set; }
        public string? StationFilter { get; set; }
        public DateTime? ReferenceDate { get; set; }
    }

    public static class ClinicalFilterEngine
    {
        public static IEnumerable<MarScheduleItemDto> ApplyFilter(
            IEnumerable<MarScheduleItemDto> source,
            ClinicalFilterCriteria criteria)
        {
            var query = source;
            var refDate = (criteria.ReferenceDate ?? DateTime.Today).Date;

            // Date filtering
            switch (criteria.DateFilter.ToLowerInvariant())
            {
                case "today":
                    query = query.Where(x => x.ScheduleDate.Date == refDate);
                    break;
                case "past":
                case "past / overdue":
                    query = query.Where(x => x.ScheduleDate.Date < refDate);
                    break;
                case "future":
                    query = query.Where(x => x.ScheduleDate.Date > refDate);
                    break;
            }

            // Status filtering
            switch (criteria.StatusFilter.ToLowerInvariant())
            {
                case "dispensed":
                    query = query.Where(x => x.DispenseStatus.Equals("Dispensed", StringComparison.OrdinalIgnoreCase));
                    break;
                case "pending":
                case "pending / due":
                    query = query.Where(x => x.DispenseStatus.StartsWith("Pending", StringComparison.OrdinalIgnoreCase));
                    break;
                case "cancelled":
                case "returned":
                case "wasted":
                    query = query.Where(x => x.DispenseStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             x.DispenseStatus.Equals("Returned", StringComparison.OrdinalIgnoreCase) ||
                                             x.DispenseStatus.Equals("Wasted", StringComparison.OrdinalIgnoreCase));
                    break;
            }

            // High alert priority filter
            if (criteria.HighAlertOnly)
            {
                query = query.Where(x => x.IsHighAlert || x.IsControlled);
            }

            // Care station filter
            if (!string.IsNullOrWhiteSpace(criteria.StationFilter) && criteria.StationFilter != "All")
            {
                query = query.Where(x => x.CareStation != null && x.CareStation.Contains(criteria.StationFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Fuzzy multi-field search
            if (!string.IsNullOrWhiteSpace(criteria.SearchText))
            {
                var term = criteria.SearchText.Trim();
                query = query.Where(x =>
                    x.PatientMrn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    x.PatientName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.DisplayName != null && x.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.GenericName != null && x.GenericName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.MedicationCode != null && x.MedicationCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.AtcCode != null && x.AtcCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.CareStation != null && x.CareStation.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.RoomBed != null && x.RoomBed.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            return query;
        }

        public static string ComputeDispensingStatus(
            string? dispenseStatus,
            DateTime? confirmedTime,
            DateTime? returnTime,
            decimal? returnQty,
            DateTime? wasteTime,
            decimal? wasteQty,
            string? scheduleStatus,
            string? orderStatus,
            DateTime scheduleDateTime,
            DateTime referenceNow)
        {
            // 1. Explicit return, waste or cancellation
            if (dispenseStatus == "N")
            {
                if (returnTime.HasValue || (returnQty.HasValue && returnQty.Value > 0))
                    return "Returned";
                if (wasteTime.HasValue || (wasteQty.HasValue && wasteQty.Value > 0))
                    return "Wasted";
                return "Cancelled";
            }

            // 2. Schedule or Order cancelled
            if (scheduleStatus == "99" || orderStatus == "99")
            {
                return "Cancelled";
            }

            // 3. Dispensed or Confirmed
            if (dispenseStatus == "Y" || confirmedTime.HasValue)
            {
                return "Dispensed";
            }

            // 4. Pending Due vs Pending Future
            if (scheduleDateTime < referenceNow)
            {
                return "Pending (Due)";
            }

            return "Pending (Future)";
        }
    }
}
