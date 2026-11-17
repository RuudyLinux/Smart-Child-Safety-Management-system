using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <param name="Purpose">"registration" or "verification" (default).</param>
public record AnalyzeImageRequest(string ImageBase64, string? Purpose = null);

/// <summary>
/// Layer 1 outcome for one image. Quality and FaceDetection are kept separate; Passed is true
/// only when both pass. Messages are plain-language and safe to show to reception staff.
/// </summary>
public record ImageAnalysisResultDto(
    bool Passed, CheckResult Quality, CheckResult FaceDetection, int FaceCount,
    IReadOnlyList<string> Issues, IReadOnlyList<string> Warnings, IReadOnlyList<string> Messages,
    ImageAnalysisMetrics Metrics);
