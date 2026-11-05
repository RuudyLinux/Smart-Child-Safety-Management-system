using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
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

    public SafetyReadService(ChildGuardDbContext db, SchoolOptions school, SchoolClock clock)
    {
        _db = db;
        _school = school;
        _clock = clock;
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

        var lateAfter = _school.LateAfterTime;
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
                LateToday: present && checkIn > lateAfter,
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
        var windowDays = Math.Max(1, _school.AttendanceWindowDays);
        var from = _clock.Today.AddDays(-windowDays);

        var arrivals = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Date >= from && a.EventType == AttendanceEventType.SchoolArrival)
            .Select(a => new { a.Date, a.Time })
            .ToListAsync(ct);

        // "School days observed" counts days this school actually recorded any
        // arrival for anyone — the only evidence available that the school was open.
        var observedDays = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Date >= from && a.EventType == AttendanceEventType.SchoolArrival)
            .Select(a => a.Date)
            .Distinct()
            .CountAsync(ct);

        var lateAfter = _school.LateAfterTime;
        var byDay = arrivals.GroupBy(a => a.Date).Select(g => g.Min(a => a.Time)).ToList();
        var present = byDay.Count;
        var late = byDay.Count(t => t > lateAfter);
        var rate = observedDays == 0 ? 0 : Math.Round(present * 100.0 / observedDays, 1);

        return new AttendanceStatsDto(windowDays, present, late, observedDays, rate);
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
