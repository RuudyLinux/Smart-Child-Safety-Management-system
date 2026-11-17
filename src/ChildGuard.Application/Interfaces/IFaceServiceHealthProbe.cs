namespace ChildGuard.Application.Interfaces;

/// <param name="Reachable">The face service answered its health check.</param>
/// <param name="AntiSpoofing">
/// Whether the liveness (anti-spoofing) model is loaded. Null when the service did not say, which happens
/// when it rejected the API's key: it then reports only that it is up.
/// </param>
/// <param name="LatencyMs">Round trip of the check, in milliseconds.</param>
/// <param name="Problem">Short, non-sensitive reason when something is wrong; null when healthy.</param>
public sealed record FaceServiceHealth(bool Reachable, bool? AntiSpoofing, int LatencyMs, string? Problem);

/// <summary>
/// Checks whether the face service is up, from the API's side (with the API's own base URL, key and
/// network path, e.g. the tunnel in production). Results are cached briefly so repeated checks cannot
/// be used to flood the face service.
/// </summary>
public interface IFaceServiceHealthProbe
{
    Task<FaceServiceHealth> CheckAsync(CancellationToken ct = default);
}
