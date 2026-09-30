# Project: MedHub DB Prescription Tool

## Architecture
Standalone Windows desktop application (.NET 9 + Photino.NET 4.0.16 + WebView2 + ClosedXML 0.105.1 + Npgsql 9.x) providing read-only inspection, search, filtering, clinical visualization, and multi-sheet Excel export of inpatient prescriptions and dispensing schedules from MedHub PostgreSQL database (`amisdbV2`).

```
┌────────────────────────────────────────────────────────────────────────┐
│ MedHub Desktop UI (Embedded Web UI: HTML5 / CSS3 / ES6+ JS in wwwroot) │
│ - Top KPI Cards (Inpatients, Active Orders, Dispensed, Due/Pending)    │
│ - Tab 1: Master-Detail Patient Directory (by Station & Bed)            │
│ - Tab 2: Global MAR Administration Timetable with Filter Chips         │
│ - Medication Details Drawer / Modal & High-Alert / Cold-Chain Badges   │
│ - In-App DB Settings Modal & Connection Tester                         │
│ - Theme Switcher: Clean Clinical Light <-> Night-Shift Dark Mode       │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Photino IPC (window.external)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Application Core (.NET 9 C# Photino Host & Service Layer)              │
│ - PhotinoHost / CustomSchemeHandler ("app://localhost/index.html")    │
│ - AppSettings & Configuration Manager (AppContext.BaseDirectory)       │
│ - ClinicalDataService (Aggregation, KPI, Patient & MAR DTO Builders)   │
│ - ExcelExportService (ClosedXML multi-sheet formatted workbook engine) │
│ - DatabaseRepository (Npgsql strictly read-only query engine)          │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Npgsql (Read-Only Connection & Tx)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ MedHub PostgreSQL Database (amisdbV2 on 127.0.0.1:5433)                │
│ - Patients, Encounter, Prescriptions, Schedule, Medications,           │
│   AMiS_Patient_Medication_Dispense, Care_Stations, Rooms_Beds, Users   │
└────────────────────────────────────────────────────────────────────────┘
```

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Default Read-Only DB Connection | Connect on startup to 127.0.0.1:5433 / amisdbV2 / postgres / Syscom@123 with protocol read-only enforcement | M1 | R1, survey_1 |
| 2 | External appsettings.json Config | Discover & load external appsettings.json next to single-file exe via AppContext.BaseDirectory | M1 | R1, survey_2 |
| 3 | In-App Connection Settings Modal | View, edit, test, and save host, port, database, username, password with instant connection test | M1, M4 | R1, survey_1,3 |
| 4 | Strict Read-Only Database Enforcement | Connection string default_transaction_read_only=on + SET transaction_read_only=on | M1 | R1, survey_1 |
| 5 | Multi-Table Schema Joins | Join across 8 core tables (Patients, Encounter, Prescriptions, Schedule, Medications, Dispense, Stations, Beds) | M1, M2 | R2, survey_1 |
| 6 | Dispensing Status Classification | NULL-safe logic for Dispensed (d.Status='Y' or Confirmed_Time), Returned, Wasted, Cancelled, Pending Due, Pending Future | M2 | R2, survey_1 |
| 7 | Clinical Safety & Alert Badges | High-Alert (isHighAlert), Refrigeration (2-8°C), Controlled Drug Level 1-4, Allergies, Drug Warnings | M2, M4 | R2, R3, survey_1,3 |
| 8 | Top KPI Metric Cards | Instant summary: Total Inpatients, Total Active Orders, Dispensed Count, Due/Pending Count | M2, M4 | R3, survey_1,3 |
| 9 | Master-Detail Patient Overview (Tab 1) | Group/filter by Care Station & Bed, select patient, view full orders, allergies, physician, doses | M2, M4 | R3, survey_1,3 |
| 10 | Global MAR Timetable (Tab 2) | Chronological schedule table across all patients with instant filter chips & search | M2, M4 | R3, survey_1,3 |
| 11 | Instant Multi-Field Fuzzy Filtering | Filter by Patient MRN, Name, Bed, Care Station, Medication Name, Date Range, Dispense Status | M4 | R3, survey_3 |
| 12 | Medication Details Drawer/Modal | Full ATC code, generic name, indication, dosage, frequency, route, storage notes, warnings | M4 | R3, survey_1,3 |
| 13 | Clinical Light & Dark Mode Toggle | Seamless theme toggle meeting WCAG AA accessible contrast for day/night clinical shifts | M4 | R3, survey_2,3 |
| 14 | Manual Refresh with Timestamp | Explicit refresh button, fetches on startup & manual click, shows "Last synchronized: YYYY-MM-DD HH:mm:ss" | M2, M4 | R2, survey_3 |
| 15 | Multi-Sheet Excel Export Engine | ClosedXML 0.105.1 export: Sheet 1 (MAR Schedule Log) + Sheet 2 (Active Prescriptions Overview) | M3 | R4, survey_2,3 |
| 16 | Excel Clinical Styling & Naming | Medical Teal header banner, bold headers, auto-fit columns, text format for MRNs, timestamped filename | M3 | R4, survey_2,3 |
| 17 | Export Filtered vs Full Dataset | Choice to export currently filtered table records or entire database dataset | M3, M4 | R4, survey_3 |
| 18 | Standalone Single-File Windows Exe | Self-contained win-x64 single-file executable embedding all DLLs and web assets (~38MB) | M5 | R5, survey_2 |
| 19 | 4-Tier Opaque-Box E2E Test Suite | 40 Tier 1 tests, 40 Tier 2 tests, 15 Tier 3 pairwise tests, 5 Tier 4 inpatient scenarios | E2E Track, M6 | AC, survey_3 |
| 20 | Adversarial Coverage Hardening | White-box edge-case and gap verification via Challenger | M6 | AC, survey_3 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Safe Read-Only Database Access Layer | PostgreSQL connection manager, read-only session guards, appsettings.json handling, core SQL repository | none | DONE |
| M2 | Clinical Data Consolidation & Business Models | DTO models, NULL-safe status machine, KPI calculations, Master-Detail & MAR data services | M1 | DONE |
| M3 | Smart Multi-Sheet Excel Export Engine | ClosedXML 2-sheet export engine, clinical styling, date/text formatting, filename generation | M2 | DONE |
| M4 | Modern Dual-View Clinical UI/UX & Photino Host | Photino embedded window, IPC broker, Vanilla HTML/CSS/JS frontend, Dual-View tabs, KPI cards, filters, dark theme, modals | M1, M2, M3 | DONE |
| M5 | Standalone Portability & Single-File Packaging | Single-file publish configuration, self-contained win-x64 build, zero-dependency validation | M4 | DONE |
| M6 | E2E Test Verification & Hardening | 100% pass of E2E test suite (Tiers 1-4) and adversarial coverage hardening (Tier 5) | M5, E2E Track | PLANNED |



