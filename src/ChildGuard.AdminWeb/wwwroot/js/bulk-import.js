/* ==========================================================================
   ChildGuard Admin — bulk import wizard
   Category → template/upload or manual grid → editable, validated preview →
   confirmation → server commit → summary. The server validates every row
   again on commit; this script never decides what is valid.
   Every value from a file is written with textContent / input.value, never
   as HTML, so a spreadsheet cannot inject markup into the page.
   ========================================================================== */
(function () {
  'use strict';

  var root = document.querySelector('[data-cg-bulk-import]');
  if (!root) return;

  var PAGE_SIZE = 50;
  var MAX_FILE_BYTES = 5 * 1024 * 1024;
  var MANUAL_START_ROWS = 5;

  function $(sel, scope) { return (scope || root).querySelector(sel); }
  function $$(sel, scope) { return Array.prototype.slice.call((scope || root).querySelectorAll(sel)); }
  function on(el, ev, fn) { if (el) el.addEventListener(ev, fn); }
  function el(tag, cls, text) {
    var node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }
  function toast(message, kind, title) {
    if (window.CgToast) window.CgToast.show(message, kind || 'info', title || null);
    else window.alert(message);
  }
  function csrfToken() {
    var meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.getAttribute('content') : '';
  }
  function plural(n, one, many) { return n.toLocaleString() + ' ' + (n === 1 ? one : many); }

  var categories = [];
  try { categories = JSON.parse(document.getElementById('cgImportCategories').textContent || '[]'); } catch (e) { categories = []; }

  var state = {
    category: null,      // ImportCategoryDto
    mode: 'upload',
    source: null,        // 'Excel' | 'Csv' | 'Manual'
    fileName: null,
    rows: [],            // { rowNumber, values, isValid, errors, edited }
    warnings: [],
    filter: 'all',
    page: 0,
    busy: false,
    committed: false
  };

  // ------------------------------------------------------------------ steps

  function setStep(step) {
    $$('[data-bi-panel]').forEach(function (p) { p.hidden = Number(p.getAttribute('data-bi-panel')) !== step; });
    $$('[data-bi-step]').forEach(function (s) {
      var n = Number(s.getAttribute('data-bi-step'));
      s.classList.toggle('cg-step--active', n === step);
      s.classList.toggle('cg-step--done', n < step);
      if (n === step) s.setAttribute('aria-current', 'step'); else s.removeAttribute('aria-current');
    });
    var panel = $('[data-bi-panel="' + step + '"]');
    if (panel && panel.scrollIntoView) panel.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }

  function stepState(step, text) {
    var s = $('[data-bi-step="' + step + '"] [data-bi-step-state]');
    if (s) s.textContent = text;
  }

  // ------------------------------------------------------------------ step 1

  function chooseCategory(name) {
    var cat = categories.filter(function (c) { return c.category === name; })[0];
    if (!cat) return;
    state.category = cat;
    resetData();
    $$('[data-bi-category]').forEach(function (b) { b.setAttribute('aria-checked', String(b.getAttribute('data-bi-category') === name)); });
    $$('[data-bi-category-name]').forEach(function (n) { n.textContent = cat.name.toLowerCase(); });
    $$('[data-bi-template]').forEach(function (a) {
      a.href = '/BulkImport/Template?category=' + encodeURIComponent(cat.category) + '&format=' + a.getAttribute('data-bi-template');
    });
    renderColumnGuide();
    renderManualGrid(MANUAL_START_ROWS);
    stepState(1, cat.name);
    setStep(2);
  }

  function renderColumnGuide() {
    var body = $('[data-bi-columns]');
    body.textContent = '';
    state.category.columns.forEach(function (c) {
      var tr = el('tr');
      var name = el('td', 'cg-strong', c.header);
      tr.appendChild(name);
      var req = el('td');
      req.appendChild(el('span', 'cg-badge ' + (c.required ? 'cg-badge--primary' : 'cg-badge--neutral'), c.required ? 'Required' : 'Optional'));
      tr.appendChild(req);
      tr.appendChild(el('td', null, c.description + (c.maxLength ? ' Up to ' + c.maxLength + ' characters.' : '')));
      tr.appendChild(el('td', 'cg-mono', c.example || ''));
      body.appendChild(tr);
    });
    var notes = $('[data-bi-notes]');
    notes.textContent = '';
    (state.category.notes || []).forEach(function (n) { notes.appendChild(el('li', null, n)); });
    notes.appendChild(el('li', null, 'At most ' + state.category.maxRows.toLocaleString() + ' rows per import.'));
  }

  // ------------------------------------------------------------------ step 2: mode

  function setMode(mode) {
    state.mode = mode;
    $$('[data-bi-mode]').forEach(function (t) {
      var active = t.getAttribute('data-bi-mode') === mode;
      t.classList.toggle('is-active', active);
      t.setAttribute('aria-selected', String(active));
    });
    // .cg-tabpanel is display:none unless .is-active; [hidden] keeps it out of the accessibility tree too.
    [['#biUpload', 'upload'], ['#biManual', 'manual']].forEach(function (p) {
      var panel = $(p[0]);
      panel.hidden = mode !== p[1];
      panel.classList.toggle('is-active', mode === p[1]);
    });
  }

  // ------------------------------------------------------------------ step 2: upload

  function handleFile(file) {
    if (!file || state.busy) return;
    var name = file.name || '';
    var ext = name.slice(name.lastIndexOf('.')).toLowerCase();
    if (ext !== '.xlsx' && ext !== '.csv') { toast('Choose an Excel (.xlsx) or CSV (.csv) file.', 'danger', 'Unsupported file'); return; }
    if (file.size > MAX_FILE_BYTES) { toast('The file is larger than 5 MB. Split it into smaller files.', 'danger', 'File too large'); return; }
    if (file.size === 0) { toast('The file is empty.', 'danger'); return; }

    var form = new FormData();
    form.append('file', file, name);

    var xhr = new XMLHttpRequest();
    xhr.open('POST', '/BulkImport/Preview?category=' + encodeURIComponent(state.category.category));
    xhr.setRequestHeader('X-CSRF-TOKEN', csrfToken());
    xhr.setRequestHeader('Accept', 'application/json');
    showProgress(0, 'Uploading ' + name + '…');
    setBusy(true);

    xhr.upload.onprogress = function (e) {
      if (e.lengthComputable) showProgress(Math.round(e.loaded / e.total * 100), 'Uploading ' + name + '…');
    };
    xhr.upload.onload = function () { showProgress(100, 'Checking every row…'); };
    xhr.onload = function () {
      setBusy(false);
      hideProgress();
      $('[data-bi-file]').value = '';
      if (xhr.status === 401) { location.href = '/Account/Login?expired=true'; return; }
      var body = null;
      try { body = JSON.parse(xhr.responseText); } catch (e) { body = null; }
      if (xhr.status >= 200 && xhr.status < 300 && body) {
        loadPreview(body, body.source || (ext === '.csv' ? 'Csv' : 'Excel'), body.fileName || name);
      } else {
        toast((body && body.message) || (xhr.status === 413 ? 'The file is too large.' : 'The file could not be read.'), 'danger', 'Upload refused');
      }
    };
    xhr.onerror = function () { setBusy(false); hideProgress(); toast('The upload failed. Check your connection and try again.', 'danger'); };
    xhr.send(form);
  }

  function showProgress(pct, label) {
    var box = $('[data-bi-progress]');
    box.hidden = false;
    $('[data-bi-progress-label]').textContent = label;
    $('[data-bi-progress-pct]').textContent = pct + '%';
    $('[data-bi-progress-bar]').style.width = pct + '%';
    $('[role="progressbar"]', box).setAttribute('aria-valuenow', String(pct));
  }
  function hideProgress() { $('[data-bi-progress]').hidden = true; }

  // ------------------------------------------------------------------ step 2: manual grid

  function headerRow(extraLeading, extraTrailing) {
    var tr = el('tr');
    tr.appendChild(el('th', null, extraLeading));
    state.category.columns.forEach(function (c) {
      var th = el('th', null, c.header);
      if (c.required) { var req = el('span', 'cg-label__req', ' *'); req.setAttribute('aria-hidden', 'true'); th.appendChild(req); }
      th.title = c.description;
      tr.appendChild(th);
    });
    (extraTrailing || []).forEach(function (t) { tr.appendChild(el('th', null, t)); });
    return tr;
  }

  function renderManualGrid(count) {
    var table = $('[data-bi-manual-grid]');
    var thead = $('thead', table), tbody = $('tbody', table);
    thead.textContent = '';
    tbody.textContent = '';
    thead.appendChild(headerRow('#', ['']));
    addManualRows(count);
  }

  function addManualRows(count) {
    var tbody = $('[data-bi-manual-grid] tbody');
    for (var i = 0; i < count; i++) {
      var tr = el('tr');
      tr.appendChild(el('td', 'cg-bi-rownum'));
      state.category.columns.forEach(function (c) {
        var td = el('td');
        var input = el('input');
        input.type = 'text';
        input.setAttribute('data-key', c.key);
        input.setAttribute('aria-label', c.header);
        if (c.maxLength) input.maxLength = Math.max(c.maxLength, 1);
        if (c.example) input.placeholder = c.example;
        td.appendChild(input);
        tr.appendChild(td);
      });
      var actions = el('td');
      var remove = el('button', 'cg-btn cg-btn--ghost cg-btn--sm', '✕');
      remove.type = 'button';
      remove.setAttribute('aria-label', 'Remove row');
      remove.setAttribute('data-bi-remove-row', '');
      actions.appendChild(remove);
      tr.appendChild(actions);
      tbody.appendChild(tr);
    }
    numberManualRows();
  }

  function numberManualRows() {
    $$('[data-bi-manual-grid] tbody tr').forEach(function (tr, i) { tr.firstChild.textContent = String(i + 1); });
  }

  /** Pasting a block copied from Excel (tabs and line breaks) fills the grid from the focused cell. */
  function onManualPaste(e) {
    var input = e.target;
    if (!input.matches || !input.matches('input[data-key]')) return;
    var text = (e.clipboardData || window.clipboardData).getData('text');
    if (!text || (text.indexOf('\t') < 0 && text.indexOf('\n') < 0)) return;
    e.preventDefault();
    var lines = text.replace(/\r\n?/g, '\n').replace(/\n$/, '').split('\n');
    var tbody = $('[data-bi-manual-grid] tbody');
    var startRow = Array.prototype.indexOf.call(tbody.children, input.closest('tr'));
    var keys = state.category.columns.map(function (c) { return c.key; });
    var startCol = keys.indexOf(input.getAttribute('data-key'));
    var needed = startRow + lines.length - tbody.children.length;
    if (startRow + lines.length > state.category.maxRows) { toast('That is more rows than one import allows.', 'warning'); return; }
    if (needed > 0) addManualRows(needed);
    lines.forEach(function (line, r) {
      var tr = tbody.children[startRow + r];
      line.split('\t').forEach(function (cell, c) {
        var key = keys[startCol + c];
        if (!key) return;
        var target = tr.querySelector('input[data-key="' + key + '"]');
        if (target) target.value = cell.trim();
      });
    });
  }

  function collectManualRows() {
    var rows = [];
    $$('[data-bi-manual-grid] tbody tr').forEach(function (tr, i) {
      var values = {};
      var any = false;
      $$('input[data-key]', tr).forEach(function (input) {
        var v = input.value.trim();
        values[input.getAttribute('data-key')] = v === '' ? null : v;
        if (v !== '') any = true;
      });
      if (any) rows.push({ rowNumber: i + 1, values: values });
    });
    return rows;
  }

  function validateManual() {
    var rows = collectManualRows();
    if (rows.length === 0) { toast('Type at least one row first.', 'warning'); return; }
    if (rows.length > state.category.maxRows) { toast('At most ' + state.category.maxRows + ' rows per import.', 'warning'); return; }
    var button = $('[data-bi-validate-manual]');
    postJson('/BulkImport/Validate', { category: state.category.category, rows: rows }, button).then(function (preview) {
      if (preview) loadPreview(preview, 'Manual', 'Manual entry');
    });
  }

  // ------------------------------------------------------------------ step 3: preview

  function loadPreview(preview, source, fileName) {
    state.source = source;
    state.fileName = fileName;
    state.warnings = preview.warnings || [];
    state.rows = preview.rows.map(function (r) {
      return { rowNumber: r.rowNumber, values: Object.assign({}, r.values), isValid: r.isValid, errors: r.errors || [], edited: false };
    });
    state.filter = state.rows.some(function (r) { return !r.isValid; }) ? 'invalid' : 'all';
    state.page = 0;
    state.committed = false;
    var label = source === 'Manual' ? 'Typed into the grid' : (source === 'Csv' ? 'CSV file · ' : 'Excel workbook · ') + (source === 'Manual' ? '' : fileName);
    $('[data-bi-source-label]').textContent = label;
    stepState(2, source === 'Manual' ? 'Manual entry' : fileName);
    renderWarnings();
    renderPreview();
    setStep(3);
  }

  /** Applies a fresh validation to the rows currently held, keeping their order. */
  function applyValidation(preview) {
    var byNumber = {};
    preview.rows.forEach(function (r) { byNumber[r.rowNumber] = r; });
    state.rows.forEach(function (row) {
      var r = byNumber[row.rowNumber];
      if (!r) return;
      row.isValid = r.isValid;
      row.errors = r.errors || [];
      row.values = Object.assign({}, r.values);
      row.edited = false;
    });
    renderPreview();
  }

  function renderWarnings() {
    var box = $('[data-bi-warnings]');
    var list = $('[data-bi-warning-list]');
    list.textContent = '';
    state.warnings.forEach(function (w) { list.appendChild(el('li', null, w)); });
    box.hidden = state.warnings.length === 0;
  }

  function counts() {
    var valid = 0, invalid = 0, edited = 0;
    state.rows.forEach(function (r) { if (r.edited) edited++; if (r.isValid) valid++; else invalid++; });
    return { total: state.rows.length, valid: valid, invalid: invalid, edited: edited };
  }

  function filteredRows() {
    return state.rows.filter(function (r) {
      return state.filter === 'all' || (state.filter === 'invalid' ? !r.isValid : r.isValid);
    });
  }

  function renderPreview() {
    var c = counts();
    $('[data-bi-total]').textContent = c.total.toLocaleString();
    $('[data-bi-valid]').textContent = c.valid.toLocaleString();
    $('[data-bi-invalid]').textContent = c.invalid.toLocaleString();
    $('[data-bi-invalid-icon]').className = 'cg-kpi__icon ' + (c.invalid ? 'cg-kpi__icon--danger' : 'cg-kpi__icon--neutral');
    $('[data-bi-download-errors]').hidden = c.invalid === 0;
    $('[data-bi-revalidate]').disabled = c.edited === 0;
    $('[data-bi-revalidate]').textContent = c.edited ? 'Re-check ' + plural(c.edited, 'edited row', 'edited rows') : 'Re-check edited rows';
    $('[data-bi-import-label]').textContent = c.valid ? 'Import ' + plural(c.valid, 'record', 'records') : 'Import';
    stepState(3, c.invalid ? plural(c.invalid, 'row needs', 'rows need') + ' attention' : 'All rows valid');

    $$('[data-bi-filter]').forEach(function (b) {
      var active = b.getAttribute('data-bi-filter') === state.filter;
      b.classList.toggle('is-active', active);
      b.setAttribute('aria-pressed', String(active));
    });

    var table = $('[data-bi-preview-grid]');
    var thead = $('thead', table), tbody = $('tbody', table);
    thead.textContent = '';
    tbody.textContent = '';
    thead.appendChild(headerRow('Row', ['Status']));

    var rows = filteredRows();
    var pages = Math.max(1, Math.ceil(rows.length / PAGE_SIZE));
    state.page = Math.min(state.page, pages - 1);
    var slice = rows.slice(state.page * PAGE_SIZE, (state.page + 1) * PAGE_SIZE);

    if (slice.length === 0) {
      var empty = el('tr');
      var td = el('td', 'cg-muted', state.filter === 'invalid' ? 'No rows with errors.' : 'No rows to show.');
      td.colSpan = state.category.columns.length + 2;
      empty.appendChild(td);
      tbody.appendChild(empty);
    }

    slice.forEach(function (row) {
      var tr = el('tr', row.edited ? 'cg-bi-row--edited' : row.isValid ? 'cg-bi-row--valid' : 'cg-bi-row--invalid');
      tr.setAttribute('data-row', String(row.rowNumber));
      tr.appendChild(el('td', 'cg-bi-rownum', String(row.rowNumber)));
      var errorColumns = {};
      row.errors.forEach(function (e) { if (e.column) errorColumns[e.column] = e.message; });

      state.category.columns.forEach(function (col) {
        var cell = el('td', errorColumns[col.key] && !row.edited ? 'cg-bi-cell--error' : null);
        var input = el('input');
        input.type = 'text';
        input.value = row.values[col.key] == null ? '' : row.values[col.key];
        input.setAttribute('data-key', col.key);
        input.setAttribute('aria-label', col.header + ', row ' + row.rowNumber);
        if (errorColumns[col.key]) { input.setAttribute('aria-invalid', 'true'); input.title = errorColumns[col.key]; }
        cell.appendChild(input);
        tr.appendChild(cell);
      });

      var status = el('td', 'cg-bi-status');
      if (row.edited) {
        status.appendChild(el('span', 'cg-badge cg-badge--warning', 'Edited · re-check'));
      } else if (row.isValid) {
        status.appendChild(el('span', 'cg-badge cg-badge--success', 'Valid'));
      } else {
        status.appendChild(el('span', 'cg-badge cg-badge--danger', plural(row.errors.length, 'error', 'errors')));
        var list = el('ul', 'cg-bi-errors');
        row.errors.forEach(function (e) {
          var col = state.category.columns.filter(function (c) { return c.key === e.column; })[0];
          list.appendChild(el('li', null, (col ? col.header + ': ' : '') + e.message));
        });
        status.appendChild(list);
      }
      tr.appendChild(status);
      tbody.appendChild(tr);
    });

    var from = rows.length ? state.page * PAGE_SIZE + 1 : 0;
    var to = Math.min(rows.length, (state.page + 1) * PAGE_SIZE);
    $('[data-bi-page-label]').textContent = 'Showing ' + from + '–' + to + ' of ' + rows.length.toLocaleString() + ' · page ' + (state.page + 1) + ' of ' + pages;
    $('[data-bi-page="-1"]').disabled = state.page === 0;
    $('[data-bi-page="1"]').disabled = state.page >= pages - 1;
  }

  function onPreviewEdit(e) {
    var input = e.target;
    if (!input.matches('input[data-key]')) return;
    var tr = input.closest('tr');
    var number = Number(tr.getAttribute('data-row'));
    var row = state.rows.filter(function (r) { return r.rowNumber === number; })[0];
    if (!row) return;
    var v = input.value.trim();
    row.values[input.getAttribute('data-key')] = v === '' ? null : v;
    if (!row.edited) {
      row.edited = true;
      tr.className = 'cg-bi-row--edited';
      var c = counts();
      $('[data-bi-revalidate]').disabled = false;
      $('[data-bi-revalidate]').textContent = 'Re-check ' + plural(c.edited, 'edited row', 'edited rows');
    }
  }

  function revalidate() {
    // All rows are sent: duplicates are judged across the whole set, not row by row.
    var payload = { category: state.category.category, rows: state.rows.map(function (r) { return { rowNumber: r.rowNumber, values: r.values }; }) };
    postJson('/BulkImport/Validate', payload, $('[data-bi-revalidate]')).then(function (preview) {
      if (!preview) return;
      applyValidation(preview);
      toast(preview.invalidRows ? plural(preview.invalidRows, 'row still needs', 'rows still need') + ' attention.' : 'Every row is valid.',
        preview.invalidRows ? 'warning' : 'success');
    });
  }

  /** Client-side copy of the error report for rows not yet imported. Formula-escaped like the server's. */
  function downloadErrors() {
    var cols = state.category.columns;
    var lines = [['Row'].concat(cols.map(function (c) { return c.header; })).concat(['Errors'])];
    state.rows.filter(function (r) { return !r.isValid; }).forEach(function (r) {
      lines.push([String(r.rowNumber)]
        .concat(cols.map(function (c) { return r.values[c.key] == null ? '' : r.values[c.key]; }))
        .concat([r.errors.map(function (e) {
          var col = cols.filter(function (c) { return c.key === e.column; })[0];
          return (col ? col.header + ': ' : '') + e.message;
        }).join('; ')]));
    });
    var csv = lines.map(function (line) {
      return line.map(function (v) {
        var s = String(v);
        if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
      }).join(',');
    }).join('\r\n');
    var blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
    var a = el('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'childguard-' + state.category.category.toLowerCase() + '-errors.csv';
    document.body.appendChild(a);
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 0);
  }

  // ------------------------------------------------------------------ confirm + commit

  var modal = document.getElementById('biConfirmModal');

  function openConfirm() {
    var c = counts();
    var skip = $('[data-bi-skip-invalid]').checked;
    if (c.valid === 0 && c.edited === 0) { toast('There are no valid rows to import. Fix the errors first.', 'warning'); return; }
    if (c.invalid > 0 && !skip && c.edited === 0) {
      toast('Some rows have errors. Fix them, or tick "Skip rows with errors".', 'warning');
      return;
    }
    var noun = state.category.name.toLowerCase();
    $('[data-bi-confirm-text]').textContent = 'Import ' + plural(c.valid, 'valid row', 'valid rows') + ' of ' + noun + ' from ' +
      (state.source === 'Manual' ? 'the grid' : '"' + state.fileName + '"') + '?';
    var extra = [];
    if (c.invalid && skip) extra.push(plural(c.invalid, 'row', 'rows') + ' with errors will be skipped and kept in the error report.');
    if (c.edited) extra.push(plural(c.edited, 'edited row', 'edited rows') + ' will be checked on the server and imported if valid.');
    if (state.category.category === 'Guardians') extra.push('Each new guardian is emailed their own sign-in details.');
    extra.push('All records are saved together in one step; records can be deactivated later, not deleted.');
    $('[data-bi-confirm-extra]').textContent = extra.join(' ');
    modal.hidden = false;
    document.body.style.overflow = 'hidden';
    $('[data-bi-commit]', modal).focus();
  }

  function closeConfirm() {
    modal.hidden = true;
    document.body.style.overflow = '';
  }

  function commit() {
    var button = $('[data-bi-commit]', modal);
    var payload = {
      category: state.category.category,
      source: state.source,
      fileName: state.fileName,
      skipInvalidRows: $('[data-bi-skip-invalid]').checked,
      rows: state.rows.map(function (r) { return { rowNumber: r.rowNumber, values: r.values }; })
    };
    postJson('/BulkImport/Commit', payload, button, 'Importing…').then(function (summary) {
      closeConfirm();
      if (summary) showSummary(summary);
    });
  }

  function showSummary(s) {
    state.committed = true;
    var badge = $('[data-bi-summary-status]');
    badge.textContent = '';
    var kind = s.status === 'Completed' ? 'success' : s.status === 'CompletedWithErrors' ? 'warning' : 'danger';
    var label = s.status === 'Completed' ? 'Completed' : s.status === 'CompletedWithErrors' ? 'Completed with errors' : 'Failed';
    badge.appendChild(el('span', 'cg-badge cg-badge--lg cg-badge--' + kind, label));
    $('[data-bi-summary-message]').textContent = s.message;
    $('[data-bi-summary-total]').textContent = s.totalRows.toLocaleString();
    $('[data-bi-summary-inserted]').textContent = s.insertedRows.toLocaleString();
    $('[data-bi-summary-failed]').textContent = s.failedRows.toLocaleString();
    $('[data-bi-summary-details]').href = '/BulkImport/Details/' + s.dataImportId;
    var errors = $('[data-bi-summary-errors]');
    errors.hidden = s.failedRows === 0;
    errors.href = '/BulkImport/ErrorReport/' + s.dataImportId + '?format=csv';

    var failures = s.emailFailures || [];
    var box = $('[data-bi-email-failures]');
    var list = $('[data-bi-email-failure-list]');
    list.textContent = '';
    failures.forEach(function (f) { list.appendChild(el('li', null, 'Row ' + f.rowNumber + ': ' + f.fullName + ' <' + f.email + '>' + (f.error ? ' — ' + f.error : ''))); });
    box.hidden = failures.length === 0;

    stepState(4, label);
    $('[data-bi-step="4"]').classList.toggle('cg-step--failed', s.status === 'Failed');
    setStep(4);
    toast(s.message, kind === 'danger' ? 'danger' : kind, label);
  }

  // ------------------------------------------------------------------ shared

  function setBusy(busy) {
    state.busy = busy;
    root.setAttribute('aria-busy', String(busy));
  }

  function postJson(url, body, button, busyLabel) {
    if (state.busy) return Promise.resolve(null);
    setBusy(true);
    var original = button ? button.innerHTML : null;
    if (button) {
      button.disabled = true;
      button.textContent = busyLabel || 'Checking…';
    }
    return fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'X-CSRF-TOKEN': csrfToken() },
      body: JSON.stringify(body)
    }).then(function (response) {
      return response.text().then(function (text) {
        var json = null;
        try { json = text ? JSON.parse(text) : null; } catch (e) { json = null; }
        if (!response.ok) {
          toast((json && json.message) || 'The request failed (' + response.status + ').', 'danger', 'Could not complete');
          return null;
        }
        return json;
      });
    }).catch(function () {
      toast('The server could not be reached. Nothing was imported.', 'danger');
      return null;
    }).then(function (result) {
      setBusy(false);
      if (button) { button.disabled = false; button.innerHTML = original; }
      return result;
    });
  }

  function resetData() {
    state.rows = [];
    state.source = null;
    state.fileName = null;
    state.warnings = [];
    state.committed = false;
    stepState(2, 'Upload or type');
    stepState(3, 'Check and fix');
    stepState(4, 'Summary');
    $('[data-bi-step="4"]').classList.remove('cg-step--failed');
  }

  // ------------------------------------------------------------------ wiring

  $$('[data-bi-category]').forEach(function (b) {
    on(b, 'click', function () { chooseCategory(b.getAttribute('data-bi-category')); });
  });
  $$('[data-bi-back]').forEach(function (b) {
    on(b, 'click', function () { setStep(Number(b.getAttribute('data-bi-back'))); });
  });
  $$('[data-bi-mode]').forEach(function (t) {
    on(t, 'click', function () { setMode(t.getAttribute('data-bi-mode')); });
  });

  var fileInput = $('[data-bi-file]');
  on(fileInput, 'change', function () { handleFile(fileInput.files && fileInput.files[0]); });
  var dropzone = $('[data-bi-dropzone]');
  ['dragenter', 'dragover'].forEach(function (ev) {
    on(dropzone, ev, function (e) { e.preventDefault(); dropzone.classList.add('is-dragover'); });
  });
  ['dragleave', 'drop'].forEach(function (ev) {
    on(dropzone, ev, function (e) { e.preventDefault(); dropzone.classList.remove('is-dragover'); });
  });
  on(dropzone, 'drop', function (e) {
    var files = e.dataTransfer && e.dataTransfer.files;
    if (files && files.length > 1) toast('Drop one file at a time; the first one is used.', 'info');
    handleFile(files && files[0]);
  });

  var manual = $('[data-bi-manual-grid]');
  on(manual, 'paste', onManualPaste);
  on(manual, 'click', function (e) {
    var btn = e.target.closest('[data-bi-remove-row]');
    if (!btn) return;
    btn.closest('tr').remove();
    if ($$('[data-bi-manual-grid] tbody tr').length === 0) addManualRows(1);
    numberManualRows();
  });
  $$('[data-bi-add-rows]').forEach(function (b) {
    on(b, 'click', function () {
      var current = $$('[data-bi-manual-grid] tbody tr').length;
      var add = Math.min(Number(b.getAttribute('data-bi-add-rows')), state.category.maxRows - current);
      if (add <= 0) { toast('That is the most rows one import allows.', 'warning'); return; }
      addManualRows(add);
    });
  });
  on($('[data-bi-clear-manual]'), 'click', function () { renderManualGrid(MANUAL_START_ROWS); });
  on($('[data-bi-validate-manual]'), 'click', validateManual);

  var preview = $('[data-bi-preview-grid]');
  on(preview, 'input', onPreviewEdit);
  $$('[data-bi-filter]').forEach(function (b) {
    on(b, 'click', function () { state.filter = b.getAttribute('data-bi-filter'); state.page = 0; renderPreview(); });
  });
  $$('[data-bi-page]').forEach(function (b) {
    on(b, 'click', function () { state.page += Number(b.getAttribute('data-bi-page')); renderPreview(); });
  });
  on($('[data-bi-revalidate]'), 'click', revalidate);
  on($('[data-bi-download-errors]'), 'click', downloadErrors);

  on($('[data-bi-confirm-open]'), 'click', openConfirm);
  $$('[data-bi-confirm-close]', modal).forEach(function (b) { on(b, 'click', closeConfirm); });
  on($('[data-bi-commit]', modal), 'click', commit);
  on($('[data-bi-restart]'), 'click', function () {
    resetData();
    $$('[data-bi-category]').forEach(function (b) { b.setAttribute('aria-checked', 'false'); });
    stepState(1, 'What to import');
    setStep(1);
  });

  // An unimported preview is easy to lose by navigating away.
  window.addEventListener('beforeunload', function (e) {
    if (state.rows.length && !state.committed) { e.preventDefault(); e.returnValue = ''; }
  });

  var initial = root.getAttribute('data-initial-category');
  if (initial) chooseCategory(initial);
})();
