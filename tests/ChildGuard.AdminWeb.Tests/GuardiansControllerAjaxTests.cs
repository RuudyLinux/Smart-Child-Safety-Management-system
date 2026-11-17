using System.Net;
using System.Text;
using ChildGuard.AdminWeb.Controllers;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>Guardian Create over AJAX: success and "created but the email failed" both navigate to the profile.</summary>
public class GuardiansControllerAjaxTests
{
    private sealed class Fixed(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static GuardiansController Controller(HttpStatusCode status, string json)
    {
        var api = new ApiClient(new HttpClient(new Fixed(status, json)) { BaseAddress = new Uri("http://api.test/") });
        var http = new DefaultHttpContext();
        http.Request.Headers[AjaxRequest.HeaderName] = AjaxRequest.HeaderValue;
        return new GuardiansController(api)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempData())
        };
    }

    private static readonly CreateGuardianRequest Request = new("Priya", "priya@childguard.test", "+91 98200 11111");

    [Fact]
    public async Task Create_EmailSent_NavigatesToTheProfileWithASuccessToast()
    {
        var c = Controller(HttpStatusCode.OK, "{\"guardianId\":7,\"credentialEmailSent\":true,\"emailError\":null}");
        var result = Assert.IsType<JsonResult>(await c.Create(Request, CancellationToken.None));
        var e = Assert.IsType<AjaxEnvelope>(result.Value);
        Assert.Equal("/Guardians/Details/7", e.Redirect);
        Assert.StartsWith("Guardian account created.", (string)c.TempData["Success"]!);
        Assert.False(c.TempData.ContainsKey("Error"));
    }

    [Fact]
    public async Task Create_EmailNotDelivered_StillNavigates_AndTheNextPageShowsAnErrorToast()
    {
        var c = Controller(HttpStatusCode.OK, "{\"guardianId\":7,\"credentialEmailSent\":false,\"emailError\":\"Mailbox full\"}");
        var result = Assert.IsType<JsonResult>(await c.Create(Request, CancellationToken.None));
        Assert.Equal("/Guardians/Details/7", Assert.IsType<AjaxEnvelope>(result.Value).Redirect);
        Assert.Contains("credential email could not be delivered: Mailbox full", (string)c.TempData["Error"]!);
        Assert.False(c.TempData.ContainsKey("Success"));
    }

    [Fact]
    public async Task Create_DuplicateNumber_IsAFormMessage_NotANavigation()
    {
        var c = Controller(HttpStatusCode.Conflict, "{\"message\":\"That contact number is already registered.\"}");
        var result = Assert.IsType<JsonResult>(await c.Create(Request, CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
        var e = Assert.IsType<AjaxEnvelope>(result.Value);
        Assert.Null(e.Redirect);
        Assert.Equal(new[] { "That contact number is already registered." }, e.Errors![""]);
    }
}
