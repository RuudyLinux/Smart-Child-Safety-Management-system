using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class BulkImportRepository : IBulkImportRepository
{
    private readonly ChildGuardDbContext _db;
    public BulkImportRepository(ChildGuardDbContext db) => _db = db;

    // Key lookups load only the natural-key columns, untracked, and are compared in memory with
    // explicit case rules, so the result is the same on MySQL (case-insensitive collation) and
    // PostgreSQL (citext on some columns, case-sensitive text on others).

    public Task<List<GuardianImportKey>> GetGuardianKeysAsync(CancellationToken ct = default) =>
        _db.Guardians.AsNoTracking().Select(g => new GuardianImportKey(g.GuardianId, g.Email, g.Phone)).ToListAsync(ct);

    public Task<List<StudentImportKey>> GetStudentKeysAsync(CancellationToken ct = default) =>
        _db.Students.AsNoTracking().Select(s => new StudentImportKey(s.FullName, s.DateOfBirth)).ToListAsync(ct);

    public Task<List<string>> GetRfidCardUidsAsync(CancellationToken ct = default) =>
        _db.RfidCards.AsNoTracking().Select(c => c.CardUid).ToListAsync(ct);

    public Task<List<DriverImportKey>> GetDriverKeysAsync(CancellationToken ct = default) =>
        _db.Drivers.AsNoTracking().Select(d => new DriverImportKey(d.DriverId, d.FullName, d.Phone, d.LicenseNumber, d.Status)).ToListAsync(ct);

    public Task<List<RouteImportKey>> GetRouteKeysAsync(CancellationToken ct = default) =>
        _db.Routes.AsNoTracking().Select(r => new RouteImportKey(r.RouteId, r.RouteName, r.Status)).ToListAsync(ct);

    public Task<List<BusImportKey>> GetBusKeysAsync(CancellationToken ct = default) =>
        _db.Buses.AsNoTracking().Select(b => new BusImportKey(b.BusId, b.BusNumber, b.RouteId, b.Capacity, b.Status,
            b.StudentBusAssignments.Count(a => a.Status == RecordStatus.Active))).ToListAsync(ct);

    public async Task<ImportSaveOutcome> SaveImportAsync(ImportWriteSet records, DataImport history, CancellationToken ct = default)
    {
        // Seats each bus needs for the new students, checked again under a lock below.
        var seatsNeeded = records.Students.SelectMany(s => s.BusAssignments)
            .GroupBy(a => a.BusId).ToDictionary(g => g.Key, g => g.Count());

        _db.Students.AddRange(records.Students); // with their guardian links, bus assignments and RFID cards
        _db.Guardians.AddRange(records.Guardians);
        _db.Drivers.AddRange(records.Drivers);
        _db.Routes.AddRange(records.Routes);
        _db.Buses.AddRange(records.Buses);
        _db.DataImports.Add(history);

        // Explicit transaction: the records and the history row commit together or not at all,
        // however EF chooses to batch the inserts.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            // Same lock as a single assignment (bus rows, ascending ids), so an import and an admin
            // assigning by hand cannot both take the last seat.
            foreach (var busId in seatsNeeded.Keys.OrderBy(i => i))
            {
                await (_db.Database.IsNpgsql()
                    ? _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"BusId\" FROM \"Buses\" WHERE \"BusId\" = {busId} FOR UPDATE", ct)
                    : _db.Database.ExecuteSqlInterpolatedAsync($"SELECT BusId FROM Buses WHERE BusId = {busId} FOR UPDATE", ct));
                var bus = await _db.Buses.AsNoTracking().Where(b => b.BusId == busId)
                    .Select(b => new { b.Capacity, b.Status, Taken = b.StudentBusAssignments.Count(a => a.Status == RecordStatus.Active) })
                    .SingleAsync(ct);
                if (bus.Status != RecordStatus.Active || bus.Taken + seatsNeeded[busId] > bus.Capacity)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                    return ImportSaveOutcome.CapacityExceeded;
                }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ImportSaveOutcome.Saved;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return ImportSaveOutcome.Conflict;
        }
    }

    public async Task AddHistoryAsync(DataImport history, CancellationToken ct = default)
    {
        _db.DataImports.Add(history);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateHistoryAsync(DataImport history, CancellationToken ct = default)
    {
        if (_db.Entry(history).State == EntityState.Detached) _db.DataImports.Update(history);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<DataImport>> GetHistoryAsync(ImportCategory? category, int take, CancellationToken ct = default)
    {
        var query = _db.DataImports.AsNoTracking().Include(d => d.Admin).AsQueryable();
        if (category is not null) query = query.Where(d => d.Category == category);
        // The (potentially large) error report is not needed for the list.
        return query.OrderByDescending(d => d.StartedAt).ThenByDescending(d => d.DataImportId).Take(take)
            .Select(d => new DataImport
            {
                DataImportId = d.DataImportId, Category = d.Category, Source = d.Source, FileName = d.FileName,
                AdminId = d.AdminId, Admin = d.Admin, StartedAt = d.StartedAt, CompletedAt = d.CompletedAt,
                TotalRows = d.TotalRows, InsertedRows = d.InsertedRows, FailedRows = d.FailedRows,
                Status = d.Status, Message = d.Message
            })
            .ToListAsync(ct);
    }

    public Task<DataImport?> GetHistoryByIdAsync(int id, CancellationToken ct = default) =>
        _db.DataImports.AsNoTracking().Include(d => d.Admin).FirstOrDefaultAsync(d => d.DataImportId == id, ct);
}
