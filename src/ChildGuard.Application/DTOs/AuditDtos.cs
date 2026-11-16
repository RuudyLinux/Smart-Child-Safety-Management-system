namespace ChildGuard.Application.DTOs;

public record AuditEntryDto(
    long Id, DateTime OccurredAt, string ActorType, int? ActorId, string? ActorName,
    string Action, string EntityType, int? EntityId, int? StudentId, int? GuardianId,
    string Summary, string? Details, string? Reason);

public record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, int Total, int Page, int PageSize);

/// <param name="From">School date (inclusive).</param>
/// <param name="To">School date (inclusive).</param>
/// <param name="Search">Text contained in the summary.</param>
public record AuditQuery(
    string? EntityType = null, int? EntityId = null, int? StudentId = null, int? GuardianId = null,
    string? ActorType = null, int? ActorId = null, string? Action = null,
    DateOnly? From = null, DateOnly? To = null, string? Search = null,
    int Page = 1, int PageSize = 50);
