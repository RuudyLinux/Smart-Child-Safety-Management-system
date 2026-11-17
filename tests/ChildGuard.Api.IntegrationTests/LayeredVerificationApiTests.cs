using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Api.IntegrationTests;

public sealed class LivenessRequiredFactory : ApiFactory
{
    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["Verification__RequireLiveness"] = "true";
            return settings;
        }
    }
}

public sealed class TightLiveLimitFactory : ApiFactory
{
    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimiting__LiveVerifyPerMinute"] = "3";
            return settings;
        }
    }
}

/// <summary>Layered desk verification end to end against the real API and database (face service faked).</summary>
public class LayeredVerificationApiTests : IClassFixture<LivenessRequiredFactory>
{
    private readonly LivenessRequiredFactory _api;
    public LayeredVerificationApiTests(LivenessRequiredFactory api) => _api = api;

    private static LiveVerifyRequest Burst(string token, string prefix, int count = 10) =>
        new(token, Enumerable.Range(0, count).Select(i => new LiveFrameDto($"{prefix}-{i}", i * 400)).ToList());

    private async Task<(HttpClient Admin, Child Child, VerificationSessionDto Session)> OpenAsync()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(FakeFaceServiceClient.Clean);
        _api.Face.NextIdentifyScore = 0.93;
        _api.Face.ScoreByGuardian.Clear();
        _api.Face.LivenessFactory = null;
        _api.Face.NextLivenessFailure = null;
        var response = await admin.PostAsJsonAsync("/api/verification/sessions", new StartVerificationSessionRequest(child.StudentId), Json.Options);
        response.EnsureSuccessStatusCode();
        return (admin, child, (await response.Content.ReadFromJsonAsync<VerificationSessionDto>(Json.Options))!);
    }

    private async Task<LiveChallengeDto> ChallengeAsync(HttpClient admin, int sessionId)
    {
        var response = await admin.PostAsync($"/api/verification/sessions/{sessionId}/challenge", null);
        response.EnsureSuccessStatusCode();
        var challenge = (await response.Content.ReadFromJsonAsync<LiveChallengeDto>(Json.Options))!;
        _api.Time.Advance(TimeSpan.FromSeconds(5));
        return challenge;
    }

    private static Task<HttpResponseMessage> LiveAsync(HttpClient admin, int sessionId, string token, string prefix) =>
        admin.PostAsJsonAsync($"/api/verification/sessions/{sessionId}/live-verify", Burst(token, prefix), Json.Options);

    private static async Task<VerificationAttemptResultDto> Body(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<VerificationAttemptResultDto>(Json.Options))!;

    // ---------------------------------------------------------------- session

    [Fact]
    public async Task StartingASession_ListsTheStudentsAuthorisedGuardians()
    {
        var (_, child, session) = await OpenAsync();

        Assert.Equal(child.StudentId, session.StudentId);
        var guardian = Assert.Single(session.Guardians);
        Assert.Equal(child.GuardianId, guardian.GuardianId);
        Assert.True(guardian.FaceRegistered);
    }

    [Fact]
    public async Task Sessions_RequireAnAdmin_AndAnUnknownStudentIsNotFound()
    {
        var child = await _api.NewChildAsync();
        var anonymous = await _api.CreateClient().PostAsJsonAsync("/api/verification/sessions", new StartVerificationSessionRequest(child.StudentId), Json.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var asGuardian = await _api.GuardianClient(child.GuardianId).PostAsJsonAsync("/api/verification/sessions", new StartVerificationSessionRequest(child.StudentId), Json.Options);
        Assert.Contains(asGuardian.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });

        var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/verification/sessions", new StartVerificationSessionRequest(999999), Json.Options)).StatusCode);
    }

    // --------------------------------------------------------- gallery: never live

    [Fact]
    public async Task AGalleryImage_Identifies_ButNeverCreatesAnApprovableVerification()
    {
        var (admin, child, session) = await OpenAsync();

        var response = await admin.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/identify",
            new SessionIdentifyRequest(CaptureSource.Gallery, "aW1hZ2U="), Json.Options);

        response.EnsureSuccessStatusCode();
        var body = await Body(response);
        Assert.Equal("LiveVerificationRequired", body.Outcome);
        Assert.False(body.Verified);
        Assert.Equal(LivenessResult.NotPerformed, body.Liveness);
        Assert.Equal(IdentityResult.Match, body.Identity);
        Assert.Equal(AuthorisationResult.Authorised, body.Authorisation);
        Assert.Equal(child.GuardianId, body.Guardian!.GuardianId);
        Assert.Null(body.VerificationId);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.GuardianVerifications.CountAsync(v => v.StudentId == child.StudentId));
    }

    [Fact]
    public async Task ALiveCheck_AfterAGalleryMatch_IsStillRequired_AndTheGuardianMustBeTheSame()
    {
        var (admin, child, session) = await OpenAsync();
        await admin.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/identify", new SessionIdentifyRequest(CaptureSource.Gallery, "aW1hZ2U="), Json.Options);
        var challenge = await ChallengeAsync(admin, session.SessionId);

        var body = await Body(await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "a"));

        Assert.True(body.Verified);
        Assert.Equal(LivenessResult.Pass, body.Liveness);
        Assert.NotNull(body.VerificationId);
    }

    // ----------------------------------------------------------- live: full pass

    [Fact]
    public async Task AFullPass_CreatesAPendingVerification_WithEveryLayerRecorded_AndAnAdminApprovesIt()
    {
        var (admin, child, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);

        var body = await Body(await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "full"));

        Assert.True(body.Verified);
        Assert.Equal("PickupEligible", body.Outcome);
        await using (var db = _api.NewDb())
        {
            var record = await db.GuardianVerifications.SingleAsync(v => v.Id == body.VerificationId);
            Assert.Equal(VerificationResult.Match, record.Result);
            Assert.False(record.PickupApproved);
            Assert.Equal(CaptureSource.Webcam, record.CaptureSource);
            Assert.Equal(CheckResult.Pass, record.QualityResult);
            Assert.Equal(CheckResult.Pass, record.FaceDetectionResult);
            Assert.Equal(IdentityResult.Match, record.IdentityResult);
            Assert.Equal(AuthorisationResult.Authorised, record.AuthorisationResult);
            Assert.Equal(LivenessResult.Pass, record.LivenessResult);
            Assert.Equal(session.SessionId, record.SessionId);
            Assert.NotNull(record.LivenessScore);
            Assert.Equal(0, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
        }

        // Nothing happened until the administrator decided.
        var approve = await admin.ApproveAsync(body.VerificationId!.Value);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        await using var after = _api.NewDb();
        Assert.Equal(1, await after.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
    }

    [Fact]
    public async Task TheStoredChallenge_IsAHash_NotTheToken()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);

        await using var db = _api.NewDb();
        var stored = await db.VerificationSessions.SingleAsync(s => s.Id == session.SessionId);
        Assert.NotEqual(challenge.ChallengeToken, stored.ChallengeTokenHash);
        Assert.Equal(64, stored.ChallengeTokenHash!.Length);
        Assert.DoesNotContain(challenge.ChallengeToken, stored.ChallengeTokenHash);
        Assert.Empty(challenge.Steps); // no head turn is asked by default (FaceVerification:RequireHeadTurn is off)
    }

    [Fact]
    public async Task TheChallengeRemoved_NeverSentByTheClient_IsWhatTheFaceServiceGets()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);
        _api.Face.LivenessSteps.Clear();

        await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "steps");

        Assert.Equal(challenge.Steps, _api.Face.LivenessSteps.Single());
    }

    // ------------------------------------------------------------ token rules

    [Fact]
    public async Task ATokenWorksOnce()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);
        Assert.Equal(HttpStatusCode.OK, (await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "once-a")).StatusCode);

        var again = await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "once-b");

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task TwoSimultaneousSubmissionsOfOneToken_LetExactlyOneThrough()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);

        var responses = await TestData.Simultaneously(2, i => LiveAsync(admin, session.SessionId, challenge.ChallengeToken, $"race{i}"));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.GuardianVerifications.CountAsync(v => v.SessionId == session.SessionId));
    }

    [Fact]
    public async Task AnExpiredChallenge_IsRefused()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);
        _api.Time.Advance(TimeSpan.FromSeconds(31));

        var response = await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "late");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await response.MessageAsync());
    }

    [Fact]
    public async Task AMadeUpToken_IsRefused()
    {
        var (admin, _, session) = await OpenAsync();
        await ChallengeAsync(admin, session.SessionId);

        Assert.Equal(HttpStatusCode.BadRequest, (await LiveAsync(admin, session.SessionId, "not-the-token", "fake")).StatusCode);
    }

    [Fact]
    public async Task ACapturedBurst_CannotBeReplayedInANewSession()
    {
        var (admin, child, first) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, first.SessionId);
        Assert.True((await Body(await LiveAsync(admin, first.SessionId, challenge.ChallengeToken, "capture"))).Verified);

        var second = (await (await admin.PostAsJsonAsync("/api/verification/sessions", new StartVerificationSessionRequest(child.StudentId), Json.Options))
            .Content.ReadFromJsonAsync<VerificationSessionDto>(Json.Options))!;
        var challenge2 = await ChallengeAsync(admin, second.SessionId);
        var replay = await Body(await LiveAsync(admin, second.SessionId, challenge2.ChallengeToken, "capture")); // same frames

        Assert.False(replay.Verified);
        Assert.Contains("REPLAYED_FRAME", replay.ReasonCodes);
    }

    [Fact]
    public async Task AnotherAdmin_CannotUseThisAdminsSessionOrToken()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);

        int otherId;
        await using (var db = _api.NewDb())
        {
            var other = new Admin { FullName = "Other Admin", Email = $"other{Guid.NewGuid():N}@childguard.test", PasswordHash = "x" };
            db.Admins.Add(other);
            await db.SaveChangesAsync();
            otherId = other.AdminId;
        }
        using var scope = _api.Services.CreateScope();
        var (token, _) = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>().GenerateToken(otherId, "o@x.test", "Other Admin", AppRole.Admin);
        var otherClient = _api.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var stolen = await LiveAsync(otherClient, session.SessionId, challenge.ChallengeToken, "stolen");
        var identify = await otherClient.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/identify", new SessionIdentifyRequest(CaptureSource.Gallery, "aW1hZ2U="), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, identify.StatusCode);
        // The owner can still use their token: the other admin's attempt did not spend it.
        Assert.Equal(HttpStatusCode.OK, (await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "owner")).StatusCode);
    }

    // --------------------------------------------------- failures leave nothing approvable

    [Fact]
    public async Task SuspectedSpoofing_IsNotVerified_AndLeavesARecordThatCanOnlyBeRejected()
    {
        var (admin, child, session) = await OpenAsync();
        _api.Face.LivenessFactory = (steps, c) => FakeFaceServiceClient.PassingLiveness(steps, c) with
        {
            Passive = new PassiveSignals(true, 5, [0.04, 0.1, 0.02, 0.3, 0.05], 0.0, 0.102, 0.02)
        };
        var challenge = await ChallengeAsync(admin, session.SessionId);

        var body = await Body(await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "spoof"));

        Assert.False(body.Verified);
        Assert.Equal(LivenessResult.Fail, body.Liveness);
        Assert.Contains("Possible spoofing detected", body.Message);
        var approve = await admin.ApproveAsync(body.VerificationId!.Value);
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
        Assert.Equal(200, (int)(await admin.RejectAsync(body.VerificationId.Value)).StatusCode);
    }

    [Fact]
    public async Task AFaceServiceOutage_ProducesNoRecord()
    {
        var (admin, child, session) = await OpenAsync();
        _api.Face.NextLivenessFailure = Result<LivenessAnalysis>.Failure("Face service is unavailable.");
        var challenge = await ChallengeAsync(admin, session.SessionId);

        var response = await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "down");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.GuardianVerifications.CountAsync(v => v.StudentId == child.StudentId));
    }

    [Fact]
    public async Task AnUnauthorisedGuardian_CannotBeVerified()
    {
        var (admin, child, session) = await OpenAsync();
        await using (var db = _api.NewDb())
        {
            var link = await db.StudentGuardians.SingleAsync(l => l.StudentId == child.StudentId);
            link.IsAuthorizedForPickup = false;
            await db.SaveChangesAsync();
        }
        var response = await admin.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/identify", new SessionIdentifyRequest(CaptureSource.Gallery, "aW1hZ2U="), Json.Options);

        var body = await Body(response);
        Assert.Equal("Unauthorised", body.Outcome);
        Assert.Contains("not authorised", body.Message);
    }

    // ------------------------------------------- the original endpoint cannot bypass it

    [Fact]
    public async Task WhileLivenessIsRequired_TheOriginalFaceVerifyPath_CannotBeApproved()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var legacyId = await admin.VerifyFaceAsync(_api, child);

        var approve = await admin.ApproveAsync(legacyId);

        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        Assert.Equal(ChildGuard.Application.Services.VerificationService.LivenessRequiredMessage, await approve.MessageAsync());
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
        Assert.Equal(HttpStatusCode.OK, (await admin.RejectAsync(legacyId)).StatusCode);
    }

    // ------------------------------------------------------------ audit trail

    [Fact]
    public async Task EveryLayerOfTheLiveCheck_IsAuditedIndependently()
    {
        var (admin, child, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);
        await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "audit");

        await using var db = _api.NewDb();
        var rows = await db.FaceAuditLogs.Where(l => l.SessionId == session.SessionId).ToListAsync();
        foreach (var layer in new[] { FaceAuditLayer.Quality, FaceAuditLayer.Detection, FaceAuditLayer.Identity, FaceAuditLayer.Authorisation, FaceAuditLayer.Liveness, FaceAuditLayer.Decision })
            Assert.Contains(rows, r => r.Layer == layer && r.Action == "LiveVerify");
        Assert.All(rows, r => Assert.Equal("Admin", r.ActorRole));
        Assert.All(rows, r => Assert.DoesNotContain(challenge.ChallengeToken, r.Detail ?? ""));
        Assert.All(rows, r => Assert.DoesNotContain("audit-", r.Detail ?? ""));
    }

    // -------------------------------------------------------------- input limits

    [Fact]
    public async Task AnOversizedFrame_IsRejectedByValidation()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);
        var frames = Enumerable.Range(0, 8).Select(i => new LiveFrameDto(new string('A', 700_001), i * 400)).ToList();

        var response = await admin.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/live-verify", new LiveVerifyRequest(challenge.ChallengeToken, frames), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnAbusiveBurst_IsRefusedByValidation_BeforeAnyFrameReachesTheFaceService()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);
        var frames = Enumerable.Range(0, 8).Select(i => new LiveFrameDto(new string('A', 650_000), i * 400)).ToList(); // ~5 MB of base64 in total
        _api.Face.LivenessFrames.Clear();

        var response = await admin.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/live-verify", new LiveVerifyRequest(challenge.ChallengeToken, frames), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("too large in total", await response.Content.ReadAsStringAsync());
        Assert.Empty(_api.Face.LivenessFrames);
    }

    [Fact]
    public void TheLiveEndpoints_CarryKestrelsRequestSizeCaps()
    {
        // The in-process test host does not enforce [RequestSizeLimit]; Kestrel does in production, so assert it is declared.
        var controller = typeof(ChildGuard.Api.Controllers.VerificationController);
        long? CapOf(string method) => ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)Attribute.GetCustomAttribute(
            controller.GetMethod(method)!, typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute))!).MaxRequestBodySize;

        Assert.Equal(ChildGuard.Api.Controllers.FaceUploads.LiveBurstRequestLimit, CapOf("LiveVerify"));
        Assert.Equal(ChildGuard.Api.Controllers.FaceUploads.SingleImageRequestLimit, CapOf("IdentifyInSession"));
        Assert.Equal(ChildGuard.Api.Controllers.FaceUploads.SingleImageRequestLimit, CapOf("AnalyzeImage"));
    }

    [Fact]
    public async Task TooFewOrTooManyFrames_AreRefused_WithoutSpendingTheToken()
    {
        var (admin, _, session) = await OpenAsync();
        var challenge = await ChallengeAsync(admin, session.SessionId);

        var few = await admin.PostAsJsonAsync($"/api/verification/sessions/{session.SessionId}/live-verify", Burst(challenge.ChallengeToken, "few", 3), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, few.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LiveAsync(admin, session.SessionId, challenge.ChallengeToken, "ok")).StatusCode);
    }
}

public class LiveVerifyRateLimitTests : IClassFixture<TightLiveLimitFactory>
{
    private readonly TightLiveLimitFactory _api;
    public LiveVerifyRateLimitTests(TightLiveLimitFactory api) => _api = api;

    [Fact]
    public async Task LiveChecksAreMeteredPerAdmin()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var session = (await (await admin.PostAsJsonAsync("/api/verification/sessions", new StartVerificationSessionRequest(child.StudentId), Json.Options))
            .Content.ReadFromJsonAsync<VerificationSessionDto>(Json.Options))!;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await admin.PostAsync($"/api/verification/sessions/{session.SessionId}/challenge", null)).StatusCode);

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
