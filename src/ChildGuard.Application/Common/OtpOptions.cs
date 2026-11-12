namespace ChildGuard.Application.Common;

/// <summary>
/// OTP policy, bound from the "Otp" configuration section. Defaults are the
/// values the flow is specified with, so the app runs without extra config.
/// </summary>
public class OtpOptions
{
    /// <summary>Number of digits in a generated code.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>How long a code stays valid after it is sent.</summary>
    public int ExpiryMinutes { get; set; } = 5;

    /// <summary>Wrong codes allowed per issued code before the challenge is burned.</summary>
    public int MaxVerificationAttempts { get; set; } = 5;

    /// <summary>Times a fresh code may be re-sent for one login attempt.</summary>
    public int MaxResends { get; set; } = 3;

    /// <summary>Minimum wait between sends, to stop resend flooding of the contact number.</summary>
    public int ResendIntervalSeconds { get; set; } = 60;

    /// <summary>Lifetime of the ticket that authorises the mandatory first-login password reset.</summary>
    public int PasswordResetTicketMinutes { get; set; } = 15;

    /// <summary>
    /// Development escape hatch: when delivery of a code fails, also write it to the
    /// log so a local flow can still be completed (for example while the email
    /// provider is in test mode and rejects most recipients). Off by default and
    /// must stay off in production — a logged code is a code anyone with log access
    /// can use. The delivery failure is logged as an error either way.
    /// </summary>
    public bool LogCodeOnDeliveryFailure { get; set; }

    /// <summary>
    /// Days a phone stays trusted after its last use (the OTP is then skipped on that phone). 0 turns the feature off:
    /// every sign-in needs the OTP and no device is ever trusted.
    /// </summary>
    public int TrustedDeviceDays { get; set; } = 90;
}
