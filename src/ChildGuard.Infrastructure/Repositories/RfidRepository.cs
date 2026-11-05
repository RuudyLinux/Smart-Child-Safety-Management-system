using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class RfidRepository : IRfidRepository
{
    private readonly ChildGuardDbContext _db;
    public RfidRepository(ChildGuardDbContext db) => _db = db;

    public Task<List<RfidCard>> GetAllAsync(CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<RfidCard?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).FirstOrDefaultAsync(c => c.RfidCardId == id, ct);

    public Task<RfidCard?> GetByCardUidAsync(string cardUid, CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).FirstOrDefaultAsync(c => c.CardUid == cardUid, ct);

    public async Task AddAsync(RfidCard card, CancellationToken ct = default) =>
        await _db.RfidCards.AddAsync(card, ct);

    public Task<List<RfidDevice>> GetDevicesAsync(CancellationToken ct = default) =>
        _db.RfidDevices.ToListAsync(ct);

    public Task<RfidDevice?> GetDeviceByIdAsync(int id, CancellationToken ct = default) =>
        _db.RfidDevices.FirstOrDefaultAsync(d => d.RfidDeviceId == id, ct);

    public Task<RfidDevice?> GetDeviceByKeyHashAsync(string keyHash, CancellationToken ct = default) =>
        _db.RfidDevices.AsNoTracking().FirstOrDefaultAsync(d => d.ApiKeyHash == keyHash, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
