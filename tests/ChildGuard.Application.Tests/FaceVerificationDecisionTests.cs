using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services.FaceVerification;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

public class VerificationDecisionEngineTests
{
    private static DecisionInput Good(
        CaptureSource source = CaptureSource.Webcam, CheckResult quality = CheckResult.Pass, CheckResult detection = CheckResult.Pass,
        IdentityResult? identity = IdentityResult.Match, AuthorisationResult authorisation = AuthorisationResult.Authorised,
        LivenessResult liveness = LivenessResult.Pass) =>
        new(source, quality, detection, identity, authorisation, liveness);

    [Fact]
    public void OnlyAFullWebcamPass_IsEligible_AcrossEveryCombinationOfLayerResults()
    {
        var checkedCombinations = 0;
        var eligible = new List<DecisionInput>();
        foreach (var source in Enum.GetValues<CaptureSource>())
        foreach (var quality in Enum.GetValues<CheckResult>())
        foreach (var detection in Enum.GetValues<CheckResult>())
        foreach (var identity in new IdentityResult?[] { null, IdentityResult.Match, IdentityResult.NoMatch, IdentityResult.Inconclusive })
        foreach (var authorisation in Enum.GetValues<AuthorisationResult>())
        foreach (var liveness in Enum.GetValues<LivenessResult>())
        {
            var input = new DecisionInput(source, quality, detection, identity, authorisation, liveness);
            var decision = VerificationDecisionEngine.Decide(input);
            checkedCombinations++;
            if (decision.PickupEligible) eligible.Add(input);

            var allPass = source == CaptureSource.Webcam && quality == CheckResult.Pass && detection == CheckResult.Pass
                && identity == IdentityResult.Match && authorisation == AuthorisationResult.Authorised && liveness == LivenessResult.Pass;
            Assert.Equal(allPass, decision.PickupEligible);
            Assert.Equal(allPass, decision.NextStep == VerificationDecisionEngine.NextApprove);
        }

        Assert.Equal(2 * 2 * 2 * 4 * 3 * 3, checkedCombinations);
        Assert.Single(eligible);
    }

    [Fact]
    public void AGalleryImage_IsNeverEligible_EvenWhenLivenessIsClaimedAsPassed()
    {
        var decision = VerificationDecisionEngine.Decide(Good(source: CaptureSource.Gallery, liveness: LivenessResult.Pass));
        Assert.False(decision.PickupEligible);
        Assert.Equal(DecisionOutcome.LiveVerificationRequired, decision.Outcome);
        Assert.Equal(VerificationDecisionEngine.NextLiveVerification, decision.NextStep);
    }

    [Fact]
    public void NotPerformed_IsNeverReadAsAPass()
    {
        var decision = VerificationDecisionEngine.Decide(Good(liveness: LivenessResult.NotPerformed));
        Assert.False(decision.PickupEligible);
        Assert.Equal(DecisionOutcome.LiveVerificationRequired, decision.Outcome);
    }

    [Fact]
    public void AStrongIdentityMatch_DoesNotExcuseAFailedLivenessCheck()
    {
        Assert.Equal(DecisionOutcome.LivenessFailed, VerificationDecisionEngine.Decide(Good(liveness: LivenessResult.Fail)).Outcome);
    }

    [Theory]
    [InlineData(CheckResult.Fail, CheckResult.Pass)]
    [InlineData(CheckResult.Pass, CheckResult.Fail)]
    [InlineData(CheckResult.Fail, CheckResult.Fail)]
    public void AnUnusableImage_IsARetry_WhateverElseIsTrue(CheckResult quality, CheckResult detection)
    {
        var decision = VerificationDecisionEngine.Decide(Good(quality: quality, detection: detection));
        Assert.Equal(DecisionOutcome.RetryCapture, decision.Outcome);
        Assert.Equal(VerificationDecisionEngine.NextRetryCapture, decision.NextStep);
    }

    [Fact]
    public void Inconclusive_BlocksAutomaticProgression_AndNeedsReview()
    {
        var decision = VerificationDecisionEngine.Decide(Good(identity: IdentityResult.Inconclusive));
        Assert.Equal(DecisionOutcome.ReviewRequired, decision.Outcome);
        Assert.Equal(VerificationDecisionEngine.NextAdminReview, decision.NextStep);
        Assert.False(decision.PickupEligible);
    }

