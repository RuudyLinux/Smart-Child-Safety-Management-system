/* Settings > Database: reset always downloads a full backup first, and only resets once that download succeeded. */
(function () {
  'use strict';

  var form = document.querySelector('[data-db-reset-form]');
  if (!form) return;

  var confirmInput = form.querySelector('[data-db-confirm]');
  var demo = form.querySelector('[data-db-demo]');
  var submit = form.querySelector('[data-db-reset-submit]');
  var label = submit.querySelector('.cg-btn__label');
  var errorBox = form.querySelector('[data-db-reset-error]');
  var status = form.querySelector('[data-db-reset-status]');
  var busy = false;

  function csrf() {
    var meta = document.querySelector('meta[name="csrf-token"]');
    return meta ? meta.getAttribute('content') : '';
  }

  function refresh() { submit.disabled = busy || confirmInput.value.trim() !== 'RESET'; }
  confirmInput.addEventListener('input', refresh);

  function fileName(resp) {
    var cd = resp.headers.get('Content-Disposition') || '';
    var m = /filename\*=UTF-8''([^;]+)/i.exec(cd) || /filename="?([^";]+)"?/i.exec(cd);
    return m ? decodeURIComponent(m[1]) : 'childguard-backup.json';
  }

  // Saves the backup through the browser's normal download, without asking.
  function downloadBackup() {
    return fetch('/Settings/ExportDatabase', { credentials: 'same-origin' }).then(function (resp) {
      if (!resp.ok) throw new Error('The backup could not be created (' + resp.status + '). Nothing was reset.');
      var name = fileName(resp);
      return resp.blob().then(function (blob) {
        if (!blob.size) throw new Error('The backup was empty. Nothing was reset.');
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = name;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
        return name;
      });
    });
  }

  function setBusy(on, text) {
    busy = on;
    submit.classList.toggle('is-loading', on);
    label.textContent = text;
    refresh();
  }

  form.addEventListener('submit', function (e) {
    e.preventDefault();
    if (busy || confirmInput.value.trim() !== 'RESET') return;
    errorBox.textContent = '';
    setBusy(true, 'Backing up…');
    status.textContent = 'Downloading a full backup…';

    downloadBackup().then(function (name) {
      status.textContent = 'Backup saved as ' + name + '. Resetting…';
      label.textContent = 'Resetting…';
      return fetch('/Settings/ResetDatabase', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'X-CSRF-TOKEN': csrf() },
        body: JSON.stringify({ confirmation: confirmInput.value.trim(), loadDemoData: demo.checked })
      }).then(function (resp) {
        return resp.json().catch(function () { return {}; }).then(function (data) {
          if (!resp.ok) throw new Error(data.message || 'The reset failed (' + resp.status + '). The backup is in your downloads.');
          window.location.href = '/Settings#database';
          window.location.reload();
        });
      });
    }).catch(function (err) {
      errorBox.textContent = err.message || 'Something went wrong. Nothing was reset.';
      status.textContent = '';
      setBusy(false, 'Back up and reset');
    });
  });
})();
