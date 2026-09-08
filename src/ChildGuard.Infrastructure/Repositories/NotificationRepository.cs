using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly ChildGuardDbContext _db;
    public NotificationRepository(ChildGuardDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct = default) =>
        await _db.Notifications.AddAsync(notification, ct);

    public Task<List<Notification>> GetForGuardianAsync(int guardianId, CancellationToken ct = default) =>
        _db.Notifications
            .Where(n => n.GuardianId == guardianId)
            .OrderByDescending(n => n.SentAt)
            .ToListAsync(ct);

    public Task<Notification?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Notifications.FirstOrDefaultAsync(n => n.NotificationId == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
