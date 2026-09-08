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

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        ViewData["Title"] = "Guardian Details";
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();
        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RecordStatus status, CancellationToken ct)
    {
        await Api.PatchAsync<object?>($"/api/guardians/{id}/status", new UpdateGuardianStatusRequest(status), ct);
        return RedirectToAction(nameof(Details), new { id });
    }
}
