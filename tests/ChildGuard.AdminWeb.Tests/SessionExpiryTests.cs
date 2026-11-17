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
public class SessionExpiryTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public SessionExpiryTests(AdminWebFactory factory) => _factory = factory;

    private static async Task<AjaxEnvelope> Envelope(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<AjaxEnvelope>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public async Task AnonymousAjax_Gets401NotRedirect()
    {
        var response = await _factory.Client().SendAsync(AdminWebFactory.Get("/Students", ajax: true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var e = await Envelope(response);
        Assert.False(e.Ok);
        // A cookie that ran out while a form was open: the sign-in page must say the session expired.
        Assert.Equal(ApiSessionExpiredFilter.LoginUrl, e.Redirect);
    }

    [Fact]
    public async Task AnonymousNavigation_StillRedirectsToSignIn()
    {
        var response = await _factory.Client().SendAsync(AdminWebFactory.Get("/Students", ajax: false));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ExpiredSession_FetchRequest_Gets401Envelope()
    {
        var client = await _factory.SignedInClientAsync();
        _factory.Api = _ => AdminWebFactory.Json(HttpStatusCode.Unauthorized, new { message = "Token expired." });

        var response = await client.SendAsync(AdminWebFactory.Get("/Students", ajax: true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var e = await Envelope(response);
        Assert.False(e.Ok);
        Assert.Equal("Your session has expired. Sign in again.", e.Message);
        Assert.Equal(ApiSessionExpiredFilter.LoginUrl, e.Redirect);
    }

    [Fact]
    public async Task ExpiredSession_PageNavigation_StillRedirects()
    {
        var client = await _factory.SignedInClientAsync();
        _factory.Api = _ => AdminWebFactory.Json(HttpStatusCode.Unauthorized, new { message = "Token expired." });

        var response = await client.SendAsync(AdminWebFactory.Get("/Students", ajax: false));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ApiSessionExpiredFilter.LoginUrl, response.Headers.Location!.ToString());
    }
}