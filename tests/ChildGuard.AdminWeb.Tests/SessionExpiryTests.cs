using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>
/// AJAX requests (X-Requested-With: fetch) never get an HTML redirect when the session is missing or the API
/// rejected its token: they get a 401 envelope that tells cg-ajax.js where to send the browser. Page
/// navigations keep redirecting to sign-in exactly as before.
/// </summary>
public class SessionExpiryTests : IClassFixture<SessionExpiryTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        /// <summary>What the fake API answers; set per test.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage> Api { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Api:BaseUrl", "http://api.test");
            builder.ConfigureTestServices(services =>
            {
                var handler = new StubHandler(r => Api(r));
                services.AddHttpClient<ApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<LongRunningApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(answer(request));
    }

    private readonly Factory _factory;
    public SessionExpiryTests(Factory factory) => _factory = factory;

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpRequestMessage Get(string url, bool ajax)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // The region refresh asks for the page's HTML, so Accept alone cannot tell AJAX from navigation.
        request.Headers.Accept.ParseAdd("text/html");
        if (ajax) request.Headers.Add(AjaxRequest.HeaderName, AjaxRequest.HeaderValue);
        return request;
    }

    private static async Task<AjaxEnvelope> Envelope(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<AjaxEnvelope>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>Signs in through the real login form against the fake API.</summary>
    private async Task<HttpClient> SignedInClient()
    {
        _factory.Api = r => r.RequestUri!.AbsolutePath == "/api/auth/admin/login"
            ? Json(HttpStatusCode.OK, new
            {
                token = "test-token", expiresAtUtc = DateTime.UtcNow.AddHours(1), role = "Admin",
                userId = 1, fullName = "Test Admin", email = "admin@test"
            })
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        var client = Client();
        var page = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@test", ["Password"] = "Secret-Pass-1!", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    [Fact]
    public async Task AnonymousAjax_Gets401NotRedirect()
    {
        var response = await Client().SendAsync(Get("/Students", ajax: true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var e = await Envelope(response);
        Assert.False(e.Ok);
        Assert.StartsWith("/Account/Login", e.Redirect);
    }

    [Fact]
    public async Task AnonymousNavigation_StillRedirectsToSignIn()
    {
        var response = await Client().SendAsync(Get("/Students", ajax: false));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ExpiredSession_FetchRequest_Gets401Envelope()
    {
        var client = await SignedInClient();
        _factory.Api = _ => Json(HttpStatusCode.Unauthorized, new { message = "Token expired." });

        var response = await client.SendAsync(Get("/Students", ajax: true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var e = await Envelope(response);
        Assert.False(e.Ok);
        Assert.Equal("Your session has expired. Sign in again.", e.Message);
        Assert.Equal(ApiSessionExpiredFilter.LoginUrl, e.Redirect);
    }

    [Fact]
    public async Task ExpiredSession_PageNavigation_StillRedirects()
    {
        var client = await SignedInClient();
        _factory.Api = _ => Json(HttpStatusCode.Unauthorized, new { message = "Token expired." });

        var response = await client.SendAsync(Get("/Students", ajax: false));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ApiSessionExpiredFilter.LoginUrl, response.Headers.Location!.ToString());
    }
}
