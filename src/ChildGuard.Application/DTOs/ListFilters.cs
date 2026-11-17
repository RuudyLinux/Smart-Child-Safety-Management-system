namespace ChildGuard.Application.DTOs;

/// <summary>Record status as the admin lists show it: archived records are a status of their own.</summary>
public enum ListStatusFilter
{
    Active,
    Inactive,
    Archived
}

/// <summary>Server-side filters for the paged student list (GET /api/students?page=…).</summary>
/// <param name="PresentToday">A school-arrival attendance on today's school date (SchoolClock), as on the list.</param>
public sealed record StudentListFilter(
    string? Search, string? Grade, ListStatusFilter? Status, bool? HasRfid, bool? HasGuardian, bool? PresentToday);

/// <summary>Server-side filters for the paged guardian list (GET /api/guardians?page=…).</summary>
public sealed record GuardianListFilter(string? Search, ListStatusFilter? Status);

/// <summary>Sort keys each paged list accepts (lower case).</summary>
public static class ListSorts
{
    public static readonly IReadOnlySet<string> Students = new HashSet<string> { "name", "grade", "activity", "status" };
    public static readonly IReadOnlySet<string> Guardians = new HashSet<string> { "name", "email", "students", "status" };
}
