using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.FaceService;

public class EfFaceAuditLog : IFaceAuditLog
{
    private readonly DbContextOptions<ChildGuardDbContext> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<EfFaceAuditLog> _logger;

    public EfFaceAuditLog(DbContextOptions<ChildGuardDbContext> options, TimeProvider time, ILogger<EfFaceAuditLog> logger)
    {
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task WriteAsync(FaceAuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var db = new ChildGuardDbContext(_options);
            db.FaceAuditLogs.Add(new FaceAuditLog
            {
                OccurredAt = _time.GetUtcNow().UtcDateTime,
                ActorRole = entry.Actor.Role,
                ActorId = entry.Actor.Id,
                Layer = entry.Layer,
                Action = Truncate(entry.Action, 40)!,
                Outcome = Truncate(entry.Outcome, 40)!,
                StudentId = entry.StudentId,
                GuardianId = entry.GuardianId,
                VerificationId = entry.VerificationId,
                SessionId = entry.SessionId,
                Detail = Truncate(entry.Detail, 1000)
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An audit failure must not turn a face check into an error for the desk, but it
            // must be loud: the trail has a gap.
            _logger.LogError(ex, "Could not write face audit row {Layer}/{Action}.", entry.Layer, entry.Action);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
