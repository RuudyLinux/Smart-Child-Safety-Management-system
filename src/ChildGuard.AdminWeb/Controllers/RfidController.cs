using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class RfidController : BaseAdminController
{
    public RfidController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "RFID Cards";
        var result = await Api.GetAsync<List<RfidCardListItemDto>>("/api/rfid/cards", ct);

        var students = await Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        ViewBag.UnassignedStudents = (students.Value ?? new()).Where(s => s.MaskedRfid is null).ToList();

        return View(result.Value ?? new());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(string cardUid, int studentId, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>("/api/rfid/cards", new AssignRfidRequest(cardUid, studentId), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "RFID card assigned." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RfidCardStatus status, CancellationToken ct)
    {
        await Api.PatchAsync<object?>($"/api/rfid/cards/{id}/status", new UpdateRfidStatusRequest(status), ct);
        return RedirectToAction(nameof(Index));
    }
}
