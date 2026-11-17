using System.Data;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class LifecycleRepository : ILifecycleRepository
{
    private readonly ChildGuardDbContext _db;
    public LifecycleRepository(ChildGuardDbContext db) => _db = db;

    public async Task<StudentDependencies?> GetStudentDependenciesAsync(int studentId, CancellationToken ct = default)
    {
        if (!await _db.Students.AnyAsync(s => s.StudentId == studentId, ct)) return null;
        var active = await _db.StudentBusAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status == RecordStatus.Active)
            .Select(a => new { a.Id, a.Bus.BusNumber })
            .FirstOrDefaultAsync(ct);
        return new StudentDependencies(
            await _db.AttendanceRecords.CountAsync(a => a.StudentId == studentId, ct),
            await _db.JourneyEvents.CountAsync(e => e.StudentId == studentId, ct),
            await _db.GuardianVerifications.CountAsync(v => v.StudentId == studentId, ct),
            await _db.VerificationSessions.CountAsync(v => v.StudentId == studentId, ct),
            await _db.TemporaryPickupAuthorizations.CountAsync(t => t.StudentId == studentId, ct),
            await _db.StudentBusAssignments.CountAsync(a => a.StudentId == studentId, ct),
            await _db.RfidCardAssignments.CountAsync(h => h.StudentId == studentId, ct),
            await _db.RfidCards.AnyAsync(c => c.StudentId == studentId, ct),
            await _db.StudentGuardians.CountAsync(sg => sg.StudentId == studentId, ct),
            await _db.EnrollmentRequests.CountAsync(r => r.StudentId == studentId, ct),
            await _db.StudentPhotos.AnyAsync(p => p.StudentId == studentId, ct),
            active?.Id, active?.BusNumber);
    }

    public async Task<GuardianDependencies?> GetGuardianDependenciesAsync(int guardianId, CancellationToken ct = default)
    {
        if (!await _db.Guardians.AnyAsync(g => g.GuardianId == guardianId, ct)) return null;
        var links = await _db.StudentGuardians.AsNoTracking()
            .Where(sg => sg.GuardianId == guardianId)
            .Select(sg => new { sg.StudentId, sg.Student.FullName, sg.IsAuthorizedForPickup })
            .ToListAsync(ct);
        var authorizedStudentIds = links.Where(l => l.IsAuthorizedForPickup).Select(l => l.StudentId).ToList();
        // Students for whom this guardian is the only active, pickup-authorised guardian.
        var otherAuthorized = await _db.StudentGuardians.AsNoTracking()
            .Where(sg => authorizedStudentIds.Contains(sg.StudentId) && sg.GuardianId != guardianId
                         && sg.IsAuthorizedForPickup && sg.Guardian.Status == RecordStatus.Active)
            .Select(sg => sg.StudentId).Distinct().ToListAsync(ct);
        var sole = links.Where(l => l.IsAuthorizedForPickup && !otherAuthorized.Contains(l.StudentId)).Select(l => l.FullName).ToList();

        return new GuardianDependencies(
            links.Count,
            await _db.GuardianVerifications.CountAsync(v => v.GuardianId == guardianId, ct),
            await _db.TemporaryPickupAuthorizations.CountAsync(t => t.RequestedByGuardianId == guardianId, ct),
            await _db.Notifications.CountAsync(n => n.GuardianId == guardianId, ct),
            await _db.GuardianFaceReferences.CountAsync(r => r.GuardianId == guardianId, ct),
            await _db.GuardianFaceData.AnyAsync(f => f.GuardianId == guardianId, ct),
            await _db.GuardianTrustedDevices.CountAsync(d => d.GuardianId == guardianId, ct),
            await _db.GuardianPhotos.AnyAsync(p => p.GuardianId == guardianId, ct),
            links.Select(l => l.FullName).OrderBy(n => n).ToList(),
            sole);
    }

    public async Task<bool> DeleteStudentAsync(int studentId, CancellationToken ct = default)
    {
        try
        {
            await _db.EnrollmentRequests.Where(r => r.StudentId == studentId).ExecuteDeleteAsync(ct);
            await _db.StudentGuardians.Where(sg => sg.StudentId == studentId).ExecuteDeleteAsync(ct);
            await _db.StudentPhotos.Where(p => p.StudentId == studentId).ExecuteDeleteAsync(ct);
            return await _db.Students.Where(s => s.StudentId == studentId).ExecuteDeleteAsync(ct) == 1;
        }
        catch (Exception ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return false;
        }
    }

    public async Task<bool> DeleteGuardianAsync(int guardianId, CancellationToken ct = default)
    {
        try
        {
            await _db.GuardianFaceReferences.Where(r => r.GuardianId == guardianId).ExecuteDeleteAsync(ct);
            await _db.GuardianFaceData.Where(f => f.GuardianId == guardianId).ExecuteDeleteAsync(ct);
            await _db.GuardianTrustedDevices.Where(d => d.GuardianId == guardianId).ExecuteDeleteAsync(ct);
            await _db.GuardianOtpChallenges.Where(c => c.GuardianId == guardianId).ExecuteDeleteAsync(ct);
            await _db.GuardianPhotos.Where(p => p.GuardianId == guardianId).ExecuteDeleteAsync(ct);
            return await _db.Guardians.Where(g => g.GuardianId == guardianId).ExecuteDeleteAsync(ct) == 1;
        }
        catch (Exception ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return false;
        }
    }

    public async Task<Result> RunInTransactionAsync(Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var result = await work(ct);
            if (result.Succeeded) await tx.CommitAsync(ct);
            else
            {
                await tx.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
            }
            return result;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }
}
