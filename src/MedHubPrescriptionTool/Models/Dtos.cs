using System;
using System.Collections.Generic;
using System.Linq;

namespace MedHubPrescriptionTool.Models;

#region Patient DTOs

/// <summary>
/// Clinical Inpatient Overview DTO for Master-Detail Tab 1.
/// Consolidates patient master data, admission encounter, assigned bed/station,
/// allergy/high-alert warnings, and prescription & schedule metrics.
/// </summary>
public class PatientOverviewDto
{
    public Guid Patient_Id { get; set; }
    public string Patient_Mrn { get; set; } = string.Empty;
    public string First_Name { get; set; } = string.Empty;
    public string Last_Name { get; set; } = string.Empty;
    public string FullName => $"{Last_Name} {First_Name}".Trim();
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Patient_Mrn : FullName;
    public string Patient_Name => string.IsNullOrWhiteSpace(FullName) ? DisplayName : FullName;
    public string Patient_Sex => Sex;

    public string Sex { get; set; } = string.Empty;
    public DateTime Date_Of_Birth { get; set; }
    public int Age => CalculateAge(Date_Of_Birth);

    public decimal? Weight_Kg { get; set; }
    public decimal? Height_Cm { get; set; }

    public string Allergy { get; set; } = "N";
    public bool HasAllergy => string.Equals(Allergy, "Y", StringComparison.OrdinalIgnoreCase);
    public string? Allergy_Desc { get; set; }

    public string DrugWarn { get; set; } = "N";
    public bool HasDrugWarn => string.Equals(DrugWarn, "Y", StringComparison.OrdinalIgnoreCase);
    public string? DrugWarn_Desc { get; set; }

    // Encounter
    public Guid Encounter_Id { get; set; }
    public string Case_No { get; set; } = string.Empty;
    public DateTime Admission_Date { get; set; }
    public DateTime? Discharge_Date { get; set; }
    public string Encounter_Status { get; set; } = string.Empty;
    public string? Attending_Physician { get; set; }

    // Location
    public Guid? Station_Id { get; set; }
    public string? Station_Code { get; set; }
    public string? Station_Name { get; set; }
    public Guid? Bed_Id { get; set; }
    public string? Bed_Code { get; set; }
    public string? Bed_Name { get; set; }

    // Aggregated Metrics
    public int Total_Prescriptions { get; set; }
    public int Total_Scheduled_Doses { get; set; }
    public int Dispensed_Doses { get; set; }
    public int Pending_Doses { get; set; }

    // Associated Details
    public IReadOnlyList<ActivePrescriptionDto> ActivePrescriptions { get; set; } = [];
    public IReadOnlyList<MarScheduleItemDto> RecentSchedules { get; set; } = [];

    public static int CalculateAge(DateTime dob, DateTime? asOf = null)
    {
        if (dob == default) return 0;
        var reference = asOf ?? DateTime.Today;
        int age = reference.Year - dob.Year;
        if (dob.Date > reference.AddYears(-age)) age--;
        return Math.Max(0, age);
    }
}

#endregion

#region MAR Schedule Item DTO

/// <summary>
/// Unified Medication Administration Record (MAR) Item DTO for Tab 2 and Excel Sheet 1.
/// Combines Schedule dose time, patient details, order instructions, dispense log, and safety flags.
/// </summary>
public class MarScheduleItemDto
{
    // Identification
    public Guid Schedule_Id { get; set; }
    private long _scheduleId;
    public long ScheduleId
    {
        get => _scheduleId != 0 ? _scheduleId : (Schedule_Id != Guid.Empty ? (BitConverter.ToInt64(Schedule_Id.ToByteArray(), 0) & 0x7FFFFFFFFFFFFFFF) : 0L);
        set => _scheduleId = value;
    }
    public Guid Med_Order_Id { get; set; }
    public Guid Encounter_Id { get; set; }
    public Guid Patient_Id { get; set; }

