using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>
/// Covers the Guardian sign-in activity diagram: credentials, OTP, the forced
/// first-login password reset, and the subsequent normal login.
/// </summary>
public class GuardianAuthFlowTests
{
    private const string ContactNumber = "9820011111";
    private const string DefaultPassword = "Guardian@123";
    private const string NewPassword = "Chosen@2026";

    /// <summary>TimeProvider with a movable clock so OTP expiry and resend windows are testable.</summary>
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
        public FakeOtpSender OtpSender { get; } = new();
        public FakePasswordResetNotifier ResetNotifier { get; } = new();
        public TestClock Clock { get; } = new();
        public OtpOptions Options { get; } = new();
        public AuthService Service { get; }

        public Harness()
        {
            Service = new AuthService(
                Admins, Guardians, new FakePasswordHasher(), new FakeJwtTokenGenerator(),
                ResetNotifier, OtpSender, Options, Clock);
        }

        public Guardian AddGuardian(string password = DefaultPassword, bool passwordResetRequired = true,
            string contactNumber = ContactNumber, RecordStatus status = RecordStatus.Active)
        {
            var guardian = new Guardian
            {
                GuardianId = Guardians.Guardians.Count + 1,
                FullName = "Priya Patel",
                Email = $"guardian{Guardians.Guardians.Count + 1}@example.test",
                Phone = contactNumber,
                PasswordHash = "hashed:" + password,
                PasswordResetRequired = passwordResetRequired,
                Status = status
            };
            Guardians.Guardians.Add(guardian);
            return guardian;
        }

        public Task<Result<GuardianOtpChallengeDto>> LoginAsync(string password, string contactNumber = ContactNumber) =>
            Service.LoginGuardianAsync(new GuardianLoginRequest(contactNumber, password));

        public Task<Result<GuardianAuthResultDto>> VerifyAsync(Guid challengeToken, string code) =>
            Service.VerifyGuardianOtpAsync(new VerifyGuardianOtpRequest(challengeToken, code));

