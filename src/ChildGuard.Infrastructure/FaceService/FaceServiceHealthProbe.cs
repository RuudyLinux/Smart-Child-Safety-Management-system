using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.FaceService;

/// <summary>
/// Calls the face service's GET /health with the same base URL and internal key as every face call.
/// A short timeout keeps the diagnostics page responsive when the face-service PC is off, and the
/// result is cached for a few seconds because the endpoint behind it is anonymous.
/// </summary>
public class FaceServiceHealthProbe : IFaceServiceHealthProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (DateTime At, FaceServiceHealth Value)? _cached;

    private readonly HttpClient _http;
    private readonly ILogger<FaceServiceHealthProbe> _logger;
    private readonly TimeProvider _time;

    public FaceServiceHealthProbe(HttpClient http, ILogger<FaceServiceHealthProbe> logger, TimeProvider time)
    {
        _http = http;
        _logger = logger;
        _time = time;
    }

    /// <summary>Forgets the cached result (tests).</summary>
    public static void ResetCache() => _cached = null;

    public async Task<FaceServiceHealth> CheckAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        if (_cached is { } hit && now - hit.At < CacheFor) return hit.Value;

        await Gate.WaitAsync(ct);
        try
        {
            if (_cached is { } again && now - again.At < CacheFor) return again.Value;
            var result = await ProbeAsync(ct);
            _cached = (_time.GetUtcNow().UtcDateTime, result);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<FaceServiceHealth> ProbeAsync(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await _http.GetAsync("/health", timeout.Token);
            var ms = (int)watch.ElapsedMilliseconds;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new FaceServiceHealth(true, null, ms, "The face service rejected the API's key (FaceService:ApiKey does not match FACE_SERVICE_KEY).");
            if (!response.IsSuccessStatusCode)
                return new FaceServiceHealth(false, null, ms, $"The face service answered HTTP {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = doc.RootElement;
            var ok = root.TryGetProperty("status", out var status) && status.GetString() == "ok";
            bool? antiSpoofing = root.TryGetProperty("anti_spoofing", out var anti) && anti.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? anti.GetBoolean()
                : null;
            if (!ok) return new FaceServiceHealth(false, antiSpoofing, ms, "The face service reported it is not ready.");
            if (antiSpoofing is null)
                return new FaceServiceHealth(true, null, ms, "Up, but it did not report its models: the API's key is probably not accepted.");
            return new FaceServiceHealth(true, antiSpoofing, ms,
                antiSpoofing == false ? "Up, but the anti-spoofing model is not loaded: liveness checks cannot pass." : null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new FaceServiceHealth(false, null, (int)watch.ElapsedMilliseconds,
                $"No answer within {Timeout.TotalSeconds:0} s. Is the face-service PC on and its tunnel running?");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Face service health check failed.");
            return new FaceServiceHealth(false, null, (int)watch.ElapsedMilliseconds,
                ex is JsonException ? "The face service answered with an unexpected body." : "Could not connect to the face service.");
        }
    }
}
