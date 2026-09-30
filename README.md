# MedHub Inpatient Prescription & MAR Workstation (.NET 9 Portable)

A standalone, portable Windows desktop application designed for healthcare providers, attending physicians, and pharmacists to safely review, search, filter, and inspect inpatient medication prescriptions and administration schedules from the MedHub PostgreSQL database (`amisdbV2`) in strict read-only mode, with rich clinical data visualization and multi-sheet Excel export capabilities.

---

## 🚀 Key Highlights & Architectural Guarantees

- **True Portability**: Self-contained single-file Windows executable (`publish/MedHubPrescriptionTool.exe`, ~38.6 MB). Bundles all .NET 9 runtimes, native WebView2/Photino libraries, ClosedXML, and Npgsql. Zero loose DLLs and zero runtime installation prerequisites on the user's machine.
- **Strict Read-Only Safety**: Enforces PostgreSQL `default_transaction_read_only=on` at the connection and transaction level (`READ ONLY`). Data integrity is strictly protected; any attempted `INSERT`, `UPDATE`, or `DELETE` is immediately rejected at the protocol layer with `PostgresException` (SqlState 25006).
- **Comprehensive Clinical Consolidation**: Consolidates 8 core database tables (`Patients`, `Encounter`, `Prescriptions`, `Schedule`, `Medications`, `AMiS_Patient_Medication_Dispense`, `Care_Stations`, `Rooms_Beds`) into a unified Medication Administration Record (MAR).
- **Accurate Dispensing Status State Machine**: Resolves past, present, and future administration schedules, correctly cross-referencing `AMiS_Patient_Medication_Dispense` and `Schedule.Confirmed_Time` to flag **Dispensed** (`Status = 'Y'`), **Pending (Due)**, **Pending (Future)**, **Returned**, **Wasted**, or **Cancelled**.
- **Modern Healthcare UI/UX (`ui-ux-pro-max`)**: Medical Teal (`#0891B2`) and Emerald (`#059669`) color system, WCAG AA compliant 4.5:1 text contrast, prominent allergy alert banners, high-alert drug warnings, and a seamless Dark/Light theme toggle for night-shift ward environments.
- **Smart Multi-Sheet Excel Export**: Generates professional, styled 2-sheet `.xlsx` clinical reports via ClosedXML with timestamped filenames (`MedHub_Prescriptions_YYYYMMDD_HHmm.xlsx`).
- **100% Test Pass Rate**: 339 / 339 automated unit, boundary, concurrency, and clinical scenario tests passing; 29 / 29 live database verification checks passing.

---

## 🖥️ Application Features & Layout

### 1. Top Clinical KPI Metric Cards
- **Total Inpatients**: Distinct count of currently admitted patients across all hospital wards.
- **Total Active Orders**: Count of ongoing clinical prescription regimens.
- **Dispensed Doses**: Confirmed administrations logged in the pharmacy system.
- **Due / Pending Doses**: Administrations due now or upcoming in future shifts.

### 2. Tab 1: Patient Directory (Master-Detail Overview)
- Browse patients grouped and filterable by **Care Station** (e.g., `5A病房`, `8A病房`, `內科加護病房`) and **Bed** (e.g., `01室01床`).
- Prominent patient header with MRN, Admission Date, Case No, Sex, Age, Attending Physician, and highlighted **Allergy & Drug Warning Banners** (e.g., NPO / Fasting status, drug sensitivities).
- Consolidated prescription history and medication administration timelines per patient.

### 3. Tab 2: Global MAR Timetable (Unified Shift View)
- Master chronological table of all scheduled doses across the hospital.
- **Temporal Filter Chips**: `All`, `Today`, `Past / Overdue`, `Future`.
- **Status Filter Chips**: `All`, `Dispensed`, `Pending (Due)`, `Pending (Future)`, `Exceptions` (Returned / Wasted / Cancelled).
- **Safety Highlights**: Tagged badges for `High Alert`, `Controlled Substance` (Schedule I-IV), and `Refrigeration Required`.
- **Instant Multi-Field Search**: Fuzzy token search across Patient MRN, Name, Bed, Care Station, Medication Code, Display Name, Generic Name, ATC Code, Attending Physician, and Administering Nurse.

### 4. Slide-Out Medication Details Drawer
- Click any medication name to open a clinical monograph showing:
  - Form & Route (e.g., `TAB`, `PO`, `IV`, `BOT`)
  - Strength & Units (e.g., `0.5 MG`, `5 MG`)
  - ATC Code & Primary Barcode
  - Controlled Drug Level & Witness Requirements
  - Clinical Indications & Physician Instruction Notes

