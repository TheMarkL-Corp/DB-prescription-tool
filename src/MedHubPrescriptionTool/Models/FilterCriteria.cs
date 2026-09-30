using System;

namespace MedHubPrescriptionTool.Models;

/// <summary>
/// Predefined Date Filter options for Clinical MAR timetable.
/// </summary>
public static class DateFilterOptions
{
    public const string All = "All";
    public const string Today = "Today";
    public const string Past = "Past";
    public const string Future = "Future";
    public const string Custom = "Custom";
}

/// <summary>
/// Predefined Status Filter options for Clinical MAR timetable.
/// </summary>
public static class StatusFilterOptions
{
    public const string All = "All";
    public const string Dispensed = "Dispensed";
    public const string Pending = "Pending";
    public const string PendingDue = "Pending (Due)";
    public const string PendingFuture = "Pending (Future)";
    public const string Returned = "Returned";
    public const string Wasted = "Wasted";
    public const string Cancelled = "Cancelled";
    public const string ReturnedWastedCancelled = "Returned/Wasted/Cancelled";
}

/// <summary>
/// MAR Timetable sorting fields.
/// </summary>
public enum MarSortField
{
    ScheduleDateTime,
    PatientMrn,
    PatientName,
    StationCode,
    BedCode,
    MedicationName,
    DispenseStatus
}

/// <summary>
/// Sort direction.
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending
}
