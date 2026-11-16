using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.ActorType).IsRequired().HasMaxLength(20);
        b.Property(x => x.ActorName).HasMaxLength(150);
        b.Property(x => x.Action).IsRequired().HasMaxLength(60);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(40);
        b.Property(x => x.Summary).IsRequired().HasMaxLength(500);
        b.Property(x => x.Details).HasMaxLength(AuditDetails.MaxLength);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.StudentId);
        b.HasIndex(x => x.GuardianId);
        b.HasIndex(x => new { x.ActorType, x.ActorId });
    }
}

/// <summary>Actor outside any request (startup seeding, background work).</summary>
public sealed class SystemActor : ICurrentActor
{
    public string Type => "System";
    public int? Id => null;
    public string? Name => null;
}

public class EfAuditTrail : IAuditTrail
{
    private readonly ChildGuardDbContext _db;
    private readonly ICurrentActor _actor;
    private readonly SchoolClock _clock;

    public EfAuditTrail(ChildGuardDbContext db, ICurrentActor actor, SchoolClock clock)
    {
        _db = db;
        _actor = actor;
        _clock = clock;
    }

    public void Record(AuditEvent e) => _db.AuditEntries.Add(Build(e));

    public async Task SaveWithAuditAsync(Func<CancellationToken, Task> save, Func<AuditEvent> describe, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            await save(ct);
            Record(describe());
            await _db.SaveChangesAsync(ct);
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await save(ct);
            Record(describe());
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RecordNowAsync(AuditEvent e, CancellationToken ct = default)
    {
        Record(e);
        await _db.SaveChangesAsync(ct);
    }

    private AuditEntry Build(AuditEvent e)
    {
        var (type, id, name) = e.ActorAdminId is int adminId
            ? ("Admin", (int?)adminId, e.ActorName)
            : (_actor.Type, _actor.Id, _actor.Name);
        return new AuditEntry
        {
            OccurredAt = _clock.UtcNow,
            ActorType = type,
            ActorId = id,
            ActorName = Cut(name, 150),
            Action = Cut(e.Action, 60)!,
            EntityType = Cut(e.EntityType, 40)!,
            EntityId = e.EntityId,
            StudentId = e.StudentId,
            GuardianId = e.GuardianId,
            Summary = Cut(e.Summary, 500)!,
            Details = AuditDetails.Serialize(e.Details),
            Reason = Cut(e.Reason, 500)
        };
    }

    private static string? Cut(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}

public class AuditQueryService : IAuditQueryService
{
    public const int MaxPageSize = 200;
    private readonly ChildGuardDbContext _db;
    private readonly SchoolClock _clock;

    public AuditQueryService(ChildGuardDbContext db, SchoolClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AuditPageDto> SearchAsync(AuditQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, MaxPageSize);
        var query = _db.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q.EntityType)) query = query.Where(a => a.EntityType == q.EntityType);
        if (q.EntityId is int entityId) query = query.Where(a => a.EntityId == entityId);
        if (q.StudentId is int studentId) query = query.Where(a => a.StudentId == studentId);
        if (q.GuardianId is int guardianId) query = query.Where(a => a.GuardianId == guardianId);
        if (!string.IsNullOrWhiteSpace(q.ActorType)) query = query.Where(a => a.ActorType == q.ActorType);
        if (q.ActorId is int actorId) query = query.Where(a => a.ActorId == actorId);
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action == q.Action);
        if (q.From is DateOnly from) { var start = _clock.StartOfDayUtc(from); query = query.Where(a => a.OccurredAt >= start); }
        if (q.To is DateOnly to) { var end = _clock.StartOfDayUtc(to.AddDays(1)); query = query.Where(a => a.OccurredAt < end); }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var text = q.Search.Trim();
            // MySQL's collation makes Contains case-insensitive; PostgreSQL needs ILIKE to match.
            if (_db.Database.IsNpgsql())
            {
                var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(a => EF.Functions.ILike(a.Summary, pattern, "\\"));
            }
            else query = query.Where(a => a.Summary.Contains(text));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(a => new AuditEntryDto(a.Id, a.OccurredAt, a.ActorType, a.ActorId, a.ActorName, a.Action, a.EntityType,
                a.EntityId, a.StudentId, a.GuardianId, a.Summary, a.Details, a.Reason))
            .ToListAsync(ct);
        return new AuditPageDto(items, total, page, size);
    }
}
