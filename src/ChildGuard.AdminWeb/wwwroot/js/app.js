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

  /** localStorage is unavailable in some privacy modes; a missing preference is not an error. */
  function readPref(key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } }
  function writePref(key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* ignore */ } }

  // ---------------------------------------------------------------- Sidebar
  function initSidebar() {
    var app = $('.cg-app');
    if (!app) return;

    var isMobile = function () { return window.matchMedia('(max-width: 860px)').matches; };

    if (readPref(STORE_COLLAPSED) === '1') app.classList.add('cg-app--collapsed');

    on($('[data-cg-sidebar-toggle]'), 'click', function () {
      if (isMobile()) {
        app.classList.toggle('cg-app--drawer');
      } else {
        var collapsed = app.classList.toggle('cg-app--collapsed');
        writePref(STORE_COLLAPSED, collapsed ? '1' : '0');
      }
    });

    on($('.cg-scrim'), 'click', function () { app.classList.remove('cg-app--drawer'); });

    // A tap on a nav link inside the drawer should close it, not leave it hanging open.
    $$('.cg-nav__link').forEach(function (link) {
      on(link, 'click', function () { if (isMobile()) app.classList.remove('cg-app--drawer'); });
    });

    on(document, 'keydown', function (e) {
      if (e.key === 'Escape') app.classList.remove('cg-app--drawer');
    });
  }

  // -------------------------------------------------------------- Dropdowns
  function initDropdowns() {
    function closeAll(except) {
      $$('.cg-dropdown.is-open').forEach(function (d) { if (d !== except) d.classList.remove('is-open'); });
    }

    $$('[data-cg-dropdown]').forEach(function (trigger) {
      var host = trigger.closest('.cg-dropdown');
      if (!host) return;
      on(trigger, 'click', function (e) {
        e.stopPropagation();
        var willOpen = !host.classList.contains('is-open');
        closeAll(host);
        host.classList.toggle('is-open', willOpen);
        trigger.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
      });
    });

    on(document, 'click', function () { closeAll(null); });
    on(document, 'keydown', function (e) { if (e.key === 'Escape') closeAll(null); });
    $$('.cg-dropdown__menu').forEach(function (menu) {
      on(menu, 'click', function (e) { e.stopPropagation(); });
    });
  }

  // ------------------------------------------------------------------- Tabs
  function initTabs() {
    $$('[data-cg-tabs]').forEach(function (group) {
      var tabs = $$('[data-cg-tab]', group);
      var scope = group.getAttribute('data-cg-tabs');
      var panels = $$('[data-cg-tabpanel][data-cg-tabs-scope="' + scope + '"]');

      function activate(name, pushHash) {
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
  function initServerToasts() {
    $$('[data-cg-toast]').forEach(function (node) {
      toasts.show(node.getAttribute('data-cg-toast-message') || node.textContent.trim(),
        node.getAttribute('data-cg-toast') || 'info',
        node.getAttribute('data-cg-toast-title') || null);
      node.remove();
    });
  }

  // ----------------------------------------------------------------- Modals
  function initModals() {
    function open(id) {
      var modal = document.getElementById(id);
      if (!modal) return;
      modal.hidden = false;
      document.body.style.overflow = 'hidden';
      var focusable = modal.querySelector('input, select, textarea, button');
      if (focusable) focusable.focus();
    }
    function close(modal) {
      if (!modal) return;
      modal.hidden = true;
      document.body.style.overflow = '';
    }

    $$('[data-cg-modal-open]').forEach(function (btn) {
      on(btn, 'click', function (e) {
        e.preventDefault();
        open(btn.getAttribute('data-cg-modal-open'));
      });
    });
    $$('[data-cg-modal-close]').forEach(function (btn) {
      on(btn, 'click', function () { close(btn.closest('.cg-modal')); });
    });
    $$('.cg-modal').forEach(function (modal) {
      // Click on the scrim, but not a click that started inside the panel.
      on(modal, 'mousedown', function (e) { if (e.target === modal) close(modal); });
    });
    on(document, 'keydown', function (e) {
      if (e.key !== 'Escape') return;
      $$('.cg-modal').forEach(function (m) { if (!m.hidden) close(m); });
    });
  }

  // ----------------------------------------------- Destructive confirmation
  function initConfirms() {
    $$('[data-cg-confirm]').forEach(function (form) {
      on(form, 'submit', function (e) {
        if (form.dataset.cgConfirmed === '1') return;
        e.preventDefault();
        if (window.confirm(form.getAttribute('data-cg-confirm'))) {
          form.dataset.cgConfirmed = '1';
          form.submit();
        }
      });
    });
  }

  // ------------------------------------------------------- Password reveal
  function initPasswordToggles() {
    $$('[data-cg-reveal]').forEach(function (btn) {
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
  function initSubmitStates() {
    $$('form[data-cg-busy]').forEach(function (form) {
      on(form, 'submit', function () {
        var btn = form.querySelector('button[type="submit"], .cg-btn--primary');
        if (!btn || btn.classList.contains('is-loading')) return;
        btn.classList.add('is-loading');
        var spinner = document.createElement('span');
        spinner.className = 'cg-btn__spinner';
        btn.insertBefore(spinner, btn.firstChild);
      });
    });
  }

  // ------------------------------------------------ Client-side table tools
  // Server-side data, browser-side refinement: filtering, sorting and paging
  // act on rows already delivered, so no request is made while the admin types.
  function initTables() {
    $$('[data-cg-table]').forEach(function (table) {
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
  function initDropzones() {
    $$('[data-cg-dropzone]').forEach(function (zone) {
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

      function reset() {
        input.value = '';
        if (preview) preview.hidden = true;
        if (submitBtn) submitBtn.disabled = true;
        if (errorEl) { errorEl.textContent = ''; errorEl.hidden = true; }
        zone.hidden = false;
      }

      function fail(message) {
        if (errorEl) { errorEl.textContent = message; errorEl.hidden = false; }
        else { toasts.show(message, 'danger'); }
        input.value = '';
        if (submitBtn) submitBtn.disabled = true;
      }

      function accept(file) {
        if (errorEl) { errorEl.textContent = ''; errorEl.hidden = true; }
        if (allowed.indexOf(file.type) === -1) {
          fail('Unsupported format. Choose a JPEG, PNG or WebP image.');
          return;
        }
        if (file.size > maxBytes) {
          fail('That image is ' + formatBytes(file.size) + '. The maximum is ' + formatBytes(maxBytes) + '.');
          return;
        }
        if (previewImg) {
          var reader = new FileReader();
          reader.onload = function (e) { previewImg.src = e.target.result; };
          reader.readAsDataURL(file);
        }
        if (nameEl) nameEl.textContent = file.name;
        if (sizeEl) sizeEl.textContent = formatBytes(file.size) + ' · ' + file.type.replace('image/', '').toUpperCase();
        if (preview) preview.hidden = false;
        zone.hidden = true;
        if (submitBtn) submitBtn.disabled = false;
      }

      on(input, 'change', function () { if (input.files && input.files[0]) accept(input.files[0]); });
      on(cancelBtn, 'click', reset);

      ['dragenter', 'dragover'].forEach(function (ev) {
        on(zone, ev, function (e) { e.preventDefault(); zone.classList.add('is-dragover'); });
      });
      ['dragleave', 'drop'].forEach(function (ev) {
        on(zone, ev, function (e) { e.preventDefault(); zone.classList.remove('is-dragover'); });
      });
      on(zone, 'drop', function (e) {
        var dt = e.dataTransfer;
        if (!dt || !dt.files || !dt.files.length) return;
        input.files = dt.files;
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
  function initRelativeTimes() {
    $$('[data-cg-ago]').forEach(function (el) {
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

  // ------------------------------------------------------- Avatar fallback
  // A broken image must never leave a torn icon on a child's record; the
  // initials underneath are revealed instead.
  function initAvatarFallbacks() {
    $$('.cg-avatar img').forEach(function (img) {
      on(img, 'error', function () { img.remove(); });
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    initSidebar();
    initDropdowns();
    initTabs();
    initServerToasts();
    initModals();
    initConfirms();
    initPasswordToggles();
    initSubmitStates();
    initTables();
    initDropzones();
    initGlobalSearch();
    initRelativeTimes();
    initAvatarFallbacks();
  });

  window.CgToast = toasts;
})();
