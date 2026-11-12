using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>An administrator can only approve a pickup that was verified with a live person. Rejecting is always possible.</summary>
public class ApprovalLivenessGateTests
{
    private sealed class Harness
    {
        public FakeVerificationRepository Verifications { get; } = new();
        public FakeJourneyRepository Journey { get; } = new();
        public FakeNotificationRepository Notifications { get; } = new();
        public VerificationService Service { get; }
        public MutableTimeProvider Time { get; }
        private readonly Student _student = new() { StudentId = 1, FullName = "Rahul Patel", DateOfBirth = new DateTime(2016, 4, 2), Status = RecordStatus.Active };
        private readonly Guardian _guardian = new() { GuardianId = 1, FullName = "Priya Patel", Email = "p@x.test", PasswordHash = "x", Status = RecordStatus.Active };

        public Harness(bool requireLiveness)
        {
            var (clock, time) = TestClock.Create();
            Time = time;
            var students = new FakeStudentRepository();
            var guardians = new FakeGuardianRepository();
            var faces = new FakeFaceServiceClient();
            students.Students.Add(_student);
            guardians.Guardians.Add(_guardian);
            students.Links.Add(new StudentGuardian { StudentId = 1, GuardianId = 1, IsAuthorizedForPickup = true });
            Journey.Events.Add(new JourneyEvent { StudentId = 1, EventType = JourneyEventType.SchoolArrival, Timestamp = clock.TodayStartUtc.AddHours(8), Source = EventSource.Rfid });
            var references = new GuardianFaceReferenceService(Verifications, guardians, faces, new NullEmbeddingProtector(), new FakeFaceAuditLog(), new BiometricsOptions(), time);
            Service = new VerificationService(Verifications, students, guardians, Journey, Notifications, new FakePushNotificationSender(), faces,
                new JourneyValidationService(), clock, new VerificationOptions { RequireLiveness = requireLiveness }, new NullEmbeddingProtector(), references);
        }

        public int Add(Action<GuardianVerification>? configure = null)
        {
            var v = new GuardianVerification
            {
                Id = Verifications.Verifications.Count + 1, StudentId = 1, GuardianId = 1, Student = _student, Guardian = _guardian,
                AttemptedAt = Time.UtcNow, MatchScore = 0.9, Result = VerificationResult.Match
            };
            configure?.Invoke(v);
            Verifications.Verifications.Add(v);
            return v.Id;
        }

        public static void FullPass(GuardianVerification v)
        {
            v.CaptureSource = CaptureSource.Webcam; v.QualityResult = CheckResult.Pass; v.FaceDetectionResult = CheckResult.Pass;
            v.IdentityResult = IdentityResult.Match; v.AuthorisationResult = AuthorisationResult.Authorised; v.LivenessResult = LivenessResult.Pass;
        }

        public bool Departed => Journey.Events.Any(e => e.EventType == JourneyEventType.SchoolDeparture);
    }

    [Fact]
    public async Task WhileTheSwitchIsOff_AnOldRecordWithoutLiveness_StillApproves_AsBefore()
    {
        var h = new Harness(requireLiveness: false);
        var id = h.Add();

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(h.Departed);
    }

    [Fact]
    public async Task WithTheSwitchOn_ARecordWithoutLiveness_CannotBeApproved_AndNothingIsRecorded()
    {
        var h = new Harness(requireLiveness: true);
        var id = h.Add(); // what the original face-verify endpoint produces

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9);

        Assert.False(result.Succeeded);
        Assert.Equal(VerificationService.LivenessRequiredMessage, result.Error);
        Assert.False(h.Departed);
        Assert.False(h.Verifications.Verifications[0].PickupApproved);
        Assert.Empty(h.Notifications.Notifications);
    }

    [Fact]
    public async Task WithTheSwitchOn_AFullyLiveVerifiedRecord_Approves()
    {
        var h = new Harness(requireLiveness: true);
        var id = h.Add(Harness.FullPass);

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(h.Departed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARecordWhoseLivenessFailedIsNeverApprovable_WhateverItsScoreOrTheSwitch(bool requireLiveness)
    {
        var h = new Harness(requireLiveness);
        var id = h.Add(v => { Harness.FullPass(v); v.LivenessResult = LivenessResult.Fail; v.MatchScore = 0.99; });

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9);

        Assert.False(result.Succeeded);
        Assert.False(h.Departed);
    }

    [Fact]
    public async Task WithTheSwitchOn_LivenessNotPerformed_IsNotAPass()
    {
        var h = new Harness(requireLiveness: true);
        var id = h.Add(v => { Harness.FullPass(v); v.LivenessResult = LivenessResult.NotPerformed; });

        Assert.False((await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9)).Succeeded);
        Assert.False(h.Departed);
    }

    [Fact]
    public async Task WithTheSwitchOn_AGalleryPhotoRecord_IsNotApprovable()
    {
        var h = new Harness(requireLiveness: true);
        var id = h.Add(v => { Harness.FullPass(v); v.CaptureSource = CaptureSource.Gallery; v.LivenessResult = LivenessResult.NotPerformed; });

        Assert.False((await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9)).Succeeded);
        Assert.False(h.Departed);
    }

    [Theory]
    [InlineData(CaptureSource.Gallery)]
    [InlineData(CaptureSource.Webcam)]
    public async Task WithTheSwitchOff_AMatchedPhotoWithoutALiveCheck_CanBeApproved(CaptureSource source)
    {
        var h = new Harness(requireLiveness: false);
        var id = h.Add(v => { Harness.FullPass(v); v.CaptureSource = source; v.LivenessResult = LivenessResult.NotPerformed; });

        var result = await h.Service.ApproveAsync(new ApprovePickupRequest(id), 9);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(h.Departed);
    }

    [Fact]
    public async Task Rejecting_StillWorks_ForAnyRecord()
    {
        var h = new Harness(requireLiveness: true);
        var legacy = h.Add();
        var failed = h.Add(v => { Harness.FullPass(v); v.LivenessResult = LivenessResult.Fail; v.Result = VerificationResult.NoMatch; });

        Assert.True((await h.Service.RejectAsync(new RejectPickupRequest(legacy, "no"), 9)).Succeeded);
        Assert.True((await h.Service.RejectAsync(new RejectPickupRequest(failed, "spoof"), 9)).Succeeded);
    }

    [Fact]
    public async Task TheHistoryShowsTheLayers_AndLegacyRecordsShowNone()
    {
        var h = new Harness(requireLiveness: false);
        h.Add();
        h.Add(Harness.FullPass);

        var history = (await h.Service.GetHistoryAsync(null)).OrderBy(r => r.Id).ToList();

        Assert.Null(history[0].Layers);
        Assert.Equal(LivenessResult.Pass, history[1].Layers!.Liveness);
        Assert.Equal(CaptureSource.Webcam, history[1].Layers!.Source);
    }
}
