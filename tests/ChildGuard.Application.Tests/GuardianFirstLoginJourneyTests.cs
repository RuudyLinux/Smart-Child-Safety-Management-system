using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>
/// End-to-end over the real services (only infrastructure is faked): an admin
/// creates a Guardian, and that Guardian goes from the emailed initial password
/// all the way to a dashboard session.
/// </summary>
public class GuardianFirstLoginJourneyTests
{
    private const string ContactNumber = "9820011111";
    private const string NewPassword = "Chosen@2026";

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class Harness
    {
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeAdminRepository Admins { get; } = new();
        public FakeEmailSender Email { get; } = new();
        public FakeOtpSender OtpSender { get; } = new();
        public TestClock Clock { get; } = new();
        public OtpOptions Options { get; } = new();
        public GuardianService GuardianService { get; }
        public AuthService Auth { get; }

        public Harness()
        {
            var hasher = new FakePasswordHasher();
            GuardianService = new GuardianService(Guardians, hasher, Email, new FakePhotoRepository(), new FakeSafetyReadService(), new FakeVerificationRepository());
            Auth = new AuthService(Admins, Guardians, hasher, new FakeJwtTokenGenerator(),
                new FakePasswordResetNotifier(), OtpSender, Options, Clock);
        }
    }

    // The starting password is random per guardian; read the one that was emailed
    // back from the fake hasher, which stores "hashed:" + plaintext.
    private static string InitialPassword(Harness h) => h.Guardians.Guardians[0].PasswordHash["hashed:".Length..];

    // 1 + 3 + 4 + 5 + 6 + 7: the whole journey, from admin creation to dashboard.
    [Fact]
    public async Task CreatedGuardian_CompletesFirstLoginAndThenSignsInNormally()
    {
        var h = new Harness();
        var created = await h.GuardianService.CreateAsync(
            new CreateGuardianRequest("Priya Patel", "priya.patel@example.test", ContactNumber));
        Assert.True(created.Succeeded);
        Assert.True(created.Value!.CredentialEmailSent);
        var initial = InitialPassword(h);

        // First login with the emailed initial password.
        var challenge = await h.Auth.LoginGuardianAsync(
            new GuardianLoginRequest(ContactNumber, initial));
        Assert.True(challenge.Succeeded);
        Assert.Equal(GuardianAuthStage.OtpRequired, challenge.Value!.Stage);

        // OTP verified: reset required, and no session yet.
        var verified = await h.Auth.VerifyGuardianOtpAsync(
            new VerifyGuardianOtpRequest(challenge.Value.ChallengeToken, h.OtpSender.LastCode!));
        Assert.Equal(GuardianAuthStage.PasswordResetRequired, verified.Value!.Stage);
        Assert.Null(verified.Value.Session);

        // Reset completes and clears the flag.
        var reset = await h.Auth.ResetGuardianPasswordAsync(
            new GuardianResetPasswordRequest(verified.Value.ResetTicket!, NewPassword, NewPassword));
        Assert.True(reset.Succeeded);
        Assert.False(h.Guardians.Guardians[0].PasswordResetRequired);

        // The initial password is dead.
        var withInitial = await h.Auth.LoginGuardianAsync(
            new GuardianLoginRequest(ContactNumber, initial));
        Assert.False(withInitial.Succeeded);

        // The new password plus OTP reaches the dashboard.
        var second = await h.Auth.LoginGuardianAsync(new GuardianLoginRequest(ContactNumber, NewPassword));
        var session = await h.Auth.VerifyGuardianOtpAsync(
            new VerifyGuardianOtpRequest(second.Value!.ChallengeToken, h.OtpSender.LastCode!));

        Assert.Equal(GuardianAuthStage.Authenticated, session.Value!.Stage);
        Assert.Equal(nameof(AppRole.Guardian), session.Value.Session!.Role);
    }

    // 7. A created Guardian cannot reach a session while the reset is outstanding,
    // no matter how many times the OTP step is repeated.
    [Fact]
    public async Task CreatedGuardian_CannotObtainSessionBeforeResetting()
    {
        var h = new Harness();
        await h.GuardianService.CreateAsync(
            new CreateGuardianRequest("Priya Patel", "priya.patel@example.test", ContactNumber));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var challenge = await h.Auth.LoginGuardianAsync(
                new GuardianLoginRequest(ContactNumber, InitialPassword(h)));
            var verified = await h.Auth.VerifyGuardianOtpAsync(
                new VerifyGuardianOtpRequest(challenge.Value!.ChallengeToken, h.OtpSender.LastCode!));
            Assert.Null(verified.Value!.Session);
        }
    }

    // 10. A pre-existing Guardian (flag already false) is unaffected: password plus OTP, straight in.
    [Fact]
    public async Task ExistingGuardian_SignsInWithCurrentPasswordAndOtp()
    {
        var h = new Harness();
        h.Guardians.Guardians.Add(new Domain.Entities.Guardian
        {
            GuardianId = 1,
            FullName = "Amit Shah",
            Email = "amit.shah@example.test",
            Phone = "9820022222",
            PasswordHash = "hashed:Existing@123",
            PasswordResetRequired = false,
            Status = RecordStatus.Active
        });

        var challenge = await h.Auth.LoginGuardianAsync(new GuardianLoginRequest("9820022222", "Existing@123"));
        var session = await h.Auth.VerifyGuardianOtpAsync(
            new VerifyGuardianOtpRequest(challenge.Value!.ChallengeToken, h.OtpSender.LastCode!));

        Assert.Equal(GuardianAuthStage.Authenticated, session.Value!.Stage);
        Assert.NotNull(session.Value.Session);
        // No credential email is sent for an account nobody created through the admin flow.
        Assert.Empty(h.Email.Sent);
    }

    // 8. Expiry and reuse are both refused on a created Guardian's challenge.
    [Fact]
    public async Task CreatedGuardian_ExpiredAndReusedOtpAreRejected()
    {
        var h = new Harness();
        await h.GuardianService.CreateAsync(
            new CreateGuardianRequest("Priya Patel", "priya.patel@example.test", ContactNumber));

        var expiredChallenge = await h.Auth.LoginGuardianAsync(
            new GuardianLoginRequest(ContactNumber, InitialPassword(h)));
        var expiredCode = h.OtpSender.LastCode!;
        h.Clock.Advance(TimeSpan.FromMinutes(h.Options.ExpiryMinutes + 1));
        Assert.False((await h.Auth.VerifyGuardianOtpAsync(
            new VerifyGuardianOtpRequest(expiredChallenge.Value!.ChallengeToken, expiredCode))).Succeeded);

        var challenge = await h.Auth.LoginGuardianAsync(
            new GuardianLoginRequest(ContactNumber, InitialPassword(h)));
        var code = h.OtpSender.LastCode!;
        Assert.True((await h.Auth.VerifyGuardianOtpAsync(
            new VerifyGuardianOtpRequest(challenge.Value!.ChallengeToken, code))).Succeeded);
        Assert.False((await h.Auth.VerifyGuardianOtpAsync(
            new VerifyGuardianOtpRequest(challenge.Value.ChallengeToken, code))).Succeeded);
    }
}
