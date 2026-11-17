using System.Net;
using System.Text;
using ChildGuard.AdminWeb.Controllers;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>Create/Edit over AJAX: field errors go beside fields; any other refusal says what actually happened.</summary>
public class StudentsControllerAjaxTests
{
    private sealed class Fixed(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static StudentsController Controller(HttpStatusCode status, string json)
    {
        var api = new ApiClient(new HttpClient(new Fixed(status, json)) { BaseAddress = new Uri("http://api.test/") });
        var http = new DefaultHttpContext();
        http.Request.Headers[AjaxRequest.HeaderName] = AjaxRequest.HeaderValue;
        return new StudentsController(api)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempData())
        };
    }

    private static readonly UpdateStudentRecordRequest Edit = new("Mira", new DateTime(2016, 4, 21), "5A", null, null, null);

    private static (int? Status, AjaxEnvelope Envelope) Read(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        return (json.StatusCode, Assert.IsType<AjaxEnvelope>(json.Value));
    }

    [Fact]
    public async Task Edit_Conflict_IsReportedAsItself_NotAsFieldErrors()
    {
        var (status, e) = Read(await Controller(HttpStatusCode.Conflict, "{\"message\":\"Admission number already in use.\"}")
            .Edit(1, Edit, CancellationToken.None));
        Assert.Equal(409, status);
        Assert.Equal("Admission number already in use.", e.Message);
        Assert.Null(e.Errors);
    }

    [Fact]
    public async Task Edit_FieldErrors_GoBesideFields()
    {
        var json = "{\"message\":\"Validation failed.\",\"errors\":[{\"PropertyName\":\"FullName\",\"ErrorMessage\":\"Too long.\"}]}";
        var (status, e) = Read(await Controller(HttpStatusCode.BadRequest, json).Edit(1, Edit, CancellationToken.None));
        Assert.Equal(400, status);
        Assert.Equal(new[] { "Too long." }, e.Errors!["FullName"]);
    }

    [Fact]
    public async Task Edit_LocalBindingError_StaysBesideTheField_EvenIfTheApiSentOnlyAMessage()
    {
        var c = Controller(HttpStatusCode.BadRequest, "{\"message\":\"Full name is required.\"}");
        c.ModelState.AddModelError("FullName", "The FullName field is required.");
        var (status, e) = Read(await c.Edit(1, Edit, CancellationToken.None));
        Assert.Equal(400, status);
        Assert.Equal(new[] { "The FullName field is required." }, e.Errors!["FullName"]);
    }

    [Fact]
    public async Task Create_ServerError_IsReportedAsItself()
    {
        var (status, e) = Read(await Controller(HttpStatusCode.InternalServerError, "{\"message\":\"An unexpected error occurred.\"}")
            .Create(new CreateStudentRequest("Mira", new DateTime(2016, 4, 21), "5A"), CancellationToken.None));
        Assert.Equal(500, status);
        Assert.Equal("An unexpected error occurred.", e.Message);
        Assert.Null(e.Errors);
    }
}
