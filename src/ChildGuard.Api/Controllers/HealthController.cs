using ChildGuard.Application.Interfaces;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Liveness only: answers without touching the database. Platform health checks poll this, so they
    /// never wake a sleeping serverless database (Neon free plan has 100 compute-hours a month).
    /// </summary>
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "ok" });

    /// <summary>
    /// Is the face service reachable from the API (same address, key and tunnel as real face calls)?
    /// Anonymous like the rest of this controller, so it reports only up/down, whether liveness can work,
    /// the round trip and a short reason; never the address, key or model details. Cached for 15 s.
    /// </summary>
    [HttpGet("face")]
    public async Task<IActionResult> Face([FromServices] IFaceServiceHealthProbe probe, CancellationToken ct)
    {
        var h = await probe.CheckAsync(ct);
        var state = !h.Reachable ? "unreachable" : h.Problem is null ? "ok" : "degraded";
        return Ok(new { faceService = state, antiSpoofing = h.AntiSpoofing, latencyMs = h.LatencyMs, problem = h.Problem });
    }

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
