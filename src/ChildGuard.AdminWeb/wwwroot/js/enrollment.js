/* Enrollment wizard: five steps kept in the page, one POST at the end (single API transaction). */
(function () {
  'use strict';

  var root = document.querySelector('[data-cg-enrol]');
  if (!root) return;

  var form = document.getElementById('enrolForm');
  var idempotencyKey = root.getAttribute('data-idempotency-key');
  var MAX_GUARDIANS = 4;
  var TOTAL = 5;

  var step = 1;
  var dirty = false;
  var finished = false;
  var submitting = false;
  var rfidMode = 'assign';
  var transportMode = 'none';
  var card = { uid: null, result: null, seq: 0 };

  // ------------------------------------------------------------------ helpers
  function $(sel, ctx) { return (ctx || root).querySelector(sel); }
  function $$(sel, ctx) { return Array.prototype.slice.call((ctx || root).querySelectorAll(sel)); }
  function text(el) { return el ? el.value.trim() : ''; }
  function orNull(v) { return v === '' ? null : v; }
  function digits(v) { return String(v || '').replace(/\D/g, ''); }

  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function csrfToken() {
    var meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.getAttribute('content') : '';
  }

  function request(url, options) {
    return fetch(url, options).then(function (resp) {
      return resp.json().catch(function () { return {}; }).then(function (data) {
        return { ok: resp.ok, status: resp.status, data: data };
      });
    });
  }

  function getJson(url) { return request(url, { headers: { 'Accept': 'application/json' } }); }

  function postJson(url, body) {
    return request(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'X-CSRF-TOKEN': csrfToken() },
      body: JSON.stringify(body)
    });
  }

  function errorMessage(res, fallback) {
    if (res.status === 401) return 'Your session has expired. Sign in again in another tab, then retry. Your entries are kept.';
    return (res.data && res.data.message) || fallback;
  }

  function formatDate(iso) {
    if (!iso) return '—';
    var p = iso.split('-');
    var d = new Date(+p[0], +p[1] - 1, +p[2]);
    return d.toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
  }

  function debounce(fn, ms) {
    var t;
    return function () {
      var args = arguments, self = this;
      clearTimeout(t);
      t = setTimeout(function () { fn.apply(self, args); }, ms);
    };
  }

  function setSegment(group, attr, value) {
    $$('[' + attr + ']', group).forEach(function (b) {
      var on = b.getAttribute(attr) === value;
      b.classList.toggle('is-active', on);
      b.setAttribute('aria-checked', on ? 'true' : 'false');
    });
  }

  // ------------------------------------------------------------------ errors
  function fieldError(container, selector, message) {
    var target = container.querySelector(selector);
    if (target) target.textContent = message || '';
  }

  function markInvalid(input, invalid) {
    if (input) {
      input.classList.toggle('is-invalid', !!invalid);
      if (invalid) input.setAttribute('aria-invalid', 'true'); else input.removeAttribute('aria-invalid');
    }
  }

  function focusFirstInvalid(panel) {
    var bad = panel.querySelector('.is-invalid');
    if (bad) bad.focus();
  }

  // ------------------------------------------------------------------ addresses
  var ADDRESS_PARTS = ['line1', 'line2', 'locality', 'landmark', 'city', 'district', 'state', 'postalCode', 'country'];

  function readAddress(container) {
    var a = {}, any = false;
    ADDRESS_PARTS.forEach(function (k) {
      a[k] = orNull(text(container.querySelector('[data-a="' + k + '"]')));
      // The pre-filled country alone is not an address.
      if (a[k] && k !== 'country') any = true;
    });
    return any ? a : null;
  }

  function validateAddress(container) {
    var input = container.querySelector('[data-a="postalCode"]');
    var bad = input && input.value.trim() !== '' && !/^[A-Za-z0-9 -]+$/.test(input.value.trim());
    markInvalid(input, bad);
    fieldError(container, '[data-a-error="postalCode"]', bad ? 'Use letters, digits, spaces or "-" only.' : '');
    return !bad;
  }

  function addressLines(a) {
    if (!a) return null;
    return [a.line1, a.line2, a.locality, a.landmark ? 'Near ' + a.landmark : null,
      [a.city, a.district].filter(Boolean).join(', '),
      [a.state, a.postalCode].filter(Boolean).join(' '), a.country].filter(Boolean);
  }

  // ------------------------------------------------------------------ step 1: student
  function student() {
    return {
      fullName: text($('[data-en="student.fullName"]')),
      admissionNumber: orNull(text($('[data-en="student.admissionNumber"]'))),
      dateOfBirth: text($('[data-en="student.dateOfBirth"]')),
      gender: orNull(text($('[data-en="student.gender"]'))),
      grade: orNull(text($('[data-en="student.grade"]'))),
      section: orNull(text($('[data-en="student.section"]'))),
      address: readAddress($('[data-en-address="student"]'))
    };
  }

  // ---- possible duplicate student (same name and date of birth): a warning the admin answers with a reason
  var duplicates = [];
  var dupReason = $('[data-en="duplicateReason"]');

  function showDuplicates(list) {
    duplicates = list || [];
    $('[data-en-dup]').hidden = duplicates.length === 0;
    $('[data-en-dup-list]').innerHTML = duplicates.map(function (d) {
      var facts = ['born ' + formatDate(String(d.dateOfBirth).slice(0, 10))];
      if (d.grade) facts.push('grade ' + d.grade + (d.section ? ' ' + d.section : ''));
      if (d.admissionNumber) facts.push('admission no. ' + d.admissionNumber);
      if (d.status !== 'Active') facts.push(String(d.status).toLowerCase());
      return '<li><a href="/Students/Details/' + d.studentId + '" target="_blank" rel="noopener">' + escapeHtml(d.fullName) +
        '</a> · ' + escapeHtml(facts.join(' · ')) + '</li>';
    }).join('');
  }

  function checkDuplicateReason(focus) {
    if (!duplicates.length) return true;
    var reason = dupReason.value.trim();
    var bad = reason.length < 10;
    markInvalid(dupReason, bad);
    fieldError($('[data-en-panel="1"]'), '[data-en-error="duplicateReason"]',
      bad ? 'Give a reason of at least 10 characters, or cancel and open the existing record.' : '');
    if (bad && focus) dupReason.focus();
    return !bad;
  }

  function duplicateReason() { return duplicates.length ? dupReason.value.trim() : null; }

  // Local checks, then the server: admission number free, and any same-name, same-birth-date student answered.
  function validateStudent() {
    if (!validateStudentLocal()) return Promise.resolve(false);
    var panel = $('[data-en-panel="1"]');
    var s = student();
    var admission = $('[data-en="student.admissionNumber"]');
    return postJson('/Enrollment/CheckStudent', { fullName: s.fullName, dateOfBirth: s.dateOfBirth, admissionNumber: s.admissionNumber })
      .then(function (res) {
        if (!res.ok) {
          fieldError(panel, '[data-en-error="student.fullName"]', errorMessage(res, 'Existing students could not be checked. Try again.'));
          return false;
        }
        var taken = !!res.data.admissionNumberTaken;
        markInvalid(admission, taken);
        fieldError(panel, '[data-en-error="student.admissionNumber"]', taken ? 'Another student already has this admission number.' : '');
        if (taken) { admission.focus(); return false; }
        showDuplicates(res.data.possibleDuplicates);
        return checkDuplicateReason(true);
      }, function () {
        fieldError(panel, '[data-en-error="student.fullName"]', 'Could not reach the server. Check the connection and try again.');
        return false;
      });
  }

  function validateStudentLocal() {
    var panel = $('[data-en-panel="1"]');
    var s = student();
    var ok = true;

    var nameBad = s.fullName === '';
    markInvalid($('[data-en="student.fullName"]'), nameBad);
    fieldError(panel, '[data-en-error="student.fullName"]', nameBad ? 'Enter the student\'s full name.' : '');
    ok = ok && !nameBad;

    var dobInput = $('[data-en="student.dateOfBirth"]');
    // yyyy-mm-dd strings compare in date order.
    var dobBad = s.dateOfBirth === '' || s.dateOfBirth > dobInput.getAttribute('max');
    markInvalid(dobInput, dobBad);
    fieldError(panel, '[data-en-error="student.dateOfBirth"]', dobBad ? 'Enter a date of birth in the past.' : '');
    ok = ok && !dobBad;

    ok = validateAddress($('[data-en-address="student"]')) && ok;
    if (!ok) focusFirstInvalid(panel);
    return ok;
  }

  // ------------------------------------------------------------------ step 2: guardians
  var guardianList = $('[data-en-guardians]');
  var template = document.getElementById('enGuardianTemplate');

  function guardianCards() { return $$('[data-en-guardian]', guardianList); }

  function addGuardian() {
    if (guardianCards().length >= MAX_GUARDIANS) return;
    var node = template.content.firstElementChild.cloneNode(true);
    guardianList.appendChild(node);
    wireGuardian(node);
    refreshGuardianChrome();
    return node;
  }

  function refreshGuardianChrome() {
    var cards = guardianCards();
    cards.forEach(function (c, i) {
      $('[data-en-guardian-title]', c).textContent = i === 0 ? 'Primary guardian' : 'Guardian ' + (i + 1);
      $('[data-en-remove-guardian]', c).hidden = cards.length === 1;
      // Labels need ids that are unique per card for screen readers.
      $$('.cg-field', c).forEach(function (f, j) {
        var input = f.querySelector('input, select');
        var label = f.querySelector('.cg-label');
        if (input && label) {
          input.id = 'enG' + i + '_' + j;
          label.setAttribute('for', input.id);
        }
      });
    });
    $('[data-en-add-guardian]').hidden = cards.length >= MAX_GUARDIANS;
  }

  function guardianMode(c) { return c.getAttribute('data-mode') || 'new'; }

  function setGuardianMode(c, mode) {
    c.setAttribute('data-mode', mode);
    setSegment(c, 'data-en-gmode', mode);
    $('[data-en-gnew]', c).hidden = mode !== 'new';
    $('[data-en-gexisting]', c).hidden = mode !== 'existing';
    if (mode === 'existing' && !c.getAttribute('data-guardian-id')) $('[data-en-gquery]', c).focus();
  }

  function selectExisting(c, g) {
    c.setAttribute('data-guardian-id', g.guardianId);
    c._selected = g;
    var box = $('[data-en-gselected]', c);
    $('[data-en-gselected-name]', c).textContent = g.fullName;
    $('[data-en-gselected-meta]', c).textContent = [g.email, g.phone].filter(Boolean).join(' · ');
    var facts = [];
    facts.push(g.linkedStudents.length
      ? 'Already guardian of ' + g.linkedStudents.join(', ')
      : 'Not linked to any student yet');
    if (g.faceRegistered) facts.push('Face registered for pickup verification');
    $('[data-en-gselected-facts]', c).innerHTML = facts.map(function (f) {
      return '<span class="cg-personcard__fact">' + escapeHtml(f) + '</span>';
    }).join('') + (g.status !== 'Active'
      ? '<span class="cg-personcard__fact"><span class="cg-badge cg-badge--warning">Deactivated account</span></span>' : '');
    // A deactivated account needs an explicit confirmation every time it is selected.
    $('[data-en-ginactive]', c).hidden = g.status === 'Active';
    $('[data-g="confirmInactiveAccount"]', c).checked = false;
    fieldError(c, '[data-g-error="inactive"]', '');
    box.hidden = false;
    $('[data-en-gsearch]', c).hidden = true;
    fieldError(c, '[data-g-error="existing"]', '');
    dirty = true;
  }

  function clearExisting(c) {
    c.removeAttribute('data-guardian-id');
    c._selected = null;
    $('[data-en-gselected]', c).hidden = true;
    $('[data-en-ginactive]', c).hidden = true;
    $('[data-en-gsearch]', c).hidden = false;
    $('[data-en-gresults]', c).innerHTML = '';
    var q = $('[data-en-gquery]', c);
    q.value = '';
    q.focus();
  }

  function selectedIdsExcept(c) {
    return guardianCards().filter(function (x) { return x !== c && guardianMode(x) === 'existing'; })
      .map(function (x) { return x.getAttribute('data-guardian-id'); }).filter(Boolean);
  }

  function renderResults(c, list) {
    var box = $('[data-en-gresults]', c);
    if (!list.length) {
      box.innerHTML = '<p class="cg-hint">No guardian matches. Check the spelling, or switch to "Create new".</p>';
      return;
    }
    var taken = selectedIdsExcept(c);
    box.innerHTML = list.map(function (g, i) {
      var already = taken.indexOf(String(g.guardianId)) >= 0;
      var badges = (g.status !== 'Active' ? '<span class="cg-badge cg-badge--warning">' + escapeHtml(g.status) + '</span>' : '') +
        (already ? '<span class="cg-badge cg-badge--neutral">Already added</span>' : '');
      return '<button type="button" class="cg-enrol-result" role="option" data-index="' + i + '"' + (already ? ' disabled' : '') + '>' +
        '<span class="cg-enrol-result__body"><span class="cg-enrol-result__name">' + escapeHtml(g.fullName) + '</span>' +
        '<span class="cg-enrol-result__meta">' + escapeHtml([g.email, g.phone].filter(Boolean).join(' · ')) +
        (g.linkedStudents.length ? '<br>Guardian of ' + escapeHtml(g.linkedStudents.join(', ')) : '') + '</span></span>' +
        badges + '<span class="cg-btn cg-btn--secondary cg-btn--sm">Select</span></button>';
    }).join('');
    $$('.cg-enrol-result', box).forEach(function (btn) {
      btn.addEventListener('click', function () { selectExisting(c, list[+btn.getAttribute('data-index')]); });
    });
  }

  function lookup(q) {
    return getJson('/Enrollment/GuardianLookup?q=' + encodeURIComponent(q)).then(function (res) {
      return res.ok && Array.isArray(res.data) ? res.data : [];
    }, function () { return []; });
  }

  // A new guardian whose email or contact already has an account: offer to link it instead of failing at the end.
  function checkDuplicate(c) {
    var email = text($('[data-g="email"]', c)).toLowerCase();
    var phone = digits(text($('[data-g="contactNumber"]', c)));
    var alertBox = $('[data-en-gdup]', c);
    var probes = [];
    if (email.indexOf('@') > 0) probes.push(email);
    if (phone.length >= 8) probes.push(phone);
    if (!probes.length) { alertBox.hidden = true; c._duplicate = null; return; }

    Promise.all(probes.map(lookup)).then(function (lists) {
      var match = null;
      lists.forEach(function (list) {
        list.forEach(function (g) {
          if (match) return;
          if ((email && g.email.toLowerCase() === email) || (phone.length >= 8 && digits(g.phone).slice(-10) === phone.slice(-10))) match = g;
        });
      });
      c._duplicate = match;
      alertBox.hidden = !match;
      if (match) {
        $('[data-en-gdup-text]', c).textContent = match.fullName + ' (' + [match.email, match.phone].filter(Boolean).join(', ') +
          ') already has a guardian account. A second account cannot be created with the same email or contact number.';
      }
    });
  }

  function wireGuardian(c) {
    setGuardianMode(c, 'new');
    $$('[data-en-gmode]', c).forEach(function (b) {
      b.addEventListener('click', function () { setGuardianMode(c, b.getAttribute('data-en-gmode')); });
    });
    $('[data-en-remove-guardian]', c).addEventListener('click', function () {
      c.remove();
      refreshGuardianChrome();
    });

    var same = $('[data-g="sameAsStudentAddress"]', c);
    same.addEventListener('change', function () { $('[data-en-gaddress]', c).hidden = same.checked; });

    var query = $('[data-en-gquery]', c);
    var search = debounce(function () {
      var q = query.value.trim();
      if (q.length < 3) { $('[data-en-gresults]', c).innerHTML = ''; return; }
      lookup(q).then(function (list) { if (query.value.trim() === q) renderResults(c, list); });
    }, 300);
    query.addEventListener('input', search);
    query.addEventListener('keydown', function (e) { if (e.key === 'Enter') e.preventDefault(); });
    $('[data-en-gchange]', c).addEventListener('click', function () { clearExisting(c); });

    ['email', 'contactNumber'].forEach(function (k) {
      $('[data-g="' + k + '"]', c).addEventListener('blur', function () { checkDuplicate(c); });
    });
    $('[data-en-gdup-link]', c).addEventListener('click', function () {
      if (!c._duplicate) return;
      setGuardianMode(c, 'existing');
      selectExisting(c, c._duplicate);
    });
  }

  function guardianData(c) {
    var relationship = orNull(text($('[data-g="relationship"]', c)));
    var pickup = $('[data-g="isAuthorizedForPickup"]', c).checked;
    if (guardianMode(c) === 'existing') {
      return {
        existingGuardianId: +c.getAttribute('data-guardian-id') || null,
        fullName: null, email: null, contactNumber: null, address: null, sameAsStudentAddress: false,
        relationship: relationship, isAuthorizedForPickup: pickup,
        confirmInactiveAccount: !$('[data-en-ginactive]', c).hidden && $('[data-g="confirmInactiveAccount"]', c).checked
      };
    }
    var same = $('[data-g="sameAsStudentAddress"]', c).checked;
    return {
      existingGuardianId: null,
      fullName: text($('[data-g="fullName"]', c)),
      email: text($('[data-g="email"]', c)),
      contactNumber: text($('[data-g="contactNumber"]', c)),
      address: same ? null : readAddress($('[data-en-gaddress]', c)),
      sameAsStudentAddress: same,
      relationship: relationship,
      isAuthorizedForPickup: pickup
    };
  }

  function validateGuardians() {
    var panel = $('[data-en-panel="2"]');
    var ok = true;
    var seenIds = {}, seenEmails = {}, seenPhones = {};

    guardianCards().forEach(function (c) {
      ['fullName', 'email', 'contactNumber', 'existing'].forEach(function (k) { fieldError(c, '[data-g-error="' + k + '"]', ''); });
      $$('.is-invalid', c).forEach(function (i) { markInvalid(i, false); });

      if (guardianMode(c) === 'existing') {
        var id = c.getAttribute('data-guardian-id');
        if (!id) {
          fieldError(c, '[data-g-error="existing"]', 'Search for the guardian and select their account.');
          markInvalid($('[data-en-gquery]', c), true);
          ok = false;
        } else if (seenIds[id]) {
          fieldError(c, '[data-g-error="existing"]', 'This guardian is already listed.');
          ok = false;
        } else if (!$('[data-en-ginactive]', c).hidden && !$('[data-g="confirmInactiveAccount"]', c).checked) {
          fieldError(c, '[data-g-error="inactive"]', 'Confirm linking this deactivated account, or choose a different guardian.');
          markInvalid($('[data-g="confirmInactiveAccount"]', c), true);
          ok = false;
        }
        seenIds[id] = true;
        return;
      }

      var g = guardianData(c);
      function bad(key, input, msg) {
        fieldError(c, '[data-g-error="' + key + '"]', msg);
        markInvalid($('[data-g="' + input + '"]', c), true);
        ok = false;
      }
      if (!g.fullName) bad('fullName', 'fullName', 'Enter the guardian\'s name.');
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(g.email)) bad('email', 'email', 'Enter a valid email address.');
      else if (seenEmails[g.email.toLowerCase()]) bad('email', 'email', 'This email is used by another guardian above.');
      var p = digits(g.contactNumber);
      if (p.length < 8) bad('contactNumber', 'contactNumber', 'Enter a valid contact number.');
      else if (seenPhones[p]) bad('contactNumber', 'contactNumber', 'This number is used by another guardian above.');
      seenEmails[g.email.toLowerCase()] = true;
      seenPhones[p] = true;
      if (c._duplicate && !$('[data-en-gdup]', c).hidden) {
        bad('email', 'email', 'Already registered. Use "Link this guardian instead".');
      }
      if (!g.sameAsStudentAddress && !validateAddress($('[data-en-gaddress]', c))) ok = false;
    });

    fieldError(panel, '[data-en-error="guardians"]', guardianCards().length ? '' : 'Add at least one guardian.');
    if (!ok) focusFirstInvalid(panel);
    return ok && guardianCards().length > 0;
  }

  // ------------------------------------------------------------------ step 3: RFID
  var scanBox = $('[data-en-scan]');
  var scanInput = $('[data-en="cardUid"]');
  var scanStatus = $('[data-en-scan-status]');
  var clearCardBtn = $('[data-en-clear-card]');

  function setScan(state, message) {
    scanBox.setAttribute('data-state', state);
    scanStatus.textContent = message;
    clearCardBtn.hidden = state !== 'ok';
    $('[data-en-check-card]').hidden = state === 'ok';
  }

  function resetCard(focus) {
    card.uid = null;
    card.result = null;
    card.seq++;
    scanInput.value = '';
    scanInput.readOnly = false;
    setScan('waiting', 'Ready. Keep this field focused and tap the card on the reader, or type the UID and press Enter.');
    if (focus) scanInput.focus();
  }

  function checkCard() {
    var uid = scanInput.value.trim();
    if (!uid) {
      setScan('error', 'Scan a card or type its UID first.');
      return Promise.resolve(false);
    }
    if (card.uid === uid && card.result) return Promise.resolve(true);
    var seq = ++card.seq;
    card.uid = null;
    card.result = null;
    setScan('checking', 'Checking card…');
    return postJson('/Enrollment/CheckCard', { cardUid: uid }).then(function (res) {
      if (seq !== card.seq) return false;
      if (!res.ok) {
        setScan('error', errorMessage(res, 'The card could not be checked. Try again.'));
        return false;
      }
      if (!res.data.canAssign) {
        setScan('error', 'Card ' + res.data.maskedCardUid + ': ' + res.data.message);
        scanInput.select();
        return false;
      }
      card.uid = uid;
      card.result = res.data;
      scanInput.readOnly = true;
      setScan('ok', 'Card ' + res.data.maskedCardUid + ' captured. ' + res.data.message);
      dirty = true;
      return true;
    }, function () {
      if (seq === card.seq) setScan('error', 'Could not reach the server. Check the connection and try again.');
      return false;
    });
  }

  // Keyboard-wedge readers type the UID and press Enter; some send no Enter, so a short pause also triggers the check.
  var autoCheck = debounce(function () {
    if (!scanInput.readOnly && scanInput.value.trim().length >= 4) checkCard();
  }, 700);
  scanInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); checkCard(); }
  });
  scanInput.addEventListener('input', function () {
    if (card.result) { card.result = null; card.uid = null; }
    setScan('waiting', 'Reading…');
    autoCheck();
  });
  $('[data-en-check-card]').addEventListener('click', checkCard);
  clearCardBtn.addEventListener('click', function () { resetCard(true); });

  function setRfidMode(mode) {
    rfidMode = mode;
    setSegment($('[data-en-panel="3"]'), 'data-en-rfid-mode', mode);
    $('[data-en-rfid-assign]').hidden = mode !== 'assign';
    $('[data-en-rfid-pending]').hidden = mode !== 'pending';
    if (mode === 'assign' && !card.result) scanInput.focus();
  }
  $$('[data-en-rfid-mode]').forEach(function (b) {
    b.addEventListener('click', function () { setRfidMode(b.getAttribute('data-en-rfid-mode')); });
  });

  function validateRfid() {
    if (rfidMode === 'pending') return Promise.resolve(true);
    if (card.result && card.uid === scanInput.value.trim()) return Promise.resolve(true);
    if (!scanInput.value.trim()) {
      setScan('error', 'Scan a card, or choose "Leave RFID pending".');
      scanInput.focus();
      return Promise.resolve(false);
    }
    return checkCard();
  }

  // ------------------------------------------------------------------ step 4: transport
  var busSelect = $('[data-en="transport.busId"]');
  var routeSelect = $('[data-en="transport.routeId"]');

  function setTransportMode(mode) {
    transportMode = mode;
    setSegment($('[data-en-panel="4"]'), 'data-en-transport-mode', mode);
    $('[data-en-transport-bus]').hidden = mode !== 'bus';
    if (mode === 'bus') busSelect.focus();
  }
  $$('[data-en-transport-mode]').forEach(function (b) {
    b.addEventListener('click', function () { if (!b.disabled) setTransportMode(b.getAttribute('data-en-transport-mode')); });
  });

  function selectedBus() {
    var opt = busSelect && busSelect.selectedOptions[0];
    if (!opt || !opt.value) return null;
    return {
      id: +opt.value,
      number: opt.getAttribute('data-number'),
      route: opt.getAttribute('data-route') || null,
      driver: opt.getAttribute('data-driver') || null,
      students: +opt.getAttribute('data-students')
    };
  }

  // Stops of each route, rendered by the server: { "<routeId>": [{ id, name, pickup, drop }] }.
  var routeStops = {};
  try { routeStops = JSON.parse(($('#enRouteStops') || {}).textContent || '{}'); } catch (e) { routeStops = {}; }
  var pickupSelect = $('[data-en="transport.pickupStopId"]');
  var dropSelect = $('[data-en="transport.dropStopId"]');

  // The route the student will ride: the override when chosen, otherwise the bus's own route.
  function effectiveRouteId() {
    if (routeSelect && routeSelect.value) return routeSelect.value;
    var opt = busSelect && busSelect.selectedOptions[0];
    return opt && opt.value ? (opt.getAttribute('data-route-id') || '') : '';
  }

  function fillStops() {
    var wrap = $('[data-en-stops]');
    if (!wrap) return;
    var stops = routeStops[effectiveRouteId()] || [];
    [[pickupSelect, 'pickup'], [dropSelect, 'drop']].forEach(function (pair) {
      var sel = pair[0], kind = pair[1];
      var keep = sel.value;
      sel.length = 1;
      stops.forEach(function (s) {
        var o = document.createElement('option');
        o.value = s.id;
        o.textContent = s.name + (s[kind] ? ' · ' + s[kind] : '');
        sel.appendChild(o);
      });
      sel.value = stops.some(function (s) { return String(s.id) === keep; }) ? keep : '';
    });
    wrap.hidden = stops.length === 0;
  }
  if (routeSelect) routeSelect.addEventListener('change', fillStops);

  if (busSelect) {
    busSelect.addEventListener('change', function () {
      fillStops();
      var b = selectedBus();
      var summary = $('[data-en-bus-summary]');
      summary.hidden = !b;
      if (!b) return;
      $('[data-en-bus-route]').textContent = b.route || 'No route set';
      $('[data-en-bus-driver]').textContent = b.driver || 'No driver assigned';
      $('[data-en-bus-students]').textContent = b.students + ' student' + (b.students === 1 ? '' : 's');
    });
  }

  function transport() {
    if (transportMode !== 'bus') return null;
    var b = selectedBus();
    if (!b) return null;
    return {
      busId: b.id,
      routeId: routeSelect && routeSelect.value ? +routeSelect.value : null,
      pickupStopId: pickupSelect && pickupSelect.value ? +pickupSelect.value : null,
      dropStopId: dropSelect && dropSelect.value ? +dropSelect.value : null
    };
  }

  function validateTransport() {
    var panel = $('[data-en-panel="4"]');
    var bad = transportMode === 'bus' && !selectedBus();
    markInvalid(busSelect, bad);
    fieldError(panel, '[data-en-error="transport.busId"]', bad ? 'Choose a bus, or select "No school transport".' : '');
    if (bad) busSelect.focus();
    return !bad;
  }

  // ------------------------------------------------------------------ step 5: review
  function block(title, gotoStep, body) {
    return '<section class="cg-enrol-review__block"><div class="cg-enrol-review__head"><h3 class="cg-enrol-review__title">' +
      escapeHtml(title) + '</h3>' +
      (gotoStep ? '<button type="button" class="cg-btn cg-btn--ghost cg-btn--sm" data-en-goto="' + gotoStep + '">Edit</button>' : '') +
      '</div>' + body + '</section>';
  }

  function deflist(rows) {
    return '<dl class="cg-deflist">' + rows.filter(function (r) { return r[1] != null && r[1] !== ''; }).map(function (r) {
      return '<dt>' + escapeHtml(r[0]) + '</dt><dd>' + (r[2] ? r[1] : escapeHtml(r[1])) + '</dd>';
    }).join('') + '</dl>';
  }

  function addressHtml(a, fallback) {
    var lines = addressLines(a);
    return lines ? lines.map(escapeHtml).join('<br>') : '<span class="cg-muted">' + escapeHtml(fallback) + '</span>';
  }

  function renderReview() {
    var s = student();
    var html = block('Student', 1, deflist([
      ['Name', s.fullName],
      ['Admission no.', s.admissionNumber || '—'],
      ['Date of birth', formatDate(s.dateOfBirth)],
      ['Gender', s.gender || 'Not specified'],
      ['Grade / section', [s.grade, s.section].filter(Boolean).join(' · ') || '—'],
      ['Address', addressHtml(s.address, 'Not provided'), true],
      ['Same-name record', duplicates.length
        ? '<span class="cg-badge cg-badge--warning">Enrolling anyway</span> ' + escapeHtml(duplicateReason()) : null, true]
    ]));

    html += block('Guardians', 2, guardianCards().map(function (c, i) {
      var g = guardianData(c);
      var pickup = g.isAuthorizedForPickup
        ? '<span class="cg-badge cg-badge--success">Pickup authorised</span>'
        : '<span class="cg-badge cg-badge--neutral">Not authorised for pickup</span>';
      if (g.existingGuardianId) {
        var sel = c._selected || {};
        return '<div class="cg-enrol-review__sub">' + escapeHtml(sel.fullName) + ' <span class="cg-badge cg-badge--info">Existing account</span>' +
          (g.confirmInactiveAccount ? ' <span class="cg-badge cg-badge--warning">Deactivated (confirmed)</span>' : '') + '</div>' +
          deflist([['Contact', [sel.email, sel.phone].filter(Boolean).join(' · ')], ['Relationship', g.relationship || '—'], ['Pickup', pickup, true]]);
      }
      return '<div class="cg-enrol-review__sub">' + escapeHtml(g.fullName) + ' <span class="cg-badge cg-badge--primary">New account</span></div>' +
        deflist([
          ['Email', g.email], ['Contact', g.contactNumber], ['Relationship', g.relationship || '—'], ['Pickup', pickup, true],
          ['Address', g.sameAsStudentAddress ? 'Same as student' : addressHtml(g.address, 'Not provided'), !g.sameAsStudentAddress]
        ]);
    }).join(''));

    html += block('RFID card', 3, rfidMode === 'pending' || !card.result
      ? '<span class="cg-badge cg-badge--warning">RFID pending</span><p class="cg-hint">No card will be assigned now.</p>'
      : deflist([['Card', '<span class="cg-mono">' + escapeHtml(card.result.maskedCardUid) + '</span>', true],
        ['Status', card.result.availability === 'New' ? 'New card, will be registered' : 'Registered spare card']]));

    var b = transportMode === 'bus' ? selectedBus() : null;
    var routeOverride = routeSelect && routeSelect.value ? routeSelect.selectedOptions[0].textContent.trim() : null;
    html += block('Transport', 4, b
      ? deflist([['Bus', b.number], ['Route', routeOverride || b.route || '—'], ['Driver', b.driver || '—']]
          .concat(pickupSelect && pickupSelect.value ? [['Pickup stop', pickupSelect.selectedOptions[0].textContent]] : [])
          .concat(dropSelect && dropSelect.value ? [['Drop-off stop', dropSelect.selectedOptions[0].textContent]] : []))
      : '<span class="cg-muted">No school transport</span>');

    var target = $('[data-en-review]');
    target.innerHTML = html;
    $$('[data-en-goto]', target).forEach(function (btn) {
      btn.addEventListener('click', function () { show(+btn.getAttribute('data-en-goto')); });
    });
  }

  // ------------------------------------------------------------------ navigation
  var backBtn = $('[data-en-back]');
  var nextBtn = $('[data-en-next]');
  var submitBtn = $('[data-en-submit]');

  function show(n, initial) {
    step = n;
    $$('[data-en-panel]').forEach(function (p) { p.hidden = +p.getAttribute('data-en-panel') !== n; });
    $$('[data-en-step]').forEach(function (s) {
      var i = +s.getAttribute('data-en-step');
      s.className = 'cg-step' + (i < n ? ' cg-step--done' : i === n ? ' cg-step--active' : '');
      if (i === n) s.setAttribute('aria-current', 'step'); else s.removeAttribute('aria-current');
    });
    backBtn.hidden = n === 1;
    nextBtn.hidden = n === TOTAL;
    submitBtn.hidden = n !== TOTAL;
    if (n === TOTAL) {
      $('[data-en-submit-error]').hidden = true;
      renderReview();
    }
    if (!initial) root.scrollIntoView({ behavior: 'smooth', block: 'start' });
    var panel = $('[data-en-panel="' + n + '"]');
    if (n === 3 && rfidMode === 'assign' && !card.result) scanInput.focus();
    else if (n === 1) $('[data-en="student.fullName"]').focus();
    else if (n === 2) { var first = panel.querySelector('[data-en-guardian] input:not([type=checkbox])'); if (first) first.focus(); }
    else if (n === TOTAL) submitBtn.focus();
  }

  function validateStep(n) {
    switch (n) {
      case 1: return validateStudent();
      case 2: return Promise.resolve(validateGuardians());
      case 3: return validateRfid();
      case 4: return Promise.resolve(validateTransport());
      default: return Promise.resolve(true);
    }
  }

  nextBtn.addEventListener('click', function () {
    nextBtn.disabled = true;
    validateStep(step).then(function (ok) {
      nextBtn.disabled = false;
      if (ok) show(step + 1);
    });
  });
  backBtn.addEventListener('click', function () { if (step > 1) show(step - 1); });

  // Steps already passed can be revisited from the stepper.
  $$('[data-en-step]').forEach(function (s) {
    s.addEventListener('click', function () {
      var i = +s.getAttribute('data-en-step');
      if (i < step && !finished) show(i);
    });
  });

  form.addEventListener('submit', function (e) { e.preventDefault(); });
  form.addEventListener('input', function () { dirty = true; });
  $('[data-en-add-guardian]').addEventListener('click', function () {
    var node = addGuardian();
    if (node) $('[data-g="fullName"]', node).focus();
  });

  // ------------------------------------------------------------------ submit
  function buildRequest() {
    return {
      idempotencyKey: idempotencyKey,
      student: student(),
      guardians: guardianCards().map(guardianData),
      cardUid: rfidMode === 'assign' && card.result ? card.uid : null,
      transport: transport(),
      duplicateStudentOverrideReason: duplicateReason()
    };
  }

  // The server found something only the admin can decide (it may have changed since step 1). Nothing was saved.
  function handleConfirmation(c) {
    if (c.possibleDuplicates && c.possibleDuplicates.length) {
      showDuplicates(c.possibleDuplicates);
      show(1);
      checkDuplicateReason(true);
      return;
    }
    var ids = (c.inactiveGuardians || []).map(function (g) { return String(g.guardianId); });
    guardianCards().forEach(function (card) {
      if (ids.indexOf(card.getAttribute('data-guardian-id')) >= 0) {
        $('[data-en-ginactive]', card).hidden = false;
        fieldError(card, '[data-g-error="inactive"]', 'This account is deactivated. Confirm linking it, or choose a different guardian.');
      }
    });
    show(2);
  }

  function showSubmitError(message) {
    $('[data-en-submit-error-text]').textContent = message;
    $('[data-en-submit-error]').hidden = false;
    $('[data-en-submit-error]').scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  submitBtn.addEventListener('click', function () {
    if (submitting) return;
    // Re-check everything: the admin may have edited earlier steps from the review.
    var checks = [validateStudentLocal() && checkDuplicateReason(false), validateGuardians(), validateTransport()];
    var firstBad = checks.indexOf(false);
    if (firstBad >= 0) { show([1, 2, 4][firstBad]); return; }

    submitting = true;
    submitBtn.disabled = true;
    submitBtn.classList.add('is-loading');
    $('.cg-btn__label', submitBtn).textContent = 'Enrolling…';

    postJson('/Enrollment/Submit', buildRequest()).then(function (res) {
      if (res.ok) { renderDone(res.data); return; }
      if (res.status === 409 && res.data && res.data.code === 'ConfirmationRequired') { handleConfirmation(res.data.confirmation); return; }
      showSubmitError(errorMessage(res, 'The enrollment could not be completed.'));
    }, function () {
      // Same idempotency key on retry, so a request that did reach the server is never applied twice.
      showSubmitError('Could not reach the server. Your entries are kept. Try again.');
    }).then(function () {
      submitting = false;
      submitBtn.disabled = false;
      submitBtn.classList.remove('is-loading');
      $('.cg-btn__label', submitBtn).textContent = 'Complete enrollment';
    });
  });

  function renderDone(r) {
    finished = true;
    form.hidden = true;
    $$('[data-en-step]').forEach(function (s) { s.className = 'cg-step cg-step--done'; s.removeAttribute('aria-current'); });

    $('[data-en-done-title]').textContent = r.alreadyEnrolled
      ? r.studentName + ' was already enrolled'
      : r.studentName + ' is enrolled';
    $('[data-en-done-sub]').textContent = r.alreadyEnrolled
      ? 'This enrollment had already been completed, so nothing new was saved. The current record is shown below.'
      : 'The student, guardian links, RFID card and transport were saved together.';

    var guardians = r.guardians.map(function (g) {
      var status;
      if (!g.created) status = '<span class="cg-badge cg-badge--info">Existing account linked</span>';
      else if (g.credentialEmailSent) status = '<span class="cg-badge cg-badge--success">Sign-in details emailed</span>';
      else status = '<span class="cg-badge cg-badge--danger">Email not sent</span><div class="cg-hint">' +
        escapeHtml(g.emailError || 'Delivery failed.') + ' The account exists; follow up from the guardian\'s profile.</div>';
      return '<div class="cg-enrol-review__sub"><a href="/Guardians/Details/' + g.guardianId + '">' + escapeHtml(g.fullName) + '</a></div>' +
        deflist([['Relationship', g.relationship || '—'], ['Pickup', g.isAuthorizedForPickup ? 'Authorised' : 'Not authorised'], ['Account', status, true]]);
    }).join('');

    var html = block('Guardians', 0, guardians);
    html += block('RFID card', 0, r.rfidPending
      ? '<span class="cg-badge cg-badge--warning">RFID pending</span><p class="cg-hint">Assign a card from the RFID Cards page.</p>'
      : deflist([['Card', '<span class="cg-mono">' + escapeHtml(r.maskedCardUid) + '</span>', true], ['Status', 'Assigned']]));
    html += block('Transport', 0, r.busNumber
      ? deflist([['Bus', r.busNumber], ['Route', r.routeName || '—']])
      : '<span class="cg-muted">No school transport</span>');
    $('[data-en-done-summary]').innerHTML = html;

    $('[data-en-done-profile]').href = '/Students/Details/' + r.studentId;
    $('[data-en-done-photo]').href = '/Students/Details/' + r.studentId + '#photo';
    var done = $('[data-en-done]');
    done.hidden = false;
    done.focus();
    root.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // ------------------------------------------------------------------ leaving
  window.addEventListener('beforeunload', function (e) {
    if (dirty && !finished) { e.preventDefault(); e.returnValue = ''; }
  });
  $('[data-en-cancel]').addEventListener('click', function (e) {
    if (dirty && !finished && !window.confirm('Discard this enrollment? Nothing has been saved yet.')) e.preventDefault();
    else finished = true;
  });

  addGuardian();
  show(1, true);
})();
