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
file record ReferenceApi(int reference_id, string embedding_json);
file record IdentifyCandidateApi(int guardian_id, List<ReferenceApi> references);
file record IdentifyApiRequest(string image_base64, List<IdentifyCandidateApi> candidates);
file record ReferenceScoreApi(int reference_id, double score);
file record GuardianIdentificationApi(int guardian_id, double? best_score, int? best_reference_id, List<ReferenceScoreApi>? scores, List<int>? unreadable_reference_ids);
file record IdentifyApiResponse(double threshold, string model, List<GuardianIdentificationApi>? results);
file record RegisterReferenceApiRequest(string image_base64, List<ReferenceApi> existing);
file record RegisterReferenceApiResponse(string embedding_json, string model, int? duplicate_of, double? best_existing_score, double threshold);
file record LiveFrameApi(string image_base64, int t_ms);
file record LivenessApiRequest(List<string> steps, List<LiveFrameApi> frames, List<IdentifyCandidateApi>? candidates);
file record LivenessStepApi(string step, bool completed, int? frame_index, double? peak_rel_yaw);
file record PassiveApi(bool available, int frames_scored, List<double>? real_scores, double? real_frac, double? mean, double? min);
file record ContinuityApi(int @checked, double? min_cosine, bool? same_person);
file record LivenessApiResponse(
    int frames_usable, double face_found_frac, int? neutral_frame_index, List<LivenessStepApi>? steps, bool challenge_completed,
    PassiveApi? passive, ContinuityApi? continuity, double duplicate_frame_frac, List<string>? issues,
    IdentifyApiResponse? identification, AnalyzeImageApiResponse? neutral_quality);
