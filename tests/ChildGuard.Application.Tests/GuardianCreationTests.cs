using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>
/// Admin-created Guardian: fixed initial password, hashed at rest, flagged for the
/// mandatory first-login reset, credentials emailed to the Guardian's own address.
/// </summary>
public class GuardianCreationTests
{
    private sealed class Harness
    {
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeEmailSender Email { get; } = new();
        public GuardianService Service { get; }

        public Harness() => Service = new GuardianService(Guardians, new FakePasswordHasher(), Email, new FakePhotoRepository(), new FakeSafetyReadService(), new FakeVerificationRepository());

        public Task<Result<CreateGuardianResultDto>> CreateAsync(
            string fullName = "Priya Patel",
            string email = "priya.patel@example.test",
            string contactNumber = "9820011111") =>
            Service.CreateAsync(new CreateGuardianRequest(fullName, email, contactNumber));
    }

    // 1. Creation stores a hashed initial password and flags the forced reset.
    [Fact]
    public async Task Create_StoresHashedInitialPasswordAndFlagsReset()
    {
        var h = new Harness();

        var result = await h.CreateAsync();

        Assert.True(result.Succeeded);
        var guardian = Assert.Single(h.Guardians.Guardians);
        Assert.True(guardian.PasswordResetRequired);
        Assert.Equal(RecordStatus.Active, guardian.Status);
        // Never plaintext, and it is the one configured initial password.
        Assert.NotEqual(GuardianCredentialDefaults.InitialPassword, guardian.PasswordHash);
        Assert.Equal("hashed:" + GuardianCredentialDefaults.InitialPassword, guardian.PasswordHash);
    }

    [Fact]
    public async Task Create_NormalizesContactNumber()
    {
        var h = new Harness();

        await h.CreateAsync(contactNumber: "+91 98200-11111");

        Assert.Equal("+919820011111", h.Guardians.Guardians[0].Phone);
    }

    // 1 (cont). The credential email goes to the Guardian, not the admin.
    [Fact]
    public async Task Create_SendsCredentialEmailToTheGuardiansOwnAddress()
    {
        var h = new Harness();

        var result = await h.CreateAsync(email: "priya.patel@example.test");

        Assert.True(result.Value!.CredentialEmailSent);
        var message = Assert.Single(h.Email.Sent);
        Assert.Equal("priya.patel@example.test", message.ToEmail);
    }

