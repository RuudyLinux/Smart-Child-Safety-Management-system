namespace ChildGuard.AdminWeb.Models.ViewModels;

/// <summary>The server-paged lists of AdminWeb: one definition each (page, API endpoint, sorts, filters).</summary>
public static class ListDefinitions
{
    public static readonly ListDefinition Assignments = new(
        BasePath: "/Transportation",
        ApiPath: "/api/transportation/assignments",
        Sorts: new HashSet<string> { "student", "bus", "assigned", "status" },
        Filters: new[]
        {
            ListFilter.Choice("status", "status", ("Active", "Active"), ("Ended", "Ended")),
        });
    public static readonly ListDefinition RfidCards = new(
        BasePath: "/Rfid",
        ApiPath: "/api/rfid/cards",
        Sorts: new HashSet<string> { "student", "status", "assigned" },
        Filters: new[]
        {
            ListFilter.Choice("state", "status", ("Active", "Active"), ("Inactive", "Inactive"), ("Lost", "Lost"), ("Damaged", "Damaged"), ("Retired", "Retired")),
            ListFilter.Choice("assigned", "assigned", ("yes", "true"), ("no", "false")),
        });

    public static readonly ListDefinition VerificationHistory = new(
        BasePath: "/Verification/History",
        ApiPath: "/api/reports/verification",
        Sorts: new HashSet<string> { "when", "score", "student", "guardian" },
        Filters: new[]
        {
            ListFilter.Text("studentId", "studentId", ListFilter.IsNumber),
            ListFilter.Choice("result", "result", ("Match", "Match"), ("NoMatch", "NoMatch")),
            ListFilter.Choice("handover", "approved", ("approved", "true"), ("not", "false")),
            ListFilter.Text("from", "from", ListFilter.IsDate),
            ListFilter.Text("to", "to", ListFilter.IsDate),
        },
        PageSize: 50);

    public static readonly ListDefinition Guardians = new(
        BasePath: "/Guardians",
        ApiPath: "/api/guardians",
        Sorts: new HashSet<string> { "name", "children", "activity" },
        Filters: new[]
        {
            ListFilter.Choice("verification", "faceReady", ("ready", "true"), ("missing", "false")),
            ListFilter.Choice("children", "hasStudents", ("linked", "true"), ("none", "false")),
            ListFilter.Choice("status", "status", ("Active", "Active"), ("Inactive", "Inactive"), ("Archived", "Archived")),
        },
        SortAliases: new Dictionary<string, string> { ["children"] = "students" });
}
