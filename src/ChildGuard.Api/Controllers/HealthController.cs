using ChildGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Liveness probe for the diagnostics page and for "is the API up?" checks.
/// Anonymous on purpose — it must answer while nobody can log in — so it reports
/// only whether the process and its database are reachable, never versions,
/// configuration, connection strings or counts.
/// </summary>
[ApiController]
[Route("api/health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly ChildGuardDbContext _db;
    public HealthController(ChildGuardDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseReachable = await _db.Database.CanConnectAsync(ct);
        return Ok(new
        {
            status = "ok",
            database = databaseReachable ? "ok" : "unreachable"
        });
    }
}
