/**
 * MedHub Inpatient Prescription & MAR Workstation - Application Controller (v1.0.0)
 * Optimized for Chief Nurse & Pharmacy Workflows
 */

(function () {
    'use strict';

    // Application State
    const state = {
        snapshot: null,
        selectedPatient: null,
        activeTab: 'patients', // 'patients' | 'pharmacy' | 'mar'
        marViewMode: 'shiftBoard', // 'shiftBoard' | 'table'
        pharmacyFilter: 'all', // 'all' | 'controlled' | 'highalert' | 'refrigerated'
        pharmacySearch: '',
        marFilters: {
            search: '',
            station: '',
            date: 'all',
            status: 'all',
            highAlertOnly: false
        },
        patientSearch: '',
        patientStation: '',
        filteredMarItems: [],
        openWards: new Set(),
        currentTheme: 'light'
    };

    // DOM Elements Cache
    const elements = {};

    // Initialization
    document.addEventListener('DOMContentLoaded', () => {
        cacheElements();
        initTheme();
        bindEvents();
        loadInitialData();
    });

    function cacheElements() {
        // Navigation & Header
        elements.themeToggleBtn = document.getElementById('btnToggleTheme');
        elements.themeIcon = document.getElementById('themeIcon');
        elements.themeLabel = document.getElementById('themeLabel');
        elements.dbStatusText = document.getElementById('dbStatusText');
        elements.dbStatusBadge = document.getElementById('dbStatusBadge');
        elements.syncTimestamp = document.getElementById('syncTimestamp');
        elements.btnRefreshData = document.getElementById('btnRefreshData');
        elements.btnExportExcel = document.getElementById('btnExportExcel');
        elements.btnSettingsModal = document.getElementById('btnSettingsModal');
        elements.fileProtocolNotice = document.getElementById('fileProtocolNotice');

        // Header KPI Metrics
        elements.kpiInpatients = document.getElementById('kpiInpatients');
        elements.kpiActiveOrders = document.getElementById('kpiActiveOrders');
        elements.kpiDispensed = document.getElementById('kpiDispensed');
        elements.kpiPendingDue = document.getElementById('kpiPendingDue');
        elements.kpiDuePendingCount = document.getElementById('kpiDuePendingCount');
        elements.kpiFuturePendingCount = document.getElementById('kpiFuturePendingCount');
        elements.kpiControlledCount = document.getElementById('kpiControlledCount');
        elements.kpiReturnedWastedCount = document.getElementById('kpiReturnedWastedCount');

        // 3 Main Navigation Tabs
        elements.tabBtnPatients = document.getElementById('tabBtnPatients');
        elements.tabBtnPharmacySync = document.getElementById('tabBtnPharmacySync');
        elements.tabBtnMar = document.getElementById('tabBtnMar');

        elements.patientTabCount = document.getElementById('patientTabCount');
        elements.pharmacyTabCount = document.getElementById('pharmacyTabCount');
        elements.marTabCount = document.getElementById('marTabCount');

        elements.panePatients = document.getElementById('panePatients');
        elements.panePharmacySync = document.getElementById('panePharmacySync');
        elements.paneMar = document.getElementById('paneMar');

        // Tab 1: Patient Directory & Ward Accordions
        elements.patientStationFilter = document.getElementById('patientStationFilter');
        elements.patientSearchInput = document.getElementById('patientSearchInput');
        elements.patientListContainer = document.getElementById('patientListContainer');
        elements.patientDetailContainer = document.getElementById('patientDetailContainer');

        // Tab 2: Pharmacy Sync Audit
        elements.pharmacyInpatientCount = document.getElementById('pharmacyInpatientCount');
        elements.pharmacyActiveOrderCount = document.getElementById('pharmacyActiveOrderCount');
        elements.pharmacyDosesCount = document.getElementById('pharmacyDosesCount');
        elements.pharmacyControlledCount = document.getElementById('pharmacyControlledCount');
        elements.pharmacyExceptionsCount = document.getElementById('pharmacyExceptionsCount');
        elements.pharmacySearchInput = document.getElementById('pharmacySearchInput');
        elements.pharmacyTableBody = document.getElementById('pharmacyTableBody');
        elements.btnExportPharmacyExcel = document.getElementById('btnExportPharmacyExcel');

        // Tab 3: MAR Shift Board & Table
        elements.marSearchInput = document.getElementById('marSearchInput');
        elements.marStationFilter = document.getElementById('marStationFilter');
        elements.chipHighAlertOnly = document.getElementById('chipHighAlertOnly');
        elements.btnViewShiftBoard = document.getElementById('btnViewShiftBoard');
        elements.btnViewTable = document.getElementById('btnViewTable');
        elements.marShiftBoardContainer = document.getElementById('marShiftBoardContainer');
        elements.marTableContainer = document.getElementById('marTableContainer');
        elements.marTableBody = document.getElementById('marTableBody');

        // Shift Board Badges & Containers
        elements.shiftBadgeMorning = document.getElementById('shiftBadgeMorning');
        elements.shiftBadgeNoon = document.getElementById('shiftBadgeNoon');
        elements.shiftBadgeEvening = document.getElementById('shiftBadgeEvening');
        elements.shiftBadgeNight = document.getElementById('shiftBadgeNight');

        elements.shiftCardsMorning = document.getElementById('shiftCardsMorning');
        elements.shiftCardsNoon = document.getElementById('shiftCardsNoon');
        elements.shiftCardsEvening = document.getElementById('shiftCardsEvening');
        elements.shiftCardsNight = document.getElementById('shiftCardsNight');

        // Slide-Over Inspector Drawer
        elements.inspectorDrawer = document.getElementById('inspectorDrawer');
        elements.drawerBackdrop = document.getElementById('drawerBackdrop');
        elements.drawerDrugName = document.getElementById('drawerDrugName');
        elements.drawerBody = document.getElementById('drawerBody');
        elements.btnCloseDrawer = document.getElementById('btnCloseDrawer');
        elements.btnCloseDrawerBtn = document.getElementById('btnCloseDrawerBtn');

        // Modals
        elements.medicationModal = document.getElementById('medicationModal');
        elements.modalMedTitle = document.getElementById('modalMedTitle');
        elements.modalMedBody = document.getElementById('modalMedBody');
        elements.btnCloseMedModal = document.getElementById('btnCloseMedModal');
        elements.btnDismissMedModal = document.getElementById('btnDismissMedModal');

        elements.settingsModal = document.getElementById('settingsModal');
        elements.btnCloseSettingsModal = document.getElementById('btnCloseSettingsModal');
        elements.btnDismissSettingsModal = document.getElementById('btnDismissSettingsModal');
        elements.btnTestDbConnection = document.getElementById('btnTestConnection') || document.getElementById('btnTestDbConnection');
        elements.cfgStatusDisplay = document.getElementById('cfgStatusDisplay') || document.getElementById('testConnectionStatus');

        elements.exportModal = document.getElementById('exportModal');
        elements.btnCloseExportModal = document.getElementById('btnCloseExportModal');
        elements.btnCancelExport = document.getElementById('btnCancelExport');
        elements.btnConfirmExport = document.getElementById('btnConfirmExport');
        elements.exportFilteredCount = document.getElementById('exportFilteredCount');
        elements.exportTotalCount = document.getElementById('exportTotalCount');

        elements.toastContainer = document.getElementById('toastContainer');

        if (window.location.protocol === 'file:' && elements.fileProtocolNotice) {
            elements.fileProtocolNotice.style.display = 'flex';
        }
    }

    // Theme Management
    function initTheme() {
        let savedTheme = 'light';
        try {
            savedTheme = localStorage.getItem('medhub_theme') || 'light';
        } catch (e) { }
        setTheme(savedTheme);
    }

    function setTheme(theme) {
        state.currentTheme = theme;
        if (theme === 'dark') {
            document.documentElement.setAttribute('data-theme', 'dark');
            document.body.classList.add('dark-theme');
            if (elements.themeIcon) elements.themeIcon.textContent = '☀️';
            if (elements.themeLabel) elements.themeLabel.textContent = 'Light Mode';
        } else {
            document.documentElement.removeAttribute('data-theme');
            document.body.classList.remove('dark-theme');
            if (elements.themeIcon) elements.themeIcon.textContent = '🌙';
            if (elements.themeLabel) elements.themeLabel.textContent = 'Dark Mode';
        }
        try {
            localStorage.setItem('medhub_theme', theme);
        } catch (e) { }
    }

    function toggleTheme() {
        setTheme(state.currentTheme === 'dark' ? 'light' : 'dark');
    }

    // Event Bindings
    function bindEvents() {
        // Theme Toggle
        if (elements.themeToggleBtn) elements.themeToggleBtn.addEventListener('click', toggleTheme);

        // Tab Switching
        elements.tabBtnPatients.addEventListener('click', () => switchTab('patients'));
        elements.tabBtnPharmacySync.addEventListener('click', () => switchTab('pharmacy'));
        elements.tabBtnMar.addEventListener('click', () => switchTab('mar'));

        // Refresh Data
        elements.btnRefreshData.addEventListener('click', refreshData);

        // Export Modal Triggers
        elements.btnExportExcel.addEventListener('click', openExportModal);
        if (elements.btnExportPharmacyExcel) elements.btnExportPharmacyExcel.addEventListener('click', openExportModal);
        elements.btnCloseExportModal.addEventListener('click', closeExportModal);
        elements.btnCancelExport.addEventListener('click', closeExportModal);
        elements.btnConfirmExport.addEventListener('click', executeExcelExport);

        // Settings Modal
        elements.btnSettingsModal.addEventListener('click', openSettingsModal);
        elements.btnCloseSettingsModal.addEventListener('click', closeSettingsModal);
        elements.btnDismissSettingsModal.addEventListener('click', closeSettingsModal);
        elements.btnTestDbConnection.addEventListener('click', testDbConnection);

        // Medication Modal (Legacy fallback)
        elements.btnCloseMedModal.addEventListener('click', closeMedModal);
        elements.btnDismissMedModal.addEventListener('click', closeMedModal);

        // Slide-Over Drawer Events
        elements.btnCloseDrawer.addEventListener('click', closeInspectorDrawer);
        elements.btnCloseDrawerBtn.addEventListener('click', closeInspectorDrawer);
        elements.drawerBackdrop.addEventListener('click', closeInspectorDrawer);

        // Tab 1 Filters
        elements.patientStationFilter.addEventListener('change', (e) => {
            state.patientStation = e.target.value;
            renderPatientList();
        });

        let patientSearchTimeout;
        elements.patientSearchInput.addEventListener('input', (e) => {
            clearTimeout(patientSearchTimeout);
            patientSearchTimeout = setTimeout(() => {
                state.patientSearch = e.target.value.trim().toLowerCase();
                renderPatientList();
            }, 120);
        });

        // Tab 2 Pharmacy Filters
        const rxFilterChips = document.querySelectorAll('[data-rxfilter]');
        rxFilterChips.forEach(chip => {
            chip.addEventListener('click', () => {
                rxFilterChips.forEach(c => c.classList.remove('active'));
                chip.classList.add('active');
                state.pharmacyFilter = chip.getAttribute('data-rxfilter');
                renderPharmacySyncTable();
            });
        });

        let pharmacySearchTimeout;
        elements.pharmacySearchInput.addEventListener('input', (e) => {
            clearTimeout(pharmacySearchTimeout);
            pharmacySearchTimeout = setTimeout(() => {
                state.pharmacySearch = e.target.value.trim().toLowerCase();
                renderPharmacySyncTable();
            }, 120);
        });

        // Tab 3 MAR Filters
        elements.marStationFilter.addEventListener('change', (e) => {
            state.marFilters.station = e.target.value;
            applyMarFilters();
        });

        let marSearchTimeout;
        elements.marSearchInput.addEventListener('input', (e) => {
            clearTimeout(marSearchTimeout);
            marSearchTimeout = setTimeout(() => {
                state.marFilters.search = e.target.value.trim().toLowerCase();
                applyMarFilters();
            }, 120);
        });

        // Date Chips
        const dateChips = document.querySelectorAll('.filter-chip[data-date]');
        dateChips.forEach(chip => {
            chip.addEventListener('click', () => {
                dateChips.forEach(c => c.classList.remove('active'));
                chip.classList.add('active');
                state.marFilters.date = chip.getAttribute('data-date');
                applyMarFilters();
            });
        });

        // Status Chips
        const statusChips = document.querySelectorAll('.filter-chip[data-status]');
        statusChips.forEach(chip => {
            chip.addEventListener('click', () => {
                statusChips.forEach(c => c.classList.remove('active'));
                chip.classList.add('active');
                state.marFilters.status = chip.getAttribute('data-status');
                applyMarFilters();
            });
        });

        // High Alert Toggle Chip
        elements.chipHighAlertOnly.addEventListener('click', () => {
            state.marFilters.highAlertOnly = !state.marFilters.highAlertOnly;
            elements.chipHighAlertOnly.classList.toggle('active', state.marFilters.highAlertOnly);
            applyMarFilters();
        });

        // Shift Board vs Table View Mode Toggles
        elements.btnViewShiftBoard.addEventListener('click', () => {
            state.marViewMode = 'shiftBoard';
            elements.btnViewShiftBoard.classList.add('active');
            elements.btnViewTable.classList.remove('active');
            elements.marShiftBoardContainer.style.display = 'grid';
            elements.marTableContainer.style.display = 'none';
        });

        elements.btnViewTable.addEventListener('click', () => {
            state.marViewMode = 'table';
            elements.btnViewTable.classList.add('active');
            elements.btnViewShiftBoard.classList.remove('active');
            elements.marShiftBoardContainer.style.display = 'none';
            elements.marTableContainer.style.display = 'block';
        });

        // Global ESC key for closing modals & drawer
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                closeInspectorDrawer();
                closeMedModal();
                closeSettingsModal();
                closeExportModal();
            }
        });
    }

    // Tab Switching
    function switchTab(tabId) {
        state.activeTab = tabId;

        // Reset tab buttons
        elements.tabBtnPatients.classList.toggle('active', tabId === 'patients');
        elements.tabBtnPharmacySync.classList.toggle('active', tabId === 'pharmacy');
        elements.tabBtnMar.classList.toggle('active', tabId === 'mar');

        // Toggle panes
        elements.panePatients.classList.toggle('active', tabId === 'patients');
        elements.panePharmacySync.classList.toggle('active', tabId === 'pharmacy');
        elements.paneMar.classList.toggle('active', tabId === 'mar');

        if (tabId === 'pharmacy') {
            renderPharmacySyncTable();
        } else if (tabId === 'mar') {
            applyMarFilters();
        }
    }

    // Data Fetching & Sync
    async function loadInitialData() {
        try {
            const snapshot = await window.ipc.invoke('loadSnapshot');
            handleSnapshotLoaded(snapshot);
        } catch (err) {
            console.error('[App] Failed to load snapshot:', err);
            showToast('Failed to connect to MedHub database: ' + err.message, 'error');
            if (elements.dbStatusText) {
                elements.dbStatusText.textContent = 'amisdbV2 Disconnected (Error)';
                elements.dbStatusBadge.style.backgroundColor = 'var(--color-danger-bg)';
                elements.dbStatusBadge.style.color = 'var(--color-danger-text)';
                elements.dbStatusBadge.style.borderColor = 'var(--color-danger-border)';
            }
        }
    }

    async function refreshData() {
        elements.btnRefreshData.disabled = true;
        elements.btnRefreshData.innerHTML = 'Refreshing...';

        try {
            const snapshot = await window.ipc.invoke('refreshData');
            handleSnapshotLoaded(snapshot);
            showToast('Clinical data synchronized from MedHub DB', 'success');
        } catch (err) {
            console.error('[App] Refresh error:', err);
            showToast('Refresh failed: ' + err.message, 'error');
        } finally {
            elements.btnRefreshData.disabled = false;
            elements.btnRefreshData.innerHTML = `
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"></path>
                    <path d="M3 3v5h5"></path>
                    <path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16"></path>
                    <path d="M16 21h5v-5"></path>
                </svg> Refresh`;
        }
    }

    function handleSnapshotLoaded(snapshot) {
        state.snapshot = snapshot;

        // Update Tab Badges
        const patientCount = snapshot.patients ? snapshot.patients.length : 0;
        const activeRxCount = snapshot.activePrescriptions ? snapshot.activePrescriptions.length : 0;
        const marCount = snapshot.marSchedules ? snapshot.marSchedules.length : 0;

        elements.patientTabCount.textContent = patientCount;
        elements.pharmacyTabCount.textContent = activeRxCount;
        elements.marTabCount.textContent = marCount;

        // Populate station dropdowns
        populateStationDropdowns();

        // Render Top KPI Metrics Strip
        renderKpiCards();

        // Open all wards by default
        if (snapshot.stations) {
            snapshot.stations.forEach(s => state.openWards.add(s.station_Code || s.station_Name));
        }

        // Render Tab 1 (Patient Directory)
        renderPatientList();

        // Auto-select first patient if none selected
        if (!state.selectedPatient && snapshot.patients && snapshot.patients.length > 0) {
            selectPatient(snapshot.patients[0]);
        }

        // Render Tab 2 (Pharmacy Sync)
        renderPharmacySyncTable();

        // Render Tab 3 (Hospital Shift Board)
        applyMarFilters();
    }

    function populateStationDropdowns() {
        if (!state.snapshot || !state.snapshot.stations) return;
        const stations = state.snapshot.stations;

        // Patient station filter
        const currentPatientStation = elements.patientStationFilter.value;
        elements.patientStationFilter.innerHTML = '<option value="">All Stations (All Wards)</option>';
        stations.forEach(s => {
            const opt = document.createElement('option');
            opt.value = s.station_Code || s.station_Name;
            opt.textContent = s.station_Name || s.station_Code;
            elements.patientStationFilter.appendChild(opt);
        });
        elements.patientStationFilter.value = currentPatientStation;

        // MAR station filter
        const currentMarStation = elements.marStationFilter.value;
        elements.marStationFilter.innerHTML = '<option value="">All Stations (All Wards)</option>';
        stations.forEach(s => {
            const opt = document.createElement('option');
            opt.value = s.station_Code || s.station_Name;
            opt.textContent = s.station_Name || s.station_Code;
            elements.marStationFilter.appendChild(opt);
        });
        elements.marStationFilter.value = currentMarStation;
    }

    function renderKpiCards() {
        if (!state.snapshot || !state.snapshot.kpiSummary) return;
        const kpi = state.snapshot.kpiSummary;

        elements.kpiInpatients.textContent = kpi.totalInpatients ?? 0;
        elements.kpiActiveOrders.textContent = kpi.activeOrders ?? 0;
        elements.kpiDispensed.textContent = kpi.dispensedCount ?? 0;
        elements.kpiPendingDue.textContent = kpi.totalPendingCount ?? 0;
        elements.kpiDuePendingCount.textContent = `Due: ${kpi.duePendingCount ?? 0}`;
        elements.kpiFuturePendingCount.textContent = Math.max(0, (kpi.totalPendingCount ?? 0) - (kpi.duePendingCount ?? 0));
        elements.kpiControlledCount.textContent = `Controlled: ${kpi.controlledCount ?? 0}`;
        elements.kpiReturnedWastedCount.textContent = `Exceptions: ${kpi.returnedCount ?? 0} Ret / ${kpi.wastedCount ?? 0} Wst`;

        // Update Tab 2 Pharmacy KPI Banner
        if (elements.pharmacyInpatientCount) elements.pharmacyInpatientCount.textContent = kpi.totalInpatients ?? 0;
        if (elements.pharmacyActiveOrderCount) elements.pharmacyActiveOrderCount.textContent = kpi.activeOrders ?? 0;
        if (elements.pharmacyDosesCount) elements.pharmacyDosesCount.textContent = (state.snapshot.marSchedules || []).length;
        if (elements.pharmacyControlledCount) elements.pharmacyControlledCount.textContent = kpi.controlledCount ?? 0;
        if (elements.pharmacyExceptionsCount) elements.pharmacyExceptionsCount.textContent = `${(kpi.returnedCount ?? 0) + (kpi.wastedCount ?? 0)}`;
    }

    // =========================================================================
    // Patient Name & Demographic Resolvers
    // =========================================================================
    function getPatientName(p) {
        if (!p) return 'Unknown';
        if (p.patient_Name && p.patient_Name.trim()) return p.patient_Name.trim();
        if (p.fullName && p.fullName.trim()) return p.fullName.trim();
        if (p.displayName && p.displayName.trim()) return p.displayName.trim();
        const combined = `${p.last_Name || ''} ${p.first_Name || ''}`.trim();
        if (combined) return combined;

        // Cross-reference from active prescriptions or MAR schedules if available
        if (state.snapshot) {
            if (state.snapshot.activePrescriptions) {
                const rx = state.snapshot.activePrescriptions.find(r => (r.patient_Mrn === p.patient_Mrn || r.patient_Id === p.patient_Id) && r.patient_Name);
                if (rx && rx.patient_Name.trim()) return rx.patient_Name.trim();
            }
            if (state.snapshot.marSchedules) {
                const s = state.snapshot.marSchedules.find(m => (m.patient_Mrn === p.patient_Mrn || m.patient_Id === p.patient_Id) && m.patient_Name);
                if (s && s.patient_Name.trim()) return s.patient_Name.trim();
            }
        }

        return p.patient_Mrn ? `Inpatient (${p.patient_Mrn})` : 'Unknown';
    }

    function getPatientSex(p) {
        if (!p) return '-';
        return p.patient_Sex || p.sex || '-';
    }

    // =========================================================================
    // TAB 1: Patient & Ward Review (Ward Accordions & Shift MAR Timeline)
    // =========================================================================
    function renderPatientList() {
        if (!state.snapshot || !state.snapshot.patients) {
            elements.patientListContainer.innerHTML = '<div class="empty-state">No patient data available.</div>';
            return;
        }

        let patients = state.snapshot.patients;

        // Filter by station
        if (state.patientStation) {
            patients = patients.filter(p =>
                (p.station_Code && p.station_Code.toLowerCase() === state.patientStation.toLowerCase()) ||
                (p.station_Name && p.station_Name.toLowerCase().includes(state.patientStation.toLowerCase()))
            );
        }

        // Filter by search query
        if (state.patientSearch) {
            const query = state.patientSearch.toLowerCase();
            patients = patients.filter(p =>
                getPatientName(p).toLowerCase().includes(query) ||
                (p.patient_Mrn && p.patient_Mrn.toLowerCase().includes(query)) ||
                (p.bed_Name && p.bed_Name.toLowerCase().includes(query)) ||
                (p.bed_Code && p.bed_Code.toLowerCase().includes(query)) ||
                (p.case_No && p.case_No.toLowerCase().includes(query))
            );
        }

        if (patients.length === 0) {
            elements.patientListContainer.innerHTML = '<div class="empty-state">No patients match current filters.</div>';
            return;
        }

        // Group patients by Ward Station
        const wardGroups = new Map();
        patients.forEach(p => {
            const wardKey = p.station_Name || p.station_Code || 'Unassigned Ward';
            if (!wardGroups.has(wardKey)) {
                wardGroups.set(wardKey, []);
            }
            wardGroups.get(wardKey).push(p);
        });

        elements.patientListContainer.innerHTML = '';

        wardGroups.forEach((wardPatients, wardName) => {
            const isOpen = state.openWards.has(wardName) || state.openWards.size === 0;

            const accordion = document.createElement('div');
            accordion.className = `ward-accordion ${isOpen ? 'open' : ''}`;

            const header = document.createElement('div');
            header.className = 'ward-accordion-header';
            header.innerHTML = `
                <div class="ward-header-title">
                    <span class="ward-toggle-arrow">▶</span>
                    <span>${escapeHtml(wardName)}</span>
                </div>
                <span class="ward-header-badge">${wardPatients.length} Patients</span>
            `;

            header.addEventListener('click', () => {
                if (accordion.classList.contains('open')) {
                    accordion.classList.remove('open');
                    state.openWards.delete(wardName);
                } else {
                    accordion.classList.add('open');
                    state.openWards.add(wardName);
                }
            });

            const body = document.createElement('div');
            body.className = 'ward-accordion-body';

            wardPatients.forEach(patient => {
                const card = document.createElement('div');
                card.className = 'patient-card';
                if (state.selectedPatient && (state.selectedPatient.patient_Id === patient.patient_Id || state.selectedPatient.patient_Mrn === patient.patient_Mrn)) {
                    card.classList.add('selected');
                }

                const activeRxCount = patient.activePrescriptionsCount ?? (patient.activePrescriptions ? patient.activePrescriptions.length : 0);

                let badgesHtml = '';
                if (patient.allergy === 'Y') {
                    badgesHtml += `<span class="badge badge-due" title="${escapeHtml(patient.allergy_Desc || 'Allergy Recorded')}">⚠️ Allergy</span>`;
                }
                if (patient.drugWarn === 'Y') {
                    badgesHtml += `<span class="badge badge-pending" title="${escapeHtml(patient.drugWarn_Desc || 'Drug Warning Recorded')}">Warning</span>`;
                }
                badgesHtml += `<span class="badge" style="background-color: var(--color-bg-surface-alt); border: 1px solid var(--color-border);">${activeRxCount} Orders</span>`;

                const patName = getPatientName(patient);
                const patSex = getPatientSex(patient);

                card.innerHTML = `
                    <div class="patient-card-header">
                        <span class="patient-card-name">${escapeHtml(patName)}</span>
                        <span class="bed-badge">${escapeHtml(patient.bed_Name || patient.bed_Code || 'Bed -')}</span>
                    </div>
                    <div class="patient-card-sub">
                        <span>MRN: ${escapeHtml(patient.patient_Mrn || '-')}</span>
                        <span>${escapeHtml(patSex)} ${patient.age ? patient.age + 'y' : ''}</span>
                    </div>
                    <div class="patient-badges">
                        ${badgesHtml}
                    </div>
                `;

                card.addEventListener('click', () => selectPatient(patient));
                body.appendChild(card);
            });

            accordion.appendChild(header);
            accordion.appendChild(body);
            elements.patientListContainer.appendChild(accordion);
        });
    }

    function selectPatient(patient) {
        state.selectedPatient = patient;

        // Re-render patient list to highlight selected card
        renderPatientList();

        // Render detail view
        renderPatientDetail();
    }

    function renderPatientDetail() {
        const p = state.selectedPatient;
        if (!p) {
            elements.patientDetailContainer.innerHTML = '<div class="empty-state">Select an inpatient to view clinical details.</div>';
            return;
        }

        // Active doctor prescriptions
        let activeRxList = [];
        if (state.snapshot && state.snapshot.activePrescriptions) {
            activeRxList = state.snapshot.activePrescriptions.filter(rx => rx.patient_Mrn === p.patient_Mrn || rx.patient_Id === p.patient_Id);
        }

        // Scheduled dose administrations (MAR)
        let patientSchedules = [];
        if (state.snapshot && state.snapshot.marSchedules) {
            patientSchedules = state.snapshot.marSchedules.filter(s => s.patient_Mrn === p.patient_Mrn || s.patient_Id === p.patient_Id);
        }

        // Allergy and drug warning alert banners
        let alertsHtml = '';
        if (p.allergy === 'Y' && p.allergy_Desc) {
            alertsHtml += `
                <div class="alert-box-allergy">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                        <line x1="12" y1="9" x2="12" y2="13"></line>
                        <line x1="12" y1="17" x2="12.01" y2="17"></line>
                    </svg>
                    <span><strong>ALLERGY ALERT:</strong> ${escapeHtml(p.allergy_Desc)}</span>
                </div>
            `;
        }
        if (p.drugWarn === 'Y' && p.drugWarn_Desc) {
            alertsHtml += `
                <div class="alert-box-warning">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"></circle>
                        <line x1="12" y1="8" x2="12" y2="12"></line>
                        <line x1="12" y1="16" x2="12.01" y2="16"></line>
                    </svg>
                    <span><strong>HIGH-ALERT / WARNING NOTE:</strong> ${escapeHtml(p.drugWarn_Desc)}</span>
                </div>
            `;
        }

        // Active Prescriptions Table Rows
        let rxRowsHtml = '';
        if (activeRxList.length === 0) {
            rxRowsHtml = '<tr><td colspan="7" class="empty-state">No active prescription orders found for this patient.</td></tr>';
        } else {
            activeRxList.forEach(rx => {
                let badges = '';
                if (rx.isHighAlert) {
                    badges += '<span class="badge badge-high-alert">HIGH-ALERT</span> ';
                }
                if (rx.isRefrigerationRequired) {
                    badges += '<span class="badge badge-cold">❄ 2-8°C</span> ';
                }
                if (rx.is_Controlled) {
                    badges += `<span class="badge badge-controlled">CTRL L${escapeHtml(rx.control_Level || '1')}</span> `;
                }

                rxRowsHtml += `
                    <tr>
                        <td>
                            <strong style="color: var(--color-primary); cursor: pointer;" onclick="window.inspectMedication('${escapeHtml(rx.medication_Code)}')">
                                ${escapeHtml(rx.medication_Name || rx.medication_Code)}
                            </strong>
                            <div style="font-size: 11px; color: var(--color-text-muted);">Item Code: <code>${escapeHtml(rx.medication_Code)}</code></div>
                        </td>
                        <td>${badges || '<span style="color: var(--color-text-muted); font-size: 11px;">Standard</span>'}</td>
                        <td><strong>${escapeHtml(String(rx.dosage_Ordered_Value))} ${escapeHtml(rx.dosage_Ordered_Unit || '')}</strong></td>
                        <td><span class="badge" style="background-color: var(--color-bg-surface-alt);">${escapeHtml(rx.frequency || '-')}</span></td>
                        <td>${escapeHtml(rx.route || '-')}</td>
                        <td>${escapeHtml(rx.clinical_Indication || '-')}</td>
                        <td style="text-align: center;">
                            <button type="button" class="btn-nav" style="padding: 2px 8px; font-size: 11px;" onclick="window.inspectMedication('${escapeHtml(rx.medication_Code)}')">
                                Inspect
                            </button>
                        </td>
                    </tr>
                `;
            });
        }

        // Distribute patient schedules into 4 shifts
        const shifts = {
            morning: [],
            noon: [],
            evening: [],
            night: []
        };

        patientSchedules.forEach(s => {
            const shiftName = getShiftForTime(s.schedule_Time);
            shifts[shiftName].push(s);
        });

        function renderShiftDoseCards(doses) {
            if (!doses || doses.length === 0) {
                return '<div style="font-size: 11.5px; color: var(--color-text-muted); padding: 8px; text-align: center;">No doses</div>';
            }
            return doses.map(s => {
                const statusBadge = getStatusBadgeHtml(s.dispense_Status);
                const nurseInfo = s.dispense_Nurse_Name || s.confirmed_Nurse_Name ? `Admin: ${escapeHtml(s.dispense_Nurse_Name || s.confirmed_Nurse_Name)}` : '';
                return `
                    <div class="shift-dose-card" onclick="window.inspectSchedule('${escapeHtml(s.schedule_Id)}')">
                        <div class="dose-card-top">
                            <span class="dose-card-time">${formatTimeOnly(s.schedule_Time)}</span>
                            ${statusBadge}
                        </div>
                        <div class="dose-card-drug" title="${escapeHtml(s.display_Name || s.item_Code)}">
                            ${escapeHtml(s.display_Name || s.item_Code)}
                        </div>
                        <div class="dose-card-sub">
                            <strong>${escapeHtml(String(s.calc_Dispense_Qty))} ${escapeHtml(s.dispense_Qty_Unit || '')}</strong> • ${escapeHtml(s.route || '-')}
                        </div>
                        ${nurseInfo ? `<div style="font-size: 10px; color: var(--color-text-muted);">${nurseInfo}</div>` : ''}
                    </div>
                `;
            }).join('');
        }

        const detailName = getPatientName(p);
        const detailSex = getPatientSex(p);

        elements.patientDetailContainer.innerHTML = `
            <!-- Patient Demographic & Safety Card -->
            <div class="patient-banner">
                <div class="banner-row-main">
                    <div class="banner-demographics">
                        <div style="display: flex; align-items: center; gap: 10px;">
                            <h2 class="banner-patient-name">${escapeHtml(detailName)}</h2>
                            <span class="bed-badge" style="font-size: 13px; padding: 3px 8px;">${escapeHtml(p.bed_Name || p.bed_Code || 'Bed -')}</span>
                        </div>
                        <div class="banner-patient-mrn">MRN: <strong>${escapeHtml(p.patient_Mrn || '-')}</strong> • Case: ${escapeHtml(p.case_No || '-')}</div>
                    </div>
                </div>

                <div class="banner-info-grid">
                    <div class="banner-info-item">
                        <span class="banner-label">Station / Ward</span>
                        <span class="banner-value">${escapeHtml(p.station_Name || p.station_Code || '-')}</span>
                    </div>
                    <div class="banner-info-item">
                        <span class="banner-label">Attending Physician</span>
                        <span class="banner-value">${escapeHtml(p.attending_Physician || '-')}</span>
                    </div>
                    <div class="banner-info-item">
                        <span class="banner-label">Gender &amp; Age</span>
                        <span class="banner-value">${escapeHtml(detailSex)} / ${p.age ? p.age + ' yrs' : '-'}</span>
                    </div>
                    <div class="banner-info-item">
                        <span class="banner-label">Admission Date</span>
                        <span class="banner-value">${formatDate(p.admission_Date)}</span>
                    </div>
                    <div class="banner-info-item">
                        <span class="banner-label">Active Orders</span>
                        <span class="banner-value" style="color: var(--color-primary);">${activeRxList.length} Prescriptions</span>
                    </div>
                    <div class="banner-info-item">
                        <span class="banner-label">Scheduled Administrations</span>
                        <span class="banner-value">${patientSchedules.length} Doses</span>
                    </div>
                </div>

                ${alertsHtml ? `<div class="banner-alerts">${alertsHtml}</div>` : ''}
            </div>

            <!-- Active Prescriptions Section -->
            <div class="section-card">
                <div class="section-header" onclick="this.parentElement.classList.toggle('collapsed')">
                    <span class="section-title">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="m10.5 20.5 10-10a4.95 4.95 0 1 0-7-7l-10 10a4.95 4.95 0 1 0 7 7Z"></path>
                            <path d="m8.5 8.5 7 7"></path>
                        </svg>
                        Active Physician Prescription Orders (${activeRxList.length})
                    </span>
                    <span class="section-collapse-chevron" title="Toggle section">▼</span>
                </div>
                <div class="section-content" style="overflow-x: auto;">
                    <table class="orders-table">
                        <thead>
                            <tr>
                                <th>Medication &amp; Item Code</th>
                                <th>Safety Alerts</th>
                                <th>Ordered Dose</th>
                                <th>Frequency</th>
                                <th>Route</th>
                                <th>Clinical Indication</th>
                                <th style="width: 80px; text-align: center;">Action</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${rxRowsHtml}
                        </tbody>
                    </table>
                </div>
            </div>

            <!-- Shift-Based Visual MAR Timeline -->
            <div class="section-card">
                <div class="section-header" onclick="this.parentElement.classList.toggle('collapsed')">
                    <span class="section-title">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="10"></circle>
                            <polyline points="12 6 12 12 16 14"></polyline>
                        </svg>
                        Shift-Based MAR Administration Timeline (${patientSchedules.length} Scheduled Doses)
                    </span>
                    <span class="section-collapse-chevron" title="Toggle section">▼</span>
                </div>
                <div class="section-content">
                    <div class="shift-timeline-grid">
                        <!-- Morning Shift -->
                        <div class="shift-timeline-col">
                            <div class="shift-col-header">
                                <span class="shift-col-title">🌅 Morning (08:00)</span>
                                <span class="shift-col-badge">${shifts.morning.length}</span>
                            </div>
                            <div class="shift-dose-cards">
                                ${renderShiftDoseCards(shifts.morning)}
                            </div>
                        </div>

                        <!-- Noon Shift -->
                        <div class="shift-timeline-col">
                            <div class="shift-col-header">
                                <span class="shift-col-title">☀️ Noon (12:00)</span>
                                <span class="shift-col-badge">${shifts.noon.length}</span>
                            </div>
                            <div class="shift-dose-cards">
                                ${renderShiftDoseCards(shifts.noon)}
                            </div>
                        </div>

                        <!-- Evening Shift -->
                        <div class="shift-timeline-col">
                            <div class="shift-col-header">
                                <span class="shift-col-title">🌆 Evening (18:00)</span>
                                <span class="shift-col-badge">${shifts.evening.length}</span>
                            </div>
                            <div class="shift-dose-cards">
                                ${renderShiftDoseCards(shifts.evening)}
                            </div>
                        </div>

                        <!-- Night Shift -->
                        <div class="shift-timeline-col">
                            <div class="shift-col-header">
                                <span class="shift-col-title">🌙 Night (21:00)</span>
                                <span class="shift-col-badge">${shifts.night.length}</span>
                            </div>
                            <div class="shift-dose-cards">
                                ${renderShiftDoseCards(shifts.night)}
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // =========================================================================
    // TAB 2: Pharmacy & HIS Sync Audit
    // =========================================================================
    function renderPharmacySyncTable() {
        if (!state.snapshot || !state.snapshot.activePrescriptions) {
            elements.pharmacyTableBody.innerHTML = '<tr><td colspan="11" class="empty-state">No prescription orders found.</td></tr>';
            return;
        }

        let orders = state.snapshot.activePrescriptions;

        // Apply Search
        if (state.pharmacySearch) {
            const q = state.pharmacySearch;
            orders = orders.filter(rx =>
                (rx.medication_Name && rx.medication_Name.toLowerCase().includes(q)) ||
                (rx.medication_Code && rx.medication_Code.toLowerCase().includes(q)) ||
                (rx.generic_Name && rx.generic_Name.toLowerCase().includes(q)) ||
                (rx.patient_Name && rx.patient_Name.toLowerCase().includes(q)) ||
                (rx.patient_Mrn && rx.patient_Mrn.toLowerCase().includes(q)) ||
                (rx.med_Order_Id && rx.med_Order_Id.toLowerCase().includes(q)) ||
                (rx.atc_Code && rx.atc_Code.toLowerCase().includes(q)) ||
                (rx.attending_Physician && rx.attending_Physician.toLowerCase().includes(q))
            );
        }

        // Apply Chip Filter
        if (state.pharmacyFilter === 'controlled') {
            orders = orders.filter(rx => rx.is_Controlled);
        } else if (state.pharmacyFilter === 'highalert') {
            orders = orders.filter(rx => rx.isHighAlert);
        } else if (state.pharmacyFilter === 'refrigerated') {
            orders = orders.filter(rx => rx.isRefrigerationRequired);
        }

        if (orders.length === 0) {
            elements.pharmacyTableBody.innerHTML = '<tr><td colspan="11" class="empty-state">No prescription orders match the sync audit filter.</td></tr>';
            return;
        }

        let html = '';
        orders.forEach(rx => {
            let safetyBadges = '';
            if (rx.isHighAlert) safetyBadges += '<span class="badge badge-high-alert">HIGH-ALERT</span> ';
            if (rx.is_Controlled) safetyBadges += `<span class="badge badge-controlled">CTRL L${escapeHtml(rx.control_Level || '1')}</span> `;
            if (rx.isRefrigerationRequired) safetyBadges += '<span class="badge badge-cold">❄ 2-8°C</span> ';

            const durationStr = `${formatDate(rx.start_Datetime || rx.order_Start_Datetime)} ~ ${rx.end_Datetime ? formatDate(rx.end_Datetime) : 'Ongoing'}`;

            html += `
                <tr class="clickable-row">
                    <td><code>${escapeHtml(rx.med_Order_Id || '-')}</code></td>
                    <td>${escapeHtml(rx.case_No || '-')}</td>
                    <td>
                        <strong>${escapeHtml(rx.patient_Name || '-')}</strong>
                        <div style="font-size: 11px; color: var(--color-text-muted);">MRN: ${escapeHtml(rx.patient_Mrn || '-')}</div>
                    </td>
                    <td>
                        <span class="bed-badge">${escapeHtml(rx.station_Code || '-')}: ${escapeHtml(rx.bed_Code || rx.bed_Name || '-')}</span>
                    </td>
                    <td>
                        <strong style="color: var(--color-primary); cursor: pointer;" onclick="window.inspectMedication('${escapeHtml(rx.medication_Code)}')">
                            ${escapeHtml(rx.medication_Name || rx.medication_Code)}
                        </strong>
                        <div style="font-size: 11px; color: var(--color-text-muted);">${escapeHtml(rx.generic_Name || '')}</div>
                    </td>
                    <td>${safetyBadges || '<span style="color: var(--color-text-muted); font-size: 11px;">Standard</span>'}</td>
                    <td><strong>${escapeHtml(String(rx.dosage_Ordered_Value))} ${escapeHtml(rx.dosage_Ordered_Unit || '')}</strong></td>
                    <td>
                        <span class="badge" style="background-color: var(--color-bg-surface-alt);">${escapeHtml(rx.frequency || '-')}</span>
                        <div style="font-size: 11px; color: var(--color-text-muted);">${escapeHtml(rx.route || '-')}</div>
                    </td>
                    <td style="font-size: 11.5px;">${durationStr}</td>
                    <td style="font-size: 12px;">${escapeHtml(rx.clinical_Indication || '-')}</td>
                    <td style="text-align: center;">
                        <button type="button" class="btn-nav" style="padding: 2px 8px; font-size: 11px;" onclick="window.inspectMedication('${escapeHtml(rx.medication_Code)}')">
                            Inspect
                        </button>
                    </td>
                </tr>
            `;
        });

        elements.pharmacyTableBody.innerHTML = html;
    }

    // =========================================================================
    // TAB 3: Global MAR Shift Board & Timetable Table
    // =========================================================================
    function applyMarFilters() {
        if (!state.snapshot || !state.snapshot.marSchedules) {
            state.filteredMarItems = [];
            renderShiftBoard();
            renderMarTable();
            return;
        }

        const items = state.snapshot.marSchedules;
        const d = new Date();
        const todayStr = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

        state.filteredMarItems = items.filter(item => {
            // Station Filter
            if (state.marFilters.station) {
                const matchesStation =
                    (item.station_Code && item.station_Code.toLowerCase() === state.marFilters.station.toLowerCase()) ||
                    (item.station_Name && item.station_Name.toLowerCase().includes(state.marFilters.station.toLowerCase()));
                if (!matchesStation) return false;
            }

            // Search Query Filter
            if (state.marFilters.search) {
                const q = state.marFilters.search;
                const matchesSearch =
                    (item.patient_Name && item.patient_Name.toLowerCase().includes(q)) ||
                    (item.patient_Mrn && item.patient_Mrn.toLowerCase().includes(q)) ||
                    (item.bed_Name && item.bed_Name.toLowerCase().includes(q)) ||
                    (item.bed_Code && item.bed_Code.toLowerCase().includes(q)) ||
                    (item.display_Name && item.display_Name.toLowerCase().includes(q)) ||
                    (item.generic_Name && item.generic_Name.toLowerCase().includes(q)) ||
                    (item.item_Code && item.item_Code.toLowerCase().includes(q)) ||
                    (item.atc_Code && item.atc_Code.toLowerCase().includes(q));
                if (!matchesSearch) return false;
            }

            // High-Alert & Controlled Filter
            if (state.marFilters.highAlertOnly) {
                if (!item.isHighAlert && !item.is_Controlled) {
                    return false;
                }
            }

            // Date Filter
            const schedDateStr = item.schedule_Date ? item.schedule_Date.slice(0, 10) : '';
            if (state.marFilters.date === 'today') {
                if (schedDateStr !== todayStr) return false;
            } else if (state.marFilters.date === 'past') {
                if (schedDateStr > todayStr) return false;
            } else if (state.marFilters.date === 'future') {
                if (schedDateStr <= todayStr) return false;
            }

            // Status Filter
            if (state.marFilters.status !== 'all') {
                const s = (item.dispense_Status || '').toLowerCase();
                if (state.marFilters.status === 'due') {
                    if (!s.includes('due') && !s.includes('overdue')) return false;
                } else if (state.marFilters.status === 'dispensed') {
                    if (!s.startsWith('dispensed')) return false;
                } else if (state.marFilters.status === 'returned') {
                    if (!s.startsWith('returned')) return false;
                } else if (state.marFilters.status === 'wasted') {
                    if (!s.startsWith('wasted')) return false;
                } else if (state.marFilters.status === 'cancelled') {
                    if (!s.startsWith('cancelled')) return false;
                }
            }

            return true;
        });

        renderShiftBoard();
        renderMarTable();
    }

    function renderShiftBoard() {
        const items = state.filteredMarItems;

        const shifts = {
            morning: [],
            noon: [],
            evening: [],
            night: []
        };

        items.forEach(s => {
            const shiftName = getShiftForTime(s.schedule_Time);
            shifts[shiftName].push(s);
        });

        elements.shiftBadgeMorning.textContent = shifts.morning.length;
        elements.shiftBadgeNoon.textContent = shifts.noon.length;
        elements.shiftBadgeEvening.textContent = shifts.evening.length;
        elements.shiftBadgeNight.textContent = shifts.night.length;

        function renderCards(doses, container) {
            if (doses.length === 0) {
                container.innerHTML = '<div class="empty-state" style="padding: 20px;">No scheduled doses in this shift.</div>';
                return;
            }

            container.innerHTML = doses.map(s => {
                const statusBadge = getStatusBadgeHtml(s.dispense_Status);
                const nurse = s.dispense_Nurse_Name || s.confirmed_Nurse_Name;

                let safety = '';
                if (s.isHighAlert) safety += '<span class="badge badge-high-alert">HIGH-ALERT</span> ';
                if (s.is_Controlled) safety += '<span class="badge badge-controlled">CTRL</span> ';

                return `
                    <div class="shift-card" onclick="window.inspectSchedule('${escapeHtml(s.schedule_Id)}')">
                        <div style="display: flex; justify-content: space-between; align-items: center;">
                            <span class="bed-badge">${escapeHtml(s.station_Code || '-')}: ${escapeHtml(s.bed_Name || s.bed_Code || '-')}</span>
                            <span style="font-weight: 800; font-size: 12px; color: var(--color-text-primary);">${formatTimeOnly(s.schedule_Time)}</span>
                        </div>
                        <div style="font-weight: 700; font-size: 13px; color: var(--color-text-primary); margin-top: 2px;">
                            ${escapeHtml(s.patient_Name || '-')} <span style="font-weight: 500; font-size: 11px; color: var(--color-text-muted);">(${escapeHtml(s.patient_Mrn || '-')})</span>
                        </div>
                        <div style="font-weight: 600; font-size: 12.5px; color: var(--color-primary);">
                            ${escapeHtml(s.display_Name || s.item_Code)}
                        </div>
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 4px;">
                            <span style="font-size: 11px; font-weight: 600;">${escapeHtml(String(s.calc_Dispense_Qty))} ${escapeHtml(s.dispense_Qty_Unit || '')} • ${escapeHtml(s.route || '-')}</span>
                            ${statusBadge}
                        </div>
                        ${nurse ? `<div style="font-size: 10.5px; color: var(--color-text-muted); margin-top: 2px;">Admin: ${escapeHtml(nurse)}</div>` : ''}
                    </div>
                `;
            }).join('');
        }

        renderCards(shifts.morning, elements.shiftCardsMorning);
        renderCards(shifts.noon, elements.shiftCardsNoon);
        renderCards(shifts.evening, elements.shiftCardsEvening);
        renderCards(shifts.night, elements.shiftCardsNight);
    }

    function renderMarTable() {
        const items = state.filteredMarItems;
        if (items.length === 0) {
            elements.marTableBody.innerHTML = `
                <tr>
                    <td colspan="11" class="empty-state">
                        <div>No medication administration schedules match current filters.</div>
                    </td>
                </tr>
            `;
            return;
        }

        let html = '';
        items.forEach(item => {
            const timeStr = formatDateTime(item.schedule_Date, item.schedule_Time);
            const statusBadge = getStatusBadgeHtml(item.dispense_Status);

            let safetyBadges = '';
            if (item.isHighAlert) safetyBadges += '<span class="badge badge-high-alert">HIGH-ALERT</span> ';
            if (item.is_Controlled) safetyBadges += `<span class="badge badge-controlled">CTRL L${escapeHtml(item.control_Level || '1')}</span> `;
            if (item.isRefrigerationRequired) safetyBadges += '<span class="badge badge-cold">❄ 2-8°C</span> ';

            let notesText = '';
            if (item.dispense_Status === 'Returned') {
                notesText = `Ret: ${escapeHtml(item.return_Reason_Name || 'Fasting / NPO')}`;
            } else if (item.dispense_Status === 'Wasted') {
                notesText = `Wst: ${escapeHtml(item.waste_Reason_Name || 'Contaminated')} (Qty: ${item.waste_Qty || 1})`;
            } else if (item.instruction_Note) {
                notesText = escapeHtml(item.instruction_Note);
            } else if (item.wintness_Required === 'Y') {
                notesText = '<span style="color: var(--color-warning-text); font-weight: 600;">Dual Witness Req</span>';
            } else {
                notesText = '-';
            }

            html += `
                <tr class="clickable-row">
                    <td><strong>${timeStr}</strong></td>
                    <td>
                        <span class="bed-badge">${escapeHtml(item.station_Name || item.station_Code || '-')} • ${escapeHtml(item.bed_Name || item.bed_Code || '-')}</span>
                    </td>
                    <td>
                        <div style="font-weight: 700; color: var(--color-text-primary);">${escapeHtml(item.patient_Name || '-')}</div>
                        <div style="font-size: 11px; color: var(--color-text-muted);">MRN: ${escapeHtml(item.patient_Mrn || '-')}</div>
                    </td>
                    <td>
                        <div style="font-weight: 600; color: var(--color-primary); cursor: pointer;" onclick="window.inspectMedication('${escapeHtml(item.item_Code)}')">
                            ${escapeHtml(item.display_Name || item.item_Code)}
                        </div>
                        <div style="font-size: 11px; color: var(--color-text-muted);">${escapeHtml(item.generic_Name || '')}</div>
                        ${safetyBadges ? `<div style="margin-top: 3px;">${safetyBadges}</div>` : ''}
                    </td>
                    <td><code>${escapeHtml(item.item_Code || '-')}</code></td>
                    <td>
                        <strong>${escapeHtml(String(item.dosage_Ordered_Value))} ${escapeHtml(item.dosage_Ordered_Unit || '')}</strong>
                        <div style="font-size: 11px; color: var(--color-text-muted);">${escapeHtml(item.route || '-')}</div>
                    </td>
                    <td>${escapeHtml(item.frequency || '-')}</td>
                    <td>${statusBadge}</td>
                    <td>${escapeHtml(item.dispense_Nurse_Name || item.confirmed_Nurse_Name || '-')}</td>
                    <td style="font-size: 12px;">${notesText}</td>
                    <td style="text-align: center;">
                        <button type="button" class="btn-nav" style="padding: 2px 8px; font-size: 11px;" onclick="window.inspectSchedule('${escapeHtml(item.schedule_Id)}')">
                            Inspect
                        </button>
                    </td>
                </tr>
            `;
        });

        elements.marTableBody.innerHTML = html;
    }

    // =========================================================================
    // SLIDE-OVER RIGHT INSPECTOR DRAWER
    // =========================================================================
    window.inspectMedication = function (itemCode) {
        if (!state.snapshot || !itemCode) return;
        const meds = state.snapshot.medications || [];
        const med = meds.find(m => (m.item_Code || '').toLowerCase() === itemCode.toLowerCase()) || {
            item_Code: itemCode,
            display_Name: itemCode,
            generic_Name: 'N/A'
        };

        // Also check if any prescription order matches
        const activeRx = (state.snapshot.activePrescriptions || []).find(rx => (rx.medication_Code || '').toLowerCase() === itemCode.toLowerCase());

        elements.drawerDrugName.textContent = med.display_Name || med.item_Code;

        let safetyTags = '';
        if (med.isHighAlert) safetyTags += '<span class="badge badge-high-alert">HIGH-ALERT</span> ';
        if (med.is_Controlled) safetyTags += `<span class="badge badge-controlled">CONTROLLED L${escapeHtml(med.control_Level || '1')}</span> `;
        if (med.isRefrigerationRequired) safetyTags += '<span class="badge badge-cold">❄ 2-8°C COLD CHAIN</span> ';
        if (med.wintness_Required === 'Y') safetyTags += '<span class="badge badge-due">DUAL-WITNESS REQ</span> ';

        elements.drawerBody.innerHTML = `
            <!-- Formulary Monograph -->
            <div class="drawer-section">
                <div class="drawer-section-title">
                    <span>💊 Master Drug Formulary</span>
                </div>
                ${safetyTags ? `<div style="margin-bottom: 8px;">${safetyTags}</div>` : ''}
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Item Code</span>
                    <span class="drawer-info-value"><code>${escapeHtml(med.item_Code)}</code></span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Generic Name</span>
                    <span class="drawer-info-value">${escapeHtml(med.generic_Name || '-')}</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Brand / Display Name</span>
                    <span class="drawer-info-value">${escapeHtml(med.display_Name || '-')}</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">ATC Code</span>
                    <span class="drawer-info-value"><code>${escapeHtml(med.atc_Code || '-')}</code></span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Dosage Form</span>
                    <span class="drawer-info-value">${escapeHtml(med.form || '-')}</span>
                </div>
            </div>

            <!-- Physician Prescription Context -->
            ${activeRx ? `
                <div class="drawer-section">
                    <div class="drawer-section-title">
                        <span>📋 Active Physician Order</span>
                    </div>
                    <div class="drawer-info-row">
                        <span class="drawer-info-label">Order ID</span>
                        <span class="drawer-info-value"><code>${escapeHtml(activeRx.med_Order_Id || '-')}</code></span>
                    </div>
                    <div class="drawer-info-row">
                        <span class="drawer-info-label">Ordered Dosage</span>
                        <span class="drawer-info-value"><strong>${escapeHtml(String(activeRx.dosage_Ordered_Value))} ${escapeHtml(activeRx.dosage_Ordered_Unit || '')}</strong></span>
                    </div>
                    <div class="drawer-info-row">
                        <span class="drawer-info-label">Frequency &amp; Route</span>
                        <span class="drawer-info-value">${escapeHtml(activeRx.frequency || '-')} • ${escapeHtml(activeRx.route || '-')}</span>
                    </div>
                    <div class="drawer-info-row">
                        <span class="drawer-info-label">Clinical Indication</span>
                        <span class="drawer-info-value">${escapeHtml(activeRx.clinical_Indication || '-')}</span>
                    </div>
                    <div class="drawer-info-row">
                        <span class="drawer-info-label">Order Start</span>
                        <span class="drawer-info-value">${formatDate(activeRx.start_Datetime || activeRx.order_Start_Datetime)}</span>
                    </div>
                </div>
            ` : ''}
        `;

        openInspectorDrawer();

        // Also update legacy modal for backward compatibility
        populateLegacyModal(med);
    };

    window.inspectSchedule = function (scheduleId) {
        if (!state.snapshot || !scheduleId) return;
        const schedule = (state.snapshot.marSchedules || []).find(s => s.schedule_Id === scheduleId);
        if (!schedule) return;

        elements.drawerDrugName.textContent = schedule.display_Name || schedule.item_Code;

        let safetyTags = '';
        if (schedule.isHighAlert) safetyTags += '<span class="badge badge-high-alert">HIGH-ALERT</span> ';
        if (schedule.is_Controlled) safetyTags += `<span class="badge badge-controlled">CTRL L${escapeHtml(schedule.control_Level || '1')}</span> `;
        if (schedule.isRefrigerationRequired) safetyTags += '<span class="badge badge-cold">❄ 2-8°C</span> ';
        if (schedule.wintness_Required === 'Y') safetyTags += '<span class="badge badge-due">DUAL-WITNESS REQ</span> ';

        const statusBadge = getStatusBadgeHtml(schedule.dispense_Status);

        elements.drawerBody.innerHTML = `
            <!-- Scheduled Administration Context -->
            <div class="drawer-section">
                <div class="drawer-section-title">
                    <span>⏱ Administration Dose Schedule</span>
                </div>
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
                    <span style="font-size: 15px; font-weight: 800; color: var(--color-text-primary);">${formatDateTime(schedule.schedule_Date, schedule.schedule_Time)}</span>
                    ${statusBadge}
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Patient &amp; MRN</span>
                    <span class="drawer-info-value">${escapeHtml(schedule.patient_Name || '-')} (${escapeHtml(schedule.patient_Mrn || '-')})</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Station &amp; Bed</span>
                    <span class="drawer-info-value">${escapeHtml(schedule.station_Name || schedule.station_Code || '-')}: ${escapeHtml(schedule.bed_Name || schedule.bed_Code || '-')}</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Scheduled Dose</span>
                    <span class="drawer-info-value"><strong>${escapeHtml(String(schedule.calc_Dispense_Qty))} ${escapeHtml(schedule.dispense_Qty_Unit || '')}</strong> (${escapeHtml(schedule.route || '-')})</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Instruction Notes</span>
                    <span class="drawer-info-value">${escapeHtml(schedule.instruction_Note || 'Standard bedside administration')}</span>
                </div>
            </div>

            <!-- Dispense & Nurse Audit Trail -->
            <div class="drawer-section">
                <div class="drawer-section-title">
                    <span>🏥 AMiS Dispense &amp; Nursing Audit</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Administering Nurse</span>
                    <span class="drawer-info-value"><strong>${escapeHtml(schedule.dispense_Nurse_Name || schedule.confirmed_Nurse_Name || 'Pending Nurse')}</strong></span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">AMiS Cart Dispense Time</span>
                    <span class="drawer-info-value">${schedule.dispense_Time ? formatDateTime(schedule.dispense_Time) : 'Not Dispensed'}</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Bedside Confirmed Time</span>
                    <span class="drawer-info-value">${schedule.confirmed_Time ? formatDateTime(schedule.confirmed_Time) : 'Unconfirmed'}</span>
                </div>
                ${schedule.dispense_Status === 'Returned' ? `
                    <div class="drawer-info-row" style="color: var(--color-purple);">
                        <span class="drawer-info-label">Return Reason</span>
                        <span class="drawer-info-value"><strong>${escapeHtml(schedule.return_Reason_Name || 'Patient Fasting / NPO')}</strong> (Qty: ${schedule.return_Qty || 1})</span>
                    </div>
                ` : ''}
                ${schedule.dispense_Status === 'Wasted' ? `
                    <div class="drawer-info-row" style="color: var(--color-orange);">
                        <span class="drawer-info-label">Waste Reason</span>
                        <span class="drawer-info-value"><strong>${escapeHtml(schedule.waste_Reason_Name || 'Contaminated / Dropped')}</strong> (Qty: ${schedule.waste_Qty || 1})</span>
                    </div>
                ` : ''}
            </div>

            <!-- Drug Formulary Monograph -->
            <div class="drawer-section">
                <div class="drawer-section-title">
                    <span>💊 Medication Information</span>
                </div>
                ${safetyTags ? `<div style="margin-bottom: 8px;">${safetyTags}</div>` : ''}
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Item Code</span>
                    <span class="drawer-info-value"><code>${escapeHtml(schedule.item_Code)}</code></span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">Generic Name</span>
                    <span class="drawer-info-value">${escapeHtml(schedule.generic_Name || '-')}</span>
                </div>
                <div class="drawer-info-row">
                    <span class="drawer-info-label">ATC Code</span>
                    <span class="drawer-info-value"><code>${escapeHtml(schedule.atc_Code || '-')}</code></span>
                </div>
            </div>
        `;

        openInspectorDrawer();
    };

    function openInspectorDrawer() {
        elements.inspectorDrawer.classList.add('open');
        elements.drawerBackdrop.classList.add('open');
    }

    function closeInspectorDrawer() {
        elements.inspectorDrawer.classList.remove('open');
        elements.drawerBackdrop.classList.remove('open');
    }

    // Legacy Medication Modal (Test compatibility)
    window.viewDrugDetails = function (itemCode) {
        window.inspectMedication(itemCode);
    };

    function populateLegacyModal(med) {
        elements.modalMedTitle.textContent = `${med.display_Name || med.item_Code} - Formulary Monograph`;
        elements.modalMedBody.innerHTML = `
            <div class="drawer-info-row">
                <span class="drawer-info-label">Generic Name:</span>
                <span class="drawer-info-value">${escapeHtml(med.generic_Name || '-')}</span>
            </div>
            <div class="drawer-info-row">
                <span class="drawer-info-label">Item Code:</span>
                <span class="drawer-info-value"><code>${escapeHtml(med.item_Code || '-')}</code></span>
            </div>
            <div class="drawer-info-row">
                <span class="drawer-info-label">ATC Code:</span>
                <span class="drawer-info-value"><code>${escapeHtml(med.atc_Code || '-')}</code></span>
            </div>
            <div class="drawer-info-row">
                <span class="drawer-info-label">Form:</span>
                <span class="drawer-info-value">${escapeHtml(med.form || '-')}</span>
            </div>
        `;
    }

    function closeMedModal() {
        elements.medicationModal.classList.remove('open');
    }

    // Settings Modal
    function openSettingsModal() {
        elements.settingsModal.classList.add('open');
    }

    function closeSettingsModal() {
        elements.settingsModal.classList.remove('open');
    }

    async function testDbConnection() {
        elements.btnTestDbConnection.disabled = true;
        elements.btnTestDbConnection.textContent = 'Testing...';
        try {
            const res = await window.ipc.invoke('testDbConnection');
            if (res && res.success) {
                elements.cfgStatusDisplay.textContent = 'Connected (Verified PostgreSQL 17 Read-Only)';
                elements.cfgStatusDisplay.style.color = 'var(--color-success)';
                showToast('Database connection verified', 'success');
            } else {
                elements.cfgStatusDisplay.textContent = 'Connection Failed: ' + (res?.message || 'Unknown');
                elements.cfgStatusDisplay.style.color = 'var(--color-danger)';
                showToast('Database test failed', 'error');
            }
        } catch (err) {
            elements.cfgStatusDisplay.textContent = 'Connection Error: ' + err.message;
            elements.cfgStatusDisplay.style.color = 'var(--color-danger)';
            showToast('Connection error: ' + err.message, 'error');
        } finally {
            elements.btnTestDbConnection.disabled = false;
            elements.btnTestDbConnection.textContent = 'Test Connection';
        }
    }

    // Export Modal
    function openExportModal() {
        const schedCount = state.snapshot?.marSchedules?.length ?? 385;
        const rxCount = state.snapshot?.activePrescriptions?.length ?? 31;
        elements.exportFilteredCount.textContent = schedCount;
        elements.exportTotalCount.textContent = rxCount;
        elements.exportModal.classList.add('open');
    }

    function closeExportModal() {
        elements.exportModal.classList.remove('open');
    }

    async function executeExcelExport() {
        elements.btnConfirmExport.disabled = true;
        elements.btnConfirmExport.textContent = 'Exporting...';

        try {
            const result = await window.ipc.invoke('exportExcel');
            closeExportModal();
            showToast(`Excel Workbook generated (${result.recordCount} rows): ${result.filename || 'MedHub_Prescriptions.xlsx'}`, 'success');
        } catch (err) {
            console.error('[App] Excel export failed:', err);
            showToast('Export failed: ' + err.message, 'error');
        } finally {
            elements.btnConfirmExport.disabled = false;
            elements.btnConfirmExport.textContent = 'Download Excel Report';
        }
    }

    // Helpers
    function getShiftForTime(timeStr) {
        if (!timeStr) return 'morning';
        const parts = timeStr.split(':');
        const hour = parseInt(parts[0], 10);
        if (hour >= 6 && hour < 12) return 'morning';
        if (hour >= 12 && hour < 17) return 'noon';
        if (hour >= 17 && hour < 21) return 'evening';
        return 'night';
    }

    function getStatusBadgeHtml(status) {
        const s = status || 'Unknown';
        if (s === 'Dispensed') {
            return '<span class="badge badge-dispensed">Dispensed</span>';
        } else if (s === 'Pending (Due)' || s.toLowerCase().includes('due')) {
            return '<span class="badge badge-due">Due Now ⚠</span>';
        } else if (s === 'Pending (Future)' || s.toLowerCase().includes('future') || s.toLowerCase().includes('pending')) {
            return '<span class="badge badge-pending">Pending</span>';
        } else if (s === 'Returned') {
            return '<span class="badge badge-returned">Returned</span>';
        } else if (s === 'Wasted') {
            return '<span class="badge badge-wasted">Wasted</span>';
        } else if (s === 'Cancelled') {
            return '<span class="badge badge-cancelled">Cancelled</span>';
        }
        return `<span class="badge">${escapeHtml(s)}</span>`;
    }

    function formatDate(dateStr) {
        if (!dateStr) return '-';
        return dateStr.slice(0, 10);
    }

    function formatTimeOnly(timeStr) {
        if (!timeStr) return '--:--';
        return timeStr.slice(0, 5);
    }

    function formatDateTime(dateStr, timeStr) {
        if (!dateStr) return '-';
        const d = dateStr.slice(0, 10);
        if (timeStr) {
            return `${d} ${timeStr.slice(0, 5)}`;
        }
        return d;
    }

    function showToast(message, type = 'info') {
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        toast.textContent = message;
        elements.toastContainer.appendChild(toast);

        setTimeout(() => {
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 300);
        }, 4000);
    }

    function escapeHtml(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }
})();
