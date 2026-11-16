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
        SetPage("RFID Cards", "Transport");

        var cards = await Api.GetAsync<List<RfidCardListItemDto>>("/api/rfid/cards", ct);
        if (!cards.Succeeded) ViewBag.LoadError = cards.Error;

        var students = await Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        var devices = await Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);

        ViewBag.UnassignedStudents = (students.Value ?? new()).Where(s => s.MaskedRfid is null).ToList();
        ViewBag.Devices = devices.Value ?? new();
        return View(cards.Value ?? new());
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var card = await Api.GetAsync<RfidCardDetailDto>($"/api/rfid/cards/{id}", ct);
        if (!card.Succeeded) return NotFound();
        SetPage($"Card {card.Value!.MaskedCardUid}", "Transport");

        ViewBag.RecentJourney = card.Value.StudentId is not null
            ? (await Api.GetAsync<List<JourneyEventDto>>($"/api/journey/{card.Value.StudentId}", ct)).Value?.Take(10).ToList()
              ?? new List<JourneyEventDto>()
            : new List<JourneyEventDto>();
        // Students who may receive a card: active, not archived, holding none.
        var students = await Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        ViewBag.EligibleStudents = (students.Value ?? new())
            .Where(s => s.MaskedRfid is null && s.Status == RecordStatus.Active && s.ArchivedAt is null)
            .OrderBy(s => s.FullName).ToList();

        return View(card.Value);
    }

    private IActionResult Back(ApiResult<RfidOperationResultDto> r, int id, bool toIndex = false)
    {
        TempData[r.Succeeded ? "Success" : "Error"] = r.Succeeded ? r.Value!.Message : r.Error;
        if (r.Succeeded && toIndex) return RedirectToAction(nameof(Index));
        return RedirectToAction(nameof(Details), new { id = r.Succeeded ? r.Value!.RfidCardId : id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(string? cardUid, CancellationToken ct)
    {
        var r = await Api.PostAsync<RfidOperationResultDto>("/api/rfid/cards/register", new RegisterRfidCardRequest(cardUid ?? ""), ct);
        TempData[r.Succeeded ? "Success" : "Error"] = r.Succeeded ? r.Value!.Message : r.Error;
        return r.Succeeded ? RedirectToAction(nameof(Details), new { id = r.Value!.RfidCardId }) : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRegistered(int id, int studentId, CancellationToken ct) =>
        Back(await Api.PostAsync<RfidOperationResultDto>($"/api/rfid/cards/{id}/assign", new AssignRegisteredCardRequest(studentId), ct), id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int id, string? reason, RfidCardStatus? newStatus, CancellationToken ct) =>
        Back(await Api.PostAsync<RfidOperationResultDto>($"/api/rfid/cards/{id}/unassign", new UnassignRfidRequest(reason ?? "", newStatus), ct), id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Replace(int id, string? newCardUid, RfidCardStatus oldCardStatus, string? reason, CancellationToken ct) =>
        Back(await Api.PostAsync<RfidOperationResultDto>($"/api/rfid/cards/{id}/replace", new ReplaceRfidRequest(newCardUid ?? "", oldCardStatus, reason ?? ""), ct), id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(int id, int studentId, string? reason, CancellationToken ct) =>
        Back(await Api.PostAsync<RfidOperationResultDto>($"/api/rfid/cards/{id}/reassign", new ReassignRfidRequest(studentId, reason ?? ""), ct), id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCard(int id, string? reason, bool confirmed, CancellationToken ct)
    {
        if (!confirmed)
        {
            TempData["Error"] = "Confirm that you understand the deletion cannot be undone.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return Back(await Api.DeleteAsync<RfidOperationResultDto>($"/api/rfid/cards/{id}", new DeleteRecordRequest(reason ?? ""), ct), id, toIndex: true);
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
        var result = await Api.PatchAsync<object?>($"/api/rfid/cards/{id}/status", new UpdateRfidStatusRequest(status), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Card marked {status}." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