    // Patient Demographics
    public string Patient_Mrn { get; set; } = string.Empty;
    public string Patient_Name { get; set; } = string.Empty;
    public string Patient_Sex { get; set; } = string.Empty;
    public DateTime Patient_Dob { get; set; }
    public int Patient_Age => PatientOverviewDto.CalculateAge(Patient_Dob);
    public string Case_No { get; set; } = string.Empty;

    // Location
    public string? Station_Code { get; set; }
    public string? Station_Name { get; set; }
    public string? Bed_Code { get; set; }
    public string? Bed_Name { get; set; }
    public string? Attending_Physician { get; set; }

    // Medication Details
    public Guid? Medication_Id { get; set; }
    public string Item_Code { get; set; } = string.Empty;
    public string? Generic_Name { get; set; }
    public string Display_Name { get; set; } = string.Empty;
    public string? Atc_Code { get; set; }
    public string? Form { get; set; }

    // Prescription Instructions
    public decimal Dosage_Ordered_Value { get; set; }
    public string Dosage_Ordered_Unit { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string DosageInstruction => $"{Dosage_Ordered_Value:0.##} {Dosage_Ordered_Unit} {Frequency} {Route}".Trim();
    public string Order_Status { get; set; } = string.Empty;
    public DateTime? Order_Start_Datetime { get; set; }
    public DateTime? Order_End_Datetime { get; set; }
    public string? Clinical_Indication { get; set; }
    public string? Instruction_Note { get; set; }

    // Schedule Administration
    public DateTime Schedule_Date { get; set; }
    public TimeSpan Schedule_Time { get; set; }
    public DateTime ScheduledDateTime => Schedule_Date.Date.Add(Schedule_Time);
    public decimal Calc_Dispense_Qty { get; set; }
    public string Dispense_Qty_Unit { get; set; } = string.Empty;
    public string? Schedule_Status { get; set; }
    public DateTime? Processed_Time { get; set; }
    public DateTime? Confirmed_Time { get; set; }

    // Dispense & Return / Waste Log
    public int? Dispense_Id { get; set; }
    public string Dispense_Status { get; set; } = string.Empty; // Dispensed, Pending (Due), Pending (Future), Returned, Wasted, Cancelled
    public DateTime? Dispense_Time { get; set; }
    public decimal? Dispensed_Qty { get; set; }
    public DateTime? Status_Time { get; set; }
    public DateTime? Return_Time { get; set; }
    public decimal? Return_Qty { get; set; }
    public string? Return_Reason_Id { get; set; }
    public string? Return_Reason_Name { get; set; }
    public DateTime? Waste_Time { get; set; }
    public decimal? Waste_Qty { get; set; }
    public string? Waste_Reason_Id { get; set; }
    public string? Waste_Reason_Name { get; set; }
    public string? Cancel_Reason_Id { get; set; }
    public string? Cancel_Reason_Name { get; set; }

    // Clinician Names
    public string? Dispense_Nurse_Name { get; set; }
    public string? Confirmed_Nurse_Name { get; set; }

    // Clinical Safety Badges
    public bool isHighAlert { get; set; }
    public bool isRefrigerationRequired { get; set; }
    public bool Is_Controlled { get; set; }
    public string? Control_Level { get; set; }
    public string? Wintness_Required { get; set; }
    public bool? Is_Divisible { get; set; }

    // Patient Safety Warnings
    public string Allergy { get; set; } = "N";
    public string? Allergy_Desc { get; set; }
    public string DrugWarn { get; set; } = "N";
    public string? DrugWarn_Desc { get; set; }
}

#endregion

#region Active Prescription DTO

/// <summary>
/// Active Inpatient Prescription DTO for Excel Sheet 2 and Patient Overview order card list.
/// </summary>
public class ActivePrescriptionDto
{
    public Guid Med_Order_Id { get; set; }
    public Guid Encounter_Id { get; set; }
    public Guid Patient_Id { get; set; }
    public string Patient_Mrn { get; set; } = string.Empty;
    public string Case_No { get; set; } = string.Empty;
    public string Patient_Name { get; set; } = string.Empty;

