using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Identity;

/// <summary>
/// Delivers the login OTP to the Guardian's registered email address. No SMS
/// gateway is part of the approved stack, so email is the delivery channel until
/// one is chosen — swap this registration for an SMS implementation and the
/// contact number is already on the Guardian passed in.
/// </summary>
public class EmailOtpSender : IOtpSender
{
    private readonly IEmailSender _email;
    private readonly OtpOptions _options;
    private readonly ILogger<EmailOtpSender> _logger;

    public EmailOtpSender(IEmailSender email, OtpOptions options, ILogger<EmailOtpSender> logger)
    {
        _email = email;
        _options = options;
        _logger = logger;
    }

    public async Task SendLoginOtpAsync(Guardian guardian, string code, TimeSpan validFor, CancellationToken ct = default)
    {
        var message = GuardianEmails.BuildLoginOtpEmail(guardian.FullName, guardian.Email, code, validFor);
        var result = await _email.SendAsync(message, ct);
        if (!result.Succeeded)
        {
            _logger.LogError("Login OTP for guardian {GuardianId} could not be delivered: {Reason}",
                guardian.GuardianId, result.Error);

            // Off by default; only a development configuration turns this on, and the
            // failure above is still reported either way.
            if (_options.LogCodeOnDeliveryFailure)
            {
                _logger.LogWarning(
                    "Otp:LogCodeOnDeliveryFailure is enabled — login OTP for {ContactNumber} (dev-only log): {Code}",
                    guardian.Phone, code);
            }
        }
    }
}
