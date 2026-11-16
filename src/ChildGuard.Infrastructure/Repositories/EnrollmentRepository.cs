using System.Data;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class EnrollmentRepository : IEnrollmentRepository
{
    private readonly ChildGuardDbContext _db;
    public EnrollmentRepository(ChildGuardDbContext db) => _db = db;

    public Task<EnrollmentRequest?> GetByKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        _db.EnrollmentRequests.AsNoTracking().FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);

    public async Task AddAsync(EnrollmentRequest request, CancellationToken ct = default) =>
        await _db.EnrollmentRequests.AddAsync(request, ct);

    // MySQL's collation and PostgreSQL's citext both compare these case-insensitively.
    public Task<bool> AdmissionNumberExistsAsync(string admissionNumber, CancellationToken ct = default) =>
        _db.Students.AnyAsync(s => s.AdmissionNumber == admissionNumber, ct);

    public Task<List<Student>> FindSameNameAndBirthDateAsync(string fullName, DateTime dateOfBirth, CancellationToken ct = default)
    {
        var day = DateTime.SpecifyKind(dateOfBirth.Date, DateTimeKind.Unspecified);
        var next = day.AddDays(1);
        return _db.Students.AsNoTracking()
            .Where(s => s.FullName == fullName && s.DateOfBirth >= day && s.DateOfBirth < next)
            .OrderBy(s => s.StudentId).Take(10)
            .ToListAsync(ct);
    }

    public async Task<(Result Result, bool UniqueConflict)> RunInTransactionAsync(
        Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var result = await work(ct);
            if (result.Succeeded)
            {
                await tx.CommitAsync(ct);
                return (result, false);
            }
            await RollbackAsync(tx);
            return (result, false);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            await RollbackAsync(tx);
            return (Result.Failure("Duplicate."), true);
        }
        catch
        {
            await RollbackAsync(tx);
            throw;
        }
    }

    private async Task RollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
    {
        await tx.RollbackAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
    }
}
