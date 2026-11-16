using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

/// <param name="Details">An explicit object of the relevant values. It is serialized and redacted; never pass an entity.</param>
/// <param name="ActorAdminId">The acting admin, for actions taken before the caller is authenticated (sign-in).</param>
public sealed record AuditEvent(string Action, string EntityType, int? EntityId, string Summary)
{
    public object? Details { get; init; }
    public int? StudentId { get; init; }
    public int? GuardianId { get; init; }
    public string? Reason { get; init; }
    public int? ActorAdminId { get; init; }
    public string? ActorName { get; init; }
}

/// <summary>
/// The general audit trail. The acting user is taken from the current request. Entries share the caller's unit of
/// work, so an entry is saved, committed or rolled back together with the change it describes.
/// </summary>
public interface IAuditTrail
{
    /// <summary>Queues an entry; the caller's next SaveChanges stores it with the change.</summary>
    void Record(AuditEvent entry);

    /// <summary>
    /// For creations, whose id exists only after saving: runs <paramref name="save"/>, then stores the entry built by
    /// <paramref name="describe"/>, both in one transaction (the caller's, when there is one).
    /// </summary>
    Task SaveWithAuditAsync(Func<CancellationToken, Task> save, Func<AuditEvent> describe, CancellationToken ct = default);

    /// <summary>Records and saves at once, for an action that changes nothing else (e.g. a refused sign-in).</summary>
    Task RecordNowAsync(AuditEvent entry, CancellationToken ct = default);
}

/// <summary>Who is acting in the current request.</summary>
public interface ICurrentActor
{
    /// <summary>Admin, Guardian, Device, System or Anonymous.</summary>
    string Type { get; }
    int? Id { get; }
    string? Name { get; }
}

public interface IAuditQueryService
{
    Task<AuditPageDto> SearchAsync(AuditQuery query, CancellationToken ct = default);
}

/// <summary>Used where no audit trail is wired (unit tests): records nothing, saves as asked.</summary>
public sealed class NullAuditTrail : IAuditTrail
{
    public static readonly NullAuditTrail Instance = new();
    public void Record(AuditEvent entry) { }
    public Task SaveWithAuditAsync(Func<CancellationToken, Task> save, Func<AuditEvent> describe, CancellationToken ct = default) => save(ct);
    public Task RecordNowAsync(AuditEvent entry, CancellationToken ct = default) => Task.CompletedTask;
}
