using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services.FaceVerification;

public enum DecisionOutcome
{
    /// <summary>Every mandatory layer passed. A pending verification may be created; an administrator still decides.</summary>
    PickupEligible = 1,
    /// <summary>The image was not usable: retake it.</summary>
    RetryCapture = 2,
    NotRecognised = 3,
    /// <summary>Identity could not be settled either way. Never approvable; retry or an administrator reviews.</summary>
    ReviewRequired = 4,
    Unauthorised = 5,
    /// <summary>Identity and authorisation passed but no live person was shown (a gallery image, or liveness not run).</summary>
    LiveVerificationRequired = 6,
    LivenessFailed = 7
}

/// <param name="Identity">Null when identity was not evaluated because the image was unusable.</param>
public sealed record DecisionInput(
    CaptureSource Source, CheckResult Quality, CheckResult FaceDetection, IdentityResult? Identity,
    AuthorisationResult Authorisation, LivenessResult Liveness, bool LivenessRequired = true);

public sealed record VerificationDecision(DecisionOutcome Outcome, string NextStep)
{
    public bool PickupEligible => Outcome == DecisionOutcome.PickupEligible;
}

/// <summary>
/// The one place that says whether a pickup may proceed to administrator approval. Layers are judged
/// independently and a strong result in one never compensates for another: a high match score does
/// not excuse a missing liveness check, and nothing here ever approves a pickup.
/// </summary>
public static class VerificationDecisionEngine
{
    public const string NextApprove = "Approve";
    public const string NextRetryCapture = "RetryCapture";
    public const string NextLiveVerification = "LiveVerificationRequired";
    public const string NextAdminReview = "AdminReview";
    public const string NextBlocked = "Blocked";

    public static VerificationDecision Decide(DecisionInput input)
    {
        if (input.Quality != CheckResult.Pass || input.FaceDetection != CheckResult.Pass)
            return new(DecisionOutcome.RetryCapture, NextRetryCapture);

        switch (input.Identity)
        {
            case IdentityResult.Match:
                break;
            case IdentityResult.Inconclusive:
                return new(DecisionOutcome.ReviewRequired, NextAdminReview);
            default:
                return new(DecisionOutcome.NotRecognised, NextBlocked);
        }

        if (input.Authorisation != AuthorisationResult.Authorised)
            return new(DecisionOutcome.Unauthorised, NextBlocked);

        // A gallery file can show who someone is, never that they are there: whatever liveness
        // value arrives with it, it counts as not performed.
        var liveness = input.Source == CaptureSource.Gallery ? LivenessResult.NotPerformed : input.Liveness;
        return liveness switch
        {
            LivenessResult.Pass => new(DecisionOutcome.PickupEligible, NextApprove),
            // A live check that was run and failed always blocks, whatever the switch says.
            LivenessResult.Fail => new(DecisionOutcome.LivenessFailed, NextBlocked),
            // Not performed: a pass only when the school does not require a live-person check (Verification:RequireLiveness off).
            _ => input.LivenessRequired
                ? new(DecisionOutcome.LiveVerificationRequired, NextLiveVerification)
                : new(DecisionOutcome.PickupEligible, NextApprove)
        };
    }
}
