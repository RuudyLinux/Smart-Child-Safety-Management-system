/* ==========================================================================
   Face image capture: webcam or gallery file, with the server's Layer 1 check
   (is this image fit for face recognition?) shown before anything is submitted.

   Nothing here decides anything: the file, the face and the image quality are
   judged by the API (which asks the face service). This script shows the verdict,
   so the page can never show "ready" for an image the server did not accept.
   The image is re-drawn to a JPEG of at most 1280 px, which also removes EXIF data.
   ========================================================================== */
(function (global) {
  'use strict';

  var MAX_SIDE = 1280;
  var MAX_FILE_BYTES = 15 * 1024 * 1024;
  var TYPES = ['image/jpeg', 'image/png', 'image/webp'];

  function csrfToken() {
    var meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.content : '';
  }

  function el(tag, cls, text) {
    var node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  }

  function postJson(url, body, onProgress) {
    return new Promise(function (resolve, reject) {
      var xhr = new XMLHttpRequest();
      xhr.open('POST', url);
      xhr.setRequestHeader('Content-Type', 'application/json');
      xhr.setRequestHeader('X-CSRF-TOKEN', csrfToken());
      xhr.upload.onprogress = function (e) { if (e.lengthComputable) onProgress(e.loaded / e.total); };
      xhr.onload = function () {
        var data = {};
        try { data = JSON.parse(xhr.responseText); } catch (e) { /* not JSON */ }
        resolve({ ok: xhr.status >= 200 && xhr.status < 300, status: xhr.status, data: data });
      };
      xhr.onerror = function () { reject(new Error('network')); };
      xhr.send(JSON.stringify(body));
    });
  }

  function drawScaled(source, width, height) {
    var scale = Math.min(1, MAX_SIDE / Math.max(width, height));
    var canvas = document.createElement('canvas');
    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);
    canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);
    var dataUrl = canvas.toDataURL('image/jpeg', 0.9);
    if (dataUrl.length > 2600000) dataUrl = canvas.toDataURL('image/jpeg', 0.75);
    return dataUrl;
  }

  function fileToDataUrl(file) {
    return new Promise(function (resolve, reject) {
      if (TYPES.indexOf(file.type) === -1) { reject(new Error('Choose a JPEG, PNG or WebP photo.')); return; }
      if (file.size > MAX_FILE_BYTES) { reject(new Error('That file is too large. Choose a photo under 15 MB.')); return; }
      var url = URL.createObjectURL(file);
      var img = new Image();
      img.onload = function () {
        URL.revokeObjectURL(url);
        try { resolve(drawScaled(img, img.naturalWidth, img.naturalHeight)); } catch (e) { reject(new Error('That file could not be read as an image.')); }
      };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error('That file could not be read as an image.')); };
      img.src = url;
    });
  }

  /**
   * options: analyzeUrl (required), purpose ('registration' | 'verification'),
   *          allowWebcam, allowGallery (default true), onChange(api)
   */
  function create(root, options) {
    var opts = options || {};
    var state = { source: null, dataUrl: null, analysis: null, analyzing: false, failedToReach: false, stream: null, run: 0 };

    root.classList.add('cg-fc');
    root.innerHTML = '';
    var stage = el('div', 'cg-camera cg-fc__stage');
    stage.tabIndex = 0;
    var progress = el('div', 'cg-fc__progress');
    progress.hidden = true;
    var bar = el('div', 'cg-fc__bar');
    progress.appendChild(bar);
    var checks = el('ul', 'cg-fc__checks');
    checks.setAttribute('aria-live', 'polite');
    var messages = el('div', 'cg-fc__messages');
    var side = el('div', 'cg-fc__side');
    side.appendChild(checks);
    side.appendChild(messages);
    side.appendChild(progress);
    var body = el('div', 'cg-fc__body');
    body.appendChild(stage);
    body.appendChild(side);

    var input = el('input');
    input.type = 'file';
    input.accept = TYPES.join(',');
    input.hidden = true;

    var buttons = el('div', 'cg-btn-group cg-fc__buttons');
    var btn = {
      camera: el('button', 'cg-btn cg-btn--secondary', 'Use camera'),
      capture: el('button', 'cg-btn cg-btn--primary', 'Capture'),
      gallery: el('button', 'cg-btn cg-btn--secondary', 'Choose photo'),
      replace: el('button', 'cg-btn cg-btn--ghost', 'Replace image'),
      remove: el('button', 'cg-btn cg-btn--ghost', 'Remove'),
      retry: el('button', 'cg-btn cg-btn--secondary', 'Retry check'),
      close: el('button', 'cg-btn cg-btn--ghost', 'Close camera')
    };
    Object.keys(btn).forEach(function (k) { btn[k].type = 'button'; buttons.appendChild(btn[k]); });
    root.appendChild(body);
    root.appendChild(buttons);
    root.appendChild(input);

    var api = {
      getImage: function () { return state.dataUrl ? state.dataUrl.split(',')[1] : null; },
      getSource: function () { return state.source; },
      getAnalysis: function () { return state.analysis; },
      isAnalyzing: function () { return state.analyzing; },
      isReady: function () { return !!(state.dataUrl && state.analysis && state.analysis.passed && !state.analyzing); },
      reset: reset,
      stopCamera: stopCamera
    };

    function changed() { if (opts.onChange) opts.onChange(api); }

    function stageHint(text, glyph) {
      stage.innerHTML = '';
      var hint = el('div', 'cg-camera__hint');
      if (glyph) hint.appendChild(el('div', 'cg-fc__glyph', glyph));
      hint.appendChild(el('div', null, text));
      stage.appendChild(hint);
    }

    function stopCamera() {
      if (state.stream) { state.stream.getTracks().forEach(function (t) { t.stop(); }); state.stream = null; }
    }

    function setButtons() {
      var hasImage = !!state.dataUrl;
      var camera = !!state.stream;
      var allowWebcam = opts.allowWebcam !== false;
      var allowGallery = opts.allowGallery !== false;
      btn.camera.hidden = hasImage || camera || !allowWebcam;
      btn.gallery.hidden = hasImage || camera || !allowGallery;
      btn.capture.hidden = !camera;
      btn.close.hidden = !camera;
      btn.replace.hidden = !hasImage || state.analyzing;
      btn.remove.hidden = !hasImage || state.analyzing;
      btn.retry.hidden = !(hasImage && state.failedToReach && !state.analyzing);
    }

    function alertBox(kind, text) {
      var box = el('div', 'cg-alert cg-alert--' + kind + ' cg-fc__message');
      box.setAttribute('role', kind === 'danger' ? 'alert' : 'status');
      var inner = el('div', 'cg-alert__body');
      inner.appendChild(el('div', null, text));
      box.appendChild(inner);
      return box;
    }

    function row(label, status, text) {
      var li = el('li', 'cg-fc__check');
      var cls = status === 'pass' ? 'success' : status === 'fail' ? 'danger' : 'neutral';
      var badge = el('span', 'cg-badge cg-badge--' + cls);
      badge.appendChild(el('span', 'cg-badge__dot'));
      badge.appendChild(document.createTextNode(status === 'pass' ? 'Pass' : status === 'fail' ? 'Fail' : 'Waiting'));
      li.appendChild(badge);
      var textBox = el('span', 'cg-fc__check-text');
      textBox.appendChild(el('strong', null, label));
      textBox.appendChild(el('span', 'cg-muted', ' ' + text));
      li.appendChild(textBox);
      return li;
    }

    function render() {
      checks.innerHTML = '';
      messages.innerHTML = '';
      var a = state.analysis;

      if (!state.dataUrl) { setButtons(); return; }

      if (state.analyzing) {
        checks.appendChild(row('Face detection', 'wait', 'Checking the image…'));
        checks.appendChild(row('Image quality', 'wait', 'Checking the image…'));
      } else if (a) {
        var faces = a.faceCount;
        checks.appendChild(row('Face detection', a.faceDetection === 'Pass' ? 'pass' : 'fail',
          a.faceDetection === 'Pass' ? 'Face detected successfully.' : (faces > 1 ? faces + ' faces detected.' : 'No single clear face found.')));
        checks.appendChild(row('Image quality', a.quality === 'Pass' ? 'pass' : 'fail',
          a.quality === 'Pass' ? 'Image quality acceptable.' : 'The image is not good enough yet.'));
        (a.messages || []).forEach(function (m, i) {
          var isWarning = i >= (a.issues || []).length;
          messages.appendChild(alertBox(isWarning ? 'info' : 'warning', m));
        });
        if (a.passed) {
          messages.appendChild(alertBox('success',
            'This image is suitable for face recognition. It has not been compared with anyone yet.'));
        } else {
          messages.appendChild(el('div', 'cg-hint', 'Replace the image or take a new one.'));
        }
      } else if (state.failedToReach) {
        checks.appendChild(row('Face detection', 'wait', 'Not checked.'));
        checks.appendChild(row('Image quality', 'wait', 'Not checked.'));
      }
      setButtons();
    }

    function showProblem(text) {
      messages.innerHTML = '';
      messages.appendChild(alertBox('danger', text));
    }

    function reset() {
      stopCamera();
      state.run++;
      state.source = null; state.dataUrl = null; state.analysis = null; state.analyzing = false; state.failedToReach = false;
      progress.hidden = true;
      stageHint(opts.allowGallery === false ? 'Use the camera to take a photo' : 'Use the camera, or drop a photo here', '');
      render();
      changed();
    }

    function analyze() {
      var run = ++state.run;
      state.analyzing = true; state.analysis = null; state.failedToReach = false;
      bar.style.width = '0%';
      progress.hidden = false;
      render();
      changed();

      postJson(opts.analyzeUrl, { imageBase64: api.getImage(), purpose: opts.purpose || 'verification' }, function (fraction) {
        if (run === state.run) bar.style.width = Math.round(fraction * 100) + '%';
      }).then(function (r) {
        if (run !== state.run) return;
        state.analyzing = false;
        progress.hidden = true;
        if (r.ok && r.data && typeof r.data.passed === 'boolean') {
          state.analysis = r.data;
        } else {
          state.failedToReach = true;
          render();
          showProblem((r.data && r.data.message) || 'The image could not be checked. Retry, or choose another image.');
          changed();
          return;
        }
        render();
        changed();
      }).catch(function () {
        if (run !== state.run) return;
        state.analyzing = false; state.failedToReach = true;
        progress.hidden = true;
        render();
        showProblem('Could not reach the server. Nothing was submitted. Check the connection, then retry.');
        changed();
      });
    }

    function setImage(dataUrl, source) {
      stopCamera();
      state.dataUrl = dataUrl;
      state.source = source;
      stage.innerHTML = '';
      var img = el('img');
      img.alt = 'Selected face image';
      img.src = dataUrl;
      stage.appendChild(img);
      analyze();
    }

    function startCamera() {
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        stageHint('The camera is only available over HTTPS (or on this computer as localhost). You can choose a photo instead.');
        return;
      }
      stageHint('Waiting for camera permission… allow it in the browser prompt.');
      navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 960 } } })
        .then(function (s) {
          state.stream = s;
          var video = document.createElement('video');
          video.autoplay = true; video.playsInline = true; video.muted = true;
          btn.capture.disabled = true;
          video.addEventListener('loadeddata', function () { if (state.stream === s) btn.capture.disabled = false; });
          video.srcObject = s;
          stage.innerHTML = '';
          stage.appendChild(video);
          setButtons();
        })
        .catch(function (e) {
          var name = e && e.name;
          stageHint(name === 'NotFoundError' || name === 'OverconstrainedError'
            ? 'No camera was found on this computer. Connect one, or choose a photo.'
            : name === 'NotReadableError'
              ? 'The camera is in use by another application. Close it, then try again.'
              : 'Camera permission was refused. Allow the camera for this site in the browser settings.');
          setButtons();
        });
    }

    function capture() {
      var video = stage.querySelector('video');
      if (!video || !video.videoWidth) return;
      setImage(drawScaled(video, video.videoWidth, video.videoHeight), 'Webcam');
    }

    function takeFile(file) {
      if (!file) return;
      messages.innerHTML = '';
      fileToDataUrl(file).then(function (dataUrl) { setImage(dataUrl, 'Gallery'); })
        .catch(function (e) { showProblem(e.message); });
    }

    btn.camera.addEventListener('click', function () { messages.innerHTML = ''; startCamera(); });
    btn.capture.addEventListener('click', capture);
    btn.close.addEventListener('click', function () { reset(); });
    btn.gallery.addEventListener('click', function () { input.click(); });
    btn.replace.addEventListener('click', function () { reset(); });
    btn.remove.addEventListener('click', function () { reset(); });
    btn.retry.addEventListener('click', analyze);
    input.addEventListener('change', function () { takeFile(input.files && input.files[0]); input.value = ''; });

    if (opts.allowGallery !== false) {
      ['dragenter', 'dragover'].forEach(function (name) {
        stage.addEventListener(name, function (e) {
          if (state.dataUrl || state.stream) return;
          e.preventDefault(); stage.classList.add('is-dragover');
        });
      });
      ['dragleave', 'drop'].forEach(function (name) {
        stage.addEventListener(name, function () { stage.classList.remove('is-dragover'); });
      });
      stage.addEventListener('drop', function (e) {
        if (state.dataUrl || state.stream) return;
        e.preventDefault();
        takeFile(e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]);
      });
    }

    window.addEventListener('pagehide', stopCamera);
    reset();
    return api;
  }

  global.CgFaceCapture = { create: create };
})(window);
