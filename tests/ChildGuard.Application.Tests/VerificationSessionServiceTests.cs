using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Services.FaceVerification;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

internal sealed class FixedChallengeGenerator : ILiveChallengeGenerator
{
    private int _n;
    public IReadOnlyList<string> Steps { get; set; } = ["turn_left", "turn_right"];
    public LiveChallenge Create() => new($"TOKEN-{++_n}", Steps);
}

public class VerificationSessionServiceTests
{
    private const int Admin = 7;

    private sealed class Harness
    {
        public FakeVerificationRepository Verifications { get; } = new();
        public FakeStudentRepository Students { get; } = new();
        public FakeGuardianRepository Guardians { get; } = new();
        public FakeFaceServiceClient Faces { get; } = new();
        public FakeFaceAuditLog Audit { get; } = new();
        public FixedChallengeGenerator Challenges { get; } = new();
        public FaceVerificationOptions Options { get; } = new();
        public VerificationOptions Pickup { get; } = new();
        public MutableTimeProvider Time { get; }
        public VerificationSessionService Service { get; }

        public Harness()
        {
            (_, Time) = TestClock.Create();
            Pickup.RequireLiveness = true; // strict mode; the photo-only default has its own tests below
            var references = new GuardianFaceReferenceService(Verifications, Guardians, Faces, new NullEmbeddingProtector(), Audit, new BiometricsOptions(), Time);
            Service = new VerificationSessionService(Verifications, Students, references, Faces, Audit, Challenges, Options, Pickup, Time);

            Students.Students.Add(new Student { StudentId = 1, FullName = "Rahul Patel", DateOfBirth = new DateTime(2016, 1, 1), Status = RecordStatus.Active });
            // 1 and 2: authorised for Rahul. 3: linked but not authorised. 4: has a face but is not linked to Rahul at all.
            AddGuardian(1, "Priya Patel", linked: true, authorised: true);
            AddGuardian(2, "Dev Patel", linked: true, authorised: true);
            AddGuardian(3, "Uncle Raj", linked: true, authorised: false);
            AddGuardian(4, "Stranger Sam", linked: false, authorised: false);
        }

        private void AddGuardian(int id, string name, bool linked, bool authorised)
        {
            var guardian = new Guardian { GuardianId = id, FullName = name, Email = $"g{id}@x.test", PasswordHash = "x", Status = RecordStatus.Active };
            Guardians.Guardians.Add(guardian);
            Verifications.FaceData.Add(new GuardianFaceData { GuardianId = id, EmbeddingJson = $"[0.{id},0.5]", Model = "m", IsActive = true });
            if (linked)
                Students.Links.Add(new StudentGuardian { StudentId = 1, GuardianId = id, Guardian = guardian, Relationship = "Parent", IsAuthorizedForPickup = authorised });
        }

        public async Task<VerificationSessionDto> StartAsync(int adminId = Admin) =>
            (await Service.StartAsync(1, adminId)).Value!;

        /// <summary>Starts a session, takes a challenge, and returns both.</summary>
        public async Task<(int SessionId, LiveChallengeDto Challenge)> ChallengedAsync(int adminId = Admin)
        {
            var session = await StartAsync(adminId);
            var challenge = (await Service.IssueChallengeAsync(session.SessionId, adminId)).Value!;
            Time.Advance(TimeSpan.FromSeconds(5)); // a plausible time to follow the prompts
            return (session.SessionId, challenge);
        }

        public static LiveVerifyRequest Burst(string token, string prefix = "frame", int count = 10) =>
            new(token, Enumerable.Range(0, count).Select(i => new LiveFrameDto($"{prefix}-{i}", i * 400)).ToList());

        public Task<Result<VerificationAttemptResultDto>> LiveAsync(int sessionId, string token, int adminId = Admin) =>
            Service.LiveVerifyAsync(sessionId, adminId, Burst(token));

        public VerificationSession Session(int id) => Verifications.Sessions.Single(s => s.Id == id);
    }

    // ------------------------------------------------------------------ Start

