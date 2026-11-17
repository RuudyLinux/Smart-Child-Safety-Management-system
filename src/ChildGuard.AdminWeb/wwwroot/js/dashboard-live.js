/* Keeps the dashboard current without a page reload.

   The whole dashboard is one region (data-cg-region="dash-live"). Once a minute, while the tab is visible, it is
   re-rendered in place through the shared region refresh (cg.refresh), so the page keeps its scroll position and
   focus. A hidden tab does not poll; coming back to a stale one refreshes at once. After 30 minutes without any
   interaction the refreshing stops ("Paused — click to refresh") so a forgotten tab does not keep calling the
   server. The Refresh button refreshes now and resumes. */
(function () {
  'use strict';

  var bar = document.querySelector('[data-cg-live-bar]');
  var statusEl = document.querySelector('[data-cg-live-status]');
  var button = document.querySelector('[data-cg-live-refresh]');
  if (!bar || !statusEl || !button || !window.cg || !cg.refresh) return;

  var EVERY_MS = 60 * 1000;
  var IDLE_MS = 30 * 60 * 1000;

  var updatedAt = Date.now();
  var lastActivity = Date.now();
  var paused = false;
  var inFlight = false;
  var timer = null;

  function clock(ms) {
    var d = new Date(ms);
    return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
  }

  function show() {
    statusEl.textContent = paused ? 'Paused — click to refresh' : 'Updated ' + clock(updatedAt);
    bar.classList.toggle('is-paused', paused);
  }

  function refreshNow() {
    if (inFlight) return Promise.resolve();
    inFlight = true;
    button.disabled = true;
    return cg.refresh('dash-live').then(function () {
      updatedAt = Date.now();
    }, function () { /* the shared layer already reports a failed refresh; keep the old time */ }).then(function () {
      inFlight = false;
      button.disabled = false;
      show();
    });
  }

  function tick() {
    if (paused || document.hidden) return;
    if (Date.now() - lastActivity >= IDLE_MS) { paused = true; show(); return; }
    refreshNow();
  }

  function start() {
    if (timer === null) timer = setInterval(tick, EVERY_MS);
  }

  ['pointerdown', 'keydown', 'scroll', 'touchstart'].forEach(function (name) {
    window.addEventListener(name, function () { lastActivity = Date.now(); }, { passive: true });
  });

  document.addEventListener('visibilitychange', function () {
    if (document.hidden || paused) return;
    if (Date.now() - updatedAt >= EVERY_MS) refreshNow();
  });

  button.addEventListener('click', function () {
    paused = false;
    lastActivity = Date.now();
    start();
    refreshNow();
  });

  bar.hidden = false;
  show();
  start();
})();
