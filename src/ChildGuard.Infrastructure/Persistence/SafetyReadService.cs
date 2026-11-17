using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Derives the live safety picture used by every "where is this child?" surface.
/// Like the dashboard read-model, it queries the DbContext directly: the answer
/// spans journey events, attendance, transport assignments and photos, so no single
/// aggregate repository owns it.
/// </summary>
public class SafetyReadService : ISafetyReadService
{
    private readonly ChildGuardDbContext _db;
    private readonly SchoolOptions _school;
    private readonly SchoolClock _clock;
    private readonly ISchoolScheduleProvider _schedule;

    public SafetyReadService(ChildGuardDbContext db, SchoolOptions school, SchoolClock clock, ISchoolScheduleProvider? schedule = null)
    {
        _db = db;
        _school = school;
        _clock = clock;
        _schedule = schedule ?? new ConfiguredSchoolScheduleProvider(school);
    }

    public async Task<IReadOnlyDictionary<int, StudentSafetySnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<int> studentIds, CancellationToken ct = default)
    {
        if (studentIds.Count == 0) return new Dictionary<int, StudentSafetySnapshot>();

        var ids = studentIds.Distinct().ToList();
        var today = _clock.Today;
        var dayStart = _clock.TodayStartUtc;

        // Latest event per student, taken from today's events only: yesterday's
        // "dropped off home" must not read as today's known position.
        var todaysEvents = await _db.JourneyEvents.AsNoTracking()
            .Where(e => ids.Contains(e.StudentId) && e.Timestamp >= dayStart)
            .Select(e => new { e.StudentId, e.EventType, e.Timestamp })
            .ToListAsync(ct);

        var latest = todaysEvents
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Timestamp).First());

        var arrivals = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => ids.Contains(a.StudentId) && a.Date == today && a.EventType == AttendanceEventType.SchoolArrival)
            .Select(a => new { a.StudentId, a.Time })
            .ToListAsync(ct);
        var arrivalByStudent = arrivals
            .GroupBy(a => a.StudentId)
            .ToDictionary(g => g.Key, g => g.Min(a => a.Time));

        var transport = await _db.StudentBusAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.StudentId) && a.Status == RecordStatus.Active)
            .Select(a => new
            {
                a.StudentId,
                a.AssignedAt,
                BusNumber = a.Bus.BusNumber,
                RouteName = a.Route != null ? a.Route.RouteName : a.Bus.Route != null ? a.Bus.Route.RouteName : null,
                DriverName = a.Bus.Driver != null ? a.Bus.Driver.FullName : null
            })
            .ToListAsync(ct);
        var transportByStudent = transport
            .GroupBy(t => t.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.AssignedAt).First());

        var photoIds = await _db.StudentPhotos.AsNoTracking()
            .Where(p => ids.Contains(p.StudentId))
            .Select(p => p.StudentId)
            .ToListAsync(ct);
        var withPhoto = photoIds.ToHashSet();

        var todaySchedule = await _schedule.GetAsync(today, today, ct);
        var result = new Dictionary<int, StudentSafetySnapshot>(ids.Count);
        foreach (var id in ids)
        {
            latest.TryGetValue(id, out var last);
            transportByStudent.TryGetValue(id, out var t);
            arrivalByStudent.TryGetValue(id, out var checkIn);
            var present = arrivalByStudent.ContainsKey(id);

            result[id] = new StudentSafetySnapshot(
                StudentId: id,
                Status: MapStatus(last?.EventType),
                LastEventType: last?.EventType,
                LastEventAt: last?.Timestamp,
                CheckInTime: present ? checkIn : null,
                PresentToday: present,
                LateToday: present && todaySchedule.IsLate(today, checkIn),
                BusNumber: t?.BusNumber,
                RouteName: t?.RouteName,
                DriverName: t?.DriverName,
                HasPhoto: withPhoto.Contains(id));
        }

        return result;
    }

    public async Task<List<StudentJourneyStatusDto>> GetLiveJourneyStatusesAsync(CancellationToken ct = default)
    {
        var dayStart = _clock.TodayStartUtc;

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.Status == RecordStatus.Active)
            .Select(s => new { s.StudentId, s.FullName, s.Grade })
            .OrderBy(s => s.FullName)
            .ToListAsync(ct);

        var ids = students.Select(s => s.StudentId).ToList();
        var snapshots = await GetSnapshotsAsync(ids, ct);

        var todaysEvents = await _db.JourneyEvents.AsNoTracking()
            .Include(e => e.Student)
            .Where(e => e.Timestamp >= dayStart)
            .OrderBy(e => e.Timestamp)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToListAsync(ct);
        var eventsByStudent = todaysEvents.GroupBy(e => e.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        return students
            .Select(s =>
            {
                var snap = snapshots.TryGetValue(s.StudentId, out var found) ? found : StudentSafetySnapshot.Unknown(s.StudentId);
                eventsByStudent.TryGetValue(s.StudentId, out var events);
                return new StudentJourneyStatusDto(
                    s.StudentId, s.FullName, s.Grade, snap.HasPhoto,
                    snap.Status, snap.LastEventType, snap.LastEventAt,
                    snap.BusNumber, snap.RouteName,
                    events ?? new List<JourneyEventDto>());
            })
            // Students with activity first, most recent at the top; idle students after.
            .OrderByDescending(s => s.LastEventAt ?? DateTime.MinValue)
            .ThenBy(s => s.StudentName)
            .ToList();
    }

    public async Task<AttendanceStatsDto> GetAttendanceStatsAsync(int studentId, CancellationToken ct = default)
    {
        var today = _clock.Today;
        var regular = (await _schedule.GetAsync(today, today, ct)).Regular;
        var windowDays = Math.Max(1, regular.AttendanceWindowDays);
        var from = today.AddDays(-windowDays);
        var schedule = await _schedule.GetAsync(from, today, ct);

        var arrivals = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Date >= from && a.EventType == AttendanceEventType.SchoolArrival)
            .Select(a => new { a.Date, a.Time })
            .ToListAsync(ct);

        // Only arrivals on scheduled school days count; one on a holiday is off-schedule.
        var byDay = arrivals
            .Where(a => schedule.IsSchoolDay(a.Date))
            .GroupBy(a => a.Date)
            .ToDictionary(g => g.Key, g => g.Min(a => a.Time));
        var present = byDay.Count;
        var late = byDay.Count(d => schedule.IsLate(d.Key, d.Value));

        // The denominator is the school days the calendar scheduled (SchoolDaysObserved keeps
        // its name for API compatibility). Today counts only once it is decided: the student
        // has arrived, or the missing check-in time has passed.
        var scheduledDays = schedule.CountSchoolDays(from, today.AddDays(-1));
        var todayInfo = schedule.For(today);
        if (todayInfo.IsSchoolDay &&
            (byDay.ContainsKey(today) || TimeOnly.FromDateTime(_clock.LocalNow) > todayInfo.MissingCheckInAfter))
            scheduledDays++;
        var rate = scheduledDays == 0 ? 0 : Math.Min(100, Math.Round(present * 100.0 / scheduledDays, 1));

        return new AttendanceStatsDto(windowDays, present, late, scheduledDays, rate);
    }

    /// <summary>Maps the last recorded checkpoint to a plain-language position.</summary>
    internal static StudentSafetyStatus MapStatus(JourneyEventType? lastEvent) => lastEvent switch
    {
        JourneyEventType.BusBoarding => StudentSafetyStatus.OnBusToSchool,
        JourneyEventType.SchoolArrival => StudentSafetyStatus.AtSchool,
        JourneyEventType.SchoolDeparture => StudentSafetyStatus.LeftSchool,
        JourneyEventType.BusDropoff => StudentSafetyStatus.DroppedOffHome,
        _ => StudentSafetyStatus.NoActivityToday
    };
}
