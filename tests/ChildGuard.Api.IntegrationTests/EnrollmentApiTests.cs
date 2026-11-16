using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Captures credential emails and records whether the guardian was already committed when each was sent.</summary>
public sealed class CommitCheckingEmailSender : IEmailSender
{
    private readonly Func<string, Task<bool>> _isCommitted;
    public CommitCheckingEmailSender(Func<string, Task<bool>> isCommitted) => _isCommitted = isCommitted;

    public List<(EmailMessage Message, bool GuardianCommitted)> Sent { get; } = new();
    public string? FailWith { get; set; }
    public bool ThrowNext { get; set; }

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (ThrowNext) throw new HttpRequestException("provider unreachable");
        var committed = await _isCommitted(message.ToEmail);
        lock (Sent) Sent.Add((message, committed));
        return FailWith is null ? Result.Success() : Result.Failure(FailWith);
    }

    public string PasswordSentTo(string email) =>
        Regex.Match(Sent.Last(s => s.Message.ToEmail == email).Message.TextBody, @"Initial password:\s*(\S+)").Groups[1].Value;
}

public class EnrollmentApiFactory : ApiFactory
{
    public CommitCheckingEmailSender Email { get; }

    public EnrollmentApiFactory()
    {
        // A separate connection sees only committed rows, so "found" proves the email went out after the commit.
        Email = new CommitCheckingEmailSender(async email =>
        {
            await using var db = NewDb();
            return await db.Guardians.AnyAsync(g => g.Email == email);
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }
}

/// <summary>
/// The unified enrollment endpoint against the real database: one transaction for student, guardians,
/// RFID card and transport; idempotent resubmission; duplicate prevention; credentials emailed only after commit.
/// </summary>
public class EnrollmentApiTests : IClassFixture<EnrollmentApiFactory>
{
    private readonly EnrollmentApiFactory _api;
    public EnrollmentApiTests(EnrollmentApiFactory api) => _api = api;

    private static int _seq;
    private static string Unique(string prefix) => $"{prefix}{Interlocked.Increment(ref _seq)}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 12, 40)];
    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Phone() => "98" + Random.Shared.Next(10000000, 99999999);
    private static string Uid() => "EN" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static readonly AddressDto Home = new("12 Lake View Road", "Flat 4B", "Bandra West", "Near St. Andrew's Church",
        "Mumbai", "Mumbai Suburban", "Maharashtra", "400050", "India");

    private static EnrollmentStudentDto NewStudent(string? admission = null, AddressDto? address = null) =>
        new(Unique("Pupil "), new DateTime(2016, 4, 12), "4", "B", admission, Gender.Female, address);

    private static EnrollmentGuardianDto NewGuardian(bool sameAddress = false, string? email = null, string? phone = null) =>
        new(null, Unique("Parent "), email ?? $"{Guid.NewGuid():N}@enrol.test", phone ?? Phone(), null, sameAddress, "Mother", true);

    private static EnrollmentGuardianDto Existing(int id, string relationship = "Father") =>
        new(id, null, null, null, null, false, relationship, true);

    private static Task<HttpResponseMessage> Enroll(HttpClient admin, EnrollStudentRequest request) =>
        admin.PostAsJsonAsync("/api/enrollments", request, Json.Options);

    private static async Task<T> Read<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private async Task<int> ExistingGuardianAsync(string? email = null, string? phone = null)
    {
        await using var db = _api.NewDb();
        var g = new Guardian { FullName = Unique("Existing "), Email = email ?? $"{Guid.NewGuid():N}@existing.test", Phone = phone ?? Phone(), PasswordHash = "x" };
        db.Guardians.Add(g);
        await db.SaveChangesAsync();
        return g.GuardianId;
    }

    private async Task<int> StudentCountAsync()
    {
        await using var db = _api.NewDb();
        return await db.Students.CountAsync();
    }

    // ---------------------------------------------------------------- 1. new student + new guardian + new card

    [Fact]
    public async Task NewStudent_NewGuardian_NewCard_AreCreatedAndLinked_AndCredentialsEmailedAfterCommit()
    {
        var admin = await _api.AdminClientAsync();
        var guardian = NewGuardian();
        var uid = Uid();
        var response = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(Unique("ADM")), [guardian], uid, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var result = (await response.Content.ReadFromJsonAsync<EnrollmentResultDto>(Json.Options))!;
        Assert.False(result.AlreadyEnrolled);
        Assert.False(result.RfidPending);
        Assert.Equal(RfidMasking.Mask(uid), result.MaskedCardUid);
        var enrolled = Assert.Single(result.Guardians);
        Assert.True(enrolled.Created);
        Assert.True(enrolled.CredentialEmailSent);

        // Emailed to the guardian, after the commit, and never returned to the admin.
        var sent = _api.Email.Sent.Single(s => s.Message.ToEmail == guardian.Email);
        Assert.True(sent.GuardianCommitted);
        var password = _api.Email.PasswordSentTo(guardian.Email!);
        Assert.False(string.IsNullOrEmpty(password));
        Assert.DoesNotContain(password, raw);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);

        await using var db = _api.NewDb();
        var g = await db.Guardians.SingleAsync(x => x.GuardianId == enrolled.GuardianId);
        Assert.True(g.PasswordResetRequired);
        Assert.NotEqual(password, g.PasswordHash);
        var link = await db.StudentGuardians.SingleAsync(x => x.StudentId == result.StudentId);
        Assert.Equal(("Mother", true), (link.Relationship, link.IsAuthorizedForPickup));
        var card = await db.RfidCards.SingleAsync(c => c.CardUid == uid);
        Assert.Equal(result.StudentId, card.StudentId);
        Assert.Equal(RfidCardStatus.Active, card.Status);
        var history = await db.RfidCardAssignments.SingleAsync(h => h.RfidCardId == card.RfidCardId);
        Assert.Equal(result.StudentId, history.StudentId);
        Assert.False(history.IsMigratedSnapshot);
        Assert.NotNull(history.AssignedByAdminId);

        // The emailed credentials are real: the guardian's first sign-in reaches the OTP step.
        var login = await _api.CreateClient().PostAsJsonAsync("/api/auth/guardian/login",
            new GuardianLoginRequest(guardian.ContactNumber!, password), Json.Options);
        var challenge = await Read<GuardianOtpChallengeDto>(login);
        Assert.NotEqual(Guid.Empty, challenge.ChallengeToken);
        Assert.Contains(_api.Otp.Sent, s => s.GuardianId == enrolled.GuardianId);
    }

    [Fact]
    public async Task FailedCredentialEmail_IsReported_AndTheEnrollmentStands()
    {
        var admin = await _api.AdminClientAsync();
        _api.Email.FailWith = "Mailbox unavailable";
        try
        {
            var result = await Read<EnrollmentResultDto>(await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], null, null)));
            var g = Assert.Single(result.Guardians);
            Assert.False(g.CredentialEmailSent);
            Assert.Equal("Mailbox unavailable", g.EmailError);
            await using var db = _api.NewDb();
            Assert.True(await db.Students.AnyAsync(s => s.StudentId == result.StudentId));
        }
        finally { _api.Email.FailWith = null; }
    }

    // ---------------------------------------------------------------- 2/3. existing and multiple guardians

    [Fact]
    public async Task ExistingGuardian_IsLinked_NotDuplicated_AndGetsNoEmail()
    {
        var admin = await _api.AdminClientAsync();
        var existingId = await ExistingGuardianAsync();
        await using (var before = _api.NewDb())
        {
            var guardians = await before.Guardians.CountAsync();
            var emails = _api.Email.Sent.Count;

            var result = await Read<EnrollmentResultDto>(await Enroll(admin,
                new EnrollStudentRequest(Key(), NewStudent(), [Existing(existingId)], null, null)));

            var g = Assert.Single(result.Guardians);
            Assert.Equal(existingId, g.GuardianId);
            Assert.False(g.Created);
            Assert.Null(g.CredentialEmailSent);
            await using var after = _api.NewDb();
            Assert.Equal(guardians, await after.Guardians.CountAsync());
            Assert.Equal(emails, _api.Email.Sent.Count);
            Assert.True(result.RfidPending);
        }
    }

    [Fact]
    public async Task OneGuardianMayBeLinkedToSeveralStudents_AndAStudentMayHaveSeveralGuardians()
    {
        var admin = await _api.AdminClientAsync();
        var shared = await ExistingGuardianAsync();
        var first = await Read<EnrollmentResultDto>(await Enroll(admin,
            new EnrollStudentRequest(Key(), NewStudent(), [Existing(shared), NewGuardian()], null, null)));
        var second = await Read<EnrollmentResultDto>(await Enroll(admin,
            new EnrollStudentRequest(Key(), NewStudent(), [Existing(shared, "Guardian")], null, null)));

        Assert.Equal(2, first.Guardians.Count);
        Assert.Single(second.Guardians);
        await using var db = _api.NewDb();
        Assert.Equal(2, await db.StudentGuardians.CountAsync(sg => sg.GuardianId == shared));
    }

    [Fact]
    public async Task NewGuardianWithARegisteredEmailOrContact_IsRefused_PointingToLinkExisting()
    {
        var admin = await _api.AdminClientAsync();
        var email = $"{Guid.NewGuid():N}@taken.test";
        var phone = Phone();
        await ExistingGuardianAsync(email, phone);
        var count = await StudentCountAsync();

        var byEmail = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian(email: email.ToUpperInvariant())], null, null));
        Assert.Equal(HttpStatusCode.BadRequest, byEmail.StatusCode);
        Assert.Contains("Link existing guardian", await byEmail.MessageAsync());

        var byPhone = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian(phone: phone[..5] + " " + phone[5..])], null, null));
        Assert.Equal(HttpStatusCode.BadRequest, byPhone.StatusCode);

        var twice = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian(phone: "9811112222"), NewGuardian(phone: "98111 12222")], null, null));
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);

        Assert.Equal(count, await StudentCountAsync());
    }

    // ---------------------------------------------------------------- 4. addresses

    [Fact]
    public async Task StudentAndGuardianAddresses_AreStoredAndShownOnTheirProfiles()
    {
        var admin = await _api.AdminClientAsync();
        var sameAsStudent = NewGuardian(sameAddress: true);
        var ownAddress = NewGuardian() with { Address = new AddressDto("7 Hill Rd", null, "Andheri", null, "Mumbai", null, "Maharashtra", "400058", "India") };
        var result = await Read<EnrollmentResultDto>(await Enroll(admin,
            new EnrollStudentRequest(Key(), NewStudent(address: Home), [sameAsStudent, ownAddress], null, null)));

        var student = await Read<StudentDetailDto>(await admin.GetAsync($"/api/students/{result.StudentId}"));
        Assert.Equal(Home, student.Address);
        Assert.Equal("B", student.Section);
        Assert.Equal(Gender.Female, student.Gender);

        var g1 = await Read<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{result.Guardians.Single(g => g.FullName == sameAsStudent.FullName).GuardianId}"));
        Assert.Equal(Home, g1.Address);
        var g2 = await Read<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{result.Guardians.Single(g => g.FullName == ownAddress.FullName).GuardianId}"));
        Assert.Equal("Andheri", g2.Address!.Locality);
        Assert.Null(g2.Address.Line2);
    }

    [Fact]
    public async Task ExistingStudentsWithoutAddress_StillLoad()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        var student = await Read<StudentDetailDto>(await admin.GetAsync($"/api/students/{child.StudentId}"));
        Assert.Null(student.Address);
        Assert.Null(student.AdmissionNumber);
    }

    // ---------------------------------------------------------------- 5. RFID

    [Fact]
    public async Task CardCheck_ReportsNewAvailableAssignedAndBlockedCards()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        string spare = Uid(), lost = Uid();
        await using (var db = _api.NewDb())
        {
            db.RfidCards.AddRange(new RfidCard { CardUid = spare }, new RfidCard { CardUid = lost, Status = RfidCardStatus.Lost });
            await db.SaveChangesAsync();
        }

        async Task<RfidCardCheckDto> Check(string uid) =>
            await Read<RfidCardCheckDto>(await admin.PostAsJsonAsync("/api/rfid/cards/check", new RfidCardCheckRequest(uid), Json.Options));

        var fresh = await Check(Uid());
        Assert.Equal((RfidCardAvailability.New, true), (fresh.Availability, fresh.CanAssign));
        var available = await Check(" " + spare + "\n");
        Assert.Equal((RfidCardAvailability.Available, true), (available.Availability, available.CanAssign));
        var assigned = await Check(child.CardUid);
        Assert.Equal((RfidCardAvailability.AssignedToStudent, false), (assigned.Availability, assigned.CanAssign));
        Assert.Equal(child.StudentId, assigned.StudentId);
        Assert.DoesNotContain(child.CardUid, assigned.MaskedCardUid);
        var blocked = await Check(lost);
        Assert.Equal((RfidCardAvailability.Blocked, false), (blocked.Availability, blocked.CanAssign));

        // A registered spare card is assigned to the enrolled student rather than registered twice.
        var result = await Read<EnrollmentResultDto>(await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], spare, null)));
        await using var after = _api.NewDb();
        Assert.Equal(result.StudentId, (await after.RfidCards.SingleAsync(c => c.CardUid == spare)).StudentId);
    }

    [Fact]
    public async Task CardAlreadyAssigned_OrBlocked_RefusesTheWholeEnrollment()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        var guardian = NewGuardian();
        var count = await StudentCountAsync();

        var taken = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [guardian], child.CardUid, null));
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("already assigned", await taken.MessageAsync());

        Assert.Equal(count, await StudentCountAsync());
        await using var db = _api.NewDb();
        Assert.False(await db.Guardians.AnyAsync(g => g.Email == guardian.Email));
        Assert.Equal(child.StudentId, (await db.RfidCards.SingleAsync(c => c.CardUid == child.CardUid)).StudentId);
    }

    [Fact]
    public async Task ExistingAssignPath_AlsoWritesHistory()
    {
        var admin = await _api.AdminClientAsync();
        int studentId;
        await using (var db = _api.NewDb())
        {
            var s = new Student { FullName = Unique("Legacy "), DateOfBirth = new DateTime(2015, 1, 1) };
            db.Students.Add(s);
            await db.SaveChangesAsync();
            studentId = s.StudentId;
        }
        var uid = Uid();
        (await admin.PostAsJsonAsync("/api/rfid/cards", new AssignRfidRequest(uid, studentId), Json.Options)).EnsureSuccessStatusCode();
        await using var check = _api.NewDb();
        var row = await check.RfidCardAssignments.SingleAsync(h => h.StudentId == studentId);
        Assert.NotNull(row.AssignedAt);
        Assert.False(row.IsMigratedSnapshot);
    }

    // ---------------------------------------------------------------- transport

    [Fact]
    public async Task Transport_IsAssignedThroughTheExistingRules_AndAFailureRollsEverythingBack()
    {
        var admin = await _api.AdminClientAsync();
        var route = await Read<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Unique("Route "), null), Json.Options));
        var bus = await Read<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("EB"), 40, null, route.RouteId), Json.Options));

        var ok = await Read<EnrollmentResultDto>(await Enroll(admin,
            new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], Uid(), new EnrollmentTransportDto(bus.BusId, null))));
        Assert.Equal(bus.BusNumber, ok.BusNumber);
        Assert.Equal(route.RouteName, ok.RouteName);

        (await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        var guardian = NewGuardian();
        var uid = Uid();
        var before = await CountsAsync();
        var refused = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [guardian], uid, new EnrollmentTransportDto(bus.BusId, null)));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("out of service", await refused.MessageAsync());

        // The failure came last, after student, guardian, links, card and history were written: all rolled back.
        Assert.Equal(before, await CountsAsync());
        await using var db = _api.NewDb();
        Assert.False(await db.Guardians.AnyAsync(g => g.Email == guardian.Email));
        Assert.False(await db.RfidCards.AnyAsync(c => c.CardUid == uid));
        Assert.DoesNotContain(_api.Email.Sent, s => s.Message.ToEmail == guardian.Email);
    }

    // ---------------------------------------------------------------- 19. duplicate enrollment

    [Fact]
    public async Task ResubmittingTheSameKey_ReturnsTheFirstEnrollment_AndWritesNothing()
    {
        var admin = await _api.AdminClientAsync();
        var request = new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], Uid(), null);
        var first = await Enroll(admin, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var count = await StudentCountAsync();
        var emails = _api.Email.Sent.Count;

        var again = await Enroll(admin, request);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var replay = await Read<EnrollmentResultDto>(again);
        Assert.True(replay.AlreadyEnrolled);
        Assert.Equal((await Read<EnrollmentResultDto>(first)).StudentId, replay.StudentId);
        Assert.Equal(count, await StudentCountAsync());
        Assert.Equal(emails, _api.Email.Sent.Count);
    }

    [Fact]
    public async Task ConcurrentSubmissionsOfOneKey_CreateExactlyOneStudent()
    {
        var request = new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], Uid(), null);
        var clients = new List<HttpClient>();
        for (var i = 0; i < 5; i++) clients.Add(await _api.AdminClientAsync());

        var responses = await TestData.Simultaneously(5, i => Enroll(clients[i], request));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        var ids = new HashSet<int>();
        foreach (var r in responses) ids.Add((await Read<EnrollmentResultDto>(r)).StudentId);
        Assert.Single(ids);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.Students.CountAsync(s => s.FullName == request.Student.FullName));
        Assert.Equal(1, await db.Guardians.CountAsync(g => g.Email == request.Guardians[0].Email));
        Assert.Single(_api.Email.Sent, s => s.Message.ToEmail == request.Guardians[0].Email);
    }

    [Fact]
    public async Task AdmissionNumber_IsAssignedAutomatically_WhenNoneIsGiven()
    {
        var admin = await _api.AdminClientAsync();
        var enrolled = await Read<EnrollmentResultDto>(await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], null, null)));
        var student = await Read<StudentDetailDto>(await admin.GetAsync($"/api/students/{enrolled.StudentId}"));
        // The test clock puts the school's today in 2026.
        Assert.Equal($"ADM2026{enrolled.StudentId:D5}", student.AdmissionNumber);

        // The plain create endpoint assigns one the same way.
        var created = await Read<StudentDetailDto>(await admin.PostAsJsonAsync("/api/students",
            new CreateStudentRequest(Unique("Auto "), new DateTime(2016, 1, 1), "2"), Json.Options));
        Assert.Equal($"ADM2026{created.StudentId:D5}", created.AdmissionNumber);

        // A number given explicitly (API clients) is still kept as given.
        var given = Unique("GIVEN");
        var manual = await Read<EnrollmentResultDto>(await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(given), [NewGuardian()], null, null)));
        Assert.Equal(given, (await Read<StudentDetailDto>(await admin.GetAsync($"/api/students/{manual.StudentId}"))).AdmissionNumber);
    }

    [Fact]
    public async Task DuplicateAdmissionNumber_IsAlwaysRefused()
    {
        var admin = await _api.AdminClientAsync();
        var admission = Unique("ADM");
        (await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(admission), [NewGuardian()], null, null))).EnsureSuccessStatusCode();

        var sameNumber = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(admission.ToLowerInvariant()), [NewGuardian()], null, null,
            DuplicateStudentOverrideReason: "An override reason does not apply to admission numbers."));
        Assert.Equal(HttpStatusCode.BadRequest, sameNumber.StatusCode);
        Assert.Equal(Application.Services.EnrollmentService.DuplicateAdmissionNumber, await sameNumber.MessageAsync());
    }

    [Fact]
    public async Task SameNameAndBirthDate_IsAWarning_ThatAnAdminOverridesWithARecordedReason()
    {
        var admin = await _api.AdminClientAsync();
        var first = NewStudent();
        var original = await Read<EnrollmentResultDto>(await Enroll(admin, new EnrollStudentRequest(Key(), first, [NewGuardian()], null, null)));
        var twin = first with { FullName = first.FullName.ToUpperInvariant(), AdmissionNumber = Unique("TWIN") };
        var guardian = NewGuardian();
        var count = await StudentCountAsync();

        // Without a reason: a confirmation request naming the existing record; nothing written, no email.
        var warned = await Enroll(admin, new EnrollStudentRequest(Key(), twin, [guardian], Uid(), null));
        Assert.Equal(HttpStatusCode.Conflict, warned.StatusCode);
        var body = await warned.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Json.Options);
        Assert.Equal("ConfirmationRequired", body.GetProperty("code").GetString());
        var match = Assert.Single(body.GetProperty("confirmation").GetProperty("possibleDuplicates").EnumerateArray());
        Assert.Equal(original.StudentId, match.GetProperty("studentId").GetInt32());
        Assert.Equal(count, await StudentCountAsync());
        Assert.DoesNotContain(_api.Email.Sent, s => s.Message.ToEmail == guardian.Email);

        // The read-only step-1 check reports the same match.
        var check = await Read<EnrollmentStudentCheckDto>(await admin.PostAsJsonAsync("/api/enrollments/check-student",
            new EnrollmentStudentCheckRequest(twin.FullName, twin.DateOfBirth, twin.AdmissionNumber), Json.Options));
        Assert.False(check.AdmissionNumberTaken);
        Assert.Equal(original.StudentId, Assert.Single(check.PossibleDuplicates).StudentId);

        // A too-short reason is refused by validation.
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll(admin, new EnrollStudentRequest(Key(), twin, [guardian], null, null,
            DuplicateStudentOverrideReason: "twin"))).StatusCode);

        // With a reason: enrolled, and the reason is recorded with the admin who gave it.
        const string reason = "Twin sister of the existing student; separate admission.";
        var key = Key();
        var enrolled = await Enroll(admin, new EnrollStudentRequest(key, twin, [guardian], null, null, DuplicateStudentOverrideReason: reason));
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        await using var db = _api.NewDb();
        var record = await db.EnrollmentRequests.SingleAsync(r => r.IdempotencyKey == key);
        Assert.Equal(reason, record.DuplicateOverrideReason);
        Assert.NotNull(record.AdminId);
        Assert.Equal(count + 1, await db.Students.CountAsync());
    }

    [Fact]
    public async Task OverrideReason_IsNotRecorded_WhenThereIsNoDuplicate()
    {
        var admin = await _api.AdminClientAsync();
        var key = Key();
        (await Enroll(admin, new EnrollStudentRequest(key, NewStudent(), [NewGuardian()], null, null,
            DuplicateStudentOverrideReason: "Precautionary reason with no match."))).EnsureSuccessStatusCode();
        await using var db = _api.NewDb();
        Assert.Null((await db.EnrollmentRequests.SingleAsync(r => r.IdempotencyKey == key)).DuplicateOverrideReason);
    }

    [Fact]
    public async Task DeactivatedGuardian_NeedsExplicitConfirmation_AndStaysDeactivated()
    {
        var admin = await _api.AdminClientAsync();
        var inactiveId = await ExistingGuardianAsync();
        await using (var db = _api.NewDb())
        {
            (await db.Guardians.SingleAsync(g => g.GuardianId == inactiveId)).Status = RecordStatus.Inactive;
            await db.SaveChangesAsync();
        }
        var lookup = await Read<List<GuardianLookupDto>>(await admin.GetAsync(
            $"/api/guardians/lookup?q={Uri.EscapeDataString((await GuardianEmailAsync(inactiveId)))}"));
        Assert.Equal(RecordStatus.Inactive, Assert.Single(lookup).Status);
        var count = await StudentCountAsync();

        var unconfirmed = await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [Existing(inactiveId)], null, null));
        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
        var body = await unconfirmed.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Json.Options);
        var listed = Assert.Single(body.GetProperty("confirmation").GetProperty("inactiveGuardians").EnumerateArray());
        Assert.Equal(inactiveId, listed.GetProperty("guardianId").GetInt32());
        Assert.Equal(count, await StudentCountAsync());

        var key = Key();
        var confirmed = await Enroll(admin, new EnrollStudentRequest(key, NewStudent(), [Existing(inactiveId) with { ConfirmInactiveAccount = true }], null, null));
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        await using var after = _api.NewDb();
        Assert.Equal(RecordStatus.Inactive, (await after.Guardians.SingleAsync(g => g.GuardianId == inactiveId)).Status);
        Assert.Equal(inactiveId.ToString(), (await after.EnrollmentRequests.SingleAsync(r => r.IdempotencyKey == key)).ConfirmedInactiveGuardianIds);
    }

    [Fact]
    public async Task AKeyReusedWithDifferentDetails_IsRefused()
    {
        var admin = await _api.AdminClientAsync();
        var key = Key();
        (await Enroll(admin, new EnrollStudentRequest(key, NewStudent(), [NewGuardian()], null, null))).EnsureSuccessStatusCode();
        var count = await StudentCountAsync();

        var reused = await Enroll(admin, new EnrollStudentRequest(key, NewStudent(), [NewGuardian()], null, null));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var body = await reused.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Json.Options);
        Assert.Equal("IdempotencyKeyReused", body.GetProperty("code").GetString());
        Assert.Equal(count, await StudentCountAsync());
    }

    [Fact]
    public async Task ConcurrentEnrollmentsCompetingForOneGuardianEmailOrCard_LeaveOneWinnerAndNoOrphans()
    {
        var sharedEmail = $"{Guid.NewGuid():N}@race.test";
        var sharedCard = Uid();
        var before = await CountsAsync();
        var clients = new List<HttpClient>();
        for (var i = 0; i < 6; i++) clients.Add(await _api.AdminClientAsync());

        // Six different forms: three race for the same new guardian email, three for the same card.
        var responses = await TestData.Simultaneously(6, i => Enroll(clients[i], new EnrollStudentRequest(Key(), NewStudent(),
            [i < 3 ? NewGuardian(email: sharedEmail) : NewGuardian()], i < 3 ? null : sharedCard, null)));

        Assert.Equal(1, responses.Take(3).Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Skip(3).Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));

        var after = await CountsAsync();
        Assert.Equal(before.Students + 2, after.Students);
        Assert.Equal(before.Requests + 2, after.Requests);
        Assert.Equal(before.Links + 2, after.Links);
        Assert.Equal(before.Guardians + 2, after.Guardians);
        Assert.Equal(before.Cards + 1, after.Cards);
        Assert.Equal(before.History + 1, after.History);
        Assert.Single(_api.Email.Sent, s => s.Message.ToEmail == sharedEmail);
    }

    [Fact]
    public async Task AnEmailProviderException_DoesNotUndoTheEnrollment()
    {
        var admin = await _api.AdminClientAsync();
        _api.Email.ThrowNext = true;
        try
        {
            var result = await Read<EnrollmentResultDto>(await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], null, null)));
            var g = Assert.Single(result.Guardians);
            Assert.False(g.CredentialEmailSent);
            Assert.Contains("delivery failed", g.EmailError);
        }
        finally { _api.Email.ThrowNext = false; }
    }

    private async Task<string> GuardianEmailAsync(int id)
    {
        await using var db = _api.NewDb();
        return (await db.Guardians.SingleAsync(g => g.GuardianId == id)).Email;
    }

    private sealed record Counts(int Students, int Guardians, int Links, int Cards, int History, int Requests, int Assignments);

    private async Task<Counts> CountsAsync()
    {
        await using var db = _api.NewDb();
        return new Counts(await db.Students.CountAsync(), await db.Guardians.CountAsync(), await db.StudentGuardians.CountAsync(),
            await db.RfidCards.CountAsync(), await db.RfidCardAssignments.CountAsync(), await db.EnrollmentRequests.CountAsync(),
            await db.StudentBusAssignments.CountAsync());
    }

    [Fact]
    public async Task InvalidRequests_AreRejectedBeforeAnythingIsWritten()
    {
        var admin = await _api.AdminClientAsync();
        var count = await StudentCountAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll(admin, new EnrollStudentRequest("bad key!", NewStudent(), [NewGuardian()], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(),
            [new EnrollmentGuardianDto(null, "No Contact", "nc@enrol.test", null, null, false, null, true)], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Enroll(admin, new EnrollStudentRequest(Key(), NewStudent(), [Existing(int.MaxValue)], null, null))).StatusCode);
        Assert.Equal(count, await StudentCountAsync());
    }

    // ---------------------------------------------------------------- guardian lookup

    [Fact]
    public async Task GuardianLookup_FindsByEmailOrContactNumber_WithLinkedStudents()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await using var db = _api.NewDb();
        var g = await db.Guardians.SingleAsync(x => x.GuardianId == child.GuardianId);

        var byEmail = await Read<List<GuardianLookupDto>>(await admin.GetAsync($"/api/guardians/lookup?q={Uri.EscapeDataString(g.Email.ToUpperInvariant())}"));
        var hit = Assert.Single(byEmail);
        Assert.Equal(g.GuardianId, hit.GuardianId);
        Assert.Single(hit.LinkedStudents);

        var spaced = g.Phone![..5] + " " + g.Phone[5..];
        var byPhone = await Read<List<GuardianLookupDto>>(await admin.GetAsync($"/api/guardians/lookup?q={Uri.EscapeDataString(spaced)}"));
        Assert.Equal(g.GuardianId, byPhone[0].GuardianId);

        Assert.Empty(await Read<List<GuardianLookupDto>>(await admin.GetAsync("/api/guardians/lookup?q=ab")));
        Assert.Empty(await Read<List<GuardianLookupDto>>(await admin.GetAsync($"/api/guardians/lookup?q=nobody-{Guid.NewGuid():N}")));
    }

    // ---------------------------------------------------------------- 18. authorization

    [Fact]
    public async Task EnrollmentEndpoints_AreAdminOnly()
    {
        var anonymous = _api.CreateClient();
        var guardian = _api.GuardianClient(1);
        var request = new EnrollStudentRequest(Key(), NewStudent(), [NewGuardian()], null, null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Enroll(anonymous, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enroll(guardian, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/guardians/lookup?q=test")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PostAsJsonAsync("/api/rfid/cards/check", new RfidCardCheckRequest("X1"), Json.Options)).StatusCode);
    }
}
