using System.Net.Http.Json;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Notifications;

file record ResendEmailRequest(string from, string[] to, string subject, string html, string text);
file record ResendEmailResponse(string? id);
file record ResendErrorResponse(string? message, string? name);

/// <summary>
/// Transactional email over the Resend HTTP API. The API key and From address come
/// from configuration (<c>Email:Resend:ApiKey</c>, <c>Email:From</c>) — never
/// hardcoded — and the key is attached by the HttpClient registration in
/// DependencyInjection, so it is not visible here or in any log line.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly string _from;
    private readonly string? _testRecipient;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, EmailOptions options, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _from = options.From;
        _testRecipient = options.Resend.TestRecipient;
        _logger = logger;
    }

    public async Task<Result> SendAsync(EmailMessage original, CancellationToken ct = default)
    {
        // While the sender is Resend's testing address, only the test recipient may be mailed.
        var policy = ResendSandbox.Apply(original, _from, _testRecipient);
        if (!policy.Succeeded)
        {
            _logger.LogWarning("Email \"{Subject}\" to {Recipient} was not sent: {Reason}", original.Subject, original.ToEmail, policy.Error);
            return Result.Failure(policy.Error!);
        }
        var message = policy.Value!;
        if (!string.Equals(message.ToEmail, original.ToEmail, StringComparison.OrdinalIgnoreCase))
            _logger.LogInformation("Test mode: email for {Intended} is being sent to the test recipient instead.", original.ToEmail);

        try
        {
            var payload = new ResendEmailRequest(
                _from, [message.ToEmail], message.Subject, message.HtmlBody, message.TextBody);

            var response = await _http.PostAsJsonAsync("/emails", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var reason = await ExtractErrorAsync(response, ct);
                // Recipient and reason only — never the body, which can carry credentials.
                _logger.LogError("Email delivery to {Recipient} failed ({Status}): {Reason}",
                    message.ToEmail, (int)response.StatusCode, reason);
                return Result.Failure($"Email could not be delivered: {reason}");
            }

            var body = await response.Content.ReadFromJsonAsync<ResendEmailResponse>(cancellationToken: ct);
            _logger.LogInformation("Email {Subject} delivered to {Recipient} (provider id {Id}).",
                message.Subject, message.ToEmail, body?.id ?? "unknown");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email delivery to {Recipient} threw.", message.ToEmail);
            return Result.Failure("Email provider is unavailable.");
        }
    }

    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ResendErrorResponse>(cancellationToken: ct);
            return error?.message ?? error?.name ?? response.ReasonPhrase ?? "unknown error";
        }
        catch
        {
            return response.ReasonPhrase ?? "unknown error";
        }
    }
}
