using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public sealed record QualityAssessment(
    CheckResult Quality, CheckResult FaceDetection, int FaceCount,
    IReadOnlyList<string> Issues, IReadOnlyList<string> Warnings, IReadOnlyList<string> Messages,
    ImageAnalysisMetrics Metrics)
{
    public bool Passed => Quality == CheckResult.Pass && FaceDetection == CheckResult.Pass;
}

/// <summary>
/// Decides what the face service's measurements mean. The service only reports issue codes;
/// whether an image is usable is decided here, so the rule lives in one place and is testable.
/// Every issue the service raises blocks; warnings never do.
/// </summary>
public static class FaceQualityPolicy
{
    /// <summary>
    /// Codes meaning no single usable face could be found. A file the service could not read
    /// also lands here: nothing was detected because there was nothing to detect in.
    /// </summary>
    private static readonly HashSet<string> DetectionCodes = new(StringComparer.Ordinal)
    {
        "NO_FACE", "MULTIPLE_FACES", "FACE_CUT_OFF", "BAD_FORMAT", "TOO_LARGE", "LOW_RESOLUTION"
    };

    public static QualityAssessment Assess(ImageAnalysis analysis)
    {
        var anyIssue = analysis.Issues.Count > 0;
        var detectionFailed = analysis.Issues.Any(DetectionCodes.Contains);

        return new QualityAssessment(
            anyIssue ? CheckResult.Fail : CheckResult.Pass,
            detectionFailed ? CheckResult.Fail : CheckResult.Pass,
            analysis.FaceCount, analysis.Issues, analysis.Warnings, analysis.Messages, analysis.Metrics);
    }
}
