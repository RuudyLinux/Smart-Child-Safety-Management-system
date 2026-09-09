using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Dashboard is a pure read-model spanning several aggregates — queries the DbContext
/// directly rather than through per-aggregate repositories, which is the conventional
/// exception for reporting/summary concerns in this layering.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly ChildGuardDbContext _db;
    public DashboardService(ChildGuardDbContext db) => _db = db;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var totalStudents = await _db.Students.CountAsync(s => s.Status == RecordStatus.Active, ct);
        var activeRfid = await _db.RfidCards.CountAsync(c => c.Status == RfidCardStatus.Active, ct);
        var totalGuardians = await _db.Guardians.CountAsync(g => g.Status == RecordStatus.Active, ct);
        var activeBuses = await _db.Buses.CountAsync(b => b.Status == RecordStatus.Active, ct);
        var todaysAttendance = await _db.AttendanceRecords.CountAsync(a => a.Date == today, ct);
        var todaysJourney = await _db.JourneyEvents.CountAsync(e => e.Timestamp >= todayStart && e.Timestamp < todayEnd, ct);
        var todaysVerifications = await _db.GuardianVerifications.CountAsync(v => v.AttemptedAt >= todayStart && v.AttemptedAt < todayEnd, ct);

        var todaysVerificationRows = await _db.GuardianVerifications
            .Where(v => v.AttemptedAt >= todayStart && v.AttemptedAt < todayEnd)
            .Select(v => new { v.Result, v.PickupApproved, v.ApprovedByAdminId })
            .ToListAsync(ct);
        var verificationStatus = new VerificationStatusDto(
            Matched: todaysVerificationRows.Count(v => v.Result == VerificationResult.Match),
            NoMatch: todaysVerificationRows.Count(v => v.Result == VerificationResult.NoMatch),
            PickupsApproved: todaysVerificationRows.Count(v => v.PickupApproved),
            PickupsRejected: todaysVerificationRows.Count(v => !v.PickupApproved && v.ApprovedByAdminId != null));

        var recent = await _db.JourneyEvents
            .Include(e => e.Student)
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToListAsync(ct);

        return new DashboardSummaryDto(
            totalStudents, activeRfid, totalGuardians, activeBuses,
            todaysAttendance, todaysJourney, todaysVerifications, verificationStatus, recent);
    }
}
