using System.Net;
using System.Net.Mail;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Notifications;

/// <summary>
/// Transactional email over plain SMTP (for example Gmail with an app password), selected with
/// <c>Email:Provider = Smtp</c>. Host, port and credentials come from configuration only and are never
/// logged. Uses the framework's SmtpClient (no extra package); it is fine for the low volume of login codes
/// but is not meant for bulk mail.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            using var mail = Build(message, _options.From);
            using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
            {
                EnableSsl = _options.Smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000
            };
            if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
                client.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);

            await client.SendMailAsync(mail, ct);
            _logger.LogInformation("Email {Subject} delivered to {Recipient} over SMTP.", message.Subject, message.ToEmail);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception text can name the server; it never contains the password. The message body is not logged.
            _logger.LogError("SMTP delivery to {Recipient} failed: {Reason}", message.ToEmail, ex.GetBaseException().Message);
            return Result.Failure("Email could not be delivered: the SMTP server refused or could not be reached.");
        }
    }

    /// <summary>Plain text and HTML alternatives in one message.</summary>
    public static MailMessage Build(EmailMessage message, string from)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(from),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(message.ToEmail, message.ToName));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));
        return mail;
    }
}
