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

    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        SetPage("Students", "People");
        var url = string.IsNullOrWhiteSpace(search) ? "/api/students" : $"/api/students?search={Uri.EscapeDataString(search)}";
        var result = await Api.GetAsync<List<StudentListItemDto>>(url, ct);
        if (!result.Succeeded) ViewBag.LoadError = result.Error;

        var students = result.Value ?? new();
        ViewData["Search"] = search;
        // Grade options come from the delivered rows, so the filter can never offer
        // a grade that no student is actually in.
        ViewBag.Grades = students
            .Select(s => s.Grade)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return View(students);
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
            SetPage("Add student", "People");
            ModelState.AddModelError("", result.Error!);
            return View(request);
        }
        TempData["Success"] = $"{result.Value!.FullName} added. Assign an RFID card and a guardian to complete the record.";
        return RedirectToAction(nameof(Details), new { id = result.Value.StudentId });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!result.Succeeded) return NotFound();

        SetPage(result.Value!.FullName, "People");

        var guardians = await Api.GetAsync<List<GuardianListItemDto>>("/api/guardians", ct);
        var linkedIds = result.Value.Guardians.Select(g => g.GuardianId).ToHashSet();
        ViewBag.AvailableGuardians = (guardians.Value ?? new()).Where(g => !linkedIds.Contains(g.GuardianId)).ToList();

        return View(result.Value);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!result.Succeeded) return NotFound();
        SetPage($"Edit {result.Value!.FullName}", "People");
        ViewData["StudentId"] = id;
        ViewData["HasPhoto"] = result.Value.HasPhoto;
        ViewData["AddressForm"] = new AddressFormModel($"/Students/EditAddress/{id}", id, result.Value.Address,
            "Residential address", "Where the student lives. Shown on the profile and used for transport planning.");
        return View(new UpdateStudentRequest(result.Value.FullName, result.Value.DateOfBirth, result.Value.Grade));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAddress(int id, AddressDto address, CancellationToken ct)
    {
        var result = await Api.PutAsync<StudentDetailDto>($"/api/students/{id}/address", address, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Edit), null, new { id }, "address");
        }
        TempData["Success"] = "Address saved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateStudentRequest request, CancellationToken ct)
    {
        var result = await Api.PutAsync<StudentDetailDto>($"/api/students/{id}", request, ct);
        if (!result.Succeeded)
        {
            SetPage("Edit student", "People");
            ViewData["StudentId"] = id;
            ModelState.AddModelError("", result.Error!);
            return View(request);
        }
        TempData["Success"] = "Student details updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RecordStatus status, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>($"/api/students/{id}/status", new UpdateStudentStatusRequest(status), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Student record {(status == RecordStatus.Active ? "activated" : "deactivated")}."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignGuardian(int id, int guardianId, string? relationship, bool isAuthorizedForPickup, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>($"/api/students/{id}/guardians",
            new AssignGuardianRequest(guardianId, relationship, isAuthorizedForPickup), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Guardian linked to this student." : result.Error;
        return RedirectToAction(nameof(Details), null, new { id }, "guardians");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGuardianLink(int id, int guardianId, string? relationship, bool isAuthorizedForPickup, CancellationToken ct)
    {
        var result = await Api.PutAsync<object?>($"/api/students/{id}/guardians/{guardianId}",
            new UpdateStudentGuardianRequest(relationship, isAuthorizedForPickup), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Guardian link updated." : result.Error;
        return RedirectToAction(nameof(Details), null, new { id }, "guardians");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGuardianLink(int id, int guardianId, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/students/{id}/guardians/{guardianId}", ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Guardian unlinked. The guardian account itself was not deleted."
            : result.Error;
        return RedirectToAction(nameof(Details), null, new { id }, "guardians");
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
        {
            TempData["Error"] = "Select an image to upload.";
            return RedirectToAction(nameof(Details), null, new { id }, "photo");
        }

        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<PhotoStateDto>(
            $"/api/students/{id}/photo", stream, file.FileName, file.ContentType, ct);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Student photo saved." : result.Error;
        return RedirectToAction(nameof(Details), null, new { id }, "photo");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/students/{id}/photo", ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Student photo removed." : result.Error;
        return RedirectToAction(nameof(Details), null, new { id }, "photo");
    }
}
