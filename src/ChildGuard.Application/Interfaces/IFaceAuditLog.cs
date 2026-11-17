using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

/// <param name="Role">"Admin" or "Guardian" (the authenticated caller), "System" for automatic steps.</param>
public sealed record FaceActor(string Role, int? Id)
{
    public static FaceActor Admin(int id) => new("Admin", id);
    public static FaceActor Guardian(int id) => new("Guardian", id);
    public static FaceActor System { get; } = new("System", null);
}

public sealed record FaceAuditEntry(
    FaceAuditLayer Layer, string Action, string Outcome, FaceActor Actor,
    int? StudentId = null, int? GuardianId = null, int? VerificationId = null, int? SessionId = null,
    string? Detail = null);

/// <summary>
/// Append-only audit trail, one row per verification layer. Writes use their own unit of
/// work so a denied or failed attempt is still recorded, and so an audit write never
/// commits half-finished changes made by the caller. Never pass images, embeddings or tokens.
/// </summary>
public interface IFaceAuditLog
{
    Task WriteAsync(FaceAuditEntry entry, CancellationToken ct = default);
}
