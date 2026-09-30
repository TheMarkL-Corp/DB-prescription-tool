# MedHub DB Prescription Tool — Test Infrastructure & Quality Assurance Specification

## 1. Testing Philosophy & Strategy

The MedHub DB Prescription Tool (`DB-prescription-tool`) operates in an inpatient hospital setting where medication administration precision is vital to patient survival. An undetected software defect—such as misrepresenting an unverified dose as dispensed, failing to present a penicillin anaphylaxis allergy alert, or dropping leading zeros from a patient's Medical Record Number (MRN) during Excel export—can directly lead to toxic overdosing, missed medication rounds, or dangerous drug interactions.

### 1.1 Core Principles
- **Opaque-Box Verification**: Tests validate the application through its observable public contracts, service APIs, database invariants, and exported artifact boundaries without coupling to volatile private implementation details.
- **Requirement-Driven Assertions**: Every test case directly traces to explicit requirements in `ORIGINAL_REQUEST.md` and feature definitions in `PROJECT.md`.
- **Zero-Mutation Clinical Invariant**: Testing explicitly enforces that the MedHub PostgreSQL database (`amisdbV2`) is strictly immutable from the application layer. Every connection and transaction must reject write operations (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`).
- **Headless Execution & Self-Containment**: The entire test suite executes in a headless CI/CD environment without requiring a running display server, WebAssembly browser instance, or desktop installation of Microsoft Excel. Spreadsheets are verified in-memory using ClosedXML document inspection.
- **Fail-Safe Clinical Invariants**: Ambiguous or null statuses must never default to "Dispensed". Cancelled, returned, and wasted medications take absolute precedence over previous confirmation timestamps.

---

## 2. 4-Tier Test Suite Architecture

The test harness is organized into four distinct tiers, totaling 100 comprehensive test cases:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        E2E Test Suite (100 Tests)                      │
├────────────────────────────────────────────────────────────────────────┤
│ Tier 1: Feature Coverage (40 Tests)                                   │
│ - 5 dedicated test cases across each of the 8 core application features│
├────────────────────────────────────────────────────────────────────────┤
│ Tier 2: Boundary & Corner Cases (40 Tests)                             │
│ - Stress testing, null safety, timeout resilience, write protection    │
├────────────────────────────────────────────────────────────────────────┤
│ Tier 3: Pairwise & Cross-Feature Interactions (15 Tests)              │
│ - Search + Chips, Filter + Excel, Modal + Theme, Multi-View state sync  │
├────────────────────────────────────────────────────────────────────────┤
│ Tier 4: Real-World Inpatient Clinical Scenarios (5 Scenarios)          │
│ - Morning Handoff, STAT Emergency, TDM Tracking, Return Audit, Narcotic│
└────────────────────────────────────────────────────────────────────────┘
```

### Coverage Thresholds by Tier
| Tier | Description | Minimum Tests | Target Met |
|---|---|---|---|
| **Tier 1** | Core Feature Coverage (Features 1–8) | 40 | 40 tests |
| **Tier 2** | Boundary Conditions & Edge Cases | 40 | 40 tests |
| **Tier 3** | Pairwise & Cross-Feature Interactions | 15 | 15 tests |
| **Tier 4** | Real-World Inpatient Clinical Scenarios | 5 | 5 scenarios |
| **Total** | | **100** | **100 tests** |

---

## 3. Feature Inventory & Test Mapping

The test suite covers the complete application feature matrix from `PROJECT.md`:

| Feature # | Feature Name | Tier 1 Tests | Tier 2 Tests | Tier 3 Tests | Tier 4 Scenarios |
|---|---|---|---|---|---|
| **F01** | Database Read-Only Connection & Configuration | TC-F01-01..05 | TC-B01-01..05 | TC-I01..04 | Scenario 1, 2 |
| **F02** | Top KPI Metric Cards Calculation | TC-F02-01..05 | TC-B02-01..05 | TC-I02, I05 | Scenario 1, 3 |
| **F03** | Patient Overview (Master-Detail Tab 1) | TC-F03-01..05 | TC-B03-01..05 | TC-I02, I08, I09 | Scenario 1, 3 |
| **F04** | Global MAR Timetable (Tab 2) | TC-F04-01..05 | TC-B04-01..05 | TC-I01, I03, I06 | Scenario 1, 2, 4, 5 |
| **F05** | Filter Chips & Instant Fuzzy Search | TC-F05-01..05 | TC-B05-01..05 | TC-I01, I02, I04, I07 | Scenario 1, 2, 4 |
| **F06** | Medication Details Drawer / Modal | TC-F06-01..05 | TC-B06-01..05 | TC-I03, I06, I08, I10, I14 | Scenario 2, 5 |
| **F07** | Smart Multi-Sheet Excel Export (ClosedXML) | TC-F07-01..05 | TC-B07-01..05 | TC-I04, I07, I09, I10, I15 | Scenario 1, 4 |
| **F08** | Clinical Light & Dark Mode Toggle | TC-F08-01..05 | TC-B08-01..05 | TC-I11..I15 | Scenario 5 |

---

## 4. Test Runner Invocation & Commands

The test suite is built on **xUnit.net v2.9**, **FluentAssertions v6.12**, **Npgsql v9.0**, and **ClosedXML v0.104/0.105**.

### 4.1 Running All Tests
```powershell
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --logger "console;verbosity=normal"
```

### 4.2 Running Specific Tiers
```powershell
# Run Tier 1: Feature Coverage Tests (40 tests)
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --filter "FullyQualifiedName~Tier1_FeatureCoverageTests"

# Run Tier 2: Boundary & Corner Cases (40 tests)
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --filter "FullyQualifiedName~Tier2_BoundaryTests"

# Run Tier 3: Pairwise Interaction Tests (15 tests)
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --filter "FullyQualifiedName~Tier3_PairwiseTests"

# Run Tier 4: Clinical Shift Scenario Tests (5 scenarios)
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --filter "FullyQualifiedName~Tier4_RealWorldScenarioTests"
```

### 4.3 Running by Trait / Category
```powershell
# Run all Read-Only safety tests
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --filter "Category=ReadOnlySafety"

# Run all Excel export integrity tests
dotnet test tests/MedHubPrescriptionTool.Tests/MedHubPrescriptionTool.Tests.csproj --filter "Category=ExcelExport"
```

---

## 5. Test Directory Layout

```
tests/
└── MedHubPrescriptionTool.Tests/
    ├── MedHubPrescriptionTool.Tests.csproj       # Project configuration (net9.0, packages)
    ├── TestHelpers/
    │   ├── TestDatabaseContext.cs                # Npgsql connection provider, read-only guards
    │   ├── TestExcelHelper.cs                    # ClosedXML headless workbook inspector
    │   └── ClinicalTestModels.cs                 # Clinical DTOs, filter criteria & calculation fixtures
    ├── Tier1_FeatureCoverageTests.cs             # 40 tests covering Features 1-8
    ├── Tier2_BoundaryTests.cs                    # 40 tests covering edge cases & write prevention
    ├── Tier3_PairwiseTests.cs                    # 15 tests covering cross-feature interactions
    └── Tier4_RealWorldScenarioTests.cs           # 5 real-world inpatient clinical shift scenarios
```

---

## 6. Authoritative Baseline Reference Values (Live amisdbV2)

For all deterministic tests asserting against the MedHub database on `localhost:5433`:
- **Database Name**: `amisdbV2` (case-sensitive)
- **Total Inpatients Admitted**: `15`
- **Total Inpatient Encounters**: `16` (15 active, 1 discharged)
- **Total Active Prescriptions**: `31`
- **Total Scheduled Administration Doses**: `385`
- **Confirmed / Dispensed Doses**: `13`
- **Pending Administration Doses**: `370` (286 due + 84 future)
- **Returned Doses**: `1` (Return reason `02` Patient Fasting / NPO)
- **Wasted Doses**: `1` (Waste quantity `1.00`)
- **Care Stations**: `4` (`8A`, `5A`, `ICU`, `OR`)
- **Patient Rooms & Beds**: `19` beds
- **Medications Catalog**: `114` drugs
- **High-Alert Drugs**: `1` (`EVOR2` Shoren Suppositories)
- **Refrigerated Drugs**: `4` (Insulins: `IHNTR`, `IHNMI`, `IHNRA`, `IHNRY`)
- **Controlled Drugs**: `8` (`OXANA`, `OXYNO001`, `OENOD001`, `IPROP`, `IMID5`, `ORIVO`, etc.)
