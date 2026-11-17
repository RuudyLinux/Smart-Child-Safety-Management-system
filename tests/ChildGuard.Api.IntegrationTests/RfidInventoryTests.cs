using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Milestone 3B against the real database: a card inventory (register, assign, unassign, replace, reassign, retire,
/// delete), complete assignment history, the new Damaged and Retired conditions, and row locking so one card can never
/// end up with two students.
/// </summary>
public class RfidInventoryTests : IClassFixture<EnrollmentApiFactory>
{
    private readonly EnrollmentApiFactory _api;
    public RfidInventoryTests(EnrollmentApiFactory api) => _api = api;

    private static string Uid() => "INV" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    private const string Why = "Testing the card inventory.";

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private async Task<int> StudentAsync(RecordStatus status = RecordStatus.Active)
    {
        await using var db = _api.NewDb();
        var s = new Student { FullName = "Inv Kid " + Guid.NewGuid().ToString("N")[..8], DateOfBirth = new DateTime(2016, 2, 2), Status = status };
        db.Students.Add(s);
        await db.SaveChangesAsync();
        return s.StudentId;
    }

    private static async Task<int> RegisterAsync(HttpClient admin, string uid) =>
        (await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync("/api/rfid/cards/register", new RegisterRfidCardRequest(uid), Json.Options))).RfidCardId;

    private static Task<HttpResponseMessage> AssignAsync(HttpClient admin, int cardId, int studentId) =>
        admin.PostAsJsonAsync($"/api/rfid/cards/{cardId}/assign", new AssignRegisteredCardRequest(studentId), Json.Options);

    private async Task<RfidCard> CardAsync(int id)
    {
        await using var db = _api.NewDb();
        return await db.RfidCards.AsNoTracking().SingleAsync(c => c.RfidCardId == id);
    }

    private async Task<List<RfidCardAssignment>> HistoryAsync(int cardId)
    {
        await using var db = _api.NewDb();
        return await db.RfidCardAssignments.AsNoTracking().Where(h => h.RfidCardId == cardId).OrderBy(h => h.Id).ToListAsync();
    }

    private async Task<int> DeviceAsync()
    {
        await using var db = _api.NewDb();
        return (await db.RfidDevices.OrderBy(d => d.RfidDeviceId).FirstAsync()).RfidDeviceId;
    }

    private async Task<RfidScanResultDto> ScanAsync(HttpClient admin, string uid) =>
        await Ok<RfidScanResultDto>(await admin.PostAsJsonAsync("/api/rfid/scan",
            new RfidScanRequest(uid, await DeviceAsync(), JourneyEventType.BusBoarding), Json.Options));

    [Fact]
    public async Task RegisterThenAssign_RecordsHistory_AndRefusesDuplicatesAndIneligibleStudents()
    {
        var admin = await _api.AdminClientAsync();
        var uid = Uid();
        var cardId = await RegisterAsync(admin, uid);
        var card = await CardAsync(cardId);
        Assert.Equal((RfidCardStatus.Active, (int?)null), (card.Status, card.StudentId));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/rfid/cards/register", new RegisterRfidCardRequest(uid.ToLowerInvariant()), Json.Options)).StatusCode);
        var check = await Ok<RfidCardCheckDto>(await admin.PostAsJsonAsync("/api/rfid/cards/check", new RfidCardCheckRequest(uid), Json.Options));
        Assert.Equal(RfidCardAvailability.Available, check.Availability);

        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(admin, cardId, await StudentAsync(RecordStatus.Inactive))).StatusCode);
        var holder = await _api.NewChildAsync(withFace: false);
        Assert.Contains("already has an RFID card", await (await AssignAsync(admin, cardId, holder.StudentId)).MessageAsync());

        var studentId = await StudentAsync();
        await Ok<RfidOperationResultDto>(await AssignAsync(admin, cardId, studentId));
        Assert.Equal(studentId, (await CardAsync(cardId)).StudentId);
        var h = Assert.Single(await HistoryAsync(cardId));
        Assert.Equal((studentId, (DateTime?)null, false), (h.StudentId, h.EndedAt, h.IsMigratedSnapshot));
        Assert.NotNull(h.AssignedByAdminId);
        Assert.True((await ScanAsync(admin, uid)).EventRecorded);

        // An assigned card cannot be assigned again.
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(admin, cardId, await StudentAsync())).StatusCode);

        await using var db = _api.NewDb();
        var actions = await db.AuditEntries.Where(e => e.EntityType == "RfidCard" && e.EntityId == cardId).Select(e => e.Action).ToListAsync();
        Assert.Equal(new[] { "RfidCardRegistered", "RfidCardAssigned" }, actions);
        Assert.False(await db.AuditEntries.AnyAsync(e => e.EntityId == cardId && e.EntityType == "RfidCard" && (e.Summary.Contains(uid) || (e.Details != null && e.Details.Contains(uid)))));
    }

    [Fact]
    public async Task Unassign_EndsTheHistory_SetsTheCondition_AndTheCardStopsScanning()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await using (var db = _api.NewDb())
        {
            var c = await db.RfidCards.SingleAsync(x => x.CardUid == child.CardUid);
            db.RfidCardAssignments.Add(new RfidCardAssignment { RfidCardId = c.RfidCardId, StudentId = child.StudentId, AssignedAt = c.AssignedAt, RecordedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var cardId = (await Ok<List<RfidCardListItemDto>>(await admin.GetAsync("/api/rfid/cards"))).Single(c => c.StudentId == child.StudentId).RfidCardId;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/rfid/cards/{cardId}/unassign", new UnassignRfidRequest("no"), Json.Options)).StatusCode);
        await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync($"/api/rfid/cards/{cardId}/unassign", new UnassignRfidRequest("Lost on the school bus.", RfidCardStatus.Lost), Json.Options));

        var card = await CardAsync(cardId);
        Assert.Equal((RfidCardStatus.Lost, (int?)null, (DateTime?)null), (card.Status, card.StudentId, card.AssignedAt));
        var h = Assert.Single(await HistoryAsync(cardId));
        Assert.NotNull(h.EndedAt);
        Assert.Contains("Lost on the school bus.", h.EndReason);
        Assert.NotNull(h.EndedByAdminId);
        Assert.False((await ScanAsync(admin, child.CardUid)).StudentIdentified);
        Assert.Null((await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{child.StudentId}"))).MaskedRfid);

        var detail = await Ok<RfidCardDetailDto>(await admin.GetAsync($"/api/rfid/cards/{cardId}"));
        Assert.Equal(child.StudentId, Assert.Single(detail.History!).StudentId);
        Assert.False(detail.CanDelete); // it has history
    }

    [Fact]
    public async Task Replace_WithANewCard_OrASpare_KeepsTheStudentCovered_AndRecordsBothSides()
    {
        var admin = await _api.AdminClientAsync();
        var studentId = await StudentAsync();
        var oldUid = Uid();
        var oldId = await RegisterAsync(admin, oldUid);
        await Ok<RfidOperationResultDto>(await AssignAsync(admin, oldId, studentId));

        var newUid = Uid();
        var replaced = await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync($"/api/rfid/cards/{oldId}/replace",
            new ReplaceRfidRequest(newUid, RfidCardStatus.Damaged, "Card snapped in half."), Json.Options));
        Assert.Equal(studentId, replaced.StudentId);
        Assert.Equal((RfidCardStatus.Damaged, (int?)null), ((await CardAsync(oldId)).Status, (await CardAsync(oldId)).StudentId));
        Assert.Equal(studentId, (await CardAsync(replaced.RfidCardId)).StudentId);
        Assert.Contains("Replaced:", (await HistoryAsync(oldId)).Single().EndReason);
        Assert.Null((await HistoryAsync(replaced.RfidCardId)).Single().EndedAt);
        Assert.False((await ScanAsync(admin, oldUid)).StudentIdentified);
        Assert.True((await ScanAsync(admin, newUid)).EventRecorded);

        // Replacing with a registered spare uses that card; a card someone else holds is refused and nothing changes.
        var spareUid = Uid();
        var spareId = await RegisterAsync(admin, spareUid);
        var taken = await _api.NewChildAsync(withFace: false);
        var refused = await admin.PostAsJsonAsync($"/api/rfid/cards/{replaced.RfidCardId}/replace", new ReplaceRfidRequest(taken.CardUid, RfidCardStatus.Lost, "Trying a held card."), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(studentId, (await CardAsync(replaced.RfidCardId)).StudentId);
        Assert.Equal(RfidCardStatus.Active, (await CardAsync(replaced.RfidCardId)).Status);

        await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync($"/api/rfid/cards/{replaced.RfidCardId}/replace", new ReplaceRfidRequest(spareUid, RfidCardStatus.Lost, "Second card lost too."), Json.Options));
        Assert.Equal(studentId, (await CardAsync(spareId)).StudentId);

        await using var db = _api.NewDb();
        Assert.Equal(2, await db.AuditEntries.CountAsync(e => e.Action == "RfidCardReplaced" && e.StudentId == studentId));
        Assert.Equal(3, await db.RfidCardAssignments.CountAsync(h => h.StudentId == studentId));
    }

    [Fact]
    public async Task Reassign_MovesTheCard_ToAStudentWithoutOne()
    {
        var admin = await _api.AdminClientAsync();
        var a = await StudentAsync();
        var b = await StudentAsync();
        var cardId = await RegisterAsync(admin, Uid());
        await Ok<RfidOperationResultDto>(await AssignAsync(admin, cardId, a));

        var holder = await _api.NewChildAsync(withFace: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/rfid/cards/{cardId}/reassign", new ReassignRfidRequest(holder.StudentId, "Wrong sibling."), Json.Options)).StatusCode);
        await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync($"/api/rfid/cards/{cardId}/reassign", new ReassignRfidRequest(b, "Issued to the wrong sibling."), Json.Options));

        Assert.Equal(b, (await CardAsync(cardId)).StudentId);
        var history = await HistoryAsync(cardId);
        Assert.Equal(new[] { a, b }, history.Select(h => h.StudentId));
        Assert.NotNull(history[0].EndedAt);
        Assert.Null(history[1].EndedAt);
    }

    [Fact]
    public async Task Retired_IsFinal_NeedsAnUnassignedCard_AndBlocksAssignment()
    {
        var admin = await _api.AdminClientAsync();
        var studentId = await StudentAsync();
        var cardId = await RegisterAsync(admin, Uid());
        await Ok<RfidOperationResultDto>(await AssignAsync(admin, cardId, studentId));

        // The existing status endpoint answers every refusal with 404 and a message.
        var retireAssigned = await admin.PatchAsJsonAsync($"/api/rfid/cards/{cardId}/status", new UpdateRfidStatusRequest(RfidCardStatus.Retired), Json.Options);
        Assert.False(retireAssigned.IsSuccessStatusCode);
        Assert.Contains("Unassign or replace", await retireAssigned.MessageAsync());
        Assert.Equal(studentId, (await CardAsync(cardId)).StudentId);
        Assert.Equal(RfidCardStatus.Active, (await CardAsync(cardId)).Status);

        await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync($"/api/rfid/cards/{cardId}/unassign", new UnassignRfidRequest("Card worn out.", RfidCardStatus.Retired), Json.Options));
        Assert.Equal(RfidCardStatus.Retired, (await CardAsync(cardId)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync($"/api/rfid/cards/{cardId}/status", new UpdateRfidStatusRequest(RfidCardStatus.Active), Json.Options)).StatusCode);
        Assert.Equal(RfidCardStatus.Retired, (await CardAsync(cardId)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(admin, cardId, await StudentAsync())).StatusCode);

        var damagedId = await RegisterAsync(admin, Uid());
        (await admin.PatchAsJsonAsync($"/api/rfid/cards/{damagedId}/status", new UpdateRfidStatusRequest(RfidCardStatus.Damaged), Json.Options)).EnsureSuccessStatusCode();
        Assert.Contains("Damaged", await (await AssignAsync(admin, damagedId, await StudentAsync())).MessageAsync());
    }

    [Fact]
    public async Task OnlyANeverUsedCard_CanBeDeleted()
    {
        var admin = await _api.AdminClientAsync();
        var unused = await RegisterAsync(admin, Uid());
        var used = await RegisterAsync(admin, Uid());
        await Ok<RfidOperationResultDto>(await AssignAsync(admin, used, await StudentAsync()));
        await Ok<RfidOperationResultDto>(await admin.PostAsJsonAsync($"/api/rfid/cards/{used}/unassign", new UnassignRfidRequest("Returned to the office."), Json.Options));

        Assert.True((await Ok<RfidCardDetailDto>(await admin.GetAsync($"/api/rfid/cards/{unused}"))).CanDelete);
        var del = (int id) => admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/rfid/cards/{id}")
        { Content = JsonContent.Create(new DeleteRecordRequest("Registered by mistake."), options: Json.Options) });

        Assert.Equal(HttpStatusCode.Conflict, (await del(used)).StatusCode);
        await Ok<RfidOperationResultDto>(await del(unused));
        await using var db = _api.NewDb();
        Assert.False(await db.RfidCards.AnyAsync(c => c.RfidCardId == unused));
        Assert.True(await db.RfidCards.AnyAsync(c => c.RfidCardId == used));
        Assert.Single(await db.AuditEntries.Where(e => e.Action == "RfidCardDeleted" && e.EntityId == unused).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentAssignmentsOfOneSpareCard_GiveItToExactlyOneStudent()
    {
        var cardId = await RegisterAsync(await _api.AdminClientAsync(), Uid());
        var students = new List<int>();
        var clients = new List<HttpClient>();
        for (var i = 0; i < 6; i++) { students.Add(await StudentAsync()); clients.Add(await _api.AdminClientAsync()); }

        var responses = await TestData.Simultaneously(6, i => AssignAsync(clients[i], cardId, students[i]));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        var winner = students[Array.FindIndex(responses, r => r.IsSuccessStatusCode)];
        Assert.Equal(winner, (await CardAsync(cardId)).StudentId);
        Assert.Equal(winner, Assert.Single(await HistoryAsync(cardId)).StudentId);
    }

    [Fact]
    public async Task ConcurrentEnrollmentsTakingOneSpareCard_EnrollOnlyOneWithIt()
    {
        var uid = Uid();
        await RegisterAsync(await _api.AdminClientAsync(), uid);
        var clients = new List<HttpClient>();
        for (var i = 0; i < 4; i++) clients.Add(await _api.AdminClientAsync());

        var responses = await TestData.Simultaneously(4, i => clients[i].PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(
            Guid.NewGuid().ToString("N"), new EnrollmentStudentDto("Spare Race " + Guid.NewGuid().ToString("N")[..8], new DateTime(2016, 3, 3), "1", null, null, null, null),
            [new EnrollmentGuardianDto(null, "Spare Parent " + i, $"{Guid.NewGuid():N}@inv.test", "93" + Random.Shared.Next(10000000, 99999999), null, true, null, true)],
            uid, null), Json.Options));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        await using var db = _api.NewDb();
        var card = await db.RfidCards.SingleAsync(c => c.CardUid == uid);
        Assert.NotNull(card.StudentId);
        Assert.Single(await db.RfidCardAssignments.Where(h => h.RfidCardId == card.RfidCardId).ToListAsync());
    }

    [Fact]
    public async Task InventoryEndpoints_AreAdminOnly()
    {
        var guardian = _api.GuardianClient((await _api.NewChildAsync(withFace: false)).GuardianId);
        var anonymous = _api.CreateClient();
        foreach (var c in new[] { guardian, anonymous })
        {
            var expected = c == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await c.PostAsJsonAsync("/api/rfid/cards/register", new RegisterRfidCardRequest(Uid()), Json.Options)).StatusCode);
            Assert.Equal(expected, (await c.PostAsJsonAsync("/api/rfid/cards/1/assign", new AssignRegisteredCardRequest(1), Json.Options)).StatusCode);
            Assert.Equal(expected, (await c.PostAsJsonAsync("/api/rfid/cards/1/unassign", new UnassignRfidRequest(Why), Json.Options)).StatusCode);
            Assert.Equal(expected, (await c.PostAsJsonAsync("/api/rfid/cards/1/replace", new ReplaceRfidRequest(Uid(), RfidCardStatus.Lost, Why), Json.Options)).StatusCode);
            Assert.Equal(expected, (await c.PostAsJsonAsync("/api/rfid/cards/1/reassign", new ReassignRfidRequest(1, Why), Json.Options)).StatusCode);
            Assert.Equal(expected, (await c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/rfid/cards/1")
            { Content = JsonContent.Create(new DeleteRecordRequest(Why + Why), options: Json.Options) })).StatusCode);
        }
    }
}
