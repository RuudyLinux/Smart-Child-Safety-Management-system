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
    private readonly IValidator<CreateStudentRequest> _createValidator;
    private readonly IValidator<UpdateStudentRequest> _updateValidator;
    private readonly IValidator<AssignGuardianRequest> _assignGuardianValidator;

    public StudentsController(
        IStudentService studentService,
        IValidator<CreateStudentRequest> createValidator,
        IValidator<UpdateStudentRequest> updateValidator,
        IValidator<AssignGuardianRequest> assignGuardianValidator)
    {
        _studentService = studentService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _assignGuardianValidator = assignGuardianValidator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, CancellationToken ct) =>
        Ok(await _studentService.GetAllAsync(search, ct));

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

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStudentStatusRequest request, CancellationToken ct)
    {
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
}
