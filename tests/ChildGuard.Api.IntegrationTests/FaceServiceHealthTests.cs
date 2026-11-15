using System.Net;
using System.Text.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Infrastructure.FaceService;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// The face-service check behind the Diagnostics page: what the probe reports for each face-service
/// state, and that the anonymous endpoint reveals nothing beyond up/down and a short reason.
/// </summary>
[Collection("FaceServiceHealthProbe")] // the probe's cache is static: run these one at a time
public class FaceServiceHealthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public FaceServiceHealthTests(ApiFactory api) => _api = api;

    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _answer;
        public int Calls;
        public Stub(Func<CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Assert.Equal("/health", request.RequestUri!.AbsolutePath);
            return _answer(ct);
        }
    }

    private static FaceServiceHealthProbe Probe(Stub stub)
    {
        FaceServiceHealthProbe.ResetCache();
        return new FaceServiceHealthProbe(new HttpClient(stub) { BaseAddress = new Uri("http://face.test") },
            NullLogger<FaceServiceHealthProbe>.Instance, TimeProvider.System);
    }

    private static Stub Json(HttpStatusCode code, string body) =>
        new(_ => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) }));

    [Fact]
    public async Task Healthy_WithLivenessModel()
    {
        var h = await Probe(Json(HttpStatusCode.OK, """{"status":"ok","model":"ArcFace","anti_spoofing":true}""")).CheckAsync();
        Assert.Equal((true, (bool?)true, (string?)null), (h.Reachable, h.AntiSpoofing, h.Problem));
    }

    [Fact]
    public async Task Up_ButLivenessModelMissing_IsReported()
    {
        var h = await Probe(Json(HttpStatusCode.OK, """{"status":"ok","anti_spoofing":false}""")).CheckAsync();
        Assert.True(h.Reachable);
        Assert.Contains("anti-spoofing", h.Problem);
    }

    [Fact]
    public async Task WrongKey_IsReportedAsAKeyProblem()
    {
        Assert.Contains("key", (await Probe(Json(HttpStatusCode.Unauthorized, "{}")).CheckAsync()).Problem);
        // A key it does not accept still gets the public {"status":"ok"} without model details.
        Assert.Contains("key", (await Probe(Json(HttpStatusCode.OK, """{"status":"ok"}""")).CheckAsync()).Problem);
    }

    [Fact]
    public async Task Down_AndTimeout_AreUnreachable()
    {
        var down = await Probe(new Stub(_ => throw new HttpRequestException("refused"))).CheckAsync();
        Assert.False(down.Reachable);

        var slow = await Probe(new Stub(async ct => { await Task.Delay(TimeSpan.FromMinutes(1), ct); return new HttpResponseMessage(); })).CheckAsync();
        Assert.False(slow.Reachable);
        Assert.Contains("No answer", slow.Problem);
        Assert.True(slow.LatencyMs < 15_000);
    }

    [Fact]
    public async Task Results_AreCachedBriefly()
    {
        var stub = Json(HttpStatusCode.OK, """{"status":"ok","anti_spoofing":true}""");
        var probe = Probe(stub);
        await probe.CheckAsync();
        await probe.CheckAsync();
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => probe.CheckAsync()));
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Endpoint_IsAnonymous_AndRevealsOnlyStatus()
    {
        FaceServiceHealthProbe.ResetCache();
        var response = await _api.CreateClient().GetAsync("/api/health/face");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Contains(doc.RootElement.GetProperty("faceService").GetString(), new[] { "ok", "degraded", "unreachable" });
        Assert.Equal(new[] { "faceService", "antiSpoofing", "latencyMs", "problem" },
            doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("http", body, StringComparison.OrdinalIgnoreCase); // no address leaked
        Assert.DoesNotContain("ArcFace", body);
    }
}
