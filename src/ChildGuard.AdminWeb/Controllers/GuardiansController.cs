using ChildGuard.AdminWeb.Models.ViewModels;
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

    /// <summary>
    /// The guardian list, paged, filtered and sorted by the API. The URL holds the whole view, so links, Back and
    /// Refresh restore it; cg-ajax.js swaps only the "list" region when the admin changes it.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Guardians", "People");
        var query = new ListQuery(ListDefinitions.Guardians, Request.Query);
        var rowsTask = Api.GetPagedAsync<GuardianListItemDto>("/api/guardians?" + query.ToApiQuery(), ct);

        // A list change re-renders only the list region: the header figures are not part of it.
        var summaryTask = ListRegionOnly ? null : Api.GetAsync<GuardianListSummaryDto>("/api/guardians/summary", ct);
        var rows = await rowsTask;
        if (!rows.Succeeded) ViewBag.LoadError = rows.Error;
        if (summaryTask is not null) ViewBag.Summary = (await summaryTask).Value;

        ViewData["Query"] = query;
        var page = rows.Value ?? new ChildGuard.Application.Common.Paged<GuardianListItemDto>(Array.Empty<GuardianListItemDto>(), 0);
        return View(new ListPageViewModel<GuardianListItemDto>(page.Items, page.Total, query.CurrentPage, query.PageSize));
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
        if (!result.Succeeded) return RefuseForm(result) ?? View(request);

        var url = DetailsUrl(result.Value!.GuardianId);
        return result.Value.CredentialEmailSent
            ? Done("Guardian account created. Login credentials were sent to the guardian's registered email address.", url)
            : DoneWithWarning($"Guardian account created, but the credential email could not be delivered: {result.Value.EmailError}", url);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();
        SetPage(result.Value!.FullName, "People");
        var check = await Api.GetAsync<DeletionCheckDto>($"/api/guardians/{id}/deletion-check", ct);
        ViewBag.Lifecycle = new RecordLifecycleModel("Guardians", id, result.Value.FullName, "guardian",
            result.Value.ArchivedAt, result.Value.ArchiveReason, check.Value);
        return View(result.Value);
    }

    private static string DetailsUrl(int id, string? tab = null) => $"/Guardians/Details/{id}" + (tab is null ? "" : "#" + tab);

    // Page parts on Details that each kind of change affects (data-cg-region names in Details.cshtml).
    private static readonly string[] RecordRegions =
        { "guardian-header", "archived-banner", "lifecycle", "account-card", "children", "children-count" };
    private static readonly string[] PhotoRegions = { "photo", "guardian-header" };

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, string? reason, CancellationToken ct)
    {
        var result = await Api.PostAsync<LifecycleResultDto>($"/api/guardians/{id}/archive", new ArchiveRecordRequest(reason ?? ""), ct);
        return FromApi(result, result.Succeeded ? Message(result.Value!) : "", DetailsUrl(id), RecordRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, string? reason, CancellationToken ct)
    {
        var result = await Api.PostAsync<LifecycleResultDto>($"/api/guardians/{id}/restore", new RestoreRecordRequest(reason), ct);
        return FromApi(result, result.Succeeded ? Message(result.Value!) : "", DetailsUrl(id), RecordRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePermanently(int id, string? reason, bool confirmed, CancellationToken ct)
    {
        if (!confirmed)
            return Failed("Confirm that you understand the deletion cannot be undone.", 400, DetailsUrl(id));
        var result = await Api.DeleteAsync<LifecycleResultDto>($"/api/guardians/{id}", new DeleteRecordRequest(reason ?? ""), ct);
        // The record is gone: the browser leaves its page for the list.
        return FromApi(result, result.Succeeded ? result.Value!.Message : "", result.Succeeded ? "/Guardians" : DetailsUrl(id));
    }

    private static string Message(LifecycleResultDto r) => r.Effects.Count == 0 ? r.Message : $"{r.Message} {string.Join(" ", r.Effects)}";

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<GuardianDetailDto>($"/api/guardians/{id}", ct);
        if (!result.Succeeded) return NotFound();

        SetPage($"Edit {result.Value!.FullName}", "People");
        ViewData["GuardianId"] = id;
        ViewData["HasPhoto"] = result.Value.HasPhoto;
        // Versions the photo URL, so a replaced photo is never served from the browser's cache.
        ViewData["PhotoUpdatedAt"] = result.Value.PhotoUpdatedAt;
        ViewData["AddressForm"] = new AddressFormModel($"/Guardians/EditAddress/{id}", id, result.Value.Address,
            "Residential address", "Shown on the guardian's profile. Optional.");
        var g = result.Value;
        return View(new AdminUpdateGuardianRequest(g.FullName, g.Email, g.Phone ?? string.Empty, g.Status));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAddress(int id, AddressDto address, CancellationToken ct)
    {
        var result = await Api.PutAsync<object?>($"/api/guardians/{id}/address", address, ct);
        // A refused address stays on the Edit page with its message; a saved one returns to the profile, as before.
        if (!result.Succeeded) return FromApi(result, "", $"/Guardians/Edit/{id}#address");
        return Done("Address saved.", DetailsUrl(id));
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
            return RefuseForm(result) ?? View(request);
        }

        return Done("Guardian details updated.", DetailsUrl(id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RecordStatus status, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>($"/api/guardians/{id}/status", new UpdateGuardianStatusRequest(status), ct);
        return FromApi(result, $"Guardian account {(status == RecordStatus.Active ? "activated" : "deactivated")}.", DetailsUrl(id), RecordRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCredentials(int id, string newPassword, CancellationToken ct)
    {
        var result = await Api.PatchAsync<object?>(
            $"/api/guardians/{id}/credentials", new AdminUpdateGuardianCredentialsRequest(newPassword), ct);
        return FromApi(result, "Password issued. The guardian must change it at their next sign-in.",
            DetailsUrl(id, "account"), "account-card");
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
            return Failed("Select an image to upload.", 400, DetailsUrl(id, "photo"));

        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<PhotoStateDto>(
            $"/api/guardians/{id}/photo", stream, file.FileName, file.ContentType, ct);

        return FromApi(result, "Guardian photo saved.", DetailsUrl(id, "photo"), PhotoRegions);
    }

    /// <summary>
    /// Face ID registration from the guardian page's camera (in person, at school).
    /// Called by face-enrol.js; on success the page reloads and shows the toast.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoRequestLimit * 2)] // base64 JSON is ~4/3 of the image size
    public async Task<IActionResult> RegisterFace(int id, [FromBody] RegisterFaceRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>($"/api/guardians/{id}/face-registration", request, ct);
        if (!result.Succeeded) return StatusCode(result.StatusCode, new { message = result.Error });
        TempData["Success"] = "Face registered. This guardian can now be verified at the pickup desk.";
        return Json(new { registered = true });
    }

    /// <summary>The guardian's reference faces (metadata only), for the Face ID card.</summary>
    [HttpGet]
    public async Task<IActionResult> FaceReferences(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<GuardianFaceReferencesDto>($"/api/guardians/{id}/face-references", ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    /// <summary>Adds one more reference face from the guardian page (camera or gallery). Called by face-enrol.js.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoRequestLimit * 2)]
    public async Task<IActionResult> AddFaceReference(int id, [FromBody] AddFaceReferenceRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<AddFaceReferenceResultDto>($"/api/guardians/{id}/face-references", request, ct);
        if (!result.Succeeded) return StatusCode(result.StatusCode, new { message = result.Error });
        if (result.Value!.Added) TempData["Success"] = result.Value.Message;
        return Json(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFaceReference(int id, int referenceId, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/guardians/{id}/face-references/{referenceId}", ct);
        return result.Succeeded ? Json(new { removed = true }) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/guardians/{id}/photo", ct);
        return FromApi(result, "Guardian photo removed.", DetailsUrl(id, "photo"), PhotoRegions);
    }
}
