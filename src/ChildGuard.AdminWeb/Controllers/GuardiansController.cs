using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class GuardiansController : BaseAdminController
{
    /// <summary>Matches the API's own cap, so an oversized file is refused before it is forwarded.</summary>
    private const long PhotoRequestLimit = 5L * 1024 * 1024;

    public GuardiansController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Guardians", "People");
        var result = await Api.GetAsync<List<GuardianListItemDto>>("/api/guardians", ct);
        if (!result.Succeeded) ViewBag.LoadError = result.Error;
        return View(result.Value ?? new());
    }

    [HttpGet]
    public IActionResult Create()
    {
        SetPage("Add guardian", "People");
        return View(new CreateGuardianRequest(string.Empty, string.Empty, string.Empty));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGuardianRequest request, CancellationToken ct)
    {
        SetPage("Add guardian", "People");
        var result = await Api.PostAsync<CreateGuardianResultDto>("/api/guardians", request, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not create the guardian.");
            return View(request);
        }

        TempData[result.Value!.CredentialEmailSent ? "Success" : "Error"] = result.Value.CredentialEmailSent
            ? "Guardian account created. Login credentials were sent to the guardian's registered email address."
            : $"Guardian account created, but the credential email could not be delivered: {result.Value.EmailError}";
        return RedirectToAction(nameof(Details), new { id = result.Value.GuardianId });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();
        SetPage(result.Value!.FullName, "People");
        return View(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();

        SetPage($"Edit {result.Value!.FullName}", "People");
        ViewData["GuardianId"] = id;
        ViewData["HasPhoto"] = result.Value.HasPhoto;
        var g = result.Value;
        return View(new AdminUpdateGuardianRequest(g.FullName, g.Email, g.Phone ?? string.Empty, g.Status));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminUpdateGuardianRequest request, CancellationToken ct)
    {
        var result = await Api.PutAsync<object?>($"/api/guardians/{id}", request, ct);
        if (!result.Succeeded)
        {
            SetPage("Edit guardian", "People");
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
        var result = await Api.PatchAsync<object?>($"/api/guardians/{id}/status", new UpdateGuardianStatusRequest(status), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Guardian account {(status == RecordStatus.Active ? "activated" : "deactivated")}."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCredentials(int id, string newPassword, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>(
            $"/api/guardians/{id}/credentials", new AdminUpdateGuardianCredentialsRequest(newPassword), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Password issued. The guardian must change it at their next sign-in."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---- Profile photo ----
    // The browser talks only to AdminWeb; AdminWeb holds the API bearer token and
    // re-serves the bytes. No storage path or API URL is ever exposed to the page.

    [HttpGet]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, NoStore = false)]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var result = await Api.GetBinaryAsync($"/api/guardians/{id}/photo", ct);
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
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<PhotoStateDto>(
            $"/api/guardians/{id}/photo", stream, file.FileName, file.ContentType, ct);

        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Guardian photo saved."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/guardians/{id}/photo", ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Guardian photo removed."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
