using ChildGuard.Application.Common;
using ChildGuard.Api.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/students")]
[Authorize(Policy = "AdminOnly")]
public class StudentsController : ControllerBase
{
    private readonly IStudentService _studentService;
    private readonly IPhotoService _photos;
    private readonly IValidator<CreateStudentRequest> _createValidator;
    private readonly IValidator<UpdateStudentRequest> _updateValidator;
    private readonly IValidator<AssignGuardianRequest> _assignGuardianValidator;
    private readonly IValidator<UpdateStudentStatusRequest> _statusValidator;
    private readonly IValidator<UpdateStudentGuardianRequest> _guardianLinkValidator;

    public StudentsController(
        IStudentService studentService,
        IPhotoService photos,
        IValidator<CreateStudentRequest> createValidator,
        IValidator<UpdateStudentRequest> updateValidator,
        IValidator<AssignGuardianRequest> assignGuardianValidator,
        IValidator<UpdateStudentStatusRequest> statusValidator,
        IValidator<UpdateStudentGuardianRequest> guardianLinkValidator)
    {
        _studentService = studentService;
        _photos = photos;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _assignGuardianValidator = assignGuardianValidator;
        _statusValidator = statusValidator;
        _guardianLinkValidator = guardianLinkValidator;
    }

    /// <summary>
    /// The student list. Without <c>page</c> the whole list, exactly as before (only <c>search</c> applies).
    /// With <c>page</c>: one page of the same rows, filtered and sorted in the database, total in X-Total-Count.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] string? grade, [FromQuery] string? status,
        [FromQuery] bool? hasRfid, [FromQuery] bool? hasGuardian, [FromQuery] bool? presentToday,
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? dir,
        CancellationToken ct)
    {
        var paging = PageRequest.From(page, pageSize, sort, dir, ListSorts.Students);
        if (!paging.Succeeded) return BadRequest(new { message = paging.Error });
        if (paging.Value is null) return Ok(await _studentService.GetAllAsync(search, ct));

        if (!PagingHeaders.TryParseStatus(status, out var statusFilter))
            return BadRequest(new { message = "status must be Active, Inactive or Archived." });
        var result = await _studentService.GetPageAsync(
            new StudentListFilter(search, grade, statusFilter, hasRfid, hasGuardian, presentToday), paging.Value, ct);
        PagingHeaders.SetTotal(Response, result.Total);
        return Ok(result.Items);
    }

    /// <summary>Distinct grades in use, for the list's grade filter (independent of the page shown).</summary>
    [HttpGet("grades")]
    public async Task<IActionResult> GetGrades(CancellationToken ct) => Ok(await _studentService.GetGradesAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _studentService.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateStudentRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var result = await _studentService.CreateAsync(request, ct);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.StudentId }, result.Value)
            : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateStudentRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        var result = await _studentService.UpdateAsync(id, request, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    /// <summary>Full edit including section, admission number and gender, saved atomically.</summary>
    [HttpPut("{id:int}/record")]
    public async Task<IActionResult> UpdateRecord(int id, UpdateStudentRecordRequest request,
        [FromServices] IValidator<UpdateStudentRecordRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var result = await _studentService.UpdateRecordAsync(id, request, ct);
        if (result.Succeeded) return Ok(result.Value);
        return result.Error == "Student not found." ? NotFound(new { message = result.Error }) : Conflict(new { message = result.Error });
    }

    /// <summary>What archiving would do, and what (if anything) prevents permanent deletion.</summary>
    [HttpGet("{id:int}/deletion-check")]
    public async Task<IActionResult> DeletionCheck(int id, [FromServices] IRecordLifecycleService lifecycle, CancellationToken ct)
    {
        var result = await lifecycle.CheckStudentAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id, ArchiveRecordRequest request, [FromServices] IValidator<ArchiveRecordRequest> validator,
        [FromServices] IRecordLifecycleService lifecycle, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return this.ToResult(await lifecycle.ArchiveStudentAsync(id, request.Reason, this.GetUserId(), ct));
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id, RestoreRecordRequest request, [FromServices] IValidator<RestoreRecordRequest> validator,
        [FromServices] IRecordLifecycleService lifecycle, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return this.ToResult(await lifecycle.RestoreStudentAsync(id, request.Reason, this.GetUserId(), ct));
    }

    /// <summary>Permanent deletion; only for a student with no history (409 lists what blocks it).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromBody] DeleteRecordRequest request, [FromServices] IValidator<DeleteRecordRequest> validator,
        [FromServices] IRecordLifecycleService lifecycle, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return this.ToResult(await lifecycle.DeleteStudentAsync(id, request.Reason, this.GetUserId(), ct));
    }

    /// <summary>Replaces the student's residential address. Every part blank clears it.</summary>
    [HttpPut("{id:int}/address")]
    public async Task<IActionResult> UpdateAddress(int id, AddressDto address, [FromServices] IValidator<AddressDto> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(address, ct);
        var result = await _studentService.UpdateAddressAsync(id, address, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStudentStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        var result = await _studentService.UpdateStatusAsync(id, request, ct);
        return result.Succeeded ? Ok(new { message = "Status updated." }) : NotFound(new { message = result.Error });
    }

    [HttpPost("{id:int}/guardians")]
    public async Task<IActionResult> AssignGuardian(int id, AssignGuardianRequest request, CancellationToken ct)
    {
        await _assignGuardianValidator.ValidateAndThrowAsync(request, ct);
        var result = await _studentService.AssignGuardianAsync(id, request, ct);
        return result.Succeeded ? Ok(new { message = "Guardian associated." }) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id:int}/guardians/{guardianId:int}")]
    public async Task<IActionResult> UpdateGuardianLink(int id, int guardianId, UpdateStudentGuardianRequest request, CancellationToken ct)
    {
        await _guardianLinkValidator.ValidateAndThrowAsync(request, ct);
        var result = await _studentService.UpdateGuardianLinkAsync(id, guardianId, request, ct);
        return result.Succeeded ? Ok(new { message = "Guardian link updated." }) : NotFound(new { message = result.Error });
    }

    [HttpDelete("{id:int}/guardians/{guardianId:int}")]
    public async Task<IActionResult> RemoveGuardianLink(int id, int guardianId, CancellationToken ct)
    {
        var result = await _studentService.RemoveGuardianLinkAsync(id, guardianId, ct);
        return result.Succeeded ? Ok(new { message = "Guardian unlinked." }) : NotFound(new { message = result.Error });
    }

    // ---- Profile photo ----

    [HttpGet("{id:int}/photo")]
    public async Task<IActionResult> GetPhoto(int id, CancellationToken ct)
    {
        var result = await _photos.GetStudentPhotoAsync(id, ct);
        if (!result.Succeeded) return NotFound(new { message = result.Error });

        var photo = result.Value!;
        // Private: a child's photo must never be cached by a shared proxy.
        Response.Headers.CacheControl = "private, max-age=60";
        return File(photo.Data, photo.ContentType);
    }

    [HttpPost("{id:int}/photo")]
    [RequestSizeLimit(PhotoUploads.RequestSizeLimit)]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile? file, CancellationToken ct)
    {
        var read = await PhotoUploads.ReadAsync(file, ct);
        if (!read.Succeeded) return BadRequest(new { message = read.Error });

        var result = await _photos.SaveStudentPhotoAsync(id, read.Value!, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id:int}/photo")]
    public async Task<IActionResult> DeletePhoto(int id, CancellationToken ct)
    {
        var result = await _photos.RemoveStudentPhotoAsync(id, ct);
        return result.Succeeded ? Ok(new { message = "Photo removed." }) : NotFound(new { message = result.Error });
    }
}
