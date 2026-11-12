using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>OTP once per phone: trust is granted by a passed OTP, checked with the password, and lost on a password change.</summary>
public class TrustedDeviceTests
{
    private const string Contact = "9820011111";
    private const string Password = "Chosen@2026";

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class H
    {
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeOtpSender Otp { get; } = new();
        public Clock Time { get; } = new();
        public OtpOptions Options { get; } = new();
        public AuthService Auth { get; }

        public H()
        {
            Auth = new AuthService(new FakeAdminRepository(), Guardians, new FakePasswordHasher(), new FakeJwtTokenGenerator(),
                new FakePasswordResetNotifier(), Otp, Options, Time);
        }

        public Guardian Add(string contact = Contact, bool firstLogin = false)
        {
            var g = new Guardian
            {
                GuardianId = Guardians.Guardians.Count + 1,
                FullName = "Priya Patel",
                Email = $"g{Guardians.Guardians.Count + 1}@example.test",
                Phone = contact,
                PasswordHash = "hashed:" + Password,
                PasswordResetRequired = firstLogin,
                Status = RecordStatus.Active
            };
            Guardians.Guardians.Add(g);
            return g;
        }

        public async Task<GuardianSignInDto> SignInAsync(string? deviceToken = null, string password = Password, string contact = Contact) =>
            (await Auth.SignInGuardianAsync(new GuardianLoginRequest(contact, password, deviceToken))).Value!;

        /// <summary>Full OTP sign-in that asks to trust the phone; returns the device token.</summary>
        public async Task<(GuardianAuthResultDto Auth, string? Token)> OtpSignInAsync(bool trust = true, string contact = Contact)
        {
            var challenge = (await SignInAsync(contact: contact)).Challenge!;
            var result = await Auth.VerifyGuardianOtpAsync(new VerifyGuardianOtpRequest(challenge.ChallengeToken, Otp.LastCode!, trust, "Pixel"));
            return (result.Value!, result.Value!.DeviceToken);
        }
    }

    [Fact]
    public async Task Verify_WithTrustDevice_ReturnsSecretOnce_AndStoresOnlyItsHash()
    {
        var h = new H();
        h.Add();

        var (auth, token) = await h.OtpSignInAsync();

        Assert.Equal(GuardianAuthStage.Authenticated, auth.Stage);
        Assert.False(string.IsNullOrEmpty(token));
        var stored = Assert.Single(h.Guardians.TrustedDevices);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token!, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Equal("Pixel", stored.Label);
    }

    [Fact]
    public async Task Verify_WithoutTrustDevice_TrustsNothing()
    {
        var h = new H();
        h.Add();

        var (_, token) = await h.OtpSignInAsync(trust: false);

        Assert.Null(token);
        Assert.Empty(h.Guardians.TrustedDevices);
    }

    [Fact]
    public async Task TrustedPhone_SignsInWithPasswordOnly_NoOtpSent()
    {
        var h = new H();
        h.Add();
        var (_, token) = await h.OtpSignInAsync();
        var sentBefore = h.Otp.Sent.Count;

        var next = await h.SignInAsync(token);

        Assert.Null(next.Challenge);
        Assert.Equal(GuardianAuthStage.Authenticated, next.Auth!.Stage);
        Assert.NotNull(next.Auth.Session);
        Assert.Equal(sentBefore, h.Otp.Sent.Count);
    }

    [Fact]
    public async Task TrustedPhone_StillNeedsTheCorrectPassword()
    {
        var h = new H();
        h.Add();
        var (_, token) = await h.OtpSignInAsync();

        var result = await h.Auth.SignInGuardianAsync(new GuardianLoginRequest(Contact, "WrongPass1", token));

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid contact number or password.", result.Error);
    }

    [Fact]
    public async Task UnknownOrBlankToken_FallsBackToOtp()
    {
        var h = new H();
        h.Add();

        var unknown = await h.SignInAsync("not-a-real-token");
        var blank = await h.SignInAsync("  ");

        Assert.NotNull(unknown.Challenge);
        Assert.NotNull(blank.Challenge);
        Assert.Equal(2, h.Otp.Sent.Count);
    }

    [Fact]
    public async Task OneGuardiansToken_DoesNotWorkForAnother()
    {
        var h = new H();
        h.Add();
        h.Add(contact: "9820022222");
        var (_, token) = await h.OtpSignInAsync();

        var other = await h.SignInAsync(token, contact: "9820022222");

        Assert.NotNull(other.Challenge);
        Assert.Null(other.Auth);
    }

