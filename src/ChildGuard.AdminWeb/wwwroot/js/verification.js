// Student Drop-off & Pickup Verification demo page.
// Thin AJAX client for the VerificationController proxy endpoints — no business
// logic lives here, it only reflects what the API returns. See Views/Verification/Index.cshtml.
(function () {
    const PICKUP_CHECKPOINTS = ['SchoolDeparture', 'BusDropoff'];
    let currentStudentId = null;
    let currentGuardianId = null;
    let currentVerificationId = null;
    let stream = null;
    let busy = false;

    function setStep(id, state) {
        const el = document.getElementById(id);
        if (el) el.className = 'cg-step' + (state ? ' cg-step--' + state : '');
    }

    function isPickupCheckpoint() {
        return PICKUP_CHECKPOINTS.includes(document.getElementById('checkpointSelect').value);
    }

    function updateFlowMode() {
        const pickup = isPickupCheckpoint();
        document.getElementById('pickupStepper').hidden = !pickup;
        document.getElementById('pickupCard').style.display = pickup ? '' : 'none';
        document.getElementById('checkpointHint').textContent = pickup
            ? 'Pickup checkpoint — scanning identifies the student, then an authorized guardian must pass face verification before the pickup is recorded.'
            : 'Drop-off checkpoint — scanning identifies the student and records the event. No guardian verification needed.';
    }

    function setBusy(isBusy, label) {
        busy = isBusy;
        const btn = document.getElementById('scanBtn');
        btn.disabled = isBusy;
        btn.textContent = isBusy && label ? label : 'Scan RFID Card';
    }

    /** Clears everything from the previous student/guardian so a fresh scan never
        shows stale green steps from someone else's completed pickup. */
    function resetForNewScan() {
        currentStudentId = null;
        currentGuardianId = null;
        currentVerificationId = null;
        ['step-student', 'step-event', 'step-guardian', 'step-result'].forEach(id => setStep(id, null));
        document.getElementById('studentCard').hidden = true;
        document.getElementById('studentMessage').innerHTML = '';
        document.getElementById('guardianSelect').innerHTML = '<option value="">Scan a student first...</option>';
        document.getElementById('guardianAuthBadge').className = 'cg-badge cg-badge--neutral';
        document.getElementById('guardianAuthBadge').textContent = 'Pending scan';
        document.getElementById('faceResult').innerHTML = '';
        document.getElementById('decisionResult').innerHTML = '';
        document.getElementById('approveBtn').disabled = true;
        document.getElementById('rejectBtn').disabled = true;
        document.getElementById('captureBtn').disabled = true;
        if (stream) {
            stream.getTracks().forEach(t => t.stop());
            stream = null;
        }
        document.getElementById('cameraBox').innerHTML = 'Camera not started';
        document.getElementById('cardUidInput').value = '';
        document.getElementById('cardUidInput').focus();
    }

    function setScannerBadge(connected) {
        const badge = document.getElementById('scannerStatusBadge');
        const deviceLabel = document.getElementById('deviceSelect').selectedOptions[0]?.text.split(' (')[0] || 'Scanner';
        if (connected) {
            badge.className = 'cg-badge cg-badge--success';
            badge.textContent = deviceLabel + ' • Connected';
        } else {
            badge.className = 'cg-badge cg-badge--error';
            badge.textContent = deviceLabel + ' • Disconnected';
        }
    }

    async function connectScanner() {
        const deviceId = parseInt(document.getElementById('deviceSelect').value);
        if (!deviceId) return;
        try {
            const resp = await fetch('/Verification/Heartbeat?deviceId=' + deviceId, { method: 'POST' });
            setStep('step-scanner', resp.ok ? 'done' : 'failed');
            setScannerBadge(resp.ok);
        } catch {
            setStep('step-scanner', 'failed');
            setScannerBadge(false);
        }
    }

    async function doScan() {
        if (busy) return;
        const cardUid = document.getElementById('cardUidInput').value.trim();
        if (!cardUid) return;
        resetForNewScan();
        document.getElementById('cardUidInput').value = cardUid;

        const rfidDeviceId = parseInt(document.getElementById('deviceSelect').value);
        const checkpoint = document.getElementById('checkpointSelect').value;

        setBusy(true, 'Scanning...');
        setStep('step-scanner', 'active');
        let data;
        try {
            const resp = await fetch('/Verification/Scan', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ cardUid, rfidDeviceId, checkpoint })
            });
            data = await resp.json();
        } catch {
            setStep('step-scanner', 'failed');
            setBusy(false);
            return;
        }
        setBusy(false);
        document.getElementById('cardUidInput').value = '';
        setScannerBadge(true);

        const card = document.getElementById('studentCard');
        card.hidden = false;
        const msgDiv = document.getElementById('studentMessage');

        if (data.studentIdentified) {
            setStep('step-student', 'done');
            document.getElementById('studentName').textContent = data.studentName;
            document.getElementById('studentRfid').textContent = 'RFID: ' + data.maskedCardUid;
            currentStudentId = data.studentId;
            setStep('step-event', data.eventRecorded ? 'done' : 'failed');
            if (isPickupCheckpoint()) loadGuardians(currentStudentId);
        } else {
            setStep('step-student', 'failed');
            document.getElementById('studentName').textContent = 'Not identified';
            document.getElementById('studentRfid').textContent = data.maskedCardUid ? ('RFID: ' + data.maskedCardUid) : '';
        }
        const cls = data.eventRecorded ? 'cg-alert--success' : 'cg-alert--warning';
        const icon = data.eventRecorded ? '✓' : '⚠';
        msgDiv.innerHTML = '<div class="cg-alert ' + cls + '">' + icon + ' ' + data.message + '</div>';
    }

    async function loadGuardians(studentId) {
        const sel = document.getElementById('guardianSelect');
        const badge = document.getElementById('guardianAuthBadge');
        sel.innerHTML = '<option value="">Loading...</option>';
        const resp = await fetch('/Verification/StudentGuardians?studentId=' + studentId);
        const guardians = await resp.json();
        if (!guardians.length) {
            sel.innerHTML = '<option value="">No authorized guardian on file</option>';
            badge.className = 'cg-badge cg-badge--error';
            badge.textContent = 'No authorized guardian';
            return;
        }
        sel.innerHTML = guardians.map(g => `<option value="${g.guardianId}">${g.fullName} (${g.relationship || 'Guardian'})</option>`).join('');
        currentGuardianId = guardians[0].guardianId;
        badge.className = 'cg-badge cg-badge--success';
        badge.textContent = 'Authorized Guardian ✓';
        sel.addEventListener('change', () => currentGuardianId = parseInt(sel.value));
    }

    function resetActionButtons() {
        document.getElementById('approveBtn').disabled = true;
        document.getElementById('rejectBtn').disabled = true;
        currentVerificationId = null;
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.getElementById('checkpointSelect').addEventListener('change', updateFlowMode);
        updateFlowMode();

        document.getElementById('newScanBtn').addEventListener('click', resetForNewScan);
        document.getElementById('cardUidInput').addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); doScan(); }
        });
        document.getElementById('scanBtn').addEventListener('click', doScan);
        document.getElementById('deviceSelect').addEventListener('change', connectScanner);
        connectScanner();

        document.getElementById('startCameraBtn').addEventListener('click', async () => {
            try {
                stream = await navigator.mediaDevices.getUserMedia({ video: true });
                const box = document.getElementById('cameraBox');
                box.innerHTML = '';
                const video = document.createElement('video');
                video.autoplay = true; video.style.width = '100%'; video.style.height = '100%'; video.style.objectFit = 'cover'; video.style.borderRadius = '8px';
                video.srcObject = stream;
                box.appendChild(video);
                document.getElementById('captureBtn').disabled = false;
            } catch (err) {
                document.getElementById('cameraBox').textContent = 'Camera unavailable: ' + err.message;
            }
        });

        document.getElementById('captureBtn').addEventListener('click', async () => {
            if (busy) return;
            if (!currentStudentId || !currentGuardianId) {
                document.getElementById('faceResult').innerHTML = '<div class="cg-alert cg-alert--error">Scan a student and select an authorized guardian first.</div>';
                return;
            }
            const video = document.querySelector('#cameraBox video');
            const canvas = document.getElementById('captureCanvas');
            canvas.width = video.videoWidth; canvas.height = video.videoHeight;
            canvas.getContext('2d').drawImage(video, 0, 0);
            const imageBase64 = canvas.toDataURL('image/jpeg').split(',')[1];

            const captureBtn = document.getElementById('captureBtn');
            busy = true; captureBtn.disabled = true; captureBtn.textContent = 'Verifying...';
            let data;
            try {
                const resp = await fetch('/Verification/FaceVerify', {
                    method: 'POST', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ studentId: currentStudentId, guardianId: currentGuardianId, imageBase64 })
                });
                data = await resp.json();
            } finally {
                busy = false; captureBtn.disabled = false; captureBtn.textContent = 'Capture & Verify Face';
            }
            const resultDiv = document.getElementById('faceResult');

            if (!data.guardianAuthorized) {
                setStep('step-guardian', 'failed');
                resultDiv.innerHTML = '<div class="cg-alert cg-alert--error">' + data.message + '</div>';
                return;
            }
            if (data.faceMatched) {
                setStep('step-guardian', 'done');
                resultDiv.innerHTML = '<div class="cg-alert cg-alert--success" style="font-weight:700;font-size:15px;">✓ FACE VERIFIED (score ' + data.matchScore.toFixed(2) + ')</div>';
                currentVerificationId = data.verificationId;
                document.getElementById('approveBtn').disabled = false;
                document.getElementById('rejectBtn').disabled = false;
            } else {
                setStep('step-guardian', 'failed');
                resultDiv.innerHTML = '<div class="cg-alert cg-alert--error" style="font-weight:700;font-size:15px;">✗ ' + data.message + '</div>';
                currentVerificationId = data.verificationId || null;
                document.getElementById('rejectBtn').disabled = !currentVerificationId;
            }
        });

        document.getElementById('approveBtn').addEventListener('click', async () => {
            const resp = await fetch('/Verification/Approve', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ verificationId: currentVerificationId })
            });
            const data = await resp.json();
            const div = document.getElementById('decisionResult');
            if (data.approved) {
                setStep('step-result', 'done');
                div.innerHTML = '<div class="cg-alert cg-alert--success">' + data.message + ' (' + data.recordedEventType + ')</div>';
            } else {
                setStep('step-result', 'failed');
                div.innerHTML = '<div class="cg-alert cg-alert--error">' + (data.message || 'Could not approve pickup.') + '</div>';
            }
            resetActionButtons();
        });

        document.getElementById('rejectBtn').addEventListener('click', async () => {
            const resp = await fetch('/Verification/Reject', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ verificationId: currentVerificationId, reason: 'Rejected by admin at verification desk.' })
            });
            const data = await resp.json();
            setStep('step-result', 'failed');
            document.getElementById('decisionResult').innerHTML = '<div class="cg-alert cg-alert--warning">' + data.message + '</div>';
            resetActionButtons();
        });
    });
})();
