using ChildGuard.AdminWeb.Models.ViewModels;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class RfidController : BaseAdminController
{
    public RfidController(ApiClient api) : base(api) { }

    /// <summary>
    /// The card list, paged, filtered and sorted by the API with its state in the URL. A list change re-renders only
    /// the "list" region, so the header figures, student picker and scanners are not fetched for it.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("RFID Cards", "Transport");
        var query = new ListQuery(ListDefinitions.RfidCards, Request.Query);
        var rowsTask = Api.GetPagedAsync<RfidCardListItemDto>("/api/rfid/cards?" + query.ToApiQuery(), ct);
        var listOnly = ListRegionOnly;
        var summaryTask = listOnly ? null : Api.GetAsync<RfidCardSummaryDto>("/api/rfid/cards/summary", ct);
        var studentsTask = listOnly ? null : Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        var devicesTask = listOnly ? null : Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);
        await Task.WhenAll(new Task?[] { rowsTask, summaryTask, studentsTask, devicesTask }.Where(t => t is not null)!);

        var rows = rowsTask.Result;
        if (!rows.Succeeded) ViewBag.LoadError = rows.Error;
        ViewBag.Summary = summaryTask?.Result.Value;
        ViewBag.UnassignedStudents = (studentsTask?.Result.Value ?? new()).Where(s => s.MaskedRfid is null).ToList();
        ViewBag.Devices = devicesTask?.Result.Value ?? new();
        ViewData["Query"] = query;
        var page = rows.Value ?? new ChildGuard.Application.Common.Paged<RfidCardListItemDto>(Array.Empty<RfidCardListItemDto>(), 0);
        return View(new ListPageViewModel<RfidCardListItemDto>(page.Items, page.Total, query.CurrentPage, query.PageSize));
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

    private static string DetailsUrl(int id) => $"/Rfid/Details/{id}";

    // Each page is one region (rfid-console, rfid-card-page): a change reaches its counts, pickers and history together.
    private static readonly string[] Console = { "rfid-console" };
    private static readonly string[] CardPage = { "rfid-card-page" };

    /// <summary>
    /// A card action's outcome. Same card: the card page refreshes in place. A different card (a replacement), or a
    /// deleted one: the browser goes to that page, since the page it was on no longer describes it.
    /// </summary>
    private IActionResult Back(ApiResult<RfidOperationResultDto> r, int id, bool toIndex = false)
    {
        if (!r.Succeeded) return FromApi(r, "", DetailsUrl(id));
        if (toIndex) return Done(r.Value!.Message, "/Rfid");
        return r.Value!.RfidCardId == id
            ? Done(r.Value.Message, DetailsUrl(id), CardPage)
            : Done(r.Value.Message, DetailsUrl(r.Value.RfidCardId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(string? cardUid, CancellationToken ct)
    {
        var r = await Api.PostAsync<RfidOperationResultDto>("/api/rfid/cards/register", new RegisterRfidCardRequest(cardUid ?? ""), ct);
        // A registered card opens on its own page, as before.
        return r.Succeeded ? Done(r.Value!.Message, DetailsUrl(r.Value.RfidCardId)) : FromApi(r, "", "/Rfid");
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
    public async Task<IActionResult> DeleteCard(int id, string? reason, bool confirmed, CancellationToken ct)
    {
        if (!confirmed)
            return Failed("Confirm that you understand the deletion cannot be undone.", 400, DetailsUrl(id));
        return Back(await Api.DeleteAsync<RfidOperationResultDto>($"/api/rfid/cards/{id}", new DeleteRecordRequest(reason ?? ""), ct), id, toIndex: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(string cardUid, int studentId, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>("/api/rfid/cards", new AssignRfidRequest(cardUid, studentId), ct);
        return FromApi(result, "RFID card assigned.", "/Rfid", Console);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RfidCardStatus status, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>($"/api/rfid/cards/{id}/status", new UpdateRfidStatusRequest(status), ct);
        // From the list or from a card's page: whichever of the two regions the page shows refreshes in place.
        return FromApi(result, $"Card marked {status}.", "/Rfid", Console.Concat(CardPage).ToArray());
    }
}