    [Fact]
    public async Task ChangingPassword_RevokesTrust()
    {
        var h = new H();
        var g = h.Add();
        var (_, token) = await h.OtpSignInAsync();

        var changed = await h.Auth.ChangePasswordAsync(g.GuardianId, AppRole.Guardian, new ChangePasswordRequest(Password, "Another@2026"));
        g.PasswordHash = "hashed:Another@2026";
        var next = await h.SignInAsync(token, password: "Another@2026");

        Assert.True(changed.Succeeded);
        Assert.NotNull(h.Guardians.TrustedDevices.Single().RevokedAt);
        Assert.NotNull(next.Challenge);
    }

    [Fact]
    public async Task RevokeAll_StopsTheShortcut()
    {
        var h = new H();
        var g = h.Add();
        var (_, token) = await h.OtpSignInAsync();

        var count = await h.Auth.RevokeTrustedDevicesAsync(g.GuardianId, null);
        var next = await h.SignInAsync(token);

        Assert.Equal(1, count);
        Assert.NotNull(next.Challenge);
    }

    [Fact]
    public async Task Trust_LapsesAfterTheConfiguredDays_AndSlidesWithUse()
    {
        var h = new H();
        h.Options.TrustedDeviceDays = 30;
        h.Add();
        var (_, token) = await h.OtpSignInAsync();

        h.Time.Advance(TimeSpan.FromDays(25));
        Assert.NotNull((await h.SignInAsync(token)).Auth);          // used on day 25: window slides to day 55
        h.Time.Advance(TimeSpan.FromDays(25));
        Assert.NotNull((await h.SignInAsync(token)).Auth);          // day 50: still trusted
        h.Time.Advance(TimeSpan.FromDays(31));
        Assert.NotNull((await h.SignInAsync(token)).Challenge);     // idle 31 days: OTP again
    }

    [Fact]
    public async Task FeatureOff_NeverTrusts_AndIgnoresTokens()
    {
        var h = new H();
        h.Options.TrustedDeviceDays = 0;
        h.Add();

        var (_, token) = await h.OtpSignInAsync();
        var next = await h.SignInAsync("anything");

        Assert.Null(token);
        Assert.Empty(h.Guardians.TrustedDevices);
        Assert.NotNull(next.Challenge);
        Assert.False((await h.Auth.EnrollTrustedDeviceAsync(1, "x")).Succeeded);
    }

    [Fact]
    public async Task FirstLoginGuardian_OnTrustedPhone_StillGetsResetTicketNotSession()
    {
        var h = new H();
        var g = h.Add(firstLogin: true);
        var (first, token) = await h.OtpSignInAsync();
        Assert.Equal(GuardianAuthStage.PasswordResetRequired, first.Stage);
        Assert.NotNull(token);

        // The forced reset itself must not throw the new trust away.
        var reset = await h.Auth.ResetGuardianPasswordAsync(new GuardianResetPasswordRequest(first.ResetTicket!, "Fresh@2026", "Fresh@2026"));
        g.PasswordHash = "hashed:Fresh@2026";
        Assert.True(reset.Succeeded);
        var next = await h.SignInAsync(token, password: "Fresh@2026");
        Assert.Equal(GuardianAuthStage.Authenticated, next.Auth!.Stage);

        // But an admin re-issuing a default password does revoke it and forces the reset again.
        g.PasswordResetRequired = true;
        var again = await h.SignInAsync(token, password: "Fresh@2026");
        Assert.Equal(GuardianAuthStage.PasswordResetRequired, again.Auth!.Stage);
        Assert.Null(again.Auth.Session);
    }

    [Fact]
    public async Task LegacyLoginContract_AlwaysReturnsAChallenge_EvenWithAToken()
    {
        var h = new H();
        h.Add();
        var (_, token) = await h.OtpSignInAsync();

        var result = await h.Auth.LoginGuardianAsync(new GuardianLoginRequest(Contact, Password, token));

        Assert.True(result.Succeeded);
        Assert.Equal(GuardianAuthStage.OtpRequired, result.Value!.Stage);
    }

    [Fact]
    public async Task Enroll_ForASignedInPhone_WorksWithoutAnotherOtp()
    {
        var h = new H();
        var g = h.Add();

        var enrolled = await h.Auth.EnrollTrustedDeviceAsync(g.GuardianId, "Android");
        var next = await h.SignInAsync(enrolled.Value);

        Assert.True(enrolled.Succeeded);
        Assert.NotNull(next.Auth);
        Assert.Empty(h.Otp.Sent);
        Assert.Single(await h.Auth.ListTrustedDevicesAsync(g.GuardianId));
    }
}
