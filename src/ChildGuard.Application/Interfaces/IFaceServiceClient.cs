using ChildGuard.Application.Common;

namespace ChildGuard.Application.Interfaces;

public record FaceEmbeddingResult(string EmbeddingJson, string Model);
public record FaceMatchResult(bool Match, double Score);

/// <summary>
/// Client for the separate Python face-recognition service (OpenCV/DeepFace/ArcFace).
/// The API never calls OpenCV directly — this is the only integration seam,
/// and the face service itself is not exposed to the public internet.
/// </summary>
public interface IFaceServiceClient
{
    Task<Result<FaceEmbeddingResult>> RegisterFaceAsync(string imageBase64, CancellationToken ct = default);
    Task<Result<FaceMatchResult>> VerifyFaceAsync(string storedEmbeddingJson, string imageBase64, CancellationToken ct = default);
}
