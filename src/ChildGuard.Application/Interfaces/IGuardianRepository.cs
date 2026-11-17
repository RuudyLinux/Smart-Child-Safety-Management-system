using ChildGuard.Application.DTOs;
using ChildGuard.Application.Common;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IGuardianRepository
{
    Task<Guardian?> GetByEmailAsync(string email, CancellationToken ct = default);
    /// <summary>Looks up by normalized contact number — the Guardian login identifier.</summary>
    Task<Guardian?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default);
    Task<Guardian?> GetByPasswordResetTokenAsync(string resetToken, CancellationToken ct = default);
    Task<Guardian?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Guardian?> GetWithStudentsAsync(int id, CancellationToken ct = default);
    Task<List<Guardian>> GetAllAsync(CancellationToken ct = default);

    /// <summary>One page of guardians matching <paramref name="filter"/>.</summary>
    Task<(List<Guardian> Items, int Total)> GetPageAsync(GuardianListFilter filter, PageRequest page, CancellationToken ct = default);
    /// <summary>
    /// Guardians whose email or name contains <paramref name="text"/> (case-insensitive), or whose contact number
    /// contains <paramref name="digits"/>, with their linked students and face data.
    /// </summary>
    Task<List<Guardian>> SearchAsync(string text, string? digits, int take, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, int excludeGuardianId, CancellationToken ct = default);
    Task<bool> ContactNumberExistsAsync(string contactNumber, CancellationToken ct = default);
    Task AddAsync(Guardian guardian, CancellationToken ct = default);
    Task<string?> GetFcmTokenAsync(int guardianId, CancellationToken ct = default);

    // ---- OTP challenges ----
    Task AddOtpChallengeAsync(GuardianOtpChallenge challenge, CancellationToken ct = default);
    /// <summary>Loads an open (unconsumed) challenge with its Guardian. Returns null once the challenge is used up.</summary>
    Task<GuardianOtpChallenge?> GetOpenOtpChallengeAsync(Guid challengeToken, CancellationToken ct = default);
    /// <summary>Burns every still-open challenge for the Guardian, so only the newest login attempt can be verified.</summary>
    Task ConsumeOpenOtpChallengesAsync(int guardianId, CancellationToken ct = default);

    Task AddTrustedDeviceAsync(GuardianTrustedDevice device, CancellationToken ct = default);
    /// <summary>The unrevoked, unexpired trusted device with this hash for this guardian, tracked for update.</summary>
    Task<GuardianTrustedDevice?> GetActiveTrustedDeviceAsync(int guardianId, string tokenHash, DateTime nowUtc, CancellationToken ct = default);
    Task<List<GuardianTrustedDevice>> GetActiveTrustedDevicesAsync(int guardianId, DateTime nowUtc, CancellationToken ct = default);
    /// <summary>Marks every active device of the guardian revoked (or just <paramref name="deviceId"/>). Returns how many.</summary>
    Task<int> RevokeTrustedDevicesAsync(int guardianId, DateTime nowUtc, int? deviceId = null, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