file record AnalyzeImageApiRequest(string image_base64, string purpose);
file record AnalyzeMetricsApi(double? sharpness, double? brightness, double? contrast, int? face_px, double? yaw_deg_est, double? detection_confidence);
file record AnalyzeImageApiResponse(string purpose, int face_count, List<string>? issues, List<string>? warnings, List<string>? messages, AnalyzeMetricsApi? metrics);

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

    public async Task<Result<ImageAnalysis>> AnalyzeImageAsync(string imageBase64, string purpose, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/analyze-image", new AnalyzeImageApiRequest(imageBase64, purpose), ct);
            if (!response.IsSuccessStatusCode)
                return Result<ImageAnalysis>.Failure(await ExtractErrorMessageAsync(response, ct));

            var body = await response.Content.ReadFromJsonAsync<AnalyzeImageApiResponse>(cancellationToken: ct);
            if (body is null) return Result<ImageAnalysis>.Failure("Face service returned an empty response.");

            var m = body.metrics;
            return Result<ImageAnalysis>.Success(new ImageAnalysis(
                body.purpose, body.face_count,
                body.issues ?? [], body.warnings ?? [], body.messages ?? [],
                new ImageAnalysisMetrics(m?.sharpness, m?.brightness, m?.contrast, m?.face_px, m?.yaw_deg_est, m?.detection_confidence)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face service analyze-image call failed.");
            return Result<ImageAnalysis>.Failure("Face service is unavailable.");
        }
    }

    public async Task<Result<FaceIdentification>> IdentifyAsync(string imageBase64, IReadOnlyList<IdentifyCandidate> candidates, CancellationToken ct = default)
    {
        try
        {
            var request = new IdentifyApiRequest(imageBase64, candidates
                .Select(c => new IdentifyCandidateApi(c.GuardianId,
                    c.References.Select(r => new ReferenceApi(r.ReferenceId, r.EmbeddingJson)).ToList()))
                .ToList());
            var response = await _http.PostAsJsonAsync("/identify", request, ct);
            if (!response.IsSuccessStatusCode)
                return Result<FaceIdentification>.Failure(await ExtractErrorMessageAsync(response, ct));

            var body = await response.Content.ReadFromJsonAsync<IdentifyApiResponse>(cancellationToken: ct);
            if (body?.results is null) return Result<FaceIdentification>.Failure("Face service returned an empty response.");

            return Result<FaceIdentification>.Success(new FaceIdentification(body.threshold, body.model,
                body.results.Select(r => new GuardianIdentification(
                    r.guardian_id, r.best_score, r.best_reference_id,
                    (r.scores ?? []).Select(s => new ReferenceScore(s.reference_id, s.score)).ToList(),
                    r.unreadable_reference_ids ?? [])).ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face service identify call failed.");
            return Result<FaceIdentification>.Failure("Face service is unavailable.");
        }
    }

    public async Task<Result<ReferenceRegistration>> RegisterReferenceAsync(string imageBase64, IReadOnlyList<ReferenceEmbedding> existing, CancellationToken ct = default)
    {
        try
        {
            var request = new RegisterReferenceApiRequest(imageBase64,
                existing.Select(r => new ReferenceApi(r.ReferenceId, r.EmbeddingJson)).ToList());
            var response = await _http.PostAsJsonAsync("/register-reference", request, ct);
            if (!response.IsSuccessStatusCode)
                return Result<ReferenceRegistration>.Failure(await ExtractErrorMessageAsync(response, ct));

            var body = await response.Content.ReadFromJsonAsync<RegisterReferenceApiResponse>(cancellationToken: ct);
            if (body is null) return Result<ReferenceRegistration>.Failure("Face service returned an empty response.");

            return Result<ReferenceRegistration>.Success(new ReferenceRegistration(
                body.embedding_json, body.model, body.duplicate_of, body.best_existing_score, body.threshold));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face service register-reference call failed.");
            return Result<ReferenceRegistration>.Failure("Face service is unavailable.");
        }
    }

    public async Task<Result<LivenessAnalysis>> AnalyzeLivenessAsync(
        IReadOnlyList<string> steps, IReadOnlyList<LiveFrameInput> frames, IReadOnlyList<IdentifyCandidate>? candidates, CancellationToken ct = default)
    {
        try
        {
            var request = new LivenessApiRequest(
                steps.ToList(),
                frames.Select(f => new LiveFrameApi(f.ImageBase64, f.TMs)).ToList(),
                candidates?.Select(c => new IdentifyCandidateApi(c.GuardianId,
                    c.References.Select(r => new ReferenceApi(r.ReferenceId, r.EmbeddingJson)).ToList())).ToList());
            var response = await _http.PostAsJsonAsync("/liveness/analyze", request, ct);
            if (!response.IsSuccessStatusCode)
                return Result<LivenessAnalysis>.Failure(await ExtractErrorMessageAsync(response, ct));

            var body = await response.Content.ReadFromJsonAsync<LivenessApiResponse>(cancellationToken: ct);
            if (body is null) return Result<LivenessAnalysis>.Failure("Face service returned an empty response.");

            FaceIdentification? identification = body.identification?.results is null ? null
                : new FaceIdentification(body.identification.threshold, body.identification.model,
                    body.identification.results.Select(r => new GuardianIdentification(
                        r.guardian_id, r.best_score, r.best_reference_id,
                        (r.scores ?? []).Select(s => new ReferenceScore(s.reference_id, s.score)).ToList(),
                        r.unreadable_reference_ids ?? [])).ToList());

            var q = body.neutral_quality;
            ImageAnalysis? neutralQuality = q is null ? null
                : new ImageAnalysis(q.purpose, q.face_count, q.issues ?? [], q.warnings ?? [], q.messages ?? [],
                    new ImageAnalysisMetrics(q.metrics?.sharpness, q.metrics?.brightness, q.metrics?.contrast,
                        q.metrics?.face_px, q.metrics?.yaw_deg_est, q.metrics?.detection_confidence));

            var passive = body.passive is null
                ? new PassiveSignals(false, 0, [], null, null, null)
                : new PassiveSignals(body.passive.available, body.passive.frames_scored, body.passive.real_scores ?? [],
                    body.passive.real_frac, body.passive.mean, body.passive.min);
            var continuity = body.continuity is null
                ? new ContinuitySignals(0, null, null)
                : new ContinuitySignals(body.continuity.@checked, body.continuity.min_cosine, body.continuity.same_person);

            return Result<LivenessAnalysis>.Success(new LivenessAnalysis(
                body.frames_usable, body.face_found_frac, body.neutral_frame_index,
                (body.steps ?? []).Select(s => new LivenessStepResult(s.step, s.completed, s.frame_index, s.peak_rel_yaw)).ToList(),
                body.challenge_completed, passive, continuity, body.duplicate_frame_frac, body.issues ?? [],
                identification, neutralQuality));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face service liveness call failed.");
            return Result<LivenessAnalysis>.Failure("Face service is unavailable.");
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
