using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>Read-only access to the audit trail. There is deliberately no endpoint that edits or deletes entries.</summary>
[ApiController]
[Route("api/audit")]
[Authorize(Policy = "AdminOnly")]
public class AuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;
    public AuditController(IAuditQueryService audit) => _audit = audit;

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? entityType, [FromQuery] int? entityId, [FromQuery] int? studentId, [FromQuery] int? guardianId,
        [FromQuery] string? actorType, [FromQuery] int? actorId, [FromQuery] string? action,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (from is DateOnly f && to is DateOnly t && f > t)
            return BadRequest(new { message = "The start date must not be after the end date." });
        if (search is { Length: > 200 })
            return BadRequest(new { message = "Search text is limited to 200 characters." });
        return Ok(await _audit.SearchAsync(new AuditQuery(entityType, entityId, studentId, guardianId, actorType, actorId, action,
            from, to, search, page, pageSize), ct));
    }
}
