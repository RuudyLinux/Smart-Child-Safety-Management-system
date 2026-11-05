using ChildGuard.Application.Common;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>School calendar: time zone, day boundaries, late cutoff.</summary>
public class SchoolClockTests
{
    [Fact]
    public void Today_FollowsTheSchoolZoneNotUtc()
    {
        // 20:00 UTC on 5 Nov is already 01:30 on 6 Nov in India.
        var (clock, _) = TestClock.Create(new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 11, 6), clock.Today);
        Assert.Equal(new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc), clock.TodayStartUtc);
    }

    [Fact]
    public void RangeUtc_IsLocalMidnightToLocalMidnight()
    {
        var (clock, _) = TestClock.Create();

        var (from, to) = clock.RangeUtc(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2));

        Assert.Equal(new DateTime(2026, 10, 31, 18, 30, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 11, 2, 18, 30, 0, DateTimeKind.Utc), to);
    }

    [Theory]
    [InlineData(18, 29, 2026, 11, 5, 23, 59)] // last minute of 5 Nov locally
    [InlineData(18, 30, 2026, 11, 6, 0, 0)]   // local midnight
    public void ToSchoolDateTime_AroundLocalMidnight(int utcH, int utcM, int y, int mo, int d, int h, int m)
    {
        var (clock, _) = TestClock.Create();

        var (date, time) = clock.ToSchoolDateTime(new DateTime(2026, 11, 5, utcH, utcM, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(y, mo, d), date);
        Assert.Equal(new TimeOnly(h, m), time);
    }

    [Fact]
    public void DayWithDstChange_HasItsRealLength()
    {
        // US spring-forward day is 23 hours long.
        var (clock, _) = TestClock.Create(zone: "America/New_York");

        var (from, to) = clock.RangeUtc(new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8));

        Assert.Equal(TimeSpan.FromHours(23), to - from);
    }

    [Fact]
    public void UnknownTimeZone_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SchoolClock(new SchoolOptions { TimeZone = "Mars/Olympus" }, TimeProvider.System));
    }

    [Theory]
    [InlineData(2, 59, false)] // 08:29 local
    [InlineData(3, 0, false)]  // 08:30 local — on the cutoff is not late
    [InlineData(3, 1, true)]   // 08:31 local
    public async Task LateArrival_UsesLocalCutoff(int utcH, int utcM, bool expectedLate)
    {
        var (clock, _) = TestClock.Create();
        var repo = new FakeJourneyRepository();
        var at = new DateTime(2026, 11, 6, utcH, utcM, 0, DateTimeKind.Utc);
        var (date, time) = clock.ToSchoolDateTime(at);
        repo.AttendanceRecords.Add(new Attendance
        {
            AttendanceId = 1, StudentId = 1, Student = new Student { StudentId = 1, FullName = "Rahul Patel" },
            EventType = AttendanceEventType.SchoolArrival, RecordedAtUtc = at, Date = date, Time = time
        });
        var service = new AttendanceJourneyService(repo, new FakeSafetyReadService(), new SchoolOptions(), clock);

        var row = Assert.Single(await service.GetAttendanceAsync(null, null, null));

        Assert.Equal(expectedLate, row.IsLate);
    }
}
