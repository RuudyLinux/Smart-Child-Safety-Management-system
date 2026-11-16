using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>A driver's profile photo, stored and validated like student and guardian photos.</summary>
[ApiController]
[Route("api/transportation/drivers/{id:int}/photo")]
[Authorize(Policy = "AdminOnly")]
public class DriverPhotosController : ControllerBase
{
    private readonly IPhotoService _photos;
    public DriverPhotosController(IPhotoService photos) => _photos = photos;

    [HttpGet]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await _photos.GetDriverPhotoAsync(id, ct);
        if (!result.Succeeded) return NotFound(new { message = result.Error });
        Response.Headers.CacheControl = "private, max-age=60";
        return File(result.Value!.Data, result.Value.ContentType);
    }

    [HttpPost]
    [RequestSizeLimit(PhotoUploads.RequestSizeLimit)]
    public async Task<IActionResult> Upload(int id, IFormFile? file, CancellationToken ct)
    {
        var read = await PhotoUploads.ReadAsync(file, ct);
        if (!read.Succeeded) return BadRequest(new { message = read.Error });
        var result = await _photos.SaveDriverPhotoAsync(id, read.Value!, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _photos.RemoveDriverPhotoAsync(id, ct);
        return result.Succeeded ? Ok(new { message = "Photo removed." }) : NotFound(new { message = result.Error });
    }
}
