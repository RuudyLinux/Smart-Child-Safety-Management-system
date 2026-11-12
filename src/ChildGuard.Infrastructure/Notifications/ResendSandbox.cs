using System.Net;
using System.Net.Mail;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Infrastructure.Notifications;

/// <summary>
/// Resend's shared testing sender (<c>@resend.dev</c>) may only deliver to the Resend account's own address;
/// anything else is rejected by Resend. While the sender is that address this redirects every message to the
/// configured test recipient (marked with who it was meant for), and refuses outright when none is configured,
/// so no request for an arbitrary guardian address is ever made through the unverified sender. A sender on a
/// verified domain passes through untouched.
/// </summary>
public static class ResendSandbox
{
    public const string TestingDomain = "resend.dev";

    public static bool IsTestingSender(string from) =>
        TryAddress(from)?.EndsWith("@" + TestingDomain, StringComparison.OrdinalIgnoreCase) == true;

    public static Result<EmailMessage> Apply(EmailMessage message, string from, string? testRecipient)
    {
        if (!IsTestingSender(from))
            return Result<EmailMessage>.Success(message);

        if (string.IsNullOrWhiteSpace(testRecipient))
            return Result<EmailMessage>.Failure(
                "Email test mode: the sender is Resend's testing address, which can only deliver to the Resend account's own email. " +
                "Set Email:Resend:TestRecipient to that address, or verify a domain and change Email:From.");

        if (string.Equals(message.ToEmail, testRecipient.Trim(), StringComparison.OrdinalIgnoreCase))
            return Result<EmailMessage>.Success(message);

        var intended = message.ToEmail;
        var banner = $"[TEST MODE] This message was meant for {intended}. It was redirected to the Resend test inbox because the sender is not on a verified domain.";
        return Result<EmailMessage>.Success(new EmailMessage(
            testRecipient.Trim(), "Test inbox",
            $"[TEST for {intended}] {message.Subject}",
            $"<p style=\"padding:8px 12px;background:#FFF6EA;border:1px solid #F8DDB4;border-radius:6px;font-family:sans-serif\"><strong>TEST MODE</strong>: meant for {WebUtility.HtmlEncode(intended)}. Redirected to the Resend test inbox because the sender is not on a verified domain.</p>{message.HtmlBody}",
            $"{banner}\n\n{message.TextBody}"));
    }

    private static string? TryAddress(string from)
    {
        try { return new MailAddress(from).Address; }
        catch (FormatException) { return null; }
    }
}