    [Fact]
    public async Task Start_ListsOnlyTheAuthorisedActiveGuardiansForThatStudent_WithoutAnyEmbedding()
    {
        var h = new Harness();
        h.Guardians.Guardians.Single(g => g.GuardianId == 2).Status = RecordStatus.Inactive;

        var session = await h.StartAsync();

        var guardian = Assert.Single(session.Guardians);
        Assert.Equal(1, guardian.GuardianId);
        Assert.True(guardian.FaceRegistered);
        Assert.Equal(1, guardian.ReferenceCount);
        Assert.Equal(h.Time.UtcNow.AddMinutes(10), session.ExpiresAt);
        Assert.DoesNotContain("0.5", System.Text.Json.JsonSerializer.Serialize(session));
    }

    [Fact]
    public async Task Start_RefusesAnUnknownOrInactiveStudent()
    {
        var h = new Harness();
        Assert.Equal("Student not found.", (await h.Service.StartAsync(99, Admin)).Error);
        h.Students.Students[0].Status = RecordStatus.Inactive;
        Assert.False((await h.Service.StartAsync(1, Admin)).Succeeded);
    }

    [Fact]
    public async Task Start_ClosesTheSameAdminsEarlierOpenSession()
    {
        var h = new Harness();
        var first = await h.StartAsync();
        var second = await h.StartAsync();

        Assert.Equal(VerificationSessionState.Closed, h.Session(first.SessionId).State);
        Assert.Equal(VerificationSessionState.Open, h.Session(second.SessionId).State);
        Assert.False((await h.Service.IssueChallengeAsync(first.SessionId, Admin)).Succeeded);
    }

    // --------------------------------------------------------------- Identify

    [Fact]
    public async Task AGalleryMatch_IsLiveVerificationRequired_NotVerified_AndCreatesNoVerificationRecord()
    {
        var h = new Harness();
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;

        Assert.Equal("LiveVerificationRequired", result.Outcome);
        Assert.False(result.Verified);
        Assert.Equal(VerificationDecisionEngine.NextLiveVerification, result.NextStep);
        Assert.Equal(LivenessResult.NotPerformed, result.Liveness);
        Assert.Equal(IdentityResult.Match, result.Identity);
        Assert.Equal(AuthorisationResult.Authorised, result.Authorisation);
        Assert.Equal(1, result.Guardian!.GuardianId);
        Assert.Empty(h.Verifications.Verifications);
        Assert.Equal(1, h.Session(session.SessionId).IdentifiedGuardianId);
        Assert.Equal(CaptureSource.Gallery, h.Session(session.SessionId).IdentifiedSource);
    }

