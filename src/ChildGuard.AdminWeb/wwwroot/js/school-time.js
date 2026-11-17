// School Time page: fills the shared add/edit modal from the clicked date or row,
// and shows only the timing fields that apply to the chosen day type.
// The page is refreshed in place after a save (cg-ajax.js replaces the "school-time-page" region), so nothing
// here holds on to an element: every handler looks the form up when it runs.
(function () {
    'use strict';

    function form() { return document.querySelector('[data-st-form]'); }
    function field(f, name) { return f.querySelector('[data-st-field="' + name + '"]'); }

    function syncType() {
        var f = form();
        if (!f) return;
        var times = f.querySelector('[data-st-times]');
        var dismissalReq = f.querySelector('[data-st-dismissal-req]');
        var type = field(f, 'type').value;
        var holiday = type === 'Holiday';
        times.hidden = holiday;
        ['start', 'late', 'dismissal'].forEach(function (n) { field(f, n).disabled = holiday; });
        var needsDismissal = type === 'HalfDay';
        field(f, 'dismissal').required = needsDismissal;
        dismissalReq.hidden = !needsDismissal;
    }

    document.addEventListener('click', function (e) {
        var trigger = e.target.closest('[data-cg-modal-open="dayModal"]');
        var f = form();
        if (!trigger || !f) return;
        var d = trigger.dataset;
        var editing = !!d.stId;
        field(f, 'id').value = d.stId || '';
        field(f, 'date').value = d.stDate || '';
        field(f, 'type').value = d.stType || 'Holiday';
        field(f, 'title').value = d.stTitle || '';
        field(f, 'start').value = d.stStart || '';
        field(f, 'late').value = d.stLate || '';
        field(f, 'dismissal').value = d.stDismissal || '';
        var title = document.querySelector('[data-st-modal-title]');
        if (title) title.textContent = editing ? 'Edit calendar day' : 'Add calendar day';
        var sub = document.querySelector('[data-st-modal-sub]');
        if (sub) sub.textContent = editing ? 'Change this entry, or remove it from the Coming up list.' : 'Mark a holiday, or change one date\'s timings.';
        var submit = f.querySelector('[data-st-submit]');
        if (submit) submit.textContent = editing ? 'Save changes' : 'Add day';
        syncType();
    });

    document.addEventListener('change', function (e) {
        if (e.target.matches && e.target.matches('[data-st-field="type"]')) syncType();
    });

    // A refreshed page brings a fresh form: apply the day type's field rules to it.
    document.addEventListener('cg:regions-updated', syncType);
    syncType();
})();
