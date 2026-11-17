using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Authentication, JWT validation and role-based authorization through the real pipeline.</summary>
public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public AuthTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task AdminLogin_WithCorrectCredentials_ReturnsTokenThatIsAccepted()
    {
        var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminLogin_WithWrongPassword_IsUnauthorized()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/admin/login",
            new LoginRequest(ApiFactory.AdminEmail, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/students")]
    [InlineData("/api/reports/journey/events")]
    [InlineData("/api/rfid/devices")]
    public async Task AdminEndpoints_WithoutToken_AreUnauthorized(string url)
    {
        var response = await _api.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_SignedWithAnotherKey_IsRejected()
    {
        var response = await WithToken(MintAdminToken(signingKey: new string('x', 64))).GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_Expired_IsRejected()
    {
        var response = await WithToken(MintAdminToken(expires: DateTime.UtcNow.AddMinutes(-5))).GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_WithWrongAudience_IsRejected()
    {
        var response = await WithToken(MintAdminToken(audience: "SomeoneElse")).GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_Valid_IsAcceptedRegressionForSubClaimMapping()
    {
        // 995e56c read only "sub", which JwtBearer remaps to NameIdentifier — every
        // token was refused. A correctly signed token must be accepted.
        var response = await WithToken(MintAdminToken()).GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GuardianToken_OnAdminEndpoint_IsForbidden()
    {
        var child = await _api.NewChildAsync();

        var response = await _api.GuardianClient(child.GuardianId).GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminToken_OnGuardianEndpoint_IsForbidden()
    {
        var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync("/api/me/profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GuardianToken_StopsWorkingOnceGuardianIsDeactivated()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync("/api/me/profile")).StatusCode);

        await using (var db = _api.NewDb())
        {
            var g = await db.Guardians.SingleAsync(x => x.GuardianId == child.GuardianId);
            g.Status = RecordStatus.Inactive;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await guardian.GetAsync("/api/me/profile")).StatusCode);
    }

    private HttpClient WithToken(string token)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string MintAdminToken(string? signingKey = null, DateTime? expires = null, string audience = "ChildGuardClients")
    {
        using var db = _api.NewDb();
        var adminId = db.Admins.Single(a => a.Email == ApiFactory.AdminEmail).AdminId;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? _api.JwtSecret));
        var exp = expires ?? DateTime.UtcNow.AddMinutes(30);
        var token = new JwtSecurityToken(
            issuer: "ChildGuard", audience: audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, adminId.ToString()),
                new Claim(ClaimTypes.Role, nameof(AppRole.Admin))
            },
            notBefore: exp.AddHours(-1), expires: exp,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