    public string Care_Station { get; set; } = string.Empty;
    public string Station_Code { get; set; } = string.Empty;
    public string Room_Bed { get; set; } = string.Empty;
    public string Bed_Code { get; set; } = string.Empty;
    public string Attending_Physician { get; set; } = string.Empty;

    public Guid? Medication_Id { get; set; }
    public string Medication_Code { get; set; } = string.Empty;
    public string Medication_Name { get; set; } = string.Empty;
    public string? Generic_Name { get; set; }
    public string? Atc_Code { get; set; }
    public string? Form { get; set; }

    public decimal Dosage_Ordered_Value { get; set; }
    public string Dosage_Ordered_Unit { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Dosage_Instruction => $"{Dosage_Ordered_Value:0.##} {Dosage_Ordered_Unit} {Frequency} {Route}".Trim();

    public string? Clinical_Indication { get; set; }
    public string? Instruction_Note { get; set; }

    public DateTime? Start_Datetime { get; set; }
    public DateTime? End_Datetime { get; set; }
    public string Order_Status { get; set; } = string.Empty;

    public int Total_Sched_Doses { get; set; }
    public int Dispensed_Doses { get; set; }
    public int Pending_Doses { get; set; }

    public bool isHighAlert { get; set; }
    public string HighAlertDisplay => isHighAlert ? "YES" : "NO";
    public bool isRefrigerationRequired { get; set; }
    public bool Is_Controlled { get; set; }
    public string? Control_Level { get; set; }
}

#endregion

#region Medication Detail DTO

/// <summary>
/// Complete Medication Monograph & Clinical Specification DTO for Medication Detail Modal / Drawer.
/// </summary>
public class MedicationDetailDto
{
    public Guid Medication_Id { get; set; }
    public string Item_Code { get; set; } = string.Empty;
    public string Display_Name { get; set; } = string.Empty;
    public string? Generic_Name { get; set; }
    public string? Atc_Code { get; set; }
    public string Form { get; set; } = string.Empty;

    public decimal Strength_Value { get; set; }
    public string Strength_Unit { get; set; } = string.Empty;
    public string StrengthDisplay => $"{Strength_Value:0.##} {Strength_Unit}".Trim();

    public string? Primary_Barcode_Value { get; set; }
    public string? Primary_Barcode_Label { get; set; }
    public string? Packaging_Type { get; set; }
    public string? Dispense_Form { get; set; }
    public decimal? Vol_Content { get; set; }
    public string? Is_ADC { get; set; }

    // Safety and Regulations
    public bool Is_Controlled { get; set; }
    public string? Control_Level { get; set; }
    public string Wintness_Required { get; set; } = "N";
    public bool IsWitnessRequired => string.Equals(Wintness_Required, "Y", StringComparison.OrdinalIgnoreCase);
    public bool isHighAlert { get; set; }
    public bool isRefrigerationRequired { get; set; }
    public bool isBlindCountRequired { get; set; }
    public bool Is_Divisible { get; set; }
    public bool Is_Active { get; set; } = true;

    // Storage & Guidance
    public string? Storage_Note { get; set; }
    public string? Storage_URL { get; set; }
    public string? Element_Name { get; set; }
    public string? Drug_License_No { get; set; }
    public string? Manufacturer_Code { get; set; }
    public string? Reporting_Unit { get; set; }
    public string? Description { get; set; }
    public string? Indication { get; set; }
    public string? Instructions { get; set; }

