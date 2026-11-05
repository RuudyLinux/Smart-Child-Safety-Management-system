using ChildGuard.Application.Common;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>The paged, date-bounded journey report that replaced the fixed "latest 200" window.</summary>
public class JourneyReportTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    private static (AttendanceJourneyService Service, FakeJourneyRepository Repo) Build(int eventsPerDay, int days)
    {
        var repo = new FakeJourneyRepository();
        var student = new Student { StudentId = 1, FullName = "Rahul Patel" };
        var id = 0;
        for (var d = 0; d < days; d++)
        for (var i = 0; i < eventsPerDay; i++)
        {
            repo.Events.Add(new JourneyEvent
            {
                JourneyEventId = ++id, StudentId = 1, Student = student, EventType = JourneyEventType.BusBoarding,
                Timestamp = Day.AddDays(d).ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc).AddMinutes(i),
                Source = EventSource.Rfid
            });
        }
        return (new AttendanceJourneyService(repo, new FakeSafetyReadService(), new SchoolOptions()), repo);
    }

    [Fact]
    public async Task Report_MoreEventsThanOnePage_ReportsTrueTotalAndHasMore()
    {
        var (service, _) = Build(eventsPerDay: 300, days: 1);

        var page1 = (await service.GetJourneyReportAsync(Day, Day, null, page: 1, pageSize: 200)).Value!;

        Assert.Equal(300, page1.TotalCount);
        Assert.Equal(200, page1.Items.Count);
        Assert.Equal(2, page1.TotalPages);
        Assert.True(page1.HasMore);
    }

    [Fact]
    public async Task Report_PagesTogetherCoverEveryEventExactlyOnce()
    {
        var (service, _) = Build(eventsPerDay: 120, days: 3);

        var seen = new List<int>();
        for (var page = 1; ; page++)
        {
            var result = (await service.GetJourneyReportAsync(Day, Day.AddDays(2), null, page, 50)).Value!;
            seen.AddRange(result.Items.Select(e => e.JourneyEventId));
            if (!result.HasMore) break;
        }

        Assert.Equal(360, seen.Count);
        Assert.Equal(360, seen.Distinct().Count());
    }

    [Fact]
    public async Task Report_OldEventsOutsideAnyRecentWindow_AreReachableByDate()
    {
        var (service, _) = Build(eventsPerDay: 250, days: 2);

        // Day 1's events would be beyond a "latest 200" window; asking for that date returns them all.
        var result = (await service.GetJourneyReportAsync(Day, Day, null, 1, 200)).Value!;

        Assert.Equal(250, result.TotalCount);
        Assert.All(result.Items, e => Assert.Equal(Day, DateOnly.FromDateTime(e.Timestamp)));
    }

    [Fact]
    public async Task Report_RangeIsInclusiveOfBothEndDates()
    {
        var (service, _) = Build(eventsPerDay: 1, days: 3);

        var result = (await service.GetJourneyReportAsync(Day.AddDays(1), Day.AddDays(1), null, 1, 50)).Value!;

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(Day.AddDays(1), DateOnly.FromDateTime(result.Items[0].Timestamp));
    }

    [Fact]
    public async Task Report_InvertedRange_Fails()
    {
        var (service, _) = Build(1, 1);

        var result = await service.GetJourneyReportAsync(Day.AddDays(1), Day, null, 1, 50);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Report_PageSizeAboveMaximum_IsClamped()
    {
        var (service, _) = Build(eventsPerDay: 500, days: 1);

        var result = (await service.GetJourneyReportAsync(Day, Day, null, 1, 10_000)).Value!;

        Assert.Equal(AttendanceJourneyService.MaxReportPageSize, result.PageSize);
        Assert.Equal(AttendanceJourneyService.MaxReportPageSize, result.Items.Count);
        Assert.Equal(500, result.TotalCount);
    }
}
