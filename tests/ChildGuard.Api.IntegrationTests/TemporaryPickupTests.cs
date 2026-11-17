using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Temporary pickup guardians end to end against the real database: request rules, admin decisions, QR,
/// face check, handover, expiry, revocation and the audit trail.
/// </summary>
public class TemporaryPickupTests : IClassFixture<ApiFactory>
{
    // Smallest bytes the image check accepts: a JPEG header.
    private static readonly string Photo = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6 });

    private readonly ApiFactory _api;
    public TemporaryPickupTests(ApiFactory api) => _api = api;

    private CreateTemporaryPickupRequest Request(int studentId, TimeSpan? length = null, TimeSpan? startIn = null, string? photo = null)
    {
        var from = _api.Time.UtcNow + (startIn ?? TimeSpan.Zero);
        return new CreateTemporaryPickupRequest(
            studentId, "Ravi Kumar", "98200 12345", "Uncle", from, from + (length ?? TimeSpan.FromHours(3)), photo ?? Photo, "Licence ending 1234");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;

    private async Task<TemporaryPickupDto> CreateAsync(HttpClient guardian, int studentId, TimeSpan? length = null, TimeSpan? startIn = null)
    {
        var r = await guardian.PostAsJsonAsync("/api/me/temporary-pickups", Request(studentId, length, startIn), Json.Options);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await ReadAsync<TemporaryPickupDto>(r);
    }

    private async Task<TemporaryPickupDto> ApprovedAsync(HttpClient admin, HttpClient guardian, int studentId)
    {
        var created = await CreateAsync(guardian, studentId);
        var approve = await admin.PostAsync($"/api/temporary-pickups/{created.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        return await ReadAsync<TemporaryPickupDto>(approve);
    }

    private async Task<string> QrAsync(HttpClient guardian, int id)
    {
        var r = await guardian.GetAsync($"/api/me/temporary-pickups/{id}/qr");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await ReadAsync<TemporaryPickupQrDto>(r)).Token;
    }

    private static Task<HttpResponseMessage> HandoverAsync(HttpClient admin, int id, string token) =>
        admin.PostAsJsonAsync($"/api/temporary-pickups/{id}/handover", new TemporaryPickupHandoverRequest(token, Photo), Json.Options);

    // ---- request rules ----

    [Fact]
    public async Task Request_ByAuthorisedGuardian_IsPending_WithPhotoAndNoQr()
    {
        var child = await _api.NewChildAsync();
        var dto = await CreateAsync(_api.GuardianClient(child.GuardianId), child.StudentId);

        Assert.Equal(TemporaryPickupState.PendingApproval, dto.State);
        Assert.True(dto.HasPhoto);
        Assert.True(dto.RequestedByYou);
        Assert.False(dto.QrAvailable);
        Assert.True(dto.CanCancel);
        Assert.Equal("98200 12345".Replace(" ", ""), dto.Phone);
    }

    [Fact]
    public async Task Request_ForAnotherGuardiansChild_IsRefused()
    {
        var mine = await _api.NewChildAsync();
        var other = await _api.NewChildAsync();

        var r = await _api.GuardianClient(mine.GuardianId).PostAsJsonAsync("/api/me/temporary-pickups", Request(other.StudentId), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("not linked", await r.MessageAsync());
    }

    [Fact]
    public async Task Request_ByLinkedGuardianWithoutPickupAuthority_IsRefused()
    {
        var child = await _api.NewChildAsync();
        int viewerId;
        await using (var db = _api.NewDb())
        {
            var g = new Guardian { FullName = "Viewer", Email = $"v.{Guid.NewGuid():N}@childguard.test", Phone = $"97{Random.Shared.Next(10000000, 99999999)}", PasswordHash = "x" };
            db.Add(g);
            db.Add(new StudentGuardian { StudentId = child.StudentId, Guardian = g, IsAuthorizedForPickup = false, Relationship = "Aunt" });
            await db.SaveChangesAsync();
            viewerId = g.GuardianId;
        }

        var r = await _api.GuardianClient(viewerId).PostAsJsonAsync("/api/me/temporary-pickups", Request(child.StudentId), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("authorised", await r.MessageAsync());
    }

    [Fact]
    public async Task Request_LongerThan24Hours_IsRefused()
    {
        var child = await _api.NewChildAsync();
        var r = await _api.GuardianClient(child.GuardianId).PostAsJsonAsync(
            "/api/me/temporary-pickups", Request(child.StudentId, TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1)), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("24 hours", await r.MessageAsync());
    }

    [Fact]
    public async Task Request_Of_Exactly24Hours_IsAccepted()
    {
        var child = await _api.NewChildAsync();
        var dto = await CreateAsync(_api.GuardianClient(child.GuardianId), child.StudentId, TimeSpan.FromHours(24));
        Assert.Equal(TimeSpan.FromHours(24), dto.ValidUntilUtc - dto.ValidFromUtc);
    }

    [Fact]
    public async Task Request_InThePast_OrWithoutPhoto_IsRefused()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);

        var past = await guardian.PostAsJsonAsync("/api/me/temporary-pickups",
            Request(child.StudentId, TimeSpan.FromHours(1), TimeSpan.FromHours(-5)), Json.Options);
        var noPhoto = await guardian.PostAsJsonAsync("/api/me/temporary-pickups",
            Request(child.StudentId, photo: ""), Json.Options);
        var notAnImage = await guardian.PostAsJsonAsync("/api/me/temporary-pickups",
            Request(child.StudentId, photo: Convert.ToBase64String("not an image at all"u8.ToArray())), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noPhoto.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notAnImage.StatusCode);
    }

    [Fact]
    public async Task Request_OverlappingAnOpenOne_IsRefused_ButAfterCancelItIsAllowed()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var first = await CreateAsync(guardian, child.StudentId);

        var overlap = await guardian.PostAsJsonAsync("/api/me/temporary-pickups", Request(child.StudentId, TimeSpan.FromHours(1)), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);

        (await guardian.PostAsync($"/api/me/temporary-pickups/{first.Id}/cancel", null)).EnsureSuccessStatusCode();
        await CreateAsync(guardian, child.StudentId);
    }

    // ---- decisions ----

    [Fact]
    public async Task Approve_IssuesQr_OnlyToTheRequestingGuardian()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var pending = await CreateAsync(guardian, child.StudentId);

        // Before approval there is no QR.
        Assert.Equal(HttpStatusCode.BadRequest, (await guardian.GetAsync($"/api/me/temporary-pickups/{pending.Id}/qr")).StatusCode);

        var approve = await admin.PostAsync($"/api/temporary-pickups/{pending.Id}/approve", null);
        Assert.Equal(TemporaryPickupState.Active, (await ReadAsync<TemporaryPickupDto>(approve)).State);
        var qr = await QrAsync(guardian, pending.Id);
        Assert.StartsWith("CGTP1.", qr);

        // A guardian of a different child cannot see the request or its QR.
        var other = await _api.NewChildAsync();
        var stranger = _api.GuardianClient(other.GuardianId);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/me/temporary-pickups/{pending.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.GetAsync($"/api/me/temporary-pickups/{pending.Id}/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.PostAsync($"/api/me/temporary-pickups/{pending.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task CoGuardian_SeesTheRequest_ButCannotCancelOrGetTheQr()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        int coId;
        await using (var db = _api.NewDb())
        {
            var g = new Guardian { FullName = "Co", Email = $"co.{Guid.NewGuid():N}@childguard.test", Phone = $"96{Random.Shared.Next(10000000, 99999999)}", PasswordHash = "x" };
            db.Add(g);
            db.Add(new StudentGuardian { StudentId = child.StudentId, Guardian = g, IsAuthorizedForPickup = true, Relationship = "Father" });
            await db.SaveChangesAsync();
            coId = g.GuardianId;
        }
        var requester = _api.GuardianClient(child.GuardianId);
        var approved = await ApprovedAsync(admin, requester, child.StudentId);
        var co = _api.GuardianClient(coId);

        var list = await ReadAsync<List<TemporaryPickupDto>>(await co.GetAsync("/api/me/temporary-pickups"));
        Assert.Contains(list, p => p.Id == approved.Id && !p.RequestedByYou && !p.CanCancel);
        Assert.Equal(HttpStatusCode.BadRequest, (await co.PostAsync($"/api/me/temporary-pickups/{approved.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await co.GetAsync($"/api/me/temporary-pickups/{approved.Id}/qr")).StatusCode);
    }

    [Fact]
    public async Task Reject_NeedsReason_AndEndsTheRequest()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var created = await CreateAsync(_api.GuardianClient(child.GuardianId), child.StudentId);

        var noReason = await admin.PostAsJsonAsync($"/api/temporary-pickups/{created.Id}/reject", new TemporaryPickupDecisionRequest(null), Json.Options);
        var rejected = await admin.PostAsJsonAsync($"/api/temporary-pickups/{created.Id}/reject", new TemporaryPickupDecisionRequest("Photo unclear"), Json.Options);
        var thenApprove = await admin.PostAsync($"/api/temporary-pickups/{created.Id}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(TemporaryPickupState.Rejected, (await ReadAsync<TemporaryPickupDto>(rejected)).State);
        Assert.Equal(HttpStatusCode.BadRequest, thenApprove.StatusCode);
    }

    [Fact]
    public async Task GuardianToken_CannotUseAdminRoutes_AndAdminTokenCannotUseGuardianRoutes()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var created = await CreateAsync(guardian, child.StudentId);

        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PostAsync($"/api/temporary-pickups/{created.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/temporary-pickups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/me/temporary-pickups")).StatusCode);
    }

    // ---- handover ----

    [Fact]
    public async Task Handover_WithQrAndFaceMatch_RecordsDepartureOnce_AndKillsTheQr()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);

        var scan = await ReadAsync<TemporaryPickupScanResultDto>(
            await admin.PostAsJsonAsync("/api/temporary-pickups/scan", new ScanTemporaryPickupRequest(token), Json.Options));
        Assert.True(scan.CanHandOver);
        Assert.Equal("Ravi Kumar", scan.Pickup.FullName);

        _api.Face.NextMatch = true;
        var done = await HandoverAsync(admin, approved.Id, token);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.True((await ReadAsync<TemporaryPickupHandoverResultDto>(done)).HandedOver);

        await using (var db = _api.NewDb())
        {
            Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
            Assert.Equal(1, await db.AttendanceRecords.CountAsync(a => a.StudentId == child.StudentId && a.EventType == AttendanceEventType.SchoolDeparture));
            var row = await db.TemporaryPickupAuthorizations.SingleAsync(a => a.Id == approved.Id);
            Assert.Equal(TemporaryPickupStatus.Completed, row.Status);
            Assert.Null(row.QrNonce);
            Assert.NotNull(row.JourneyEventId);
        }

        // Duplicate pickup: the same QR is dead, and a guardian pickup is blocked because the child already left.
        var again = await HandoverAsync(admin, approved.Id, token);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var verification = await admin.VerifyFaceAsync(_api, child);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.ApproveAsync(verification)).StatusCode);
    }

    [Fact]
    public async Task Handover_WithWrongFace_IsRefused_ChildStaysAtSchool_AndIsAudited()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);

        _api.Face.NextMatch = false;
        var r = await HandoverAsync(admin, approved.Id, token);
        _api.Face.NextMatch = true;

        var body = await ReadAsync<TemporaryPickupHandoverResultDto>(r);
        Assert.False(body.HandedOver);
        Assert.False(body.FaceMatched);

        await using var db = _api.NewDb();
        Assert.Equal(0, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
        Assert.Equal(TemporaryPickupStatus.Approved, (await db.TemporaryPickupAuthorizations.SingleAsync(a => a.Id == approved.Id)).Status);
        Assert.Contains(await db.TemporaryPickupAuditEntries.Where(a => a.AuthorizationId == approved.Id).Select(a => a.Action).ToListAsync(), a => a == "FaceMismatch");
        Assert.Contains(await db.Notifications.Where(n => n.GuardianId == child.GuardianId).Select(n => n.Title).ToListAsync(), t => t.Contains("refused"));
    }

    [Fact]
    public async Task Handover_WhenChildIsNotCheckedIn_IsRefused()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);

        var scan = await ReadAsync<TemporaryPickupScanResultDto>(
            await admin.PostAsJsonAsync("/api/temporary-pickups/scan", new ScanTemporaryPickupRequest(token), Json.Options));
        var r = await HandoverAsync(admin, approved.Id, token);

        Assert.False(scan.CanHandOver);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("not checked in", await r.MessageAsync());
    }

    [Fact]
    public async Task Handover_BeforeTheWindowOpens_IsRefused_AndAfterItCloses_TheQrIsGone()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var created = await CreateAsync(guardian, child.StudentId, TimeSpan.FromHours(2), TimeSpan.FromHours(3));
        (await admin.PostAsync($"/api/temporary-pickups/{created.Id}/approve", null)).EnsureSuccessStatusCode();
        var token = await QrAsync(guardian, created.Id);

        var early = await HandoverAsync(admin, created.Id, token);
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.Contains("not yet", await early.MessageAsync());

        _api.Time.Advance(TimeSpan.FromHours(6));
        try
        {
            var late = await HandoverAsync(admin, created.Id, token);
            Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
            Assert.Contains("expired", await late.MessageAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await guardian.GetAsync($"/api/me/temporary-pickups/{created.Id}/qr")).StatusCode);
        }
        finally
        {
            _api.Time.Advance(TimeSpan.FromHours(-6));
        }
    }

    [Fact]
    public async Task Revoke_KillsTheQrImmediately_AndNotifiesGuardians()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);

        var revoke = await admin.PostAsJsonAsync($"/api/temporary-pickups/{approved.Id}/revoke", new TemporaryPickupDecisionRequest("Custody order"), Json.Options);
        var use = await HandoverAsync(admin, approved.Id, token);

        Assert.Equal(TemporaryPickupState.Revoked, (await ReadAsync<TemporaryPickupDto>(revoke)).State);
        Assert.Equal(HttpStatusCode.BadRequest, use.StatusCode);
        Assert.Contains("not valid", await use.MessageAsync());
        await using var db = _api.NewDb();
        Assert.Contains(await db.Notifications.Where(n => n.GuardianId == child.GuardianId).Select(n => n.Title).ToListAsync(), t => t.Contains("revoked"));
    }

    [Fact]
    public async Task ForgedOrForeignTokens_AreRefused()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);
        var parts = token.Split('.');
        var forged = $"{parts[0]}.{parts[1]}.{parts[2]}.{new string('A', parts[3].Length)}";

        foreach (var bad in new[] { forged, "garbage", $"CGTP1.{approved.Id}.AAAA.BBBB", $"CGTP1.999999.{parts[2]}.{parts[3]}" })
        {
            var scan = await admin.PostAsJsonAsync("/api/temporary-pickups/scan", new ScanTemporaryPickupRequest(bad), Json.Options);
            Assert.Equal(HttpStatusCode.BadRequest, scan.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await HandoverAsync(admin, approved.Id, bad)).StatusCode);
        }
        await using var db = _api.NewDb();
        Assert.Equal(TemporaryPickupStatus.Approved, (await db.TemporaryPickupAuthorizations.SingleAsync(a => a.Id == approved.Id)).Status);
    }

    [Fact]
    public async Task Token_OfAnotherRequest_CannotCompleteThisOne()
    {
        var admin = await _api.AdminClientAsync();
        var a = await _api.NewChildAsync();
        var b = await _api.NewChildAsync();
        await admin.ScanOkAsync(a, JourneyEventType.SchoolArrival);
        await admin.ScanOkAsync(b, JourneyEventType.SchoolArrival);
        var ga = _api.GuardianClient(a.GuardianId);
        var gb = _api.GuardianClient(b.GuardianId);
        var pa = await ApprovedAsync(admin, ga, a.StudentId);
        var pb = await ApprovedAsync(admin, gb, b.StudentId);
        var tokenB = await QrAsync(gb, pb.Id);

        var r = await HandoverAsync(admin, pa.Id, tokenB);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.JourneyEvents.CountAsync(e => e.StudentId == a.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
    }

    [Fact]
    public async Task ConcurrentHandovers_CompleteExactlyOnce()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);

        var results = await TestData.Simultaneously(4, _ => HandoverAsync(admin, approved.Id, token));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
    }

    // ---- audit, feed and notifications ----

    [Fact]
    public async Task FullLifecycle_IsAudited_AndShowsInPickupFeed()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        var token = await QrAsync(guardian, approved.Id);
        await admin.PostAsJsonAsync("/api/temporary-pickups/scan", new ScanTemporaryPickupRequest(token), Json.Options);
        (await HandoverAsync(admin, approved.Id, token)).EnsureSuccessStatusCode();

        var detail = await ReadAsync<TemporaryPickupDetailDto>(await admin.GetAsync($"/api/temporary-pickups/{approved.Id}"));
        Assert.Equal(new[] { "Requested", "Approved", "QrScanned", "HandedOver" }, detail.Audit.Select(a => a.Action).ToArray());

        var feed = await ReadAsync<List<GuardianPickupDto>>(await guardian.GetAsync("/api/me/pickups"));
        var item = Assert.Single(feed, p => p.Kind == "Temporary");
        Assert.Equal(PickupVerificationState.Approved, item.State);
        Assert.Equal("Ravi Kumar", item.PickedUpBy);

        var summary = await ReadAsync<TemporaryPickupSummaryDto>(await admin.GetAsync("/api/temporary-pickups/summary"));
        Assert.True(summary.CompletedToday >= 1);

        var photo = await admin.GetAsync($"/api/temporary-pickups/{approved.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Embedding_IsStoredProtected_AndNeverReturned()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var r = await guardian.PostAsJsonAsync("/api/me/temporary-pickups", Request(child.StudentId), Json.Options);
        var raw = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain("embedding", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qrNonce", raw, StringComparison.OrdinalIgnoreCase);
        await using var db = _api.NewDb();
        var row = await db.TemporaryPickupAuthorizations.OrderByDescending(a => a.Id).FirstAsync(a => a.StudentId == child.StudentId);
        Assert.NotEmpty(row.FaceEmbeddingJson);
    }

    [Fact]
    public async Task ApprovalAndCancellation_NotifyTheGuardian()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var created = await CreateAsync(guardian, child.StudentId);
        (await admin.PostAsync($"/api/temporary-pickups/{created.Id}/approve", null)).EnsureSuccessStatusCode();
        var cancelled = await guardian.PostAsJsonAsync($"/api/me/temporary-pickups/{created.Id}/cancel", new TemporaryPickupDecisionRequest("Plans changed"), Json.Options);

        Assert.Equal(TemporaryPickupState.Cancelled, (await ReadAsync<TemporaryPickupDto>(cancelled)).State);
        await using var db = _api.NewDb();
        var titles = await db.Notifications.Where(n => n.GuardianId == child.GuardianId).Select(n => n.Title).ToListAsync();
        Assert.Contains("Temporary pickup requested", titles);
        Assert.Contains("Temporary pickup approved", titles);
        Assert.Contains("Temporary pickup cancelled", titles);
    }

    [Fact]
    public async Task TemporaryGuardian_IsNeverAddedToThePermanentGuardianTables()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await using var before = _api.NewDb();
        var guardians = await before.Guardians.CountAsync();
        var links = await before.StudentGuardians.CountAsync();

        var guardian = _api.GuardianClient(child.GuardianId);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var approved = await ApprovedAsync(admin, guardian, child.StudentId);
        (await HandoverAsync(admin, approved.Id, await QrAsync(guardian, approved.Id))).EnsureSuccessStatusCode();

        await using var after = _api.NewDb();
        Assert.Equal(guardians, await after.Guardians.CountAsync());
        Assert.Equal(links, await after.StudentGuardians.CountAsync());
    }
}
