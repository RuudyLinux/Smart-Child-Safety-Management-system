using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Optional server paging on the RFID card list (one card per student and spares, so it grows with the roll).
/// Without <c>page</c> the endpoint answers as before; with it, one page of the same rows plus X-Total-Count.
/// </summary>
public class PagedRfidCardsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PagedRfidCardsTests(ApiFactory api) => _api = api;

    private static string Uid(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static async Task<int> RegisterSpareAsync(HttpClient admin, string uid)
    {
        var r = await admin.PostAsJsonAsync("/api/rfid/cards/register", new RegisterRfidCardRequest(uid), Json.Options);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<RfidOperationResultDto>(Json.Options))!.RfidCardId;
    }

    private static async Task<(List<RfidCardListItemDto> Items, int? Total)> GetAsync(HttpClient admin, string query)
    {
        var r = await admin.GetAsync("/api/rfid/cards?" + query);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        int? total = r.Headers.TryGetValues("X-Total-Count", out var v) ? int.Parse(v.Single()) : null;
        return ((await r.Content.ReadFromJsonAsync<List<RfidCardListItemDto>>(Json.Options))!, total);
    }

    [Fact]
    public async Task WithoutPage_AnswersAsBefore_NoTotalHeader()
    {
        var admin = await _api.AdminClientAsync();
        await RegisterSpareAsync(admin, Uid("UNP"));
        var r = await admin.GetAsync("/api/rfid/cards");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(r.Headers.Contains("X-Total-Count"));
    }

    [Fact]
    public async Task Paged_SlicesCountsAndNewestFirst()
    {
        var admin = await _api.AdminClientAsync();
        // Use a 4-char unique suffix so the security-hardened search (EndsWith last ≤4 chars) can still isolate
        // exactly this test's cards without relying on prefix search, which would reveal hidden UID portions.
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        for (var i = 1; i <= 5; i++)
        {
            await RegisterSpareAsync(admin, $"PAG{i:D6}{suffix}");
            _api.Time.Advance(TimeSpan.FromMinutes(1));
        }
        var (page1, total) = await GetAsync(admin, $"search={suffix}&page=1&pageSize=2");
        var (page3, _) = await GetAsync(admin, $"search={suffix}&page=3&pageSize=2");
        Assert.Equal(5, total);
        Assert.Equal(2, page1.Count);
        Assert.Single(page3);
        // Newest first: ids descend across the first page.
        Assert.True(page1[0].RfidCardId > page1[1].RfidCardId);
    }

    [Fact]
    public async Task Filters_ByStatus_Assigned_AndSearchByStudentOrCard()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);               // an assigned, active card
        using (var db = _api.NewDb()) { db.Students.Single(s => s.StudentId == child.StudentId).FullName = "Rfidflt Carded"; db.SaveChanges(); }
        var spareUid = Uid("RFS");
        var spare = await RegisterSpareAsync(admin, spareUid);
        Assert.True((await admin.PatchAsJsonAsync($"/api/rfid/cards/{spare}/status", new UpdateRfidStatusRequest(RfidCardStatus.Lost), Json.Options)).IsSuccessStatusCode);

        Assert.Equal("Rfidflt Carded", Assert.Single((await GetAsync(admin, "search=Rfidflt&page=1")).Items).StudentName);   // student name
        Assert.Equal(spare, Assert.Single((await GetAsync(admin, $"search={spareUid}&page=1")).Items).RfidCardId);          // card uid
        Assert.Equal(spare, Assert.Single((await GetAsync(admin, $"search={spareUid}&status=Lost&page=1")).Items).RfidCardId);
        Assert.Empty((await GetAsync(admin, $"search={spareUid}&status=Active&page=1")).Items);
        Assert.Equal(spare, Assert.Single((await GetAsync(admin, $"search={spareUid}&assigned=false&page=1")).Items).RfidCardId);
        Assert.Empty((await GetAsync(admin, $"search={spareUid}&assigned=true&page=1")).Items);
    }

    [Fact]
    public async Task Sorts_ByStatusAndStudent()
    {
        var admin = await _api.AdminClientAsync();
        // 4-char unique suffix: security fix restricts UID search to exact/suffix/name, not prefix.
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var a = await RegisterSpareAsync(admin, $"SR1A{suffix}");
        var b = await RegisterSpareAsync(admin, $"SR2B{suffix}");
        Assert.True((await admin.PatchAsJsonAsync($"/api/rfid/cards/{b}/status", new UpdateRfidStatusRequest(RfidCardStatus.Lost), Json.Options)).IsSuccessStatusCode);
        var asc = await GetAsync(admin, $"search={suffix}&page=1&sort=status&dir=asc");
        Assert.Equal(new[] { a, b }, asc.Items.Select(c => c.RfidCardId));        // Active before Lost
        var desc = await GetAsync(admin, $"search={suffix}&page=1&sort=status&dir=desc");
        Assert.Equal(new[] { b, a }, desc.Items.Select(c => c.RfidCardId));
    }

    [Fact]
    public async Task Summary_MatchesTheFilters()
    {
        var admin = await _api.AdminClientAsync();
        async Task<RfidCardSummaryDto> Summary() => (await admin.GetFromJsonAsync<RfidCardSummaryDto>("/api/rfid/cards/summary", Json.Options))!;
        var before = await Summary();
        await RegisterSpareAsync(admin, Uid("SUM"));                           // active, unassigned: a spare
        var lost = await RegisterSpareAsync(admin, Uid("SML"));
        Assert.True((await admin.PatchAsJsonAsync($"/api/rfid/cards/{lost}/status", new UpdateRfidStatusRequest(RfidCardStatus.Damaged), Json.Options)).IsSuccessStatusCode);
        var after = await Summary();

        Assert.Equal(before.Total + 2, after.Total);
        Assert.Equal(before.Active + 1, after.Active);
        Assert.Equal(before.LostOrDamaged + 1, after.LostOrDamaged);
        Assert.Equal(before.AvailableSpare + 1, after.AvailableSpare);
        Assert.Equal(before.Unassigned + 2, after.Unassigned);
        Assert.Equal(after.Total, (await GetAsync(admin, "page=1&pageSize=1")).Total);
        Assert.Equal(after.Active, (await GetAsync(admin, "status=Active&page=1&pageSize=1")).Total);
        Assert.Equal(after.Unassigned, (await GetAsync(admin, "assigned=false&page=1&pageSize=1")).Total);
        Assert.Equal(after.AvailableSpare, (await GetAsync(admin, "status=Active&assigned=false&page=1&pageSize=1")).Total);
    }

    [Theory]
    [InlineData("page=1&sort=cardUid")]
    [InlineData("page=0")]
    [InlineData("page=1&pageSize=101")]
    [InlineData("page=1&status=Broken")]
    public async Task BadPaging_Is400(string query)
    {
        var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/rfid/cards?" + query)).StatusCode);
    }

    [Fact]
    public async Task RequiresAdmin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/rfid/cards?page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/rfid/cards/summary")).StatusCode);
    }
}
