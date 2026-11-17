using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>School time management: the regular week, calendar exceptions and the rules built on them.</summary>
public class SchoolCalendarTests
{
    // Nov 2026: Mon 2 .. Sun 8. TestClock's "today" is Fri 6 Nov.
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Friday = new(2026, 11, 6);
    private static readonly DateOnly Saturday = new(2026, 11, 7);
    private static readonly DateOnly Sunday = new(2026, 11, 8);

    private static SchoolTimingSettings Regular() => new SchoolOptions().ToDefaultSettings();

    private static SchoolCalendarDay Exception(DateOnly date, SchoolDayType type, string title = "Test",
        TimeOnly? start = null, TimeOnly? late = null, TimeOnly? dismissal = null, RecordStatus status = RecordStatus.Active) =>
        new() { SchoolCalendarDayId = date.DayNumber, Date = date, DayType = type, Title = title, StartTime = start, LateArrivalAfter = late, DismissalTime = dismissal, Status = status };

    // ------------------------------------------------------------------ SchoolSchedule

    [Fact]
    public void ConfiguredDefaults_MatchTheExistingSchoolSection()
    {
        var r = Regular();
        Assert.Equal(SchoolWeekdays.MondayToFriday, r.WorkingDays);
        Assert.Equal(new TimeOnly(8, 30), r.LateArrivalAfter);
        Assert.Equal(new TimeOnly(9, 30), r.MissingCheckInAfter);
        Assert.Equal(30, r.AttendanceWindowDays);
    }

    [Fact]
    public void WorkingDays_ParseFromConfiguration_AndFallBackWhenEmpty()
    {
        Assert.Equal(SchoolWeekdays.MondayToFriday | SchoolWeekdays.Saturday,
            new SchoolOptions { WorkingDays = "monday, Tuesday,Wednesday,Thursday,Friday,Saturday" }.ToDefaultSettings().WorkingDays);
        Assert.Equal(SchoolWeekdays.MondayToFriday, new SchoolOptions { WorkingDays = "nonsense" }.ToDefaultSettings().WorkingDays);
    }

    [Fact]
    public void RegularWeek_WeekendIsClosed()
    {
        var schedule = new SchoolSchedule(Regular(), Array.Empty<SchoolCalendarDay>());

        Assert.Equal(SchoolDayKind.Regular, schedule.For(Friday).Kind);
        Assert.True(schedule.IsSchoolDay(Friday));
        var sat = schedule.For(Saturday);
        Assert.Equal(SchoolDayKind.NonWorkingDay, sat.Kind);
        Assert.False(sat.IsSchoolDay);
        Assert.Null(sat.LateArrivalAfter);
        Assert.Equal(5, schedule.CountSchoolDays(Monday, Sunday));
    }

    [Fact]
    public void Holiday_ClosesAWeekday_AndArrivalIsNotLate()
    {
        var schedule = new SchoolSchedule(Regular(), new[] { Exception(Friday, SchoolDayType.Holiday, "Diwali") });

        var info = schedule.For(Friday);
        Assert.Equal(SchoolDayKind.Holiday, info.Kind);
        Assert.Equal("Diwali", info.Title);
        Assert.False(info.IsSchoolDay);
        Assert.False(schedule.IsLate(Friday, new TimeOnly(11, 0)));
        Assert.Equal(4, schedule.CountSchoolDays(Monday, Sunday));
    }

    [Fact]
    public void InactiveException_IsIgnored()
    {
        var schedule = new SchoolSchedule(Regular(), new[] { Exception(Friday, SchoolDayType.Holiday, status: RecordStatus.Inactive) });
        Assert.Equal(SchoolDayKind.Regular, schedule.For(Friday).Kind);
    }

