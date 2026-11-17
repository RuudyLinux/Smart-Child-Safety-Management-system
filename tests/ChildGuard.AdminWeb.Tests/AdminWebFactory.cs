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

/// <summary>The real AdminWeb in memory, talking to a fake API that each test scripts.</summary>
public sealed class AdminWebFactory : WebApplicationFactory<Program>
{
    /// <summary>What the fake API answers; set per test. Every call is also recorded in <see cref="Calls"/>.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Api { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public System.Collections.Concurrent.ConcurrentQueue<string> Calls { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Api:BaseUrl", "http://api.test");
        builder.ConfigureTestServices(services =>
        {
            var handler = new StubHandler(r => { Calls.Enqueue(r.RequestUri!.PathAndQuery); return Api(r); });
            services.AddHttpClient<ApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddHttpClient<LongRunningApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(answer(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public static HttpRequestMessage Get(string url, bool ajax, string? regions = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // The region refresh asks for the page's HTML, so Accept alone cannot tell AJAX from navigation.
        request.Headers.Accept.ParseAdd("text/html");
        if (ajax) request.Headers.Add(AjaxRequest.HeaderName, AjaxRequest.HeaderValue);
        if (regions is not null) request.Headers.Add("X-CG-Regions", regions);
        return request;
    }

    /// <summary>Signs in through the real login form; afterwards the fake API answers with <paramref name="then"/>.</summary>
    public async Task<HttpClient> SignedInClientAsync(Func<HttpRequestMessage, HttpResponseMessage>? then = null)
    {
        Api = r => r.RequestUri!.AbsolutePath == "/api/auth/admin/login"
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
        if (then is not null) Api = then;
        Calls.Clear();
        return client;
    }
}
