using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MedHubPrescriptionTool.Data;
using MedHubPrescriptionTool.Models;

namespace MedHubPrescriptionTool.Services;

/// <summary>
/// Core Clinical Data Consolidation, Status State Machine, and Business Model Service.
/// Implements IClinicalDataService with strict NULL-safe status logic and sub-16ms fuzzy filtering.
/// </summary>
public class ClinicalDataService : IClinicalDataService
{
    private readonly IDatabaseRepository _repository;
    private ClinicalDataSnapshot? _cachedSnapshot;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ClinicalDataService(IDatabaseRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Returns the currently cached snapshot, or loads a new one if none is cached yet.
    /// Uses double-checked locking on _refreshLock to prevent cache stampedes during concurrent cold starts.
    /// </summary>
    public async Task<ClinicalDataSnapshot> GetCurrentSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (_cachedSnapshot != null && !forceRefresh)
        {
            return _cachedSnapshot;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedSnapshot != null && !forceRefresh)
            {
                return _cachedSnapshot;
            }

            return await LoadClinicalSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ClinicalDataSnapshot> LoadClinicalSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadClinicalSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<ClinicalDataSnapshot> LoadClinicalSnapshotCoreAsync(CancellationToken cancellationToken)
    {
            // 1. Fetch raw datasets from repository
            var marRecords = await _repository.GetMarScheduleDataAsync(cancellationToken).ConfigureAwait(false);
            var patientEncounterRecords = await _repository.GetPatientEncountersAsync(cancellationToken).ConfigureAwait(false);
            var medicationRecords = await _repository.GetAllMedicationsAsync(cancellationToken).ConfigureAwait(false);
            var stationRecords = await _repository.GetCareStationsAsync(cancellationToken).ConfigureAwait(false);
            var bedRecords = await _repository.GetRoomsBedsAsync(cancellationToken).ConfigureAwait(false);
            var prescriptionRecords = await _repository.GetAllPrescriptionsAsync(cancellationToken).ConfigureAwait(false);
            var patientRecords = await _repository.GetAllPatientsAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.Now;

            // 2. Build Medication Catalog DTOs
            var medicationDtos = medicationRecords.Select(m => new MedicationDetailDto
            {
                Medication_Id = m.Medication_Id,
                Item_Code = m.Item_Code,
                Display_Name = m.Display_Name,
                Generic_Name = m.Generic_Name,
                Atc_Code = m.Atc_Code,
                Form = m.Form,
                Strength_Value = m.Strength_Value,
                Strength_Unit = m.Strength_Unit,
                Primary_Barcode_Value = m.Primary_Barcode_Value,
                Primary_Barcode_Label = m.Primary_Barcode_Label,
                Packaging_Type = m.Packaging_Type,
                Dispense_Form = m.Dispense_Form,
                Vol_Content = m.Vol_Content,
                Is_ADC = m.Is_ADC,
                Is_Controlled = m.Is_Controlled == true,
                Control_Level = m.Control_Level,
                Wintness_Required = m.Wintness_Required,
                isHighAlert = m.isHighAlert == true,
                isRefrigerationRequired = m.isRefrigerationRequired == true,
                isBlindCountRequired = m.isBlindCountRequired == true,
                Is_Divisible = m.Is_Divisible == true,
                Is_Active = m.Is_Active ?? true,
                Storage_Note = m.Storage_Note,
                Storage_URL = m.Storage_URL,
                Element_Name = m.Element_Name,
                Drug_License_No = m.Drug_License_No,
                Manufacturer_Code = m.Manufacturer_Code,
                Reporting_Unit = m.Reporting_Unit,
                Description = m.Description,
                ActivePrescriptionsCount = 0
            }).ToList();

            var medByCode = medicationDtos
                .Where(m => !string.IsNullOrEmpty(m.Item_Code))
                .GroupBy(m => m.Item_Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 3. Build Unified MAR Timetable DTOs
            var marItems = marRecords.Select(r =>
            {
                var status = ClinicalStatusEvaluator.EvaluateStatus(r, now);

                return new MarScheduleItemDto
                {
                    Schedule_Id = r.Schedule_Id,
                    ScheduleId = BitConverter.ToInt64(r.Schedule_Id.ToByteArray(), 0) & 0x7FFFFFFFFFFFFFFF,
                    Med_Order_Id = r.Med_Order_Id,
                    Encounter_Id = r.Encounter_Id,
                    Patient_Id = r.Patient_Id,
                    Patient_Mrn = r.Patient_Mrn,
                    Patient_Name = r.PatientFullName,
                    Patient_Sex = r.Sex,
                    Patient_Dob = r.Date_Of_Birth,
                    Case_No = r.Case_No,
                    Station_Code = r.Station_Code,
                    Station_Name = r.Station_Name,
                    Bed_Code = r.Rb_Code,
                    Bed_Name = r.Rb_Name,
                    Attending_Physician = r.Attending_Physician,
                    Medication_Id = r.Medication_Id,
                    Item_Code = r.Item_Code ?? string.Empty,
                    Generic_Name = r.Generic_Name,
                    Display_Name = r.Display_Name ?? r.Item_Code ?? string.Empty,
                    Atc_Code = r.Atc_Code,
                    Form = r.Form,
                    Dosage_Ordered_Value = r.Dosage_Ordered_Value,
                    Dosage_Ordered_Unit = r.Dosage_Ordered_Unit,
                    Frequency = r.Frequency,
                    Route = r.Route,
                    Order_Status = r.Order_Status,
                    Clinical_Indication = r.Clinical_Indication,
                    Instruction_Note = r.Instruction_Note,
                    Order_Start_Datetime = r.Start_Datetime,
                    Order_End_Datetime = r.End_Datetime,
                    Schedule_Date = r.Schedule_Date,
                    Schedule_Time = r.Schedule_Time,
                    Calc_Dispense_Qty = r.Calc_Dispense_Qty,
                    Dispense_Qty_Unit = r.Dispense_Qty_Unit,
                    Schedule_Status = r.Schedule_Status,
                    Processed_Time = r.Processed_Time,
                    Confirmed_Time = r.Confirmed_Time,
                    Dispense_Id = r.Dispense_Id,
                    Dispense_Status = status,
                    Dispense_Time = r.Dispense_Time,
                    Dispensed_Qty = r.Calc_Dispense_Qty > 0 ? r.Calc_Dispense_Qty : null,
                    Status_Time = r.Dispense_Time ?? r.Confirmed_Time ?? r.Processed_Time,
                    Return_Time = r.Return_Time,
                    Return_Qty = r.Return_Qty,
                    Return_Reason_Id = r.Return_Reason_Id,
                    Return_Reason_Name = ClinicalStatusEvaluator.GetReturnReasonDescription(r.Return_Reason_Id),
                    Waste_Time = r.Waste_Time,
                    Waste_Qty = r.Waste_Qty,
                    Waste_Reason_Id = r.Waste_Reason_Id,
                    Waste_Reason_Name = ClinicalStatusEvaluator.GetWasteReasonDescription(r.Waste_Reason_Id),
                    Cancel_Reason_Id = r.Cancel_Reason_Id,
                    Cancel_Reason_Name = ClinicalStatusEvaluator.GetCancelReasonDescription(r.Cancel_Reason_Id),
                    Dispense_Nurse_Name = r.Dispense_Nurse_Name,
                    Confirmed_Nurse_Name = r.Confirmed_Nurse_Name,
                    isHighAlert = r.isHighAlert == true,
                    isRefrigerationRequired = r.isRefrigerationRequired == true,
                    Is_Controlled = r.Is_Controlled == true,
                    Control_Level = r.Control_Level,
                    Wintness_Required = r.Wintness_Required,
                    Allergy = r.Allergy,
                    Allergy_Desc = r.Allergy_Desc,
                    DrugWarn = r.DrugWarn,
                    DrugWarn_Desc = r.DrugWarn_Desc
                };
            }).ToList();

            // 4. Build Active Prescriptions Overview DTOs (Sheet 2)
            var schedulesByOrder = marItems
                .GroupBy(s => s.Med_Order_Id)
                .ToDictionary(g => g.Key, g => g.ToList());

            var encounterMap = patientEncounterRecords
                .ToDictionary(e => e.Encounter_Id, e => e);

            var patientMap = patientRecords
                .ToDictionary(p => p.Patient_Id, p => p);

            var activePrescriptions = new List<ActivePrescriptionDto>();

            // Aggregate active orders (Order_Status = '01')
            foreach (var pr in prescriptionRecords.Where(p => string.Equals(p.Order_Status, "01", StringComparison.OrdinalIgnoreCase)))
            {
                encounterMap.TryGetValue(pr.Encounter_Id, out var enc);
                PatientRawRecord? pat = null;
                if (enc != null)
                {
                    patientMap.TryGetValue(enc.Patient_Id, out pat);
                }

                MedicationDetailDto? med = null;
                if (pr.Medication_Id.HasValue)
                {
                    med = medicationDtos.FirstOrDefault(m => m.Medication_Id == pr.Medication_Id.Value);
                }

                schedulesByOrder.TryGetValue(pr.Med_Order_Id, out var ordSchedules);
                var ordSchedList = ordSchedules ?? [];

                int totalDoses = ordSchedList.Count;
                int dispensedDoses = ordSchedList.Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusDispensed, StringComparison.OrdinalIgnoreCase));
                int pendingDoses = ordSchedList.Count(s => s.Dispense_Status?.StartsWith("Pending", StringComparison.OrdinalIgnoreCase) == true);

                var activeDto = new ActivePrescriptionDto
                {
                    Med_Order_Id = pr.Med_Order_Id,
                    Encounter_Id = pr.Encounter_Id,
                    Patient_Id = enc?.Patient_Id ?? pat?.Patient_Id ?? Guid.Empty,
                    Patient_Mrn = enc?.Patient_Mrn ?? pat?.Patient_Mrn ?? string.Empty,
                    Case_No = enc?.Case_No ?? string.Empty,
                    Patient_Name = enc?.PatientFullName ?? pat?.FullName ?? string.Empty,
                    Care_Station = enc?.Station_Name ?? string.Empty,
                    Station_Code = enc?.Station_Code ?? string.Empty,
                    Room_Bed = enc?.Rb_Name ?? string.Empty,
                    Bed_Code = enc?.Rb_Code ?? string.Empty,
                    Attending_Physician = enc?.Attending_Physician ?? string.Empty,
                    Medication_Id = pr.Medication_Id,
                    Medication_Code = med?.Item_Code ?? string.Empty,
                    Medication_Name = med?.Display_Name ?? string.Empty,
                    Generic_Name = med?.Generic_Name,
                    Atc_Code = med?.Atc_Code,
                    Form = med?.Form,
                    Dosage_Ordered_Value = pr.Dosage_Ordered_Value,
                    Dosage_Ordered_Unit = pr.Dosage_Ordered_Unit,
                    Frequency = pr.Frequency,
                    Route = pr.Route,
                    Clinical_Indication = pr.Clinical_Indication,
                    Instruction_Note = pr.Instruction_Note,
                    Start_Datetime = pr.Start_Datetime,
                    End_Datetime = pr.End_Datetime,
                    Order_Status = pr.Order_Status,
                    Total_Sched_Doses = totalDoses,
                    Dispensed_Doses = dispensedDoses,
                    Pending_Doses = pendingDoses,
                    isHighAlert = med?.isHighAlert == true,
                    isRefrigerationRequired = med?.isRefrigerationRequired == true,
                    Is_Controlled = med?.Is_Controlled == true,
                    Control_Level = med?.Control_Level
                };

                activePrescriptions.Add(activeDto);

                if (med != null)
                {
                    med.ActivePrescriptionsCount++;
                    if (string.IsNullOrEmpty(med.Indication) && !string.IsNullOrEmpty(pr.Clinical_Indication))
                    {
                        med.Indication = pr.Clinical_Indication;
                    }
                    if (string.IsNullOrEmpty(med.Instructions) && !string.IsNullOrEmpty(pr.Instruction_Note))
                    {
                        med.Instructions = pr.Instruction_Note;
                    }
                }
            }

            // 5. Build Master-Detail Patient Overview DTOs (Tab 1)
            var activeEncounters = patientEncounterRecords
                .Where(e => (string.Equals(e.Encounter_Status, "1", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(e.Encounter_Status, "01", StringComparison.OrdinalIgnoreCase)) &&
                             !e.Discharge_Date.HasValue)
                .ToList();

            var patientDtos = new List<PatientOverviewDto>();
            foreach (var pe in activeEncounters)
            {
                var patOrders = activePrescriptions.Where(p => p.Encounter_Id == pe.Encounter_Id).ToList();
                var patSchedules = marItems.Where(s => s.Encounter_Id == pe.Encounter_Id).OrderBy(s => s.ScheduledDateTime).ToList();

                var overviewDto = new PatientOverviewDto
                {
                    Patient_Id = pe.Patient_Id,
                    Patient_Mrn = pe.Patient_Mrn,
                    First_Name = pe.First_Name,
                    Last_Name = pe.Last_Name,
                    Sex = pe.Sex,
                    Date_Of_Birth = pe.Date_Of_Birth,
                    Weight_Kg = pe.Weight_Kg,
                    Height_Cm = pe.Height_Cm,
                    Allergy = pe.Allergy,
                    Allergy_Desc = pe.Allergy_Desc,
                    DrugWarn = pe.DrugWarn,
                    DrugWarn_Desc = pe.DrugWarn_Desc,
                    Encounter_Id = pe.Encounter_Id,
                    Case_No = pe.Case_No,
                    Admission_Date = pe.Admission_Date,
                    Discharge_Date = pe.Discharge_Date,
                    Encounter_Status = pe.Encounter_Status,
                    Attending_Physician = pe.Attending_Physician,
                    Station_Id = pe.Station_Id,
                    Station_Code = pe.Station_Code,
                    Station_Name = pe.Station_Name,
                    Bed_Id = pe.Bed_Id,
                    Bed_Code = pe.Rb_Code,
                    Bed_Name = pe.Rb_Name,
                    Total_Prescriptions = patOrders.Count,
                    Total_Scheduled_Doses = patSchedules.Count,
                    Dispensed_Doses = patSchedules.Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusDispensed, StringComparison.OrdinalIgnoreCase)),
                    Pending_Doses = patSchedules.Count(s => s.Dispense_Status?.StartsWith("Pending", StringComparison.OrdinalIgnoreCase) == true),
                    ActivePrescriptions = patOrders,
                    RecentSchedules = patSchedules
                };

                patientDtos.Add(overviewDto);
            }

