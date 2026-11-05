/* ==========================================================================
   Pickup & Drop-off desk.
   A thin client over the VerificationController proxy endpoints: it reflects
   what the API returns and never decides an outcome itself. Every failure is
   reported with what failed, why, and what the operator should do next.
   ========================================================================== */
(function () {
  'use strict';

  /** Checkpoints that hand a child to a person, and therefore need guardian verification. */
  var PICKUP_CHECKPOINTS = ['SchoolDeparture', 'BusDropoff'];

  var state = {
    studentId: null,
    studentName: null,
    guardianId: null,
    verificationId: null,
    stream: null,
    busy: false
  };

  function $(id) { return document.getElementById(id); }

  function icon(name) {
    var paths = {
      check: '<path d="M20 6 9 17l-5-5"/>',
      x: '<path d="M18 6 6 18M6 6l12 12"/>',
      alert: '<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/><path d="M12 9v4M12 17h.01"/>',
      info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>'
    };
    return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
      'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (paths[name] || paths.info) + '</svg>';
  }

  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  // ------------------------------------------------------------------ Steps
  function setStep(id, kind, stateText) {
    var el = $(id);
    if (!el) return;
    el.className = 'cg-step' + (kind ? ' cg-step--' + kind : '');
    var label = el.querySelector('[data-state]');
    if (label && stateText != null) label.textContent = stateText;
  }

  function resetSteps(pickup) {
    setStep('step-student', null, 'Waiting for a card');
    var notNeeded = pickup ? 'Not started' : 'Not required here';
    setStep('step-guardian', null, notNeeded);
    setStep('step-face', null, notNeeded);
    setStep('step-decision', null, notNeeded);
    setStep('step-done', null, 'Not started');
  }

  // ----------------------------------------------------------------- Panels
  function banner(kind, title, body, action) {
    var cls = { success: 'cg-alert--success', danger: 'cg-alert--danger', warning: 'cg-alert--warning', info: 'cg-alert--info' }[kind]
      || 'cg-alert--info';
    var glyph = { success: 'check', danger: 'x', warning: 'alert', info: 'info' }[kind] || 'info';
    return '<div class="cg-alert ' + cls + '" role="status">' +
      '<span class="cg-alert__icon">' + icon(glyph) + '</span>' +
      '<div class="cg-alert__body">' +
        '<div class="cg-alert__title">' + escapeHtml(title) + '</div>' +
        '<div>' + escapeHtml(body) + '</div>' +
        (action ? '<div style="margin-top:6px"><strong>Next:</strong> ' + escapeHtml(action) + '</div>' : '') +
      '</div></div>';
  }

  function isPickupCheckpoint() {
    var select = $('checkpointSelect');
    return !!select && PICKUP_CHECKPOINTS.indexOf(select.value) !== -1;
  }

  function updateFlowMode() {
    var pickup = isPickupCheckpoint();
    var panel = $('pickupCard');
    if (panel) panel.hidden = !pickup;

    var hint = $('checkpointHint');
    if (hint) {
      hint.textContent = pickup
        ? 'Pickup checkpoint: the scan identifies the student, then an authorised guardian must pass face verification before the handover is recorded.'
        : 'Drop-off checkpoint: the scan identifies the student and records the event. No guardian verification is needed.';
    }
    resetSteps(pickup);
  }

  function setBusy(isBusy, label) {
    state.busy = isBusy;
    var btn = $('scanBtn');
    if (!btn) return;
    btn.disabled = isBusy;
    var text = btn.querySelector('.cg-btn__label');
    if (text) text.textContent = isBusy && label ? label : 'Read card';
  }

  function setAvatar(el, name, photoUrl) {
    if (!el) return;
    el.textContent = initials(name);
    if (photoUrl) {
      var img = document.createElement('img');
      img.alt = '';
      img.src = photoUrl;
      // A photo that fails to load is dropped so the initials remain readable.
      img.addEventListener('error', function () { img.remove(); });
      el.appendChild(img);
    }
  }

  function initials(name) {
    if (!name) return '?';
    var parts = String(name).trim().split(/\s+/);
    return (parts.length === 1 ? parts[0][0] : parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  }

  function stopCamera() {
    if (state.stream) {
      state.stream.getTracks().forEach(function (t) { t.stop(); });
      state.stream = null;
    }
    var box = $('cameraBox');
    if (box) {
      box.innerHTML = '<div class="cg-camera__hint">' + icon('info') +
        '<div style="margin-top:8px">Camera not started</div></div>';
    }
  }

  /** Clears everything from the previous handover so a new scan can never inherit a stale green step. */
  function resetForNewScan(keepCardUid) {
    state.studentId = null;
    state.studentName = null;
    state.guardianId = null;
    state.verificationId = null;

    resetSteps(isPickupCheckpoint());

    var studentCard = $('studentCard');
    if (studentCard) studentCard.hidden = true;
    $('studentMessage').innerHTML = '';
    $('studentStatusBadge').className = 'cg-badge cg-badge--neutral';
    $('studentStatusBadge').textContent = '';

    var select = $('guardianSelect');
    select.innerHTML = '<option value="">Read a student\'s card first…</option>';
    select.disabled = true;

    $('guardianPreview').hidden = true;
    $('guardianAuthBadge').className = 'cg-badge cg-badge--neutral';
    $('guardianAuthBadge').innerHTML = '<span class="cg-badge__dot"></span>Pending scan';
    $('faceResult').innerHTML = '';
    $('decisionResult').innerHTML = '';
    $('approveBtn').disabled = true;
    $('rejectBtn').disabled = true;
    $('captureBtn').disabled = true;

    stopCamera();

    var input = $('cardUidInput');
    if (input && !keepCardUid) { input.value = ''; input.focus(); }
  }

  // --------------------------------------------------------------- Scanner
  function setScannerBadge(connected) {
    var badge = $('scannerStatusBadge');
    if (!badge) return;
    var device = $('deviceSelect');
    var label = device && device.selectedOptions[0] ? device.selectedOptions[0].text : 'Scanner';
    badge.className = 'cg-badge ' + (connected ? 'cg-badge--success' : 'cg-badge--danger');
    badge.innerHTML = '<span class="cg-badge__dot"></span>' + escapeHtml(label) +
      (connected ? ' · Connected' : ' · Disconnected');
  }

  async function connectScanner() {
    var device = $('deviceSelect');
    if (!device || !device.value) return;
    try {
      var resp = await fetch('/Verification/Heartbeat?deviceId=' + encodeURIComponent(device.value), { method: 'POST' });
      setStep('step-scanner', resp.ok ? 'done' : 'failed', resp.ok ? 'Connected' : 'Not reachable');
      setScannerBadge(resp.ok);
    } catch (e) {
      setStep('step-scanner', 'failed', 'Not reachable');
      setScannerBadge(false);
    }
  }

  // ------------------------------------------------------------------ Scan
  async function doScan() {
    if (state.busy) return;
    var input = $('cardUidInput');
    var cardUid = input ? input.value.trim() : '';
    if (!cardUid) {
      $('studentCard').hidden = false;
      $('studentMessage').innerHTML = banner('warning', 'No card read',
        'The field is empty.', 'Hold the card against the reader, or type the UID by hand.');
      return;
    }

    resetForNewScan(true);
    setBusy(true, 'Reading…');
    setStep('step-student', 'active', 'Reading card…');

    var payload = {
      cardUid: cardUid,
      rfidDeviceId: parseInt($('deviceSelect').value, 10),
      checkpoint: $('checkpointSelect').value
    };

    var data;
    try {
      var resp = await fetch('/Verification/Scan', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      data = await resp.json();
      if (!resp.ok) {
        setBusy(false);
        setStep('step-student', 'failed', 'Scan refused');
        $('studentCard').hidden = false;
        $('studentMessage').innerHTML = banner('danger', 'The scan was refused',
          data && data.message ? data.message : 'The API rejected this scan.',
          'Check the scanner selection and try again.');
        return;
      }
    } catch (e) {
      setBusy(false);
      setStep('step-student', 'failed', 'No response');
      $('studentCard').hidden = false;
      $('studentMessage').innerHTML = banner('danger', 'Could not reach the server',
        'The scan was not recorded.', 'Check the connection, then read the card again.');
      return;
    }

    setBusy(false);
    if (input) input.value = '';
    setScannerBadge(true);
    setStep('step-scanner', 'done', 'Connected');

    var card = $('studentCard');
    card.hidden = false;

    if (!data.studentIdentified) {
      setStep('step-student', 'failed', 'Not recognised');
      $('studentName').textContent = 'Unknown card';
      $('studentRfid').textContent = data.maskedCardUid || '';
      setAvatar($('studentAvatar'), '?', null);
      $('studentStatusBadge').className = 'cg-badge cg-badge--danger';
      $('studentStatusBadge').innerHTML = '<span class="cg-badge__dot"></span>Not recognised';
      $('studentMessage').innerHTML = banner('danger', 'Card not recognised',
        data.message || 'This card is not assigned to an active student.',
        'Check the card is registered on the RFID page, then try again.');
      return;
    }

    state.studentId = data.studentId;
    state.studentName = data.studentName;
    setStep('step-student', 'done', data.studentName);
    $('studentName').textContent = data.studentName;
    $('studentRfid').textContent = data.maskedCardUid || '';
    setAvatar($('studentAvatar'), data.studentName, '/Students/Photo/' + data.studentId);

    if (data.eventRecorded) {
      $('studentStatusBadge').className = 'cg-badge cg-badge--success';
      $('studentStatusBadge').innerHTML = '<span class="cg-badge__dot"></span>Checkpoint recorded';
      $('studentMessage').innerHTML = banner('success', 'Checkpoint recorded', data.message);
      if (!isPickupCheckpoint()) {
        setStep('step-done', 'done', 'Drop-off complete');
      }
    } else {
      $('studentStatusBadge').className = 'cg-badge cg-badge--warning';
      $('studentStatusBadge').innerHTML = '<span class="cg-badge__dot"></span>Not recorded';
      $('studentMessage').innerHTML = banner('warning', 'No event was recorded', data.message,
        'Pick the checkpoint that matches where this student actually is.');
    }

    if (isPickupCheckpoint()) {
      await loadGuardians(data.studentId);
    }
  }

  // -------------------------------------------------------------- Guardians
  async function loadGuardians(studentId) {
    setStep('step-guardian', 'active', 'Loading…');
    var select = $('guardianSelect');
    var badge = $('guardianAuthBadge');

    var guardians;
    try {
      var resp = await fetch('/Verification/StudentGuardians?studentId=' + encodeURIComponent(studentId));
      guardians = await resp.json();
      if (!resp.ok) throw new Error('load failed');
    } catch (e) {
      setStep('step-guardian', 'failed', 'Could not load');
      badge.className = 'cg-badge cg-badge--danger';
      badge.innerHTML = '<span class="cg-badge__dot"></span>Could not load guardians';
      return;
    }

    if (!guardians.length) {
      setStep('step-guardian', 'failed', 'None authorised');
      select.innerHTML = '<option value="">No authorised guardian</option>';
      select.disabled = true;
      badge.className = 'cg-badge cg-badge--danger';
      badge.innerHTML = '<span class="cg-badge__dot"></span>No authorised guardian';
      $('faceResult').innerHTML = banner('danger', 'Nobody may collect this student',
        'No guardian linked to ' + (state.studentName || 'this student') + ' is authorised for pickup.',
        'Authorise a guardian on the student\'s profile before releasing them.');
      return;
    }

    select.innerHTML = guardians.map(function (g) {
      return '<option value="' + g.guardianId + '" data-name="' + escapeHtml(g.fullName) +
        '" data-relationship="' + escapeHtml(g.relationship || '') +
        '" data-face="' + (g.faceRegistered ? '1' : '0') +
        '" data-photo="' + escapeHtml(g.photoUrl || '') + '">' +
        escapeHtml(g.fullName) + (g.relationship ? ' — ' + escapeHtml(g.relationship) : '') + '</option>';
    }).join('');
    select.disabled = false;

    badge.className = 'cg-badge cg-badge--success';
    badge.innerHTML = '<span class="cg-badge__dot"></span>' + guardians.length + ' authorised';
    onGuardianChange();
  }

  function onGuardianChange() {
    var select = $('guardianSelect');
    var option = select.selectedOptions[0];
    if (!option || !option.value) {
      $('guardianPreview').hidden = true;
      state.guardianId = null;
      $('captureBtn').disabled = true;
      return;
    }

    state.guardianId = parseInt(option.value, 10);
    var faceRegistered = option.getAttribute('data-face') === '1';

    $('guardianPreview').hidden = false;
    $('guardianName').textContent = option.getAttribute('data-name');
    $('guardianRelationship').textContent = option.getAttribute('data-relationship') || 'Relationship not recorded';
    setAvatar($('guardianAvatar'), option.getAttribute('data-name'), option.getAttribute('data-photo'));

    $('guardianFaceBadge').innerHTML = faceRegistered
      ? '<span class="cg-badge cg-badge--success"><span class="cg-badge__dot"></span>Face registered</span>'
      : '<span class="cg-badge cg-badge--warning"><span class="cg-badge__dot"></span>No face registered</span>';

    setStep('step-guardian', 'done', option.getAttribute('data-name'));
    setStep('step-face', null, 'Not started');

    if (!faceRegistered) {
      $('faceResult').innerHTML = banner('warning', 'This guardian cannot be face-verified',
        'They have not registered a face in the ChildGuard app yet.',
        'Ask them to register their face in the app, or verify their identity another way before releasing the student.');
    } else {
      $('faceResult').innerHTML = '';
    }

    $('captureBtn').disabled = !state.stream;
    $('approveBtn').disabled = true;
    $('rejectBtn').disabled = true;
    state.verificationId = null;
  }

  // ----------------------------------------------------------------- Camera
  async function startCamera() {
    var box = $('cameraBox');
    try {
      state.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' } });
    } catch (e) {
      box.innerHTML = '<div class="cg-camera__hint">' + icon('alert') +
        '<div style="margin-top:8px">Camera unavailable — check the browser\'s camera permission.</div></div>';
      return;
    }

    var video = document.createElement('video');
    video.autoplay = true;
    video.playsInline = true;
    video.muted = true;
    video.srcObject = state.stream;
    box.innerHTML = '';
    box.appendChild(video);

    $('captureBtn').disabled = !state.guardianId;
  }

  async function captureAndVerify() {
    if (!state.stream || !state.studentId || !state.guardianId) return;

    var video = $('cameraBox').querySelector('video');
    if (!video) return;

    var canvas = $('captureCanvas');
    canvas.width = video.videoWidth || 640;
    canvas.height = video.videoHeight || 480;
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
    var imageBase64 = canvas.toDataURL('image/jpeg', 0.85).split(',')[1];

    setStep('step-face', 'active', 'Verifying…');
    $('captureBtn').disabled = true;
    $('faceResult').innerHTML = banner('info', 'Verifying', 'Comparing the capture with the registered face…');

    var data;
    try {
      var resp = await fetch('/Verification/FaceVerify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ studentId: state.studentId, guardianId: state.guardianId, imageBase64: imageBase64 })
      });
      data = await resp.json();
      if (!resp.ok) {
        setStep('step-face', 'failed', 'Service error');
        $('faceResult').innerHTML = banner('danger', 'Verification could not run',
          data && data.message ? data.message : 'The face service did not respond.',
          'Try again. If it keeps failing, verify identity another way and record the handover manually.');
        $('captureBtn').disabled = false;
        return;
      }
    } catch (e) {
      setStep('step-face', 'failed', 'No response');
      $('faceResult').innerHTML = banner('danger', 'Could not reach the server',
        'Nothing was recorded.', 'Check the connection and capture again.');
      $('captureBtn').disabled = false;
      return;
    }

    $('captureBtn').disabled = false;
    state.verificationId = data.verificationId;

    if (!data.guardianAuthorized) {
      setStep('step-face', 'failed', 'Not authorised');
      $('faceResult').innerHTML = banner('danger', 'Guardian is not authorised',
        data.message, 'Do not release the student. Authorise this guardian on the student\'s profile first.');
      return;
    }

    if (!data.faceMatched) {
      setStep('step-face', 'failed', 'No match');
      $('faceResult').innerHTML = banner('danger', 'Face did not match',
        data.message + ' Match score ' + Number(data.matchScore).toFixed(2) + '.',
        'Do not release the student. Confirm identity in person, then capture again or reject the handover.');
      $('rejectBtn').disabled = false;
      return;
    }

    setStep('step-face', 'done', 'Match ' + Number(data.matchScore).toFixed(2));
    setStep('step-decision', 'active', 'Awaiting decision');
    $('faceResult').innerHTML = banner('success', 'Face verified',
      data.message + ' Match score ' + Number(data.matchScore).toFixed(2) + '.',
      'Confirm the handover to record the student leaving with this guardian.');
    $('approveBtn').disabled = false;
    $('rejectBtn').disabled = false;
  }

  // --------------------------------------------------------------- Decision
  async function decide(approve) {
    if (!state.verificationId) return;

    var url = approve ? '/Verification/Approve' : '/Verification/Reject';
    var body = approve
      ? { verificationId: state.verificationId }
      : { verificationId: state.verificationId, reason: 'Rejected at the pickup desk.' };

    $('approveBtn').disabled = true;
    $('rejectBtn').disabled = true;

    var data;
    try {
      var resp = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });
      data = await resp.json();
      if (!resp.ok) {
        setStep('step-decision', 'failed', 'Refused');
        $('decisionResult').innerHTML = banner('danger', 'The decision was refused',
          data && data.message ? data.message : 'The API rejected the decision.',
          'Read the reason above, correct it, and try again.');
        $('approveBtn').disabled = !approve;
        $('rejectBtn').disabled = false;
        return;
      }
    } catch (e) {
      setStep('step-decision', 'failed', 'No response');
      $('decisionResult').innerHTML = banner('danger', 'Could not reach the server',
        'The decision was not recorded.', 'Check the connection and try again.');
      $('approveBtn').disabled = false;
      $('rejectBtn').disabled = false;
      return;
    }

    if (approve) {
      setStep('step-decision', 'done', 'Approved');
      setStep('step-done', 'done', 'Handover complete');
      $('decisionResult').innerHTML = banner('success', 'Handover approved', data.message,
        'The guardian has been notified. Press "New scan" for the next student.');
    } else {
      setStep('step-decision', 'failed', 'Rejected');
      setStep('step-done', 'failed', 'Not released');
      $('decisionResult').innerHTML = banner('warning', 'Handover rejected', data.message,
        'The student was not released. The guardian has been notified.');
    }
  }

  // ------------------------------------------------------------------ Wire
  document.addEventListener('DOMContentLoaded', function () {
    if (!$('verifySteps')) return;

    updateFlowMode();
    connectScanner();

    var device = $('deviceSelect');
    if (device) device.addEventListener('change', connectScanner);

    var checkpoint = $('checkpointSelect');
    if (checkpoint) {
      checkpoint.addEventListener('change', function () {
        updateFlowMode();
        resetForNewScan(false);
      });
    }

    var scanBtn = $('scanBtn');
    if (scanBtn) scanBtn.addEventListener('click', doScan);

    var input = $('cardUidInput');
    if (input) {
      // A keyboard-wedge reader ends its transmission with Enter.
      input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); doScan(); }
      });
    }

    var newScan = $('newScanBtn');
    if (newScan) newScan.addEventListener('click', function () { resetForNewScan(false); });

    $('guardianSelect').addEventListener('change', onGuardianChange);
    $('startCameraBtn').addEventListener('click', startCamera);
    $('captureBtn').addEventListener('click', captureAndVerify);
    $('approveBtn').addEventListener('click', function () { decide(true); });
    $('rejectBtn').addEventListener('click', function () { decide(false); });

    // Leaving the page must not leave the camera running.
    window.addEventListener('pagehide', stopCamera);
  });
})();
