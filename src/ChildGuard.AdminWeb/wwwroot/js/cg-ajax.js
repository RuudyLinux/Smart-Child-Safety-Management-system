/* ==========================================================================
   ChildGuard Admin — shared AJAX layer
   Progressive enhancement: a form marked data-cg-ajax still posts normally
   without this file. With it, the form is sent with fetch, the controller
   answers an envelope { ok, message, redirect, regions, errors } (see
   Services/AjaxEnvelope.cs), and only the named page regions are re-rendered.

   Markup:
     <form data-cg-ajax data-cg-regions="a b">        save without reloading
     <button data-cg-action="/url" data-cg-confirm="…"> post one click
     <div data-cg-region="a">                          a part that can refresh
   Security is unchanged: the antiforgery token travels as the form field and
   as X-CSRF-TOKEN, cookies are same-origin, the server authorises as before.
   ========================================================================== */
(function () {
  'use strict';

  var cg = window.cg = window.cg || {};
  var TIMEOUT_MS = 30000;
  var UPLOAD_TIMEOUT_MS = 120000;
  var LOGIN_URL = '/Account/Login?expired=true';

  var MESSAGES = {
    offline: 'You appear to be offline. Check the connection and try again.',
    network: 'Could not reach the server. Check the connection and try again.',
    timeout: 'The server took too long to answer. Try again.',
    403: 'You do not have permission to do that.',
    404: 'That record no longer exists. Refresh the page.',
    429: 'Too many requests. Wait a moment and try again.',
    server: 'Something went wrong on the server. Try again.',
    generic: 'The request could not be completed. Try again.'
  };

  function csrfToken() {
    var meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.getAttribute('content') : '';
  }

  function messageFor(status) {
    if (status === 403 || status === 404 || status === 429) return MESSAGES[status];
    if (status >= 500) return MESSAGES.server;
    return MESSAGES.generic;
  }

  function toast(message, kind) {
    if (cg.toast) cg.toast(message, kind);
  }

  // ------------------------------------------------------------ Requests
  /**
   * cg.request(url, options) → Promise<{ status, ok, envelope, data, text, message, aborted }>
   * Never rejects: network failure, timeout and HTTP errors all resolve with ok=false and a message
   * fit for the admin. A 401 sends the browser to sign-in. options: method, body (FormData |
   * URLSearchParams | object → JSON), headers, timeoutMs, signal (external abort), retry (GET only).
   */
  function request(url, options) {
    options = options || {};
    var method = (options.method || 'GET').toUpperCase();
    var attempts = method === 'GET' && options.retry !== false ? 2 : 1;

    function attempt(n) {
      var controller = new AbortController();
      var timedOut = false;
      var timer = setTimeout(function () { timedOut = true; controller.abort(); }, options.timeoutMs || TIMEOUT_MS);
      var external = options.signal;
      var onExternalAbort = function () { controller.abort(); };
      if (external) {
        if (external.aborted) controller.abort();
        else external.addEventListener('abort', onExternalAbort);
      }

      var headers = { 'X-Requested-With': 'fetch', 'Accept': options.accept || 'application/json' };
      if (method !== 'GET') headers['X-CSRF-TOKEN'] = csrfToken();
      var body = options.body;
      if (body && !(body instanceof FormData) && !(body instanceof URLSearchParams) && typeof body === 'object') {
        headers['Content-Type'] = 'application/json';
        body = JSON.stringify(body);
      }
      Object.keys(options.headers || {}).forEach(function (k) { headers[k] = options.headers[k]; });

      function done() {
        clearTimeout(timer);
        if (external) external.removeEventListener('abort', onExternalAbort);
      }

      return fetch(url, { method: method, headers: headers, body: method === 'GET' ? undefined : body, credentials: 'same-origin', signal: controller.signal })
        .then(function (response) {
          done();
          var type = response.headers.get('Content-Type') || '';
          var read = type.indexOf('json') !== -1
            ? response.json().catch(function () { return null; })
            : response.text().then(function (t) { return { __text: t }; });
          return read.then(function (payload) {
            var text = payload && payload.__text !== undefined ? payload.__text : null;
            var data = text === null ? payload : null;
            var envelope = data && typeof data.ok === 'boolean' ? data : null;
            if (response.status === 401) {
              location.href = (envelope && envelope.redirect) || LOGIN_URL;
            }
            var message = (envelope && envelope.message) || (data && data.message) ||
              (response.ok ? null : messageFor(response.status));
            return { status: response.status, ok: response.ok && (!envelope || envelope.ok), envelope: envelope, data: data, text: text, message: message, aborted: false };
          });
        })
        .catch(function (error) {
          done();
          if (external && external.aborted) return { status: 0, ok: false, aborted: true, message: null };
          if (timedOut) return { status: 0, ok: false, aborted: false, message: MESSAGES.timeout };
          if (n + 1 < attempts && navigator.onLine !== false) return attempt(n + 1);
          return { status: 0, ok: false, aborted: false, message: navigator.onLine === false ? MESSAGES.offline : MESSAGES.network };
        });
    }

    return attempt(0);
  }

  // ------------------------------------------------------------- Regions
  // Region refreshes after saves run one at a time and are merged, never cancelled: if another starts while
  // one is loading, its regions wait and are fetched together right after, from a page that already reflects
  // both saves. List loads are different: only the newest list view matters, so a new one aborts the last.
  var regionRunning = null;   // promise of the refresh in flight
  var regionPending = null;   // { names: [], promise, resolve } waiting for it
  var listController = null;

  function splitNames(value) {
    if (!value) return [];
    if (Array.isArray(value)) return value.filter(Boolean);
    return String(value).split(/[\s,]+/).filter(Boolean);
  }

  /**
   * Re-renders the named [data-cg-region] parts of this page from a fresh copy of it. The server names
   * every part a change affects; a page refreshes the ones it shows, and reloads if it shows none.
   */
  function refresh(names, url) {
    names = splitNames(names).filter(function (n) { return document.querySelector('[data-cg-region="' + n + '"]'); });
    if (!names.length) { location.reload(); return Promise.resolve(); }

    if (url) {
      if (listController) listController.abort();
      listController = new AbortController();
      return swapRegions(names, url, listController.signal);
    }

    if (!regionRunning) {
      regionRunning = swapRegions(names, location.href, null).then(nextRegionRefresh, nextRegionRefresh);
      return regionRunning;
    }
    if (!regionPending) {
      var resolve;
      regionPending = { names: [], promise: new Promise(function (r) { resolve = r; }) };
      regionPending.resolve = resolve;
    }
    names.forEach(function (n) { if (regionPending.names.indexOf(n) === -1) regionPending.names.push(n); });
    return regionPending.promise;
  }

  function nextRegionRefresh() {
    regionRunning = null;
    if (!regionPending) return;
    var waiting = regionPending;
    regionPending = null;
    regionRunning = swapRegions(waiting.names, location.href, null).then(nextRegionRefresh, nextRegionRefresh);
    regionRunning.then(waiting.resolve, waiting.resolve);
  }

  /** Fetches url and swaps the named regions in place (scroll, focus, tabs and open details kept). */
  function swapRegions(names, url, signal) {
    names = names.filter(function (n) { return document.querySelector('[data-cg-region="' + n + '"]'); });
    if (!names.length) return Promise.resolve();
    var current = names.map(function (n) { return document.querySelector('[data-cg-region="' + n + '"]'); });
    current.forEach(function (el) { el.classList.add('cg-region--loading'); el.setAttribute('aria-busy', 'true'); });

    return request(url, { accept: 'text/html', headers: { 'X-CG-Regions': names.join(',') }, signal: signal || undefined })
      .then(function (result) {
        current.forEach(function (el) { el.classList.remove('cg-region--loading'); el.removeAttribute('aria-busy'); });
        if (result.aborted) return;
        if (!result.ok || result.text === null) {
          toast(result.message || MESSAGES.generic, 'danger');
          return;
        }
        var doc = new DOMParser().parseFromString(result.text, 'text/html');
        var scrollX = window.scrollX, scrollY = window.scrollY;
        var focusedId = document.activeElement && document.activeElement.id;
        var swapped = [];

        for (var i = 0; i < names.length; i++) {
          var fresh = doc.querySelector('[data-cg-region="' + names[i] + '"]');
          if (!fresh) { location.reload(); return; }
          var old = current[i];
          var openDetails = Array.prototype.map.call(old.querySelectorAll('details[open][id]'), function (d) { return d.id; });
          var node = document.importNode(fresh, true);
          old.replaceWith(node);
          openDetails.forEach(function (id) { var d = document.getElementById(id); if (d) d.open = true; });
          swapped.push(node);
        }

        swapped.forEach(function (node) { if (cg.init) cg.init(node); });
        if (cg.syncTabPanels) cg.syncTabPanels();
        window.scrollTo(scrollX, scrollY);
        if (focusedId) { var f = document.getElementById(focusedId); if (f && f !== document.activeElement) f.focus({ preventScroll: true }); }
        document.dispatchEvent(new CustomEvent('cg:regions-updated', { detail: { names: names } }));
      });
  }

  // -------------------------------------------------------------- Errors
  function clearErrors(form) {
    form.querySelectorAll('[data-cg-error-set]').forEach(function (el) {
      el.textContent = '';
      el.classList.remove('field-validation-error');
      el.classList.add('field-validation-valid');
      el.removeAttribute('data-cg-error-set');
    });
    form.querySelectorAll('[aria-invalid="true"]').forEach(function (el) { el.removeAttribute('aria-invalid'); });
    var summary = form.querySelector('[data-cg-error-summary]');
    if (summary) { summary.hidden = true; summary.textContent = ''; }
  }

  function cssEscape(value) {
    return window.CSS && CSS.escape ? CSS.escape(value) : String(value).replace(/["\\]/g, '\\$&');
  }

  /** Shows field errors next to their fields; returns messages that have no field on this form. */
  function showErrors(form, errors) {
    var unplaced = [];
    var first = null;
    Object.keys(errors || {}).forEach(function (key) {
      var text = (errors[key] || []).join(' ');
      if (!text) return;
      var slot = key ? form.querySelector('[data-valmsg-for="' + cssEscape(key) + '"]') : null;
      var field = key ? form.querySelector('[name="' + cssEscape(key) + '"]') : null;
      if (field) { field.setAttribute('aria-invalid', 'true'); first = first || field; }
      if (slot) {
        slot.textContent = text;
        slot.classList.remove('field-validation-valid');
        slot.classList.add('field-validation-error');
        slot.setAttribute('data-cg-error-set', '1');
      } else {
        unplaced.push(text);
      }
    });
    var summary = form.querySelector('[data-cg-error-summary]');
    if (summary && unplaced.length) {
      summary.textContent = unplaced.join(' ');
      summary.hidden = false;
      unplaced = [];
    }
    if (first) first.focus();
    return unplaced;
  }

  // ------------------------------------------------- Forms and actions
  function submitterOf(form, event) {
    return (event && event.submitter) || form.querySelector('button[type="submit"], button:not([type])');
  }

  function busy(el, on) {
    if (!el) return;
    el.disabled = on;
    if (cg.setBusy) cg.setBusy(el, on);
  }

  /** Acts on an answer: toast, then redirect, refresh regions, or show errors. */
  function handle(result, source, regionsFallback) {
    if (result.aborted) return Promise.resolve(false);
    var env = result.envelope;
    if (result.ok) {
      if (env && env.redirect) { location.assign(env.redirect); return Promise.resolve(true); }
      if (result.message) toast(result.message, 'success');
      var modal = source.closest && source.closest('.cg-modal');
      if (modal && cg.closeModal) cg.closeModal(modal);
      if (source.tagName === 'FORM' && source.hasAttribute('data-cg-reset-on-success')) source.reset();
      document.dispatchEvent(new CustomEvent('cg:ajax-done', { detail: { source: source, envelope: env } }));
      var names = (env && env.regions && env.regions.length) ? env.regions : splitNames(regionsFallback);
      return refresh(names).then(function () { return false; });
    }
    var leftover = [];
    if (env && env.errors && source.tagName === 'FORM') leftover = showErrors(source, env.errors);
    toast(leftover.length ? leftover.join(' ') : (result.message || MESSAGES.generic), 'danger');
    return Promise.resolve(false);
  }

  function hasFile(form) {
    return !!form.querySelector('input[type="file"]');
  }

  /** data-cg-ajax (any value but "off"): shared partials render "off" on pages that are not converted yet. */
  function isAjaxForm(form) {
    return form instanceof HTMLFormElement && form.hasAttribute('data-cg-ajax') && form.getAttribute('data-cg-ajax') !== 'off';
  }

  document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!isAjaxForm(form)) return;
    // A handler before us (the destructive confirmation) declined: do nothing.
    if (e.defaultPrevented) return;
    e.preventDefault();
    if (form.dataset.cgInflight === '1') return;   // double click, Enter twice
    form.dataset.cgInflight = '1';

    var submitter = submitterOf(form, e);
    var body;
    try { body = new FormData(form, submitter && submitter.name ? submitter : undefined); }
    catch (err) { body = new FormData(form); }
    clearErrors(form);
    busy(submitter, true);

    var navigating = false;
    request(form.action, { method: form.method || 'POST', body: body, timeoutMs: hasFile(form) ? UPLOAD_TIMEOUT_MS : TIMEOUT_MS })
      .then(function (result) { return handle(result, form, form.getAttribute('data-cg-regions')); })
      .then(function (redirecting) { navigating = redirecting; })
      .finally(function () {
        delete form.dataset.cgConfirmed;   // the next submit asks again
        if (navigating) return;            // keep the button busy while the next page loads
        delete form.dataset.cgInflight;
        if (document.contains(submitter)) busy(submitter, false);
      });
  });

  document.addEventListener('click', function (e) {
    var btn = e.target.closest && e.target.closest('[data-cg-action]');
    if (!btn || btn.disabled) return;
    e.preventDefault();
    var question = btn.getAttribute('data-cg-confirm');
    if (question && !window.confirm(question)) return;
    if (btn.dataset.cgInflight === '1') return;
    btn.dataset.cgInflight = '1';
    busy(btn, true);

    var body = new FormData();
    body.append('__RequestVerificationToken', csrfToken());
    var navigating = false;
    request(btn.getAttribute('data-cg-action'), { method: 'POST', body: body })
      .then(function (result) { return handle(result, btn, btn.getAttribute('data-cg-regions')); })
      .then(function (redirecting) { navigating = redirecting; })
      .finally(function () {
        if (navigating) return;
        delete btn.dataset.cgInflight;
        if (document.contains(btn)) busy(btn, false);
      });
  });

  // --------------------------------------------------------------- Lists
  // <form method="get" data-cg-list="region"> holds a server-paged list's search and filters; links marked
  // data-cg-list-link (sort headers, pager, Clear) change sort or page. The URL is the list's state: every
  // change is written to it, so Back/Forward, Refresh and bookmarks show the same view.
  var LIST_DEBOUNCE_MS = 300;
  var listTimer = null;

  function listForm() { return document.querySelector('form[data-cg-list]'); }

  /** The form's choices as a query string: empty values and "__all" (= any) are left out. */
  function formQuery(form) {
    var params = new URLSearchParams();
    new FormData(form).forEach(function (value, key) {
      var v = String(value).trim();
      if (v && v !== '__all') params.append(key, v);
    });
    return params.toString();
  }

  /** Puts the URL's values back into the form (after Back/Forward or a Clear link). */
  function syncForm(form, url) {
    var params = new URL(url, location.href).searchParams;
    Array.prototype.forEach.call(form.elements, function (el) {
      if (!el.name || el.type === 'submit' || el.type === 'button') return;
      if (el.type === 'hidden') {
        // sort/dir are handled by loadList; any other hidden field carries a filter (for example the audit log's
        // student): follow the URL so a stale value is never sent with the next change.
        if (el.name !== 'sort' && el.name !== 'dir') el.value = params.get(el.name) || '';
        return;
      }
      var v = params.get(el.name);
      if (el.tagName === 'SELECT') el.value = v !== null && Array.prototype.some.call(el.options, function (o) { return o.value === v; }) ? v : (el.options[0] ? el.options[0].value : '');
      else if (el !== document.activeElement) el.value = v || '';
    });
  }

  /** Loads a list URL into the form's region; history: 'push', 'replace' or null (already there). */
  var listSeq = 0;

  function loadList(form, url, history) {
    var region = form.getAttribute('data-cg-list') || 'list';
    var target = new URL(url, location.href);
    var seq = ++listSeq;
    if (history === 'push' && target.href !== location.href) window.history.pushState({ cgList: true }, '', target.href);
    else if (history === 'replace') window.history.replaceState({ cgList: true }, '', target.href);
    return refresh(region, target.href).then(function () {
      if (seq !== listSeq) return;   // a newer load replaced this one (it was aborted)
      // Hidden sort/dir inputs live outside the region; keep them in step with the URL for the next filter change.
      var params = target.searchParams;
      ['sort', 'dir'].forEach(function (name) {
        var input = form.querySelector('input[type="hidden"][name="' + name + '"]');
        var value = params.get(name);
        if (value && !input) {
          input = document.createElement('input');
          input.type = 'hidden'; input.name = name;
          form.appendChild(input);
        }
        if (input) { if (value) input.value = value; else input.remove(); }
      });
      syncForm(form, target.href);
      // Tell screen readers the new count (the announcer sits outside the replaced region).
      var status = document.querySelector('[data-cg-list-status]');
      var count = document.querySelector('[data-cg-region="' + region + '"] [data-cg-count]');
      if (status) status.textContent = count ? count.textContent.trim() : '';
    });
  }

  function filterUrl(form) {
    var action = form.getAttribute('action') || location.pathname;
    var query = formQuery(form);
    return action + (query ? '?' + query : '');   // a new filter starts again at page 1
  }

  document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-cg-list')) return;
    e.preventDefault();
    clearTimeout(listTimer);
    loadList(form, filterUrl(form), 'push');
  });

  document.addEventListener('change', function (e) {
    var form = e.target.form;
    if (!form || !form.hasAttribute('data-cg-list') || e.target.tagName !== 'SELECT') return;
    clearTimeout(listTimer);
    loadList(form, filterUrl(form), 'push');
  });

  document.addEventListener('input', function (e) {
    var form = e.target.form;
    if (!form || !form.hasAttribute('data-cg-list') || e.target.tagName !== 'INPUT' || e.target.type === 'hidden') return;
    clearTimeout(listTimer);
    // Typing replaces the history entry instead of adding one per keystroke.
    listTimer = setTimeout(function () { loadList(form, filterUrl(form), 'replace'); }, LIST_DEBOUNCE_MS);
  });

  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('a[data-cg-list-link]');
    if (!link || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    var form = listForm();
    if (!form) return;
    e.preventDefault();
    clearTimeout(listTimer);
    if (link.hasAttribute('data-cg-reset') && form.contains(link)) {
      // Clear resets the filters but keeps the search typed so far. The link's own href was rendered with
      // the page and may predate that search, so the URL is built from the form instead.
      // Every filter control: selects back to "any", dates and other inputs emptied. The search box and the
      // hidden sort fields stay.
      Array.prototype.forEach.call(form.elements, function (el) {
        if (el.tagName === 'SELECT') el.selectedIndex = 0;
        else if (el.tagName === 'INPUT' && el.type !== 'hidden' && el.type !== 'submit' && el.type !== 'button' && el.name !== 'search') el.value = '';
      });
      loadList(form, filterUrl(form), 'push');
      return;
    }
    var paging = !!link.closest('[data-cg-pager]');
    loadList(form, link.href, 'push').then(function () {
      if (!paging) return;
      // Another page starts at its first row: bring the start of the table into view, not the pager.
      var region = document.querySelector('[data-cg-region="' + (form.getAttribute('data-cg-list') || 'list') + '"]');
      // Below the sticky top bar (the region's scroll-margin-top keeps the column headers clear of it).
      var bar = document.querySelector('.cg-topbar');
      var barBottom = bar ? bar.getBoundingClientRect().bottom : 0;
      if (region && region.getBoundingClientRect().top < barBottom) region.scrollIntoView({ block: 'start' });
    });
  });

  window.addEventListener('popstate', function () {
    var form = listForm();
    if (form) loadList(form, location.href, null);
  });

  cg.request = request;
  cg.refresh = refresh;
})();
