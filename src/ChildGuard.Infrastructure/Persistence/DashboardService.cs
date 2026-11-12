using ChildGuard.Application.Common;
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
    /// <summary>Days of history behind the attendance trend line.</summary>
    private const int TrendDays = 7;

    private readonly ChildGuardDbContext _db;
    private readonly ISafetyReadService _safety;
    private readonly SchoolOptions _school;
    private readonly SchoolClock _clock;

    public DashboardService(ChildGuardDbContext db, ISafetyReadService safety, SchoolOptions school, SchoolClock clock)
    {
        _db = db;
        _safety = safety;
        _school = school;
        _clock = clock;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        // "Today" is the school's calendar day, bounded by local midnights in UTC.
        var today = _clock.Today;
        var (todayStart, todayEnd) = _clock.RangeUtc(today, today);

        var roster = await _db.Students.AsNoTracking()
            .Where(s => s.Status == RecordStatus.Active)
            .Select(s => new { s.StudentId, s.FullName, s.Grade })
            .ToListAsync(ct);
        var rosterIds = roster.Select(s => s.StudentId).ToList();

        var activeRfid = await _db.RfidCards.CountAsync(c => c.Status == RfidCardStatus.Active, ct);
        var totalGuardians = await _db.Guardians.CountAsync(g => g.Status == RecordStatus.Active, ct);
        var activeBuses = await _db.Buses.CountAsync(b => b.Status == RecordStatus.Active, ct);
        var inactiveBuses = await _db.Buses.CountAsync(b => b.Status != RecordStatus.Active, ct);
        var driverCount = await _db.Drivers.CountAsync(d => d.Status == RecordStatus.Active, ct);
        var routeCount = await _db.Routes.CountAsync(r => r.Status == RecordStatus.Active, ct);
        var todaysAttendance = await _db.AttendanceRecords.CountAsync(a => a.Date == today, ct);
        var todaysJourney = await _db.JourneyEvents.CountAsync(e => e.Timestamp >= todayStart && e.Timestamp < todayEnd, ct);
        var todaysVerifications = await _db.GuardianVerifications.CountAsync(v => v.AttemptedAt >= todayStart && v.AttemptedAt < todayEnd, ct);

        var todaysVerificationRows = await _db.GuardianVerifications.AsNoTracking()
            .Where(v => v.AttemptedAt >= todayStart && v.AttemptedAt < todayEnd)
            .Select(v => new { v.Result, v.PickupApproved, v.ApprovedByAdminId })
            .ToListAsync(ct);
        var verificationStatus = new VerificationStatusDto(
            Matched: todaysVerificationRows.Count(v => v.Result == VerificationResult.Match),
            NoMatch: todaysVerificationRows.Count(v => v.Result == VerificationResult.NoMatch),
            PickupsApproved: todaysVerificationRows.Count(v => v.PickupApproved),
            PickupsRejected: todaysVerificationRows.Count(v => !v.PickupApproved && v.ApprovedByAdminId != null));

        // Matched but never decided on — the queue an admin still has to work through.
        var pendingVerifications = await _db.GuardianVerifications
            .CountAsync(v => v.Result == VerificationResult.Match && !v.PickupApproved && v.ApprovedByAdminId == null, ct);

        var recent = await _db.JourneyEvents.AsNoTracking()
            .Include(e => e.Student)
            .OrderByDescending(e => e.Timestamp)
            .Take(12)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToListAsync(ct);

        var snapshots = await _safety.GetSnapshotsAsync(rosterIds, ct);

        var attendance = BuildAttendanceOverview(roster.Count, snapshots.Values);
        var previousRate = await GetPresentRateAsync(today.AddDays(-1), roster.Count, ct);
        attendance = attendance with
        {
            PreviousDayPresentRate = previousRate,
            RateChange = previousRate is null ? null : Math.Round(attendance.PresentRate - previousRate.Value, 1)
        };

        var journey = new JourneyOverviewDto(
            OnBusToSchool: snapshots.Values.Count(s => s.Status == StudentSafetyStatus.OnBusToSchool),
            AtSchool: snapshots.Values.Count(s => s.Status == StudentSafetyStatus.AtSchool),
            LeftSchool: snapshots.Values.Count(s => s.Status == StudentSafetyStatus.LeftSchool),
            DroppedOffHome: snapshots.Values.Count(s => s.Status == StudentSafetyStatus.DroppedOffHome),
            NoActivityToday: snapshots.Values.Count(s => s.Status == StudentSafetyStatus.NoActivityToday));

        var assignedStudents = await _db.StudentBusAssignments.AsNoTracking()
            .Where(a => a.Status == RecordStatus.Active)
            .Select(a => a.StudentId)
            .Distinct()
            .CountAsync(ct);
        var scannersTotal = await _db.RfidDevices.CountAsync(ct);
        var scannersConnected = await _db.RfidDevices.CountAsync(d => d.ConnectionStatus == RfidConnectionStatus.Connected, ct);

        var transport = new TransportOverviewDto(
            activeBuses, inactiveBuses, driverCount, routeCount,
            assignedStudents, Math.Max(0, roster.Count - assignedStudents),
            scannersConnected, scannersTotal);

        var trend = await BuildTrendAsync(today, roster.Count, ct);
        var byGrade = BuildGradeBreakdown(roster.Select(r => (r.StudentId, r.Grade)), snapshots);
        var alerts = await BuildAlertsAsync(roster.ToDictionary(r => r.StudentId, r => r.FullName), snapshots, ct);

        return new DashboardSummaryDto(
            roster.Count, activeRfid, totalGuardians, activeBuses,
            todaysAttendance, todaysJourney, todaysVerifications, verificationStatus, recent,
            attendance, journey, transport, pendingVerifications, trend, alerts, byGrade);
    }

    private AttendanceOverviewDto BuildAttendanceOverview(int rosterSize, IEnumerable<StudentSafetySnapshot> snapshots)
    {
        var list = snapshots.ToList();
        var present = list.Count(s => s.PresentToday);
        var late = list.Count(s => s.LateToday);
        var absent = Math.Max(0, rosterSize - present);
        var rate = rosterSize == 0 ? 0 : Math.Round(present * 100.0 / rosterSize, 1);
        return new AttendanceOverviewDto(
            rosterSize, present, late, absent, rate, null, null,
            _school.LateAfterTime.ToString("HH:mm"));
    }

    private async Task<double?> GetPresentRateAsync(DateOnly date, int rosterSize, CancellationToken ct)
    {
        if (rosterSize == 0) return null;
        var present = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Date == date && a.EventType == AttendanceEventType.SchoolArrival)
            .Select(a => a.StudentId)
            .Distinct()
            .CountAsync(ct);
        // No arrivals at all means the school was closed, not that everyone was
        // absent — reporting 0% there would be a fabricated comparison.
        return present == 0 ? null : Math.Round(present * 100.0 / rosterSize, 1);
    }

    private async Task<List<AttendanceTrendPointDto>> BuildTrendAsync(DateOnly today, int rosterSize, CancellationToken ct)
    {
        var from = today.AddDays(-(TrendDays - 1));
        var rows = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Date >= from && a.Date <= today && a.EventType == AttendanceEventType.SchoolArrival)
            .Select(a => new { a.Date, a.StudentId })
            .ToListAsync(ct);

        var presentByDate = rows
            .GroupBy(r => r.Date)
            .ToDictionary(g => g.Key, g => g.Select(r => r.StudentId).Distinct().Count());

        return Enumerable.Range(0, TrendDays)
            .Select(offset =>
            {
                var date = from.AddDays(offset);
                var present = presentByDate.TryGetValue(date, out var count) ? count : 0;
                var rate = rosterSize == 0 ? 0 : Math.Round(present * 100.0 / rosterSize, 1);
                return new AttendanceTrendPointDto(date, present, rosterSize, rate);
            })
            .ToList();
    }

    private static List<GradeAttendanceDto> BuildGradeBreakdown(
        IEnumerable<(int StudentId, string? Grade)> roster,
        IReadOnlyDictionary<int, StudentSafetySnapshot> snapshots) =>
        roster
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Grade) ? "Unassigned" : r.Grade!)
            .Select(g =>
            {
                var size = g.Count();
                var present = g.Count(r => snapshots.TryGetValue(r.StudentId, out var s) && s.PresentToday);
                return new GradeAttendanceDto(g.Key, size, present, size == 0 ? 0 : Math.Round(present * 100.0 / size, 1));
            })
            .OrderBy(g => g.Grade, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Builds the alert centre from recorded conditions only. Each alert names what
    /// happened, when, and what the admin should do about it.
    /// </summary>
    private async Task<List<SafetyAlertDto>> BuildAlertsAsync(
        IReadOnlyDictionary<int, string> rosterNames,
        IReadOnlyDictionary<int, StudentSafetySnapshot> snapshots,
        CancellationToken ct)
    {
        var alerts = new List<SafetyAlertDto>();
        var todayStart = _clock.TodayStartUtc;

        // 1. Failed face verifications today — the most serious routine condition.
        var failedVerifications = await _db.GuardianVerifications.AsNoTracking()
            .Include(v => v.Student)
            .Include(v => v.Guardian)
            .Where(v => v.AttemptedAt >= todayStart && v.Result == VerificationResult.NoMatch)
            .OrderByDescending(v => v.AttemptedAt)
            .Take(10)
            .Select(v => new { v.Id, v.AttemptedAt, v.StudentId, StudentName = v.Student.FullName, GuardianName = v.Guardian.FullName })
            .ToListAsync(ct);
        foreach (var v in failedVerifications)
        {
            alerts.Add(new SafetyAlertDto(
                $"verification-{v.Id}", AlertSeverity.Critical, "Verification",
                "Pickup verification failed",
                $"{v.GuardianName} did not match the registered face for {v.StudentName}.",
                "Confirm the guardian's identity in person before releasing the student, and re-run verification.",
                v.AttemptedAt, v.StudentId, v.StudentName, "/Verification"));
        }

        // 2. Missing check-ins, but only once the school day is far enough along
        //    that an absence is meaningful rather than simply not-yet.
        // School wall-clock time, not the server's (which may run in UTC).
        var nowLocalTime = TimeOnly.FromDateTime(_clock.LocalNow);
        if (nowLocalTime > _school.MissingCheckInAfterTime)
        {
            var missing = snapshots.Values
                .Where(s => !s.PresentToday && s.Status is StudentSafetyStatus.NoActivityToday or StudentSafetyStatus.OnBusToSchool)
                .Take(10);
            foreach (var s in missing)
            {
                var name = rosterNames.TryGetValue(s.StudentId, out var n) ? n : $"Student #{s.StudentId}";
                alerts.Add(new SafetyAlertDto(
                    $"no-checkin-{s.StudentId}", AlertSeverity.Warning, "Attendance",
                    "Student has not checked in",
                    $"{name} has no school arrival recorded after {_school.MissingCheckInAfter}.",
                    "Contact the guardian to confirm the student's whereabouts.",
                    null, s.StudentId, name, $"/Students/Details/{s.StudentId}"));
            }
        }

        // 3. Pending pickup decisions left open.
        var pending = await _db.GuardianVerifications.AsNoTracking()
            .Include(v => v.Student)
            .Where(v => v.Result == VerificationResult.Match && !v.PickupApproved && v.ApprovedByAdminId == null)
            .OrderByDescending(v => v.AttemptedAt)
            .Take(5)
            .Select(v => new { v.Id, v.AttemptedAt, v.StudentId, StudentName = v.Student.FullName })
            .ToListAsync(ct);
        foreach (var v in pending)
        {
            alerts.Add(new SafetyAlertDto(
                $"pending-{v.Id}", AlertSeverity.Warning, "Verification",
                "Pickup awaiting approval",
                $"{v.StudentName} has a verified guardian waiting on a handover decision.",
                "Approve or reject the handover on the Pickup & Drop-off desk.",
                v.AttemptedAt, v.StudentId, v.StudentName, "/Verification"));
        }

        // 4. Scanners that have never reported in or dropped off.
        var offlineScanners = await _db.RfidDevices.AsNoTracking()
            .Where(d => d.ConnectionStatus != RfidConnectionStatus.Connected)
            .Select(d => new { d.RfidDeviceId, d.DeviceLabel, d.LastSeenAt })
            .ToListAsync(ct);
        foreach (var d in offlineScanners)
        {
            alerts.Add(new SafetyAlertDto(
                $"scanner-{d.RfidDeviceId}", AlertSeverity.Warning, "System",
                $"Scanner {d.DeviceLabel} is disconnected",
                d.LastSeenAt is null ? "This scanner has never reported in." : $"Last seen {d.LastSeenAt:g} UTC.",
                "Check the reader's power and network connection at its checkpoint.",
                d.LastSeenAt, null, null, "/Settings"));
        }

        // 5. Active students with no RFID card — they cannot be identified at a checkpoint.
        var withoutCard = await _db.Students.AsNoTracking()
            .Where(s => s.Status == RecordStatus.Active && s.RfidCard == null)
            .OrderBy(s => s.StudentId)
            .Select(s => new { s.StudentId, s.FullName })
            .Take(5)
            .ToListAsync(ct);
        foreach (var s in withoutCard)
        {
            alerts.Add(new SafetyAlertDto(
                $"no-rfid-{s.StudentId}", AlertSeverity.Info, "RFID",
                "Student has no RFID card",
                $"{s.FullName} cannot be identified at a checkpoint until a card is assigned.",
                "Assign an RFID card from the RFID page.",
                null, s.StudentId, s.FullName, "/Rfid"));
        }

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.OccurredAt ?? DateTime.MinValue)
            .ToList();
    }
}