### 5. In-App Database Settings Modal
- Access via the **Settings (⚙️)** button in the header.
- View or reconfigure MedHub database host IP, port (default `5433`), database name (`amisdbV2`), username, and password.
- Built-in **Test Connection** button validates connectivity before changes are saved to `appsettings.json`.

### 6. Excel Report Download
- Click **Export Excel** in the header to download a two-sheet clinical workbook:
  - **Sheet 1 ("MAR Schedule & Dispense Log")**: 17 columns detailing patient, station, bed, drug, dose, scheduled datetime, dispense status, dispensed quantity, timestamp, and nurse.
  - **Sheet 2 ("Active Prescriptions Overview")**: 16 columns detailing inpatient prescriptions, case numbers, physicians, dosage, frequency, route, clinical indications, and start/end dates.

---

## 📂 File & Directory Structure

```text
DB-prescription-tool/
├── publish/                                     <-- Portable Application Distribution
│   ├── MedHubPrescriptionTool.exe               <-- Standalone Executable (~38.6 MB, win-x64)
│   ├── appsettings.json                         <-- Operator Database Configuration
│   └── wwwroot/                                 <-- Embedded Web Interface Assets
│       ├── index.html
│       ├── css/clinical-theme.css
│       ├── js/app.js
│       ├── js/ipc-bridge.js
│       └── assets/icons.svg
│
├── src/MedHubPrescriptionTool/                  <-- Core .NET 9 Application Source
│   ├── Configuration/                           <-- AppConfigurationManager & Connection Settings
│   ├── Data/                                    <-- DatabaseRepository (Read-Only Npgsql/Dapper)
│   ├── Host/                                    <-- PhotinoAppHost & IPC Message Dispatcher
│   ├── Models/                                  <-- Domain Models & DTOs
│   ├── Services/                                <-- ClinicalDataService & ClosedXML ExcelExportService
│   ├── wwwroot/                                 <-- UI Source Files
│   └── Program.cs                               <-- Entry point (GUI, --verify, --export)
│
├── tests/MedHubPrescriptionTool.Tests/          <-- 339-Test Automated Verification Suite
│   ├── Tier1_FeatureCoverageTests.cs
│   ├── Tier2_BoundaryTests.cs
│   ├── Tier3_PairwiseTests.cs
│   ├── Tier4_RealWorldScenarioTests.cs
│   ├── Milestone1_AdversarialReadOnlyTests.cs
│   ├── Milestone2_AdversarialKpiAndStatusTests.cs
│   ├── Milestone2_AdversarialSearchAndConcurrencyTests.cs
│   └── Milestone4_ClinicalUiAndPhotinoHostTests.cs
│
├── DB reference/                                <-- Database Schema Reference & APIs
│   ├── Table_Layout_20260922.xlsx
│   ├── medTRx Web API_V2.09.pdf
│   └── medTRx Web API_V2.10.pdf
│
└── README.md
```

---

## 🏃 Quick Start: Running the Application

### Option A: Launch the Desktop GUI
Simply run or double-click the portable executable:
```powershell
.\publish\MedHubPrescriptionTool.exe
```
The application opens a desktop window, automatically connects to `amisdbV2` on `localhost:5433`, and renders the clinical dashboard.

### Option B: Headless Automated Verification
To verify database connectivity, read-only mutation blocking, and clinical data integrity without opening a window:
```powershell
.\publish\MedHubPrescriptionTool.exe --verify
```
*Outputs a detailed test summary of all 29 milestone checks with exit code 0.*

### Option C: Direct Excel Export via Command Line
To export the latest prescription orders directly to an Excel spreadsheet without launching the GUI:
```powershell
.\publish\MedHubPrescriptionTool.exe --export .
```
*Generates a timestamped `.xlsx` report in the specified directory.*

---

## 🧪 Automated Test Suite Execution

To run the complete automated test suite (339 tests):
```powershell
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj
```

**Test Summary**:
- **339 Passed**, **0 Failed**, **0 Skipped** (100% Pass Rate).
- Execution duration: ~14 seconds.
- Validates read-only invariants, multi-threaded concurrency (50 threads), path traversal protection, OpenXML structure, and UI IPC schemas.

---

## 🔒 Security & Data Integrity Compliance

1. **Protocol-Enforced Read-Only Mode**: Every database transaction executes under `SET TRANSACTION READ ONLY`.
2. **Path Traversal Protection**: The Photino custom scheme handler strictly verifies that URI requests remain within the canonical `wwwroot` directory, rejecting rooted paths, drive letters, and parent traversal attempts (`..`).
3. **Leading Zero Preservation**: Patient MRNs (e.g., `00075655`, `0439449`) and Case Numbers are formatted explicitly as Text (`@`) in ClosedXML export to ensure hospital identifiers never lose leading zeros when opened in Microsoft Excel.
