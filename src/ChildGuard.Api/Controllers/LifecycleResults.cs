using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>Maps an archive/restore/delete outcome to a response: 200, 404, 409 (with what blocks a deletion) or 400.</summary>
internal static class LifecycleResults
{
    public static IActionResult ToResult(this ControllerBase c, LifecycleOutcome o)
    {
        if (o.Done is { } done) return c.Ok(done);
        if (o.NotFound) return c.NotFound(new { message = o.Error });
        if (o.Blocked is { } blocked)
            return c.Conflict(new
            {
                message = "This record has history and cannot be permanently deleted: " + string.Join("; ", blocked.Blockers) + ". Archive it instead.",
                code = "HasHistory",
                check = blocked
            });
        return c.BadRequest(new { message = o.Error });
    }
}
