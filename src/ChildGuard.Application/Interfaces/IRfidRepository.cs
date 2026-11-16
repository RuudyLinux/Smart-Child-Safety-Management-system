using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IRfidRepository
{
    Task<List<RfidCard>> GetAllAsync(CancellationToken ct = default);
    Task<RfidCard?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<RfidCard?> GetByCardUidAsync(string cardUid, CancellationToken ct = default);
    Task AddAsync(RfidCard card, CancellationToken ct = default);
    Task AddAssignmentHistoryAsync(RfidCardAssignment entry, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> holding row locks on the cards (ascending ids), in a transaction committed only on
    /// success. Inside a caller's transaction it only takes the locks. Tracked copies of the cards are reloaded after
    /// locking, so checks see the committed state.
    /// </summary>
    Task<Result> RunLockedAsync(IReadOnlyCollection<int> cardIds, Func<CancellationToken, Task<Result>> work, CancellationToken ct = default);
    /// <summary>Locks the cards' rows in the current transaction and reloads tracked copies.</summary>
    Task LockCardsAsync(IReadOnlyCollection<int> cardIds, CancellationToken ct = default);
    /// <summary>The card's open (not ended) assignment row, if any.</summary>
    Task<RfidCardAssignment?> GetOpenAssignmentAsync(int cardId, CancellationToken ct = default);
    Task<List<RfidCardAssignmentDto>> GetHistoryAsync(int cardId, CancellationToken ct = default);
    /// <summary>The card has ever been assigned or recorded a checkpoint or attendance.</summary>
    Task<bool> HasHistoryAsync(int cardId, CancellationToken ct = default);
    void Remove(RfidCard card);
    /// <summary>Saves; false (nothing saved) when a unique index refused the write.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
    Task<List<RfidDevice>> GetDevicesAsync(CancellationToken ct = default);
    Task<RfidDevice?> GetDeviceByIdAsync(int id, CancellationToken ct = default);
    Task AddDeviceAsync(RfidDevice device, CancellationToken ct = default);
    Task<RfidDevice?> GetDeviceByKeyHashAsync(string keyHash, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
