using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>The OTP-once-per-phone flow over real HTTP: JSON shapes, revocation and the database contents.</summary>
public class TrustedDeviceApiTests : IClassFixture<ApiFactory>
{
    private const string Password = "Chosen@2026";
    private readonly ApiFactory _api;
    public TrustedDeviceApiTests(ApiFactory api) => _api = api;

    private async Task<(int Id, string Contact)> NewGuardianAsync(bool firstLogin = false)
    {
        using var scope = _api.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();
        var contact = $"9{Random.Shared.NextInt64(100000000, 999999999)}";
        await using var db = _api.NewDb();
        var g = new Guardian
        {
            FullName = "Trusted Test", Email = $"t.{Guid.NewGuid():N}@childguard.test", Phone = contact,
            PasswordHash = hasher.Hash(Password), PasswordResetRequired = firstLogin
        };
        db.Add(g);
        await db.SaveChangesAsync();
        return (g.GuardianId, contact);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private Task<HttpResponseMessage> LoginAsync(HttpClient c, string contact, string? deviceToken, string password = Password)
    {
        // Built by hand so the "no deviceToken property at all" shape of older app builds is covered too.
        var body = deviceToken is null
            ? (object)new { contactNumber = contact, password }
            : new { contactNumber = contact, password, deviceToken };
        return c.PostAsJsonAsync("/api/auth/guardian/login", body);
    }

    /// <summary>Password, emailed code, verify with trust: returns the session token and the device secret.</summary>
    private async Task<(string Jwt, string? Device)> OtpLoginAsync(HttpClient c, string contact, bool trust = true)
    {
        var login = await LoginAsync(c, contact, null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var challenge = await JsonAsync(login);
        var verify = await c.PostAsJsonAsync("/api/auth/guardian/verify-otp", new
        {
            challengeToken = challenge.GetProperty("challengeToken").GetString(),
            code = _api.Otp.LastCode,
            trustDevice = trust,
            deviceLabel = "Test phone"
        });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var body = await JsonAsync(verify);
        return (body.GetProperty("session").GetProperty("token").GetString()!,
            body.TryGetProperty("deviceToken", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null);
    }

    [Fact]
    public async Task FirstSignInNeedsOtp_ThenTheSamePhoneNeedsOnlyThePassword()
    {
        var (_, contact) = await NewGuardianAsync();
        var c = _api.CreateClient();
        var (_, device) = await OtpLoginAsync(c, contact);
        Assert.False(string.IsNullOrEmpty(device));
        var codesBefore = _api.Otp.Sent.Count;

        var second = await LoginAsync(c, contact, device);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await JsonAsync(second);
        Assert.Equal("Authenticated", body.GetProperty("stage").GetString());
        Assert.False(body.TryGetProperty("challengeToken", out _));
        Assert.Equal(codesBefore, _api.Otp.Sent.Count);

        // The session it returns is a real one.
        var me = _api.CreateClient();
        me.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("session").GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await me.GetAsync("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task OtpChallengeResponse_KeepsItsOriginalShape_ForPhonesWithoutATokenOrWithABadOne()
    {
        var (_, contact) = await NewGuardianAsync();
        var c = _api.CreateClient();

        foreach (var token in new string?[] { null, "garbage-token" })
        {
            var body = await JsonAsync(await LoginAsync(c, contact, token));
            Assert.Equal("OtpRequired", body.GetProperty("stage").GetString());
            Assert.True(body.TryGetProperty("challengeToken", out _));
            Assert.True(body.TryGetProperty("codeLength", out _));
            Assert.False(body.TryGetProperty("session", out _));
        }
    }

    [Fact]
    public async Task WrongPassword_IsRefused_EvenWithAValidDeviceToken()
    {
        var (_, contact) = await NewGuardianAsync();
        var c = _api.CreateClient();
        var (_, device) = await OtpLoginAsync(c, contact);

        var r = await LoginAsync(c, contact, device, password: "Nope1234");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task DatabaseHoldsOnlyAHash_NeverTheDeviceSecret()
    {
        var (id, contact) = await NewGuardianAsync();
        var (_, device) = await OtpLoginAsync(_api.CreateClient(), contact);

        await using var db = _api.NewDb();
        var row = await db.GuardianTrustedDevices.SingleAsync(d => d.GuardianId == id);
        Assert.NotEqual(device, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        Assert.Equal("Test phone", row.Label);
        Assert.True(row.ExpiresAt > _api.Time.UtcNow.AddDays(80));
    }

    [Fact]
    public async Task ChangingThePassword_MakesEveryPhoneNeedTheOtpAgain()
    {
        var (_, contact) = await NewGuardianAsync();
        var c = _api.CreateClient();
        var (jwt, device) = await OtpLoginAsync(c, contact);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var change = await c.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = "Another@2026" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var anonymous = _api.CreateClient();
        var next = await JsonAsync(await LoginAsync(anonymous, contact, device, password: "Another@2026"));
        Assert.Equal("OtpRequired", next.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task AdminIssuedPassword_RevokesTrust()
    {
        var admin = await _api.AdminClientAsync();
        var (id, contact) = await NewGuardianAsync();
        var (_, device) = await OtpLoginAsync(_api.CreateClient(), contact);

        var r = await admin.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/guardians/{id}/credentials") { Content = JsonContent.Create(new { newPassword = "Issued@2026" }) });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var next = await JsonAsync(await LoginAsync(_api.CreateClient(), contact, device, password: "Issued@2026"));
        Assert.Equal("OtpRequired", next.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task GuardianCanListAndRevokeTheirTrustedPhones()
    {
        var (_, contact) = await NewGuardianAsync();
        var (jwt, device) = await OtpLoginAsync(_api.CreateClient(), contact);
        var me = _api.CreateClient();
        me.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var list = await JsonAsync(await me.GetAsync("/api/me/trusted-devices"));
        Assert.Equal(1, list.GetArrayLength());
        var raw = list.ToString();
        Assert.DoesNotContain(device!, raw);
        Assert.DoesNotContain("tokenHash", raw, StringComparison.OrdinalIgnoreCase);

        var revoke = await me.DeleteAsync("/api/me/trusted-devices");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(0, (await JsonAsync(await me.GetAsync("/api/me/trusted-devices"))).GetArrayLength());

        var next = await JsonAsync(await LoginAsync(_api.CreateClient(), contact, device));
        Assert.Equal("OtpRequired", next.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task APhoneAlreadySignedIn_CanEnrolWithoutAnotherOtp_AndOnlyForItself()
    {
        var (id, contact) = await NewGuardianAsync();
        var me = _api.GuardianClient(id);

        var enrol = await me.PostAsJsonAsync("/api/me/trusted-devices", new { label = "Old install" });
        Assert.Equal(HttpStatusCode.OK, enrol.StatusCode);
        var device = (await JsonAsync(enrol)).GetProperty("deviceToken").GetString();
        var codes = _api.Otp.Sent.Count;

        var next = await JsonAsync(await LoginAsync(_api.CreateClient(), contact, device));
        Assert.Equal("Authenticated", next.GetProperty("stage").GetString());
        Assert.Equal(codes, _api.Otp.Sent.Count);

        // Another guardian cannot revoke someone else's device by id.
        var (otherId, _) = await NewGuardianAsync();
        await using var db = _api.NewDb();
        var deviceId = (await db.GuardianTrustedDevices.SingleAsync(d => d.GuardianId == id)).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GuardianClient(otherId).DeleteAsync($"/api/me/trusted-devices/{deviceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await _api.AdminClientAsync()).GetAsync("/api/me/trusted-devices")).StatusCode);
    }

    [Fact]
    public async Task FirstLoginGuardian_OnATrustedPhone_IsStillSentToThePasswordReset()
    {
        var (_, contact) = await NewGuardianAsync(firstLogin: true);
        var c = _api.CreateClient();
        var login = await JsonAsync(await LoginAsync(c, contact, null));
        var verify = await JsonAsync(await c.PostAsJsonAsync("/api/auth/guardian/verify-otp", new
        {
            challengeToken = login.GetProperty("challengeToken").GetString(), code = _api.Otp.LastCode, trustDevice = true
        }));
        Assert.Equal("PasswordResetRequired", verify.GetProperty("stage").GetString());
        var device = verify.GetProperty("deviceToken").GetString();

        // The same phone signs in again before resetting: still no session, only another reset ticket.
        var again = await JsonAsync(await LoginAsync(c, contact, device));
        Assert.Equal("PasswordResetRequired", again.GetProperty("stage").GetString());
        Assert.False(again.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task OtpDeliveryFailure_StillWorksAsBefore_ForUntrustedPhones()
    {
        var (_, contact) = await NewGuardianAsync();
        _api.Otp.FailWith = "provider down";
        try
        {
            var r = await LoginAsync(_api.CreateClient(), contact, null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }
        finally { _api.Otp.FailWith = null; }
    }
}
