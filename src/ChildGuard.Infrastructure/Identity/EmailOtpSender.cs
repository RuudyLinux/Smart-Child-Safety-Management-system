using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Identity;

/// <summary>
/// Delivers the login OTP to the Guardian's registered email address through whichever
/// <see cref="IEmailSender"/> is configured (Resend, SMTP, ...). A failed delivery is returned to the caller,
/// which tells the guardian; the provider's reason is logged, never the code.
/// </summary>
public class EmailOtpSender : IOtpSender
{
    private readonly IEmailSender _email;
    private readonly OtpOptions _options;
    private readonly ILogger<EmailOtpSender> _logger;
    private readonly bool _allowDevCodeLogging;

    /// <param name="allowDevCodeLogging">True only in Development. Otherwise <see cref="OtpOptions.LogCodeOnDeliveryFailure"/> is ignored.</param>
    public EmailOtpSender(IEmailSender email, OtpOptions options, ILogger<EmailOtpSender> logger, bool allowDevCodeLogging)
    {
        _email = email;
        _options = options;
        _logger = logger;
        _allowDevCodeLogging = allowDevCodeLogging;
    }

    public async Task<Result> SendLoginOtpAsync(Guardian guardian, string code, TimeSpan validFor, CancellationToken ct = default)
    {
        var message = GuardianEmails.BuildLoginOtpEmail(guardian.FullName, guardian.Email, code, validFor);
        var result = await _email.SendAsync(message, ct);
        if (result.Succeeded) return result;

        _logger.LogError("Login OTP for guardian {GuardianId} could not be delivered: {Reason}", guardian.GuardianId, result.Error);

        // Development only, off unless asked for: lets a local flow finish while the provider is in test mode.
        if (_allowDevCodeLogging && _options.LogCodeOnDeliveryFailure)
            _logger.LogWarning("Otp:LogCodeOnDeliveryFailure (development only): login OTP for guardian {GuardianId} is {Code}", guardian.GuardianId, code);

        return result;
    }
}
