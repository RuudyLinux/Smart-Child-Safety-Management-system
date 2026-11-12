using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IRfidRepository
{
    Task<List<RfidCard>> GetAllAsync(CancellationToken ct = default);
    Task<RfidCard?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<RfidCard?> GetByCardUidAsync(string cardUid, CancellationToken ct = default);
    Task AddAsync(RfidCard card, CancellationToken ct = default);
    Task<List<RfidDevice>> GetDevicesAsync(CancellationToken ct = default);
    Task<RfidDevice?> GetDeviceByIdAsync(int id, CancellationToken ct = default);
    Task AddDeviceAsync(RfidDevice device, CancellationToken ct = default);
    Task<RfidDevice?> GetDeviceByKeyHashAsync(string keyHash, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
