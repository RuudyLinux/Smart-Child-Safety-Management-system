using ChildGuard.Application.Common;

namespace ChildGuard.Application.Interfaces;

public record FaceEmbeddingResult(string EmbeddingJson, string Model);
public record FaceMatchResult(bool Match, double Score);

/// <summary>The measurements the face service took from one image. Issue codes are Python's own (e.g. BLURRY, NO_FACE).</summary>
public record ImageAnalysisMetrics(
    double? Sharpness, double? Brightness, double? Contrast, int? FacePx,
    double? YawDegEstimate, double? DetectionConfidence);

public record ImageAnalysis(
    string Purpose, int FaceCount, IReadOnlyList<string> Issues, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Messages, ImageAnalysisMetrics Metrics);

/// <param name="ReferenceId">0 for the guardian's primary face (GuardianFaceData), otherwise the GuardianFaceReference id.</param>
public record ReferenceEmbedding(int ReferenceId, string EmbeddingJson);
public record IdentifyCandidate(int GuardianId, IReadOnlyList<ReferenceEmbedding> References);
public record ReferenceScore(int ReferenceId, double Score);

/// <summary>Scores of one capture against one guardian's references. BestScore is null when none was readable.</summary>
public record GuardianIdentification(
    int GuardianId, double? BestScore, int? BestReferenceId,
    IReadOnlyList<ReferenceScore> Scores, IReadOnlyList<int> UnreadableReferenceIds);

/// <param name="Threshold">The face service's cosine-similarity match threshold (currently 0.40).</param>
public record FaceIdentification(double Threshold, string Model, IReadOnlyList<GuardianIdentification> Results);

/// <param name="DuplicateOf">Id of an existing reference this photo is near-identical to, else null.</param>
/// <param name="BestExistingScore">Highest similarity to any existing reference; null when there was none to compare with.</param>
/// <param name="Threshold">The face service's match threshold, so "looks like the registered face" uses the same line as matching.</param>
public record ReferenceRegistration(string EmbeddingJson, string Model, int? DuplicateOf, double? BestExistingScore, double Threshold);

public record LiveFrameInput(string ImageBase64, int TMs);
public record LivenessStepResult(string Step, bool Completed, int? FrameIndex, double? PeakRelYaw);
public record PassiveSignals(bool Available, int FramesScored, IReadOnlyList<double> RealScores, double? RealFraction, double? Mean, double? Min);
public record ContinuitySignals(int Checked, double? MinCosine, bool? SamePerson);

/// <summary>Everything the face service measured on one webcam burst. Signals only; the API decides.</summary>
public record LivenessAnalysis(
    int FramesUsable, double FaceFoundFraction, int? NeutralFrameIndex,
    IReadOnlyList<LivenessStepResult> Steps, bool ChallengeCompleted,
    PassiveSignals Passive, ContinuitySignals Continuity, double DuplicateFrameFraction,
    IReadOnlyList<string> Issues, FaceIdentification? Identification, ImageAnalysis? NeutralQuality);

/// <summary>
/// Client for the separate Python face-recognition service (OpenCV/DeepFace/ArcFace).
/// The API never calls OpenCV directly — this is the only integration seam,
/// and the face service itself is not exposed to the public internet.
/// </summary>
public interface IFaceServiceClient
{
    Task<Result<FaceEmbeddingResult>> RegisterFaceAsync(string imageBase64, CancellationToken ct = default);
    Task<Result<FaceMatchResult>> VerifyFaceAsync(string storedEmbeddingJson, string imageBase64, CancellationToken ct = default);

    /// <param name="purpose">"registration" or "verification"; registration is stricter about flat lighting.</param>
    Task<Result<ImageAnalysis>> AnalyzeImageAsync(string imageBase64, string purpose, CancellationToken ct = default);

    /// <summary>Scores one capture against every reference of every candidate guardian. Measures only; the caller decides.</summary>
    Task<Result<FaceIdentification>> IdentifyAsync(string imageBase64, IReadOnlyList<IdentifyCandidate> candidates, CancellationToken ct = default);

    /// <summary>
    /// Analyses a short webcam burst against a head-turn challenge: yaw sequence, passive anti-spoofing,
    /// face continuity, and (with candidates) who the frontal frame looks like.
    /// </summary>
    Task<Result<LivenessAnalysis>> AnalyzeLivenessAsync(
        IReadOnlyList<string> steps, IReadOnlyList<LiveFrameInput> frames, IReadOnlyList<IdentifyCandidate>? candidates, CancellationToken ct = default);

    /// <summary>Embeds one registration photo (strict rules) and reports a near-identical existing reference, if any.</summary>
    Task<Result<ReferenceRegistration>> RegisterReferenceAsync(string imageBase64, IReadOnlyList<ReferenceEmbedding> existing, CancellationToken ct = default);
}
