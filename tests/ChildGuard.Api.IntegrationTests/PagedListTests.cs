using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Optional server paging on the student and guardian lists. Without <c>page</c> the endpoints answer exactly
/// as before; with it they answer the same JSON shape for one page plus the total in X-Total-Count. Each test
/// uses its own grade (students) or name prefix (guardians) so tests in this class never see each other's rows.
/// </summary>
public class PagedListTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PagedListTests(ApiFactory api) => _api = api;

    private static async Task<int> CreateStudentAsync(HttpClient admin, string name, string grade)
    {
        var r = await admin.PostAsJsonAsync("/api/students", new CreateStudentRequest(name, new DateTime(2016, 4, 21), grade), Json.Options);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<StudentDetailDto>(Json.Options))!.StudentId;
    }

    private static async Task<(List<StudentListItemDto> Items, int? Total)> GetAsync(HttpClient admin, string query)
    {
        var r = await admin.GetAsync("/api/students?" + query);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        int? total = r.Headers.TryGetValues("X-Total-Count", out var v) ? int.Parse(v.Single()) : null;
        return ((await r.Content.ReadFromJsonAsync<List<StudentListItemDto>>(Json.Options))!, total);
    }

    [Fact]
    public async Task List_WithoutPage_BodyIdenticalToBefore()
    {
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Unpaged Alpha", "G-unpaged");

        // The unpaged path is the original one: same body, no paging header.
        var r = await admin.GetAsync("/api/students?search=Unpaged");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(r.Headers.Contains("X-Total-Count"));
        var items = await r.Content.ReadFromJsonAsync<List<StudentListItemDto>>(Json.Options);
        Assert.Single(items!, s => s.FullName == "Unpaged Alpha");
    }

    [Fact]
    public async Task List_Page2_ReturnsSecondSliceAndTotal()
    {
        var admin = await _api.AdminClientAsync();
        for (var i = 1; i <= 7; i++) await CreateStudentAsync(admin, $"Slice {i:D2}", "G-slice");

        var (page1, total1) = await GetAsync(admin, "grade=G-slice&page=1&pageSize=3&sort=name");
        var (page3, total3) = await GetAsync(admin, "grade=G-slice&page=3&pageSize=3&sort=name");
        Assert.Equal(7, total1);
        Assert.Equal(7, total3);
        Assert.Equal(new[] { "Slice 01", "Slice 02", "Slice 03" }, page1.Select(s => s.FullName));
        Assert.Equal(new[] { "Slice 07" }, page3.Select(s => s.FullName));
    }

    [Fact]
    public async Task List_PageBeyondEnd_IsEmptyWithTotal()
    {
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Beyond One", "G-beyond");
        var (items, total) = await GetAsync(admin, "grade=G-beyond&page=5&pageSize=10");
        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task List_SortByNameDesc_Ordered()
    {
        var admin = await _api.AdminClientAsync();
        foreach (var n in new[] { "Bravo Desc", "Alpha Desc", "Charlie Desc" }) await CreateStudentAsync(admin, n, "G-desc");
        var (items, _) = await GetAsync(admin, "grade=G-desc&page=1&sort=name&dir=desc");
        Assert.Equal(new[] { "Charlie Desc", "Bravo Desc", "Alpha Desc" }, items.Select(s => s.FullName));
    }

    [Fact]
    public async Task List_SearchAndGradeCombine_CaseInsensitive()
    {
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Mira Combine", "G-Combine");
        await CreateStudentAsync(admin, "Omar Combine", "G-Combine");
        await CreateStudentAsync(admin, "Mira Elsewhere", "G-other");
        var (items, total) = await GetAsync(admin, "grade=g-combine&search=mira&page=1");
        Assert.Equal(1, total);
        Assert.Equal("Mira Combine", Assert.Single(items).FullName);
    }

    [Fact]
    public async Task List_FilterPresentToday_UsesSchoolDay()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        using (var db = _api.NewDb())
        {
            var s = db.Students.Single(x => x.StudentId == child.StudentId);
            s.Grade = "G-present";
            db.SaveChanges();
        }
        await CreateStudentAsync(admin, "Absent Today", "G-present");
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        var (present, presentTotal) = await GetAsync(admin, "grade=G-present&presentToday=true&page=1");
        var (absent, absentTotal) = await GetAsync(admin, "grade=G-present&presentToday=false&page=1");
        Assert.Equal(1, presentTotal);
        Assert.Equal(child.StudentId, Assert.Single(present).StudentId);
        Assert.True(present[0].PresentToday);
        Assert.Equal(1, absentTotal);
        Assert.Equal("Absent Today", Assert.Single(absent).FullName);
    }

    [Fact]
    public async Task List_SortByActivity_MostRecentFirst_NoActivityLast()
    {
        var admin = await _api.AdminClientAsync();
        var early = await _api.NewChildAsync(withFace: false);
        var late = await _api.NewChildAsync(withFace: false);
        using (var db = _api.NewDb())
        {
            foreach (var s in db.Students.Where(x => x.StudentId == early.StudentId || x.StudentId == late.StudentId)) s.Grade = "G-activity";
            db.SaveChanges();
        }
        var idle = await CreateStudentAsync(admin, "Idle Activity", "G-activity");
        await admin.ScanOkAsync(early, JourneyEventType.SchoolArrival);
        _api.Time.Advance(TimeSpan.FromMinutes(5));
        await admin.ScanOkAsync(late, JourneyEventType.SchoolArrival);

        var (items, _) = await GetAsync(admin, "grade=G-activity&page=1&sort=activity&dir=desc");
        Assert.Equal(new[] { late.StudentId, early.StudentId, idle }, items.Select(s => s.StudentId));
    }

    [Fact]
    public async Task List_FilterRfidGuardianAndArchived()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);   // has a card and a guardian
        using (var db = _api.NewDb())
        {
            db.Students.Single(x => x.StudentId == child.StudentId).Grade = "G-flags";
            db.SaveChanges();
        }
        var bare = await CreateStudentAsync(admin, "Bare Flags", "G-flags");
        var archived = await CreateStudentAsync(admin, "Archived Flags", "G-flags");
        var a = await admin.PostAsJsonAsync($"/api/students/{archived}/archive", new ArchiveRecordRequest("Left the school this term"), Json.Options);
        Assert.True(a.IsSuccessStatusCode, await a.Content.ReadAsStringAsync());

        Assert.Equal(child.StudentId, Assert.Single((await GetAsync(admin, "grade=G-flags&hasRfid=true&page=1")).Items).StudentId);
        Assert.Equal(child.StudentId, Assert.Single((await GetAsync(admin, "grade=G-flags&hasGuardian=true&page=1")).Items).StudentId);
        Assert.Equal(archived, Assert.Single((await GetAsync(admin, "grade=G-flags&status=Archived&page=1")).Items).StudentId);
        var active = (await GetAsync(admin, "grade=G-flags&status=Active&page=1&sort=name")).Items.Select(s => s.StudentId).ToList();
        Assert.Contains(bare, active);
        Assert.DoesNotContain(archived, active);
    }

    [Theory]
    [InlineData("page=1&sort=PasswordHash")]
    [InlineData("page=0")]
    [InlineData("page=1&pageSize=500")]
    [InlineData("page=1&dir=up")]
    [InlineData("page=1&status=Deleted")]
    public async Task List_BadPaging_Is400(string query)
    {
        var admin = await _api.AdminClientAsync();
        var r = await admin.GetAsync("/api/students?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task List_Unauthenticated_401()
    {
        var r = await _api.CreateClient().GetAsync("/api/students?page=1");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Grades_AreDistinctNonEmptyAndSorted()
    {
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Grade A", "Z-grade-2");
        await CreateStudentAsync(admin, "Grade B", "Z-grade-1");
        await CreateStudentAsync(admin, "Grade C", "Z-grade-1");
        var grades = await admin.GetFromJsonAsync<List<string>>("/api/students/grades", Json.Options);
        var mine = grades!.Where(g => g.StartsWith("Z-grade")).ToList();
        Assert.Equal(new[] { "Z-grade-1", "Z-grade-2" }, mine);
    }

    [Fact]
    public async Task Grades_DifferingOnlyInCase_AreListedOnce()
    {
        // MySQL compares grades case-insensitively; PostgreSQL text does not. The list must behave the same on both.
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Case One", "Q-case");
        await CreateStudentAsync(admin, "Case Two", "q-CASE");
        await CreateStudentAsync(admin, "Case Three", "Q-case");
        var grades = await admin.GetFromJsonAsync<List<string>>("/api/students/grades", Json.Options);
        // One entry, spelled as most students have it.
        Assert.Equal("Q-case", Assert.Single(grades!, g => string.Equals(g, "q-case", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GradeFilter_MatchesEveryCaseVariant()
    {
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Variant Upper", "W-VAR");
        await CreateStudentAsync(admin, "Variant Lower", "w-var");
        var (items, total) = await GetAsync(admin, "grade=W-Var&page=1&sort=name");
        Assert.Equal(2, total);
        Assert.Equal(new[] { "Variant Lower", "Variant Upper" }, items.Select(s => s.FullName));
    }

    [Fact]
    public async Task SortByGrade_IgnoresCase_AndPutsNoGradeFirst()
    {
        var admin = await _api.AdminClientAsync();
        await CreateStudentAsync(admin, "Gsort Bravo", "B-gsort");
        await CreateStudentAsync(admin, "Gsort Alpha", "a-gsort");
        await CreateStudentAsync(admin, "Gsort Charlie", "C-gsort");
        await CreateStudentAsync(admin, "Gsort None", null!);
        var (asc, _) = await GetAsync(admin, "search=Gsort&page=1&sort=grade");
        Assert.Equal(new[] { "Gsort None", "Gsort Alpha", "Gsort Bravo", "Gsort Charlie" }, asc.Select(s => s.FullName));
        var (desc, _) = await GetAsync(admin, "search=Gsort&page=1&sort=grade&dir=desc");
        Assert.Equal(new[] { "Gsort Charlie", "Gsort Bravo", "Gsort Alpha", "Gsort None" }, desc.Select(s => s.FullName));
    }

    [Fact]
    public async Task SortByStatus_ActiveInactiveArchived()
    {
        var admin = await _api.AdminClientAsync();
        var archived = await CreateStudentAsync(admin, "Ssort Archived", "G-ssort");
        var inactive = await CreateStudentAsync(admin, "Ssort Inactive", "G-ssort");
        var active = await CreateStudentAsync(admin, "Ssort Active", "G-ssort");
        Assert.True((await admin.PostAsJsonAsync($"/api/students/{archived}/archive", new ArchiveRecordRequest("Left the school this term"), Json.Options)).IsSuccessStatusCode);
        Assert.True((await admin.PatchAsJsonAsync($"/api/students/{inactive}/status", new UpdateStudentStatusRequest(RecordStatus.Inactive), Json.Options)).IsSuccessStatusCode);
        var (items, _) = await GetAsync(admin, "grade=G-ssort&page=1&sort=status");
        Assert.Equal(new[] { active, inactive, archived }, items.Select(s => s.StudentId));
    }

    // ------------------------------------------------------------- Guardians

    [Fact]
    public async Task Guardians_WithoutPage_Unchanged_WithPage_Sliced()
    {
        var admin = await _api.AdminClientAsync();
        for (var i = 1; i <= 3; i++)
        {
            var r = await admin.PostAsJsonAsync("/api/guardians",
                new CreateGuardianRequest($"Pager Guardian {i}", $"pager{i}@childguard.test", $"+91 90000 1000{i}"), Json.Options);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }

        var all = await admin.GetAsync("/api/guardians");
        Assert.False(all.Headers.Contains("X-Total-Count"));

        var paged = await admin.GetAsync("/api/guardians?search=Pager%20Guardian&page=2&pageSize=2&sort=name");
        Assert.True(paged.IsSuccessStatusCode, await paged.Content.ReadAsStringAsync());
        Assert.Equal("3", paged.Headers.GetValues("X-Total-Count").Single());
        var items = await paged.Content.ReadFromJsonAsync<List<GuardianListItemDto>>(Json.Options);
        Assert.Equal("Pager Guardian 3", Assert.Single(items!).FullName);
    }
}
