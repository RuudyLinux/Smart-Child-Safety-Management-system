using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class FaceImageAnalysisService : IFaceImageAnalysisService
{
    public const string RegistrationPurpose = "registration";
    public const string VerificationPurpose = "verification";

    private readonly IFaceServiceClient _faces;
    private readonly IFaceAuditLog _audit;

    public FaceImageAnalysisService(IFaceServiceClient faces, IFaceAuditLog audit)
    {
        _faces = faces;
        _audit = audit;
    }

    public async Task<Result<ImageAnalysisResultDto>> AnalyzeAsync(AnalyzeImageRequest request, FaceActor actor, CancellationToken ct = default)
    {
        var purpose = string.Equals(request.Purpose, RegistrationPurpose, StringComparison.OrdinalIgnoreCase)
            ? RegistrationPurpose
            : VerificationPurpose;

        var response = await _faces.AnalyzeImageAsync(request.ImageBase64, purpose, ct);
        if (!response.Succeeded)
        {
            await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Quality, "AnalyzeImage", "Unavailable", actor,
                Detail: $"purpose={purpose}"), ct);
            return Result<ImageAnalysisResultDto>.Failure(response.Error ?? "Face service error.");
        }

        var a = FaceQualityPolicy.Assess(response.Value!);
        var detail = $"purpose={purpose};faces={a.FaceCount};issues={string.Join(',', a.Issues)}";
        await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Quality, "AnalyzeImage", a.Quality.ToString(), actor, Detail: detail), ct);
        await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Detection, "AnalyzeImage", a.FaceDetection.ToString(), actor, Detail: detail), ct);

        return Result<ImageAnalysisResultDto>.Success(new ImageAnalysisResultDto(
            a.Passed, a.Quality, a.FaceDetection, a.FaceCount, a.Issues, a.Warnings, a.Messages, a.Metrics));
    }
}