    [Fact]
    public void WorkingDay_OpensASaturday_WithRegularTimings()
    {
        var schedule = new SchoolSchedule(Regular(), new[] { Exception(Saturday, SchoolDayType.WorkingDay, "Make-up day") });

        var info = schedule.For(Saturday);
        Assert.True(info.IsSchoolDay);
        Assert.Equal(SchoolDayKind.WorkingDay, info.Kind);
        Assert.Equal(new TimeOnly(8, 30), info.LateArrivalAfter);
        Assert.True(schedule.IsLate(Saturday, new TimeOnly(8, 31)));
    }

    [Fact]
    public void SpecialTiming_ShiftsLateCutoff_AndMissingCheckInKeepsTheRegularGap()
    {
        var schedule = new SchoolSchedule(Regular(), new[]
        {
            Exception(Friday, SchoolDayType.SpecialTiming, "Sports day", start: new TimeOnly(9, 0), late: new TimeOnly(9, 15))
        });

        var info = schedule.For(Friday);
        Assert.Equal(new TimeOnly(9, 0), info.StartTime);
        Assert.Equal(new TimeOnly(9, 15), info.LateArrivalAfter);
        Assert.Equal(new TimeOnly(10, 15), info.MissingCheckInAfter); // regular gap is 60 minutes
        Assert.Equal(new TimeOnly(14, 30), info.DismissalTime);       // not overridden
        Assert.False(schedule.IsLate(Friday, new TimeOnly(9, 0)));    // late under the regular cutoff, not today's
        Assert.True(schedule.IsLate(Friday, new TimeOnly(9, 16)));
    }

    [Fact]
    public void HalfDay_UsesItsDismissal()
    {
        var schedule = new SchoolSchedule(Regular(), new[] { Exception(Friday, SchoolDayType.HalfDay, dismissal: new TimeOnly(11, 30)) });
        var info = schedule.For(Friday);
        Assert.Equal(SchoolDayKind.HalfDay, info.Kind);
        Assert.Equal(new TimeOnly(11, 30), info.DismissalTime);
        Assert.Equal(new TimeOnly(8, 30), info.LateArrivalAfter);
    }

    [Fact]
    public void LateCutoff_IsExclusive()
    {
        var schedule = new SchoolSchedule(Regular(), Array.Empty<SchoolCalendarDay>());
        Assert.False(schedule.IsLate(Friday, new TimeOnly(8, 30)));
        Assert.True(schedule.IsLate(Friday, new TimeOnly(8, 30, 1)));
    }

    // ------------------------------------------------------------------ SchoolCalendarService

    private static (SchoolCalendarService Service, FakeSchoolCalendarRepository Repo) CreateService()
    {
        var repo = new FakeSchoolCalendarRepository();
        var options = new SchoolOptions();
        var (clock, _) = TestClock.Create();
        return (new SchoolCalendarService(repo, new SchoolScheduleProvider(repo, options), options, clock), repo);
    }

