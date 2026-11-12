using ChildGuard.Domain.Enums;

namespace ChildGuard.AdminWeb.Models.ViewModels;

/// <summary>
/// Everything the shared avatar partial needs. <paramref name="PhotoUrl"/> is an
/// AdminWeb route, never an API or storage path, and it is null when the person
/// has no photo — in which case initials are rendered instead of a broken image.
/// </summary>
public record AvatarModel(
    string? PhotoUrl,
    string Name,
    string Size = "",
    bool IsGuardian = false,
    bool Square = false,
    string? StatusColor = null,
    string? StatusTitle = null);

/// <summary>
/// Everything the photo manager panel needs. It is driven entirely by AdminWeb
/// routes: the browser posts the file to <paramref name="Controller"/> and reads
/// the image back from the same place, so neither the API URL nor any storage
/// location is exposed to the page.
/// </summary>
public record PhotoManagerModel(
    string Controller,
    int OwnerId,
    string OwnerName,
    bool HasPhoto,
    DateTime? UpdatedAt,
    bool IsGuardian,
    string Title,
    string Description);

/// <summary>
/// The single mapping from a safety status to how it is presented. Status is
/// always shown as a label plus a colour (and usually an icon), so meaning never
/// depends on the colour alone.
/// </summary>
public static class SafetyUi
{
    public static string Label(StudentSafetyStatus status) => status switch
    {
        StudentSafetyStatus.OnBusToSchool => "On the bus",
        StudentSafetyStatus.AtSchool => "Safe at school",
        StudentSafetyStatus.LeftSchool => "Left school",
        StudentSafetyStatus.DroppedOffHome => "Dropped off",
        _ => "No activity today"
    };

    /// <summary>A fuller sentence for cards that have room to explain the status.</summary>
    public static string Explanation(StudentSafetyStatus status) => status switch
    {
        StudentSafetyStatus.OnBusToSchool => "Boarded the bus; school arrival not yet recorded.",
        StudentSafetyStatus.AtSchool => "Checked in at school and has not departed.",
        StudentSafetyStatus.LeftSchool => "Departed school; drop-off not yet recorded.",
        StudentSafetyStatus.DroppedOffHome => "Bus drop-off recorded — the day's journey is complete.",
        _ => "No checkpoint has been recorded for this student today."
    };

    public static string BadgeClass(StudentSafetyStatus status) => status switch
    {
        StudentSafetyStatus.AtSchool => "cg-badge--success",
        StudentSafetyStatus.DroppedOffHome => "cg-badge--success",
        StudentSafetyStatus.OnBusToSchool => "cg-badge--info",
        StudentSafetyStatus.LeftSchool => "cg-badge--warning",
        _ => "cg-badge--neutral"
    };

    public static string Icon(StudentSafetyStatus status) => status switch
    {
        StudentSafetyStatus.OnBusToSchool => "bus",
        StudentSafetyStatus.AtSchool => "shield-check",
        StudentSafetyStatus.LeftSchool => "log-out",
        StudentSafetyStatus.DroppedOffHome => "home",
        _ => "help-circle"
    };

    /// <summary>Hex used for the small dot on an avatar, where no text fits.</summary>
    public static string DotColor(StudentSafetyStatus status) => status switch
    {
        StudentSafetyStatus.AtSchool or StudentSafetyStatus.DroppedOffHome => "#16A36A",
        StudentSafetyStatus.OnBusToSchool => "#2B8FC9",
        StudentSafetyStatus.LeftSchool => "#E99A32",
        _ => "#8A97AB"
    };

    public static string EventLabel(JourneyEventType? type) => type switch
    {
        JourneyEventType.BusBoarding => "Bus boarding",
        JourneyEventType.SchoolArrival => "School arrival",
        JourneyEventType.SchoolDeparture => "School departure",
        JourneyEventType.BusDropoff => "Bus drop-off",
        _ => "—"
    };

    public static string EventIcon(JourneyEventType type) => type switch
    {
        JourneyEventType.BusBoarding => "bus",
        JourneyEventType.SchoolArrival => "log-in",
        JourneyEventType.SchoolDeparture => "log-out",
        JourneyEventType.BusDropoff => "home",
        _ => "circle"
    };

    public static string AlertMarkClass(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "cg-alertrow__mark--critical",
        AlertSeverity.Warning => "cg-alertrow__mark--warning",
        _ => "cg-alertrow__mark--info"
    };

    public static string AlertBadgeClass(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "cg-badge--danger",
        AlertSeverity.Warning => "cg-badge--warning",
        _ => "cg-badge--info"
    };

    public static string AlertIcon(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "alert-octagon",
        AlertSeverity.Warning => "alert-triangle",
        _ => "info"
    };

    /// <summary>Up to two initials, used when no photo is stored.</summary>
    public static string Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}