    // 2. Email contents: name, contact number, initial password, first-login instructions.
    [Fact]
    public async Task CredentialEmail_ContainsNameContactNumberPasswordAndInstructions()
    {
        var h = new Harness();
        await h.CreateAsync(fullName: "Priya Patel", contactNumber: "9820011111");

        var message = h.Email.Last!;
        foreach (var body in new[] { message.HtmlBody, message.TextBody })
        {
            Assert.Contains("Priya Patel", body);
            Assert.Contains("9820011111", body);
            Assert.Contains(GuardianCredentialDefaults.InitialPassword, body);
        }

        Assert.Contains("one-time password", message.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set your own password", message.TextBody, StringComparison.OrdinalIgnoreCase);
        // Both formats are present, so clients without HTML still get the details.
        Assert.False(string.IsNullOrWhiteSpace(message.HtmlBody));
        Assert.False(string.IsNullOrWhiteSpace(message.TextBody));
        Assert.Contains("<html", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", message.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginOtpEmail_ContainsCodeAndValidity()
    {
        var message = GuardianEmails.BuildLoginOtpEmail("Priya Patel", "priya@example.test", "123456", TimeSpan.FromMinutes(5));

        Assert.Contains("123456", message.TextBody);
        Assert.Contains("123456", message.HtmlBody);
        Assert.Contains("5 minute", message.TextBody);
        Assert.Equal("priya@example.test", message.ToEmail);
    }

    // 9. A delivery failure is reported to the admin, not swallowed — and the account still exists.
    [Fact]
    public async Task Create_WhenEmailFails_ReportsFailureAndKeepsAccount()
    {
        var h = new Harness();
        h.Email.FailWith = "Email provider is unavailable.";

        var result = await h.CreateAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.CredentialEmailSent);
        Assert.Equal("Email provider is unavailable.", result.Value.EmailError);
        Assert.Single(h.Guardians.Guardians);
        Assert.True(h.Guardians.Guardians[0].PasswordResetRequired);
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_IsRejected()
    {
        var h = new Harness();
        await h.CreateAsync(email: "dup@example.test", contactNumber: "9820011111");

        var second = await h.CreateAsync(email: "dup@example.test", contactNumber: "9820022222");

        Assert.False(second.Succeeded);
        Assert.Equal("Email already registered.", second.Error);
    }

    [Fact]
    public async Task Create_WithDuplicateContactNumber_IsRejected()
    {
        var h = new Harness();
        await h.CreateAsync(email: "one@example.test", contactNumber: "9820011111");

        var second = await h.CreateAsync(email: "two@example.test", contactNumber: "98200 11111");

        Assert.False(second.Succeeded);
        Assert.Equal("Contact number already registered.", second.Error);
        Assert.Single(h.Guardians.Guardians);
    }

    // "If Admin resets an existing Guardian password, set PasswordResetRequired = true".
    [Fact]
    public async Task AdminIssuingAPassword_FlagsTheAccountForReset()
    {
        var h = new Harness();
        await h.CreateAsync();
        var guardian = h.Guardians.Guardians[0];
        guardian.PasswordResetRequired = false;

        var result = await h.Service.AdminUpdateCredentialsAsync(
            guardian.GuardianId, new AdminUpdateGuardianCredentialsRequest("Reissued@2026"));

        Assert.True(result.Succeeded);
        Assert.True(guardian.PasswordResetRequired);
        Assert.Equal("hashed:Reissued@2026", guardian.PasswordHash);
    }

    // Editing details is a separate action and must not touch the password or its flag.
    [Fact]
    public async Task AdminEdit_UpdatesEveryDetailWithoutTouchingTheCredentials()
    {
        var h = new Harness();
        await h.CreateAsync();
        var guardian = h.Guardians.Guardians[0];
        guardian.PasswordResetRequired = false;
        var originalHash = guardian.PasswordHash;

        var result = await h.Service.AdminUpdateAsync(guardian.GuardianId,
            new AdminUpdateGuardianRequest("Priya S Patel", "changed@example.test", "+91 98200-99999", RecordStatus.Inactive));

        Assert.True(result.Succeeded);
        Assert.Equal("Priya S Patel", guardian.FullName);
        Assert.Equal("changed@example.test", guardian.Email);
        Assert.Equal("+919820099999", guardian.Phone);
        Assert.Equal(RecordStatus.Inactive, guardian.Status);
        Assert.Equal(originalHash, guardian.PasswordHash);
        Assert.False(guardian.PasswordResetRequired);
    }

    [Fact]
    public async Task AdminEdit_KeepingTheSameContactNumber_IsAllowed()
    {
        var h = new Harness();
        await h.CreateAsync(contactNumber: "9820011111");
        var guardian = h.Guardians.Guardians[0];

        var result = await h.Service.AdminUpdateAsync(guardian.GuardianId,
            new AdminUpdateGuardianRequest("Renamed", guardian.Email, "9820011111", RecordStatus.Active));

        Assert.True(result.Succeeded);
        Assert.Equal("Renamed", guardian.FullName);
    }

    [Fact]
    public async Task AdminEdit_TakingAnotherGuardiansContactNumber_IsRejected()
    {
        var h = new Harness();
        await h.CreateAsync(email: "one@example.test", contactNumber: "9820011111");
        await h.CreateAsync(email: "two@example.test", contactNumber: "9820022222");
        var second = h.Guardians.Guardians[1];

        var result = await h.Service.AdminUpdateAsync(second.GuardianId,
            new AdminUpdateGuardianRequest(second.FullName, second.Email, "9820011111", RecordStatus.Active));

        Assert.False(result.Succeeded);
        Assert.Equal("Contact number already in use by another guardian.", result.Error);
        Assert.Equal("9820022222", second.Phone);
    }

    [Fact]
    public async Task AdminEdit_TakingAnotherGuardiansEmail_IsRejected()
    {
        var h = new Harness();
        await h.CreateAsync(email: "one@example.test", contactNumber: "9820011111");
        await h.CreateAsync(email: "two@example.test", contactNumber: "9820022222");
        var second = h.Guardians.Guardians[1];

        var result = await h.Service.AdminUpdateAsync(second.GuardianId,
            new AdminUpdateGuardianRequest(second.FullName, "one@example.test", "9820022222", RecordStatus.Active));

        Assert.False(result.Succeeded);
        Assert.Equal("Email already in use by another guardian.", result.Error);
        Assert.Equal("two@example.test", second.Email);
    }
}
