using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Phase 2 against the real database: address editing for students and guardians, the general audit trail (who, what,
/// which record, when), its read API and filters, its access rules, and that no credential or biometric value ever
/// reaches it.
/// </summary>
public class AuditAndAddressTests : IClassFixture<EnrollmentApiFactory>
{
    private readonly EnrollmentApiFactory _api;
    public AuditAndAddressTests(EnrollmentApiFactory api) => _api = api;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string Phone() => "96" + Random.Shared.Next(10000000, 99999999);

    private static readonly AddressDto Home = new("221B Residency Road", "Block C", "Shanthala Nagar", "Opp. City Park",
        "Bengaluru", "Bengaluru Urban", "Karnataka", "560025", "India");

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private async Task<List<AuditEntry>> AuditAsync(Func<AuditEntry, bool> where)
    {
        await using var db = _api.NewDb();
        return (await db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToListAsync()).Where(where).ToList();
    }

    private async Task<int> AdminIdAsync()
    {
        await using var db = _api.NewDb();
        return (await db.Admins.SingleAsync(a => a.Email == ApiFactory.AdminEmail)).AdminId;
    }

    private async Task<StudentDetailDto> NewStudentAsync(HttpClient admin) =>
        await Ok<StudentDetailDto>(await admin.PostAsJsonAsync("/api/students", new CreateStudentRequest(Unique("Audit Kid "), new DateTime(2016, 3, 3), "2B"), Json.Options));

    private async Task<int> NewGuardianAsync(HttpClient admin) =>
        (await Ok<CreateGuardianResultDto>(await admin.PostAsJsonAsync("/api/guardians",
            new CreateGuardianRequest(Unique("Audit Parent "), $"{Guid.NewGuid():N}@audit.test", Phone()), Json.Options))).GuardianId;

    // ------------------------------------------------------------------ addresses

    [Fact]
    public async Task StudentAddress_CanBeSet_Changed_AndCleared_AndIsAudited()
    {
        var admin = await _api.AdminClientAsync();
        var student = await NewStudentAsync(admin);

        var set = await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/address", Home, Json.Options));
        Assert.Equal(Home, set.Address);
        Assert.Equal(Home, (await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{student.StudentId}"))).Address);

        var moved = Home with { Line1 = "  14 MG Road  ", Line2 = "", PostalCode = "560001" };
        var changed = await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/address", moved, Json.Options));
        Assert.Equal("14 MG Road", changed.Address!.Line1);
        Assert.Null(changed.Address.Line2);

        var cleared = await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/address",
            new AddressDto(null, null, null, null, null, null, null, null, null), Json.Options));
        Assert.Null(cleared.Address);

        var entries = await AuditAsync(a => a.Action == "StudentAddressUpdated" && a.StudentId == student.StudentId);
        Assert.Equal(3, entries.Count);
        Assert.Equal("{\"changedParts\":[\"line1\",\"line2\",\"postalCode\"]}", entries[1].Details);
        // Only the names of changed parts are listed: the address itself is not copied into the trail.
        Assert.All(entries, e => Assert.DoesNotContain("Residency Road", e.Details + e.Summary));
        Assert.All(entries, e => Assert.DoesNotContain("MG Road", e.Details + e.Summary));
        Assert.Contains("cleared", entries[2].Summary);

        // Saving the same address again changes nothing and records nothing.
        await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/address",
            new AddressDto(null, null, null, null, null, null, null, null, null), Json.Options));
        Assert.Equal(3, (await AuditAsync(a => a.Action == "StudentAddressUpdated" && a.StudentId == student.StudentId)).Count);
    }

    [Fact]
    public async Task GuardianAddress_CanBeSet_AndIsShownOnTheProfile()
    {
        var admin = await _api.AdminClientAsync();
        var guardianId = await NewGuardianAsync(admin);

        (await admin.PutAsJsonAsync($"/api/guardians/{guardianId}/address", Home, Json.Options)).EnsureSuccessStatusCode();
        Assert.Equal(Home, (await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{guardianId}"))).Address);
        var entry = Assert.Single(await AuditAsync(a => a.Action == "GuardianAddressUpdated" && a.GuardianId == guardianId));
        Assert.Equal("Admin", entry.ActorType);
    }

    [Fact]
    public async Task AddressEdits_AreValidated_AndUnknownRecordsAre404()
    {
        var admin = await _api.AdminClientAsync();
        var student = await NewStudentAsync(admin);
        var badPin = Home with { PostalCode = "<script>" };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/address", badPin, Json.Options)).StatusCode);
        var tooLong = Home with { City = new string('x', 101) };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/address", tooLong, Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/students/999999/address", Home, Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/guardians/999999/address", Home, Json.Options)).StatusCode);
        Assert.Null((await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{student.StudentId}"))).Address);
    }

    [Fact]
    public async Task EnrollmentAddresses_ArePreserved_AndEditable()
    {
        var admin = await _api.AdminClientAsync();
        var enrolled = await Ok<EnrollmentResultDto>(await admin.PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(
            Guid.NewGuid().ToString("N"), new EnrollmentStudentDto(Unique("Addr Kid "), new DateTime(2015, 9, 9), "5", null, null, null, Home),
            [new EnrollmentGuardianDto(null, Unique("Addr Parent "), $"{Guid.NewGuid():N}@audit.test", Phone(), null, true, "Father", true)], null, null), Json.Options));
        var guardianId = enrolled.Guardians[0].GuardianId;
        Assert.Equal(Home, (await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{guardianId}"))).Address);

        // Editing the student's address does not touch the guardian's copy.
        await Ok<StudentDetailDto>(await admin.PutAsJsonAsync($"/api/students/{enrolled.StudentId}/address", Home with { City = "Mysuru" }, Json.Options));
        Assert.Equal("Bengaluru", (await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{guardianId}"))).Address!.City);
    }

    // ------------------------------------------------------------------ audit creation

    [Fact]
    public async Task StudentLifecycle_IsAudited_WithActorRecordAndTime()
    {
        var admin = await _api.AdminClientAsync();
        var adminId = await AdminIdAsync();
        var student = await NewStudentAsync(admin);
        (await admin.PutAsJsonAsync($"/api/students/{student.StudentId}", new UpdateStudentRequest(student.FullName, student.DateOfBirth, "3A"), Json.Options)).EnsureSuccessStatusCode();
        (await admin.PatchAsJsonAsync($"/api/students/{student.StudentId}/status", new UpdateStudentStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();

        var entries = await AuditAsync(a => a.StudentId == student.StudentId);
        Assert.Equal(new[] { "StudentCreated", "StudentUpdated", "StudentDeactivated" }, entries.Select(e => e.Action));
        Assert.All(entries, e =>
        {
            Assert.Equal("Admin", e.ActorType);
            Assert.Equal(adminId, e.ActorId);
            Assert.Equal("Integration Test Admin", e.ActorName);
            Assert.Equal("Student", e.EntityType);
            Assert.Equal(student.StudentId, e.EntityId);
            Assert.Equal(_api.Time.UtcNow, e.OccurredAt);
        });
        Assert.Contains("\"grade\":{\"from\":\"2B\",\"to\":\"3A\"}", entries[1].Details);
    }

    [Fact]
    public async Task GuardianCreation_Links_AndPasswordReset_AreAudited_WithoutAnyPassword()
    {
        var admin = await _api.AdminClientAsync();
        var student = await NewStudentAsync(admin);
        var guardianId = await NewGuardianAsync(admin);
        (await admin.PostAsJsonAsync($"/api/students/{student.StudentId}/guardians", new AssignGuardianRequest(guardianId, "Aunt", false), Json.Options)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/students/{student.StudentId}/guardians/{guardianId}", new UpdateStudentGuardianRequest("Aunt", true), Json.Options)).EnsureSuccessStatusCode();
        const string issued = "Temp-Pass-9081";
        (await admin.PatchAsJsonAsync($"/api/guardians/{guardianId}/credentials", new AdminUpdateGuardianCredentialsRequest(issued), Json.Options)).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/students/{student.StudentId}/guardians/{guardianId}")).EnsureSuccessStatusCode();

        var entries = await AuditAsync(a => a.GuardianId == guardianId);
        Assert.Equal(new[] { "GuardianCreated", "GuardianLinked", "GuardianLinkUpdated", "GuardianPasswordReset", "GuardianUnlinked" },
            entries.Select(e => e.Action));
        Assert.Contains("\"isAuthorizedForPickup\":{\"from\":false,\"to\":true}", entries[2].Details);
        Assert.All(entries.Where(e => e.Action.StartsWith("GuardianLink") || e.Action == "GuardianUnlinked"), e => Assert.Equal(student.StudentId, e.StudentId));

        await using var db = _api.NewDb();
        var hash = (await db.Guardians.SingleAsync(g => g.GuardianId == guardianId)).PasswordHash;
        var initial = _api.Email.PasswordSentTo((await db.Guardians.SingleAsync(g => g.GuardianId == guardianId)).Email);
        Assert.All(entries, e =>
        {
            var text = e.Summary + e.Details + e.Reason;
            Assert.DoesNotContain(issued, text);
            Assert.DoesNotContain(initial, text);
            Assert.DoesNotContain(hash, text);
        });
    }

    [Fact]
    public async Task RfidAssignment_AndStatusChanges_AreAudited_WithMaskedCardsOnly()
    {
        var admin = await _api.AdminClientAsync();
        var student = await NewStudentAsync(admin);
        var uid = "AUD" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        (await admin.PostAsJsonAsync("/api/rfid/cards", new AssignRfidRequest(uid, student.StudentId), Json.Options)).EnsureSuccessStatusCode();
        await using var db = _api.NewDb();
        var cardId = (await db.RfidCards.SingleAsync(c => c.CardUid == uid)).RfidCardId;
        (await admin.PatchAsJsonAsync($"/api/rfid/cards/{cardId}/status", new UpdateRfidStatusRequest(RfidCardStatus.Lost), Json.Options)).EnsureSuccessStatusCode();

        var entries = await AuditAsync(a => a.EntityType == "RfidCard" && a.EntityId == cardId);
        Assert.Equal(new[] { "RfidCardAssigned", "RfidCardStatusChanged" }, entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.Equal(student.StudentId, e.StudentId));
        Assert.All(entries, e => Assert.DoesNotContain(uid, e.Summary + e.Details));
        Assert.Contains(RfidMasking.Mask(uid), entries[0].Summary);
    }

    [Fact]
    public async Task Enrollment_IsAuditedInItsOwnTransaction()
    {
        var admin = await _api.AdminClientAsync();
        var first = new EnrollmentStudentDto(Unique("Enrol Audit "), new DateTime(2016, 1, 1), "1", null, null, null, null);
        await Ok<EnrollmentResultDto>(await admin.PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(Guid.NewGuid().ToString("N"), first,
            [new EnrollmentGuardianDto(null, Unique("P "), $"{Guid.NewGuid():N}@audit.test", Phone(), null, true, "Mother", true)], null, null), Json.Options));
        const string reason = "Twin of the first student; separate admission.";
        var uid = "ENA" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var result = await Ok<EnrollmentResultDto>(await admin.PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(Guid.NewGuid().ToString("N"), first,
            [new EnrollmentGuardianDto(null, Unique("P "), $"{Guid.NewGuid():N}@audit.test", Phone(), null, true, "Mother", true)], uid, null,
            DuplicateStudentOverrideReason: reason), Json.Options));

        var entries = await AuditAsync(a => a.StudentId == result.StudentId);
        Assert.Equal(new[] { "StudentEnrolled", "GuardianCreated", "GuardianLinked", "RfidCardAssigned" }, entries.Select(e => e.Action));
        var enrolled = entries[0];
        Assert.Equal(reason, enrolled.Reason);
        Assert.Contains("\"duplicateOverride\":true", enrolled.Details);
        Assert.Equal(result.Guardians[0].GuardianId, entries[1].GuardianId);
        Assert.All(entries, e => Assert.DoesNotContain(uid, e.Summary + e.Details));

        // A refused enrollment (bus out of service) leaves no audit trace: it rolled back with everything else.
        var route = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Unique("R "), null), Json.Options));
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("AB"), 30, null, route.RouteId), Json.Options));
        (await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        var before = (await AuditAsync(_ => true)).Count;
        var refused = await admin.PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(Guid.NewGuid().ToString("N"),
            new EnrollmentStudentDto(Unique("Refused "), new DateTime(2016, 2, 2), "1", null, null, null, null),
            [new EnrollmentGuardianDto(null, Unique("P "), $"{Guid.NewGuid():N}@audit.test", Phone(), null, true, null, true)], null,
            new EnrollmentTransportDto(bus.BusId, null)), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(before, (await AuditAsync(_ => true)).Count);
    }

    [Fact]
    public async Task AdminSignIns_AreAudited_WithMaskedEmailAndNoPassword()
    {
        var anonymous = _api.CreateClient();
        const string wrong = "Definitely-Wrong-Password-1";
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/admin/login", new LoginRequest(ApiFactory.AdminEmail, wrong), Json.Options)).IsSuccessStatusCode);
        await _api.AdminClientAsync();

        var failed = (await AuditAsync(a => a.Action == "AdminSignInFailed")).Last();
        Assert.Equal("Anonymous", failed.ActorType);
        Assert.Equal(await AdminIdAsync(), failed.EntityId);
        Assert.DoesNotContain(ApiFactory.AdminEmail, failed.Summary + failed.Details);
        Assert.DoesNotContain(wrong, failed.Summary + failed.Details);
        var signedIn = (await AuditAsync(a => a.Action == "AdminSignedIn")).Last();
        Assert.Equal(("Admin", await AdminIdAsync(), "Integration Test Admin"), (signedIn.ActorType, signedIn.ActorId, signedIn.ActorName));
    }

    [Fact]
    public async Task FaceRegistration_DeviceKeys_PickupDecisions_AndTransport_AreAudited_WithoutSecrets()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        (await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration", new RegisterFaceRequest("aW1hZ2U="), Json.Options)).EnsureSuccessStatusCode();
        var face = Assert.Single(await AuditAsync(a => a.Action == "GuardianFaceRegistered" && a.GuardianId == child.GuardianId));
        Assert.DoesNotContain("0.1,0.2,0.3", face.Details + face.Summary);
        Assert.DoesNotContain("aW1hZ2U=", face.Details + face.Summary);

        var key = await Ok<DeviceCredentialDto>(await admin.PostAsync($"/api/rfid/devices/{child.DeviceId}/credentials", null));
        var issued = (await AuditAsync(a => a.EntityType == "RfidDevice" && a.EntityId == child.DeviceId)).Last();
        Assert.StartsWith("RfidDeviceKey", issued.Action);
        Assert.DoesNotContain(key.ApiKey, issued.Summary + issued.Details);

        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var verification = await admin.VerifyFaceAsync(_api, child);
        (await admin.ApproveAsync(verification)).EnsureSuccessStatusCode();
        var approved = Assert.Single(await AuditAsync(a => a.Action == "PickupApproved" && a.EntityId == verification));
        Assert.Equal((child.StudentId, child.GuardianId, "Admin"), (approved.StudentId!.Value, approved.GuardianId!.Value, approved.ActorType));

        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Unique("Driver "), null, null), Json.Options));
        Assert.Single(await AuditAsync(a => a.Action == "DriverCreated" && a.EntityId == driver.DriverId));
    }

    // ------------------------------------------------------------------ reading the trail

    [Fact]
    public async Task AuditApi_FiltersAndPages()
    {
        var admin = await _api.AdminClientAsync();
        var student = await NewStudentAsync(admin);
        for (var i = 0; i < 3; i++)
            (await admin.PutAsJsonAsync($"/api/students/{student.StudentId}", new UpdateStudentRequest(student.FullName, student.DateOfBirth, $"G{i}"), Json.Options)).EnsureSuccessStatusCode();

        var all = await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}"));
        Assert.Equal(4, all.Total);
        Assert.Equal("StudentUpdated", all.Items[0].Action); // newest first

        var page2 = await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}&pageSize=3&page=2"));
        Assert.Equal((4, 1, "StudentCreated"), (page2.Total, page2.Items.Count, page2.Items[0].Action));

        Assert.Equal(1, (await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?entityType=Student&entityId={student.StudentId}&action=StudentCreated"))).Total);
        Assert.Equal(4, (await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}&actorType=Admin"))).Total);
        Assert.Equal(0, (await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}&actorType=Guardian"))).Total);
        Assert.Equal(1, (await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}&search={Uri.EscapeDataString(student.FullName.ToUpperInvariant() + " created")}"))).Total);

        var today = _api.Clock.Today;
        Assert.Equal(4, (await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"))).Total);
        Assert.Equal(0, (await Ok<AuditPageDto>(await admin.GetAsync($"/api/audit?studentId={student.StudentId}&from={today.AddDays(1):yyyy-MM-dd}"))).Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/audit?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}")).StatusCode);
        Assert.Equal(200, (await Ok<AuditPageDto>(await admin.GetAsync("/api/audit?pageSize=100000"))).PageSize);
    }

    [Fact]
    public async Task AuditAndAddressEndpoints_RejectEveryoneButAdmins()
    {
        var anonymous = _api.CreateClient();
        var guardian = _api.GuardianClient(1);
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        var key = await Ok<DeviceCredentialDto>(await admin.PostAsync($"/api/rfid/devices/{child.DeviceId}/credentials", null));
        var device = _api.CreateClient();
        device.DefaultRequestHeaders.Add("X-Device-Key", key.ApiKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/audit")).StatusCode);
        // A scanner's key is not accepted on admin routes at all.
        Assert.True((await device.GetAsync("/api/audit")).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        foreach (var client in new[] { anonymous, guardian, device })
        {
            Assert.True((await client.PutAsJsonAsync($"/api/students/{child.StudentId}/address", Home, Json.Options)).StatusCode
                is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
            Assert.True((await client.PutAsJsonAsync($"/api/guardians/{child.GuardianId}/address", Home, Json.Options)).StatusCode
                is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }
        // There is no way to change or delete the trail.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.DeleteAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.PutAsJsonAsync("/api/audit", new { }, Json.Options)).StatusCode);

        await using var db = _api.NewDb();
        Assert.Null((await db.Students.SingleAsync(s => s.StudentId == child.StudentId)).Address);
    }

    [Fact]
    public async Task NoCredentialOrBiometricValueAppearsAnywhereInTheTrail()
    {
        var admin = await _api.AdminClientAsync();
        await NewGuardianAsync(admin);

        await using var db = _api.NewDb();
        var secrets = new List<string>();
        secrets.AddRange(await db.Guardians.Select(g => g.PasswordHash).ToListAsync());
        secrets.AddRange(await db.Admins.Select(a => a.PasswordHash).ToListAsync());
        secrets.AddRange((await db.GuardianFaceData.Select(f => f.EmbeddingJson).ToListAsync()));
        secrets.AddRange(await db.RfidCards.Select(c => c.CardUid).ToListAsync());
        secrets.AddRange(_api.Email.Sent.Select(s => System.Text.RegularExpressions.Regex.Match(s.Message.TextBody, @"Initial password:\s*(\S+)").Groups[1].Value));
        secrets.Add(_api.AdminPassword);

        var trail = await db.AuditEntries.AsNoTracking().ToListAsync();
        Assert.NotEmpty(trail);
        foreach (var e in trail)
        {
            var text = $"{e.Summary}|{e.Details}|{e.Reason}|{e.ActorName}";
            foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
                Assert.False(text.Contains(secret, StringComparison.Ordinal), $"Audit entry {e.Id} ({e.Action}) contains a secret value.");
            Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
