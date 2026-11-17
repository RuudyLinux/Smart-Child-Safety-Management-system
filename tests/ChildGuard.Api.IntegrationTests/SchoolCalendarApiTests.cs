using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// School time management against the real database: the regular-week settings, calendar exceptions, who may
/// read or change them, and the dashboard rules that follow them (no absences or missing check-in alerts on a
/// holiday, a not-picked-up alert after dismissal). The test clock puts "today" at the school on Fri 6 Nov 2026.
/// </summary>
public class SchoolCalendarApiTests : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private readonly ApiFactory _api;
    public SchoolCalendarApiTests(ApiFactory api) => _api = api;

    private static string Unique(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static async Task<DashboardSummaryDto> SummaryAsync(HttpClient admin) =>
        await Ok<DashboardSummaryDto>(await admin.GetAsync("/api/dashboard/summary"));

    private async Task<int> NewGuardianAsync()
    {
        await using var db = _api.NewDb();
        var g = new Guardian { FullName = Unique("Cal Guardian "), Email = $"{Unique("g")}@cal.test", PasswordHash = "x" };
        db.Guardians.Add(g);
        await db.SaveChangesAsync();
        return g.GuardianId;
    }

    [Fact]
    public async Task Settings_DefaultToConfiguration_AndAdminCanSaveThem()
    {
        var admin = await _api.AdminClientAsync();
        var before = await Ok<SchoolTimingSettingsDto>(await admin.GetAsync("/api/school-calendar/settings"));
        Assert.Equal(new TimeOnly(8, 30), before.LateArrivalAfter);

        var request = new UpdateSchoolTimingSettingsRequest(before.WorkingDays, before.StartTime, new TimeOnly(8, 40),
            before.MissingCheckInAfter, before.DismissalTime, before.PickupGraceMinutes, before.AttendanceWindowDays);
        try
        {
            var saved = await Ok<SchoolTimingSettingsDto>(await admin.PutAsJsonAsync("/api/school-calendar/settings", request, Json.Options));
            Assert.True(saved.IsCustomized);
            Assert.Equal(new TimeOnly(8, 40), saved.LateArrivalAfter);
            Assert.Equal("08:40", (await SummaryAsync(admin)).Attendance.LateAfter);
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/school-calendar/settings", request with { LateArrivalAfter = before.LateArrivalAfter }, Json.Options);
        }
    }

    [Fact]
    public async Task Settings_OutOfOrderTimes_AreRejected()
    {
        var admin = await _api.AdminClientAsync();
        var bad = new UpdateSchoolTimingSettingsRequest(new List<DayOfWeek> { DayOfWeek.Monday },
            new TimeOnly(9, 0), new TimeOnly(8, 0), new TimeOnly(9, 30), new TimeOnly(14, 0), 30, 30);
        var r = await admin.PutAsJsonAsync("/api/school-calendar/settings", bad, Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Guardian_CanReadToday_ButNotChangeAnything()
    {
        var guardian = _api.GuardianClient(await NewGuardianAsync());

        var today = await Ok<SchoolDayDto>(await guardian.GetAsync("/api/school-calendar/today"));
        Assert.Equal(Today, today.Date);

        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/school-calendar/settings")).StatusCode);
        var post = await guardian.PostAsJsonAsync("/api/school-calendar/days",
            new SchoolCalendarDayRequest(Today.AddDays(30), SchoolDayType.Holiday, "Nope", null, null, null), Json.Options);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Holiday_ClosesTheDay_AndSuppressesAbsencesAndMissingCheckIns()
    {
        var admin = await _api.AdminClientAsync();
        await using (var db = _api.NewDb())
        {
            db.Students.Add(new Student { FullName = Unique("Cal NoShow "), DateOfBirth = new DateTime(2016, 1, 1) });
            await db.SaveChangesAsync();
        }

        // 10:00 on a school day: the no-show is absent and raises a missing check-in alert.
        var open = await SummaryAsync(admin);
        Assert.True(open.Attendance.IsSchoolDay);
        Assert.True(open.Attendance.Absent > 0);
        Assert.Contains(open.Alerts, a => a.Key.StartsWith("no-checkin-"));

        var holiday = await Ok<SchoolCalendarDayDto>(await admin.PostAsJsonAsync("/api/school-calendar/days",
            new SchoolCalendarDayRequest(Today, SchoolDayType.Holiday, "Founders' Day", null, null, null), Json.Options));
        try
        {
            var closed = await SummaryAsync(admin);
            Assert.False(closed.Attendance.IsSchoolDay);
            Assert.Equal(0, closed.Attendance.Absent);
            Assert.DoesNotContain(closed.Alerts, a => a.Key.StartsWith("no-checkin-"));
            Assert.Equal(SchoolDayKind.Holiday, closed.SchoolDay!.Kind);
            Assert.Equal("Founders' Day", closed.SchoolDay.Title);

            var cal = await Ok<SchoolCalendarViewDto>(await admin.GetAsync("/api/school-calendar?from=2026-11-02&to=2026-11-08"));
            Assert.Equal(4, cal.SchoolDays);
        }
        finally
        {
            await Ok<object>(await admin.PostAsJsonAsync($"/api/school-calendar/days/{holiday.Id}/deactivate", new { }, Json.Options));
        }

        Assert.True((await SummaryAsync(admin)).Attendance.IsSchoolDay);
    }

    [Fact]
    public async Task DuplicateDate_IsRejected_AndRemovedEntryCanBeAddedAgain()
    {
        var admin = await _api.AdminClientAsync();
        var date = Today.AddDays(40);
        var first = await Ok<SchoolCalendarDayDto>(await admin.PostAsJsonAsync("/api/school-calendar/days",
            new SchoolCalendarDayRequest(date, SchoolDayType.HalfDay, "PTA", null, null, new TimeOnly(12, 0)), Json.Options));

        var dup = await admin.PostAsJsonAsync("/api/school-calendar/days",
            new SchoolCalendarDayRequest(date, SchoolDayType.Holiday, "Other", null, null, null), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);

        await Ok<object>(await admin.PostAsJsonAsync($"/api/school-calendar/days/{first.Id}/deactivate", new { }, Json.Options));
        var again = await Ok<SchoolCalendarDayDto>(await admin.PostAsJsonAsync("/api/school-calendar/days",
            new SchoolCalendarDayRequest(date, SchoolDayType.Holiday, "Other", null, null, null), Json.Options));
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(SchoolDayType.Holiday, again.DayType);

        await admin.PostAsJsonAsync($"/api/school-calendar/days/{again.Id}/deactivate", new { }, Json.Options);
    }

    [Fact]
    public async Task StudentStillAtSchoolAfterDismissalGrace_RaisesNotPickedUpAlert()
    {
        var admin = await _api.AdminClientAsync();
        int studentId;
        await using (var db = _api.NewDb())
        {
            var s = new Student { FullName = Unique("Cal Stayer "), DateOfBirth = new DateTime(2016, 1, 1) };
            db.Students.Add(s);
            await db.SaveChangesAsync();
            studentId = s.StudentId;
            var arrivedUtc = new DateTime(2026, 11, 6, 2, 45, 0, DateTimeKind.Utc); // 08:15 at the school
            db.JourneyEvents.Add(new JourneyEvent { StudentId = s.StudentId, EventType = JourneyEventType.SchoolArrival, Timestamp = arrivedUtc, Source = EventSource.Manual });
            db.AttendanceRecords.Add(new Attendance
            {
                StudentId = s.StudentId, EventType = AttendanceEventType.SchoolArrival, RecordedAtUtc = arrivedUtc,
                Date = Today, Time = new TimeOnly(8, 15)
            });
            await db.SaveChangesAsync();
        }

        var original = _api.Time.UtcNow;
        try
        {
            // 14:45: after dismissal (14:30) but inside the 30-minute grace.
            _api.Time.UtcNow = new DateTime(2026, 11, 6, 9, 15, 0, DateTimeKind.Utc);
            Assert.DoesNotContain((await SummaryAsync(admin)).Alerts, a => a.Key == $"not-picked-up-{studentId}");

            // 15:05: grace has passed.
            _api.Time.UtcNow = new DateTime(2026, 11, 6, 9, 35, 0, DateTimeKind.Utc);
            Assert.Contains((await SummaryAsync(admin)).Alerts, a => a.Key == $"not-picked-up-{studentId}");
        }
        finally
        {
            _api.Time.UtcNow = original;
        }
    }
}
