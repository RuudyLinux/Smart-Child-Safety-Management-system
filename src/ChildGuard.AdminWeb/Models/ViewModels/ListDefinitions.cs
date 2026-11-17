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
