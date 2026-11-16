using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/enrollments")]
[Authorize(Policy = "AdminOnly")]
public class EnrollmentsController : ControllerBase
{
    public const string ConfirmationRequiredCode = "ConfirmationRequired";
    public const string KeyReusedCode = "IdempotencyKeyReused";

    private readonly IEnrollmentService _enrollment;
    private readonly IValidator<EnrollStudentRequest> _validator;

    public EnrollmentsController(IEnrollmentService enrollment, IValidator<EnrollStudentRequest> validator)
    {
        _enrollment = enrollment;
        _validator = validator;
    }

    /// <summary>
    /// Enrolls a student with guardians, RFID card and optional transport in one transaction.
    /// 201 new enrollment; 200 the same request was already completed (nothing new written);
    /// 409 <c>ConfirmationRequired</c> (possible duplicate student or deactivated guardian, nothing written)
    /// or <c>IdempotencyKeyReused</c>; 400 any other refusal. Generated passwords are never returned.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Enroll(EnrollStudentRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var outcome = await _enrollment.EnrollAsync(request, this.GetUserId(), ct);

        if (outcome.Confirmation is { } c)
            return Conflict(new { message = c.Message, code = ConfirmationRequiredCode, confirmation = c });
        if (outcome.KeyReused)
            return Conflict(new { message = outcome.Error, code = KeyReusedCode });
        if (outcome.Error is not null)
            return BadRequest(new { message = outcome.Error });

        var r = outcome.Enrollment!;
        return r.AlreadyEnrolled ? Ok(r) : Created($"/api/students/{r.StudentId}", r);
    }

    /// <summary>Step-1 check: is the admission number taken, and does a student share this name and date of birth? Read-only.</summary>
    [HttpPost("check-student")]
    public async Task<IActionResult> CheckStudent(EnrollmentStudentCheckRequest request,
        [FromServices] IValidator<EnrollmentStudentCheckRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await _enrollment.CheckStudentAsync(request, ct));
    }
}
