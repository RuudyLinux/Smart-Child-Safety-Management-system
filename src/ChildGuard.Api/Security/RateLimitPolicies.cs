namespace ChildGuard.Api.Security;

/// <summary>Named rate-limit policies applied to abuse-prone endpoints (login, password reset, RFID scan, face analysis).</summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Otp = "otp";
    public const string RfidScan = "rfid-scan";
    public const string FaceAnalyze = "face-analyze";
    public const string LiveVerify = "live-verify";
}
