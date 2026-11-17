using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Domain.Entities;
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
    private readonly ISchoolScheduleProvider _schedule;

    /// <summary>How far back to look for the previous school day the rate is compared with.</summary>
    private const int PreviousSchoolDayLookback = 14;

    public DashboardService(ChildGuardDbContext db, ISafetyReadService safety, SchoolOptions school, SchoolClock clock,
        ISchoolScheduleProvider? schedule = null)
    {
        _db = db;
        _safety = safety;
        _school = school;
        _clock = clock;
        _schedule = schedule ?? new ConfiguredSchoolScheduleProvider(school);
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
        var activeBuses = await _db.Buses.CountAsync(b => b.Status == BusStatus.Active, ct);
        var inactiveBuses = await _db.Buses.CountAsync(b => b.Status != BusStatus.Active, ct);
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

        var schedule = await _schedule.GetAsync(today.AddDays(-PreviousSchoolDayLookback), today, ct);
        var todayInfo = schedule.For(today);

        var attendance = BuildAttendanceOverview(roster.Count, snapshots.Values, todayInfo, schedule.Regular);
        // Compared with the previous school day, so Monday is compared with Friday, not Sunday.
        var previousSchoolDay = Enumerable.Range(1, PreviousSchoolDayLookback)
            .Select(n => today.AddDays(-n))
            .Cast<DateOnly?>()
            .FirstOrDefault(d => schedule.IsSchoolDay(d!.Value));
        var previousRate = previousSchoolDay is null ? null : await GetPresentRateAsync(previousSchoolDay.Value, roster.Count, ct);
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

        var trend = await BuildTrendAsync(today, roster.Count, schedule, ct);
        var byGrade = BuildGradeBreakdown(roster.Select(r => (r.StudentId, r.Grade)), snapshots);
        var alerts = await BuildAlertsAsync(roster.ToDictionary(r => r.StudentId, r => r.FullName), snapshots, todayInfo, schedule.Regular, ct);
        var (admin, adminAlerts) = await BuildAdminOverviewAsync(today, ct);
        alerts = alerts.Concat(adminAlerts)
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.OccurredAt ?? DateTime.MinValue)
            .ToList();

        return new DashboardSummaryDto(
            roster.Count, activeRfid, totalGuardians, activeBuses,
            todaysAttendance, todaysJourney, todaysVerifications, verificationStatus, recent,
            attendance, journey, transport, pendingVerifications, trend, alerts, byGrade, admin,
            SchoolCalendarService.ToDto(todayInfo));
    }

    /// <summary>Most records of one kind named in the alert centre; the KPI cards carry the full counts.</summary>
    private const int AlertListLimit = 5;

    /// <summary>
    /// People, RFID inventory and fleet KPIs, plus alerts for records an admin should complete or renew:
    /// students missing a guardian or transport, lost or damaged cards still held, licences, buses off the road.
    /// Counts come straight from the records; nothing is estimated.
    /// </summary>
    private async Task<(AdminOverviewDto, List<SafetyAlertDto>)> BuildAdminOverviewAsync(DateOnly today, CancellationToken ct)
    {
        var students = await _db.Students.AsNoTracking()
            .GroupBy(s => new { s.Status, Archived = s.ArchivedAt != null })
            .Select(g => new { g.Key.Status, g.Key.Archived, Count = g.Count() }).ToListAsync(ct);
        var guardians = await _db.Guardians.AsNoTracking()
            .GroupBy(s => new { s.Status, Archived = s.ArchivedAt != null })
            .Select(g => new { g.Key.Status, g.Key.Archived, Count = g.Count() }).ToListAsync(ct);
        var people = new PeopleOverviewDto(
            students.Sum(x => x.Count), students.Where(x => x.Status == RecordStatus.Active).Sum(x => x.Count),
            students.Where(x => x.Status != RecordStatus.Active && !x.Archived).Sum(x => x.Count), students.Where(x => x.Archived).Sum(x => x.Count),
            guardians.Sum(x => x.Count), guardians.Where(x => x.Status == RecordStatus.Active).Sum(x => x.Count),
            guardians.Where(x => x.Status != RecordStatus.Active && !x.Archived).Sum(x => x.Count), guardians.Where(x => x.Archived).Sum(x => x.Count));

        var cards = await _db.RfidCards.AsNoTracking()
            .GroupBy(c => new { c.Status, Held = c.StudentId != null })
            .Select(g => new { g.Key.Status, g.Key.Held, Count = g.Count() }).ToListAsync(ct);
        int Cards(RfidCardStatus s) => cards.Where(x => x.Status == s).Sum(x => x.Count);
        var rfid = new RfidOverviewDto(cards.Sum(x => x.Count), cards.Where(x => x.Held).Sum(x => x.Count),
            cards.Where(x => x.Status == RfidCardStatus.Active && !x.Held).Sum(x => x.Count),
            Cards(RfidCardStatus.Inactive), Cards(RfidCardStatus.Lost), Cards(RfidCardStatus.Damaged), Cards(RfidCardStatus.Retired));

        var drivers = await _db.Drivers.AsNoTracking()
            .Select(d => new { d.DriverId, d.FullName, d.Status, d.LicenseExpiresOn }).ToListAsync(ct);
        var buses = await _db.Buses.AsNoTracking()
            .Select(b => new { b.BusId, b.BusNumber, b.Status, b.Capacity }).ToListAsync(ct);
        var loads = await _db.StudentBusAssignments.AsNoTracking()
            .Where(a => a.Status == RecordStatus.Active)
            .GroupBy(a => a.BusId).Select(g => new { BusId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BusId, x => x.Count, ct);
        var routes = await _db.Routes.AsNoTracking().GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Buses(BusStatus s) => buses.Count(b => b.Status == s);
        var fleet = new FleetOverviewDto(
            drivers.Count, drivers.Count(d => d.Status == RecordStatus.Active),
            buses.Count, Buses(BusStatus.Active), Buses(BusStatus.UnderMaintenance), Buses(BusStatus.OutOfService), Buses(BusStatus.Inactive),
            routes.Sum(r => r.Count), routes.Where(r => r.Status == RecordStatus.Active).Sum(r => r.Count),
            loads.Values.Sum(),
            buses.OrderBy(b => b.BusNumber)
                .Select(b => new BusLoadDto(b.BusId, b.BusNumber, b.Status, b.Capacity, loads.GetValueOrDefault(b.BusId))).ToList());

        // Gaps among active students.
        var active = _db.Students.AsNoTracking().Where(s => s.Status == RecordStatus.Active);
        var noGuardian = active.Where(s => !_db.StudentGuardians.Any(l => l.StudentId == s.StudentId && l.Guardian.Status == RecordStatus.Active));
        var noTransport = active.Where(s => !_db.StudentBusAssignments.Any(a => a.StudentId == s.StudentId && a.Status == RecordStatus.Active));
        var noCard = active.Where(s => s.RfidCard == null);

        var activeDrivers = drivers.Where(d => d.Status == RecordStatus.Active)
            .Select(d => (d.DriverId, d.FullName, d.LicenseExpiresOn, State: LicenceRules.State(d.LicenseExpiresOn, today))).ToList();
        var gaps = new CoverageGapsDto(
            await noGuardian.CountAsync(ct), await noCard.CountAsync(ct), await noTransport.CountAsync(ct),
            activeDrivers.Count(d => d.State == LicenceState.Expired), activeDrivers.Count(d => d.State == LicenceState.ExpiringSoon),
            activeDrivers.Count(d => d.State == LicenceState.NotRecorded));

        var alerts = new List<SafetyAlertDto>();

        foreach (var s in await noGuardian.OrderBy(s => s.StudentId).Take(AlertListLimit).Select(s => new { s.StudentId, s.FullName }).ToListAsync(ct))
            alerts.Add(new SafetyAlertDto($"no-guardian-{s.StudentId}", AlertSeverity.Warning, "Guardians",
                "Student has no active guardian",
                $"{s.FullName} has no active guardian linked, so nobody can be notified or collect them.",
                "Link a guardian from the student's profile.", null, s.StudentId, s.FullName, $"/Students/Details/{s.StudentId}"));

        foreach (var s in await noTransport.OrderBy(s => s.StudentId).Take(AlertListLimit).Select(s => new { s.StudentId, s.FullName }).ToListAsync(ct))
            alerts.Add(new SafetyAlertDto($"no-transport-{s.StudentId}", AlertSeverity.Info, "Transport",
                "Student has no school transport",
                $"{s.FullName} is not assigned to a bus. Ignore this if the student travels privately.",
                "Assign a bus on the Transportation page if the student uses school transport.", null, s.StudentId, s.FullName, "/Transportation"));

        var badCards = await _db.RfidCards.AsNoTracking()
            .Where(c => c.StudentId != null && (c.Status == RfidCardStatus.Lost || c.Status == RfidCardStatus.Damaged))
            .OrderBy(c => c.RfidCardId).Take(AlertListLimit)
            .Select(c => new { c.RfidCardId, c.Status, c.StudentId, StudentName = c.Student!.FullName }).ToListAsync(ct);
        foreach (var c in badCards)
            alerts.Add(new SafetyAlertDto($"card-{c.RfidCardId}", AlertSeverity.Warning, "RFID",
                $"Student's card is {(c.Status == RfidCardStatus.Lost ? "lost" : "damaged")}",
                $"{c.StudentName} still holds a card marked {c.Status.ToString().ToLowerInvariant()} and cannot be scanned reliably.",
                "Replace the card from the RFID page.", null, c.StudentId, c.StudentName, $"/Rfid/Details/{c.RfidCardId}"));

        foreach (var d in activeDrivers.Where(d => d.State is LicenceState.Expired or LicenceState.ExpiringSoon)
                     .OrderBy(d => d.LicenseExpiresOn).Take(AlertListLimit))
        {
            var expired = d.State == LicenceState.Expired;
            alerts.Add(new SafetyAlertDto($"licence-{d.DriverId}", expired ? AlertSeverity.Critical : AlertSeverity.Warning, "Transport",
                expired ? "Driver licence has expired" : "Driver licence expires soon",
                $"{d.FullName}'s licence {(expired ? "expired" : "expires")} on {d.LicenseExpiresOn:d MMM yyyy}.",
                expired ? "Do not let this driver drive until the licence is renewed, then update the profile."
                        : "Ask the driver to renew the licence and update the profile.",
                null, null, null, $"/Transportation/Driver/{d.DriverId}"));
        }

        foreach (var b in buses.Where(b => b.Status is BusStatus.UnderMaintenance or BusStatus.OutOfService).OrderBy(b => b.BusNumber).Take(AlertListLimit))
        {
            var riders = loads.GetValueOrDefault(b.BusId);
            alerts.Add(new SafetyAlertDto($"bus-{b.BusId}", riders > 0 ? AlertSeverity.Warning : AlertSeverity.Info, "Transport",
                $"Bus {b.BusNumber} is {(b.Status == BusStatus.UnderMaintenance ? "under maintenance" : "out of service")}",
                riders > 0 ? $"{riders} student{(riders == 1 ? " is" : "s are")} still assigned to it." : "No students are assigned to it.",
                riders > 0 ? "Move its students to another bus, or return it to service." : "Return it to service when it is ready.",
                null, null, null, $"/Transportation/Bus/{b.BusId}"));
        }

        return (new AdminOverviewDto(people, rfid, fleet, gaps), alerts);
    }

    private static AttendanceOverviewDto BuildAttendanceOverview(int rosterSize, IEnumerable<StudentSafetySnapshot> snapshots,
        SchoolDayInfo today, SchoolTimingSettings regular)
    {
        var list = snapshots.ToList();
        var present = list.Count(s => s.PresentToday);
        var late = list.Count(s => s.LateToday);
        // Nobody is absent on a day the school is closed.
        var absent = today.IsSchoolDay ? Math.Max(0, rosterSize - present) : 0;
        var rate = rosterSize == 0 ? 0 : Math.Round(present * 100.0 / rosterSize, 1);
        return new AttendanceOverviewDto(
            rosterSize, present, late, absent, rate, null, null,
            (today.LateArrivalAfter ?? regular.LateArrivalAfter).ToString("HH:mm"),
            today.IsSchoolDay,
            today.DismissalTime?.ToString("HH:mm"));
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

    private async Task<List<AttendanceTrendPointDto>> BuildTrendAsync(DateOnly today, int rosterSize, SchoolSchedule schedule, CancellationToken ct)
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
                return new AttendanceTrendPointDto(date, present, rosterSize, rate, schedule.IsSchoolDay(date));
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
    /// Alerts tied to the school day's timings: an early departure (informational: a
    /// guardian may have collected a sick child), a student still at school once the
    /// pickup grace after dismissal has passed, and arrivals recorded on a closed day.
    /// </summary>
    private async Task<List<SafetyAlertDto>> BuildDismissalAlertsAsync(
        IReadOnlyDictionary<int, string> rosterNames,
        IReadOnlyDictionary<int, StudentSafetySnapshot> snapshots,
        SchoolDayInfo today,
        SchoolTimingSettings regular,
        TimeOnly nowLocalTime,
        CancellationToken ct)
    {
        var alerts = new List<SafetyAlertDto>();
        string Name(int id) => rosterNames.TryGetValue(id, out var n) ? n : $"Student #{id}";

        if (!today.IsSchoolDay)
        {
            var offSchedule = await _db.AttendanceRecords.AsNoTracking()
                .Where(a => a.Date == today.Date && a.EventType == AttendanceEventType.SchoolArrival)
                .Select(a => a.StudentId)
                .Distinct()
                .CountAsync(ct);
            if (offSchedule > 0)
            {
                var closedAs = today.Kind == SchoolDayKind.Holiday ? $"a holiday ({today.Title})" : "a non-working day";
                alerts.Add(new SafetyAlertDto(
                    $"off-schedule-{today.Date:yyyyMMdd}", AlertSeverity.Info, "Attendance",
                    "Arrivals recorded on a closed day",
                    $"{offSchedule} student(s) scanned in today, which the school calendar marks as {closedAs}.",
                    "If the school opened today, add it as a working day on the School Time page.",
                    null, null, null, "/SchoolTime"));
            }
            return alerts;
        }

        if (today.DismissalTime is not { } dismissal) return alerts;

        var early = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Date == today.Date && a.EventType == AttendanceEventType.SchoolDeparture && a.Time < dismissal)
            .Select(a => new { a.StudentId, a.Time, a.RecordedAtUtc })
            .ToListAsync(ct);
        var earliest = early
            .Where(e => rosterNames.ContainsKey(e.StudentId))
            .GroupBy(e => e.StudentId)
            .Select(g => g.OrderBy(e => e.Time).First())
            .OrderByDescending(e => e.RecordedAtUtc)
            .Take(10);
        foreach (var e in earliest)
        {
            var name = Name(e.StudentId);
            alerts.Add(new SafetyAlertDto(
                $"early-departure-{e.StudentId}", AlertSeverity.Info, "Attendance",
                "Student left before dismissal",
                $"{name} left school at {e.Time:HH:mm}, before dismissal at {dismissal:HH:mm}.",
                "Check the pickup was expected (e.g. the guardian collected the student early).",
                e.RecordedAtUtc, e.StudentId, name, $"/Students/Details/{e.StudentId}"));
        }

        var graceEnds = dismissal.ToTimeSpan() + TimeSpan.FromMinutes(Math.Max(0, regular.PickupGraceMinutes));
        if (nowLocalTime.ToTimeSpan() > graceEnds)
        {
            foreach (var s in snapshots.Values.Where(s => s.Status == StudentSafetyStatus.AtSchool).Take(10))
            {
                var name = Name(s.StudentId);
                alerts.Add(new SafetyAlertDto(
                    $"not-picked-up-{s.StudentId}", AlertSeverity.Warning, "Pickup",
                    "Student not picked up",
                    $"{name} is still at school {regular.PickupGraceMinutes} minutes after dismissal at {dismissal:HH:mm}.",
                    "Contact the guardian and keep the student supervised until collected.",
                    null, s.StudentId, name, $"/Students/Details/{s.StudentId}"));
            }
        }

        return alerts;
    }

    /// <summary>
    /// Builds the alert centre from recorded conditions only. Each alert names what
    /// happened, when, and what the admin should do about it.
    /// </summary>
    private async Task<List<SafetyAlertDto>> BuildAlertsAsync(
        IReadOnlyDictionary<int, string> rosterNames,
        IReadOnlyDictionary<int, StudentSafetySnapshot> snapshots,
        SchoolDayInfo today,
        SchoolTimingSettings regular,
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
        // Never on a weekend or holiday, and judged against today's own timings.
        if (today is { IsSchoolDay: true, MissingCheckInAfter: { } missingAfter } && nowLocalTime > missingAfter)
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
                    $"{name} has no school arrival recorded after {missingAfter:HH:mm}.",
                    "Contact the guardian to confirm the student's whereabouts.",
                    null, s.StudentId, name, $"/Students/Details/{s.StudentId}"));
            }
        }

        // 2b. Dismissal: students who left before it, and students still at school well after it.
        alerts.AddRange(await BuildDismissalAlertsAsync(rosterNames, snapshots, today, regular, nowLocalTime, ct));

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
