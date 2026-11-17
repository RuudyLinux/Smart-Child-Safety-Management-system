using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// School time zone (Asia/Kolkata, UTC+05:30) end to end: attendance stamping, late
/// cutoff, local-midnight boundaries, and the paged journey report.
/// </summary>
public class TimeZoneAndReportTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public TimeZoneAndReportTests(ApiFactory api) => _api = api;

    private static DateTime Utc(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private async Task<List<AttendanceRecordDto>> AttendanceAsync(HttpClient admin, Child child, string from, string to) =>
        (await admin.GetFromJsonAsync<List<AttendanceRecordDto>>(
            $"/api/reports/attendance?studentId={child.StudentId}&from={from}&to={to}", Json.Options))!;

    [Theory]
    [InlineData(2, 59, "08:29", false)]
    [InlineData(3, 0, "08:30", false)]
    [InlineData(3, 1, "08:31", true)]
    [InlineData(3, 30, "09:00", true)] // was reported on time when attendance was stamped in UTC
    public async Task LateArrival_IsJudgedAgainstLocalCutoff(int utcHour, int utcMinute, string localTime, bool late)
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        _api.Time.UtcNow = Utc(11, 9, utcHour, utcMinute);

        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        var row = Assert.Single(await AttendanceAsync(admin, child, "2026-11-09", "2026-11-09"));
        Assert.Equal(new DateOnly(2026, 11, 9), row.Date);
        Assert.Equal(TimeOnly.Parse(localTime), new TimeOnly(row.Time.Hour, row.Time.Minute));
        Assert.Equal(late, row.IsLate);
    }

    [Fact]
    public async Task ArrivalsEitherSideOfLocalMidnight_FallOnDifferentSchoolDates()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        _api.Time.UtcNow = Utc(11, 10, 18, 29); // 23:59 on 10 Nov locally
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        _api.Time.UtcNow = Utc(11, 10, 18, 31); // 00:01 on 11 Nov locally — still 10 Nov in UTC
        var next = await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        Assert.True(next.EventRecorded && !next.AlreadyRecorded, "a new school day starts a new cycle");
        var rows = await AttendanceAsync(admin, child, "2026-11-10", "2026-11-11");
        Assert.Equal(new[] { new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11) }, rows.Select(r => r.Date).OrderBy(d => d).ToArray());
        Assert.Single(await AttendanceAsync(admin, child, "2026-11-11", "2026-11-11"));
    }

    [Fact]
    public async Task JourneyReport_UsesLocalDateBoundaries()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        _api.Time.UtcNow = Utc(11, 12, 18, 40); // 00:10 on 13 Nov locally, 12 Nov in UTC
        await admin.ScanOkAsync(child, JourneyEventType.BusBoarding);

        var on12 = await admin.GetFromJsonAsync<JourneyReportPageDto>(
            $"/api/reports/journey/events?from=2026-11-12&to=2026-11-12&studentId={child.StudentId}", Json.Options);
        var on13 = await admin.GetFromJsonAsync<JourneyReportPageDto>(
            $"/api/reports/journey/events?from=2026-11-13&to=2026-11-13&studentId={child.StudentId}", Json.Options);

        Assert.Equal(0, on12!.TotalCount);
        Assert.Equal(1, on13!.TotalCount);
    }

    [Fact]
    public async Task JourneyReport_PagesThroughHistoricEventsWithAccurateTotals()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        // 450 events on 1–3 Oct (school time), far older than any "latest 200" window.
        await using (var db = _api.NewDb())
        {
            for (var i = 0; i < 450; i++)
            {
                db.JourneyEvents.Add(new JourneyEvent
                {
                    StudentId = child.StudentId, EventType = JourneyEventType.BusBoarding, Source = EventSource.Rfid,
                    Timestamp = Utc(10, 1 + i % 3, 2, 0).AddSeconds(i)
                });
            }
            await db.SaveChangesAsync();
        }

        var seen = new List<int>();
        JourneyReportPageDto? page = null;
        for (var p = 1; p < 20; p++)
        {
            page = await admin.GetFromJsonAsync<JourneyReportPageDto>(
                $"/api/reports/journey/events?from=2026-10-01&to=2026-10-03&studentId={child.StudentId}&page={p}&pageSize=200", Json.Options);
            seen.AddRange(page!.Items.Select(e => e.JourneyEventId));
            if (!page.HasMore) break;
        }

        Assert.Equal(450, page!.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(450, seen.Distinct().Count());
        Assert.Equal(new DateOnly(2026, 10, 1), page.From);
        Assert.Equal(new DateOnly(2026, 10, 3), page.To);

        var day2 = await admin.GetFromJsonAsync<JourneyReportPageDto>(
            $"/api/reports/journey/events?from=2026-10-02&to=2026-10-02&studentId={child.StudentId}", Json.Options);
        Assert.Equal(150, day2!.TotalCount);
        Assert.Equal(50, day2.Items.Count); // default page size
        Assert.True(day2.HasMore);
    }

    [Theory]
    [InlineData("from=2026-10-10&to=2026-10-01")]   // inverted
    [InlineData("from=2024-01-01&to=2026-10-01")]   // longer than 366 days
    [InlineData("from=not-a-date")]
    public async Task JourneyReport_InvalidRange_Returns400(string query)
    {
        var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync("/api/reports/journey/events?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task JourneyReport_PageSizeIsCapped()
    {
        var admin = await _api.AdminClientAsync();

        var page = await admin.GetFromJsonAsync<JourneyReportPageDto>(
            "/api/reports/journey/events?from=2026-10-01&to=2026-10-31&pageSize=100000", Json.Options);

        Assert.Equal(200, page!.PageSize);
    }

    [Fact]
    public async Task AttendanceAndJourney_StayConsistent_ForRfidAndGuardianDepartures()
    {
        var admin = await _api.AdminClientAsync();
        var rfidChild = await _api.NewChildAsync();
        var pickupChild = await _api.NewChildAsync();
        _api.Time.UtcNow = Utc(11, 16, 2, 30); // 08:00 locally

        await admin.ScanOkAsync(rfidChild, JourneyEventType.SchoolArrival);
        await admin.ScanOkAsync(pickupChild, JourneyEventType.SchoolArrival);
        _api.Time.UtcNow = Utc(11, 16, 9, 30); // 15:00 locally
        await admin.ScanOkAsync(rfidChild, JourneyEventType.SchoolDeparture);
        var id = await admin.VerifyFaceAsync(_api, pickupChild);
        (await admin.ApproveAsync(id)).EnsureSuccessStatusCode();

        await using var db = _api.NewDb();
        foreach (var child in new[] { rfidChild, pickupChild })
        {
            var arrivals = await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolArrival);
            var departures = await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture);
            var attendance = await db.AttendanceRecords.Where(a => a.StudentId == child.StudentId).OrderBy(a => a.RecordedAtUtc).ToListAsync();
            Assert.Equal(1, arrivals);
            Assert.Equal(1, departures);
            Assert.Equal(new[] { AttendanceEventType.SchoolArrival, AttendanceEventType.SchoolDeparture }, attendance.Select(a => a.EventType).ToArray());
            Assert.All(attendance, a => Assert.Equal(new DateOnly(2026, 11, 16), a.Date));
            Assert.Equal(new TimeOnly(15, 0), attendance[1].Time);
        }
    }
}
