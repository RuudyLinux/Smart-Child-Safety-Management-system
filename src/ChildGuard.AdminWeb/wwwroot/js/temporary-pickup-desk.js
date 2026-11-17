/* ==========================================================================
   Temporary pickup desk: scan the QR, show who is expected, capture a face, hand over.

   Nothing here decides anything. The QR token, the window, the child's position and the face
   match are all judged by the API; this script shows the verdict and never reports a handover
   the server did not record.
   ========================================================================== */
(function () {
  'use strict';

  var $ = function (id) { return document.getElementById(id); };
  var csrf = (document.querySelector('meta[name="csrf-token"]') || {}).content || '';
  var state = { token: null, id: null, qrStream: null, faceStream: null, scanTimer: null, ready: false };

  function show(el, on) { el.hidden = !on; }

  function alertBox(message) {
    $('deskAlertText').textContent = message || '';
    show($('deskAlert'), !!message);
  }

  function post(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf },
      body: JSON.stringify(body)
    }).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (data) { return { ok: r.ok, data: data }; });
    });
  }

  function stop(stream) {
    if (stream) stream.getTracks().forEach(function (t) { t.stop(); });
  }

  // ---- QR camera (BarcodeDetector where the browser has it; otherwise paste/scanner input) ----

  var detectorSupported = 'BarcodeDetector' in window;
  $('qrSupport').textContent = detectorSupported ? '' : 'Camera QR scanning is not supported in this browser. Paste the code or use a keyboard-mode scanner.';
  if (!detectorSupported) $('qrCameraBtn').disabled = true;

  $('qrCameraBtn').addEventListener('click', function () {
    if (state.qrStream) return;
    navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } }).then(function (stream) {
      state.qrStream = stream;
      var video = $('qrVideo');
      video.srcObject = stream;
      video.style.display = 'block';
      video.play();
      var detector = new window.BarcodeDetector({ formats: ['qr_code'] });
      state.scanTimer = setInterval(function () {
        detector.detect(video).then(function (codes) {
          if (codes.length) { $('qrToken').value = codes[0].rawValue; stopQr(); checkToken(); }
        }).catch(function () { /* frame not ready */ });
      }, 400);
    }).catch(function () { alertBox('The camera could not be opened. Check the browser permission, or paste the code instead.'); });
  });

  function stopQr() {
    clearInterval(state.scanTimer);
    stop(state.qrStream);
    state.qrStream = null;
    $('qrVideo').style.display = 'none';
  }

  // ---- Step 1 -> 2: resolve the token ----

  function checkToken() {
    var token = $('qrToken').value.trim();
    if (!token) { alertBox('Scan or paste a QR code first.'); return; }
    alertBox('');
    show($('stepPerson'), false);
    show($('stepFace'), false);
    $('handoverResult').hidden = true;
    state.ready = false;

    post('/TemporaryPickups/ScanToken', { token: token }).then(function (res) {
      if (!res.ok) { alertBox(res.data.message || 'This code could not be checked.'); return; }
      var d = res.data;
      state.token = token;
      state.id = d.id;
      $('pName').textContent = d.fullName;
      $('pRel').textContent = d.relationship;
      $('pPhone').textContent = d.phone;
      $('pId').textContent = d.identityNote || '—';
      $('pStudent').textContent = d.studentName + (d.studentGrade ? ' (' + d.studentGrade + ')' : '');
      $('pBy').textContent = d.requestedBy;
      $('pUntil').textContent = d.validUntil;
      var photo = $('personPhoto');
      if (d.photoUrl) { photo.src = d.photoUrl; photo.hidden = false; } else { photo.hidden = true; }
      show($('stepPerson'), true);

      if (d.canHandOver) {
        show($('blocked'), false);
        show($('stepFace'), true);
        state.ready = true;
        startFaceCamera();
      } else {
        $('blockedText').textContent = d.blockedReason || 'A handover is not possible right now.';
        show($('blocked'), true);
      }
    }).catch(function () { alertBox('Could not reach the server.'); });
  }

  $('checkTokenBtn').addEventListener('click', checkToken);
  $('qrToken').addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); checkToken(); } });

  // ---- Step 3: face capture and handover ----

  function startFaceCamera() {
    if (state.faceStream) return;
    navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 960 } } }).then(function (stream) {
      state.faceStream = stream;
      var v = $('faceVideo');
      v.srcObject = stream;
      v.play();
      $('handoverBtn').disabled = false;
      $('faceCameraBtn').textContent = 'Camera on';
    }).catch(function () { alertBox('The camera could not be opened. Allow camera access to continue.'); });
  }

  $('faceCameraBtn').addEventListener('click', startFaceCamera);

  function capture() {
    var v = $('faceVideo'), c = $('faceCanvas');
    var scale = Math.min(1, 1024 / Math.max(v.videoWidth, v.videoHeight));
    c.width = Math.round(v.videoWidth * scale);
    c.height = Math.round(v.videoHeight * scale);
    c.getContext('2d').drawImage(v, 0, 0, c.width, c.height);
    return c.toDataURL('image/jpeg', 0.88);
  }

  $('handoverBtn').addEventListener('click', function () {
    if (!state.ready || !state.faceStream) return;
    var box = $('handoverResult');
    var btn = $('handoverBtn');
    btn.disabled = true;
    box.hidden = false;
    box.className = 'cg-alert cg-alert--info';
    box.textContent = 'Checking…';

    post('/TemporaryPickups/Handover/' + state.id, { token: state.token, imageBase64: capture() }).then(function (res) {
      if (!res.ok) {
        box.className = 'cg-alert cg-alert--danger';
        box.textContent = res.data.message || 'The handover could not be completed.';
        btn.disabled = false;
        return;
      }
      var d = res.data;
      if (d.handedOver) {
        box.className = 'cg-alert cg-alert--success';
        box.textContent = d.message + ' (face score ' + Number(d.faceScore).toFixed(2) + ')';
        state.ready = false;
        stop(state.faceStream);
        state.faceStream = null;
        $('qrToken').value = '';
      } else {
        box.className = 'cg-alert cg-alert--danger';
        box.textContent = d.message + ' (face score ' + Number(d.faceScore).toFixed(2) + ')';
        btn.disabled = false;
      }
    }).catch(function () {
      box.className = 'cg-alert cg-alert--danger';
      box.textContent = 'Could not reach the server. Do not release the child until this succeeds.';
      btn.disabled = false;
    });
  });

  window.addEventListener('beforeunload', function () { stopQr(); stop(state.faceStream); });
})();