        /// <summary>Drives a first-time Guardian to the point where the reset ticket has been issued.</summary>
        public async Task<string> FirstLoginToResetTicketAsync()
        {
            AddGuardian();
            var challenge = (await LoginAsync(DefaultPassword)).Value!;
            var verified = await VerifyAsync(challenge.ChallengeToken, OtpSender.LastCode!);
            return verified.Value!.ResetTicket!;
        }
    }

    // 1. First-time login with the correct default password raises an OTP challenge — and no session.
    [Fact]
    public async Task FirstTimeLogin_WithDefaultPassword_IssuesOtpChallengeWithoutToken()
    {
        var h = new Harness();
        h.AddGuardian();

        var result = await h.LoginAsync(DefaultPassword);

        Assert.True(result.Succeeded);
        Assert.Equal(GuardianAuthStage.OtpRequired, result.Value!.Stage);
        Assert.NotEqual(Guid.Empty, result.Value.ChallengeToken);
        // 3. An OTP was generated and sent to the registered contact number.
        Assert.Single(h.OtpSender.Sent);
        Assert.Equal(ContactNumber, h.OtpSender.Sent[0].ContactNumber);
        Assert.Equal(h.Options.CodeLength, h.OtpSender.LastCode!.Length);
    }

    // 2. Wrong password: no challenge, no OTP, and a message that does not reveal the account.
    [Fact]
    public async Task Login_WithIncorrectPassword_FailsWithoutSendingOtp()
    {
        var h = new Harness();
        h.AddGuardian();

        var result = await h.LoginAsync("WrongPass1");

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid contact number or password.", result.Error);
        Assert.Empty(h.OtpSender.Sent);
    }

    // 20 (account enumeration). An unknown contact number fails identically to a wrong password.
    [Fact]
    public async Task Login_WithUnknownContactNumber_ReturnsSameErrorAsWrongPassword()
    {
        var h = new Harness();
        h.AddGuardian();

        var unknown = await h.LoginAsync(DefaultPassword, contactNumber: "9999999999");
        var wrongPassword = await h.LoginAsync("WrongPass1");

        Assert.False(unknown.Succeeded);
        Assert.Equal(wrongPassword.Error, unknown.Error);
    }

    [Fact]
    public async Task Login_AcceptsFormattedContactNumber()
    {
        var h = new Harness();
        h.AddGuardian();

        var result = await h.LoginAsync(DefaultPassword, contactNumber: "98200 11111");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Login_ForInactiveGuardian_IsRejected()
    {
        var h = new Harness();
        h.AddGuardian(status: RecordStatus.Inactive);

        var result = await h.LoginAsync(DefaultPassword);

        Assert.False(result.Succeeded);
        Assert.Empty(h.OtpSender.Sent);
    }

    // 4. Invalid OTP does not authenticate.
    [Fact]
    public async Task VerifyOtp_WithWrongCode_Fails()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;

        var result = await h.VerifyAsync(challenge.ChallengeToken, "000000");

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid or expired OTP.", result.Error);
    }

    // 5. Expired OTP does not authenticate.
    [Fact]
    public async Task VerifyOtp_AfterExpiry_Fails()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var code = h.OtpSender.LastCode!;

        h.Clock.Advance(TimeSpan.FromMinutes(h.Options.ExpiryMinutes + 1));
        var result = await h.VerifyAsync(challenge.ChallengeToken, code);

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid or expired OTP.", result.Error);
    }

    // 6. Resend issues a fresh code, and the old one stops working.
    [Fact]
    public async Task ResendOtp_IssuesNewCodeAndInvalidatesPrevious()
    {
        var h = new Harness();
        h.AddGuardian(passwordResetRequired: false);
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var firstCode = h.OtpSender.LastCode!;

        h.Clock.Advance(TimeSpan.FromSeconds(h.Options.ResendIntervalSeconds));
        var resend = await h.Service.ResendGuardianOtpAsync(new ResendGuardianOtpRequest(challenge.ChallengeToken));
        var secondCode = h.OtpSender.LastCode!;

        Assert.True(resend.Succeeded);
        Assert.Equal(2, h.OtpSender.Sent.Count);
        Assert.NotEqual(firstCode, secondCode);
        Assert.False((await h.VerifyAsync(challenge.ChallengeToken, firstCode)).Succeeded);
        Assert.True((await h.VerifyAsync(challenge.ChallengeToken, secondCode)).Succeeded);
    }

    // Email delivery failures are surfaced to the guardian, never swallowed.
    [Fact]
    public async Task Login_WhenEmailDeliveryFails_ReturnsDeliveryMessage_AndLeavesNoUsableChallenge()
    {
        var h = new Harness();
        h.AddGuardian(passwordResetRequired: false);
        h.OtpSender.FailWith = "provider said no (secret detail)";

        var result = await h.LoginAsync(DefaultPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthService.OtpDeliveryFailedMessage, result.Error);
        Assert.DoesNotContain("secret detail", result.Error);
        Assert.All(h.Guardians.Challenges, c => Assert.True(c.IsConsumed));
    }

    [Fact]
    public async Task ResendOtp_WhenEmailDeliveryFails_RestoresPreviousCodeAndCounters()
    {
        var h = new Harness();
        h.AddGuardian(passwordResetRequired: false);
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var firstCode = h.OtpSender.LastCode!;
        var stored = h.Guardians.Challenges.Single();
        var (hash, expires, resends, sentAt) = (stored.CodeHash, stored.ExpiresAtUtc, stored.ResendCount, stored.LastSentAtUtc);

        h.Clock.Advance(TimeSpan.FromSeconds(h.Options.ResendIntervalSeconds));
        h.OtpSender.FailWith = "down";
        var resend = await h.Service.ResendGuardianOtpAsync(new ResendGuardianOtpRequest(challenge.ChallengeToken));

        Assert.False(resend.Succeeded);
        Assert.Equal(AuthService.OtpDeliveryFailedMessage, resend.Error);
        Assert.Equal(hash, stored.CodeHash);
        Assert.Equal(expires, stored.ExpiresAtUtc);
        Assert.Equal(resends, stored.ResendCount);
        Assert.Equal(sentAt, stored.LastSentAtUtc);

        h.OtpSender.FailWith = null;
        Assert.True((await h.VerifyAsync(challenge.ChallengeToken, firstCode)).Succeeded);
    }

    [Fact]
    public async Task ResendOtp_BeforeIntervalElapses_IsRejected()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;

        var resend = await h.Service.ResendGuardianOtpAsync(new ResendGuardianOtpRequest(challenge.ChallengeToken));

        Assert.False(resend.Succeeded);
        Assert.Single(h.OtpSender.Sent);
    }

    [Fact]
    public async Task ResendOtp_BeyondResendLimit_IsRejected()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;

        for (var i = 0; i < h.Options.MaxResends; i++)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(h.Options.ResendIntervalSeconds));
            Assert.True((await h.Service.ResendGuardianOtpAsync(new ResendGuardianOtpRequest(challenge.ChallengeToken))).Succeeded);
        }

        h.Clock.Advance(TimeSpan.FromSeconds(h.Options.ResendIntervalSeconds));
        var tooMany = await h.Service.ResendGuardianOtpAsync(new ResendGuardianOtpRequest(challenge.ChallengeToken));

        Assert.False(tooMany.Succeeded);
        Assert.Equal("Resend limit reached. Please sign in again.", tooMany.Error);
    }

    // 7. Too many wrong codes burns the challenge — the correct code no longer helps.
    [Fact]
    public async Task VerifyOtp_AfterMaxAttempts_BurnsChallenge()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var code = h.OtpSender.LastCode!;

        for (var i = 0; i < h.Options.MaxVerificationAttempts; i++)
            await h.VerifyAsync(challenge.ChallengeToken, "000000");

        var afterLockout = await h.VerifyAsync(challenge.ChallengeToken, code);

        Assert.False(afterLockout.Succeeded);
    }

    // 8 + 9. Correct OTP on a first-time login yields a reset ticket and no session.
    [Fact]
    public async Task VerifyOtp_FirstTimeLogin_ForcesPasswordResetWithoutIssuingToken()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;

        var result = await h.VerifyAsync(challenge.ChallengeToken, h.OtpSender.LastCode!);

        Assert.True(result.Succeeded);
        Assert.Equal(GuardianAuthStage.PasswordResetRequired, result.Value!.Stage);
        Assert.Null(result.Value.Session);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ResetTicket));
    }

    // 19. A verified OTP is single-use.
    [Fact]
    public async Task VerifyOtp_CannotBeReplayed()
    {
        var h = new Harness();
        h.AddGuardian(passwordResetRequired: false);
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var code = h.OtpSender.LastCode!;

        Assert.True((await h.VerifyAsync(challenge.ChallengeToken, code)).Succeeded);
        var replay = await h.VerifyAsync(challenge.ChallengeToken, code);

        Assert.False(replay.Succeeded);
    }

    // 18. The reset cannot be skipped: no ticket means no password change, and no token exists to use instead.
    [Fact]
    public async Task ResetPassword_WithoutValidTicket_IsRejected()
    {
        var h = new Harness();
        h.AddGuardian();

        var result = await h.Service.ResetGuardianPasswordAsync(
            new GuardianResetPasswordRequest("not-a-real-ticket", NewPassword, NewPassword));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_WithExpiredTicket_IsRejected()
    {
        var h = new Harness();
        h.AddGuardian();
        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var ticket = (await h.VerifyAsync(challenge.ChallengeToken, h.OtpSender.LastCode!)).Value!.ResetTicket!;

        h.Clock.Advance(TimeSpan.FromMinutes(h.Options.PasswordResetTicketMinutes + 1));
        var result = await h.Service.ResetGuardianPasswordAsync(
            new GuardianResetPasswordRequest(ticket, NewPassword, NewPassword));

        Assert.False(result.Succeeded);
    }

    // 10. Confirmation mismatch.
    [Fact]
    public async Task ResetPassword_WithMismatchedConfirmation_IsRejected()
    {
        var h = new Harness();
        var ticket = await h.FirstLoginToResetTicketAsync();

        var result = await h.Service.ResetGuardianPasswordAsync(
            new GuardianResetPasswordRequest(ticket, NewPassword, "Different@2026"));

        Assert.False(result.Succeeded);
        Assert.Equal("Passwords do not match.", result.Error);
        Assert.True(h.Guardians.Guardians[0].PasswordResetRequired);
    }

    // 11 (server-side rule). Reusing the default password is refused.
    [Fact]
    public async Task ResetPassword_ReusingDefaultPassword_IsRejected()
    {
        var h = new Harness();
        var ticket = await h.FirstLoginToResetTicketAsync();

        var result = await h.Service.ResetGuardianPasswordAsync(
            new GuardianResetPasswordRequest(ticket, DefaultPassword, DefaultPassword));

        Assert.False(result.Succeeded);
        Assert.Equal("New password must be different from your current password.", result.Error);
    }

    // 12 + 13 + 14 + 15 + 16. The full first-login journey through to a session.
    [Fact]
    public async Task FirstLoginJourney_EndsWithSessionOnSecondLoginUsingNewPassword()
    {
        var h = new Harness();
        var ticket = await h.FirstLoginToResetTicketAsync();

        var reset = await h.Service.ResetGuardianPasswordAsync(
            new GuardianResetPasswordRequest(ticket, NewPassword, NewPassword));
        Assert.True(reset.Succeeded);

        var guardian = h.Guardians.Guardians[0];
        Assert.False(guardian.PasswordResetRequired);
        Assert.Null(guardian.PasswordResetToken);

        // 13. The default password no longer works.
        Assert.False((await h.LoginAsync(DefaultPassword)).Succeeded);

        // 14 + 15. Sign in with the new password, verify OTP, get a session.
        var challenge = (await h.LoginAsync(NewPassword)).Value!;
        var authenticated = await h.VerifyAsync(challenge.ChallengeToken, h.OtpSender.LastCode!);

        Assert.True(authenticated.Succeeded);
        Assert.Equal(GuardianAuthStage.Authenticated, authenticated.Value!.Stage);
        Assert.NotNull(authenticated.Value.Session);
        Assert.Equal(nameof(AppRole.Guardian), authenticated.Value.Session!.Role);
    }

    // 17 (server side). Nothing in the first-login flow ever hands out a token before the reset,
    // so protected Guardian endpoints stay unreachable.
    [Fact]
    public async Task FirstTimeGuardian_NeverReceivesTokenBeforeResettingPassword()
    {
        var h = new Harness();
        h.AddGuardian();

        var challenge = (await h.LoginAsync(DefaultPassword)).Value!;
        var verified = await h.VerifyAsync(challenge.ChallengeToken, h.OtpSender.LastCode!);

        Assert.Null(verified.Value!.Session);
        // A second login attempt still yields no session.
        var retry = (await h.LoginAsync(DefaultPassword)).Value!;
        var retryVerified = await h.VerifyAsync(retry.ChallengeToken, h.OtpSender.LastCode!);
        Assert.Null(retryVerified.Value!.Session);
    }

    // Subsequent login: an already-reset Guardian goes straight to a session after OTP.
    [Fact]
    public async Task SubsequentLogin_AfterOtp_ReturnsSession()
    {
        var h = new Harness();
        h.AddGuardian(password: NewPassword, passwordResetRequired: false);

        var challenge = (await h.LoginAsync(NewPassword)).Value!;
        var result = await h.VerifyAsync(challenge.ChallengeToken, h.OtpSender.LastCode!);

        Assert.Equal(GuardianAuthStage.Authenticated, result.Value!.Stage);
        Assert.NotNull(result.Value.Session);
    }

    [Fact]
    public async Task Login_SupersedesPreviousOpenChallenge()
    {
        var h = new Harness();
        h.AddGuardian(passwordResetRequired: false);
        var first = (await h.LoginAsync(DefaultPassword)).Value!;
        var firstCode = h.OtpSender.LastCode!;

        var second = (await h.LoginAsync(DefaultPassword)).Value!;

        Assert.NotEqual(first.ChallengeToken, second.ChallengeToken);
        Assert.False((await h.VerifyAsync(first.ChallengeToken, firstCode)).Succeeded);
    }

    // 20 (other roles). Admin sign-in is untouched by the Guardian OTP flow.
    [Fact]
    public async Task AdminLogin_StillReturnsTokenDirectly()
    {
        var h = new Harness();
        h.Admins.Admins.Add(new Admin
        {
            AdminId = 1,
            FullName = "System Administrator",
            Email = "admin@example.test",
            PasswordHash = "hashed:Admin@123",
            Status = RecordStatus.Active
        });

        var result = await h.Service.LoginAdminAsync(new LoginRequest("admin@example.test", "Admin@123"));

        Assert.True(result.Succeeded);
        Assert.Equal(nameof(AppRole.Admin), result.Value!.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Token));
        Assert.Empty(h.OtpSender.Sent);
    }

    [Fact]
    public async Task GuardianForgotPasswordFlow_AlsoClearsForcedReset()
    {
        var h = new Harness();
        var guardian = h.AddGuardian();

        await h.Service.ForgotPasswordAsync(new ForgotPasswordRequest(guardian.Email, nameof(AppRole.Guardian)));
        var token = h.ResetNotifier.Sent[0].Token;
        var result = await h.Service.ResetPasswordAsync(
            new ResetPasswordRequest(guardian.Email, nameof(AppRole.Guardian), token, NewPassword));

        Assert.True(result.Succeeded);
        Assert.False(guardian.PasswordResetRequired);
    }
}
