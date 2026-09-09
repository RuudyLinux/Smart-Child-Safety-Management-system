namespace ChildGuard.Application.Common;

/// <summary>
/// The single backend location for the password an admin-created Guardian starts
/// with. It is never sent to any client as a default value and never appears in
/// frontend code — the account is created with it server-side, it is emailed to
/// the Guardian, and the first login forces it to be replaced.
/// </summary>
public static class GuardianCredentialDefaults
{
    public const string InitialPassword = "PASSWORD";
}
