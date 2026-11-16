using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Common;

public static class LicenceRules
{
    /// <summary>A licence expiring within this many days is flagged so it can be renewed in time.</summary>
    public const int WarnDays = 30;

    public static LicenceState State(DateOnly? expiresOn, DateOnly today) => expiresOn switch
    {
        null => LicenceState.NotRecorded,
        { } d when d < today => LicenceState.Expired,
        { } d when d <= today.AddDays(WarnDays) => LicenceState.ExpiringSoon,
        _ => LicenceState.Valid
    };
}
