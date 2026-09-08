namespace ChildGuard.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Role, int UserId, string FullName, string Email);

public record RegisterGuardianRequest(string FullName, string Email, string Phone, string Password);

public record ForgotPasswordRequest(string Email, string Role);

public record ResetPasswordRequest(string Email, string Role, string ResetToken, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
