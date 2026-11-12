using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.DTOs;

public record AddFaceReferenceRequest(string ImageBase64, string? Label = null);

/// <summary>Quality measurements taken when the photo was registered. Contains no biometric data.</summary>
public record FaceReferenceQualityDto(double? Sharpness, double? Brightness, int? FacePx, double? YawDegEstimate);

/// <param name="Id">0 for the primary face (the one registered first), otherwise the reference id used to remove it.</param>
/// <param name="Source">"Primary" or "Additional".</param>
public record FaceReferenceDto(int Id, string Source, string? Label, DateTime CreatedAt, FaceReferenceQualityDto? Quality);

/// <summary>A guardian's registered reference faces. Never carries an embedding.</summary>
public record GuardianFaceReferencesDto(IReadOnlyList<FaceReferenceDto> References, int MaxReferences, bool CanAdd);

/// <param name="Added">False when nothing was stored (a near-identical photo is already registered).</param>
public record AddFaceReferenceResultDto(bool Added, bool Duplicate, string Message, int? ReferenceId, ImageAnalysisMetrics? Quality);