    [Fact]
    public void NoMatch_IsBlocked()
    {
        var decision = VerificationDecisionEngine.Decide(Good(identity: IdentityResult.NoMatch, authorisation: AuthorisationResult.NotEvaluated));
        Assert.Equal(DecisionOutcome.NotRecognised, decision.Outcome);
        Assert.Equal(VerificationDecisionEngine.NextBlocked, decision.NextStep);
    }

    [Theory]
    [InlineData(AuthorisationResult.Unauthorised)]
    [InlineData(AuthorisationResult.NotEvaluated)]
    public void AMatchedGuardianWhoIsNotAuthorised_IsBlocked(AuthorisationResult authorisation)
    {
        Assert.Equal(DecisionOutcome.Unauthorised, VerificationDecisionEngine.Decide(Good(authorisation: authorisation)).Outcome);
    }
}

public class IdentityPolicyTests
{
    private static FaceIdentification Ids(double threshold, params (int guardian, double? score)[] results) =>
        new(threshold, "m", results.Select(r => new GuardianIdentification(
            r.guardian, r.score, r.score is null ? null : 0, [], [])).ToList());

    [Fact]
    public void ScoreAtTheThreshold_IsAMatch_JustBelowIsNot()
    {
        Assert.Equal(IdentityResult.Match, IdentityPolicy.Assess(Ids(0.40, (1, 0.40)), 0).Result);
        Assert.Equal(IdentityResult.NoMatch, IdentityPolicy.Assess(Ids(0.40, (1, 0.3999)), 0).Result);
    }

    [Fact]
    public void TheThresholdIsTheFaceServicesOwn()
    {
        Assert.Equal(IdentityResult.NoMatch, IdentityPolicy.Assess(Ids(0.55, (1, 0.50)), 0).Result);
        Assert.Equal(0.55, IdentityPolicy.Assess(Ids(0.55, (1, 0.50)), 0).Threshold);
    }

    [Fact]
    public void TheBestGuardianWins_AndTheScoreAndReferenceAreReported()
    {
        var a = IdentityPolicy.Assess(new FaceIdentification(0.4, "m", [
            new GuardianIdentification(1, 0.55, 3, [], []),
            new GuardianIdentification(2, 0.91, 0, [], [])]), 0);
        Assert.Equal(IdentityResult.Match, a.Result);
        Assert.Equal(2, a.GuardianId);
        Assert.Equal(0.91, a.Score);
        Assert.Equal(0, a.BestReferenceId);
    }

    [Fact]
    public void ACloseSecondGuardian_IsInconclusiveOnlyWhenTheMarginIsSet()
    {
        var close = Ids(0.40, (1, 0.80), (2, 0.78));
        Assert.Equal(IdentityResult.Match, IdentityPolicy.Assess(close, ambiguityMargin: 0).Result);
        Assert.Equal(IdentityResult.Inconclusive, IdentityPolicy.Assess(close, ambiguityMargin: 0.05).Result);
        Assert.Equal(IdentityResult.Match, IdentityPolicy.Assess(Ids(0.40, (1, 0.80), (2, 0.60)), 0.05).Result);
    }

    [Fact]
    public void ASecondGuardianBelowTheThreshold_NeverMakesItInconclusive()
    {
        Assert.Equal(IdentityResult.Match, IdentityPolicy.Assess(Ids(0.40, (1, 0.42), (2, 0.39)), 0.10).Result);
    }

    [Fact]
    public void TheMarginCanNeverTurnANoMatchIntoAMatch()
    {
        Assert.Equal(IdentityResult.NoMatch, IdentityPolicy.Assess(Ids(0.40, (1, 0.35), (2, 0.34)), 0.50).Result);
    }

    [Fact]
    public void NothingReadable_IsNoMatch()
    {
        var a = IdentityPolicy.Assess(Ids(0.40, (1, null), (2, null)), 0);
        Assert.Equal(IdentityResult.NoMatch, a.Result);
        Assert.Contains("NO_READABLE_REFERENCE", a.Reasons);
        Assert.Equal(IdentityResult.NoMatch, IdentityPolicy.Assess(Ids(0.40), 0).Result);
    }

    [Theory]
    [InlineData(0.95, "Strong")]
    [InlineData(0.60, "Moderate")]
    [InlineData(0.42, "Borderline")]
    [InlineData(0.10, "Below threshold")]
    public void TheConfidenceBand_IsInformationOnly(double score, string band)
    {
        Assert.Equal(band, IdentityPolicy.ConfidenceBand(score, 0.40));
    }
}