    [Fact]
    public async Task AWebcamStill_AlsoCannotEstablishLiveness()
    {
        var h = new Harness();
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Webcam, "img"))).Value!;

        Assert.False(result.Verified);
        Assert.Equal("LiveVerificationRequired", result.Outcome);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task AnUnusableImage_IsARetry_AndNoIdentityCheckRuns()
    {
        var h = new Harness();
        h.Faces.NextAnalysis = Result<ImageAnalysis>.Success(new("verification", 1, ["BLURRY"], [], ["The image is too blurry."], new(5, 150, 40, 200, 0, 0.99)));
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;

        Assert.Equal("RetryCapture", result.Outcome);
        Assert.Equal(CheckResult.Fail, result.Quality);
        Assert.Equal(CheckResult.Pass, result.FaceDetection);
        Assert.Null(result.Identity);
        Assert.Equal("The image is too blurry.", result.Message);
        Assert.Empty(h.Faces.IdentifyCalls);
    }

    [Fact]
    public async Task OnlyGuardiansLinkedToTheStudentAreEverCompared()
    {
        var h = new Harness();
        var session = await h.StartAsync();

        await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"));

        var ids = h.Faces.IdentifyCalls.Single().Select(c => c.GuardianId).OrderBy(i => i).ToArray();
        Assert.Equal([1, 2, 3], ids); // never guardian 4, who has a face but another family
    }

    [Fact]
    public async Task AGuardianOfAnotherChild_CannotVerifyHere_ItIsJustNoMatch()
    {
        var h = new Harness();
        h.Faces.NextScore = 0.05; // only Stranger Sam's face is at the camera, and Sam is not compared
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;

        Assert.Equal("NotRecognised", result.Outcome);
        Assert.Equal(IdentityResult.NoMatch, result.Identity);
        Assert.Null(result.Guardian);
        Assert.Equal(AuthorisationResult.NotEvaluated, result.Authorisation);
        Assert.DoesNotContain("Stranger Sam", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task ALinkedButNotAuthorisedGuardian_IsReportedAsUnauthorised()
    {
        var h = new Harness();
        h.Faces.ScoreByGuardian[3] = 0.9;
        h.Faces.NextScore = 0.1;
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;

        Assert.Equal("Unauthorised", result.Outcome);
        Assert.Equal(AuthorisationResult.Unauthorised, result.Authorisation);
        Assert.Equal("Uncle Raj", result.Guardian!.FullName);
        Assert.Contains("not authorised", result.Message);
        Assert.Null(h.Session(session.SessionId).IdentifiedGuardianId);
    }

    [Fact]
    public async Task AnInactiveGuardian_IsUnauthorised_EvenIfTheLinkSaysAuthorised()
    {
        var h = new Harness();
        h.Guardians.Guardians.Single(g => g.GuardianId == 1).Status = RecordStatus.Inactive;
        h.Faces.ScoreByGuardian[1] = 0.9;
        h.Faces.NextScore = 0.1;
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;

        Assert.Equal(AuthorisationResult.Unauthorised, result.Authorisation);
    }

    [Fact]
    public async Task TwoCloseGuardians_AreInconclusive_OnlyWhenTheMarginIsConfigured()
    {
        var h = new Harness();
        h.Faces.ScoreByGuardian[1] = 0.80;
        h.Faces.ScoreByGuardian[2] = 0.78;
        h.Faces.NextScore = 0.1;
        var session = await h.StartAsync();

        Assert.Equal("LiveVerificationRequired", (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!.Outcome);

        h.Options.AmbiguityMargin = 0.05;
        var inconclusive = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;
        Assert.Equal("ReviewRequired", inconclusive.Outcome);
        Assert.Equal(IdentityResult.Inconclusive, inconclusive.Identity);
        Assert.False(inconclusive.Verified);
    }

    [Fact]
    public async Task TheResultCarriesTheScoreThresholdAndWhichPhotoMatched()
    {
        var h = new Harness();
        h.Faces.NextScore = 0.55;
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"))).Value!;

        Assert.Equal(0.55, result.Score);
        Assert.Equal(0.40, result.Threshold);
        Assert.Equal("Moderate", result.ConfidenceBand);
        Assert.Equal("Primary face", result.ReferenceLabel);
    }

    [Fact]
    public async Task AnotherAdminsSession_IsReportedAsMissing_AndAudited()
    {
        var h = new Harness();
        var session = await h.StartAsync(adminId: 7);

        var result = await h.Service.IdentifyAsync(session.SessionId, 8, new(CaptureSource.Gallery, "img"));

        Assert.False(result.Succeeded);
        Assert.Equal(VerificationSessionService.SessionGoneMessage, result.Error);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Access && e.Outcome == "Denied");
        Assert.False((await h.Service.IssueChallengeAsync(session.SessionId, 8)).Succeeded);
    }

    [Fact]
    public async Task AnExpiredSession_IsRefusedAndMarkedExpired()
    {
        var h = new Harness();
        var session = await h.StartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(11));

        var result = await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"));

        Assert.False(result.Succeeded);
        Assert.Contains("expired", result.Error);
        Assert.Equal(VerificationSessionState.Expired, h.Session(session.SessionId).State);
    }

    [Fact]
    public async Task EachLayerIsLoggedOnItsOwn()
    {
        var h = new Harness();
        var session = await h.StartAsync();
        await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "img"));

        foreach (var layer in new[] { FaceAuditLayer.Quality, FaceAuditLayer.Detection, FaceAuditLayer.Identity, FaceAuditLayer.Authorisation, FaceAuditLayer.Liveness, FaceAuditLayer.Decision })
            Assert.Contains(h.Audit.Entries, e => e.Layer == layer && e.SessionId == session.SessionId && e.Actor.Id == Admin);
        Assert.Equal("NotPerformed", h.Audit.Entries.Single(e => e.Layer == FaceAuditLayer.Liveness && e.Action == "Identify").Outcome);
    }

    // ------------------------------------------- photo pass without a live check

    [Theory]
    [InlineData(CaptureSource.Gallery)]
    [InlineData(CaptureSource.Webcam)]
    public async Task WhenALiveCheckIsNotRequired_AMatchedAuthorisedPhoto_BecomesAPendingVerification(CaptureSource source)
    {
        var h = new Harness();
        h.Pickup.RequireLiveness = false;
        var session = await h.StartAsync();

        var result = (await h.Service.IdentifyAsync(session.SessionId, Admin, new(source, "img"))).Value!;

        Assert.True(result.Verified);
        Assert.Equal("PickupEligible", result.Outcome);
        Assert.Equal(LivenessResult.NotPerformed, result.Liveness);
        Assert.NotNull(result.VerificationId);
        var record = Assert.Single(h.Verifications.Verifications);
        Assert.Equal(VerificationResult.Match, record.Result);
        Assert.False(record.PickupApproved);
        Assert.Equal(source, record.CaptureSource);
        Assert.Equal(LivenessResult.NotPerformed, record.LivenessResult);
        Assert.Equal(PickupVerificationState.Pending, record.GetState(h.Time.UtcNow, h.Pickup.PendingExpiry));
        Assert.Equal(VerificationSessionState.Completed, h.Session(session.SessionId).State);
    }

    [Fact]
    public async Task WhenALiveCheckIsNotRequired_ABadPhotoOrAStrangerOrAnUnauthorisedGuardian_StillGetsNothing()
    {
        var h = new Harness();
        h.Pickup.RequireLiveness = false;

        h.Faces.NextScore = 0.05;
        var s1 = await h.StartAsync();
        Assert.False((await h.Service.IdentifyAsync(s1.SessionId, Admin, new(CaptureSource.Webcam, "img"))).Value!.Verified);

        h.Faces.NextScore = 0.1; h.Faces.ScoreByGuardian[3] = 0.9;
        var s2 = await h.StartAsync();
        Assert.Equal("Unauthorised", (await h.Service.IdentifyAsync(s2.SessionId, Admin, new(CaptureSource.Webcam, "img"))).Value!.Outcome);

        h.Faces.ScoreByGuardian.Clear(); h.Faces.NextScore = 0.9;
        h.Faces.NextAnalysis = Result<ImageAnalysis>.Success(new("verification", 1, ["BLURRY"], [], ["The image is too blurry."], new(5, 150, 40, 200, 0, 0.99)));
        var s3 = await h.StartAsync();
        Assert.Equal("RetryCapture", (await h.Service.IdentifyAsync(s3.SessionId, Admin, new(CaptureSource.Webcam, "img"))).Value!.Outcome);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task AFailedLiveCheck_StillBlocks_EvenWhenALiveCheckIsNotRequired()
    {
        var h = new Harness();
        h.Pickup.RequireLiveness = false;
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with { Passive = new PassiveSignals(true, 5, [0.05], 0.0, 0.05, 0.05) };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.False(result.Verified);
        Assert.Equal("LivenessFailed", result.Outcome);
    }

    // -------------------------------------------------------------- Challenge

    [Fact]
    public async Task TheChallengeIsStoredHashedNotInTheClear_AndExpiresInThirtySeconds()
    {
        var h = new Harness();
        h.Options.RequireHeadTurn = true;
        var session = await h.StartAsync();
        var issuedAt = h.Time.UtcNow;

        var challenge = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;

        var stored = h.Session(session.SessionId);
        Assert.Equal("TOKEN-1", challenge.ChallengeToken);
        Assert.NotEqual(challenge.ChallengeToken, stored.ChallengeTokenHash);
        Assert.Equal(64, stored.ChallengeTokenHash!.Length);
        Assert.DoesNotContain("TOKEN", stored.ChallengeTokenHash);
        Assert.Equal(issuedAt.AddSeconds(30), challenge.ExpiresAt);
        Assert.Equal(["turn_left", "turn_right"], challenge.Steps);
        Assert.Equal("turn_left,turn_right", stored.ChallengeSteps);
        Assert.Equal(6, challenge.MinFrames);
        Assert.Equal(16, challenge.MaxFrames);
    }

    [Fact]
    public async Task ByDefaultNoHeadTurnIsAsked_ButTheTokenRulesStillApply()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();

        Assert.Empty(challenge.Steps);
        Assert.True((await h.LiveAsync(id, challenge.ChallengeToken)).Value!.Verified);
        Assert.Empty(h.Faces.LivenessCalls.Single().Steps);
        Assert.False((await h.LiveAsync(id, challenge.ChallengeToken)).Succeeded);
    }

    [Fact]
    public async Task ANewChallenge_InvalidatesTheOldOne()
    {
        var h = new Harness();
        var session = await h.StartAsync();
        var first = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;
        var second = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;
        h.Time.Advance(TimeSpan.FromSeconds(5));

        var old = await h.LiveAsync(session.SessionId, first.ChallengeToken);
        Assert.False(old.Succeeded);
        Assert.Equal(VerificationSessionService.ChallengeUsedMessage, old.Error);
        Assert.True((await h.LiveAsync(session.SessionId, second.ChallengeToken)).Succeeded);
    }

    // ------------------------------------------------------------- LiveVerify

    [Fact]
    public async Task AFullPass_CreatesAPendingVerification_ThatStillNeedsAnAdministrator()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.True(result.Verified);
        Assert.Equal("PickupEligible", result.Outcome);
        Assert.Equal(VerificationDecisionEngine.NextApprove, result.NextStep);
        Assert.Equal(CheckResult.Pass, result.Quality);
        Assert.Equal(CheckResult.Pass, result.FaceDetection);
        Assert.Equal(IdentityResult.Match, result.Identity);
        Assert.Equal(AuthorisationResult.Authorised, result.Authorisation);
        Assert.Equal(LivenessResult.Pass, result.Liveness);
        Assert.Equal(1, result.Guardian!.GuardianId);

        var record = Assert.Single(h.Verifications.Verifications);
        Assert.Equal(VerificationResult.Match, record.Result);
        Assert.False(record.PickupApproved);
        Assert.Null(record.ApprovedByAdminId);
        Assert.Equal(CaptureSource.Webcam, record.CaptureSource);
        Assert.Equal(LivenessResult.Pass, record.LivenessResult);
        Assert.Equal(id, record.SessionId);
        Assert.Equal(PickupVerificationState.Pending, record.GetState(h.Time.UtcNow, h.Pickup.PendingExpiry));
        Assert.Equal(record.AttemptedAt + h.Pickup.PendingExpiry, result.ExpiresAt);
        Assert.Equal(VerificationSessionState.Completed, h.Session(id).State);
        Assert.Equal(record.Id, h.Session(id).VerificationId);
    }

    [Fact]
    public async Task TheLiveCheckAskedTheFaceServiceForTheChallengeStepsAndAllLinkedGuardians()
    {
        var h = new Harness();
        h.Options.RequireHeadTurn = true;
        h.Challenges.Steps = ["turn_right", "turn_right"];
        var (id, challenge) = await h.ChallengedAsync();

        await h.LiveAsync(id, challenge.ChallengeToken);

        var call = Assert.Single(h.Faces.LivenessCalls);
        Assert.Equal(["turn_right", "turn_right"], call.Steps);
        Assert.Equal(10, call.Frames.Count);
        Assert.Equal([1, 2, 3], call.Candidates!.Select(c => c.GuardianId).OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task ATokenCanBeUsedOnce()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();
        Assert.True((await h.LiveAsync(id, challenge.ChallengeToken)).Succeeded);

        var again = await h.LiveAsync(id, challenge.ChallengeToken);

        Assert.False(again.Succeeded);
        Assert.Single(h.Faces.LivenessCalls);
    }

    [Fact]
    public async Task AFailedAttemptSpendsTheToken_SoItCannotBeRetriedWithTheSameOne()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with { ChallengeCompleted = false };
        var (id, challenge) = await h.ChallengedAsync();

        Assert.Equal("LivenessFailed", (await h.LiveAsync(id, challenge.ChallengeToken)).Value!.Outcome);
        var retry = await h.LiveAsync(id, challenge.ChallengeToken);

        Assert.False(retry.Succeeded);
        Assert.Single(h.Faces.LivenessCalls);
    }

    [Fact]
    public async Task AWrongToken_IsRefused_WithoutSpendingTheRealOne_OrReachingTheFaceService()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();

        var wrong = await h.LiveAsync(id, "TOKEN-999");

        Assert.False(wrong.Succeeded);
        Assert.Empty(h.Faces.LivenessCalls);
        Assert.True((await h.LiveAsync(id, challenge.ChallengeToken)).Succeeded);
        Assert.Contains(h.Audit.Entries, e => e.Layer == FaceAuditLayer.Liveness && e.Outcome == "Rejected" && e.Detail == "token not valid");
    }

    [Fact]
    public async Task AnExpiredChallenge_IsRefused()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();
        h.Time.Advance(TimeSpan.FromSeconds(30)); // 5 s already passed in ChallengedAsync

        var result = await h.LiveAsync(id, challenge.ChallengeToken);

        Assert.False(result.Succeeded);
        Assert.Equal(VerificationSessionService.ChallengeExpiredMessage, result.Error);
        Assert.Empty(h.Faces.LivenessCalls);
    }

    [Fact]
    public async Task AChallengeBelongsToItsSession_AnotherSessionsTokenDoesNotWork()
    {
        var h = new Harness();
        var (idA, challengeA) = await h.ChallengedAsync(adminId: 7);
        var (idB, _) = await h.ChallengedAsync(adminId: 8);

        var crossed = await h.LiveAsync(idB, challengeA.ChallengeToken, adminId: 8);

        Assert.False(crossed.Succeeded);
        Assert.Empty(h.Faces.LivenessCalls);
        Assert.True((await h.LiveAsync(idA, challengeA.ChallengeToken, adminId: 7)).Succeeded);
    }

    [Fact]
    public async Task AnotherAdmin_CannotUseSomeoneElsesSessionOrToken()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync(adminId: 7);

        var result = await h.LiveAsync(id, challenge.ChallengeToken, adminId: 8);

        Assert.False(result.Succeeded);
        Assert.Empty(h.Faces.LivenessCalls);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    public async Task TheFrameCountIsBounded_AndATokenIsNotSpentOnAnObviouslyBadRequest(int frames)
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();

        var result = await h.Service.LiveVerifyAsync(id, Admin, Harness.Burst(challenge.ChallengeToken, count: frames));

        Assert.False(result.Succeeded);
        Assert.Contains("between 6 and 16", result.Error);
        Assert.True((await h.LiveAsync(id, challenge.ChallengeToken)).Succeeded);
    }

    [Fact]
    public async Task ACaptureThatArrivesTooSoon_CannotHaveFollowedThePrompts()
    {
        var h = new Harness();
        var session = await h.StartAsync();
        var challenge = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;
        h.Time.Advance(TimeSpan.FromSeconds(0.5));

        var result = (await h.LiveAsync(session.SessionId, challenge.ChallengeToken)).Value!;

        Assert.Equal("LivenessFailed", result.Outcome);
        Assert.Contains("TOO_FAST", result.ReasonCodes);
        Assert.False(result.Verified);
    }

    [Fact]
    public async Task AnIncompleteChallenge_Fails_AndLeavesANonApprovableRecord()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            ChallengeCompleted = false, Issues = ["CHALLENGE_NOT_COMPLETED"]
        };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.Equal("LivenessFailed", result.Outcome);
        Assert.Equal(LivenessResult.Fail, result.Liveness);
        Assert.False(result.Verified);
        Assert.Contains("head movements", result.Message);
        var record = Assert.Single(h.Verifications.Verifications);
        Assert.Equal(VerificationResult.NoMatch, record.Result);
        Assert.Equal(PickupVerificationState.NotMatched, record.GetState(h.Time.UtcNow, h.Pickup.PendingExpiry));
        Assert.Equal(IdentityResult.Match, record.IdentityResult);
        Assert.Equal(LivenessResult.Fail, record.LivenessResult);
    }

    [Fact]
    public async Task SuspectedSpoofing_IsReportedAsSuch_AndNeverVerified()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            Passive = new PassiveSignals(true, 5, [0.05, 0.1, 0.02, 0.4, 0.05], 0.0, 0.124, 0.02)
        };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.Equal("LivenessFailed", result.Outcome);
        Assert.Contains("PASSIVE_SPOOF_SUSPECTED", result.ReasonCodes);
        Assert.Contains("Possible spoofing detected", result.Message);
        Assert.Equal(0.124, h.Verifications.Verifications.Single().LivenessScore!.Value, 3);
    }

    [Fact]
    public async Task AMissingAntiSpoofingModel_FailsClosed()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            Passive = new PassiveSignals(false, 0, [], null, null, null), Issues = ["PASSIVE_UNAVAILABLE"]
        };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.False(result.Verified);
        Assert.Equal(LivenessResult.Fail, result.Liveness);
    }

    [Fact]
    public async Task ADifferentFaceDuringTheCheck_Fails()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            Continuity = new ContinuitySignals(2, 0.02, false), Issues = ["FACE_CHANGED"]
        };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.False(result.Verified);
        Assert.Contains("FACE_CHANGED", result.ReasonCodes);
    }

    [Fact]
    public async Task NoMatchOnTheLiveFrame_IsNotRecognised_AndLeavesNoRecord()
    {
        var h = new Harness();
        h.Faces.NextScore = 0.08;
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.Equal("NotRecognised", result.Outcome);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task ALiveCaptureThatShowsAnotherGuardianThanTheGalleryPhoto_IsRefused()
    {
        var h = new Harness();
        var session = await h.StartAsync();
        h.Faces.ScoreByGuardian[1] = 0.9; h.Faces.NextScore = 0.05;
        await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "photo-of-priya"));
        var challenge = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;
        h.Time.Advance(TimeSpan.FromSeconds(5));
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c, 0.9, guardianId: 2); // Dev is at the camera

        var result = (await h.LiveAsync(session.SessionId, challenge.ChallengeToken)).Value!;

        Assert.False(result.Verified);
        Assert.Equal("NotRecognised", result.Outcome);
        Assert.Contains("DIFFERS_FROM_PHOTO", result.ReasonCodes);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task AGalleryMatchThenALiveCaptureOfTheSameGuardian_Passes()
    {
        var h = new Harness();
        var session = await h.StartAsync();
        await h.Service.IdentifyAsync(session.SessionId, Admin, new(CaptureSource.Gallery, "photo-of-priya"));
        var challenge = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;
        h.Time.Advance(TimeSpan.FromSeconds(5));

        var result = (await h.LiveAsync(session.SessionId, challenge.ChallengeToken)).Value!;

        Assert.True(result.Verified);
        Assert.Equal(CaptureSource.Webcam, h.Verifications.Verifications.Single().CaptureSource);
    }

    [Fact]
    public async Task ALiveGuardianWhoIsNotAuthorised_IsBlocked_AndLeavesANonApprovableRecord()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c, 0.9, guardianId: 3);
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.Equal("Unauthorised", result.Outcome);
        Assert.False(result.Verified);
        var record = Assert.Single(h.Verifications.Verifications);
        Assert.Equal(3, record.GuardianId);
        Assert.Equal(AuthorisationResult.Unauthorised, record.AuthorisationResult);
        Assert.Equal(VerificationResult.NoMatch, record.Result);
    }

    [Fact]
    public async Task AnUnusableNeutralFrame_IsARetry_WithoutARecord()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            NeutralQuality = new("verification", 1, ["TOO_DARK"], [], ["The face is too dark."], new(300, 40, 40, 200, 0, 0.99))
        };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.Equal("RetryCapture", result.Outcome);
        Assert.Equal(CheckResult.Fail, result.Quality);
        Assert.Null(result.Identity);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task NoFrontalFrame_IsARetry()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            NeutralFrameIndex = null, NeutralQuality = null, Identification = null, Issues = ["NO_NEUTRAL_FRAME"], ChallengeCompleted = false
        };
        var (id, challenge) = await h.ChallengedAsync();

        var result = (await h.LiveAsync(id, challenge.ChallengeToken)).Value!;

        Assert.Equal("RetryCapture", result.Outcome);
        Assert.Contains("straight at the camera", result.Message);
    }

    [Fact]
    public async Task ASubmittedCaptureCannotBeReplayedInAnotherSession()
    {
        var h = new Harness();
        var (id1, challenge1) = await h.ChallengedAsync();
        Assert.True((await h.LiveAsync(id1, challenge1.ChallengeToken)).Value!.Verified);

        var (id2, challenge2) = await h.ChallengedAsync();
        var replay = (await h.LiveAsync(id2, challenge2.ChallengeToken)).Value!; // the very same frames

        Assert.False(replay.Verified);
        Assert.Equal("LivenessFailed", replay.Outcome);
        Assert.Contains("REPLAYED_FRAME", replay.ReasonCodes);
        Assert.Contains("matches an earlier one", replay.Message);
    }

    [Fact]
    public async Task DifferentFrames_AreNotMistakenForAReplay()
    {
        var h = new Harness();
        var (id1, challenge1) = await h.ChallengedAsync();
        await h.LiveAsync(id1, challenge1.ChallengeToken);

        var (id2, challenge2) = await h.ChallengedAsync();
        var fresh = await h.Service.LiveVerifyAsync(id2, Admin, Harness.Burst(challenge2.ChallengeToken, prefix: "other"));

        Assert.True(fresh.Value!.Verified);
    }

    [Fact]
    public async Task TheSessionClosesAfterTooManyFailedAttempts()
    {
        var h = new Harness();
        h.Faces.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with { ChallengeCompleted = false };
        var session = await h.StartAsync();

        for (var i = 1; i <= 5; i++)
        {
            var challenge = (await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Value!;
            h.Time.Advance(TimeSpan.FromSeconds(5));
            var result = (await h.Service.LiveVerifyAsync(session.SessionId, Admin, Harness.Burst(challenge.ChallengeToken, prefix: $"try{i}"))).Value!;
            Assert.Equal(5 - i, result.AttemptsRemaining);
        }

        Assert.Equal(VerificationSessionState.Closed, h.Session(session.SessionId).State);
        Assert.False((await h.Service.IssueChallengeAsync(session.SessionId, Admin)).Succeeded);
    }

    [Fact]
    public async Task AFaceServiceOutage_IsAnErrorNotAFailedAttempt()
    {
        var h = new Harness();
        h.Faces.NextLivenessFailure = Result<LivenessAnalysis>.Failure("Face service is unavailable.");
        var (id, challenge) = await h.ChallengedAsync();

        var result = await h.LiveAsync(id, challenge.ChallengeToken);

        Assert.False(result.Succeeded);
        Assert.Equal("Face service is unavailable.", result.Error);
        Assert.Equal(0, h.Session(id).FailedLiveAttempts);
        Assert.Empty(h.Verifications.Verifications);
    }

    [Fact]
    public async Task AFinishedSession_CannotBeUsedAgain()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();
        await h.LiveAsync(id, challenge.ChallengeToken);

        Assert.False((await h.Service.IssueChallengeAsync(id, Admin)).Succeeded);
        Assert.False((await h.Service.IdentifyAsync(id, Admin, new(CaptureSource.Gallery, "img"))).Succeeded);
    }

    [Fact]
    public async Task EveryLayerOfALiveCheckIsLoggedIndependently_WithoutImagesOrTokens()
    {
        var h = new Harness();
        var (id, challenge) = await h.ChallengedAsync();
        await h.LiveAsync(id, challenge.ChallengeToken);

        foreach (var layer in new[] { FaceAuditLayer.Quality, FaceAuditLayer.Detection, FaceAuditLayer.Identity, FaceAuditLayer.Authorisation, FaceAuditLayer.Liveness, FaceAuditLayer.Decision })
            Assert.Contains(h.Audit.Entries, e => e.Layer == layer && e.Action == "LiveVerify");
        Assert.All(h.Audit.Entries, e =>
        {
            Assert.DoesNotContain("frame-", e.Detail ?? "");
            Assert.DoesNotContain(challenge.ChallengeToken, e.Detail ?? "");
        });
    }
}