            var patByMrn = patientDtos
                .Where(p => !string.IsNullOrEmpty(p.Patient_Mrn))
                .GroupBy(p => p.Patient_Mrn, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var stationByCode = stationRecords
                .Where(s => !string.IsNullOrEmpty(s.Station_Code))
                .GroupBy(s => s.Station_Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // 6. Assemble Immutable Snapshot
            var snapshot = new ClinicalDataSnapshot
            {
                Patients = patientDtos.AsReadOnly(),
                MarSchedules = marItems.AsReadOnly(),
                ActivePrescriptions = activePrescriptions.AsReadOnly(),
                Medications = medicationDtos.AsReadOnly(),
                Stations = stationRecords.AsReadOnly(),
                Beds = bedRecords.AsReadOnly(),
                LastRefreshedAt = now,
                MedicationByCode = medByCode,
                PatientByMrn = patByMrn,
                StationByCode = stationByCode
            };

            // 7. Calculate KPI metrics
            snapshot.KpiSummary = ComputeKpiSummary(snapshot);

            _cachedSnapshot = snapshot;
            return snapshot;
    }

    /// <inheritdoc />
    public KpiSummaryDto ComputeKpiSummary(ClinicalDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        int inpatients = snapshot.Patients
            .Select(p => p.Patient_Id)
            .Distinct()
            .Count();

        int activeOrders = snapshot.ActivePrescriptions.Count;

        int dispensed = snapshot.MarSchedules
            .Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusDispensed, StringComparison.OrdinalIgnoreCase));

