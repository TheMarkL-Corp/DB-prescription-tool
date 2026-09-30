/**
 * MedHub Desktop IPC Bridge
 * Asynchronous Promise-based bridge connecting WebView2 / Photino to C# Host.
 */

(function () {
    const pendingRequests = new Map();
    const defaultTimeoutMs = 30000;

    // Initialize receiver if Photino window.external is present
    if (window.external && typeof window.external.receiveMessage === 'function') {
        window.external.receiveMessage(function (rawMessage) {
            try {
                const message = typeof rawMessage === 'string' ? JSON.parse(rawMessage) : rawMessage;
                if (message && message.requestId) {
                    const pending = pendingRequests.get(message.requestId);
                    if (pending) {
                        clearTimeout(pending.timer);
                        pendingRequests.delete(message.requestId);
                        if (message.success) {
                            pending.resolve(message.data);
                        } else {
                            pending.reject(new Error(message.error || 'Unknown IPC backend error'));
                        }
                    }
                }
            } catch (err) {
                console.error('[IPC Bridge] Failed to process incoming message:', err, rawMessage);
            }
        });
    }

    /**
     * Invokes a backend C# action through Photino IPC.
     * @param {string} action The name of the backend action (e.g. 'loadSnapshot', 'filterMar', 'exportExcel')
     * @param {object} payload The payload object to pass to the backend
     * @param {number} timeoutMs Optional timeout in milliseconds (defaults to 30000)
     * @returns {Promise<any>}
     */
    function invoke(action, payload = null, timeoutMs = defaultTimeoutMs) {
        return new Promise((resolve, reject) => {
            const requestId = 'req_' + Date.now() + '_' + Math.random().toString(36).substring(2, 9);

            const timer = setTimeout(() => {
                if (pendingRequests.has(requestId)) {
                    pendingRequests.delete(requestId);
                    reject(new Error(`IPC request '${action}' timed out after ${timeoutMs}ms`));
                }
            }, timeoutMs);

            pendingRequests.set(requestId, { resolve, reject, timer });

            const requestObj = {
                requestId: requestId,
                action: action,
                payload: payload
            };

            // 1. If running on HTTP/HTTPS (EmbeddedWebServer), use direct fast REST API
            if (window.location.protocol.startsWith('http')) {
                fetch('/api/' + action, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(requestObj)
                })
                .then(res => {
                    if (!res.ok) throw new Error(`HTTP error ${res.status}`);
                    return res.json();
                })
                .then(resData => {
                    clearTimeout(timer);
                    pendingRequests.delete(requestId);
                    if (resData && resData.success) {
                        resolve(resData.data);
                    } else {
                        reject(new Error(resData?.error || 'Unknown server error'));
                    }
                })
                .catch(fetchErr => {
                    console.warn(`[IPC Bridge] HTTP fetch failed for '${action}', trying mock fallback:`, fetchErr);
                    handleMockResponse(action, payload, resolve, reject, timer, requestId);
                });
                return;
            }

            // 2. Check if running inside Photino custom scheme (app://)
            if (window.external && typeof window.external.sendMessage === 'function') {
                try {
                    window.external.sendMessage(JSON.stringify(requestObj));
                } catch (sendErr) {
                    clearTimeout(timer);
                    pendingRequests.delete(requestId);
                    reject(sendErr);
                }
                return;
            }

            if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
                try {
                    window.chrome.webview.postMessage(JSON.stringify(requestObj));
                } catch (sendErr) {
                    clearTimeout(timer);
                    pendingRequests.delete(requestId);
                    reject(sendErr);
                }
                return;
            }

            // 3. Fallback for static file:// preview
            handleMockResponse(action, payload, resolve, reject, timer, requestId);
        });
    }

    function handleMockResponse(action, payload, resolve, reject, timer, requestId) {
        setTimeout(() => {
            clearTimeout(timer);
            pendingRequests.delete(requestId);

            if (action === 'testDbConnection') {
                resolve({ success: true, message: 'Offline Demo Mode: PostgreSQL 17 on 127.0.0.1:5433 (amisdbv2)' });
            } else if (action === 'loadSnapshot' || action === 'refreshData') {
                const now = new Date();
                const nowIso = now.toISOString();
                resolve({
                    kpiSummary: {
                        totalInpatients: 15,
                        activeOrders: 31,
                        dispensedCount: 13,
                        duePendingCount: 18,
                        totalPendingCount: 370,
                        returnedCount: 1,
                        wastedCount: 1,
                        controlledCount: 143
                    },
                    patients: [
                        {
                            patientId: 'P001',
                            patientMrn: 'MRN-78012',
                            firstName: 'David',
                            lastName: 'Chen',
                            sex: 'M',
                            dateOfBirth: '1965-04-12',
                            weightKg: 72.5,
                            heightCm: 174,
                            allergy: 'Penicillin, NSAIDs',
                            allergyDesc: 'Anaphylaxis',
                            drugWarn: 'Warfarin interaction',
                            drugWarnDesc: 'Elevated INR risk',
                            encounterId: 'E101',
                            caseNo: 'INP-2026-0891',
                            admissionDate: '2026-09-20',
                            dischargeDate: null,
                            encounterStatus: '01',
                            attendingPhysician: 'Dr. Sarah Lin',
                            stationId: 'CS1',
                            stationCode: 'ICU-1',
                            stationName: 'Intensive Care Unit',
                            bedId: 'B01',
                            rbCode: 'ICU-B01',
                            rbName: 'Bed 01',
                            totalPrescriptions: 4,
                            totalScheduledDoses: 16,
                            dispensedDoses: 6,
                            pendingDoses: 10
                        },
                        {
                            patientId: 'P002',
                            patientMrn: 'MRN-89043',
                            firstName: 'Emily',
                            lastName: 'Watson',
                            sex: 'F',
                            dateOfBirth: '1982-11-23',
                            weightKg: 64.0,
                            heightCm: 165,
                            allergy: 'None',
                            allergyDesc: '',
                            drugWarn: null,
                            drugWarnDesc: null,
                            encounterId: 'E102',
                            caseNo: 'INP-2026-0895',
                            admissionDate: '2026-09-22',
                            dischargeDate: null,
                            encounterStatus: '01',
                            attendingPhysician: 'Dr. Michael Roberts',
                            stationId: 'CS2',
                            stationCode: 'CARD-3',
                            stationName: 'Cardiology Ward 3',
                            bedId: 'B05',
                            rbCode: 'C3-305',
                            rbName: 'Room 305 Bed A',
                            totalPrescriptions: 3,
                            totalScheduledDoses: 12,
                            dispensedDoses: 4,
                            pendingDoses: 8
                        }
                    ],
                    marSchedules: [
                        {
                            scheduleId: 'S101',
                            patientId: 'P001',
                            patientMrn: 'MRN-78012',
                            patientName: 'David Chen',
                            firstLastName: 'Chen, David',
                            encounterId: 'E101',
                            caseNo: 'INP-2026-0891',
                            stationCode: 'ICU-1',
                            stationName: 'Intensive Care Unit',
                            rbCode: 'ICU-B01',
                            rbName: 'Bed 01',
                            medOrderId: 'ORD-001',
                            medicationId: 'MED-01',
                            itemCode: 'CEFTRX-1G',
                            genericName: 'Ceftriaxone Sodium',
                            displayName: 'Ceftriaxone 1g IV Infusion',
                            dosageOrderedValue: 1.0,
                            dosageOrderedUnit: 'g',
                            frequency: 'Q12H',
                            route: 'IV Infusion',
                            orderStatus: '01',
                            scheduleDate: nowIso.substring(0, 10),
                            scheduleTime: '08:00:00',
                            scheduledDateTime: `${nowIso.substring(0, 10)} 08:00:00`,
                            calcDispenseQty: 1.0,
                            dispenseQtyUnit: 'Vial',
                            computedStatus: 'Dispensed',
                            dispenseTime: `${nowIso.substring(0, 10)} 08:02:15`,
                            dispenseNurseName: 'Nurse Jennifer Lee',
                            confirmedTime: `${nowIso.substring(0, 10)} 08:05:00`,
                            confirmedNurseName: 'Nurse Jennifer Lee',
                            isHighAlert: false,
                            isControlled: false,
                            controlLevel: null,
                            isRefrigerationRequired: true,
                            witnessRequired: false,
                            allergy: 'Penicillin, NSAIDs',
                            drugWarn: 'Warfarin interaction',
                            clinicalIndication: 'Severe Sepsis Empirical Coverage',
                            instructionNote: 'Infuse over 30 minutes in 50ml NS'
                        },
                        {
                            scheduleId: 'S102',
                            patientId: 'P001',
                            patientMrn: 'MRN-78012',
                            patientName: 'David Chen',
                            firstLastName: 'Chen, David',
                            encounterId: 'E101',
                            caseNo: 'INP-2026-0891',
                            stationCode: 'ICU-1',
                            stationName: 'Intensive Care Unit',
                            rbCode: 'ICU-B01',
                            rbName: 'Bed 01',
                            medOrderId: 'ORD-002',
                            medicationId: 'MED-02',
                            itemCode: 'FENT-100MCG',
                            genericName: 'Fentanyl Citrate',
                            displayName: 'Fentanyl 100mcg/2ml Ampoule',
                            dosageOrderedValue: 50.0,
                            dosageOrderedUnit: 'mcg',
                            frequency: 'PRN Q4H',
                            route: 'IV Push',
                            orderStatus: '01',
                            scheduleDate: nowIso.substring(0, 10),
                            scheduleTime: '12:00:00',
                            scheduledDateTime: `${nowIso.substring(0, 10)} 12:00:00`,
                            calcDispenseQty: 1.0,
                            dispenseQtyUnit: 'Ampoule',
                            computedStatus: 'Pending (Due)',
                            dispenseTime: null,
                            dispenseNurseName: null,
                            confirmedTime: null,
                            confirmedNurseName: null,
                            isHighAlert: true,
                            isControlled: true,
                            controlLevel: 'Schedule II',
                            isRefrigerationRequired: false,
                            witnessRequired: true,
                            allergy: 'Penicillin, NSAIDs',
                            drugWarn: 'Warfarin interaction',
                            clinicalIndication: 'Post-op pain management',
                            instructionNote: 'Requires dual-nurse witness prior to administration'
                        },
                        {
                            scheduleId: 'S103',
                            patientId: 'P001',
                            patientMrn: 'MRN-78012',
                            patientName: 'David Chen',
                            firstLastName: 'Chen, David',
                            encounterId: 'E101',
                            caseNo: 'INP-2026-0891',
                            stationCode: 'ICU-1',
                            stationName: 'Intensive Care Unit',
                            rbCode: 'ICU-B01',
                            rbName: 'Bed 01',
                            medOrderId: 'ORD-001',
                            medicationId: 'MED-01',
                            itemCode: 'CEFTRX-1G',
                            genericName: 'Ceftriaxone Sodium',
                            displayName: 'Ceftriaxone 1g IV Infusion',
                            dosageOrderedValue: 1.0,
                            dosageOrderedUnit: 'g',
                            frequency: 'Q12H',
                            route: 'IV Infusion',
                            orderStatus: '01',
                            scheduleDate: nowIso.substring(0, 10),
                            scheduleTime: '20:00:00',
                            scheduledDateTime: `${nowIso.substring(0, 10)} 20:00:00`,
                            calcDispenseQty: 1.0,
                            dispenseQtyUnit: 'Vial',
                            computedStatus: 'Pending (Future)',
                            dispenseTime: null,
                            dispenseNurseName: null,
                            confirmedTime: null,
                            confirmedNurseName: null,
                            isHighAlert: false,
                            isControlled: false,
                            controlLevel: null,
                            isRefrigerationRequired: true,
                            witnessRequired: false,
                            allergy: 'Penicillin, NSAIDs',
                            drugWarn: 'Warfarin interaction',
                            clinicalIndication: 'Severe Sepsis Empirical Coverage',
                            instructionNote: 'Infuse over 30 minutes in 50ml NS'
                        }
                    ],
                    activePrescriptions: [
                        {
                            medOrderId: 'ORD-001',
                            patientMrn: 'MRN-78012',
                            patientName: 'David Chen',
                            itemCode: 'CEFTRX-1G',
                            displayName: 'Ceftriaxone 1g IV Infusion',
                            dosageOrderedValue: 1.0,
                            dosageOrderedUnit: 'g',
                            frequency: 'Q12H',
                            route: 'IV Infusion',
                            orderStatus: '01',
                            startDatetime: `${nowIso.substring(0, 10)} 08:00:00`,
                            endDatetime: null,
                            clinicalIndication: 'Severe Sepsis Empirical Coverage'
                        },
                        {
                            medOrderId: 'ORD-002',
                            patientMrn: 'MRN-78012',
                            patientName: 'David Chen',
                            itemCode: 'FENT-100MCG',
                            displayName: 'Fentanyl 100mcg/2ml Ampoule',
                            dosageOrderedValue: 50.0,
                            dosageOrderedUnit: 'mcg',
                            frequency: 'PRN Q4H',
                            route: 'IV Push',
                            orderStatus: '01',
                            startDatetime: `${nowIso.substring(0, 10)} 10:00:00`,
                            endDatetime: null,
                            clinicalIndication: 'Post-op pain management'
                        }
                    ],
                    medications: [],
                    stations: [
                        { stationId: 'CS1', stationCode: 'ICU-1', stationName: 'Intensive Care Unit' },
                        { stationId: 'CS2', stationCode: 'CARD-3', stationName: 'Cardiology Ward 3' }
                    ],
                    refreshedAt: new Date().toLocaleTimeString()
                });
            } else if (action === 'exportExcel') {
                resolve({ filePath: 'MedHub_Prescriptions_Demo.xlsx', recordCount: 3 });
            } else {
                resolve({ status: 'mock_ok' });
            }
        }, 100);
    }

    // Expose on global window object
    window.ipc = {
        invoke: invoke
    };

    console.log('[IPC Bridge] Initialized successfully (HTTP-first mode enabled)');
})();
