/* ==========================================================================
   Pickup & Drop-off desk.
   A thin client over the VerificationController proxy endpoints: it reflects
   what the API returns and never decides an outcome itself. Image quality, who
   the person is, whether they may collect this student and whether a live person
   was present are all judged on the server; this page shows each result on its
   own and offers "Approve" only for a result the server marked verified. Every
   failure is reported with what failed, why, and what the operator should do next.
   ========================================================================== */
(function () {
  'use strict';

  /** Checkpoints that hand a child to a person, and therefore need guardian verification. */
  var PICKUP_CHECKPOINTS = ['SchoolDeparture'];

  /** Webcam frames for one live check (the server accepts 6 to 16 and the challenge sets the pace). */
  var FRAME_WIDTH = 640;
  var FRAME_QUALITY = 0.8;

  var state = {
    studentId: null,
    studentName: null,
    sessionId: null,
    verificationId: null,
    stream: null,
    frameReady: false,     // the camera has produced a picture
    live: false,           // a live check is running
    lastPhotoAnalysis: null, // the photo analysis already sent for identification
    method: null,          // 'live' | 'gallery'
    gallery: null,         // the shared capture widget for photo upload
    hasFaces: false,       // at least one authorised guardian has a registered face
    busy: false
  };

  function $(id) { return document.getElementById(id); }

  function csrfToken() {
    var meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.content : '';
  }

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

  function postJson(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken() },
      body: body == null ? '{}' : JSON.stringify(body)
    }).then(function (resp) {
      return resp.json().catch(function () { return {}; }).then(function (data) {
        return { ok: resp.ok, status: resp.status, data: data };
      });
    });
  }

  function sleep(ms) { return new Promise(function (resolve) { setTimeout(resolve, ms); }); }

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
        (body ? '<div>' + escapeHtml(body) + '</div>' : '') +
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
        ? 'Pickup checkpoint: the scan identifies the student, then the collecting guardian must pass identity, authorisation and a live-person check before the handover can be approved.'
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
    state.frameReady = false;
    refreshLiveButtons();
    var box = $('cameraBox');
    if (box) {
      box.innerHTML = '<div class="cg-camera__hint">' + icon('info') +
        '<div style="margin-top:8px">Camera not started</div></div>';
    }
  }

  function refreshLiveButtons() {
    var begin = $('beginLiveBtn');
    if (begin) begin.disabled = !(state.stream && state.frameReady && state.sessionId && state.hasFaces && !state.live);
    var start = $('startCameraBtn');
    if (start) start.disabled = state.live;
  }

  function setDecisionButtons(canApprove, canReject) {
    $('approveBtn').disabled = !canApprove;
    $('rejectBtn').disabled = !canReject;
  }

  /** Clears everything from the previous handover so a new scan can never inherit a stale green step. */
  function resetForNewScan(keepCardUid) {
    state.studentId = null;
    state.studentName = null;
    state.sessionId = null;
    state.verificationId = null;
    state.method = null;
    state.live = false;
    state.hasFaces = false;

    resetSteps(isPickupCheckpoint());

    var studentCard = $('studentCard');
    if (studentCard) studentCard.hidden = true;
    $('studentMessage').innerHTML = '';
    $('studentStatusBadge').className = 'cg-badge cg-badge--neutral';
    $('studentStatusBadge').textContent = '';

    $('guardianList').hidden = true;
    $('guardianList').innerHTML = '';
    $('methodBar').hidden = true;
    $('livePanel').hidden = true;
    $('galleryPanel').hidden = true;
    $('livePrompt').hidden = true;
    $('liveProgress').hidden = true;
    $('guardianAuthBadge').className = 'cg-badge cg-badge--neutral';
    $('guardianAuthBadge').innerHTML = '<span class="cg-badge__dot"></span>Pending scan';
    $('layerResults').innerHTML = '';
    $('decisionResult').innerHTML = '';
    setDecisionButtons(false, false);
    if (state.gallery) state.gallery.reset();

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
      var resp = await fetch('/Verification/Heartbeat?deviceId=' + encodeURIComponent(device.value), {
        method: 'POST',
        headers: { 'X-CSRF-TOKEN': csrfToken() }
      });
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

    // At a pickup checkpoint the card only identifies the student: the departure is
    // recorded by the handover approval, after the guardian is verified (BUG-001).
    var pickup = isPickupCheckpoint();
    var payload = pickup
      ? { cardUid: cardUid, rfidDeviceId: parseInt($('deviceSelect').value, 10) }
      : { cardUid: cardUid, rfidDeviceId: parseInt($('deviceSelect').value, 10), checkpoint: $('checkpointSelect').value };

    var data;
    try {
      var resp = await fetch(pickup ? '/Verification/Identify' : '/Verification/Scan', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken() },
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
    setAvatar($('studentAvatar'), data.studentName, data.studentPhotoUrl || null);

    if (pickup) {
      $('studentStatusBadge').className = 'cg-badge cg-badge--primary';
      $('studentStatusBadge').innerHTML = '<span class="cg-badge__dot"></span>Identified';
      $('studentMessage').innerHTML = banner('info', 'Student identified',
        'Nothing is recorded yet. The departure is recorded when the handover is approved.');
    } else if (data.eventRecorded) {
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
      await startSession(data.studentId);
    }
  }

  // ----------------------------------------------------------------- Session
  async function startSession(studentId) {
    setStep('step-guardian', 'active', 'Loading…');
    var badge = $('guardianAuthBadge');
    var r;
    var photos = {};
    try {
      r = await postJson('/Verification/StartSession', { studentId: studentId });
      var list = await fetch('/Verification/StudentGuardians?studentId=' + encodeURIComponent(studentId));
      if (list.ok) (await list.json()).forEach(function (g) { photos[g.guardianId] = g.photoUrl; });
    } catch (e) {
      setStep('step-guardian', 'failed', 'Could not load');
      badge.className = 'cg-badge cg-badge--danger';
      badge.innerHTML = '<span class="cg-badge__dot"></span>Could not start';
      return;
    }
    if (!r.ok) {
      setStep('step-guardian', 'failed', 'Could not start');
      badge.className = 'cg-badge cg-badge--danger';
      badge.innerHTML = '<span class="cg-badge__dot"></span>Could not start';
      $('layerResults').innerHTML = banner('danger', 'The verification could not be started',
        (r.data && r.data.message) || 'The server refused.', 'Read the card again.');
      return;
    }

    var guardians = r.data.guardians || [];
    state.sessionId = r.data.sessionId;
    state.hasFaces = guardians.some(function (g) { return g.faceRegistered; });

    var listEl = $('guardianList');
    listEl.hidden = false;
    if (!guardians.length) {
      listEl.innerHTML = '';
      setStep('step-guardian', 'failed', 'None authorised');
      badge.className = 'cg-badge cg-badge--danger';
      badge.innerHTML = '<span class="cg-badge__dot"></span>No authorised guardian';
      $('layerResults').innerHTML = banner('danger', 'Nobody may collect this student',
        'No guardian linked to ' + (state.studentName || 'this student') + ' is authorised for pickup.',
        'Authorise a guardian on the student\'s profile before releasing them.');
      return;
    }

    listEl.innerHTML = '<div class="cg-label" style="margin-bottom:8px">Authorised to collect ' + escapeHtml(state.studentName || '') + '</div>' +
      guardians.map(function (g) {
        return '<div class="cg-row cg-vguardian">' +
          '<span class="cg-avatar cg-avatar--guardian cg-avatar--square" data-g="' + g.guardianId + '">' + escapeHtml(initials(g.fullName)) + '</span>' +
          '<div style="min-width:0"><div class="cg-strong">' + escapeHtml(g.fullName) + '</div>' +
          '<div class="cg-text-sm cg-muted">' + escapeHtml(g.relationship || 'Relationship not recorded') + '</div></div>' +
          (g.faceRegistered
            ? '<span class="cg-badge cg-badge--success"><span class="cg-badge__dot"></span>' + g.referenceCount + ' face photo' + (g.referenceCount === 1 ? '' : 's') + '</span>'
            : '<span class="cg-badge cg-badge--warning"><span class="cg-badge__dot"></span>No face registered</span>') +
          '</div>';
      }).join('');
    guardians.forEach(function (g) {
      var el = listEl.querySelector('[data-g="' + g.guardianId + '"]');
      if (el && photos[g.guardianId]) setAvatar(el, g.fullName, photos[g.guardianId]);
    });

    badge.className = 'cg-badge cg-badge--success';
    badge.innerHTML = '<span class="cg-badge__dot"></span>' + guardians.length + ' authorised';
    setStep('step-guardian', 'active', 'Waiting for the guardian');
    setStep('step-face', null, 'Not started');

    if (!state.hasFaces) {
      $('layerResults').innerHTML = banner('warning', 'No face is registered for these guardians',
        'There is nothing to compare a capture with.',
        'Register a face on a guardian\'s page (Photo & Face ID tab), or verify identity another way before releasing the student.');
      return;
    }
    $('methodBar').hidden = false;
    selectMethod('live');
  }

  function selectMethod(method) {
    state.method = method;
    $('livePanel').hidden = method !== 'live';
    $('galleryPanel').hidden = method !== 'gallery';
    $('methodLive').className = 'cg-btn ' + (method === 'live' ? 'cg-btn--primary' : 'cg-btn--secondary');
    $('methodGallery').className = 'cg-btn ' + (method === 'gallery' ? 'cg-btn--primary' : 'cg-btn--secondary');
    if (method === 'gallery' && !state.gallery) {
      state.gallery = window.CgFaceCapture.create($('galleryHost'), {
        analyzeUrl: '/Verification/AnalyzeImage',
        purpose: 'verification',
        onChange: function (api) {
          // As soon as the server has accepted the image, identify the guardian: no extra button.
          var analysis = api.getAnalysis();
          if (api.isReady() && analysis !== state.lastPhotoAnalysis) {
            state.lastPhotoAnalysis = analysis;
            identifyPhoto();
          }
          if (!api.getImage()) state.lastPhotoAnalysis = null;
        }
      });
    }
    if (method !== 'live') stopCamera();
  }

  /** The session ended (expired, replaced or too many attempts): say so and offer a fresh start. */
  function sessionEnded(message) {
    state.sessionId = null;
    state.live = false;
    $('livePrompt').hidden = true;
    $('liveProgress').hidden = true;
    refreshLiveButtons();
    $('layerResults').innerHTML = banner('warning', 'This verification has ended', message,
      'Start again to verify the guardian.') +
      '<div style="margin-top:8px"><button type="button" class="cg-btn cg-btn--secondary" id="restartSessionBtn">Start again</button></div>';
    var restart = $('restartSessionBtn');
    if (restart) restart.addEventListener('click', function () {
      $('layerResults').innerHTML = '';
      if (state.studentId) startSession(state.studentId);
    });
  }

  function isSessionGone(data) {
    var m = (data && data.message) || '';
    return /session/i.test(m) && /(no longer open|expired)/i.test(m);
  }

  // ------------------------------------------------------------------ Gallery
  async function identifyPhoto() {
    if (!state.sessionId || !state.gallery || !state.gallery.isReady()) return;
    setDecisionButtons(false, false);
    $('layerResults').innerHTML = banner('info', 'Checking', 'Identifying the guardian from the photo…');
    var r;
    try {
      r = await postJson('/Verification/SessionIdentify/' + state.sessionId,
        { source: state.gallery.getSource() || 'Gallery', imageBase64: state.gallery.getImage() });
    } catch (e) {
      $('layerResults').innerHTML = banner('danger', 'Could not reach the server', 'Nothing was recorded.', 'Check the connection and try again.');
      return;
    }
    if (!r.ok) {
      if (isSessionGone(r.data)) { sessionEnded(r.data.message); return; }
      $('layerResults').innerHTML = banner('warning', 'The photo could not be checked',
        (r.data && r.data.message) || 'The face service did not respond.', 'Try again, or use the live camera check.');
      return;
    }
    renderAttempt(r.data);
    // A photo never completes a pickup: move straight on to the live check.
    if (r.data.outcome === 'LiveVerificationRequired') selectMethod('live');
  }

  // ------------------------------------------------------------------ Camera
  async function startCamera() {
    var box = $('cameraBox');
    function hint(glyph, text) {
      box.innerHTML = '<div class="cg-camera__hint">' + icon(glyph) +
        '<div style="margin-top:8px">' + escapeHtml(text) + '</div></div>';
    }
    // Browsers expose the camera only on HTTPS (or localhost).
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      hint('alert', 'The camera is only available when the console is opened over HTTPS.');
      return;
    }
    // getUserMedia waits silently while the browser asks for permission; say so.
    hint('info', 'Waiting for camera permission… allow it in the browser prompt.');
    try {
      state.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 720 } } });
    } catch (e) {
      var name = e && e.name;
      hint('alert', name === 'NotFoundError' || name === 'OverconstrainedError'
        ? 'No camera was found on this computer. Connect one, then press Start camera again.'
        : name === 'NotReadableError'
          ? 'The camera is in use by another application. Close it, then press Start camera again.'
          : 'Camera permission was refused. Allow the camera for this site in the browser settings.');
      return;
    }

    var video = document.createElement('video');
    video.autoplay = true;
    video.playsInline = true;
    video.muted = true;
    state.frameReady = false;
    var stream = state.stream;
    video.addEventListener('loadeddata', function () {
      if (state.stream === stream) { state.frameReady = true; refreshLiveButtons(); }
    });
    video.srcObject = state.stream;
    box.innerHTML = '';
    box.appendChild(video);
    refreshLiveButtons();
  }

  function grabFrame(video, startedAt) {
    var scale = Math.min(1, FRAME_WIDTH / video.videoWidth);
    var canvas = document.createElement('canvas');
    canvas.width = Math.round(video.videoWidth * scale);
    canvas.height = Math.round(video.videoHeight * scale);
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
    return {
      imageBase64: canvas.toDataURL('image/jpeg', FRAME_QUALITY).split(',')[1],
      tMs: Math.round(performance.now() - startedAt)
    };
  }

  // -------------------------------------------------------------- Live check
  /** The prompts for one challenge: look straight, then each requested turn with a return to centre. */
  function planPrompts(steps) {
    if (!steps.length) return [{ text: 'Look straight at the camera and hold still', ms: 2600 }];
    var plan = [{ text: 'Look straight at the camera', ms: 1500 }];
    steps.forEach(function (step, i) {
      var left = step === 'turn_left';
      plan.push({ text: 'Slowly turn your head to YOUR ' + (left ? 'LEFT  ←' : 'RIGHT  →'), ms: 2200 });
      plan.push({ text: i === steps.length - 1 ? 'Face the camera again' : 'Now face the camera again', ms: i === steps.length - 1 ? 700 : 900 });
    });
    return plan;
  }

  async function runLiveCheck() {
    if (!state.sessionId || !state.stream || !state.frameReady || state.live) return;
    var video = $('cameraBox').querySelector('video');
    if (!video || !video.videoWidth) return;

    state.live = true;
    refreshLiveButtons();
    setDecisionButtons(false, false);
    $('layerResults').innerHTML = '';
    setStep('step-face', 'active', 'Live check…');

    var prompt = $('livePrompt');
    var bar = $('liveBar');
    var progress = $('liveProgress');
    function finish() { prompt.hidden = true; progress.hidden = true; state.live = false; refreshLiveButtons(); }

    // 1. The server issues a random single-use challenge for this session.
    var ch;
    try {
      ch = await postJson('/Verification/SessionChallenge/' + state.sessionId, {});
    } catch (e) {
      finish();
      $('layerResults').innerHTML = banner('danger', 'Could not reach the server', 'The live check did not start.', 'Check the connection and try again.');
      return;
    }
    if (!ch.ok) {
      finish();
      if (isSessionGone(ch.data)) { sessionEnded(ch.data.message); return; }
      setStep('step-face', 'failed', 'Not started');
      $('layerResults').innerHTML = banner('danger', 'The live check could not start',
        (ch.data && ch.data.message) || 'The server refused.', 'Try again.');
      return;
    }
    var challenge = ch.data;

    // 2. Follow the prompts while taking frames at an even pace.
    var plan = planPrompts(challenge.steps);
    var total = plan.reduce(function (sum, p) { return sum + p.ms; }, 0);
    var interval = Math.max(350, Math.ceil(total / (challenge.maxFrames - 1)));
    var frames = [];
    var startedAt = performance.now();
    prompt.hidden = false;
    progress.hidden = false;
    bar.style.width = '0%';

    var phaseEnd = 0;
    for (var p = 0; p < plan.length; p++) {
      prompt.textContent = plan[p].text;
      phaseEnd += plan[p].ms;
      while (performance.now() - startedAt < phaseEnd && frames.length < challenge.maxFrames) {
        if (!state.stream || !state.live) { finish(); return; } // the camera was closed or the check was cancelled
        if (frames.length * interval <= performance.now() - startedAt) frames.push(grabFrame(video, startedAt));
        bar.style.width = Math.min(100, ((performance.now() - startedAt) / total) * 100) + '%';
        await sleep(40);
      }
    }
    prompt.textContent = 'Analysing…';
    bar.style.width = '100%';

    if (frames.length < challenge.minFrames) {
      finish();
      setStep('step-face', 'failed', 'Not checked');
      $('layerResults').innerHTML = banner('warning', 'The camera did not deliver enough frames',
        'Only ' + frames.length + ' frames were captured.', 'Check the camera, then try again.');
      return;
    }

    // 3. The server judges the burst against the challenge it issued.
    var r;
    try {
      r = await postJson('/Verification/SessionLiveVerify/' + state.sessionId,
        { challengeToken: challenge.challengeToken, frames: frames.map(function (f) { return { imageBase64: f.imageBase64, tMs: f.tMs }; }) });
    } catch (e) {
      finish();
      setStep('step-face', 'failed', 'No response');
      $('layerResults').innerHTML = banner('danger', 'Could not reach the server', 'Nothing was recorded.', 'Check the connection and try again.');
      return;
    }
    finish();
    if (!r.ok) {
      if (isSessionGone(r.data)) { sessionEnded(r.data.message); return; }
      setStep('step-face', 'failed', 'Not checked');
      $('layerResults').innerHTML = banner('warning', 'The live check could not be completed',
        (r.data && r.data.message) || 'The face service did not respond.', 'Start the live check again.');
      return;
    }
    renderAttempt(r.data);
  }

  // ------------------------------------------------------------ Result panel
  var LAYER = {
    quality: { Pass: ['success', 'Pass'], Fail: ['danger', 'Fail'] },
    detection: { Pass: ['success', 'Pass'], Fail: ['danger', 'Fail'] },
    identity: { Match: ['success', 'Match'], NoMatch: ['danger', 'No match'], Inconclusive: ['warning', 'Inconclusive'] },
    authorisation: { Authorised: ['success', 'Authorised'], Unauthorised: ['danger', 'Unauthorised'], NotEvaluated: ['neutral', 'Not evaluated'] },
    liveness: { Pass: ['success', 'Pass'], Fail: ['danger', 'Fail'], NotPerformed: ['warning', 'Not performed'] }
  };

  function layerRow(label, kind, value, text) {
    var pair = (LAYER[kind] || {})[value] || ['neutral', value == null ? 'Not evaluated' : String(value)];
    return '<li class="cg-vrow"><span class="cg-badge cg-badge--' + pair[0] + '"><span class="cg-badge__dot"></span>' +
      escapeHtml(pair[1]) + '</span><span class="cg-fc__check-text"><strong>' + escapeHtml(label) + '</strong> ' +
      '<span class="cg-muted">' + escapeHtml(text || '') + '</span></span></li>';
  }

  function renderAttempt(r) {
    var g = r.guardian;
    var identityText =
      r.identity === 'Match' ? (g ? g.fullName + (g.relationship ? ' (' + g.relationship + ')' : '') : '') +
          (r.score != null ? ' · score ' + Number(r.score).toFixed(2) + ' vs threshold ' + Number(r.threshold).toFixed(2) +
          (r.confidenceBand ? ' (' + r.confidenceBand.toLowerCase() + ')' : '') : '') +
          (r.referenceLabel ? ' · matched: ' + r.referenceLabel : '')
      : r.identity === 'Inconclusive' ? 'Two guardians matched almost equally.'
      : r.identity === 'NoMatch' ? 'No authorised guardian for this student matches.' +
          (r.score != null ? ' Best score ' + Number(r.score).toFixed(2) + ' vs threshold ' + Number(r.threshold).toFixed(2) + '.' : '')
      : 'Not evaluated: the image was not usable.';

    var failure = (r.issues || []).join(' ');
    var qualityText = r.quality === 'Pass' ? 'Image quality acceptable.' : (failure || 'The image is not good enough.');
    var detectionText = r.faceDetection === 'Pass' ? 'Face detected successfully.' : 'No single clear face was found.';
    var authText = r.authorisation === 'Authorised' ? 'Authorised to collect this student.'
      : r.authorisation === 'Unauthorised' ? 'Guardian not authorised for this student.' : '';
    var liveText = r.liveness === 'Pass' ? 'A live person was present.'
      : r.liveness === 'NotPerformed' ? (r.verified ? 'Live check not required for this pickup.' : 'Liveness verification required.')
      : (failure || 'The live-person check did not pass.');

    var rows = '<ul class="cg-vrows">' +
      layerRow('Image quality', 'quality', r.quality, qualityText) +
      layerRow('Face detection', 'detection', r.faceDetection, detectionText) +
      layerRow('Identity', 'identity', r.identity, identityText) +
      layerRow('Authorisation', 'authorisation', r.authorisation, authText) +
      layerRow('Liveness', 'liveness', r.liveness, liveText) +
      '</ul>';

    var kind, title;
    var spoof = (r.reasonCodes || []).some(function (c) {
      return ['PASSIVE_SPOOF_SUSPECTED', 'FACE_CHANGED', 'REPLAYED_FRAME', 'DUPLICATE_FRAMES'].indexOf(c) !== -1;
    });
    var next;
    if (r.verified) {
      kind = 'success'; title = 'All mandatory checks passed';
      var deadline = r.expiresAt
        ? ' Decide before ' + new Date(r.expiresAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + ', or verify again.' : '';
      next = 'Review the result above, then approve or reject the handover.' + deadline;
    } else if (r.outcome === 'LiveVerificationRequired') {
      kind = 'warning'; title = 'Liveness verification required'; next = 'Use the live camera check. A photo cannot show the guardian is here.';
    } else if (r.outcome === 'RetryCapture') {
      kind = 'warning'; title = 'The image is not suitable'; next = 'Retake the capture, or choose another photo.';
    } else if (r.outcome === 'ReviewRequired') {
      kind = 'warning'; title = 'Identity could not be settled'; next = 'Retry, or verify the guardian another way. This result cannot be approved.';
    } else if (r.outcome === 'Unauthorised') {
      kind = 'danger'; title = 'Guardian not authorised for this student'; next = 'Do not release the student. Authorise the guardian on the student\'s profile first.';
    } else if (r.outcome === 'LivenessFailed') {
      kind = 'danger'; title = spoof ? 'Possible spoofing detected' : 'The live-person check did not pass';
      next = 'Do not release the student on this result. Try the live check again; if it keeps failing, verify the guardian another way or reject.';
    } else {
      kind = 'danger'; title = 'No matching guardian'; next = 'Do not release the student.';
    }
    var tail = !r.verified && r.liveness !== 'NotPerformed' && r.attemptsRemaining != null
      ? ' ' + r.attemptsRemaining + ' attempt' + (r.attemptsRemaining === 1 ? '' : 's') + ' left.' : '';

    // The rows already list why an image or live check failed; repeating it in the banner would say it twice.
    var bannerBody = (r.outcome === 'RetryCapture' || r.outcome === 'LivenessFailed') ? '' : r.message;
    $('layerResults').innerHTML = rows + banner(kind, title, bannerBody, next + tail);

    // Steps and decision buttons follow the server's verdict only.
    state.verificationId = r.verificationId || null;
    var identified = r.identity === 'Match' && r.authorisation === 'Authorised';
    setStep('step-guardian', identified ? 'done' : (r.identity ? 'failed' : 'active'), identified && g ? g.fullName : (r.identity ? 'Not confirmed' : 'Waiting'));
    if (r.verified) {
      setStep('step-face', 'done', 'Live person confirmed');
      setStep('step-decision', 'active', 'Awaiting decision');
    } else if (r.liveness === 'Fail') {
      setStep('step-face', 'failed', 'Liveness failed');
    } else if (identified) {
      setStep('step-face', 'active', 'Live check needed');
    }
    setDecisionButtons(!!(r.verified && r.verificationId), !!r.verificationId);
  }

  /** Abandon the current verification: stop any capture, clear the results, start a fresh session. Nothing is approved or rejected. */
  function cancelVerification() {
    state.live = false;            // stops a running live capture at its next step
    state.verificationId = null;
    stopCamera();
    $('livePrompt').hidden = true;
    $('liveProgress').hidden = true;
    $('layerResults').innerHTML = '';
    $('decisionResult').innerHTML = '';
    setDecisionButtons(false, false);
    if (state.gallery) state.gallery.reset();
    setStep('step-face', null, 'Not started');
    setStep('step-decision', null, 'Not started');
    if (state.studentId) startSession(state.studentId);
  }

  // --------------------------------------------------------------- Decision
  async function decide(approve) {
    if (!state.verificationId) return;

    var url = approve ? '/Verification/Approve' : '/Verification/Reject';
    var body = approve
      ? { verificationId: state.verificationId }
      : { verificationId: state.verificationId, reason: 'Rejected at the pickup desk.' };

    setDecisionButtons(false, false);

    var data;
    try {
      var resp = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken() },
        body: JSON.stringify(body)
      });
      data = await resp.json();
      if (!resp.ok) {
        setStep('step-decision', 'failed', 'Refused');
        $('decisionResult').innerHTML = banner('danger', 'The decision was refused',
          data && data.message ? data.message : 'The API rejected the decision.',
          'Read the reason above, correct it, and try again.');
        setDecisionButtons(!approve ? false : true, true);
        return;
      }
    } catch (e) {
      setStep('step-decision', 'failed', 'No response');
      $('decisionResult').innerHTML = banner('danger', 'Could not reach the server',
        'The decision was not recorded.', 'Check the connection and try again.');
      setDecisionButtons(true, true);
      return;
    }

    stopCamera();
    $('methodBar').hidden = true;
    $('livePanel').hidden = true;
    $('galleryPanel').hidden = true;
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

    $('methodLive').addEventListener('click', function () { selectMethod('live'); });
    $('methodGallery').addEventListener('click', function () { selectMethod('gallery'); });
    $('startCameraBtn').addEventListener('click', startCamera);
    $('beginLiveBtn').addEventListener('click', runLiveCheck);
    $('approveBtn').addEventListener('click', function () { decide(true); });
    $('rejectBtn').addEventListener('click', function () { decide(false); });
    $('cancelBtn').addEventListener('click', cancelVerification);

    // Leaving the page must not leave the camera running.
    window.addEventListener('pagehide', stopCamera);
  });
})();
