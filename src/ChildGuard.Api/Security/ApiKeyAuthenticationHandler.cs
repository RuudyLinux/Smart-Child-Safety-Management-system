using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChildGuard.Api.Security;

/// <summary>
/// Lets a physical RFID scanner device call the scan/heartbeat endpoints directly
/// without an Admin login, by presenting a shared secret in the X-Device-Key header.
/// Configured via Rfid:DeviceApiKey (config/env — never hardcoded). This is additive:
/// the existing Admin-JWT path (Admin Web's browser-based keyboard-wedge demo) keeps
/// working unchanged; this only opens a second, device-shaped path for rule 22
/// ("additional RFID scanners can be added later").
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DeviceKey";
    private const string HeaderName = "X-Device-Key";

    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = _configuration["Rfid:DeviceApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(HeaderName, out var provided) ||
            string.IsNullOrWhiteSpace(provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!CryptographicEquals(provided.ToString(), configuredKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid device key."));

        var claims = new[] { new Claim(ClaimTypes.Name, "rfid-scanner-device"), new Claim("scanner", "device") };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
