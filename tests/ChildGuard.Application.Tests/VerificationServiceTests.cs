using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>The pickup desk: face verify, then an admin approves or rejects.</summary>
public class VerificationServiceTests
{
    private sealed class Harness
    {
        public FakeVerificationRepository Verifications { get; } = new();
        public FakeStudentRepository Students { get; } = new();
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeJourneyRepository Journey { get; } = new();
        public FakeNotificationRepository Notifications { get; } = new();
        public FakePushNotificationSender Push { get; } = new();
        public FakeFaceServiceClient FaceClient { get; } = new();
        public VerificationService Service { get; }

        public Student Student { get; } = new() { StudentId = 1, FullName = "Rahul Patel", DateOfBirth = new DateTime(2016, 4, 2), Status = RecordStatus.Active };
        public Guardian Guardian { get; } = new() { GuardianId = 1, FullName = "Priya Patel", Email = "priya@example.test", Phone = "9820011111", PasswordHash = "hashed:x", Status = RecordStatus.Active };

        public Harness()
        {
            Service = new VerificationService(Verifications, Students, Guardians, Journey, Notifications, Push, FaceClient, new JourneyValidationService());
            Students.Students.Add(Student);
            Guardians.Guardians.Add(Guardian);
            Students.Links.Add(new StudentGuardian { StudentId = 1, GuardianId = 1, IsAuthorizedForPickup = true });
            Verifications.FaceData.Add(new GuardianFaceData
            {
                GuardianId = 1, EmbeddingJson = "[0.1,0.2]", Model = "test-model", IsActive = true, RegisteredAt = DateTime.UtcNow
            });
            // A pickup only makes sense once the student is recorded as at school.
            Journey.Events.Add(new JourneyEvent
            {
                StudentId = 1, EventType = JourneyEventType.SchoolArrival, Timestamp = DateTime.UtcNow.Date, Source = EventSource.Rfid
            });
        }

        /// <summary>
        /// Runs a matching face-verify and returns the resulting verification id.
        /// Also wires up the Student/Guardian navigation properties that the real
        /// repository would load via .Include() — this fake has no equivalent.
        /// </summary>
        public async Task<int> VerifyAsync()
        {
            FaceClient.NextMatch = true;
            var result = await Service.VerifyAsync(new FaceVerifyRequest(1, 1, "img"));
            var id = result.Value!.VerificationId;
            var stored = Verifications.Verifications.First(v => v.Id == id);
            stored.Student = Student;
            stored.Guardian = Guardian;
            return id;
        }
    }

    [Fact]
    public async Task Approve_OnAMatchedVerification_RecordsDepartureAndNotifies()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.True(result.Succeeded);
        Assert.Contains(h.Journey.Events, e => e.EventType == JourneyEventType.SchoolDeparture);
        Assert.Single(h.Notifications.Notifications);
        Assert.Single(h.Push.Sent);
        Assert.True(h.Verifications.Verifications[0].PickupApproved);
    }

    [Fact]
    public async Task Approve_CalledTwice_SecondCallIsRejected()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        var second = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.False(second.Succeeded);
        Assert.Equal("Pickup already approved for this verification.", second.Error);
        Assert.Single(h.Journey.Events, e => e.EventType == JourneyEventType.SchoolDeparture);
    }

    [Fact]
    public async Task Approve_WhenAConcurrentApprovalWinsTheRace_IsRejectedNotDuplicated()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        // Simulates a second admin's SaveChanges landing first, between this
        // call's read and its own write.
        h.Verifications.SimulateConcurrencyConflictOnNextSave = true;

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.False(result.Succeeded);
        Assert.Equal(VerificationService.DecidedConcurrentlyMessage, result.Error);
        Assert.DoesNotContain(h.Journey.Events, e => e.EventType == JourneyEventType.SchoolDeparture);
    }

    [Fact]
    public async Task Approve_WhenGuardianWasDeactivatedAfterVerify_IsRejected()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        h.Guardian.Status = RecordStatus.Inactive;

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(h.Journey.Events, e => e.EventType == JourneyEventType.SchoolDeparture);
    }

    [Fact]
    public async Task Approve_WhenStudentWasDeactivatedAfterVerify_IsRejected()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        h.Student.Status = RecordStatus.Inactive;

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(h.Journey.Events, e => e.EventType == JourneyEventType.SchoolDeparture);
    }

    [Fact]
    public async Task Approve_WhenFaceDidNotMatch_IsRejected()
    {
        var h = new Harness();
        h.FaceClient.NextMatch = false;
        var verifyResult = await h.Service.VerifyAsync(new FaceVerifyRequest(1, 1, "img"));
        var id = verifyResult.Value!.VerificationId;

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.False(result.Succeeded);
        Assert.Equal("Cannot approve pickup — face was not verified as a match.", result.Error);
    }

    [Fact]
    public async Task Reject_OnAPendingVerification_RecordsReasonAndNotifies()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();

        var result = await h.Service.RejectAsync(new RejectPickupRequest(id, "Guardian left before capture"), actingAdminId: 7);

        Assert.True(result.Succeeded);
        Assert.False(h.Verifications.Verifications[0].PickupApproved);
        Assert.Equal("Guardian left before capture", h.Verifications.Verifications[0].Notes);
        Assert.Single(h.Notifications.Notifications);
    }

    [Fact]
    public async Task Reject_CalledTwice_SecondCallIsANoOpAndDoesNotNotifyAgain()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        await h.Service.RejectAsync(new RejectPickupRequest(id, "First reason"), actingAdminId: 7);

        var second = await h.Service.RejectAsync(new RejectPickupRequest(id, "Second reason"), actingAdminId: 9);

        Assert.True(second.Succeeded);
        Assert.Single(h.Notifications.Notifications);
        Assert.Equal("First reason", h.Verifications.Verifications[0].Notes);
    }

    [Fact]
    public async Task Reject_WhenAConcurrentDecisionWinsTheRace_ReportsConflictNotRejected()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        // The winner may have been an approval — the loser must not claim "rejected".
        h.Verifications.SimulateConcurrencyConflictOnNextSave = true;

        var result = await h.Service.RejectAsync(new RejectPickupRequest(id, "No ID"), actingAdminId: 9);

        Assert.False(result.Succeeded);
        Assert.Equal(VerificationService.DecidedConcurrentlyMessage, result.Error);
        Assert.Empty(h.Notifications.Notifications);
    }

    [Fact]
    public async Task Approve_RunsUnderTheStudentJourneyLock()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();

        await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.Equal(1, h.Journey.SerializedRuns);
    }

    [Fact]
    public async Task Approve_WhenArrivalWasYesterdayNotToday_IsRejected()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        // Move the only arrival into yesterday: the student is not at school today.
        h.Journey.Events[0].Timestamp = DateTime.UtcNow.Date.AddHours(-3);

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        Assert.False(result.Succeeded);
        Assert.False(h.Verifications.Verifications[0].PickupApproved);
        Assert.DoesNotContain(h.Journey.Events, e => e.EventType == JourneyEventType.SchoolDeparture);
    }

    [Fact]
    public async Task Reject_AfterApproval_IsRejected()
    {
        var h = new Harness();
        var id = await h.VerifyAsync();
        await h.Service.ApproveAsync(new ApprovePickupRequest(id), approvingAdminId: 7);

        var result = await h.Service.RejectAsync(new RejectPickupRequest(id, "Changed mind"), actingAdminId: 7);

        Assert.False(result.Succeeded);
        Assert.Equal("This pickup was already approved and cannot be rejected.", result.Error);
    }
}