    public int ActivePrescriptionsCount { get; set; }
}

#endregion

#region KPI Summary DTO

/// <summary>
/// Top Dashboard KPI Metric Summary DTO.
/// Accurately computed from consolidated clinical snapshot.
/// </summary>
public class KpiSummaryDto
{
    public int TotalInpatients { get; set; }
    public int TotalActiveOrders { get; set; }
    public int DispensedCount { get; set; }
    public int DuePendingOrders { get; set; }
    public int TotalPendingOrders { get; set; }
    public int FuturePendingOrders { get; set; }
    public int ReturnedCount { get; set; }
    public int WastedCount { get; set; }
    public int CancelledCount { get; set; }
    public int HighAlertCount { get; set; }
    public int ControlledCount { get; set; }
    public DateTime LastRefreshedAt { get; set; } = DateTime.Now;
}

#endregion

#region Filter Criteria & Snapshot

/// <summary>
/// Multi-field criteria for instant fuzzy filtering on MAR schedules and patient directories.
/// </summary>
public class ClinicalFilterCriteria
{
    public string? SearchQuery { get; set; }
    public string? StationCode { get; set; }
    public string? BedCode { get; set; }
    public string? PatientMrn { get; set; }

    /// <summary>
    /// Date filter: "All", "Today", "Past", "Future", "Custom"
    /// </summary>
    public string? DateFilter { get; set; } = "All";
    public DateTime? CustomStartDate { get; set; }
    public DateTime? CustomEndDate { get; set; }

    /// <summary>
    /// Status filter: "All", "Dispensed", "Pending", "Pending (Due)", "Pending (Future)", "Returned", "Wasted", "Cancelled", "Returned/Wasted/Cancelled"
    /// </summary>
    public string? StatusFilter { get; set; } = "All";

    public bool? HighAlertOnly { get; set; }
    public bool? ControlledOnly { get; set; }
    public bool? RefrigerationOnly { get; set; }

    /// <summary>
    /// Optional reference time for deterministic time-based filtering (defaults to DateTime.Now).
    /// </summary>
    public DateTime? ReferenceTime { get; set; }
}

/// <summary>
/// Immutable snapshot container holding all consolidated clinical DTOs, lookup maps, and refresh timestamp.
/// </summary>
public class ClinicalDataSnapshot
{
    public IReadOnlyList<PatientOverviewDto> Patients { get; init; } = [];
    public IReadOnlyList<MarScheduleItemDto> MarSchedules { get; init; } = [];
    public IReadOnlyList<ActivePrescriptionDto> ActivePrescriptions { get; init; } = [];
    public IReadOnlyList<MedicationDetailDto> Medications { get; init; } = [];
    public IReadOnlyList<CareStationRawRecord> Stations { get; init; } = [];
    public IReadOnlyList<RoomBedRawRecord> Beds { get; init; } = [];

    public KpiSummaryDto KpiSummary { get; set; } = new();
    public DateTime LastRefreshedAt { get; init; } = DateTime.Now;

    public IReadOnlyDictionary<string, MedicationDetailDto> MedicationByCode { get; init; } =
        new Dictionary<string, MedicationDetailDto>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PatientOverviewDto> PatientByMrn { get; init; } =
        new Dictionary<string, PatientOverviewDto>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, CareStationRawRecord> StationByCode { get; init; } =
        new Dictionary<string, CareStationRawRecord>(StringComparer.OrdinalIgnoreCase);
}

#endregion

#region NULL-Safe Status Evaluator & Clinical Reason Dictionary

/// <summary>
/// NULL-safe status state machine and clinical dictionary lookup.
/// Strictly implements the precedence rules documented in Explorer 1 § 5 and Explorer 3 § 2.
/// </summary>
public static class ClinicalStatusEvaluator
{
    public const string StatusDispensed = "Dispensed";
    public const string StatusPendingDue = "Pending (Due)";
    public const string StatusPendingFuture = "Pending (Future)";
    public const string StatusReturned = "Returned";
    public const string StatusWasted = "Wasted";
    public const string StatusCancelled = "Cancelled";