## E2E Testing Track
| # | Scope | Deliverable | Status |
|---|-------|-------------|--------|
| E2E | Requirement-driven test suite (Tiers 1-4) | `TEST_INFRA.md`, xUnit test runner, and `TEST_READY.md` | DONE |

## Interface Contracts

### DatabaseRepository ↔ ClinicalDataService
```csharp
public interface IDatabaseRepository
{
    Task<bool> TestConnectionAsync(DbConnectionSettings settings);
    Task<List<PatientRawRecord>> GetPatientMasterDataAsync();
    Task<List<MarScheduleRawRecord>> GetMarScheduleDataAsync();
    Task<KpiRawCounts> GetKpiCountsAsync();
    Task<List<CareStationRawRecord>> GetCareStationsAsync();
    Task<List<RoomBedRawRecord>> GetRoomsBedsAsync();
}
```

### ClinicalDataService ↔ UI IPC Bridge / ExcelExportService
```csharp
public interface IClinicalDataService
{
    Task<ClinicalDataSnapshot> LoadClinicalSnapshotAsync();
    KpiSummaryDto ComputeKpiSummary(ClinicalDataSnapshot snapshot);
    List<PatientOverviewDto> GetPatientOverviewList(ClinicalDataSnapshot snapshot, string? stationFilter = null);
    List<MarScheduleItemDto> FilterMarSchedule(ClinicalDataSnapshot snapshot, ClinicalFilterCriteria criteria);
    MedicationDetailDto? GetMedicationDetails(ClinicalDataSnapshot snapshot, string itemCode);
}
```

### ExcelExportService ↔ UI IPC Bridge
```csharp
public interface IExcelExportService
{
    byte[] GenerateMultiSheetExcel(
        IReadOnlyList<MarScheduleItemDto> marItems, 
        IReadOnlyList<ActivePrescriptionDto> activePrescriptions,
        string hospitalTitle = "MedHub Inpatient Clinical Administration");
    string GenerateTimestampedFilename();
}
```

### Photino IPC Protocol (Frontend ↔ Backend)
```json
// Request from Web UI (window.external.sendMessage):
{
  "requestId": "req_12345",
  "action": "loadSnapshot" | "testDbConnection" | "saveDbSettings" | "exportExcel" | "getMedicationDetail",
  "payload": { ... }
}

// Response from C# Host (window.SendWebMessage / window.external.receiveMessage):
{
  "requestId": "req_12345",
  "success": true,
  "data": { ... },
  "error": null
}
```

## Code Layout
```
c:\Users\adv\Documents\antigravity\DB-prescription-tool\
├── src\
│   └── MedHubPrescriptionTool\
│       ├── MedHubPrescriptionTool.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Configuration\
│       │   └── DbConnectionSettings.cs
│       ├── Data\
│       │   ├── DatabaseConnectionFactory.cs
│       │   ├── IDatabaseRepository.cs
│       │   └── DatabaseRepository.cs
│       ├── Models\
│       │   ├── RawRecords.cs
│       │   ├── Dtos.cs
│       │   └── FilterCriteria.cs
│       ├── Services\
│       │   ├── IClinicalDataService.cs
│       │   ├── ClinicalDataService.cs
│       │   ├── IExcelExportService.cs
│       │   └── ExcelExportService.cs
│       ├── Ipc\
│       │   ├── IpcDispatcher.cs
│       │   └── IpcMessages.cs
│       └── wwwroot\
│           ├── index.html
│           ├── css\
│           │   └── clinical-theme.css
│           ├── js\
│           │   ├── app.js
│           │   ├── ipc-bridge.js
│           │   ├── kpi-manager.js
│           │   ├── patient-view.js
│           │   ├── mar-view.js
│           │   ├── detail-modal.js
│           │   └── settings-modal.js
│           └── assets\
│               └── icons.svg
├── tests\
│   └── MedHubPrescriptionTool.Tests\
│       ├── MedHubPrescriptionTool.Tests.csproj
│       ├── Tier1_FeatureCoverageTests.cs
│       ├── Tier2_BoundaryTests.cs
│       ├── Tier3_PairwiseTests.cs
│       ├── Tier4_RealWorldScenarioTests.cs
│       └── TestHelpers\
│           └── TestDatabaseContext.cs
├── PROJECT.md
└── TEST_INFRA.md
```
