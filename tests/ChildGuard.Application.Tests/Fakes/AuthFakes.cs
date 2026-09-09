using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests.Fakes;

/// <summary>
/// In-memory doubles for the auth dependencies. The project has no mocking
/// package, and the existing tests are plain unit tests, so these stay hand-rolled.
/// </summary>
internal sealed class FakeGuardianRepository : IGuardianRepository
{
    public List<Guardian> Guardians { get; } = new();
    public List<GuardianOtpChallenge> Challenges { get; } = new();
    public int SaveCount { get; private set; }

    public Task<Guardian?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Guardians.FirstOrDefault(g => g.Email == email));

    public Task<Guardian?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default) =>
        Task.FromResult(Guardians.FirstOrDefault(g => g.Phone == contactNumber));

    public Task<Guardian?> GetByPasswordResetTokenAsync(string resetToken, CancellationToken ct = default) =>
        Task.FromResult(Guardians.FirstOrDefault(g => g.PasswordResetToken == resetToken));

    public Task<Guardian?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Guardians.FirstOrDefault(g => g.GuardianId == id));

    public Task<Guardian?> GetWithStudentsAsync(int id, CancellationToken ct = default) => GetByIdAsync(id, ct);

    public Task<List<Guardian>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Guardians.ToList());

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Guardians.Any(g => g.Email == email));

    public Task<bool> EmailExistsAsync(string email, int excludeGuardianId, CancellationToken ct = default) =>
        Task.FromResult(Guardians.Any(g => g.Email == email && g.GuardianId != excludeGuardianId));

    public Task<bool> ContactNumberExistsAsync(string contactNumber, CancellationToken ct = default) =>
        Task.FromResult(Guardians.Any(g => g.Phone == contactNumber));

    public Task AddAsync(Guardian guardian, CancellationToken ct = default)
    {
        guardian.GuardianId = guardian.GuardianId == 0 ? Guardians.Count + 1 : guardian.GuardianId;
        Guardians.Add(guardian);
        return Task.CompletedTask;
    }

    public Task<string?> GetFcmTokenAsync(int guardianId, CancellationToken ct = default) =>
        Task.FromResult(Guardians.FirstOrDefault(g => g.GuardianId == guardianId)?.FcmToken);

    public Task AddOtpChallengeAsync(GuardianOtpChallenge challenge, CancellationToken ct = default)
    {
        challenge.GuardianOtpChallengeId = Challenges.Count + 1;
        challenge.Guardian ??= Guardians.First(g => g.GuardianId == challenge.GuardianId);
        Challenges.Add(challenge);
        return Task.CompletedTask;
    }

    public Task<GuardianOtpChallenge?> GetOpenOtpChallengeAsync(Guid challengeToken, CancellationToken ct = default) =>
        Task.FromResult(Challenges.FirstOrDefault(c => c.ChallengeToken == challengeToken && !c.IsConsumed));

    public Task ConsumeOpenOtpChallengesAsync(int guardianId, CancellationToken ct = default)
    {
        foreach (var c in Challenges.Where(c => c.GuardianId == guardianId && !c.IsConsumed)) c.IsConsumed = true;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeAdminRepository : IAdminRepository
{
    public List<Admin> Admins { get; } = new();

    public Task<Admin?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Admins.FirstOrDefault(a => a.Email == email));

    public Task<Admin?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Admins.FirstOrDefault(a => a.AdminId == id));

    public Task AddAsync(Admin admin, CancellationToken ct = default)
    {
        Admins.Add(admin);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Deterministic stand-in for the PBKDF2 hasher — same contract, no crypto cost.</summary>
internal sealed class FakePasswordHasher : IPasswordHasherService
{
    public string Hash(string password) => "hashed:" + password;
    public bool Verify(string hash, string password) => hash == "hashed:" + password;
}

internal sealed class FakeJwtTokenGenerator : IJwtTokenGenerator
{
    public (string Token, DateTime ExpiresAtUtc) GenerateToken(int userId, string email, string fullName, AppRole role) =>
        ($"token-{role}-{userId}", DateTime.UtcNow.AddMinutes(120));
}

internal sealed class FakePasswordResetNotifier : IPasswordResetNotifier
{
    public List<(string Email, string Token)> Sent { get; } = new();

    public Task SendResetTokenAsync(string email, string resetToken, CancellationToken ct = default)
    {
        Sent.Add((email, resetToken));
        return Task.CompletedTask;
    }
}

/// <summary>Captures the OTP the service generated, which is the only way a test can know it.</summary>
internal sealed class FakeOtpSender : IOtpSender
{
    public List<(string ContactNumber, string Code)> Sent { get; } = new();
    public string? LastCode => Sent.Count == 0 ? null : Sent[^1].Code;

    public Task SendLoginOtpAsync(Guardian guardian, string code, TimeSpan validFor, CancellationToken ct = default)
    {
        Sent.Add((guardian.Phone!, code));
        return Task.CompletedTask;
    }
}

/// <summary>Captures every message the app tried to send, and can be told to fail.</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();
    public string? FailWith { get; set; }
    public EmailMessage? Last => Sent.Count == 0 ? null : Sent[^1];

    public Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (FailWith is not null) return Task.FromResult(Result.Failure(FailWith));
        Sent.Add(message);
        return Task.FromResult(Result.Success());
    }
}