    /// <summary>
    /// Evaluates the clinical dispensing status with NULL safety and absolute return/waste cancellation precedence.
    /// </summary>
    public static string EvaluateStatus(
        string? dispenseStatus,
        DateTime? confirmedTime,
        string? scheduleStatus,
        string? orderStatus,
        DateTime scheduleDate,
        TimeSpan scheduleTime,
        DateTime? returnTime = null,
        decimal? returnQty = null,
        DateTime? wasteTime = null,
        decimal? wasteQty = null,
        string? returnReasonId = null,
        string? wasteReasonId = null,
        DateTime? referenceTime = null)
    {
        // 1. Explicit Return, Waste, or Cancel in Dispense Log has absolute precedence
        if (string.Equals(dispenseStatus, "N", StringComparison.OrdinalIgnoreCase))
        {
            if (returnTime != null || (returnQty ?? 0) > 0 || !string.IsNullOrWhiteSpace(returnReasonId))
                return StatusReturned;

            if (wasteTime != null || (wasteQty ?? 0) > 0 || !string.IsNullOrWhiteSpace(wasteReasonId))
                return StatusWasted;

            return StatusCancelled;
        }

        // 2. Inactive Prescription or Inactive Schedule
        if (string.Equals(scheduleStatus, "99", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(orderStatus, "99", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCancelled;
        }

        // 3. Dispensed or Confirmed (Dual verification or signed administration)
        if (string.Equals(dispenseStatus, "Y", StringComparison.OrdinalIgnoreCase) || confirmedTime != null)
        {
            return StatusDispensed;
        }

        // 4. Pending Administration: Due Now vs Scheduled Future
        var scheduledDateTime = scheduleDate.Date.Add(scheduleTime);
        var now = referenceTime ?? DateTime.Now;

        return scheduledDateTime < now ? StatusPendingDue : StatusPendingFuture;
    }

    /// <summary>
    /// Helper evaluating status directly from a MarScheduleRawRecord.
    /// </summary>
    public static string EvaluateStatus(MarScheduleRawRecord record, DateTime? referenceTime = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        return EvaluateStatus(
            dispenseStatus: record.Dispense_Status,
            confirmedTime: record.Confirmed_Time,
            scheduleStatus: record.Schedule_Status,
            orderStatus: record.Order_Status,
            scheduleDate: record.Schedule_Date,
            scheduleTime: record.Schedule_Time,
            returnTime: record.Return_Time,
            returnQty: record.Return_Qty,
            wasteTime: record.Waste_Time,
            wasteQty: record.Waste_Qty,
            returnReasonId: record.Return_Reason_Id,
            wasteReasonId: record.Waste_Reason_Id,
            referenceTime: referenceTime);
    }

    /// <summary>
    /// Translates Return reason code to clinical English / Chinese description.
    /// </summary>
    public static string GetReturnReasonDescription(string? reasonId) => reasonId switch
    {
        "01" => "醫師取消 (Physician Cancelled)",
        "02" => "病人禁食 (Patient Fasting / NPO)",
        "03" => "病人抗拒 (Patient Refused)",
        "04" => "給錯藥 (Wrong Medication Dispensed)",
        "05" => "多給藥 (Excess Medication Dispensed)",
        _ => string.IsNullOrWhiteSpace(reasonId) ? string.Empty : $"Code: {reasonId}"
    };

    /// <summary>
    /// Translates Waste reason code to clinical English / Chinese description.
    /// </summary>
    public static string GetWasteReasonDescription(string? reasonId) => reasonId switch
    {
        "A" => "逾期 (Expired)",
        "B" => "污損 (Contaminated / Damaged)",
        "C" => "不可回收餘藥 (Non-recyclable Residual Drug)",
        _ => string.IsNullOrWhiteSpace(reasonId) ? string.Empty : $"Code: {reasonId}"
    };

    /// <summary>
    /// Translates Cancel reason code to clinical English / Chinese description.
    /// </summary>
    public static string GetCancelReasonDescription(string? reasonId) => reasonId switch
    {
        "A" => "醫師減藥 (Dose Reduction)",
        "B" => "病人請假 (Patient on Leave)",
        "C" => "病人手術 (Patient in Surgery)",
        "W" => "廢棄 (Discarded)",
        "Z" => "其他 (Other)",
        _ => string.IsNullOrWhiteSpace(reasonId) ? string.Empty : $"Code: {reasonId}"
    };
}

#endregion
