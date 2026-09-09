namespace ChildGuard.Api.Security;

/// <summary>Named rate-limit policies applied to abuse-prone endpoints (login, password reset, RFID scan).</summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Otp = "otp";
    public const string RfidScan = "rfid-scan";
}
