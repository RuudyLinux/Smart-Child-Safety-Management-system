using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services.FaceVerification;

public sealed record LivenessAssessment(LivenessResult Result, double? Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Decides whether a live person was shown. Everything has to hold: the requested head movements were
/// made in order, the passive anti-spoofing model called enough of the sampled frames real, the same face
/// stayed in view, the capture was not a repeat and arrived in a plausible time. Any problem the face
/// service reports blocks, and anything that could not be measured counts as not passed.
/// </summary>
public static class LivenessPolicy
{
    public static LivenessAssessment Assess(LivenessAnalysis a, FaceVerificationOptions options, bool replayedCapture, bool arrivedTooFast)
    {
        var reasons = new List<string>(a.Issues);

        if (!a.ChallengeCompleted) reasons.Add("CHALLENGE_NOT_COMPLETED");
        if (!a.Passive.Available)
            reasons.Add("PASSIVE_UNAVAILABLE");
        else if (a.Passive.RealFraction is null || a.Passive.Mean is null
                 || a.Passive.RealFraction < options.PassiveMinRealFraction || a.Passive.Mean < options.PassiveMinMeanReal)
            reasons.Add("PASSIVE_SPOOF_SUSPECTED");
        // Continuity is only measurable once the head turns happened; without them it is the same problem, not a second one.
        if (a.Continuity.SamePerson is false && !reasons.Contains("FACE_CHANGED"))
            reasons.Add("FACE_CHANGED");
        else if (a.Continuity.SamePerson is null && a.ChallengeCompleted)
            reasons.Add("CONTINUITY_UNKNOWN");
        if (replayedCapture) reasons.Add("REPLAYED_FRAME");
        if (arrivedTooFast) reasons.Add("TOO_FAST");

        var distinct = reasons.Distinct().ToList();
        return new(distinct.Count == 0 ? LivenessResult.Pass : LivenessResult.Fail, a.Passive.Mean, distinct);
    }
}
