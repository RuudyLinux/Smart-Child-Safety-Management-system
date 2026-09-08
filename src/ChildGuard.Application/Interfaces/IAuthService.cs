using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAdminAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<LoginResponse>> LoginGuardianAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result> RegisterGuardianAsync(RegisterGuardianRequest request, CancellationToken ct = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(int userId, AppRole role, ChangePasswordRequest request, CancellationToken ct = default);
}
