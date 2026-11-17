using ChildGuard.Application.Common;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>Automatic checkpoint selection from the school schedule and the student's journey today.</summary>
public class CheckpointResolverTests
{
    private static readonly DateOnly Day = new(2026, 11, 17);

    private static SchoolDayInfo Open(string start = "08:00", string dismissal = "14:30", string? title = null) =>
        new(Day, SchoolDayKind.Regular, true, title, TimeOnly.Parse(start), new TimeOnly(8, 30), new TimeOnly(9, 30), TimeOnly.Parse(dismissal), null);

    private static SchoolDayInfo Closed(string? title = "Diwali") =>
        new(Day, SchoolDayKind.Holiday, false, title, null, null, null, null, 1);

    private static JourneyEventType? Pick(JourneyEventType? last, bool bus, SchoolDayInfo day, string now) =>
        CheckpointResolver.Resolve(last, bus, day, TimeOnly.Parse(now)).Checkpoint;

    [Theory]
    // nothing yet today
    [InlineData(null, true, "07:10", JourneyEventType.BusBoarding)]   // bus student before school starts
    [InlineData(null, true, "08:20", JourneyEventType.SchoolArrival)] // bus student once school has started
    [InlineData(null, false, "07:50", JourneyEventType.SchoolArrival)]
    // through the day
    [InlineData(JourneyEventType.BusBoarding, true, "07:55", JourneyEventType.SchoolArrival)]
    [InlineData(JourneyEventType.SchoolArrival, false, "14:40", JourneyEventType.SchoolDeparture)]
    [InlineData(JourneyEventType.SchoolArrival, true, "11:00", JourneyEventType.SchoolDeparture)] // early pickup
    [InlineData(JourneyEventType.SchoolDeparture, true, "14:45", JourneyEventType.BusDropoff)]
    [InlineData(JourneyEventType.SchoolDeparture, false, "14:45", null)]                        // journey complete
    // a new trip after a drop-off starts again
    [InlineData(JourneyEventType.BusDropoff, false, "16:00", JourneyEventType.SchoolArrival)]
    public void FollowsTheJourneyAndTheTimetable(JourneyEventType? last, bool bus, string now, JourneyEventType? expected) =>
        Assert.Equal(expected, Pick(last, bus, Open(), now));

    [Fact]
    public void SpecialStartTime_MovesTheBoardingWindow()
    {
        Assert.Equal(JourneyEventType.BusBoarding, Pick(null, true, Open(start: "09:00"), "08:40"));
        Assert.Equal(JourneyEventType.SchoolArrival, Pick(null, true, Open(start: "09:00"), "09:05"));
    }

    [Fact]
    public void EarlyPickup_IsExplainedAgainstTodaysDismissal()
    {
        var d = CheckpointResolver.Resolve(JourneyEventType.SchoolArrival, false, Open(dismissal: "11:30"), new TimeOnly(10, 0));
        Assert.Equal(JourneyEventType.SchoolDeparture, d.Checkpoint);
        Assert.Contains("early pickup", d.Reason);
        Assert.Contains("11:30", d.Reason);
    }

    [Fact]
    public void ClosedDay_StillPicksTheNextStep_ButFlagsItOffSchedule()
    {
        var d = CheckpointResolver.Resolve(null, false, Closed(), new TimeOnly(10, 0));
        Assert.Equal(JourneyEventType.SchoolArrival, d.Checkpoint);
        Assert.True(d.OffSchedule);
        Assert.Contains("Diwali", d.Reason);
    }
}
