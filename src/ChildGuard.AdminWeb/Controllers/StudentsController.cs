using ChildGuard.AdminWeb.Models.ViewModels;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class StudentsController : BaseAdminController
{
    private const long PhotoRequestLimit = 5L * 1024 * 1024;

    public StudentsController(ApiClient api) : base(api) { }

    /// <summary>
    /// The student list, paged, filtered and sorted by the API. The URL holds the whole view, so links, Back
    /// and Refresh restore it; cg-ajax.js swaps only the "list" region when the admin changes it.
    /// </summary>
    public async Task<IActionResult> Index([FromQuery] StudentListQuery query, CancellationToken ct)
    {
        SetPage("Students", "People");
        var rowsTask = Api.GetPagedAsync<StudentListItemDto>("/api/students?" + query.ToApiQuery(), ct);

        // A list change (typing, a filter, a page) re-renders only the list region: the header figures and grade
        // options are not part of it, so do not ask the API for them.
        if (AjaxRequest.IsAjax(Request) && Request.Headers["X-CG-Regions"].ToString() == "list")
        {
            var only = await rowsTask;
            if (!only.Succeeded) ViewBag.LoadError = only.Error;
            ViewData["Query"] = query;
            var rowsOnly = only.Value ?? new ChildGuard.Application.Common.Paged<StudentListItemDto>(Array.Empty<StudentListItemDto>(), 0);
            return View(new ListPageViewModel<StudentListItemDto>(rowsOnly.Items, rowsOnly.Total, query.CurrentPage, StudentListQuery.PageSize));
        }

        // Full page: also the grade options and the school-wide figures in the header (counts only: pageSize=1).
        var gradesTask = Api.GetAsync<List<string>>("/api/students/grades", ct);
        var onRollTask = Api.GetPagedAsync<StudentListItemDto>("/api/students?page=1&pageSize=1", ct);
        var presentTask = Api.GetPagedAsync<StudentListItemDto>("/api/students?page=1&pageSize=1&presentToday=true", ct);
        var carriedTask = Api.GetPagedAsync<StudentListItemDto>("/api/students?page=1&pageSize=1&hasRfid=true", ct);
        var unlinkedTask = Api.GetPagedAsync<StudentListItemDto>("/api/students?page=1&pageSize=1&hasGuardian=false", ct);
        await Task.WhenAll(rowsTask, gradesTask, onRollTask, presentTask, carriedTask, unlinkedTask);

        var rows = rowsTask.Result;
        if (!rows.Succeeded) ViewBag.LoadError = rows.Error;
        ViewBag.Grades = gradesTask.Result.Value ?? new List<string>();
        ViewBag.Figures = new StudentListFigures(
            onRollTask.Result.Value?.Total, presentTask.Result.Value?.Total,
            carriedTask.Result.Value?.Total, unlinkedTask.Result.Value?.Total);
        ViewData["Query"] = query;
        var page = rows.Value ?? new ChildGuard.Application.Common.Paged<StudentListItemDto>(Array.Empty<StudentListItemDto>(), 0);
        return View(new ListPageViewModel<StudentListItemDto>(page.Items, page.Total, query.CurrentPage, StudentListQuery.PageSize));
    }

    public IActionResult Create()
    {
        SetPage("Add student", "People");
        return View(new CreateStudentRequest("", DateTime.Today.AddYears(-8), null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateStudentRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<StudentDetailDto>("/api/students", request, ct);
        if (!result.Succeeded)
        {
            // Field errors from the API or from binding this form both belong beside their fields.
            var fieldErrors = AddApiErrors(result) | !ModelState.IsValid;
            // AJAX: field errors go beside their fields; any other refusal is reported as itself.
            if (AjaxRequest.IsAjax(Request) && !fieldErrors) return FromApi(result, "");
            if (!fieldErrors) ModelState.AddModelError("", result.Error!);
            SetPage("Add student", "People");
            return Invalid(ModelState) ?? View(request);
        }
        return Done($"{result.Value!.FullName} added. Assign an RFID card and a guardian to complete the record.",
            DetailsUrl(result.Value.StudentId));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!result.Succeeded) return NotFound();

        SetPage(result.Value!.FullName, "People");

        var guardians = await Api.GetAsync<List<GuardianListItemDto>>("/api/guardians", ct);
        var linkedIds = result.Value.Guardians.Select(g => g.GuardianId).ToHashSet();
        ViewBag.AvailableGuardians = (guardians.Value ?? new()).Where(g => !linkedIds.Contains(g.GuardianId)).ToList();
        var check = await Api.GetAsync<DeletionCheckDto>($"/api/students/{id}/deletion-check", ct);
        ViewBag.Lifecycle = new RecordLifecycleModel("Students", id, result.Value.FullName, "student",
            result.Value.ArchivedAt, result.Value.ArchiveReason, check.Value);

        return View(result.Value);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!result.Succeeded) return NotFound();
        SetPage($"Edit {result.Value!.FullName}", "People");
        ViewData["StudentId"] = id;
        ViewData["HasPhoto"] = result.Value.HasPhoto;
        // Versions the photo URL, so a replaced photo is never served from the browser's cache.
        ViewData["PhotoUpdatedAt"] = result.Value.PhotoUpdatedAt;
        ViewData["AddressForm"] = new AddressFormModel($"/Students/EditAddress/{id}", id, result.Value.Address,
            "Residential address", "Where the student lives. Shown on the profile and used for transport planning.");
        var s = result.Value;
        return View(new UpdateStudentRecordRequest(s.FullName, s.DateOfBirth, s.Grade, s.Section, s.AdmissionNumber, s.Gender));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, string? reason, CancellationToken ct)
    {
        var result = await Api.PostAsync<LifecycleResultDto>($"/api/students/{id}/archive", new ArchiveRecordRequest(reason ?? ""), ct);
        return FromApi(result, result.Succeeded ? Message(result.Value!) : "", DetailsUrl(id), RecordRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, string? reason, CancellationToken ct)
    {
        var result = await Api.PostAsync<LifecycleResultDto>($"/api/students/{id}/restore", new RestoreRecordRequest(reason), ct);
        return FromApi(result, result.Succeeded ? Message(result.Value!) : "", DetailsUrl(id), RecordRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePermanently(int id, string? reason, bool confirmed, CancellationToken ct)
    {
        if (!confirmed)
            return Failed("Confirm that you understand the deletion cannot be undone.", 400, DetailsUrl(id));
        var result = await Api.DeleteAsync<LifecycleResultDto>($"/api/students/{id}", new DeleteRecordRequest(reason ?? ""), ct);
        // The record is gone: the browser leaves its page for the list.
        return FromApi(result, result.Succeeded ? result.Value!.Message : "", result.Succeeded ? "/Students" : DetailsUrl(id));
    }

    private static string Message(LifecycleResultDto r) => r.Effects.Count == 0 ? r.Message : $"{r.Message} {string.Join(" ", r.Effects)}";

    private static string DetailsUrl(int id, string? tab = null) => $"/Students/Details/{id}" + (tab is null ? "" : "#" + tab);

    // Page parts on Details that each kind of change affects (data-cg-region names in Details.cshtml).
    // Archiving ends the bus assignment and can stop the card, so every part showing them refreshes too.
    private static readonly string[] RecordRegions =
        { "student-header", "archived-banner", "lifecycle", "overview-facts", "rfid-card", "transport", "transport-history" };
    private static readonly string[] GuardianRegions = { "guardians", "guardian-modal", "guardian-count", "student-header" };
    private static readonly string[] PhotoRegions = { "photo", "student-header" };

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAddress(int id, AddressDto address, CancellationToken ct)
    {
        var result = await Api.PutAsync<StudentDetailDto>($"/api/students/{id}/address", address, ct);
        // A refused address stays on the Edit page with its message; a saved one returns to the profile, as before.
        if (!result.Succeeded) return FromApi(result, "", $"/Students/Edit/{id}#address");
        return Done("Address saved.", DetailsUrl(id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateStudentRecordRequest request, CancellationToken ct)
    {
        var result = await Api.PutAsync<StudentDetailDto>($"/api/students/{id}/record", request, ct);
        if (!result.Succeeded)
        {
            // Field errors from the API or from binding this form both belong beside their fields.
            var fieldErrors = AddApiErrors(result) | !ModelState.IsValid;
            if (AjaxRequest.IsAjax(Request) && !fieldErrors) return FromApi(result, "");
            if (!fieldErrors) ModelState.AddModelError("", result.Error!);
            SetPage("Edit student", "People");
            ViewData["StudentId"] = id;
            return Invalid(ModelState) ?? View(request);
        }
        return Done("Student details updated.", DetailsUrl(id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RecordStatus status, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>($"/api/students/{id}/status", new UpdateStudentStatusRequest(status), ct);
        return FromApi(result, $"Student record {(status == RecordStatus.Active ? "activated" : "deactivated")}.", DetailsUrl(id), RecordRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignGuardian(int id, int guardianId, string? relationship, bool isAuthorizedForPickup, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>($"/api/students/{id}/guardians",
            new AssignGuardianRequest(guardianId, relationship, isAuthorizedForPickup), ct);
        return FromApi(result, "Guardian linked to this student.", DetailsUrl(id, "guardians"), GuardianRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGuardianLink(int id, int guardianId, string? relationship, bool isAuthorizedForPickup, CancellationToken ct)
    {
        var result = await Api.PutAsync<object?>($"/api/students/{id}/guardians/{guardianId}",
            new UpdateStudentGuardianRequest(relationship, isAuthorizedForPickup), ct);
        return FromApi(result, "Guardian link updated.", DetailsUrl(id, "guardians"), GuardianRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGuardianLink(int id, int guardianId, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/students/{id}/guardians/{guardianId}", ct);
        return FromApi(result, "Guardian unlinked. The guardian account itself was not deleted.", DetailsUrl(id, "guardians"), GuardianRegions);
    }

    // ---- Profile photo ----

    [HttpGet]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, NoStore = false)]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var result = await Api.GetBinaryAsync($"/api/students/{id}/photo", ct);
        if (!result.Succeeded) return NotFound();
        return File(result.Value.Data, result.Value.ContentType);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoRequestLimit)]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Failed("Select an image to upload.", 400, DetailsUrl(id, "photo"));

        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<PhotoStateDto>(
            $"/api/students/{id}/photo", stream, file.FileName, file.ContentType, ct);

        return FromApi(result, "Student photo saved.", DetailsUrl(id, "photo"), PhotoRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/students/{id}/photo", ct);
        return FromApi(result, "Student photo removed.", DetailsUrl(id, "photo"), PhotoRegions);
    }
}