public class LivenessPolicyTests
{
    private readonly FaceVerificationOptions _options = new();

    private static LivenessAnalysis Passing() => ChildGuard.Application.Tests.Fakes.FakeFaceServiceClient.PassingLiveness(["turn_left", "turn_right"], null);

    [Fact]
    public void EverythingHolding_IsAPass_AndTheScoreIsReported()
    {
        var result = LivenessPolicy.Assess(Passing(), _options, false, false);
        Assert.Equal(LivenessResult.Pass, result.Result);
        Assert.Empty(result.Reasons);
        Assert.Equal(0.972, result.Score);
    }

    [Fact]
    public void AnIncompleteChallenge_Fails()
    {
        var result = LivenessPolicy.Assess(Passing() with { ChallengeCompleted = false }, _options, false, false);
        Assert.Equal(LivenessResult.Fail, result.Result);
        Assert.Contains("CHALLENGE_NOT_COMPLETED", result.Reasons);
    }

    [Theory]
    [InlineData(0.79, 0.97)]
    [InlineData(1.0, 0.59)]
    public void WeakPassiveScores_AreSuspectedSpoofing(double realFraction, double mean)
    {
        var a = Passing() with { Passive = new PassiveSignals(true, 5, [], realFraction, mean, 0.1) };
        var result = LivenessPolicy.Assess(a, _options, false, false);
        Assert.Equal(LivenessResult.Fail, result.Result);
        Assert.Contains("PASSIVE_SPOOF_SUSPECTED", result.Reasons);
    }

    [Fact]
    public void ThePassiveThresholdsAreInclusive()
    {
        var a = Passing() with { Passive = new PassiveSignals(true, 5, [], 0.8, 0.6, 0.5) };
        Assert.Equal(LivenessResult.Pass, LivenessPolicy.Assess(a, _options, false, false).Result);
    }

    [Fact]
    public void AMissingAntiSpoofingModel_NeverPasses()
    {
        var a = Passing() with { Passive = new PassiveSignals(false, 0, [], null, null, null), Issues = ["PASSIVE_UNAVAILABLE"] };
        var result = LivenessPolicy.Assess(a, _options, false, false);
        Assert.Equal(LivenessResult.Fail, result.Result);
        Assert.Contains("PASSIVE_UNAVAILABLE", result.Reasons);
    }

    [Fact]
    public void AChangedFace_Fails()
    {
        var result = LivenessPolicy.Assess(Passing() with { Continuity = new ContinuitySignals(2, 0.05, false) }, _options, false, false);
        Assert.Contains("FACE_CHANGED", result.Reasons);
        Assert.Equal(LivenessResult.Fail, result.Result);
    }

    [Fact]
    public void ContinuityThatCouldNotBeChecked_IsNotAPass()
    {
        var result = LivenessPolicy.Assess(Passing() with { Continuity = new ContinuitySignals(0, null, null) }, _options, false, false);
        Assert.Equal(LivenessResult.Fail, result.Result);
        Assert.Contains("CONTINUITY_UNKNOWN", result.Reasons);
    }

    [Fact]
    public void WithoutTheTurns_ContinuityIsNotReportedAsASecondProblem_ButItStillFails()
    {
        var result = LivenessPolicy.Assess(Passing() with { ChallengeCompleted = false, Continuity = new ContinuitySignals(0, null, null) }, _options, false, false);
        Assert.Equal(LivenessResult.Fail, result.Result);
        Assert.Equal(["CHALLENGE_NOT_COMPLETED"], result.Reasons);
    }

    [Theory]
    [InlineData("FACE_LOST")]
    [InlineData("DUPLICATE_FRAMES")]
    [InlineData("BAD_TIMING")]
    [InlineData("WRONG_DIRECTION")]
    [InlineData("EMBEDDING_FAILED")]
    [InlineData("SOMETHING_NEW")]
    public void AnyIssueTheFaceServiceReports_Blocks(string issue)
    {
        var result = LivenessPolicy.Assess(Passing() with { Issues = [issue] }, _options, false, false);
        Assert.Equal(LivenessResult.Fail, result.Result);
        Assert.Contains(issue, result.Reasons);
    }

    [Fact]
    public void AReplayedCaptureAndATooFastOne_Fail()
    {
        Assert.Contains("REPLAYED_FRAME", LivenessPolicy.Assess(Passing(), _options, true, false).Reasons);
        Assert.Contains("TOO_FAST", LivenessPolicy.Assess(Passing(), _options, false, true).Reasons);
    }
}
