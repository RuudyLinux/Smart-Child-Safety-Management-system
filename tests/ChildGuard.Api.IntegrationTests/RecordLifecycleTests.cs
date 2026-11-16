using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Milestone 3A against the real database: full student record edits, archive and restore of students and guardians
/// (with their effects on transport, sign-in and pickup), and permanent deletion that is refused while history exists.
/// </summary>
public class RecordLifecycleTests : IClassFixture<EnrollmentApiFactory>
{
    private readonly EnrollmentApiFactory _api;
    public RecordLifecycleTests(EnrollmentApiFactory api) => _api = api;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string Phone() => "95" + Random.Shared.Next(10000000, 99999999);
    private const string Reason = "Left the school at the end of term.";

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static Task<HttpResponseMessage> Delete(HttpClient c, string url, string reason) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(new DeleteRecordRequest(reason), options: Json.Options) });

    private async Task<StudentDetailDto> NewStudentAsync(HttpClient admin) =>
        await Ok<StudentDetailDto>(await admin.PostAsJsonAsync("/api/students", new CreateStudentRequest(Unique("Life Kid "), new DateTime(2016, 6, 6), "3"), Json.Options));

    private async Task<int> NewGuardianAsync(HttpClient admin, string? email = null, string? phone = null) =>
        (await Ok<CreateGuardianResultDto>(await admin.PostAsJsonAsync("/api/guardians",
            new CreateGuardianRequest(Unique("Life Parent "), email ?? $"{Guid.NewGuid():N}@life.test", phone ?? Phone()), Json.Options))).GuardianId;

    // ------------------------------------------------------------------ record edits

    [Fact]
    public async Task FullRecordEdit_SavesSectionAdmissionAndGender_AndRefusesADuplicateAdmissionNumber()
    {
        var admin = await _api.AdminClientAsync();
        var a = await NewStudentAsync(admin);
        var b = await NewStudentAsync(admin);
        var number = Unique("ADM");

        var saved = await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{a.StudentId}/record",
            new UpdateStudentRecordRequest(a.FullName, a.DateOfBirth, "4", " C ", number, Gender.Male), Json.Options));
        Assert.Equal(("4", "C", number, Gender.Male), (saved.Grade, saved.Section, saved.AdmissionNumber, saved.Gender));

        var taken = await admin.PutAsJsonAsync($"/api/students/{b.StudentId}/record",
            new UpdateStudentRecordRequest(b.FullName, b.DateOfBirth, null, null, number.ToLowerInvariant(), null), Json.Options);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Null((await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{b.StudentId}"))).AdmissionNumber);

        // Clearing works, and the original endpoint still behaves as before.
        var cleared = await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{a.StudentId}/record",
            new UpdateStudentRecordRequest(a.FullName, a.DateOfBirth, "4", null, null, null), Json.Options));
        Assert.Null(cleared.AdmissionNumber);
        (await admin.PutAsJsonAsync($"/api/students/{a.StudentId}", new UpdateStudentRequest(a.FullName, a.DateOfBirth, "5"), Json.Options)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/students/999999/record",
            new UpdateStudentRecordRequest("X", new DateTime(2015, 1, 1), null, null, null, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/students/{a.StudentId}/record",
            new UpdateStudentRecordRequest("", new DateTime(2015, 1, 1), null, null, null, null), Json.Options)).StatusCode);

        await using var db = _api.NewDb();
        var audit = await db.AuditEntries.Where(e => e.StudentId == a.StudentId && e.Action == "StudentUpdated").OrderBy(e => e.Id).ToListAsync();
        Assert.Contains("admissionNumber", audit.First().Details);
    }

    // ------------------------------------------------------------------ students

    [Fact]
    public async Task ArchivingAStudent_EndsTheirBusSeat_BlocksScans_AndRestoreBringsThemBack()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        var route = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Unique("R "), null), Json.Options));
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("LB"), 10, null, route.RouteId), Json.Options));
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(child.StudentId, bus.BusId, null), Json.Options)).EnsureSuccessStatusCode();

        var check = await Ok<DeletionCheckDto>(await admin.GetAsync($"/api/students/{child.StudentId}/deletion-check"));
        Assert.Contains(check.Warnings, w => w.Contains(bus.BusNumber));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/students/{child.StudentId}/archive", new ArchiveRecordRequest("short"), Json.Options)).StatusCode);
        var archived = await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/students/{child.StudentId}/archive", new ArchiveRecordRequest(Reason), Json.Options));
        Assert.Contains(archived.Effects, e => e.Contains(bus.BusNumber));

        var detail = await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{child.StudentId}"));
        Assert.Equal((RecordStatus.Inactive, Reason), (detail.Status, detail.ArchiveReason));
        Assert.NotNull(detail.ArchivedAt);
        Assert.Null(detail.BusNumber);
        Assert.Equal(0, (await Ok<List<BusDto>>(await admin.GetAsync("/api/transportation/buses"))).Single(b => b.BusId == bus.BusId).ActiveStudentCount);
        Assert.Contains((await Ok<List<StudentListItemDto>>(await admin.GetAsync("/api/students"))), s => s.StudentId == child.StudentId && s.ArchivedAt is not null);

        var scan = await admin.ScanOkAsync(child, JourneyEventType.BusBoarding);
        Assert.False(scan.EventRecorded);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/students/{child.StudentId}/archive", new ArchiveRecordRequest(Reason), Json.Options)).StatusCode);

        await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/students/{child.StudentId}/restore", new RestoreRecordRequest("Re-admitted this term."), Json.Options));
        var restored = await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{child.StudentId}"));
        Assert.Equal(RecordStatus.Active, restored.Status);
        Assert.Null(restored.ArchivedAt);
        Assert.Null(restored.BusNumber); // transport is not restored automatically
        Assert.True((await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival)).EventRecorded);

        await using var db = _api.NewDb();
        var ended = await db.StudentBusAssignments.SingleAsync(a => a.StudentId == child.StudentId);
        Assert.Equal(RecordStatus.Inactive, ended.Status);
        Assert.NotNull(ended.EndedAt);
        var actions = await db.AuditEntries.Where(e => e.StudentId == child.StudentId).Select(e => e.Action).ToListAsync();
        Assert.Contains("StudentArchived", actions);
        Assert.Contains("StudentRestored", actions);
        Assert.Equal(Reason, (await db.AuditEntries.SingleAsync(e => e.StudentId == child.StudentId && e.Action == "StudentArchived")).Reason);
    }

    [Fact]
    public async Task ActivatingAnArchivedStudent_ThroughTheOldStatusEndpoint_ClearsTheArchive()
    {
        var admin = await _api.AdminClientAsync();
        var s = await NewStudentAsync(admin);
        await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/students/{s.StudentId}/archive", new ArchiveRecordRequest(Reason), Json.Options));
        (await admin.PatchAsJsonAsync($"/api/students/{s.StudentId}/status", new UpdateStudentStatusRequest(RecordStatus.Active), Json.Options)).EnsureSuccessStatusCode();
        var detail = await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{s.StudentId}"));
        Assert.Equal(RecordStatus.Active, detail.Status);
        Assert.Null(detail.ArchivedAt);
    }

    [Fact]
    public async Task AStudentWithHistory_CannotBeDeleted_AndTheRefusalSaysWhy()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        var check = await Ok<DeletionCheckDto>(await admin.GetAsync($"/api/students/{child.StudentId}/deletion-check"));
        Assert.False(check.CanDelete);
        Assert.Contains(check.Blockers, b => b.Contains("attendance"));
        Assert.Contains(check.Blockers, b => b.Contains("journey event"));
        Assert.Contains(check.Blockers, b => b.Contains("RFID card"));

        var refused = await Delete(admin, $"/api/students/{child.StudentId}", "Created by mistake in testing.");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Json.Options);
        Assert.Equal("HasHistory", body.GetProperty("code").GetString());

        await using var db = _api.NewDb();
        Assert.True(await db.Students.AnyAsync(s => s.StudentId == child.StudentId));
        Assert.True(await db.AttendanceRecords.AnyAsync(a => a.StudentId == child.StudentId));
    }

    [Fact]
    public async Task AStudentWithoutHistory_IsDeleted_WithLinksAndEnrollmentRecord_ButGuardiansAreKept()
    {
        var admin = await _api.AdminClientAsync();
        var enrolled = await Ok<EnrollmentResultDto>(await admin.PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(
            Guid.NewGuid().ToString("N"), new EnrollmentStudentDto(Unique("Mistake "), new DateTime(2016, 1, 1), "1", null, Unique("DEL"), null, null),
            [new EnrollmentGuardianDto(null, Unique("Keep Parent "), $"{Guid.NewGuid():N}@life.test", Phone(), null, true, "Mother", true)], null, null), Json.Options));
        var guardianId = enrolled.Guardians[0].GuardianId;

        var check = await Ok<DeletionCheckDto>(await admin.GetAsync($"/api/students/{enrolled.StudentId}/deletion-check"));
        Assert.True(check.CanDelete);
        Assert.Contains(check.AlsoRemoved, a => a.Contains("guardian link"));
        Assert.Contains(check.AlsoRemoved, a => a.Contains("enrollment record"));

        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(admin, $"/api/students/{enrolled.StudentId}", "oops")).StatusCode);
        await Ok<LifecycleResultDto>(await Delete(admin, $"/api/students/{enrolled.StudentId}", "Created twice by mistake during enrollment."));

        await using var db = _api.NewDb();
        Assert.False(await db.Students.AnyAsync(s => s.StudentId == enrolled.StudentId));
        Assert.False(await db.StudentGuardians.AnyAsync(sg => sg.StudentId == enrolled.StudentId));
        Assert.False(await db.EnrollmentRequests.AnyAsync(r => r.StudentId == enrolled.StudentId));
        Assert.True(await db.Guardians.AnyAsync(g => g.GuardianId == guardianId));
        var entry = await db.AuditEntries.SingleAsync(e => e.Action == "StudentDeleted" && e.StudentId == enrolled.StudentId);
        Assert.Equal("Created twice by mistake during enrollment.", entry.Reason);
        Assert.Contains("Mistake", entry.Details);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/students/{enrolled.StudentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(admin, $"/api/students/{enrolled.StudentId}", "Created twice by mistake during enrollment.")).StatusCode);
    }

    // ------------------------------------------------------------------ guardians

    [Fact]
    public async Task ArchivingAGuardian_SignsThemOut_BlocksSignIn_WarnsAboutSolePickup_AndRestoreReenables()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await using (var db = _api.NewDb())
        {
            var g = await db.Guardians.SingleAsync(x => x.GuardianId == child.GuardianId);
            g.FcmToken = "fcm-device-token";
            db.GuardianTrustedDevices.Add(new GuardianTrustedDevice { GuardianId = g.GuardianId, TokenHash = new string('a', 64), Label = "Phone", CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30) });
            await db.SaveChangesAsync();
        }

        var check = await Ok<DeletionCheckDto>(await admin.GetAsync($"/api/guardians/{child.GuardianId}/deletion-check"));
        Assert.Contains(check.Warnings, w => w.Contains("only active guardian"));
        var archived = await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/archive", new ArchiveRecordRequest("Custody arrangement changed."), Json.Options));
        Assert.Contains(archived.Effects, e => e.Contains("No active guardian"));

        await using (var db = _api.NewDb())
        {
            var g = await db.Guardians.SingleAsync(x => x.GuardianId == child.GuardianId);
            Assert.Equal(RecordStatus.Inactive, g.Status);
            Assert.Null(g.FcmToken);
            Assert.All(await db.GuardianTrustedDevices.Where(d => d.GuardianId == g.GuardianId).ToListAsync(), d => Assert.NotNull(d.RevokedAt));
            Assert.True(await db.StudentGuardians.AnyAsync(sg => sg.GuardianId == g.GuardianId)); // links kept
        }
        var detail = await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{child.GuardianId}"));
        Assert.Equal("Custody arrangement changed.", detail.ArchiveReason);

        await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/restore", new RestoreRecordRequest(null), Json.Options));
        var restored = await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{child.GuardianId}"));
        Assert.Equal(RecordStatus.Active, restored.Status);
        Assert.Null(restored.ArchivedAt);
    }

    [Fact]
    public async Task ArchivedGuardian_CannotSignIn_UntilRestored()
    {
        var admin = await _api.AdminClientAsync();
        var email = $"{Guid.NewGuid():N}@life.test";
        var phone = Phone();
        var guardianId = await NewGuardianAsync(admin, email, phone);
        var password = _api.Email.PasswordSentTo(email);
        var anonymous = _api.CreateClient();

        await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/guardians/{guardianId}/archive", new ArchiveRecordRequest("Duplicate account, see the other record."), Json.Options));
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/guardian/login", new GuardianLoginRequest(phone, password), Json.Options)).IsSuccessStatusCode);

        await Ok<LifecycleResultDto>(await admin.PostAsJsonAsync($"/api/guardians/{guardianId}/restore", new RestoreRecordRequest("Confirmed as the correct account."), Json.Options));
        Assert.True((await anonymous.PostAsJsonAsync("/api/auth/guardian/login", new GuardianLoginRequest(phone, password), Json.Options)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task GuardianDeletion_IsRefusedWhileLinkedOrWithHistory_AndRemovesFaceDataOtherwise()
    {
        var admin = await _api.AdminClientAsync();
        var student = await NewStudentAsync(admin);
        var email = $"{Guid.NewGuid():N}@life.test";
        var guardianId = await NewGuardianAsync(admin, email);
        (await admin.PostAsJsonAsync($"/api/students/{student.StudentId}/guardians", new AssignGuardianRequest(guardianId, "Uncle", true), Json.Options)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/guardians/{guardianId}/face-registration", new RegisterFaceRequest("aW1hZ2U="), Json.Options)).EnsureSuccessStatusCode();

        var linked = await Delete(admin, $"/api/guardians/{guardianId}", "Entered with the wrong email address.");
        Assert.Equal(HttpStatusCode.Conflict, linked.StatusCode);
        Assert.Contains("unlink", await linked.MessageAsync());

        (await admin.DeleteAsync($"/api/students/{student.StudentId}/guardians/{guardianId}")).EnsureSuccessStatusCode();
        var check = await Ok<DeletionCheckDto>(await admin.GetAsync($"/api/guardians/{guardianId}/deletion-check"));
        Assert.True(check.CanDelete);
        Assert.Contains(check.AlsoRemoved, a => a.Contains("face data"));
        await Ok<LifecycleResultDto>(await Delete(admin, $"/api/guardians/{guardianId}", "Entered with the wrong email address."));

        await using var db = _api.NewDb();
        Assert.False(await db.Guardians.AnyAsync(g => g.GuardianId == guardianId));
        Assert.False(await db.GuardianFaceData.AnyAsync(f => f.GuardianId == guardianId));
        Assert.True(await db.Students.AnyAsync(s => s.StudentId == student.StudentId));
        var entry = await db.AuditEntries.SingleAsync(e => e.Action == "GuardianDeleted" && e.GuardianId == guardianId);
        Assert.DoesNotContain(email, entry.Details); // masked only

        // A guardian with pickup history is never deleted.
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        await admin.VerifyFaceAsync(_api, child);
        (await admin.DeleteAsync($"/api/students/{child.StudentId}/guardians/{child.GuardianId}")).EnsureSuccessStatusCode();
        var history = await Delete(admin, $"/api/guardians/{child.GuardianId}", "Trying to remove a guardian with history.");
        Assert.Equal(HttpStatusCode.Conflict, history.StatusCode);
        Assert.Contains("pickup verification", await history.MessageAsync());
    }

    [Fact]
    public async Task LifecycleEndpoints_AreAdminOnly()
    {
        var anonymous = _api.CreateClient();
        var guardian = _api.GuardianClient(1);
        var child = await _api.NewChildAsync(withFace: false);
        foreach (var client in new[] { anonymous, guardian })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await client.PostAsJsonAsync($"/api/students/{child.StudentId}/archive", new ArchiveRecordRequest(Reason), Json.Options)).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync($"/api/students/{child.StudentId}/restore", new RestoreRecordRequest(null), Json.Options)).StatusCode);
            Assert.Equal(expected, (await Delete(client, $"/api/students/{child.StudentId}", Reason)).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"/api/students/{child.StudentId}/deletion-check")).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/students/{child.StudentId}/record",
                new UpdateStudentRecordRequest("X", new DateTime(2015, 1, 1), null, null, null, null), Json.Options)).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/archive", new ArchiveRecordRequest(Reason), Json.Options)).StatusCode);
            Assert.Equal(expected, (await Delete(client, $"/api/guardians/{child.GuardianId}", Reason)).StatusCode);
        }
        await using var db = _api.NewDb();
        Assert.Null((await db.Students.SingleAsync(s => s.StudentId == child.StudentId)).ArchivedAt);
    }
}
