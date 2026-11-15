using System.Security.Claims;
using System.Text.Encodings.Web;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ChildGuard.Api.Security;

/// <summary>
/// Lets a physical RFID scanner call the scan/heartbeat endpoints without an Admin
/// login by presenting its own API key in the X-Device-Key header.
///
/// Each scanner has an individual key (issued via POST /api/rfid/devices/{id}/credentials,
/// only its SHA-256 stored). The authenticated principal carries a "device_id" claim and
/// the controller only lets it act as that device, so one leaked key cannot impersonate
/// another scanner and can be revoked on its own.
///
/// Legacy: a single shared Rfid:DeviceApiKey is still honoured if configured, so
/// existing scanners keep working during migration, but it is logged as deprecated
/// on every use. Leave it empty once every scanner has its own key.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DeviceKey";
    public const string DeviceIdClaim = "device_id";
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

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var providedValues))
            return AuthenticateResult.NoResult();
        var provided = providedValues.ToString();
        if (string.IsNullOrWhiteSpace(provided))
            return AuthenticateResult.NoResult();

        if (provided.StartsWith(DeviceKeys.Prefix, StringComparison.Ordinal))
        {
            var repo = Context.RequestServices.GetRequiredService<IRfidRepository>();
            var device = await repo.GetDeviceByKeyHashAsync(DeviceKeys.Hash(provided), Context.RequestAborted);
            if (device is null)
                return AuthenticateResult.Fail("Invalid device key.");

            return Success(
                new Claim(ClaimTypes.Name, device.DeviceLabel),
                new Claim("scanner", "device"),
                new Claim(DeviceIdClaim, device.RfidDeviceId.ToString()));
        }

        var sharedKey = _configuration["Rfid:DeviceApiKey"];
        if (!string.IsNullOrWhiteSpace(sharedKey) && CryptographicEquals(provided, sharedKey))
        {
            Logger.LogWarning(
                "A scanner authenticated with the deprecated shared Rfid:DeviceApiKey. Issue it an individual key and clear the shared key.");
            return Success(new Claim(ClaimTypes.Name, "rfid-scanner-shared-key"), new Claim("scanner", "device"));
        }

        return AuthenticateResult.Fail("Invalid device key.");
    }

    private static AuthenticateResult Success(params Claim[] claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
