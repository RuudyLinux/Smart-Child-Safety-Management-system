using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Optional server paging, filters and a summary for the verification log (every pickup verification attempt, so
/// it grows with each one). Without <c>page</c> the endpoint answers as before.
/// </summary>
public class PagedVerificationHistoryTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PagedVerificationHistoryTests(ApiFactory api) => _api = api;

    private async Task<(Child Child, int VerificationId)> AttemptAsync(HttpClient admin, string studentName, string guardianName, bool match)
    {
        var child = await _api.NewChildAsync();
        using (var db = _api.NewDb())
        {
            db.Students.Single(s => s.StudentId == child.StudentId).FullName = studentName;
            db.Guardians.Single(g => g.GuardianId == child.GuardianId).FullName = guardianName;
            db.SaveChanges();
        }
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        return (child, await admin.VerifyFaceAsync(_api, child, match));
    }

    private static async Task<(List<GuardianVerificationHistoryDto> Items, int? Total)> GetAsync(HttpClient admin, string query)
    {
        var r = await admin.GetAsync("/api/reports/verification?" + query);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        int? total = r.Headers.TryGetValues("X-Total-Count", out var v) ? int.Parse(v.Single()) : null;
        return ((await r.Content.ReadFromJsonAsync<List<GuardianVerificationHistoryDto>>(Json.Options))!, total);
    }

    [Fact]
    public async Task WithoutPage_AnswersAsBefore_NoTotalHeader()
    {
        var admin = await _api.AdminClientAsync();
        await AttemptAsync(admin, "Vhist Unpaged", "Vguard Unpaged", match: true);
        var r = await admin.GetAsync("/api/reports/verification");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(r.Headers.Contains("X-Total-Count"));
    }

    [Fact]
    public async Task Paged_SlicesCountsAndNewestFirst()
    {
        var admin = await _api.AdminClientAsync();
        for (var i = 1; i <= 3; i++)
        {
            await AttemptAsync(admin, $"Vslice {i}", "Vguard Slice", match: true);
            _api.Time.Advance(TimeSpan.FromMinutes(1));
        }
        var (page1, total) = await GetAsync(admin, "search=Vguard%20Slice&page=1&pageSize=2");
        var (page2, _) = await GetAsync(admin, "search=Vguard%20Slice&page=2&pageSize=2");
        Assert.Equal(3, total);
        Assert.Equal(new[] { "Vslice 3", "Vslice 2" }, page1.Select(v => v.StudentName));   // newest first
        Assert.Equal(new[] { "Vslice 1" }, page2.Select(v => v.StudentName));
    }

    [Fact]
    public async Task Filters_BySearch_Result_AndHandover()
    {
        var admin = await _api.AdminClientAsync();
        var (matchChild, matchId) = await AttemptAsync(admin, "Vflt Matched", "Vguard Flt", match: true);
        await AttemptAsync(admin, "Vflt Missed", "Vguard Flt", match: false);
        Assert.True((await admin.ApproveAsync(matchId)).IsSuccessStatusCode);

        Assert.Equal(2, (await GetAsync(admin, "search=Vguard%20Flt&page=1")).Total);                      // guardian name
        Assert.Equal("Vflt Matched", Assert.Single((await GetAsync(admin, "search=Vflt%20Matched&page=1")).Items).StudentName);   // student name
        Assert.Equal("Vflt Matched", Assert.Single((await GetAsync(admin, "search=Vguard%20Flt&result=Match&page=1")).Items).StudentName);
        Assert.Equal("Vflt Missed", Assert.Single((await GetAsync(admin, "search=Vguard%20Flt&result=NoMatch&page=1")).Items).StudentName);
        Assert.Equal("Vflt Matched", Assert.Single((await GetAsync(admin, "search=Vguard%20Flt&approved=true&page=1")).Items).StudentName);
        Assert.Equal("Vflt Missed", Assert.Single((await GetAsync(admin, "search=Vguard%20Flt&approved=false&page=1")).Items).StudentName);
        Assert.Single((await GetAsync(admin, $"studentId={matchChild.StudentId}&page=1")).Items);
    }

    [Fact]
    public async Task Sorts_ByScoreAndStudent()
    {
        var admin = await _api.AdminClientAsync();
        foreach (var n in new[] { "Vsort Charlie", "Vsort alpha", "Vsort Bravo" })
            await AttemptAsync(admin, n, "Vguard Sort", match: true);
        var (asc, _) = await GetAsync(admin, "search=Vguard%20Sort&page=1&sort=student&dir=asc");
        Assert.Equal(new[] { "Vsort alpha", "Vsort Bravo", "Vsort Charlie" }, asc.Select(v => v.StudentName));
        var (desc, _) = await GetAsync(admin, "search=Vguard%20Sort&page=1&sort=student&dir=desc");
        Assert.Equal(new[] { "Vsort Charlie", "Vsort Bravo", "Vsort alpha" }, desc.Select(v => v.StudentName));
        var (byScore, _) = await GetAsync(admin, "search=Vguard%20Sort&page=1&sort=score&dir=desc");
        Assert.Equal(byScore.OrderByDescending(v => v.MatchScore).Select(v => v.Id), byScore.Select(v => v.Id));
    }

    [Fact]
    public async Task DateRange_UsesTheSchoolDay()
    {
        var admin = await _api.AdminClientAsync();
        await AttemptAsync(admin, "Vdate Today", "Vguard Date", match: true);
        var today = DateOnly.FromDateTime(_api.Clock.LocalNow);
        Assert.Equal(1, (await GetAsync(admin, $"search=Vguard%20Date&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&page=1")).Total);
        Assert.Equal(0, (await GetAsync(admin, $"search=Vguard%20Date&from={today.AddDays(1):yyyy-MM-dd}&page=1")).Total);
        Assert.Equal(0, (await GetAsync(admin, $"search=Vguard%20Date&to={today.AddDays(-1):yyyy-MM-dd}&page=1")).Total);
    }

    [Fact]
    public async Task Summary_MatchesTheFilters()
    {
        var admin = await _api.AdminClientAsync();
        async Task<VerificationHistorySummaryDto> Summary(string q = "") =>
            (await admin.GetFromJsonAsync<VerificationHistorySummaryDto>("/api/reports/verification/summary" + q, Json.Options))!;
        var before = await Summary();
        var (_, id) = await AttemptAsync(admin, "Vsum Ok", "Vguard Sum", match: true);
        await AttemptAsync(admin, "Vsum Bad", "Vguard Sum", match: false);
        Assert.True((await admin.ApproveAsync(id)).IsSuccessStatusCode);
        var after = await Summary();

        Assert.Equal(before.Total + 2, after.Total);
        Assert.Equal(before.Matched + 1, after.Matched);
        Assert.Equal(before.NoMatch + 1, after.NoMatch);
        Assert.Equal(before.Approved + 1, after.Approved);
        Assert.Equal(after.Total, (await GetAsync(admin, "page=1&pageSize=1")).Total);
        Assert.Equal(after.Matched, (await GetAsync(admin, "result=Match&page=1&pageSize=1")).Total);
        Assert.Equal(after.NoMatch, (await GetAsync(admin, "result=NoMatch&page=1&pageSize=1")).Total);
        Assert.Equal(after.Approved, (await GetAsync(admin, "approved=true&page=1&pageSize=1")).Total);
    }

    [Theory]
    [InlineData("page=1&sort=guardianId")]
    [InlineData("page=0")]
    [InlineData("page=1&pageSize=101")]
    [InlineData("page=1&result=Maybe")]
    public async Task BadPaging_Is400(string query)
    {
        var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/reports/verification?" + query)).StatusCode);
    }

    [Fact]
    public async Task RequiresAdmin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/reports/verification?page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/reports/verification/summary")).StatusCode);
    }
}
