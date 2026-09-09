using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class GuardiansController : BaseAdminController
{
    public GuardiansController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Guardians";
        var result = await Api.GetAsync<List<GuardianListItemDto>>("/api/guardians", ct);
        return View(result.Value ?? new());
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Add Guardian";
        return View(new CreateGuardianRequest(string.Empty, string.Empty, string.Empty));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGuardianRequest request, CancellationToken ct)
    {
        ViewData["Title"] = "Add Guardian";
        var result = await Api.PostAsync<CreateGuardianResultDto>("/api/guardians", request, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not create the guardian.");
            return View(request);
        }

        TempData[result.Value!.CredentialEmailSent ? "Success" : "Error"] = result.Value.CredentialEmailSent
            ? "Guardian account created successfully. Login credentials have been sent to the Guardian's registered email address."
            : $"Guardian account created, but the credential email could not be delivered: {result.Value.EmailError}";
        return RedirectToAction(nameof(Details), new { id = result.Value.GuardianId });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        ViewData["Title"] = "Guardian Details";
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();
        return View(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        ViewData["Title"] = "Edit Guardian";
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();

        ViewData["GuardianId"] = id;
        var g = result.Value!;
        return View(new AdminUpdateGuardianRequest(g.FullName, g.Email, g.Phone ?? string.Empty, g.Status));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminUpdateGuardianRequest request, CancellationToken ct)
    {
        var result = await Api.PutAsync<object?>($"/api/guardians/{id}", request, ct);
        if (!result.Succeeded)
        {
            ViewData["Title"] = "Edit Guardian";
            ViewData["GuardianId"] = id;
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not update the guardian.");
            return View(request);
        }

        TempData["Success"] = "Guardian details updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RecordStatus status, CancellationToken ct)
    {
        await Api.PatchAsync<object?>($"/api/guardians/{id}/status", new UpdateGuardianStatusRequest(status), ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCredentials(int id, string newPassword, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>(
            $"/api/guardians/{id}/credentials", new AdminUpdateGuardianCredentialsRequest(newPassword), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Password issued. The guardian must change it at next login."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
