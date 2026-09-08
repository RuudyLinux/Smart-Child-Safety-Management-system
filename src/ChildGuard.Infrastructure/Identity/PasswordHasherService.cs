using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace ChildGuard.Infrastructure.Identity;

/// <summary>Wraps ASP.NET Core Identity's PasswordHasher (PBKDF2) — no custom crypto.</summary>
public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(new object(), password);

    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(new object(), hash, password) != PasswordVerificationResult.Failed;
}
