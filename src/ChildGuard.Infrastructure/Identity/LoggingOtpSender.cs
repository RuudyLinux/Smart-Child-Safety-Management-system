using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Identity;

/// <summary>
/// Last-resort OTP delivery: logs the code instead of sending it, used only when
/// no email provider is configured either. <see cref="EmailOtpSender"/> is the
/// registration when one is — see README "OTP delivery".
/// </summary>
public class LoggingOtpSender : IOtpSender
{
    private readonly ILogger<LoggingOtpSender> _logger;
    public LoggingOtpSender(ILogger<LoggingOtpSender> logger) => _logger = logger;

    public Task<Application.Common.Result> SendLoginOtpAsync(Domain.Entities.Guardian guardian, string code, TimeSpan validFor, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Login OTP for guardian {GuardianId} (dev-only log): {Code}. Valid for {Minutes} minute(s).",
            guardian.GuardianId, code, validFor.TotalMinutes);
        return Task.FromResult(Application.Common.Result.Success());
    }
}
