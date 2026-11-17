using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Read model behind the Guardian app's dashboard, student details, transport, reports and pickup status.
/// Like <see cref="SafetyReadService"/> it queries the DbContext directly. Every query starts from the
/// guardian's own StudentGuardian links, so an unlinked student id never returns anything.
/// </summary>
public class GuardianPortalReadService : IGuardianPortalReadService
{
    /// <summary>Longest report range, in days, so one request cannot scan the whole history.</summary>
    public const int MaxReportDays = 92;
    public const int DefaultReportDays = 30;
    private const int RecentEventCount = 10;

    private readonly ChildGuardDbContext _db;
    private readonly ISafetyReadService _safety;
    private readonly SchoolClock _clock;
    private readonly SchoolOptions _school;
    private readonly VerificationOptions _verification;
    private readonly ISchoolScheduleProvider _schedule;

    public GuardianPortalReadService(
        ChildGuardDbContext db, ISafetyReadService safety, SchoolClock clock, SchoolOptions school, VerificationOptions verification,
        ISchoolScheduleProvider? schedule = null)
    {
        _schedule = schedule ?? new ConfiguredSchoolScheduleProvider(school);
        _db = db;
        _safety = safety;
        _clock = clock;
        _school = school;
        _verification = verification;
    }

    public Task<bool> IsLinkedAsync(int guardianId, int studentId, CancellationToken ct = default) =>
        _db.StudentGuardians.AsNoTracking().AnyAsync(sg => sg.GuardianId == guardianId && sg.StudentId == studentId, ct);

    public async Task<GuardianDashboardDto?> GetDashboardAsync(int guardianId, CancellationToken ct = default)
    {
        var guardianName = await _db.Guardians.AsNoTracking()
            .Where(g => g.GuardianId == guardianId)
            .Select(g => g.FullName)
            .FirstOrDefaultAsync(ct);
        if (guardianName is null) return null;

        var children = await GetChildrenAsync(guardianId, ct);
        var ids = children.Select(c => c.StudentId).ToList();
        var dayStart = _clock.TodayStartUtc;

        var recent = await _db.JourneyEvents.AsNoTracking()
            .Where(e => ids.Contains(e.StudentId) && e.Timestamp >= dayStart)
            .OrderByDescending(e => e.Timestamp)
            .Take(RecentEventCount)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToListAsync(ct);

        var unread = await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.GuardianId == guardianId && !n.IsRead, ct);

        var pendingSince = _clock.UtcNow - _verification.PendingExpiry;
        var pending = await _db.GuardianVerifications.AsNoTracking()
            .CountAsync(v => ids.Contains(v.StudentId)
                && v.Result == VerificationResult.Match
                && !v.PickupApproved
                && v.ApprovedByAdminId == null
                && v.AttemptedAt >= pendingSince, ct);

