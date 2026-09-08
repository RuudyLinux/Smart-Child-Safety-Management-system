using System.Security.Cryptography;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAdminRepository _adminRepo;
    private readonly IGuardianRepository _guardianRepo;
    private readonly IPasswordHasherService _hasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IPasswordResetNotifier _resetNotifier;

    public AuthService(
        IAdminRepository adminRepo,
        IGuardianRepository guardianRepo,
        IPasswordHasherService hasher,
        IJwtTokenGenerator jwt,
        IPasswordResetNotifier resetNotifier)
    {
        _adminRepo = adminRepo;
        _guardianRepo = guardianRepo;
        _hasher = hasher;
        _jwt = jwt;
        _resetNotifier = resetNotifier;
    }

    public async Task<Result<LoginResponse>> LoginAdminAsync(LoginRequest request, CancellationToken ct = default)
    {
        var admin = await _adminRepo.GetByEmailAsync(request.Email, ct);
        if (admin is null || admin.Status != RecordStatus.Active || !_hasher.Verify(admin.PasswordHash, request.Password))
            return Result<LoginResponse>.Failure("Invalid email or password.");

        var (token, expires) = _jwt.GenerateToken(admin.AdminId, admin.Email, admin.FullName, AppRole.Admin);
        return Result<LoginResponse>.Success(new LoginResponse(token, expires, nameof(AppRole.Admin), admin.AdminId, admin.FullName, admin.Email));
    }

    public async Task<Result<LoginResponse>> LoginGuardianAsync(LoginRequest request, CancellationToken ct = default)
    {
        var guardian = await _guardianRepo.GetByEmailAsync(request.Email, ct);
        if (guardian is null || guardian.Status != RecordStatus.Active || !_hasher.Verify(guardian.PasswordHash, request.Password))
            return Result<LoginResponse>.Failure("Invalid email or password.");

        var (token, expires) = _jwt.GenerateToken(guardian.GuardianId, guardian.Email, guardian.FullName, AppRole.Guardian);
        return Result<LoginResponse>.Success(new LoginResponse(token, expires, nameof(AppRole.Guardian), guardian.GuardianId, guardian.FullName, guardian.Email));
    }

    public async Task<Result> RegisterGuardianAsync(RegisterGuardianRequest request, CancellationToken ct = default)
    {
        if (await _guardianRepo.EmailExistsAsync(request.Email, ct))
            return Result.Failure("Email already registered.");

        var guardian = new Guardian
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = _hasher.Hash(request.Password),
            Status = RecordStatus.Active
        };

        await _guardianRepo.AddAsync(guardian, ct);
        await _guardianRepo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var token = GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(30);

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
                admin.PasswordResetTokenExpiresAt is null || admin.PasswordResetTokenExpiresAt < DateTime.UtcNow)
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
                guardian.PasswordResetTokenExpiresAt is null || guardian.PasswordResetTokenExpiresAt < DateTime.UtcNow)
                return Result.Failure("Invalid or expired reset token.");

            guardian.PasswordHash = _hasher.Hash(request.NewPassword);
            guardian.PasswordResetToken = null;
            guardian.PasswordResetTokenExpiresAt = null;
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
            admin.UpdatedAt = DateTime.UtcNow;
            await _adminRepo.SaveChangesAsync(ct);
        }
        else
        {
            var guardian = await _guardianRepo.GetByIdAsync(userId, ct);
            if (guardian is null || !_hasher.Verify(guardian.PasswordHash, request.CurrentPassword))
                return Result.Failure("Current password is incorrect.");
            guardian.PasswordHash = _hasher.Hash(request.NewPassword);
            guardian.UpdatedAt = DateTime.UtcNow;
            await _guardianRepo.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    private static string GenerateSecureToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
