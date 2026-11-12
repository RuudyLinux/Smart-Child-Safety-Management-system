using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IFaceImageAnalysisService
{
    /// <summary>Runs Layer 1 (image quality + face detection) on one image and records both results in the audit log.</summary>
    Task<Result<ImageAnalysisResultDto>> AnalyzeAsync(AnalyzeImageRequest request, FaceActor actor, CancellationToken ct = default);
}
