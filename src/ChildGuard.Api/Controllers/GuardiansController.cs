using ChildGuard.Api.Security;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/guardians")]
[Authorize(Policy = "AdminOnly")]
public class GuardiansController : ControllerBase
{
    private readonly IGuardianService _guardianService;
    private readonly IPhotoService _photos;
    private readonly IValidator<UpdateGuardianStatusRequest> _statusValidator;
    private readonly IValidator<AdminUpdateGuardianCredentialsRequest> _credentialsValidator;
    private readonly IValidator<CreateGuardianRequest> _createValidator;
    private readonly IValidator<AdminUpdateGuardianRequest> _updateValidator;
    private readonly IVerificationService _verification;
    private readonly IValidator<RegisterFaceRequest> _registerFaceValidator;
    private readonly IGuardianFaceReferenceService _faceReferences;
    private readonly IValidator<AddFaceReferenceRequest> _addReferenceValidator;

    public GuardiansController(
        IGuardianService guardianService,
        IPhotoService photos,
        IValidator<UpdateGuardianStatusRequest> statusValidator,
        IValidator<AdminUpdateGuardianCredentialsRequest> credentialsValidator,
        IValidator<CreateGuardianRequest> createValidator,
        IValidator<AdminUpdateGuardianRequest> updateValidator,
        IVerificationService verification,
        IValidator<RegisterFaceRequest> registerFaceValidator,
        IGuardianFaceReferenceService faceReferences,
        IValidator<AddFaceReferenceRequest> addReferenceValidator)
    {
        _faceReferences = faceReferences;
        _addReferenceValidator = addReferenceValidator;
        _guardianService = guardianService;
        _photos = photos;
        _statusValidator = statusValidator;
        _credentialsValidator = credentialsValidator;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _verification = verification;
        _registerFaceValidator = registerFaceValidator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _guardianService.GetAllAsync(ct));

    /// <summary>
    /// Creates a Guardian on the fixed initial password and emails the credentials
    /// to the Guardian's registered address. The password is never accepted from,
    /// or returned to, the client.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateGuardianRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var result = await _guardianService.CreateAsync(request, ct);
        if (!result.Succeeded) return Conflict(new { message = result.Error });

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.GuardianId }, result.Value);
    }

    /// <summary>Finds existing guardians by email, contact number or name, so one can be selected and linked. Links nothing.</summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string? q, CancellationToken ct) =>
        Ok(await _guardianService.LookupAsync(q ?? "", ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _guardianService.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    /// <summary>Admin edit of the Guardian's own details — name, email, contact number, status.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, AdminUpdateGuardianRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var result = await _guardianService.AdminUpdateAsync(id, request, ct);
        return result.Succeeded ? Ok(new { message = "Guardian updated." }) : BadRequest(new { message = result.Error });
    }

    /// <summary>Replaces the guardian's residential address. Every part blank clears it.</summary>
    [HttpPut("{id:int}/address")]
    public async Task<IActionResult> UpdateAddress(int id, AddressDto address, [FromServices] IValidator<AddressDto> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(address, ct);
        var result = await _guardianService.UpdateAddressAsync(id, address, ct);
        return result.Succeeded ? Ok(new { message = "Address updated." }) : NotFound(new { message = result.Error });
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateGuardianStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        var result = await _guardianService.UpdateStatusAsync(id, request, ct);
        return result.Succeeded ? Ok(new { message = "Status updated." }) : NotFound(new { message = result.Error });
    }

    [HttpPatch("{id:int}/credentials")]
    public async Task<IActionResult> UpdateCredentials(int id, AdminUpdateGuardianCredentialsRequest request, CancellationToken ct)
    {
        await _credentialsValidator.ValidateAndThrowAsync(request, ct);
        var result = await _guardianService.AdminUpdateCredentialsAsync(id, request, ct);
        return result.Succeeded
            ? Ok(new { message = "Password issued. The guardian must change it at next login." })
            : BadRequest(new { message = result.Error });
    }

    // ---- Profile photo ----
    // The image is addressed by guardian id only. No storage path is ever returned,
    // and every read is behind the same Admin authorization as the rest of this
    // controller, so a photo cannot be fetched by guessing a file name.

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id, CancellationToken ct)
    {
        var result = await _photos.GetGuardianPhotoAsync(id, ct);
        if (!result.Succeeded) return NotFound(new { message = result.Error });

        var photo = result.Value!;
        // Private: a guardian's face must not be cached by a shared proxy. The short
        // max-age still spares the admin's own browser a re-download per table row.
        Response.Headers.CacheControl = "private, max-age=60";
        return File(photo.Data, photo.ContentType);
    }

    [HttpPost("{id:int}/photo")]
    [RequestSizeLimit(PhotoUploads.RequestSizeLimit)]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile? file, CancellationToken ct)
    {
        var read = await PhotoUploads.ReadAsync(file, ct);
        if (!read.Succeeded) return BadRequest(new { message = read.Error });

        var result = await _photos.SaveGuardianPhotoAsync(id, read.Value!, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// In-person face registration by an admin (school desk camera), alongside the
    /// guardian's own registration from the app. Registers or replaces the stored
    /// face; only the embedding is kept, never the captured image.
    /// </summary>
    [HttpPost("{id:int}/face-registration")]
    public async Task<IActionResult> RegisterFace(int id, RegisterFaceRequest request, CancellationToken ct)
    {
        await _registerFaceValidator.ValidateAndThrowAsync(request, ct);
        var result = await _verification.RegisterFaceAsync(id, request, ct);
        if (result.Succeeded) return Ok(new { message = "Face registered." });
        return result.Error == "Guardian not found."
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    // ---- Reference faces ----
    // A guardian can have several reference faces (different light, angle, expression). Only metadata
    // is ever returned: no embedding leaves the server.

    [HttpGet("{id:int}/face-references")]
    public async Task<IActionResult> GetFaceReferences(int id, CancellationToken ct)
    {
        var result = await _faceReferences.ListAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost("{id:int}/face-references")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> AddFaceReference(int id, AddFaceReferenceRequest request, CancellationToken ct)
    {
        await _addReferenceValidator.ValidateAndThrowAsync(request, ct);
        var result = await _faceReferences.AddAsync(id, request, FaceActor.Admin(this.GetUserId()), ct);
        if (result.Succeeded) return Ok(result.Value);
        return result.Error == "Guardian not found."
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id:int}/face-references/{referenceId:int}")]
    public async Task<IActionResult> RemoveFaceReference(int id, int referenceId, CancellationToken ct)
    {
        var result = await _faceReferences.RemoveAsync(id, referenceId, FaceActor.Admin(this.GetUserId()), ct);
        return result.Succeeded ? Ok(new { message = "Reference photo removed." }) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id:int}/photo")]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await _photos.RemoveGuardianPhotoAsync(id, ct);
        return result.Succeeded ? Ok(new { message = "Photo removed." }) : NotFound(new { message = result.Error });
    }
}