    private static UpdateSchoolTimingSettingsRequest Settings(
        List<DayOfWeek>? days = null, string start = "08:00", string late = "08:30", string missing = "09:30", string dismissal = "14:30",
        int grace = 30, int window = 30) =>
        new(days ?? new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday },
            TimeOnly.Parse(start), TimeOnly.Parse(late), TimeOnly.Parse(missing), TimeOnly.Parse(dismissal), grace, window);

    [Fact]
    public async Task GetSettings_BeforeAnySave_ReportsConfiguredValues()
    {
        var (service, _) = CreateService();
        var dto = await service.GetSettingsAsync();
        Assert.False(dto.IsCustomized);
        Assert.Equal(5, dto.WorkingDays.Count);
        Assert.Equal("Asia/Kolkata", dto.TimeZone);
    }

    [Fact]
    public async Task UpdateSettings_SavesAndChangesTheSchedule()
    {
        var (service, repo) = CreateService();

        var result = await service.UpdateSettingsAsync(Settings(late: "08:45"), adminId: 7);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.IsCustomized);
        Assert.Equal(7, repo.Settings!.UpdatedByAdminId);
        var today = await service.GetTodayAsync();
        Assert.Equal(new TimeOnly(8, 45), today.LateArrivalAfter);
        var cal = await service.GetCalendarAsync(Monday, Sunday);
        Assert.Equal(6, cal.Value!.SchoolDays); // Saturday now included
    }

    [Theory]
    [InlineData("08:00", "07:59", "09:30", "14:30", 30, 30, SchoolCalendarService.StartAfterLate)]
    [InlineData("08:00", "08:30", "08:29", "14:30", 30, 30, SchoolCalendarService.LateAfterMissing)]
    [InlineData("08:00", "08:30", "09:30", "08:00", 30, 30, SchoolCalendarService.DismissalBeforeStart)]
    [InlineData("08:00", "08:30", "09:30", "14:30", 241, 30, SchoolCalendarService.GraceOutOfRange)]
    [InlineData("08:00", "08:30", "09:30", "14:30", 30, 0, SchoolCalendarService.WindowOutOfRange)]
    public async Task UpdateSettings_RejectsTimesOutOfOrder(string start, string late, string missing, string dismissal, int grace, int window, string expected)
    {
        var (service, repo) = CreateService();
        var result = await service.UpdateSettingsAsync(Settings(null, start, late, missing, dismissal, grace, window), 1);
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
        Assert.Null(repo.Settings);
    }

    [Fact]
    public async Task UpdateSettings_RequiresASchoolDay()
    {
        var (service, _) = CreateService();
        var result = await service.UpdateSettingsAsync(Settings(days: new List<DayOfWeek>()), 1);
        Assert.Equal(SchoolCalendarService.NoWorkingDays, result.Error);
    }

    [Fact]
    public async Task CreateHoliday_ClosesTheDate()
    {
        var (service, _) = CreateService();

        var result = await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, SchoolDayType.Holiday, " Diwali ", null, null, null));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Diwali", result.Value!.Title);
        var today = await service.GetTodayAsync();
        Assert.False(today.IsSchoolDay);
        Assert.Equal(SchoolDayKind.Holiday, today.Kind);
    }

    [Theory]
    [InlineData(SchoolDayType.Holiday, "Diwali", "09:00", null, SchoolCalendarService.HolidayHasTimes)]
    [InlineData(SchoolDayType.HalfDay, "Half", null, null, SchoolCalendarService.HalfDayNeedsDismissal)]
    [InlineData(SchoolDayType.SpecialTiming, "Special", null, null, SchoolCalendarService.SpecialTimingNeedsTime)]
    [InlineData(SchoolDayType.Holiday, "  ", null, null, SchoolCalendarService.TitleRequired)]
    [InlineData(SchoolDayType.HalfDay, "Half", null, "07:00", SchoolCalendarService.DismissalBeforeStart)]
    public async Task CreateException_Validates(SchoolDayType type, string title, string? start, string? dismissal, string expected)
    {
        var (service, _) = CreateService();
        var result = await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, type, title,
            start is null ? null : TimeOnly.Parse(start), null, dismissal is null ? null : TimeOnly.Parse(dismissal)));
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task CreateException_RejectsSecondEntryOnTheSameDate()
    {
        var (service, _) = CreateService();
        await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, SchoolDayType.Holiday, "A", null, null, null));
        var second = await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, SchoolDayType.Holiday, "B", null, null, null));
        Assert.Equal(SchoolCalendarService.DuplicateDate, second.Error);
    }

    [Fact]
    public async Task Deactivate_KeepsTheRow_AndReAddingReusesIt()
    {
        var (service, repo) = CreateService();
        var created = await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, SchoolDayType.Holiday, "A", null, null, null));

        var removed = await service.DeactivateExceptionAsync(created.Value!.Id);
        Assert.True(removed.Succeeded);
        Assert.Single(repo.Days);
        Assert.Equal(RecordStatus.Inactive, repo.Days[0].Status);
        Assert.True((await service.GetTodayAsync()).IsSchoolDay);

        var again = await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, SchoolDayType.HalfDay, "B", null, null, new TimeOnly(12, 0)));
        Assert.True(again.Succeeded, again.Error);
        Assert.Single(repo.Days);
        Assert.Equal(created.Value.Id, again.Value!.Id);
        Assert.Equal(RecordStatus.Active, repo.Days[0].Status);
    }

    [Fact]
    public async Task UpdateRemovedException_IsRefused()
    {
        var (service, _) = CreateService();
        var created = await service.CreateExceptionAsync(new SchoolCalendarDayRequest(Friday, SchoolDayType.Holiday, "A", null, null, null));
        await service.DeactivateExceptionAsync(created.Value!.Id);

        var result = await service.UpdateExceptionAsync(created.Value.Id, new SchoolCalendarDayRequest(Friday, SchoolDayType.Holiday, "B", null, null, null));
        Assert.Equal(SchoolCalendarService.InactiveExceptionIsHistory, result.Error);
    }

    [Fact]
    public async Task GetCalendar_RejectsOversizedRange()
    {
        var (service, _) = CreateService();
        var result = await service.GetCalendarAsync(Monday, Monday.AddDays(SchoolCalendarService.MaxRangeDays));
        Assert.False(result.Succeeded);
    }

    // ------------------------------------------------------------------ attendance rules on the schedule

    [Fact]
    public async Task AttendanceList_FlagsPastLateArrivals_AndOffScheduleScans()
    {
        var journey = new FakeJourneyRepository();
        var student = new Student { StudentId = 1, FullName = "Asha" };
        journey.AttendanceRecords.AddRange(new[]
        {
            new Attendance { AttendanceId = 1, StudentId = 1, Student = student, EventType = AttendanceEventType.SchoolArrival, Date = Monday, Time = new TimeOnly(8, 50) },
            new Attendance { AttendanceId = 2, StudentId = 1, Student = student, EventType = AttendanceEventType.SchoolArrival, Date = Saturday, Time = new TimeOnly(9, 0) },
            new Attendance { AttendanceId = 3, StudentId = 1, Student = student, EventType = AttendanceEventType.SchoolArrival, Date = Friday, Time = new TimeOnly(8, 10) }
        });
        var options = new SchoolOptions();
        var (clock, _) = TestClock.Create();
        var service = new AttendanceJourneyService(journey, new FakeSafetyReadService(), options, clock);

        var rows = (await service.GetAttendanceAsync(1, null, null)).ToDictionary(r => r.AttendanceId);

        Assert.True(rows[1].IsLate);       // a past Monday, judged against its own cutoff
        Assert.False(rows[1].OffSchedule);
        Assert.False(rows[2].IsLate);      // Saturday: off-schedule, never late
        Assert.True(rows[2].OffSchedule);
        Assert.False(rows[3].IsLate);
    }
}

internal sealed class FakeSchoolCalendarRepository : ISchoolCalendarRepository
{
    public SchoolTimingSettings? Settings { get; private set; }
    public List<SchoolCalendarDay> Days { get; } = new();
    private int _nextId = 1;

    public Task<SchoolTimingSettings?> GetSettingsAsync(bool track, CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<List<SchoolCalendarDay>> GetDaysAsync(DateOnly from, DateOnly to, bool activeOnly, CancellationToken ct = default) =>
        Task.FromResult(Days.Where(d => d.Date >= from && d.Date <= to && (!activeOnly || d.Status == RecordStatus.Active)).ToList());

    public Task<SchoolCalendarDay?> GetDayByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Days.FirstOrDefault(d => d.SchoolCalendarDayId == id));

    public Task<SchoolCalendarDay?> GetDayByDateAsync(DateOnly date, CancellationToken ct = default) =>
        Task.FromResult(Days.FirstOrDefault(d => d.Date == date));

    public void AddSettings(SchoolTimingSettings settings) => Settings = settings;

    public void AddDay(SchoolCalendarDay day)
    {
        day.SchoolCalendarDayId = _nextId++;
        Days.Add(day);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
