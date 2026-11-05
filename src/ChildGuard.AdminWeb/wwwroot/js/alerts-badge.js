/* ==========================================================================
   Topbar safety-alert badge.
   Loaded after first paint and fed by a single AdminWeb endpoint, so the badge
   never blocks a page render and no page pays for a second dashboard query.
   Without JavaScript the bell is simply a link to the alert centre.
   ========================================================================== */
(function () {
  'use strict';

  var SEVERITY = {
    Critical: { cls: 'cg-badge--danger', label: 'Critical' },
    Warning: { cls: 'cg-badge--warning', label: 'Attention' },
    Info: { cls: 'cg-badge--info', label: 'Info' }
  };

  function el(sel) { return document.querySelector(sel); }

  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function render(payload) {
    var countEl = el('[data-cg-alert-count]');
    var summaryEl = el('[data-cg-alert-summary]');
    var listEl = el('[data-cg-alert-list]');
    if (!listEl) return;

    var alerts = payload.alerts || [];
    var critical = payload.critical || 0;

    if (countEl) {
      if (alerts.length) {
        countEl.textContent = alerts.length > 9 ? '9+' : String(alerts.length);
        countEl.hidden = false;
        countEl.style.background = critical > 0 ? '#DC2626' : '#F59E0B';
      } else {
        countEl.hidden = true;
      }
    }

    if (summaryEl) {
      summaryEl.textContent = alerts.length === 0
        ? 'All systems operational.'
        : critical + ' critical, ' + (alerts.length - critical) + ' to review.';
    }

    if (!alerts.length) {
      listEl.innerHTML = '<div class="cg-notif"><div class="cg-notif__detail">' +
        'Nothing needs attention right now.</div></div>';
      return;
    }

    listEl.innerHTML = alerts.slice(0, 5).map(function (a) {
      var sev = SEVERITY[a.severity] || SEVERITY.Info;
      var href = a.link || '/Dashboard#alerts';
      return '<a class="cg-notif" href="' + escapeHtml(href) + '">' +
        '<div style="flex:1;min-width:0">' +
          '<div class="cg-notif__title">' + escapeHtml(a.title) + '</div>' +
          '<div class="cg-notif__detail">' + escapeHtml(a.detail) + '</div>' +
        '</div>' +
        '<span class="cg-badge ' + sev.cls + '" style="align-self:flex-start">' + sev.label + '</span>' +
      '</a>';
    }).join('');
  }

  function load() {
    fetch('/Dashboard/AlertsSummary', { headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) { if (data) render(data); })
      .catch(function () {
        // The console must stay usable when the API is unreachable; a missing
        // badge is the correct degradation, not an error banner.
        var summaryEl = el('[data-cg-alert-summary]');
        if (summaryEl) summaryEl.textContent = 'Alert status unavailable right now.';
      });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', load);
  } else {
    load();
  }
})();
