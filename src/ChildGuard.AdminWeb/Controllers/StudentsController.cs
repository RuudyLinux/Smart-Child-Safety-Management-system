using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class StudentsController : BaseAdminController
{
    public StudentsController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        ViewData["Title"] = "Students";
        var url = string.IsNullOrWhiteSpace(search) ? "/api/students" : $"/api/students?search={Uri.EscapeDataString(search)}";
        var result = await Api.GetAsync<List<StudentListItemDto>>(url, ct);
        ViewData["Search"] = search;
        return View(result.Value ?? new());
    }

    public IActionResult Create()
    {
        ViewData["Title"] = "Add Student";
        return View(new CreateStudentRequest("", DateTime.Today.AddYears(-8), null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateStudentRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<StudentDetailDto>("/api/students", request, ct);
        if (!result.Succeeded)
        {
            ViewData["Title"] = "Add Student";
            ModelState.AddModelError("", result.Error!);
            return View(request);
        }
        TempData["Success"] = "Student created.";
        return RedirectToAction(nameof(Details), new { id = result.Value!.StudentId });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        ViewData["Title"] = "Student Details";
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!result.Succeeded) return NotFound();

        var guardians = await Api.GetAsync<List<GuardianListItemDto>>("/api/guardians", ct);
        var linkedIds = result.Value!.Guardians.Select(g => g.GuardianId).ToHashSet();
        ViewBag.AvailableGuardians = (guardians.Value ?? new()).Where(g => !linkedIds.Contains(g.GuardianId)).ToList();

        return View(result.Value);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        ViewData["Title"] = "Edit Student";
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!result.Succeeded) return NotFound();
        ViewData["StudentId"] = id;
        return View(new UpdateStudentRequest(result.Value!.FullName, result.Value.DateOfBirth, result.Value.Grade));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateStudentRequest request, CancellationToken ct)
    {
        var result = await Api.PutAsync<StudentDetailDto>($"/api/students/{id}", request, ct);
        if (!result.Succeeded)
        {
            ViewData["Title"] = "Edit Student";
            ViewData["StudentId"] = id;
            ModelState.AddModelError("", result.Error!);
            return View(request);
        }
        TempData["Success"] = "Student updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RecordStatus status, CancellationToken ct)
    {
        await Api.PatchAsync<object?>($"/api/students/{id}/status", new UpdateStudentStatusRequest(status), ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignGuardian(int id, int guardianId, string? relationship, bool isAuthorizedForPickup, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>($"/api/students/{id}/guardians",
            new AssignGuardianRequest(guardianId, relationship, isAuthorizedForPickup), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Guardian associated." : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
