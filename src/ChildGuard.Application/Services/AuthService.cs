using System.Globalization;
using System.Security.Cryptography;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class AuthService : IAuthService
{
    /// <summary>Same message for unknown account, wrong password and inactive account — never reveal which.</summary>
    private const string InvalidCredentials = "Invalid contact number or password.";

    /// <summary>Shown to the guardian when their code could not be emailed. The provider's reason is logged, not shown.</summary>
    public const string OtpDeliveryFailedMessage =
        "We couldn't send the verification code to your email right now. Please try again in a few minutes, or contact the school office.";
    private const string InvalidOtp = "Invalid or expired OTP.";
    private const string OtpAttemptsExceeded = "Too many incorrect attempts. Please sign in again to request a new code.";
    private const string ChallengeGone = "Your sign-in session has expired. Please sign in again.";

    /// <summary>Verified against when no account matches, so a missing account costs the same time as a wrong password.</summary>
    private readonly Lazy<string> _decoyHash;

    private readonly IAdminRepository _adminRepo;
    private readonly IGuardianRepository _guardianRepo;
    private readonly IPasswordHasherService _hasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IPasswordResetNotifier _resetNotifier;
    private readonly IOtpSender _otpSender;
    private readonly OtpOptions _otpOptions;
    private readonly TimeProvider _clock;

    public AuthService(
        IAdminRepository adminRepo,
        IGuardianRepository guardianRepo,
        IPasswordHasherService hasher,
        IJwtTokenGenerator jwt,
        IPasswordResetNotifier resetNotifier,
        IOtpSender otpSender,
        OtpOptions otpOptions,
        TimeProvider clock)
    {
        _adminRepo = adminRepo;
        _guardianRepo = guardianRepo;
        _hasher = hasher;
        _jwt = jwt;
        _resetNotifier = resetNotifier;
        _otpSender = otpSender;
        _otpOptions = otpOptions;
        _clock = clock;
        _decoyHash = new Lazy<string>(() => _hasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task<Result<LoginResponse>> LoginAdminAsync(LoginRequest request, CancellationToken ct = default)
    {
        var admin = await _adminRepo.GetByEmailAsync(request.Email, ct);
        if (admin is null || admin.Status != RecordStatus.Active || !_hasher.Verify(admin.PasswordHash, request.Password))
            return Result<LoginResponse>.Failure("Invalid email or password.");

        var (token, expires) = _jwt.GenerateToken(admin.AdminId, admin.Email, admin.FullName, AppRole.Admin);
        return Result<LoginResponse>.Success(new LoginResponse(token, expires, nameof(AppRole.Admin), admin.AdminId, admin.FullName, admin.Email));
    }

    public async Task<Result<GuardianOtpChallengeDto>> LoginGuardianAsync(GuardianLoginRequest request, CancellationToken ct = default)
    {
        // The original contract: always an OTP challenge, never a trusted-device shortcut.
        var result = await SignInGuardianAsync(request with { DeviceToken = null }, ct);
        return result.Succeeded
            ? Result<GuardianOtpChallengeDto>.Success(result.Value!.Challenge!)
            : Result<GuardianOtpChallengeDto>.Failure(result.Error!);
    }

    public async Task<Result<GuardianSignInDto>> SignInGuardianAsync(GuardianLoginRequest request, CancellationToken ct = default)
    {
        var contactNumber = ContactNumber.Normalize(request.ContactNumber);
        var guardian = contactNumber is null ? null : await _guardianRepo.GetByContactNumberAsync(contactNumber, ct);

        if (guardian is null || guardian.Status != RecordStatus.Active)
        {
            _hasher.Verify(_decoyHash.Value, request.Password);
            return Result<GuardianSignInDto>.Failure(InvalidCredentials);
        }

        if (!_hasher.Verify(guardian.PasswordHash, request.Password))
            return Result<GuardianSignInDto>.Failure(InvalidCredentials);

        var now = UtcNow;

        // A phone that already passed the OTP skips it, but only after the password has been checked above, and
        // only while its trust is unrevoked and unexpired. Anything else falls through to the normal OTP step.
        if (_otpOptions.TrustedDeviceDays > 0 && !string.IsNullOrWhiteSpace(request.DeviceToken))
        {
            var device = await _guardianRepo.GetActiveTrustedDeviceAsync(
                guardian.GuardianId, HashDeviceToken(request.DeviceToken), now, ct);
            if (device is not null)
            {
                device.LastUsedAt = now;
                device.ExpiresAt = now.AddDays(_otpOptions.TrustedDeviceDays);
                var auth = await CompleteGuardianSignInAsync(guardian, now, deviceToken: null, ct);
                return Result<GuardianSignInDto>.Success(new GuardianSignInDto(null, auth));
            }
        }

        // Only the newest login attempt may be verified.
        await _guardianRepo.ConsumeOpenOtpChallengesAsync(guardian.GuardianId, ct);

        var code = GenerateOtpCode(_otpOptions.CodeLength);
        var challenge = new GuardianOtpChallenge
        {
            GuardianId = guardian.GuardianId,
            Guardian = guardian,
            CodeHash = _hasher.Hash(code),
            ExpiresAtUtc = now.AddMinutes(_otpOptions.ExpiryMinutes),
            LastSentAtUtc = now,
            CreatedAt = now
        };

        await _guardianRepo.AddOtpChallengeAsync(challenge, ct);
        await _guardianRepo.SaveChangesAsync(ct);

        var sent = await SendOtpAsync(guardian, code, ct);
        if (!sent.Succeeded)
        {
            // The code never left, so this attempt must not be completable.
            challenge.IsConsumed = true;
            await _guardianRepo.SaveChangesAsync(ct);
            return Result<GuardianSignInDto>.Failure(OtpDeliveryFailedMessage);
        }
        return Result<GuardianSignInDto>.Success(new GuardianSignInDto(ToChallengeDto(challenge, now), null));
    }

    public async Task<Result<GuardianAuthResultDto>> VerifyGuardianOtpAsync(VerifyGuardianOtpRequest request, CancellationToken ct = default)
    {
        var challenge = await _guardianRepo.GetOpenOtpChallengeAsync(request.ChallengeToken, ct);
        if (challenge is null)
            return Result<GuardianAuthResultDto>.Failure(InvalidOtp);

        var now = UtcNow;
        if (challenge.ExpiresAtUtc <= now)
        {
            challenge.IsConsumed = true;
            await _guardianRepo.SaveChangesAsync(ct);
            return Result<GuardianAuthResultDto>.Failure(InvalidOtp);
        }

        if (!_hasher.Verify(challenge.CodeHash, request.Code))
        {
            challenge.FailedAttempts++;
            var burned = challenge.FailedAttempts >= _otpOptions.MaxVerificationAttempts;
            if (burned) challenge.IsConsumed = true;
            await _guardianRepo.SaveChangesAsync(ct);
            return Result<GuardianAuthResultDto>.Failure(burned ? OtpAttemptsExceeded : InvalidOtp);
        }

        // Single use: this code cannot authenticate a second request.
        challenge.VerifiedAtUtc = now;
        challenge.IsConsumed = true;

        var guardian = challenge.Guardian;
        if (guardian.Status != RecordStatus.Active)
        {
            await _guardianRepo.SaveChangesAsync(ct);
            return Result<GuardianAuthResultDto>.Failure(InvalidCredentials);
        }

        // Trust this phone only now that the OTP has actually been proven.
        string? deviceToken = null;
        if (request.TrustDevice && _otpOptions.TrustedDeviceDays > 0)
            deviceToken = await AddTrustedDeviceAsync(guardian.GuardianId, request.DeviceLabel, now, ct);

        var auth = await CompleteGuardianSignInAsync(guardian, now, deviceToken, ct);
        return Result<GuardianAuthResultDto>.Success(auth);
    }

    /// <summary>
    /// The end of every successful guardian sign-in, with or without the OTP: a first-login guardian gets a
    /// password-reset ticket and no token; everyone else gets a session. Saves the caller's pending changes.
    /// </summary>
    private async Task<GuardianAuthResultDto> CompleteGuardianSignInAsync(Guardian guardian, DateTime now, string? deviceToken, CancellationToken ct)
    {
        if (guardian.PasswordResetRequired)
        {
            // No token is issued, so the first-login reset cannot be skipped by the client.
            var ticket = GenerateSecureToken();
            var ticketExpiry = now.AddMinutes(_otpOptions.PasswordResetTicketMinutes);
            guardian.PasswordResetToken = ticket;
            guardian.PasswordResetTokenExpiresAt = ticketExpiry;
            guardian.UpdatedAt = now;
            await _guardianRepo.SaveChangesAsync(ct);

            return new GuardianAuthResultDto(GuardianAuthStage.PasswordResetRequired, null, ticket, ticketExpiry, deviceToken);
        }

        await _guardianRepo.SaveChangesAsync(ct);

        var (token, expires) = _jwt.GenerateToken(guardian.GuardianId, guardian.Email, guardian.FullName, AppRole.Guardian);
        var session = new LoginResponse(token, expires, nameof(AppRole.Guardian), guardian.GuardianId, guardian.FullName, guardian.Email);
        return new GuardianAuthResultDto(GuardianAuthStage.Authenticated, session, null, null, deviceToken);
    }

    // ---- Trusted devices ----

    /// <summary>
    /// For a phone that is already signed in (it holds a valid session, which itself proves an OTP was passed):
    /// trusts it without another code. Used once per phone by the app after an update.
    /// </summary>
    public async Task<Result<string>> EnrollTrustedDeviceAsync(int guardianId, string? label, CancellationToken ct = default)
    {
        if (_otpOptions.TrustedDeviceDays <= 0)
            return Result<string>.Failure("Trusted devices are turned off.");
        var guardian = await _guardianRepo.GetByIdAsync(guardianId, ct);
        if (guardian is null || guardian.Status != RecordStatus.Active)
            return Result<string>.Failure("Guardian not found.");
        var token = await AddTrustedDeviceAsync(guardianId, label, UtcNow, ct);
        await _guardianRepo.SaveChangesAsync(ct);
        return Result<string>.Success(token);
    }

    public async Task<List<TrustedDeviceDto>> ListTrustedDevicesAsync(int guardianId, CancellationToken ct = default) =>
        (await _guardianRepo.GetActiveTrustedDevicesAsync(guardianId, UtcNow, ct))
            .Select(d => new TrustedDeviceDto(d.Id, d.Label, d.CreatedAt, d.LastUsedAt, d.ExpiresAt))
            .ToList();

    public async Task<int> RevokeTrustedDevicesAsync(int guardianId, int? deviceId, CancellationToken ct = default)
    {
        var count = await _guardianRepo.RevokeTrustedDevicesAsync(guardianId, UtcNow, deviceId, ct);
        await _guardianRepo.SaveChangesAsync(ct);
        return count;
    }

    private async Task<string> AddTrustedDeviceAsync(int guardianId, string? label, DateTime now, CancellationToken ct)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var trimmed = label?.Trim();
        await _guardianRepo.AddTrustedDeviceAsync(new GuardianTrustedDevice
        {
            GuardianId = guardianId,
            TokenHash = HashDeviceToken(secret),
            Label = string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 100)],
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now.AddDays(_otpOptions.TrustedDeviceDays)
        }, ct);
        return secret;
    }

    /// <summary>SHA-256 is enough here: the secret is 256 random bits, so there is nothing to brute-force.</summary>
    private static string HashDeviceToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();

    public async Task<Result<GuardianOtpChallengeDto>> ResendGuardianOtpAsync(ResendGuardianOtpRequest request, CancellationToken ct = default)
    {
        var challenge = await _guardianRepo.GetOpenOtpChallengeAsync(request.ChallengeToken, ct);
        if (challenge is null)
            return Result<GuardianOtpChallengeDto>.Failure(ChallengeGone);

        if (challenge.ResendCount >= _otpOptions.MaxResends)
        {
            challenge.IsConsumed = true;
            await _guardianRepo.SaveChangesAsync(ct);
            return Result<GuardianOtpChallengeDto>.Failure("Resend limit reached. Please sign in again.");
        }

        var now = UtcNow;
        var nextAllowedAt = challenge.LastSentAtUtc.AddSeconds(_otpOptions.ResendIntervalSeconds);
        if (now < nextAllowedAt)
        {
            var wait = (int)Math.Ceiling((nextAllowedAt - now).TotalSeconds);
            return Result<GuardianOtpChallengeDto>.Failure(
                string.Create(CultureInfo.InvariantCulture, $"Please wait {wait} seconds before requesting another code."));
        }

        var (previousHash, previousExpiry, previousFailures, previousResends, previousSentAt) =
            (challenge.CodeHash, challenge.ExpiresAtUtc, challenge.FailedAttempts, challenge.ResendCount, challenge.LastSentAtUtc);

        var code = GenerateOtpCode(_otpOptions.CodeLength);
        challenge.CodeHash = _hasher.Hash(code);
        challenge.ExpiresAtUtc = now.AddMinutes(_otpOptions.ExpiryMinutes);
        challenge.FailedAttempts = 0;
        challenge.ResendCount++;
        challenge.LastSentAtUtc = now;
        await _guardianRepo.SaveChangesAsync(ct);

        var sent = await SendOtpAsync(challenge.Guardian, code, ct);
        if (!sent.Succeeded)
        {
            // A failed send is not a used resend: put the challenge back as it was, so the code the guardian
            // already holds still works and they can try again straight away.
            (challenge.CodeHash, challenge.ExpiresAtUtc, challenge.FailedAttempts, challenge.ResendCount, challenge.LastSentAtUtc) =
                (previousHash, previousExpiry, previousFailures, previousResends, previousSentAt);
            await _guardianRepo.SaveChangesAsync(ct);
            return Result<GuardianOtpChallengeDto>.Failure(OtpDeliveryFailedMessage);
        }
        return Result<GuardianOtpChallengeDto>.Success(ToChallengeDto(challenge, now));
    }

    public async Task<Result> ResetGuardianPasswordAsync(GuardianResetPasswordRequest request, CancellationToken ct = default)
    {
        var guardian = await _guardianRepo.GetByPasswordResetTokenAsync(request.ResetTicket, ct);
        if (guardian is null || guardian.PasswordResetTokenExpiresAt is null ||
            guardian.PasswordResetTokenExpiresAt < UtcNow)
            return Result.Failure("Invalid or expired reset request. Please sign in again.");

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            return Result.Failure("Passwords do not match.");

        // Rule: the default password must stop working once the reset completes.
        if (_hasher.Verify(guardian.PasswordHash, request.NewPassword))
            return Result.Failure("New password must be different from your current password.");

        guardian.PasswordHash = _hasher.Hash(request.NewPassword);
        guardian.PasswordResetToken = null;
        guardian.PasswordResetTokenExpiresAt = null;
        guardian.PasswordResetRequired = false;
        guardian.UpdatedAt = UtcNow;
        await _guardianRepo.ConsumeOpenOtpChallengesAsync(guardian.GuardianId, ct);
        await _guardianRepo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RegisterGuardianAsync(RegisterGuardianRequest request, CancellationToken ct = default)
    {
        if (await _guardianRepo.EmailExistsAsync(request.Email, ct))
            return Result.Failure("Email already registered.");

        var contactNumber = ContactNumber.Normalize(request.Phone);
        if (contactNumber is null)
            return Result.Failure("A valid contact number is required.");
        if (await _guardianRepo.ContactNumberExistsAsync(contactNumber, ct))
            return Result.Failure("Contact number already registered.");

        var guardian = new Guardian
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = contactNumber,
            PasswordHash = _hasher.Hash(request.Password),
            Status = RecordStatus.Active,
            // Self-chosen password — nothing to force a reset of.
            PasswordResetRequired = false
        };

        await _guardianRepo.AddAsync(guardian, ct);
        await _guardianRepo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var token = GenerateSecureToken();
        var expiresAt = UtcNow.AddMinutes(30);

        if (string.Equals(request.Role, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase))
        {
            var admin = await _adminRepo.GetByEmailAsync(request.Email, ct);
            if (admin is null) return Result.Success(); // do not reveal account existence
            admin.PasswordResetToken = token;
            admin.PasswordResetTokenExpiresAt = expiresAt;
            await _adminRepo.SaveChangesAsync(ct);
        }
        else
        {
            var guardian = await _guardianRepo.GetByEmailAsync(request.Email, ct);
            if (guardian is null) return Result.Success();
            guardian.PasswordResetToken = token;
            guardian.PasswordResetTokenExpiresAt = expiresAt;
            await _guardianRepo.SaveChangesAsync(ct);
        }

        await _resetNotifier.SendResetTokenAsync(request.Email, token, ct);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        if (string.Equals(request.Role, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase))
        {
            var admin = await _adminRepo.GetByEmailAsync(request.Email, ct);
            if (admin is null || admin.PasswordResetToken != request.ResetToken ||
                admin.PasswordResetTokenExpiresAt is null || admin.PasswordResetTokenExpiresAt < UtcNow)
                return Result.Failure("Invalid or expired reset token.");

            admin.PasswordHash = _hasher.Hash(request.NewPassword);
            admin.PasswordResetToken = null;
            admin.PasswordResetTokenExpiresAt = null;
            await _adminRepo.SaveChangesAsync(ct);
        }
        else
        {
            var guardian = await _guardianRepo.GetByEmailAsync(request.Email, ct);
            if (guardian is null || guardian.PasswordResetToken != request.ResetToken ||
                guardian.PasswordResetTokenExpiresAt is null || guardian.PasswordResetTokenExpiresAt < UtcNow)
                return Result.Failure("Invalid or expired reset token.");

            guardian.PasswordHash = _hasher.Hash(request.NewPassword);
            guardian.PasswordResetToken = null;
            guardian.PasswordResetTokenExpiresAt = null;
            // A self-chosen password also clears the forced first-login reset.
            guardian.PasswordResetRequired = false;
            await _guardianRepo.RevokeTrustedDevicesAsync(guardian.GuardianId, UtcNow, null, ct);
            await _guardianRepo.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(int userId, AppRole role, ChangePasswordRequest request, CancellationToken ct = default)
    {
        if (role == AppRole.Admin)
        {
            var admin = await _adminRepo.GetByIdAsync(userId, ct);
            if (admin is null || !_hasher.Verify(admin.PasswordHash, request.CurrentPassword))
                return Result.Failure("Current password is incorrect.");
            admin.PasswordHash = _hasher.Hash(request.NewPassword);
            admin.UpdatedAt = UtcNow;
            await _adminRepo.SaveChangesAsync(ct);
        }
        else
        {
            var guardian = await _guardianRepo.GetByIdAsync(userId, ct);
            if (guardian is null || !_hasher.Verify(guardian.PasswordHash, request.CurrentPassword))
                return Result.Failure("Current password is incorrect.");
            guardian.PasswordHash = _hasher.Hash(request.NewPassword);
            guardian.PasswordResetRequired = false;
            guardian.UpdatedAt = UtcNow;
            // A new password ends every phone's OTP exemption: they must prove the email again.
            await _guardianRepo.RevokeTrustedDevicesAsync(guardian.GuardianId, UtcNow, null, ct);
            await _guardianRepo.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    private Task<Result> SendOtpAsync(Guardian guardian, string code, CancellationToken ct) =>
        _otpSender.SendLoginOtpAsync(
            guardian, code, TimeSpan.FromMinutes(_otpOptions.ExpiryMinutes), ct);

    private GuardianOtpChallengeDto ToChallengeDto(GuardianOtpChallenge challenge, DateTime now) =>
        new(challenge.ChallengeToken,
            challenge.ExpiresAtUtc,
            _otpOptions.CodeLength,
            Math.Max(0, (int)Math.Ceiling((challenge.LastSentAtUtc.AddSeconds(_otpOptions.ResendIntervalSeconds) - now).TotalSeconds)),
            Math.Max(0, _otpOptions.MaxResends - challenge.ResendCount),
            EmailMasking.Mask(challenge.Guardian?.Email));

    private static string GenerateOtpCode(int length)
    {
        var exclusiveMax = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(0, exclusiveMax)
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(length, '0');
    }

    private static string GenerateSecureToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
