using System;

namespace MedHubPrescriptionTool.Models;

#region Core Entity Raw Records (8 Core Tables + Users)

/// <summary>
/// Direct mapping to PostgreSQL table "Patients" (15 rows).
/// </summary>
public class PatientRawRecord
{
    public Guid Patient_Id { get; set; }
    public string Patient_Mrn { get; set; } = string.Empty;
    public string Patient_Id_Type { get; set; } = string.Empty;
    public string Patient_National_Id { get; set; } = string.Empty;
    public string First_Name { get; set; } = string.Empty;
    public string Last_Name { get; set; } = string.Empty;
    public string? Middle_Name { get; set; }
    public string Sex { get; set; } = string.Empty;
    public DateTime Date_Of_Birth { get; set; }
    public decimal? Weight_Kg { get; set; }
    public decimal? Height_Cm { get; set; }
    public string Allergy { get; set; } = "N";
    public string? Allergy_Desc { get; set; }
    public string DrugWarn { get; set; } = "N";
    public string? DrugWarn_Desc { get; set; }
    public string? Contact_No { get; set; }
    public string? Email { get; set; }
    public string? Hint_Code { get; set; }
    public Guid Create_User { get; set; }
    public DateTime Create_Time { get; set; }
    public Guid Modified_User { get; set; }
    public DateTime Modified_Time { get; set; }

    public string FullName => $"{Last_Name} {First_Name}".Trim();
}

