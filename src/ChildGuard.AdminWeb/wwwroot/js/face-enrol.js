/* ==========================================================================
   Face ID on the guardian page (in person, at school).
   The shared capture widget (camera or gallery file) shows the server's image check;
   buttons are offered only for an image the server accepted. A guardian can have several
   reference photos (different light and angle). The image is sent once to the API, which
   keeps only the face embedding and never stores the image; the list below shows
   labels and quality only, never any biometric data.
   ========================================================================== */
(function () {
  'use strict';

  var root = document.querySelector('[data-cg-face-enrol]');
  if (!root || !window.CgFaceCapture) return;

  var host = root.querySelector('[data-cg-face-capture]');
  var result = root.querySelector('[data-cg-face-result]');
  var list = root.querySelector('[data-cg-face-references]');
  var labelField = root.querySelector('[data-cg-face-label-field]');
  var labelInput = root.querySelector('[data-cg-face-label]');
  var submitBtn = root.querySelector('[data-cg-face-submit]');
  var addBtn = root.querySelector('[data-cg-face-add]');
  var registerUrl = root.getAttribute('data-register-url');
  var referencesUrl = root.getAttribute('data-references-url');
  var addUrl = root.getAttribute('data-add-url');
  var removeUrl = root.getAttribute('data-remove-url'); // carries referenceId=0; the real id is swapped in
  var registered = root.getAttribute('data-registered') === 'true';
  var canAdd = true;
  var capture;

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

  function message(kind, title, body) {
    result.innerHTML = '';
    if (!title) return;
    var box = el('div', 'cg-alert cg-alert--' + kind);
    box.setAttribute('role', kind === 'danger' ? 'alert' : 'status');
    var inner = el('div', 'cg-alert__body');
    inner.appendChild(el('div', 'cg-alert__title', title));
    if (body) inner.appendChild(el('div', null, body));
    box.appendChild(inner);
    result.appendChild(box);
  }

  function post(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken() },
      body: body == null ? undefined : JSON.stringify(body)
    }).then(function (resp) {
      return resp.json().catch(function () { return {}; }).then(function (data) { return { ok: resp.ok, data: data }; });
    });
  }

  function refreshButtons() {
    var ready = capture && capture.isReady();
    submitBtn.disabled = !ready;
    addBtn.hidden = !(registered && canAdd);
    addBtn.disabled = !ready;
    labelField.hidden = !(registered && canAdd && capture && capture.getImage());
  }

  // ------------------------------------------------------------ reference list
  function describeQuality(q) {
    if (!q) return '';
    var parts = [];
    if (q.sharpness != null) parts.push('sharpness ' + Math.round(q.sharpness));
    if (q.brightness != null) parts.push('brightness ' + Math.round(q.brightness));
    if (q.yawDegEstimate != null) parts.push('turn ' + Math.round(Math.abs(q.yawDegEstimate)) + '°');
    return parts.join(' · ');
  }

  function renderList(data) {
    list.innerHTML = '';
    canAdd = !!data.canAdd;
    var refs = data.references || [];
    if (!refs.length) {
      list.appendChild(el('span', 'cg-muted', 'No face registered yet.'));
    } else {
      var ul = el('ul', 'cg-fc__checks');
      refs.forEach(function (r) {
        var li = el('li', 'cg-fc__check');
        var badge = el('span', 'cg-badge cg-badge--' + (r.source === 'Primary' ? 'primary' : 'neutral'));
        badge.appendChild(el('span', 'cg-badge__dot'));
        badge.appendChild(document.createTextNode(r.source === 'Primary' ? 'Primary' : 'Extra'));
        li.appendChild(badge);
        var text = el('span', 'cg-fc__check-text');
        text.appendChild(el('strong', null, r.label || (r.source === 'Primary' ? 'First registered face' : 'Reference photo')));
        var meta = 'added ' + new Date(r.createdAt).toLocaleDateString();
        var q = describeQuality(r.quality);
        text.appendChild(el('span', 'cg-muted', ' ' + meta + (q ? ' · ' + q : '')));
        li.appendChild(text);
        if (r.source !== 'Primary') {
          var remove = el('button', 'cg-btn cg-btn--ghost cg-btn--sm', 'Remove');
          remove.type = 'button';
          remove.addEventListener('click', function () { removeReference(r, remove); });
          li.appendChild(remove);
        }
        ul.appendChild(li);
      });
      list.appendChild(ul);
    }
    list.appendChild(el('div', 'cg-hint', refs.length + ' of ' + data.maxReferences + ' reference photos registered.'));
    refreshButtons();
  }

  function loadList() {
    fetch(referencesUrl, { headers: { 'Accept': 'application/json' } })
      .then(function (resp) { return resp.json().then(function (d) { return { ok: resp.ok, data: d }; }); })
      .then(function (r) {
        if (r.ok) { renderList(r.data); return; }
        list.textContent = '';
        list.appendChild(el('span', 'cg-muted', (r.data && r.data.message) || 'The photos could not be listed.'));
      })
      .catch(function () {
        list.textContent = '';
        list.appendChild(el('span', 'cg-muted', 'The photos could not be listed. Reload the page to try again.'));
      });
  }

  function removeReference(reference, button) {
    if (!window.confirm('Remove this reference photo? The guardian can still be verified with the others.')) return;
    button.disabled = true;
    post(removeUrl.replace('referenceId=0', 'referenceId=' + reference.id), null).then(function (r) {
      if (r.ok) { message(); loadList(); return; }
      button.disabled = false;
      message('danger', 'The photo was not removed', (r.data && r.data.message) || 'Try again.');
    }).catch(function () {
      button.disabled = false;
      message('danger', 'Could not reach the server', 'Nothing was removed.');
    });
  }

  // -------------------------------------------------------------- registration
  function replacePrimary() {
    var image = capture.getImage();
    if (!capture.isReady() || !image) return;
    submitBtn.disabled = true; addBtn.disabled = true;
    message('info', 'Registering…', 'Checking the photo for one clear face.');
    post(registerUrl, { imageBase64: image }).then(function (r) {
      if (r.ok) {
        window.location.hash = 'photo';
        window.location.reload(); // the server queued a success toast
        return;
      }
      refreshButtons();
      message('danger', 'The face was not registered',
        (r.data && r.data.message) || 'The face service did not accept the photo. Replace the image and try again.');
    }).catch(function () {
      refreshButtons();
      message('danger', 'Could not reach the server', 'Nothing was registered. Check the connection and try again.');
    });
  }

  function addReference() {
    var image = capture.getImage();
    if (!capture.isReady() || !image) return;
    submitBtn.disabled = true; addBtn.disabled = true;
    message('info', 'Adding…', 'Checking the photo and comparing it with the registered ones.');
    post(addUrl, { imageBase64: image, label: labelInput.value.trim() || null }).then(function (r) {
      if (r.ok && r.data && r.data.added) {
        window.location.hash = 'photo';
        window.location.reload();
        return;
      }
      refreshButtons();
      if (r.ok && r.data) { message('info', 'Not added', r.data.message); return; }
      message('danger', 'The photo was not added', (r.data && r.data.message) || 'Replace the image and try again.');
    }).catch(function () {
      refreshButtons();
      message('danger', 'Could not reach the server', 'Nothing was added. Check the connection and try again.');
    });
  }

  capture = window.CgFaceCapture.create(host, {
    analyzeUrl: host.getAttribute('data-analyze-url'),
    purpose: host.getAttribute('data-purpose') || 'registration',
    onChange: function () { message(); refreshButtons(); }
  });

  submitBtn.addEventListener('click', replacePrimary);
  addBtn.addEventListener('click', addReference);
  loadList();
})();
