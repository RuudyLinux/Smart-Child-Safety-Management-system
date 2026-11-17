using System.Net;
using System.Text.Json;
using ChildGuard.AdminWeb.Services;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>
/// A form whose antiforgery token no longer validates (the page was opened before signing in again, or the key
/// ring changed) can never succeed by retrying. AJAX gets an envelope saying the page must be reloaded; the
/// check itself is never skipped.
/// </summary>
public class AntiforgeryExpiryTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public AntiforgeryExpiryTests(AdminWebFactory factory) => _factory = factory;

    private static HttpRequestMessage Post(string url, bool ajax, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["guardianId"] = "3", ["relationship"] = "Mother", ["__RequestVerificationToken"] = token
            })
        };
        if (ajax) request.Headers.Add(AjaxRequest.HeaderName, AjaxRequest.HeaderValue);
        return request;
    }

    [Fact]
    public async Task StaleToken_Ajax_Gets400EnvelopeAskingForAReload()
    {
        var client = await _factory.SignedInClientAsync(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var response = await client.SendAsync(Post("/Students/AssignGuardian/1", ajax: true, token: "stale-token-from-an-old-page"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var e = JsonSerializer.Deserialize<AjaxEnvelope>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(e.Ok);
        Assert.Equal(AjaxAntiforgeryFilter.ExpiredPageMessage, e.Message);
        Assert.Contains("Reload", e.Message);
        // The check really failed: nothing reached the API.
        Assert.Empty(_factory.Calls);
    }

    [Fact]
    public async Task StaleToken_PageNavigation_KeepsTheFrameworkAnswer()
    {
        var client = await _factory.SignedInClientAsync(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var response = await client.SendAsync(Post("/Students/AssignGuardian/1", ajax: false, token: "stale"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Calls);
    }
}
