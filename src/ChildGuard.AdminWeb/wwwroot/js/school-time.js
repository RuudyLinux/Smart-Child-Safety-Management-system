// School Time page: fills the shared add/edit modal from the clicked date or row,
// and shows only the timing fields that apply to the chosen day type.
(function () {
    'use strict';
    var form = document.querySelector('[data-st-form]');
    if (!form) return;
    var field = function (name) { return form.querySelector('[data-st-field="' + name + '"]'); };
    var title = document.querySelector('[data-st-modal-title]');
    var times = form.querySelector('[data-st-times]');
    var dismissalReq = form.querySelector('[data-st-dismissal-req]');

    function syncType() {
        var type = field('type').value;
        var holiday = type === 'Holiday';
        times.hidden = holiday;
        ['start', 'late', 'dismissal'].forEach(function (n) { field(n).disabled = holiday; });
        var needsDismissal = type === 'HalfDay';
        field('dismissal').required = needsDismissal;
        dismissalReq.hidden = !needsDismissal;
    }

    document.addEventListener('click', function (e) {
        var trigger = e.target.closest('[data-cg-modal-open="dayModal"]');
        if (!trigger) return;
        var d = trigger.dataset;
        var editing = !!d.stId;
        field('id').value = d.stId || '';
        field('date').value = d.stDate || '';
        field('type').value = d.stType || 'Holiday';
        field('title').value = d.stTitle || '';
        field('start').value = d.stStart || '';
        field('late').value = d.stLate || '';
        field('dismissal').value = d.stDismissal || '';
        title.textContent = editing ? 'Edit calendar day' : 'Add calendar day';
        var sub = document.querySelector('[data-st-modal-sub]');
        if (sub) sub.textContent = editing ? 'Change this entry, or remove it from the Coming up list.' : 'Mark a holiday, or change one date\'s timings.';
        var submit = form.querySelector('[data-st-submit]');
        if (submit) submit.textContent = editing ? 'Save changes' : 'Add day';
        syncType();
    });

    field('type').addEventListener('change', syncType);
    syncType();
})();