        int duePending = snapshot.MarSchedules
            .Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusPendingDue, StringComparison.OrdinalIgnoreCase));

        int futurePending = snapshot.MarSchedules
            .Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusPendingFuture, StringComparison.OrdinalIgnoreCase));

        int totalPending = duePending + futurePending;

        int returned = snapshot.MarSchedules
            .Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusReturned, StringComparison.OrdinalIgnoreCase));

        int wasted = snapshot.MarSchedules
            .Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusWasted, StringComparison.OrdinalIgnoreCase));

        int cancelled = snapshot.MarSchedules
            .Count(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusCancelled, StringComparison.OrdinalIgnoreCase));

        int highAlert = snapshot.MarSchedules
            .Count(s => s.isHighAlert);

        int controlled = snapshot.MarSchedules
            .Count(s => s.Is_Controlled);

        return new KpiSummaryDto
        {
            TotalInpatients = inpatients,
            TotalActiveOrders = activeOrders,
            DispensedCount = dispensed,
            DuePendingOrders = duePending,
            TotalPendingOrders = totalPending,
            FuturePendingOrders = futurePending,
            ReturnedCount = returned,
            WastedCount = wasted,
            CancelledCount = cancelled,
            HighAlertCount = highAlert,
            ControlledCount = controlled,
            LastRefreshedAt = snapshot.LastRefreshedAt
        };
    }

    /// <inheritdoc />
    public List<PatientOverviewDto> GetPatientOverviewList(ClinicalDataSnapshot snapshot, string? stationFilter = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        IEnumerable<PatientOverviewDto> query = snapshot.Patients;

        if (!string.IsNullOrWhiteSpace(stationFilter) && !string.Equals(stationFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var filter = stationFilter.Trim();
            query = query.Where(p =>
                string.Equals(p.Station_Code, filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Station_Name, filter, StringComparison.OrdinalIgnoreCase) ||
                (p.Station_Id.HasValue && string.Equals(p.Station_Id.Value.ToString(), filter, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderBy(p => p.Station_Code ?? "ZZZ")
            .ThenBy(p => p.Bed_Code ?? "ZZZ")
            .ThenBy(p => p.Patient_Mrn)
            .ToList();
    }

    /// <inheritdoc />
    public List<MarScheduleItemDto> FilterMarSchedule(ClinicalDataSnapshot snapshot, ClinicalFilterCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (criteria == null) return snapshot.MarSchedules.ToList();

        IEnumerable<MarScheduleItemDto> query = snapshot.MarSchedules;
        var now = criteria.ReferenceTime ?? DateTime.Now;
        var today = now.Date;

        // 1. Station Filter
        if (!string.IsNullOrWhiteSpace(criteria.StationCode) && !string.Equals(criteria.StationCode, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var station = criteria.StationCode.Trim();
            query = query.Where(s =>
                string.Equals(s.Station_Code, station, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Station_Name, station, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Bed Filter
        if (!string.IsNullOrWhiteSpace(criteria.BedCode) && !string.Equals(criteria.BedCode, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var bed = criteria.BedCode.Trim();
            query = query.Where(s =>
                string.Equals(s.Bed_Code, bed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Bed_Name, bed, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Patient MRN Filter
        if (!string.IsNullOrWhiteSpace(criteria.PatientMrn))
        {
            var mrn = criteria.PatientMrn.Trim();
            query = query.Where(s => string.Equals(s.Patient_Mrn, mrn, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Date Filter: "All", "Today", "Past", "Future", "Custom"
        if (!string.IsNullOrWhiteSpace(criteria.DateFilter) && !string.Equals(criteria.DateFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(criteria.DateFilter, DateFilterOptions.Today, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.Schedule_Date.Date == today);
            }
            else if (string.Equals(criteria.DateFilter, DateFilterOptions.Past, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(criteria.DateFilter, "Past / Overdue", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.ScheduledDateTime < now);
            }
            else if (string.Equals(criteria.DateFilter, DateFilterOptions.Future, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.ScheduledDateTime > now);
            }
            else if (string.Equals(criteria.DateFilter, DateFilterOptions.Custom, StringComparison.OrdinalIgnoreCase))
            {
                if (criteria.CustomStartDate.HasValue)
                    query = query.Where(s => s.Schedule_Date.Date >= criteria.CustomStartDate.Value.Date);
                if (criteria.CustomEndDate.HasValue)
                    query = query.Where(s => s.Schedule_Date.Date <= criteria.CustomEndDate.Value.Date);
            }
        }
        else
        {
            if (criteria.CustomStartDate.HasValue)
                query = query.Where(s => s.Schedule_Date.Date >= criteria.CustomStartDate.Value.Date);
            if (criteria.CustomEndDate.HasValue)
                query = query.Where(s => s.Schedule_Date.Date <= criteria.CustomEndDate.Value.Date);
        }

        // 5. Status Filter: "All", "Dispensed", "Pending", "Pending (Due)", "Pending (Future)", "Returned", "Wasted", "Cancelled", "Returned/Wasted/Cancelled"
        if (!string.IsNullOrWhiteSpace(criteria.StatusFilter) && !string.Equals(criteria.StatusFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sf = criteria.StatusFilter.Trim();
            if (string.Equals(sf, StatusFilterOptions.Dispensed, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusDispensed, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(sf, StatusFilterOptions.Pending, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(sf, "Pending / Due", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.Dispense_Status?.StartsWith("Pending", StringComparison.OrdinalIgnoreCase) == true);
            }
            else if (string.Equals(sf, StatusFilterOptions.PendingDue, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(sf, "Due", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusPendingDue, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(sf, StatusFilterOptions.PendingFuture, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusPendingFuture, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(sf, StatusFilterOptions.Returned, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusReturned, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(sf, StatusFilterOptions.Wasted, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusWasted, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(sf, StatusFilterOptions.Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusCancelled, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(sf, StatusFilterOptions.ReturnedWastedCancelled, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(sf, "Exceptions", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s =>
                    string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusReturned, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusWasted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Dispense_Status, ClinicalStatusEvaluator.StatusCancelled, StringComparison.OrdinalIgnoreCase));
            }
        }

        // 6. Clinical Priority Flags
        if (criteria.HighAlertOnly == true)
        {
            // Filter to High-Alert or Controlled medications (Explorer 3 § 2.5)
            query = query.Where(s => s.isHighAlert || s.Is_Controlled);
        }
        if (criteria.ControlledOnly == true)
        {
            query = query.Where(s => s.Is_Controlled);
        }
        if (criteria.RefrigerationOnly == true)
        {
            query = query.Where(s => s.isRefrigerationRequired);
        }

        // 7. Instant Multi-Field Fuzzy Search
        if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
        {
            var rawQuery = criteria.SearchQuery.Trim();
            var terms = rawQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            query = query.Where(s =>
            {
                foreach (var term in terms)
                {
                    bool matchesTerm =
                        (s.Patient_Mrn != null && s.Patient_Mrn.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Patient_Name != null && s.Patient_Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Case_No != null && s.Case_No.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Bed_Code != null && s.Bed_Code.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Bed_Name != null && s.Bed_Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Station_Code != null && s.Station_Code.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Station_Name != null && s.Station_Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Item_Code != null && s.Item_Code.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Display_Name != null && s.Display_Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Generic_Name != null && s.Generic_Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Atc_Code != null && s.Atc_Code.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Clinical_Indication != null && s.Clinical_Indication.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Attending_Physician != null && s.Attending_Physician.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (s.Dispense_Nurse_Name != null && s.Dispense_Nurse_Name.Contains(term, StringComparison.OrdinalIgnoreCase));

                    if (!matchesTerm)
                        return false;
                }
                return true;
            });
        }

        return query
            .OrderByDescending(s => s.Schedule_Date)
            .ThenByDescending(s => s.Schedule_Time)
            .ThenBy(s => s.Patient_Mrn)
            .ToList();
    }

    /// <inheritdoc />
    public MedicationDetailDto? GetMedicationDetails(ClinicalDataSnapshot snapshot, string itemCode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(itemCode)) return null;

        var key = itemCode.Trim();
        if (snapshot.MedicationByCode.TryGetValue(key, out var details))
        {
            return details;
        }

        return snapshot.Medications.FirstOrDefault(m =>
            string.Equals(m.Item_Code, key, StringComparison.OrdinalIgnoreCase));
    }
}