/// <summary>
/// Direct mapping to PostgreSQL table "Encounter" (16 rows).
/// </summary>
public class EncounterRawRecord
{
    public Guid Encounter_Id { get; set; }
    public Guid Patient_Id { get; set; }
    public string Case_No { get; set; } = string.Empty;
    public DateTime Admission_Date { get; set; }
    public string? Patient_Qrcode { get; set; }
    public Guid? Station_Id { get; set; }
    public Guid? Bed_Id { get; set; }
    public string? Admission_Source { get; set; }
    public string? Attending_Physician { get; set; }
    public string Encounter_Status { get; set; } = string.Empty;
    public DateTime? Discharge_Date { get; set; }
    public bool? isPharmacySupply { get; set; }
    public Guid Create_User { get; set; }
    public DateTime Create_Time { get; set; }
    public Guid Modified_User { get; set; }
    public DateTime Modified_Time { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "Prescriptions" (31 rows).
/// </summary>
public class PrescriptionRawRecord
{
    public Guid Med_Order_Id { get; set; }
    public Guid Encounter_Id { get; set; }
    public Guid? Medication_Id { get; set; }
    public decimal Dosage_Ordered_Value { get; set; }
    public string Dosage_Ordered_Unit { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Order_Status { get; set; } = string.Empty;
    public DateTime? Start_Datetime { get; set; }
    public DateTime? End_Datetime { get; set; }
    public string? Clinical_Indication { get; set; }
    public string? Instruction_Note { get; set; }
    public Guid Create_User { get; set; }
    public DateTime Create_Time { get; set; }
    public Guid Modified_User { get; set; }
    public DateTime Modified_Time { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "Schedule" (385 rows).
/// </summary>
public class ScheduleRawRecord
{
    public Guid Schedule_Id { get; set; }
    public Guid Med_Order_Id { get; set; }
    public DateTime Schedule_Date { get; set; }
    public TimeSpan Schedule_Time { get; set; }
    public decimal Calc_Dispense_Qty { get; set; }
    public string Dispense_Qty_Unit { get; set; } = string.Empty;
    public string? Schedule_Status { get; set; }
    public DateTime? Processed_Time { get; set; }
    public Guid? Processed_User { get; set; }
    public DateTime? Confirmed_Time { get; set; }
    public Guid? Confirmed_User { get; set; }
    public string? His_Schedule_Id { get; set; }
    public Guid Create_User { get; set; }
    public DateTime Create_Time { get; set; }
    public Guid Modified_User { get; set; }
    public DateTime Modified_Time { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "Medications" (114 rows).
/// </summary>
public class MedicationRawRecord
{
    public Guid Medication_Id { get; set; }
    public string Item_Code { get; set; } = string.Empty;
    public string? Atc_Code { get; set; }
    public string? Generic_Name { get; set; }
    public string Display_Name { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public bool? Is_Controlled { get; set; }
    public string? Control_Level { get; set; }
    public string Wintness_Required { get; set; } = "N";
    public bool? isHighAlert { get; set; }
    public bool? isRefrigerationRequired { get; set; }
    public bool? isBlindCountRequired { get; set; }
    public bool? Is_Divisible { get; set; }
    public bool? Is_Active { get; set; }
    public DateTime? Deactivated_At { get; set; }
    public string? Deactivated_Reason { get; set; }
    public decimal Strength_Value { get; set; }
    public string Strength_Unit { get; set; } = string.Empty;
    public string? Primary_Barcode_Value { get; set; }
    public string? Primary_Barcode_Label { get; set; }
    public string? Packaging_Type { get; set; }
    public string? Dispense_Form { get; set; }
    public decimal? Vol_Content { get; set; }
    public string? Is_ADC { get; set; }
    public string? Storage_Note { get; set; }
    public string? Storage_URL { get; set; }
    public string? Element_Name { get; set; }
    public string? Drug_License_No { get; set; }
    public string? Manufacturer_Code { get; set; }
    public string? Reporting_Unit { get; set; }
    public string Manual_Match { get; set; } = "N";
    public string? Image { get; set; }
    public string? Image_Data { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "AMiS_Patient_Medication_Dispense" (2 rows).
/// </summary>
public class DispenseRawRecord
{
    public int Id { get; set; }
    public string Bin_Id { get; set; } = string.Empty;
    public Guid Encounter_Id { get; set; }
    public Guid? Patient_Id { get; set; }
    public Guid Schedule_Id { get; set; }
    public Guid? Medication_Id { get; set; }
    public decimal Order_Qty { get; set; }
    public decimal Qty { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime Status_Time { get; set; }
    public string? Cancel_Reason_Id { get; set; }
    public Guid Nurse_Code { get; set; }
    public Guid? Confirmed_Nurse_Code { get; set; }
    public DateTime? Return_Time { get; set; }
    public string? Return_Reason_Id { get; set; }
    public decimal? Return_Qty { get; set; }
    public Guid? Return_Nurse_Code { get; set; }
    public Guid? Confirmed_Return_Nurse_Code { get; set; }
    public DateTime? Waste_Time { get; set; }
    public string? Waste_Reason_Id { get; set; }
    public decimal? Waste_Qty { get; set; }
    public Guid? Waste_Nurse_Code { get; set; }
    public Guid? Confirmed_Waste_Nurse_Code { get; set; }
    public string? Waste_Remark { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "Care_Stations" (4 rows).
/// </summary>
public class CareStationRawRecord
{
    public Guid Station_Id { get; set; }
    public Guid Department_Id { get; set; }
    public string Station_Code { get; set; } = string.Empty;
    public string Station_Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? hasPatientRooms { get; set; }
    public bool? Is_Decommissioned { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "Rooms_Beds" (19 rows).
/// </summary>
public class RoomBedRawRecord
{
    public Guid Bed_Id { get; set; }
    public Guid Station_Id { get; set; }
    public string Rb_Code { get; set; } = string.Empty;
    public string Rb_Name { get; set; } = string.Empty;
    public bool? Is_Decommissioned { get; set; }
}

/// <summary>
/// Direct mapping to PostgreSQL table "Users" (Holds clinician accounts).
/// </summary>
public class UserRawRecord
{
    public Guid User_Id { get; set; }
    public string Employee_Code { get; set; } = string.Empty;
    public string Employee_Name { get; set; } = string.Empty;
    public string? Role_Type { get; set; }
    public Guid? Department_Id { get; set; }
    public Guid? Station_Id { get; set; }
}

#endregion

#region Canonical Query Result Records (Explorer 1 § 7)

/// <summary>
/// Consolidated raw record mapping output of Unified MAR Timetable Query (Explorer 1 § 7.1).
/// Joins across Schedule, Prescriptions, Encounter, Patients, Medications, Care_Stations,
/// Rooms_Beds, AMiS_Patient_Medication_Dispense, and Users.
/// </summary>
public class MarScheduleRawRecord
{
    // Patient Master
    public Guid Patient_Id { get; set; }
    public string Patient_Mrn { get; set; } = string.Empty;
    public string First_Name { get; set; } = string.Empty;
    public string Last_Name { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public DateTime Date_Of_Birth { get; set; }
    public string Allergy { get; set; } = "N";
    public string? Allergy_Desc { get; set; }
    public string DrugWarn { get; set; } = "N";
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
    public string? Rb_Code { get; set; }
    public string? Rb_Name { get; set; }

    // Prescription
    public Guid Med_Order_Id { get; set; }
    public decimal Dosage_Ordered_Value { get; set; }
    public string Dosage_Ordered_Unit { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Order_Status { get; set; } = string.Empty;
    public DateTime? Start_Datetime { get; set; }
    public DateTime? End_Datetime { get; set; }
    public string? Clinical_Indication { get; set; }
    public string? Instruction_Note { get; set; }

    // Medication Master
    public Guid? Medication_Id { get; set; }
    public string? Item_Code { get; set; }
    public string? Generic_Name { get; set; }
    public string? Display_Name { get; set; }
    public string? Atc_Code { get; set; }
    public string? Form { get; set; }
    public bool? isHighAlert { get; set; }
    public bool? isRefrigerationRequired { get; set; }
    public bool? Is_Controlled { get; set; }
    public string? Control_Level { get; set; }
    public string? Wintness_Required { get; set; }

    // Schedule
    public Guid Schedule_Id { get; set; }
    public DateTime Schedule_Date { get; set; }
    public TimeSpan Schedule_Time { get; set; }
    public decimal Calc_Dispense_Qty { get; set; }
    public string Dispense_Qty_Unit { get; set; } = string.Empty;
    public string? Schedule_Status { get; set; }
    public DateTime? Processed_Time { get; set; }
    public DateTime? Confirmed_Time { get; set; }

    // Dispense Log
    public int? Dispense_Id { get; set; }
    public string? Dispense_Status { get; set; }
    public DateTime? Dispense_Time { get; set; }
    public DateTime? Return_Time { get; set; }
    public decimal? Return_Qty { get; set; }
    public DateTime? Waste_Time { get; set; }
    public decimal? Waste_Qty { get; set; }
    public string? Cancel_Reason_Id { get; set; }
    public string? Return_Reason_Id { get; set; }
    public string? Waste_Reason_Id { get; set; }

    // Operator Names
    public string? Dispense_Nurse_Name { get; set; }
    public string? Confirmed_Nurse_Name { get; set; }

    // Computed Status from SQL CASE
    public string Computed_Status { get; set; } = string.Empty;

    public string PatientFullName => $"{Last_Name} {First_Name}".Trim();
    public DateTime FullScheduledDateTime => Schedule_Date.Date.Add(Schedule_Time);
}

/// <summary>
/// Consolidated raw record mapping output of Patient Directory Master-Detail Query (Explorer 1 § 7.2).
/// Summarizes active inpatient encounters with prescription and scheduled dose metrics.
/// </summary>
public class PatientEncounterRawRecord
{
    public Guid Patient_Id { get; set; }
    public string Patient_Mrn { get; set; } = string.Empty;
    public string First_Name { get; set; } = string.Empty;
    public string Last_Name { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public DateTime Date_Of_Birth { get; set; }
    public decimal? Weight_Kg { get; set; }
    public decimal? Height_Cm { get; set; }
    public string Allergy { get; set; } = "N";
    public string? Allergy_Desc { get; set; }
    public string DrugWarn { get; set; } = "N";
    public string? DrugWarn_Desc { get; set; }

    public Guid Encounter_Id { get; set; }
    public string Case_No { get; set; } = string.Empty;
    public DateTime Admission_Date { get; set; }
    public DateTime? Discharge_Date { get; set; }
    public string Encounter_Status { get; set; } = string.Empty;
    public string? Attending_Physician { get; set; }

    public Guid? Station_Id { get; set; }
    public string? Station_Code { get; set; }
    public string? Station_Name { get; set; }
    public Guid? Bed_Id { get; set; }
    public string? Rb_Code { get; set; }
    public string? Rb_Name { get; set; }

    public int Total_Prescriptions { get; set; }
    public int Total_Scheduled_Doses { get; set; }
    public int Dispensed_Doses { get; set; }
    public int Pending_Doses { get; set; }

    public string PatientFullName => $"{Last_Name} {First_Name}".Trim();
}

/// <summary>
/// Direct mapping to Top KPI Summary Query (Explorer 1 § 7.3).
/// </summary>
public class KpiRawCounts
{
    public int total_inpatients { get; set; }
    public int total_active_orders { get; set; }
    public int total_dispensed { get; set; }
    public int due_pending_orders { get; set; }
    public int total_pending_orders { get; set; }
}

#endregion
