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
/// <param name="Search">Name, email or phone (as typed, or its digits).</param>
/// <param name="FaceReady">An active registered face.</param>
/// <param name="AwaitingFirstSignIn">Still on an issued password.</param>
public sealed record GuardianListFilter(
    string? Search, ListStatusFilter? Status, bool? FaceReady = null, bool? HasStudents = null,
    bool? HasPhoto = null, bool? AwaitingFirstSignIn = null);

/// <summary>Whether an assignment is current or has ended (its history is kept).</summary>
public enum AssignmentStatusFilter
{
    Active,
    Ended
}

/// <summary>Server-side filters for the paged bus assignment list (GET /api/transportation/assignments?page=…).</summary>
/// <param name="Search">Student, bus number, or route (the assignment's own, else the bus's).</param>
public sealed record AssignmentListFilter(string? Search, AssignmentStatusFilter? Status, int? BusId);

/// <summary>Sort keys each paged list accepts (lower case).</summary>
public static class ListSorts
{
    public static readonly IReadOnlySet<string> Students = new HashSet<string> { "name", "grade", "activity", "status" };
    public static readonly IReadOnlySet<string> Assignments = new HashSet<string> { "student", "bus", "assigned", "status" };
    public static readonly IReadOnlySet<string> Guardians = new HashSet<string> { "name", "email", "students", "status", "activity" };
}
