using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/guardians")]
[Authorize(Policy = "AdminOnly")]
public class GuardiansController : ControllerBase
{
    private readonly IGuardianService _guardianService;
    private readonly IValidator<UpdateGuardianStatusRequest> _statusValidator;
    private readonly IValidator<AdminUpdateGuardianCredentialsRequest> _credentialsValidator;
    private readonly IValidator<CreateGuardianRequest> _createValidator;
    private readonly IValidator<AdminUpdateGuardianRequest> _updateValidator;

    public GuardiansController(
        IGuardianService guardianService,
        IValidator<UpdateGuardianStatusRequest> statusValidator,
        IValidator<AdminUpdateGuardianCredentialsRequest> credentialsValidator,
        IValidator<CreateGuardianRequest> createValidator,
        IValidator<AdminUpdateGuardianRequest> updateValidator)
    {
        _guardianService = guardianService;
        _statusValidator = statusValidator;
        _credentialsValidator = credentialsValidator;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
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
}
