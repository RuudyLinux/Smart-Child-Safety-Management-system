using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services.FaceVerification;

public sealed record IdentityAssessment(
    IdentityResult Result, int? GuardianId, double? Score, int? BestReferenceId, double Threshold,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Turns the face service's scores into an identity result. The threshold is the face service's own
/// (never redefined here); a score at or above it is a match, below it is not, and the only extra
/// outcome is Inconclusive when two guardians tie closely, which can never make a non-match a match.
/// </summary>
public static class IdentityPolicy
{
    public static IdentityAssessment Assess(FaceIdentification identification, double ambiguityMargin)
    {
        var scored = identification.Results.Where(r => r.BestScore is not null).OrderByDescending(r => r.BestScore).ToList();
        if (scored.Count == 0)
            return new(IdentityResult.NoMatch, null, null, null, identification.Threshold, ["NO_READABLE_REFERENCE"]);

        var top = scored[0];
        if (top.BestScore < identification.Threshold)
            return new(IdentityResult.NoMatch, null, top.BestScore, null, identification.Threshold, ["BELOW_THRESHOLD"]);

        var rival = scored.Skip(1).FirstOrDefault(r => r.BestScore >= identification.Threshold);
        if (ambiguityMargin > 0 && rival is not null && top.BestScore - rival.BestScore < ambiguityMargin)
            return new(IdentityResult.Inconclusive, null, top.BestScore, null, identification.Threshold, ["AMBIGUOUS_MATCH"]);

        return new(IdentityResult.Match, top.GuardianId, top.BestScore, top.BestReferenceId, identification.Threshold, []);
    }

    /// <summary>Informational only: how far above the threshold the score is. Never used to decide anything.</summary>
    public static string ConfidenceBand(double score, double threshold) =>
        score >= threshold + 0.30 ? "Strong" : score >= threshold + 0.15 ? "Moderate" : score >= threshold ? "Borderline" : "Below threshold";
}
