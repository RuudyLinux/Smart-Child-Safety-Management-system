namespace ChildGuard.Application.Common;

/// <summary>
/// Outbound email settings, bound from the "Email" configuration section.
/// Credentials (<c>Resend:ApiKey</c>, <c>Smtp:Password</c>) come only from user-secrets or
/// environment variables, never from the repository, and are never logged.
/// </summary>
public class EmailOptions
{
    /// <summary>RFC 5322 From value, e.g. "ChildGuard &lt;onboarding@resend.dev&gt;".</summary>
    public string From { get; set; } = "ChildGuard <onboarding@resend.dev>";

    /// <summary>
    /// "Resend", "Smtp" or "Log". Leave empty to use Resend when <c>Resend:ApiKey</c> is set and
    /// otherwise Log (Development only: it writes email bodies, including login codes, to the log).
    /// </summary>
    public string? Provider { get; set; }

    public ResendEmailOptions Resend { get; set; } = new();
    public SmtpEmailOptions Smtp { get; set; } = new();
}

public class ResendEmailOptions
{
    public string? ApiKey { get; set; }

    /// <summary>
    /// The email address of the Resend account. Resend's shared testing sender (<c>onboarding@resend.dev</c>)
    /// may only deliver to that address, so while it is the sender every message is redirected here, marked
    /// with who it was meant for. Without it, sends from the testing sender are refused rather than attempted.
    /// Ignored once the sender is on a verified domain.
    /// </summary>
    public string? TestRecipient { get; set; }
}

public class SmtpEmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}
