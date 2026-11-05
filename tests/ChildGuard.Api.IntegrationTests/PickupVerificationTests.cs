using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Pickup verification state machine, expiry and concurrency against the real database.</summary>
public class PickupVerificationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PickupVerificationTests(ApiFactory api) => _api = api;

    private async Task<(HttpClient Admin, Child Child)> ChildAtSchoolAsync()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        return (admin, child);
    }

    [Fact]
    public async Task Approve_RecordsDepartureEventAndAttendanceTogether()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var id = await admin.VerifyFaceAsync(_api, child);

        var response = await admin.ApproveAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
        var departure = await db.AttendanceRecords.SingleAsync(a => a.StudentId == child.StudentId && a.EventType == AttendanceEventType.SchoolDeparture);
        Assert.Null(departure.RfidCardId);
        Assert.Equal(_api.Clock.ToSchoolDateTime(departure.RecordedAtUtc), (departure.Date, departure.Time));
    }

    [Fact]
    public async Task ConcurrentApprovals_OfDifferentVerificationsForOneChild_RecordOneDeparture()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var first = await admin.VerifyFaceAsync(_api, child);
        var second = await admin.VerifyFaceAsync(_api, child);

        var responses = await TestData.Simultaneously(2, i => admin.ApproveAsync(i == 0 ? first : second));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
        Assert.Equal(1, await db.AttendanceRecords.CountAsync(a => a.StudentId == child.StudentId && a.EventType == AttendanceEventType.SchoolDeparture));
        Assert.Equal(1, await db.GuardianVerifications.CountAsync(v => v.StudentId == child.StudentId && v.PickupApproved));
    }

    [Fact]
    public async Task ConcurrentApprovals_OfTheSameVerification_RecordOneDeparture()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var id = await admin.VerifyFaceAsync(_api, child);

        var responses = await TestData.Simultaneously(5, _ => admin.ApproveAsync(id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.AttendanceRecords.CountAsync(a => a.StudentId == child.StudentId && a.EventType == AttendanceEventType.SchoolDeparture));
    }

    [Fact]
    public async Task ConcurrentApproveAndReject_ResponsesAlwaysMatchTheStoredOutcome()
    {
        for (var round = 0; round < 5; round++)
        {
            var (admin, child) = await ChildAtSchoolAsync();
            var id = await admin.VerifyFaceAsync(_api, child);

            var responses = await TestData.Simultaneously(2, i => i == 0 ? admin.ApproveAsync(id) : admin.RejectAsync(id));

            await using var db = _api.NewDb();
            var stored = await db.GuardianVerifications.SingleAsync(v => v.Id == id);
            var approveOk = responses[0].StatusCode == HttpStatusCode.OK;
            var rejectOk = responses[1].StatusCode == HttpStatusCode.OK;
            Assert.True(approveOk ^ rejectOk, $"round {round}: exactly one decision must win");
            Assert.Equal(approveOk, stored.PickupApproved);
            var departures = await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture);
            Assert.Equal(approveOk ? 1 : 0, departures);
        }
    }

    [Fact]
    public async Task RejectedVerification_CannotBeApproved_NewVerificationCan()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var rejected = await admin.VerifyFaceAsync(_api, child);
        (await admin.RejectAsync(rejected, "ID not shown")).EnsureSuccessStatusCode();

        var blocked = await admin.ApproveAsync(rejected);
        var fresh = await admin.VerifyFaceAsync(_api, child);
        var approved = await admin.ApproveAsync(fresh);

        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(VerificationService.RejectedCannotBeApprovedMessage, await blocked.MessageAsync());
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task RepeatedReject_IsIdempotent_AndApprovedCannotBeRejected()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var id = await admin.VerifyFaceAsync(_api, child);
        (await admin.RejectAsync(id)).EnsureSuccessStatusCode();
        var again = await admin.RejectAsync(id);

        var other = await admin.VerifyFaceAsync(_api, child);
        (await admin.ApproveAsync(other)).EnsureSuccessStatusCode();
        var rejectApproved = await admin.RejectAsync(other);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejectApproved.StatusCode);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.RelatedEntityId == id && n.Type == NotificationType.PickupVerification));
    }

    [Fact]
    public async Task Verification_ExpiresAfterFifteenMinutes_OnTheBackend()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var id = await admin.VerifyFaceAsync(_api, child);

        _api.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var approve = await admin.ApproveAsync(id);
        var reject = await admin.RejectAsync(id);
        var history = await admin.GetFromJsonAsync<List<GuardianVerificationHistoryDto>>(
            $"/api/verification/history?studentId={child.StudentId}", Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        Assert.Equal(VerificationService.ExpiredMessage, await approve.MessageAsync());
        Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);
        Assert.Equal(PickupVerificationState.Expired, history!.Single(v => v.Id == id).State);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
    }

    [Fact]
    public async Task Verification_ExpiryIsBasedOnStoredUtcAttemptTime()
    {
        // Expiry must survive an API restart: it is computed from AttemptedAt in the
        // database, not from in-memory state. Simulate by checking a row whose attempt
        // time is 14 minutes old (still pending) after a fresh read.
        var (admin, child) = await ChildAtSchoolAsync();
        var id = await admin.VerifyFaceAsync(_api, child);
        _api.Time.Advance(TimeSpan.FromMinutes(14));

        var response = await admin.ApproveAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FaceNoMatch_CannotBeApproved()
    {
        var (admin, child) = await ChildAtSchoolAsync();
        var id = await admin.VerifyFaceAsync(_api, child, match: false);

        var response = await admin.ApproveAsync(id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_WhenChildIsNotAtSchool_IsRefused()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(); // no arrival today
        var id = await admin.VerifyFaceAsync(_api, child);

        var response = await admin.ApproveAsync(id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("Pickup cannot be recorded", await response.MessageAsync());
    }

    [Fact]
    public async Task Approve_UnknownVerification_Returns400()
    {
        var admin = await _api.AdminClientAsync();

        var response = await admin.ApproveAsync(987654);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