        return new GuardianDashboardDto(
            guardianName,
            _clock.Today,
            children.Count,
            children.Count(c => c.PresentToday),
            children.Count(c => c.LateToday),
            children.Count(c => c.SafetyStatus == StudentSafetyStatus.AtSchool),
            children.Count(c => c.SafetyStatus == StudentSafetyStatus.OnBusToSchool),
            children.Count(c => c.SafetyStatus is StudentSafetyStatus.LeftSchool or StudentSafetyStatus.DroppedOffHome),
            unread,
            pending,
            children,
            recent);
    }

    public async Task<List<GuardianChildCardDto>> GetChildrenAsync(int guardianId, CancellationToken ct = default)
    {
        var links = await _db.StudentGuardians.AsNoTracking()
            .Where(sg => sg.GuardianId == guardianId)
            .Select(sg => new { sg.StudentId, sg.Student.FullName, sg.Student.Grade, sg.Relationship, sg.IsAuthorizedForPickup })
            .OrderBy(sg => sg.FullName)
            .ToListAsync(ct);

        var snapshots = await _safety.GetSnapshotsAsync(links.Select(l => l.StudentId).ToList(), ct);
        return links.Select(l =>
        {
            var s = snapshots.TryGetValue(l.StudentId, out var found) ? found : StudentSafetySnapshot.Unknown(l.StudentId);
            return new GuardianChildCardDto(
                l.StudentId, l.FullName, l.Grade, l.Relationship, l.IsAuthorizedForPickup, s.HasPhoto,
                s.Status, s.LastEventType, s.LastEventAt, s.CheckInTime, s.PresentToday, s.LateToday, s.BusNumber, s.RouteName);
        }).ToList();
    }

    public async Task<GuardianStudentDetailDto?> GetStudentDetailAsync(int guardianId, int studentId, CancellationToken ct = default)
    {
        var child = (await GetChildrenAsync(guardianId, ct)).FirstOrDefault(c => c.StudentId == studentId);
        if (child is null) return null;

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.StudentId == studentId)
            .Select(s => new { s.DateOfBirth, s.Status, HasCard = s.RfidCard != null })
            .FirstAsync(ct);

        var transport = await _db.StudentBusAssignments.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status == RecordStatus.Active)
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new GuardianTransportDto(
                a.Bus.BusNumber,
                a.Bus.Capacity,
                a.Route != null ? a.Route.RouteName : a.Bus.Route != null ? a.Bus.Route.RouteName : null,
                a.Route != null ? a.Route.Description : a.Bus.Route != null ? a.Bus.Route.Description : null,
                a.Bus.Driver != null ? a.Bus.Driver.FullName : null,
                a.Bus.Driver != null ? a.Bus.Driver.Phone : null,
                a.AssignedAt))
            .FirstOrDefaultAsync(ct);

        var guardians = await _db.StudentGuardians.AsNoTracking()
            .Where(sg => sg.StudentId == studentId && sg.Guardian.Status == RecordStatus.Active)
            .OrderByDescending(sg => sg.GuardianId == guardianId)
            .ThenBy(sg => sg.Guardian.FullName)
            .Select(sg => new GuardianCoGuardianDto(
                sg.GuardianId, sg.Guardian.FullName, sg.Relationship, sg.IsAuthorizedForPickup, sg.GuardianId == guardianId))
            .ToListAsync(ct);

        var dayStart = _clock.TodayStartUtc;
        var today = await _db.JourneyEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.Timestamp >= dayStart)
            .OrderBy(e => e.Timestamp)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToListAsync(ct);

        var stats = await _safety.GetAttendanceStatsAsync(studentId, ct);
        return new GuardianStudentDetailDto(child, student.DateOfBirth, student.Status, student.HasCard, stats, transport, guardians, today);
    }

    public async Task<List<JourneyEventDto>> GetJourneyAsync(int guardianId, int studentId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!await IsLinkedAsync(guardianId, studentId, ct)) return new List<JourneyEventDto>();
        var (fromUtc, toUtc) = _clock.RangeUtc(from, to);
        return await _db.JourneyEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.Timestamp >= fromUtc && e.Timestamp < toUtc)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToListAsync(ct);
    }

    public async Task<Result<GuardianStudentReportDto>> GetReportAsync(
        int guardianId, int studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var range = ResolveRange(from, to);
        if (!range.Succeeded) return Result<GuardianStudentReportDto>.Failure(range.Error!);
        var (start, end) = range.Value;

        var name = await _db.StudentGuardians.AsNoTracking()
            .Where(sg => sg.GuardianId == guardianId && sg.StudentId == studentId)
            .Select(sg => sg.Student.FullName)
            .FirstOrDefaultAsync(ct);
        if (name is null) return Result<GuardianStudentReportDto>.Failure("Student not found.");

        var attendance = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Date >= start && a.Date <= end)
            .Select(a => new { a.Date, a.Time, a.EventType })
            .ToListAsync(ct);

        // Scheduled school days in the range (SchoolDaysObserved keeps its name for the app).
        // Future dates are not counted, and today only once it is decided.
        var schedule = await _schedule.GetAsync(start, end, ct);
        var today = _clock.Today;
        var observed = schedule.CountSchoolDays(start, end < today ? end : today.AddDays(-1));
        if (start <= today && today <= end && schedule.For(today) is { IsSchoolDay: true } todayInfo &&
            (attendance.Any(a => a.Date == today && a.EventType == AttendanceEventType.SchoolArrival)
             || TimeOnly.FromDateTime(_clock.LocalNow) > todayInfo.MissingCheckInAfter))
            observed++;

        var (fromUtc, toUtc) = _clock.RangeUtc(start, end);
        var busEvents = await _db.JourneyEvents.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.Timestamp >= fromUtc && e.Timestamp < toUtc
                && (e.EventType == JourneyEventType.BusBoarding || e.EventType == JourneyEventType.BusDropoff))
            .Select(e => new { e.EventType, e.Timestamp })
            .ToListAsync(ct);

        var pickups = await _db.GuardianVerifications.AsNoTracking()
            .CountAsync(v => v.StudentId == studentId && v.PickupApproved && v.AttemptedAt >= fromUtc && v.AttemptedAt < toUtc, ct);

        var busByDay = busEvents
            .Select(e => new { e.EventType, e.Timestamp, Date = _clock.ToSchoolDateTime(e.Timestamp).Date })
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dates = attendance.Select(a => a.Date).Concat(busByDay.Keys).Distinct().OrderByDescending(d => d);
        var days = dates.Select(date =>
        {
            var arrivals = attendance.Where(a => a.Date == date && a.EventType == AttendanceEventType.SchoolArrival).Select(a => a.Time).ToList();
            var departures = attendance.Where(a => a.Date == date && a.EventType == AttendanceEventType.SchoolDeparture).Select(a => a.Time).ToList();
            busByDay.TryGetValue(date, out var bus);
            TimeOnly? checkIn = arrivals.Count == 0 ? null : arrivals.Min();
            return new GuardianReportDayDto(
                date,
                checkIn,
                departures.Count == 0 ? null : departures.Max(),
                checkIn is not null && schedule.IsLate(date, checkIn.Value),
                bus?.Where(b => b.EventType == JourneyEventType.BusBoarding).Select(b => (DateTime?)b.Timestamp).Min(),
                bus?.Where(b => b.EventType == JourneyEventType.BusDropoff).Select(b => (DateTime?)b.Timestamp).Max(),
                schedule.IsSchoolDay(date));
        }).ToList();

        // Only school-day arrivals count toward the rate; an off-schedule arrival is listed, not counted.
        var present = days.Count(d => d.CheckIn is not null && d.IsSchoolDay);
        var late = days.Count(d => d.Late);
        return Result<GuardianStudentReportDto>.Success(new GuardianStudentReportDto(
            studentId, name, start, end, observed, present, late,
            observed == 0 ? 0 : Math.Min(100, Math.Round(present * 100.0 / observed, 1)),
            busEvents.Count(e => e.EventType == JourneyEventType.BusBoarding),
            busEvents.Count(e => e.EventType == JourneyEventType.BusDropoff),
            pickups,
            days));
    }

    public async Task<List<GuardianPickupDto>> GetPickupsAsync(
        int guardianId, int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var linked = await _db.StudentGuardians.AsNoTracking()
            .Where(sg => sg.GuardianId == guardianId)
            .Select(sg => sg.StudentId)
            .ToListAsync(ct);
        if (studentId is not null) linked = linked.Where(id => id == studentId).ToList();
        if (linked.Count == 0) return new List<GuardianPickupDto>();

        var end = to ?? _clock.Today;
        var start = from ?? end.AddDays(-DefaultReportDays);
        if (start > end) (start, end) = (end, start);
        var (fromUtc, toUtc) = _clock.RangeUtc(start, end);
        var now = _clock.UtcNow;

        var rows = await _db.GuardianVerifications.AsNoTracking()
            .Include(v => v.Student)
            .Include(v => v.Guardian)
            .Where(v => linked.Contains(v.StudentId) && v.AttemptedAt >= fromUtc && v.AttemptedAt < toUtc)
            .OrderByDescending(v => v.AttemptedAt)
            .Take(200)
            .ToListAsync(ct);

        var pickups = rows.Select(v => new GuardianPickupDto(
            "Guardian", v.Id, v.StudentId, v.Student.FullName, v.Guardian.FullName, v.GuardianId == guardianId,
            v.AttemptedAt, v.GetState(now, _verification.PendingExpiry),
            v.Result == VerificationResult.Match ? v.AttemptedAt + _verification.PendingExpiry : null)).ToList();

        pickups.AddRange(await GetTemporaryPickupsAsync(guardianId, linked, fromUtc, toUtc, ct));
        return pickups.OrderByDescending(p => p.AttemptedAt).ToList();
    }

    /// <summary>
    /// Temporary pickups for the guardian's children, shown in the same feed as face-verified pickups. Their
    /// richer lifecycle maps onto the pickup states the app already renders: waiting or ready = Pending,
    /// handed over = Approved, rejected, cancelled or revoked = Rejected.
    /// </summary>
    private async Task<List<GuardianPickupDto>> GetTemporaryPickupsAsync(
        int guardianId, IReadOnlyCollection<int> studentIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var rows = await _db.TemporaryPickupAuthorizations.AsNoTracking()
            .Include(a => a.Student)
            .Include(a => a.RequestedByGuardian)
            .Where(a => studentIds.Contains(a.StudentId) && a.CreatedAt >= fromUtc && a.CreatedAt < toUtc)
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(a =>
        {
            var state = a.GetState(now);
            var mapped = state switch
            {
                TemporaryPickupState.Completed => PickupVerificationState.Approved,
                TemporaryPickupState.Rejected or TemporaryPickupState.Cancelled or TemporaryPickupState.Revoked => PickupVerificationState.Rejected,
                TemporaryPickupState.Expired => PickupVerificationState.Expired,
                _ => PickupVerificationState.Pending
            };
            return new GuardianPickupDto(
                "Temporary", a.Id, a.StudentId, a.Student.FullName, a.FullName, a.RequestedByGuardianId == guardianId,
                a.CompletedAt ?? a.CreatedAt, mapped,
                mapped == PickupVerificationState.Pending ? a.ValidUntilUtc : null);
        }).ToList();
    }

    private Result<(DateOnly From, DateOnly To)> ResolveRange(DateOnly? from, DateOnly? to)
    {
        var end = to ?? _clock.Today;
        var start = from ?? end.AddDays(-(DefaultReportDays - 1));
        if (start > end) return Result<(DateOnly, DateOnly)>.Failure("The start date must be on or before the end date.");
        if (end.DayNumber - start.DayNumber + 1 > MaxReportDays)
            return Result<(DateOnly, DateOnly)>.Failure($"Choose a range of at most {MaxReportDays} days.");
        return Result<(DateOnly, DateOnly)>.Success((start, end));
    }
}
