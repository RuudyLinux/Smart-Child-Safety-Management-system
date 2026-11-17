/* ==========================================================================
   ChildGuard Admin — shell behaviour
   Progressive enhancement only: every page works with this file absent.
   Forms post normally, links navigate, tables render server-side. What lives
   here is the polish — collapsing, sorting, filtering, previews, toasts.
   ========================================================================== */
(function () {
  'use strict';

  var STORE_COLLAPSED = 'cg.sidebar.collapsed';

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }
  function on(el, ev, fn) { if (el) el.addEventListener(ev, fn); }

  /**
   * Runs fn once per element matching sel inside root (root included). An element is marked when
   * bound, so re-running an initialiser over refreshed HTML (cg-ajax.js region swaps) never adds a
   * second listener to an element that already has one.
   */
  function each(root, sel, key, fn) {
    var list = $$(sel, root);
    if (root !== document && root.matches && root.matches(sel)) list.unshift(root);
    var mark = 'cgBound' + key;
    list.forEach(function (el) {
      if (el.dataset[mark]) return;
      el.dataset[mark] = '1';
      fn(el);
    });
  }

  /** localStorage is unavailable in some privacy modes; a missing preference is not an error. */
  function readPref(key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } }
  function writePref(key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* ignore */ } }

  // ------------------------------------------------------ Session expiry
  // The server answers a background request with 401 once the admin's session has
  // ended (its API token expired or was revoked). Send the browser to sign-in rather
  // than leaving the page showing empty or stale data.
  if (window.fetch && !/^\/Account\//i.test(location.pathname)) {
    var nativeFetch = window.fetch.bind(window);
    window.fetch = function () {
      return nativeFetch.apply(null, arguments).then(function (response) {
        var sameOrigin = !response.url || response.url.indexOf(location.origin + '/') === 0;
        if (response.status === 401 && sameOrigin) {
          location.href = '/Account/Login?expired=true';
        }
        return response;
      });
    };
  }

  // ---------------------------------------------------------------- Sidebar
  function initSidebar() {
    var app = $('.cg-app');
    if (!app) return;

    var isMobile = function () { return window.matchMedia('(max-width: 860px)').matches; };

    if (readPref(STORE_COLLAPSED) === '1') app.classList.add('cg-app--collapsed');

    var toggle = $('[data-cg-sidebar-toggle]');
    var syncExpanded = function () {
      if (!toggle) return;
      var open = isMobile() ? app.classList.contains('cg-app--drawer') : !app.classList.contains('cg-app--collapsed');
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    };
    var closeDrawer = function (restoreFocus) {
      if (!app.classList.contains('cg-app--drawer')) return;
      app.classList.remove('cg-app--drawer');
      syncExpanded();
      if (restoreFocus && toggle) toggle.focus();
    };
    syncExpanded();

    on(toggle, 'click', function () {
      if (isMobile()) {
        var opened = app.classList.toggle('cg-app--drawer');
        // Move focus into the drawer so keyboard users land in the menu they opened.
        if (opened) { var current = $('.cg-nav__link.is-active') || $('.cg-nav__link'); if (current) setTimeout(function () { current.focus(); }, 50); }
      } else {
        var collapsed = app.classList.toggle('cg-app--collapsed');
        writePref(STORE_COLLAPSED, collapsed ? '1' : '0');
      }
      syncExpanded();
    });

    on($('.cg-scrim'), 'click', function () { closeDrawer(false); });

    // A tap on a nav link inside the drawer should close it, not leave it hanging open.
    $$('.cg-nav__link').forEach(function (link) {
      on(link, 'click', function () { if (isMobile()) closeDrawer(false); });
    });

    on(document, 'keydown', function (e) {
      if (e.key === 'Escape') closeDrawer(true);
    });

    // Crossing the mobile breakpoint with the drawer open would leave the scrim up.
    on(window, 'resize', function () { if (!isMobile()) closeDrawer(false); syncExpanded(); });

    // Collapsed-rail tooltips are position: fixed (the scrolling nav would clip them),
    // so place each one at its item's vertical centre when it is about to show.
    var placeTip = function (item) {
      var link = $('.cg-nav__link', item);
      if (!link) return;
      var r = link.getBoundingClientRect();
      item.style.setProperty('--cg-tip-top', (r.top + r.height / 2) + 'px');
    };
    $$('.cg-nav__item').forEach(function (item) {
      on(item, 'mouseenter', function () { placeTip(item); });
      on(item, 'focusin', function () { placeTip(item); });
    });
  }

  // -------------------------------------------------------------- Dropdowns
  function closeAllDropdowns(except) {
    $$('.cg-dropdown.is-open').forEach(function (d) {
      if (d === except) return;
      d.classList.remove('is-open');
      var t = $('[data-cg-dropdown]', d);
      if (t) t.setAttribute('aria-expanded', 'false');
    });
  }

  /** Document-wide dropdown behaviour: bound once per page. */
  function initDropdownsGlobal() {
    on(document, 'click', function () { closeAllDropdowns(null); });
    on(document, 'keydown', function (e) {
      if (e.key !== 'Escape') return;
      var open = $('.cg-dropdown.is-open');
      closeAllDropdowns(null);
      // Return focus to the trigger if it was inside the menu, so Escape does not drop it.
      if (open && open.contains(document.activeElement)) { var t = $('[data-cg-dropdown]', open); if (t) t.focus(); }
    });
  }

  function initDropdowns(root) {
    each(root, '[data-cg-dropdown]', 'Dropdown', function (trigger) {
      var host = trigger.closest('.cg-dropdown');
      if (!host) return;
      on(trigger, 'click', function (e) {
        e.stopPropagation();
        var willOpen = !host.classList.contains('is-open');
        closeAllDropdowns(host);
        host.classList.toggle('is-open', willOpen);
        trigger.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
      });
    });
    each(root, '.cg-dropdown__menu', 'DropdownMenu', function (menu) {
      on(menu, 'click', function (e) { e.stopPropagation(); });
    });
  }

  // ------------------------------------------------------------------- Tabs
  function initTabs(root) {
    each(root, '[data-cg-tabs]', 'Tabs', function (group) {
      var tabs = $$('[data-cg-tab]', group);
      var scope = group.getAttribute('data-cg-tabs');

      function activate(name, pushHash) {
        // Looked up on every switch: a region refresh may have replaced a panel since binding.
        var panels = $$('[data-cg-tabpanel][data-cg-tabs-scope="' + scope + '"]');
        tabs.forEach(function (t) {
          var active = t.getAttribute('data-cg-tab') === name;
          t.classList.toggle('is-active', active);
          t.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        panels.forEach(function (p) {
          p.classList.toggle('is-active', p.getAttribute('data-cg-tabpanel') === name);
        });
        if (pushHash) history.replaceState(null, '', '#' + name);
      }

      tabs.forEach(function (t) {
        on(t, 'click', function () { activate(t.getAttribute('data-cg-tab'), true); });
      });

      // A deep link like /Students/Details/3#journey opens on that tab.
      var fromHash = window.location.hash.replace('#', '');
      var known = tabs.some(function (t) { return t.getAttribute('data-cg-tab') === fromHash; });
      if (known) activate(fromHash, false);
    });
  }

  // ----------------------------------------------------------------- Toasts
  var toasts = {
    host: null,
    ensure: function () {
      if (!this.host) {
        this.host = document.createElement('div');
        this.host.className = 'cg-toasts';
        this.host.setAttribute('role', 'status');
        this.host.setAttribute('aria-live', 'polite');
        document.body.appendChild(this.host);
      }
      return this.host;
    },
    show: function (message, kind, title) {
      var host = this.ensure();
      var el = document.createElement('div');
      el.className = 'cg-toast cg-toast--' + (kind || 'info');

      var icon = document.createElement('span');
      icon.className = 'cg-toast__icon';
      icon.innerHTML = iconSvg(kind === 'success' ? 'check-circle' : kind === 'danger' ? 'alert-circle' : 'info');

      var body = document.createElement('div');
      body.className = 'cg-toast__body';
      if (title) {
        var t = document.createElement('div');
        t.className = 'cg-toast__title';
        t.textContent = title;
        body.appendChild(t);
      }
      var m = document.createElement('div');
      m.className = 'cg-toast__msg';
      m.textContent = message;
      body.appendChild(m);

      var close = document.createElement('button');
      close.type = 'button';
      close.className = 'cg-toast__close';
      close.setAttribute('aria-label', 'Dismiss');
      close.innerHTML = iconSvg('x');

      function dismiss() {
        el.classList.add('is-leaving');
        setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 200);
      }
      on(close, 'click', dismiss);

      el.appendChild(icon);
      el.appendChild(body);
      el.appendChild(close);
      host.appendChild(el);
      setTimeout(dismiss, 6000);
    }
  };

  function iconSvg(name) {
    var paths = {
      'check-circle': '<path d="M22 11.1V12a10 10 0 1 1-5.9-9.1"/><path d="m9 11 3 3L22 4"/>',
      'alert-circle': '<circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/>',
      'info': '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>',
      'x': '<path d="M18 6 6 18M6 6l12 12"/>'
    };
    return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
      'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
      (paths[name] || paths.info) + '</svg>';
  }

  /** Server-rendered TempData messages surface as toasts on first paint. */
  /** Re-applies each tab group's active tab to its panels (after a region refresh replaced some). */
  function syncTabPanels() {
    $$('[data-cg-tabs]').forEach(function (group) {
      var active = $('[data-cg-tab].is-active', group);
      if (!active) return;
      var name = active.getAttribute('data-cg-tab');
      $$('[data-cg-tabpanel][data-cg-tabs-scope="' + group.getAttribute('data-cg-tabs') + '"]').forEach(function (p) {
        p.classList.toggle('is-active', p.getAttribute('data-cg-tabpanel') === name);
      });
    });
  }

  function initServerToasts(root) {
    $$('[data-cg-toast]', root).forEach(function (node) {
      toasts.show(node.getAttribute('data-cg-toast-message') || node.textContent.trim(),
        node.getAttribute('data-cg-toast') || 'info',
        node.getAttribute('data-cg-toast-title') || null);
      node.remove();
    });
  }

  // ----------------------------------------------------------------- Modals
  var MODAL_FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
  var modalOpener = null;

  function openModal(id) {
    var modal = document.getElementById(id);
    if (!modal) return;
    modalOpener = document.activeElement;
    modal.hidden = false;
    document.body.style.overflow = 'hidden';
    // Prefer the first form field over the header's close button.
    var focusable = modal.querySelector('input:not([type="hidden"]), select, textarea') || modal.querySelector(MODAL_FOCUSABLE);
    if (focusable) focusable.focus();
  }

  function closeModal(modal) {
    if (!modal || modal.hidden) return;
    modal.hidden = true;
    document.body.style.overflow = '';
    if (modalOpener && document.contains(modalOpener)) modalOpener.focus();
    modalOpener = null;
  }

  /** Document-wide modal keyboard handling: bound once per page. */
  function initModalsGlobal() {
    var FOCUSABLE = MODAL_FOCUSABLE;
    // Keep Tab inside an open modal; otherwise focus walks into the page behind it.
    on(document, 'keydown', function (e) {
      if (e.key !== 'Tab') return;
      var modal = $$('.cg-modal').filter(function (m) { return !m.hidden; })[0];
      if (!modal) return;
      var items = $$(FOCUSABLE, modal).filter(function (el) { return el.offsetParent !== null; });
      if (!items.length) return;
      var first = items[0], last = items[items.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      else if (!modal.contains(document.activeElement)) { e.preventDefault(); first.focus(); }
    });

    on(document, 'keydown', function (e) {
      if (e.key !== 'Escape') return;
      $$('.cg-modal').forEach(function (m) { if (!m.hidden) closeModal(m); });
    });
  }

  function initModals(root) {
    each(root, '[data-cg-modal-open]', 'ModalOpen', function (btn) {
      on(btn, 'click', function (e) {
        e.preventDefault();
        openModal(btn.getAttribute('data-cg-modal-open'));
      });
    });
    each(root, '[data-cg-modal-close]', 'ModalClose', function (btn) {
      on(btn, 'click', function () { closeModal(btn.closest('.cg-modal')); });
    });
    each(root, '.cg-modal', 'Modal', function (modal) {
      // Click on the scrim, but not a click that started inside the panel.
      on(modal, 'mousedown', function (e) { if (e.target === modal) closeModal(modal); });
    });
  }

  // ----------------------------------------------- Destructive confirmation
  function initConfirms(root) {
    // Forms only: cg-ajax.js asks for buttons ([data-cg-action][data-cg-confirm]) itself.
    each(root, 'form[data-cg-confirm]', 'Confirm', function (form) {
      on(form, 'submit', function (e) {
        if (form.dataset.cgConfirmed === '1') return;
        e.preventDefault();
        if (window.confirm(form.getAttribute('data-cg-confirm'))) {
          form.dataset.cgConfirmed = '1';
          // requestSubmit fires the submit event again (busy state, validation);
          // submit() would bypass both. Deferred: browsers ignore requestSubmit
          // while this form's submit event is still being dispatched.
          setTimeout(function () {
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
          }, 0);
        }
      });
    });
  }

  // ------------------------------------------------------- Password reveal
  function initPasswordToggles(root) {
    each(root, '[data-cg-reveal]', 'Reveal', function (btn) {
      on(btn, 'click', function () {
        var input = document.getElementById(btn.getAttribute('data-cg-reveal'));
        if (!input) return;
        var showing = input.type === 'text';
        input.type = showing ? 'password' : 'text';
        btn.setAttribute('aria-label', showing ? 'Show password' : 'Hide password');
        btn.setAttribute('aria-pressed', showing ? 'false' : 'true');
      });
    });
  }

  // ------------------------------------------------- Submit loading states
  /** Shows or clears a button's loading state (spinner + is-loading). Shared with cg-ajax.js. */
  function setBusy(btn, busy) {
    if (!btn) return;
    if (busy) {
      if (btn.classList.contains('is-loading')) return;
      btn.classList.add('is-loading');
      var spinner = document.createElement('span');
      spinner.className = 'cg-btn__spinner';
      btn.insertBefore(spinner, btn.firstChild);
    } else {
      btn.classList.remove('is-loading');
      var s = btn.querySelector('.cg-btn__spinner');
      if (s) s.remove();
    }
  }

  function initSubmitStatesGlobal() {
    // Back/forward cache restores the page exactly as it was left, spinner included.
    on(window, 'pageshow', function (e) {
      if (!e.persisted) return;
      $$('.cg-btn.is-loading').forEach(function (btn) { setBusy(btn, false); });
      $$('form[data-cg-confirm]').forEach(function (form) { delete form.dataset.cgConfirmed; });
    });
  }

  function initSubmitStates(root) {
    // AJAX forms show their own busy state (cg-ajax.js); this is the full-page post.
    each(root, 'form[data-cg-busy]:not([data-cg-ajax]), form[data-cg-busy][data-cg-ajax="off"]', 'Busy', function (form) {
      on(form, 'submit', function (e) {
        // Decide after every submit handler has run: a declined confirmation cancels
        // the event, and a button left "loading" then could never be pressed again.
        setTimeout(function () {
          if (e.defaultPrevented) return;
          setBusy(form.querySelector('button[type="submit"], .cg-btn--primary'), true);
        }, 0);
      });
    });
  }

  // ------------------------------------------------ Client-side table tools
  // Server-side data, browser-side refinement: filtering, sorting and paging
  // act on rows already delivered, so no request is made while the admin types.
  function initTables(root) {
    each(root, '[data-cg-table]', 'Table', function (table) {
      var scope = table.getAttribute('data-cg-table');
      var tbody = table.querySelector('tbody');
      if (!tbody) return;

      var rows = $$('tr[data-row]', tbody);
      if (!rows.length) return;

      var controls = $$('[data-cg-filter][data-cg-filter-scope="' + scope + '"]');
      var searchBox = $('[data-cg-search][data-cg-search-scope="' + scope + '"]');
      var counter = $('[data-cg-count][data-cg-count-scope="' + scope + '"]');
      var emptyNote = $('[data-cg-noresults][data-cg-noresults-scope="' + scope + '"]');
      var pager = $('[data-cg-pager][data-cg-pager-scope="' + scope + '"]');
      var pageSize = parseInt(table.getAttribute('data-cg-page-size') || '0', 10);
      var page = 1;
      var matched = rows.slice();

      function matches(row) {
        var ok = true;
        controls.forEach(function (control) {
          var wanted = control.value;
          if (!wanted || wanted === '__all') return;
          var field = control.getAttribute('data-cg-filter');
          if ((row.getAttribute('data-' + field) || '') !== wanted) ok = false;
        });
        if (ok && searchBox && searchBox.value.trim()) {
          var needle = searchBox.value.trim().toLowerCase();
          ok = (row.getAttribute('data-search') || row.textContent).toLowerCase().indexOf(needle) !== -1;
        }
        return ok;
      }

      function renderPager() {
        if (!pager) return;
        pager.innerHTML = '';
        if (!pageSize) return;
        var pages = Math.max(1, Math.ceil(matched.length / pageSize));
        if (pages < 2) return;
        for (var i = 1; i <= pages; i++) {
          (function (n) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'cg-pagination__btn' + (n === page ? ' is-active' : '');
            btn.textContent = String(n);
            on(btn, 'click', function () { page = n; apply(true); });
            pager.appendChild(btn);
          })(i);
        }
      }

      function apply(keepPage) {
        if (!keepPage) page = 1;
        matched = rows.filter(matches);
        rows.forEach(function (r) { r.hidden = true; });

        var visible = pageSize ? matched.slice((page - 1) * pageSize, page * pageSize) : matched;
        visible.forEach(function (r) { r.hidden = false; });

        if (counter) counter.textContent = matched.length + (matched.length === 1 ? ' record' : ' records');
        if (emptyNote) emptyNote.hidden = matched.length !== 0;
        renderPager();
      }

      controls.forEach(function (c) { on(c, 'change', function () { apply(false); }); });
      if (searchBox) on(searchBox, 'input', function () { apply(false); });

      var resetBtn = $('[data-cg-reset][data-cg-reset-scope="' + scope + '"]');
      on(resetBtn, 'click', function () {
        controls.forEach(function (c) { c.value = '__all'; });
        if (searchBox) searchBox.value = '';
        apply(false);
      });

      // Sorting reorders the delivered rows; the comparison key is declared on
      // the cell so text like "2 h ago" still sorts by its real timestamp.
      $$('[data-cg-sort]', table).forEach(function (header) {
        on(header, 'click', function () {
          var key = header.getAttribute('data-cg-sort');
          var descending = header.classList.contains('is-asc');
          $$('[data-cg-sort]', table).forEach(function (h) { h.classList.remove('is-asc', 'is-desc'); });
          header.classList.add(descending ? 'is-desc' : 'is-asc');

          rows.sort(function (a, b) {
            var av = a.getAttribute('data-sort-' + key) || '';
            var bv = b.getAttribute('data-sort-' + key) || '';
            var an = parseFloat(av), bn = parseFloat(bv);
            var cmp = (!isNaN(an) && !isNaN(bn)) ? an - bn : av.localeCompare(bv, undefined, { numeric: true });
            return descending ? -cmp : cmp;
          });
          rows.forEach(function (r) { tbody.appendChild(r); });
          apply(true);
        });
      });

      apply(false);
    });
  }

  // ------------------------------------------------------- Photo dropzones
  // Uploads post as a normal multipart form. The browser-side checks here are a
  // courtesy that saves a round trip; the API validates the bytes regardless.
  // Cameras opened by any photo dropzone. One page-wide pagehide handler stops them all, so refreshing a
  // photo section never adds another window listener (or keeps a replaced section alive through one).
  var openCameraStreams = new Set();

  function initDropzonesGlobal() {
    on(window, 'pagehide', function () {
      openCameraStreams.forEach(function (s) { s.getTracks().forEach(function (t) { t.stop(); }); });
      openCameraStreams.clear();
    });
  }

  function initDropzones(root) {
    each(root, '[data-cg-dropzone]', 'Dropzone', function (zone) {
      var input = zone.querySelector('input[type="file"]');
      var form = zone.closest('form');
      if (!input || !form) return;

      var preview = $('[data-cg-preview]', form);
      var previewImg = preview ? preview.querySelector('img') : null;
      var nameEl = $('[data-cg-preview-name]', form);
      var sizeEl = $('[data-cg-preview-size]', form);
      var submitBtn = form.querySelector('[data-cg-upload-submit]');
      var cancelBtn = form.querySelector('[data-cg-upload-cancel]');
      var errorEl = $('[data-cg-upload-error]', form);
      var maxBytes = parseInt(zone.getAttribute('data-cg-max-bytes') || '4194304', 10);
      var allowed = ['image/jpeg', 'image/png', 'image/webp'];

      // Camera capture (optional panel in the same form).
      var camOpenRow = $('[data-cg-camera-open-row]', form);
      var camOpenBtn = $('[data-cg-camera-open]', form);
      var camPanel = $('[data-cg-camera]', form);
      var camBox = $('[data-cg-camera-box]', form);
      var camCaptureBtn = $('[data-cg-camera-capture]', form);
      var camCloseBtn = $('[data-cg-camera-close]', form);
      // Browsers expose the camera only on HTTPS or localhost; elsewhere the option stays hidden.
      var canUseCamera = !!(camPanel && navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
      var stream = null;

      function stopCamera() {
        if (stream) { stream.getTracks().forEach(function (t) { t.stop(); }); openCameraStreams.delete(stream); stream = null; }
        if (camPanel) camPanel.hidden = true;
        if (camBox) camBox.innerHTML = '';
        if (camCaptureBtn) camCaptureBtn.disabled = true;
      }

      function cameraHint(text) {
        if (!camBox) return;
        camBox.innerHTML = '';
        var hint = document.createElement('div');
        hint.className = 'cg-camera__hint';
        hint.textContent = text;
        camBox.appendChild(hint);
      }

      function openCamera() {
        if (errorEl) { errorEl.textContent = ''; errorEl.hidden = true; }
        zone.hidden = true;
        if (camOpenRow) camOpenRow.hidden = true;
        camPanel.hidden = false;
        cameraHint('Waiting for camera permission… allow it in the browser prompt.');
        navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 960 } } })
          .then(function (s) {
            if (camPanel.hidden) { s.getTracks().forEach(function (t) { t.stop(); }); return; } // closed while asking
            stream = s;
            openCameraStreams.add(s);
            var video = document.createElement('video');
            video.autoplay = true; video.playsInline = true; video.muted = true;
            // Capture is enabled only once a frame exists; before that the video has no size
            // and a click would silently capture nothing.
            on(video, 'loadeddata', function () { if (stream === s) camCaptureBtn.disabled = false; });
            video.srcObject = s;
            camBox.innerHTML = '';
            camBox.appendChild(video);
          })
          .catch(function (e) {
            var name = e && e.name;
            cameraHint(name === 'NotFoundError' || name === 'OverconstrainedError'
              ? 'No camera was found on this computer. Connect one, or upload a photo instead.'
              : name === 'NotReadableError'
                ? 'The camera is in use by another application. Close it, then try again.'
                : 'Camera permission was refused. Allow the camera for this site in the browser settings.');
          });
      }

      function capture() {
        var video = camBox && camBox.querySelector('video');
        if (!video || !video.videoWidth) return;
        var canvas = document.createElement('canvas');
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
        camCaptureBtn.disabled = true;
        canvas.toBlob(function (blob) {
          stopCamera();
          if (!blob) { reset(); fail('The camera image could not be captured. Try again.'); return; }
          var stamp = new Date().toISOString().slice(0, 19).replace(/[-:T]/g, '');
          accept(new File([blob], 'camera-' + stamp + '.jpg', { type: 'image/jpeg', lastModified: Date.now() }));
        }, 'image/jpeg', 0.9);
      }

      function reset() {
        stopCamera();
        input.value = '';
        if (preview) preview.hidden = true;
        if (submitBtn) submitBtn.disabled = true;
        if (errorEl) { errorEl.textContent = ''; errorEl.hidden = true; }
        zone.hidden = false;
        if (camOpenRow) camOpenRow.hidden = !canUseCamera;
      }

      function fail(message) {
        if (errorEl) { errorEl.textContent = message; errorEl.hidden = false; }
        else { toasts.show(message, 'danger'); }
        input.value = '';
        if (submitBtn) submitBtn.disabled = true;
        // A rejected camera capture leaves nothing on screen to retry from; bring the choices back.
        if (zone.hidden && (!preview || preview.hidden) && (!camPanel || camPanel.hidden)) {
          zone.hidden = false;
          if (camOpenRow) camOpenRow.hidden = !canUseCamera;
        }
      }

      // Phone photos (often 2–8 MB) are resized here before upload: the database stores
      // one row per photo and many MySQL servers refuse rows over 1 MB. A profile
      // picture never needs more than this, and pages load faster for it.
      var MAX_SIDE = 1024, RESIZE_OVER = 600 * 1024, MAX_SOURCE = 20 * 1024 * 1024;

      function optimise(file) {
        return new Promise(function (resolve) {
          if (!window.createImageBitmap || !window.DataTransfer) return resolve(file);
          createImageBitmap(file).then(function (bitmap) {
            var scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
            if (scale === 1 && file.size <= RESIZE_OVER) { bitmap.close && bitmap.close(); return resolve(file); }
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(bitmap.width * scale);
            canvas.height = Math.round(bitmap.height * scale);
            var ctx = canvas.getContext('2d');
            ctx.fillStyle = '#fff'; // JPEG has no transparency
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            bitmap.close && bitmap.close();
            canvas.toBlob(function (blob) {
              if (!blob || blob.size >= file.size) return resolve(file);
              var name = file.name.replace(/\.[^.]+$/, '') + '.jpg';
              resolve(new File([blob], name, { type: 'image/jpeg', lastModified: Date.now() }));
            }, 'image/jpeg', 0.85);
          }).catch(function () { resolve(file); });
        });
      }

      function accept(original) {
        if (errorEl) { errorEl.textContent = ''; errorEl.hidden = true; }
        if (allowed.indexOf(original.type) === -1) {
          fail('Unsupported format. Choose a JPEG, PNG or WebP image.');
          return;
        }
        if (original.size > MAX_SOURCE) {
          fail('That image is ' + formatBytes(original.size) + '. Choose one under ' + formatBytes(MAX_SOURCE) + '.');
          return;
        }
        if (submitBtn) submitBtn.disabled = true;
        optimise(original).then(function (file) {
          if (file.size > maxBytes) {
            fail('That image is ' + formatBytes(file.size) + '. The maximum is ' + formatBytes(maxBytes) + '.');
            return;
          }
          // The form submits whatever the input holds: the resized photo, or a dropped file.
          if (!input.files || input.files[0] !== file) {
            var dt = new DataTransfer();
            dt.items.add(file);
            input.files = dt.files;
          }
          show(file, original);
        });
      }

      function show(file, original) {
        if (previewImg) {
          var reader = new FileReader();
          reader.onload = function (e) { previewImg.src = e.target.result; };
          reader.readAsDataURL(file);
        }
        if (nameEl) nameEl.textContent = original.name;
        if (sizeEl) sizeEl.textContent = formatBytes(file.size) + ' · ' + file.type.replace('image/', '').toUpperCase() +
          (file !== original ? ' · resized from ' + formatBytes(original.size) : '');
        if (preview) preview.hidden = false;
        zone.hidden = true;
        if (camOpenRow) camOpenRow.hidden = true;
        if (submitBtn) submitBtn.disabled = false;
      }

      on(input, 'change', function () { if (input.files && input.files[0]) accept(input.files[0]); });
      on(cancelBtn, 'click', reset);
      on(camOpenBtn, 'click', function () { if (canUseCamera) openCamera(); });
      on(camCaptureBtn, 'click', capture);
      on(camCloseBtn, 'click', reset);
      on(form, 'submit', stopCamera);

      ['dragenter', 'dragover'].forEach(function (ev) {
        on(zone, ev, function (e) { e.preventDefault(); zone.classList.add('is-dragover'); });
      });
      ['dragleave', 'drop'].forEach(function (ev) {
        on(zone, ev, function (e) { e.preventDefault(); zone.classList.remove('is-dragover'); });
      });
      on(zone, 'drop', function (e) {
        var dt = e.dataTransfer;
        if (!dt || !dt.files || !dt.files.length) return;
        accept(dt.files[0]);
      });

      reset();
    });
  }

  function formatBytes(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(0) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }

  // --------------------------------------------------------- Global search
  // Searches the navigation index rendered by the layout, and offers a student
  // lookup as the always-available fallback. No speculative API calls.
  function initGlobalSearch() {
    var input = $('[data-cg-global-search]');
    var results = $('[data-cg-global-results]');
    if (!input || !results) return;

    var indexNode = $('#cgSearchIndex');
    var index = [];
    try { index = indexNode ? JSON.parse(indexNode.textContent) : []; } catch (e) { index = []; }

    function hide() { results.hidden = true; results.innerHTML = ''; }

    function render(query) {
      var q = query.trim().toLowerCase();
      if (!q) { hide(); return; }

      var hits = index.filter(function (item) {
        return item.title.toLowerCase().indexOf(q) !== -1 ||
          (item.keywords || '').toLowerCase().indexOf(q) !== -1;
      }).slice(0, 6);

      results.innerHTML = '';
      hits.forEach(function (hit) {
        var a = document.createElement('a');
        a.className = 'cg-search__result';
        a.href = hit.url;
        a.innerHTML = '<span>' + escapeHtml(hit.title) + '</span><span class="cg-search__result-sub">' +
          escapeHtml(hit.section || 'Page') + '</span>';
        results.appendChild(a);
      });

      var lookup = document.createElement('a');
      lookup.className = 'cg-search__result';
      lookup.href = '/Students?search=' + encodeURIComponent(query.trim());
      lookup.innerHTML = '<span>Search students for “' + escapeHtml(query.trim()) +
        '”</span><span class="cg-search__result-sub">Enter</span>';
      results.appendChild(lookup);

      results.hidden = false;
    }

    on(input, 'input', function () { render(input.value); });
    on(input, 'keydown', function (e) {
      if (e.key === 'Enter') {
        e.preventDefault();
        var first = results.querySelector('.cg-search__result');
        if (first) first.click();
      }
      if (e.key === 'Escape') { input.blur(); hide(); }
    });
    on(document, 'click', function (e) { if (!results.contains(e.target) && e.target !== input) hide(); });

    // "/" focuses search, unless the admin is already typing somewhere.
    on(document, 'keydown', function (e) {
      if (e.key !== '/' || e.metaKey || e.ctrlKey) return;
      var tag = (document.activeElement && document.activeElement.tagName) || '';
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
      e.preventDefault();
      input.focus();
    });
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  // ---------------------------------------------- Relative timestamp labels
  function initRelativeTimes(root) {
    $$('[data-cg-ago]', root).forEach(function (el) {
      var iso = el.getAttribute('data-cg-ago');
      var when = new Date(iso);
      if (isNaN(when.getTime())) return;
      el.textContent = relative(when);
      el.title = when.toLocaleString();
    });
  }

  function relative(date) {
    var seconds = Math.round((Date.now() - date.getTime()) / 1000);
    if (seconds < 45) return 'just now';
    if (seconds < 90) return 'a minute ago';
    var minutes = Math.round(seconds / 60);
    if (minutes < 60) return minutes + ' min ago';
    var hours = Math.round(minutes / 60);
    if (hours < 24) return hours + (hours === 1 ? ' hour ago' : ' hours ago');
    var days = Math.round(hours / 24);
    if (days < 7) return days + (days === 1 ? ' day ago' : ' days ago');
    return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }

  // --------------------------------------------- Scrollable table regions
  // A table wider than its card scrolls sideways. Keyboard users must be able to
  // reach that scroll (WCAG 2.1.1), so an overflowing wrapper becomes a labelled,
  // focusable region; it goes back to plain markup when it fits again.
  var scrollObserver = null;

  function initScrollRegions(root) {
    var wraps = [];
    each(root, '.cg-table-wrap', 'Scroll', function (w) { wraps.push(w); });
    if (!wraps.length) return;

    function label(wrap) {
      var card = wrap.closest('.cg-card');
      var title = card && card.querySelector('.cg-card__title');
      return (title && title.textContent.trim()) || 'Table';
    }

    function sync(wrap) {
      var overflows = wrap.scrollWidth > wrap.clientWidth + 1;
      if (overflows && !wrap.hasAttribute('tabindex')) {
        wrap.setAttribute('tabindex', '0');
        wrap.setAttribute('role', 'region');
        wrap.setAttribute('aria-label', label(wrap) + ' (scrolls sideways)');
      } else if (!overflows && wrap.getAttribute('role') === 'region') {
        wrap.removeAttribute('tabindex');
        wrap.removeAttribute('role');
        wrap.removeAttribute('aria-label');
      }
    }

    wraps.forEach(sync);
    if ('ResizeObserver' in window) {
      // One observer for the page; wrappers removed by a region refresh are simply garbage collected.
      scrollObserver = scrollObserver || new ResizeObserver(function (entries) { entries.forEach(function (e) { sync(e.target); }); });
      wraps.forEach(function (w) { scrollObserver.observe(w); });
    }
  }

  // ------------------------------------------------------- Avatar fallback
  // A broken image must never leave a torn icon on a child's record; the
  // initials underneath are revealed instead.
  function initAvatarFallbacks(root) {
    each(root, '.cg-avatar img', 'Avatar', function (img) {
      on(img, 'error', function () { img.remove(); });
    });
  }

  // ------------------------------------------------------------- Motion
  // Purely presentational. Every value on the page is the server's value; the
  // count-up only animates towards it, and is skipped when html.cg-motion is
  // absent (reduced motion, or automation such as the E2E suite).
  var motionOn = document.documentElement.classList.contains('cg-motion');

  function initCountUps(root) {
    if (!motionOn || !('IntersectionObserver' in window)) return;
    var targets = [];
    each(root, '.cg-kpi__value, .cg-hero__stat-value, .cg-donut__center-value, .cg-kpi__breakdown-value', 'CountUp',
      function (el) { targets.push(el); });
    if (!targets.length) return;
    var numberRe = /^(\d{1,3}(?:,\d{3})*|\d+)(\.\d+)?$/;

    function firstNumberNode(el) {
      for (var i = 0; i < el.childNodes.length; i++) {
        var node = el.childNodes[i];
        if (node.nodeType === 3) {
          var raw = node.nodeValue.trim().split(/\s+/)[0] || '';
          if (numberRe.test(raw)) return { node: node, raw: raw };
          if (node.nodeValue.trim()) return null; // leading text that is not a number
        }
      }
      return null;
    }

    function run(el) {
      var found = firstNumberNode(el);
      if (!found) return;
      var original = found.node.nodeValue;
      var decimals = (found.raw.split('.')[1] || '').length;
      var end = parseFloat(found.raw.replace(/,/g, ''));
      if (!isFinite(end) || end === 0) return;
      // Reserve the final width so neighbours never shift while it counts.
      el.style.minWidth = el.getBoundingClientRect().width + 'px';
      var start = performance.now();
      var duration = Math.min(1100, 500 + end * 4);
      function frame(now) {
        var p = Math.min(1, (now - start) / duration);
        var eased = 1 - Math.pow(1 - p, 3);
        var value = (end * eased).toFixed(decimals);
        if (found.raw.indexOf(',') !== -1) value = Number(value).toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
        found.node.nodeValue = original.replace(found.raw, value);
        if (p < 1) requestAnimationFrame(frame);
        else { found.node.nodeValue = original; el.style.minWidth = ''; }
      }
      requestAnimationFrame(frame);
    }

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        io.unobserve(entry.target);
        run(entry.target);
      });
    }, { threshold: 0.4 });
    targets.forEach(function (el) { io.observe(el); });
  }

  // Soft light that follows the pointer across KPI cards.
  function initPointerSheen(root) {
    if (!motionOn || !window.matchMedia('(hover: hover)').matches) return;
    each(root, '.cg-kpi', 'Sheen', function (card) {
      on(card, 'pointermove', function (e) {
        var r = card.getBoundingClientRect();
        card.style.setProperty('--mx', (e.clientX - r.left) + 'px');
        card.style.setProperty('--my', (e.clientY - r.top) + 'px');
      });
    });
  }

  // A brand line at the top while the next page loads, so a click always answers.
  function initNavProgress() {
    var root = document.documentElement;
    on(document, 'click', function (e) {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      var a = e.target.closest && e.target.closest('a[href]');
      if (!a || a.target === '_blank' || a.hasAttribute('download')) return;
      var url;
      try { url = new URL(a.href, location.href); } catch (err) { return; }
      if (url.origin !== location.origin) return;
      if (url.pathname === location.pathname && url.search === location.search) return; // hash or same page
      if (/export|download/i.test(url.pathname)) return; // file responses never unload the page
      start();
    });
    on(document, 'submit', function (e) {
      var form = e.target;
      // An AJAX form never unloads the page; it shows its own busy state.
      var ajax = form && form.hasAttribute('data-cg-ajax') && form.getAttribute('data-cg-ajax') !== 'off';
      if (e.defaultPrevented || !form || form.hasAttribute('data-cg-no-progress') || ajax) return;
      start();
    });
    var safety;
    function start() {
      root.classList.add('cg-navigating');
      clearTimeout(safety);
      safety = setTimeout(function () { root.classList.remove('cg-navigating'); }, 8000);
    }
    // Back/forward cache restores a page mid-"navigating"; clear it.
    on(window, 'pageshow', function () { root.classList.remove('cg-navigating'); });
  }

  // Prefetch internal pages on hover intent so the next click renders at once.
  // Account routes (sign-out is a GET) and file exports are never prefetched.
  function initPrefetch() {
    if (!motionOn || !HTMLScriptElement.supports || !HTMLScriptElement.supports('speculationrules')) return;
    if (!document.querySelector('.cg-app')) return;
    // Safari refuses prefetch over plain HTTP and logs an error per link; production is HTTPS.
    if (location.protocol !== 'https:') return;
    var rules = document.createElement('script');
    rules.type = 'speculationrules';
    rules.textContent = JSON.stringify({
      prefetch: [{
        where: { and: [
          { href_matches: '/*' },
          { not: { href_matches: '/Account/*' } },
          { not: { href_matches: '/Reports/Export*' } },
          { not: { href_matches: '/Diagnostics*' } },
          { not: { selector_matches: '[download], [data-cg-no-prefetch]' } }
        ] },
        eagerness: 'moderate'
      }]
    });
    document.head.appendChild(rules);
  }

  // Apply [autofocus] ourselves as soon as the page is ready. WebKit can run its own
  // autofocus late and pull focus back from a field the user already moved to, so
  // what they type lands in the wrong box. Once a field has focus, the browser skips it.
  function initAutofocus() {
    var target = document.querySelector('[autofocus]');
    if (target && (!document.activeElement || document.activeElement === document.body)) target.focus();
  }

  /**
   * Binds every widget found inside root. Safe to call again on HTML that replaced part of the page
   * (cg-ajax.js region refresh): elements already bound are skipped, document-level handlers are not
   * touched.
   */
  function initWidgets(root) {
    initCountUps(root);
    initPointerSheen(root);
    initDropdowns(root);
    initTabs(root);
    initServerToasts(root);
    initModals(root);
    initConfirms(root);
    initPasswordToggles(root);
    initSubmitStates(root);
    initTables(root);
    initDropzones(root);
    initRelativeTimes(root);
    initAvatarFallbacks(root);
    initScrollRegions(root);
  }

  document.addEventListener('DOMContentLoaded', function () {
    initAutofocus();
    initNavProgress();
    initPrefetch();
    initSidebar();
    initDropdownsGlobal();
    initModalsGlobal();
    initSubmitStatesGlobal();
    initDropzonesGlobal();
    initGlobalSearch();
    initWidgets(document);
  });

  window.CgToast = toasts;
  var cg = window.cg = window.cg || {};
  cg.init = initWidgets;
  cg.toast = function (message, kind, title) { toasts.show(message, kind, title); };
  cg.setBusy = setBusy;
  cg.openModal = openModal;
  cg.closeModal = closeModal;
  cg.syncTabPanels = syncTabPanels;
})();
