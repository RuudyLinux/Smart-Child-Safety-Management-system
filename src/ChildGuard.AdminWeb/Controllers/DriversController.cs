using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// A driver's profile photo, through the shared photo panel (which expects Photo, UploadPhoto and DeletePhoto
/// actions on one controller). The driver profile itself lives on the Transportation pages.
/// </summary>
public class DriversController : BaseAdminController
{
    private const long PhotoRequestLimit = 5L * 1024 * 1024;

    public DriversController(ApiClient api) : base(api) { }

    private static string ProfileUrl(int id) => $"/Transportation/Driver/{id}#photo";   // the driver page is one region: driver-page

    [HttpGet]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, NoStore = false)]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var result = await Api.GetBinaryAsync($"/api/transportation/drivers/{id}/photo", ct);
        if (!result.Succeeded) return NotFound();
        return File(result.Value.Data, result.Value.ContentType);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(PhotoRequestLimit)]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Failed("Select an image to upload.", 400, ProfileUrl(id));
        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<PhotoStateDto>($"/api/transportation/drivers/{id}/photo", stream, file.FileName, file.ContentType, ct);
        return FromApi(result, "Driver photo saved.", ProfileUrl(id), "driver-page");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/transportation/drivers/{id}/photo", ct);
        return FromApi(result, "Driver photo removed.", ProfileUrl(id), "driver-page");
    }
}
