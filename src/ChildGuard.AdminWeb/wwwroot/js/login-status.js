// Sign-in page: while the hosted API (and its database) wake from sleep, say so instead of
// leaving the admin to discover it through a failed sign-in. Polling also keeps waking them.
(function () {
    const box = document.getElementById('cg-api-status');
    if (!box || !window.fetch) return;
    const url = box.dataset.url;
    const started = Date.now();

    async function check() {
        let ready = false;
        try {
            const res = await fetch(url, { cache: 'no-store', headers: { 'Accept': 'application/json' } });
            ready = res.ok && (await res.json()).ready === true;
        } catch { /* network blip: treat as not ready */ }

        if (ready) { box.hidden = true; return; }
        box.hidden = false;
        // Give up showing the banner after 3 minutes; the sign-in itself still waits and reports.
        if (Date.now() - started < 180000) setTimeout(check, 3000);
    }

    check();
})();
