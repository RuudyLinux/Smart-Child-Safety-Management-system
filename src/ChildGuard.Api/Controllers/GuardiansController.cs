using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/guardians")]
[Authorize(Policy = "AdminOnly")]
public class GuardiansController : ControllerBase
{
    private readonly IGuardianService _guardianService;
    public GuardiansController(IGuardianService guardianService) => _guardianService = guardianService;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _guardianService.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _guardianService.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateGuardianStatusRequest request, CancellationToken ct)
    {
        var result = await _guardianService.UpdateStatusAsync(id, request, ct);
        return result.Succeeded ? Ok(new { message = "Status updated." }) : NotFound(new { message = result.Error });
    }
}
