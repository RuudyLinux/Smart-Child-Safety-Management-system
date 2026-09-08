using System.Net.Http.Json;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.FaceService;

file record RegisterFaceApiRequest(string image_base64);
file record RegisterFaceApiResponse(string embedding_json, string model);
file record VerifyFaceApiRequest(string stored_embedding_json, string image_base64);
file record VerifyFaceApiResponse(bool match, double score);
file record FaceServiceErrorResponse(string? detail);

/// <summary>
/// HTTP client to the internal Python face-recognition service. Base URL comes
/// from configuration (FaceService:BaseUrl) — never hardcoded, and the service
/// is expected to be reachable only on the internal network, not the public internet.
/// </summary>
public class FaceServiceHttpClient : IFaceServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FaceServiceHttpClient> _logger;

    public FaceServiceHttpClient(HttpClient http, ILogger<FaceServiceHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<FaceEmbeddingResult>> RegisterFaceAsync(string imageBase64, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/register-face", new RegisterFaceApiRequest(imageBase64), ct);
            if (!response.IsSuccessStatusCode)
                return Result<FaceEmbeddingResult>.Failure(await ExtractErrorMessageAsync(response, ct));

            var body = await response.Content.ReadFromJsonAsync<RegisterFaceApiResponse>(cancellationToken: ct);
            if (body is null) return Result<FaceEmbeddingResult>.Failure("Face service returned an empty response.");

            return Result<FaceEmbeddingResult>.Success(new FaceEmbeddingResult(body.embedding_json, body.model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face service register-face call failed.");
            return Result<FaceEmbeddingResult>.Failure("Face service is unavailable.");
        }
    }

    public async Task<Result<FaceMatchResult>> VerifyFaceAsync(string storedEmbeddingJson, string imageBase64, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/verify-face",
                new VerifyFaceApiRequest(storedEmbeddingJson, imageBase64), ct);
            if (!response.IsSuccessStatusCode)
                return Result<FaceMatchResult>.Failure(await ExtractErrorMessageAsync(response, ct));

            var body = await response.Content.ReadFromJsonAsync<VerifyFaceApiResponse>(cancellationToken: ct);
            if (body is null) return Result<FaceMatchResult>.Failure("Face service returned an empty response.");

            return Result<FaceMatchResult>.Success(new FaceMatchResult(body.match, body.score));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face service verify-face call failed.");
            return Result<FaceMatchResult>.Failure("Face service is unavailable.");
        }
    }

    /// <summary>
    /// FastAPI returns {"detail": "..."} on validation errors (e.g. 422 "No face
    /// detected") — surface that diagnosis directly instead of a raw status code,
    /// since it's the actionable part for whoever is holding the camera.
    /// </summary>
    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<FaceServiceErrorResponse>(cancellationToken: ct);
            if (!string.IsNullOrWhiteSpace(error?.detail)) return error.detail;
        }
        catch { /* body wasn't the expected shape — fall back below */ }

        return $"Face service returned {(int)response.StatusCode}.";
    }
}
