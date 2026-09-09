using ChildGuard.Application.Common;

namespace ChildGuard.Application.Interfaces;

/// <summary>One outbound email, always carrying a plain-text fallback beside the HTML body.</summary>
public record EmailMessage(string ToEmail, string ToName, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Transactional email delivery. The provider and its credentials come from
/// configuration, never from code — see <c>Email:*</c> in the README.
/// Returns a <see cref="Result"/> rather than throwing so callers can report a
/// delivery failure to the operator instead of losing it.
/// </summary>
public interface IEmailSender
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default);
}
