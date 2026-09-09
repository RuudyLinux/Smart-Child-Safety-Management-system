namespace ChildGuard.Application.Common;

/// <summary>
/// Email settings bound from the "Email" configuration section. The provider API
/// key is read separately in DependencyInjection and never stored on this object,
/// so it cannot be logged or serialised by accident.
/// </summary>
public class EmailOptions
{
    /// <summary>RFC 5322 From value, e.g. "ChildGuard &lt;onboarding@resend.dev&gt;".</summary>
    public string From { get; set; } = "ChildGuard <onboarding@resend.dev>";
}
