using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class GuardianRepository : IGuardianRepository
{
    private readonly ChildGuardDbContext _db;
    public GuardianRepository(ChildGuardDbContext db) => _db = db;

    public Task<Guardian?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Guardians.FirstOrDefaultAsync(g => g.Email == email, ct);

    public Task<Guardian?> GetByContactNumberAsync(string contactNumber, CancellationToken ct = default) =>
        _db.Guardians.FirstOrDefaultAsync(g => g.Phone == contactNumber, ct);

    public Task<Guardian?> GetByPasswordResetTokenAsync(string resetToken, CancellationToken ct = default) =>
        _db.Guardians.FirstOrDefaultAsync(g => g.PasswordResetToken == resetToken, ct);

    public Task<Guardian?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Guardians.FirstOrDefaultAsync(g => g.GuardianId == id, ct);

    public Task<Guardian?> GetWithStudentsAsync(int id, CancellationToken ct = default) =>
        _db.Guardians
            .Include(g => g.FaceData)
            .Include(g => g.StudentGuardians).ThenInclude(sg => sg.Student)
            .FirstOrDefaultAsync(g => g.GuardianId == id, ct);

    public Task<List<Guardian>> GetAllAsync(CancellationToken ct = default) =>
        _db.Guardians
            .Include(g => g.StudentGuardians)
            // FaceData drives the "verification ready" column on the guardian list.
            .Include(g => g.FaceData)
            .OrderBy(g => g.FullName)
            .ToListAsync(ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        _db.Guardians.AnyAsync(g => g.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, int excludeGuardianId, CancellationToken ct = default) =>
        _db.Guardians.AnyAsync(g => g.Email == email && g.GuardianId != excludeGuardianId, ct);

    public Task<bool> ContactNumberExistsAsync(string contactNumber, CancellationToken ct = default) =>
        _db.Guardians.AnyAsync(g => g.Phone == contactNumber, ct);

    public async Task AddAsync(Guardian guardian, CancellationToken ct = default) =>
        await _db.Guardians.AddAsync(guardian, ct);

    public Task<string?> GetFcmTokenAsync(int guardianId, CancellationToken ct = default) =>
        _db.Guardians.Where(g => g.GuardianId == guardianId).Select(g => g.FcmToken).FirstOrDefaultAsync(ct);

    public async Task AddOtpChallengeAsync(GuardianOtpChallenge challenge, CancellationToken ct = default) =>
        await _db.GuardianOtpChallenges.AddAsync(challenge, ct);

    public Task<GuardianOtpChallenge?> GetOpenOtpChallengeAsync(Guid challengeToken, CancellationToken ct = default) =>
        _db.GuardianOtpChallenges
            .Include(c => c.Guardian)
            .FirstOrDefaultAsync(c => c.ChallengeToken == challengeToken && !c.IsConsumed, ct);

    public async Task ConsumeOpenOtpChallengesAsync(int guardianId, CancellationToken ct = default)
    {
        var open = await _db.GuardianOtpChallenges
            .Where(c => c.GuardianId == guardianId && !c.IsConsumed)
            .ToListAsync(ct);
        foreach (var challenge in open) challenge.IsConsumed = true;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
