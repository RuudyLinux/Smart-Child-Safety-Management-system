using ChildGuard.AdminWeb.Controllers;
using ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ChildGuard.AdminWeb.Tests;

public class AjaxEnvelopeTests
{
    /// <summary>Exposes the protected helpers; the real controllers call them the same way.</summary>
    private sealed class ProbeController : BaseAdminController
    {
        public ProbeController() : base(new ApiClient(new HttpClient())) { }
        public IActionResult CallDone(string message, string? redirect, params string[] regions) => Done(message, redirect, regions);
        public IActionResult CallFailed(string message, int status, string? redirect) => Failed(message, status, redirect);
        public IActionResult? CallInvalid(string? message = null) => Invalid(ModelState, message);
        public IActionResult CallFromApi<T>(ApiResult<T> result, string message, string? redirect, params string[] regions) =>
            FromApi(result, message, redirect, regions);
        public IActionResult? CallRefuseForm<T>(ApiResult<T> result) => RefuseForm(result);
        public ModelStateDictionary State => ModelState;
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }

    private static ProbeController Controller(bool ajax, string? referer = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/Students/AssignGuardian/5";
        if (ajax) http.Request.Headers[AjaxRequest.HeaderName] = AjaxRequest.HeaderValue;
        if (referer is not null) http.Request.Headers.Referer = referer;
        http.Request.Host = new HostString("admin.test");
        http.Request.Scheme = "https";
        var controller = new ProbeController { ControllerContext = new ControllerContext { HttpContext = http } };
        controller.TempData = new TempDataDictionary(http, new MemoryTempDataProvider());
        return controller;
    }

    private static AjaxEnvelope Envelope(IActionResult result) => Assert.IsType<AjaxEnvelope>(Assert.IsType<JsonResult>(result).Value);
    private static int? Status(IActionResult result) => Assert.IsType<JsonResult>(result).StatusCode;

    [Fact]
    public void Done_Ajax_Returns200Envelope()
    {
        var c = Controller(ajax: true);
        var result = c.CallDone("Guardian linked to this student.", "/Students/Details/5#guardians", "guardians");

        Assert.Equal(200, Status(result));
        var e = Envelope(result);
        Assert.True(e.Ok);
        Assert.Equal("Guardian linked to this student.", e.Message);
        Assert.Equal(new[] { "guardians" }, e.Regions);
        Assert.Null(e.Redirect);
        Assert.False(c.TempData.ContainsKey("Success"));
    }

    [Fact]
    public void Done_Ajax_WithoutRegions_RedirectsTheBrowser()
    {
        var c = Controller(ajax: true);
        var e = Envelope(c.CallDone("Student added.", "/Students/Details/9"));
        Assert.Equal("/Students/Details/9", e.Redirect);
        // The browser loads a new page, so the toast travels the classic way.
        Assert.Equal("Student added.", c.TempData["Success"]);
    }

    [Fact]
    public void FromApi_400WithFieldErrors_IsInvalidKeyedByField()
    {
        var api = ApiResult<object>.Fail("Full name is required.", 400,
            new Dictionary<string, string[]> { ["FullName"] = new[] { "Full name is required." } });
        var result = Controller(ajax: true).CallFromApi(api, "Saved.", null);

        Assert.Equal(400, Status(result));
        var e = Envelope(result);
        Assert.Equal(new[] { "Full name is required." }, e.Errors!["FullName"]);
        Assert.Equal(BaseAdminController.InvalidFormMessage, e.Message);
    }

    [Fact]
    public void Done_NonAjax_RedirectsWithTempData()
    {
        var c = Controller(ajax: false);
        var result = c.CallDone("Guardian linked to this student.", "/Students/Details/5#guardians", "guardians");

        Assert.Equal("/Students/Details/5#guardians", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal("Guardian linked to this student.", c.TempData["Success"]);
    }

    [Fact]
    public void Done_NonAjax_NoRedirect_ReturnsToLocalReferer()
    {
        var result = Controller(ajax: false, referer: "https://admin.test/Students/Details/5?tab=x").CallDone("Saved.", null);
        Assert.Equal("/Students/Details/5?tab=x", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public void Done_NonAjax_ForeignReferer_IsIgnored()
    {
        var result = Controller(ajax: false, referer: "https://evil.test/phish").CallDone("Saved.", null);
        Assert.Equal("/", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public void Failed_Ajax_UsesStatus()
    {
        var result = Controller(ajax: true).CallFailed("The student is already assigned to this bus.", 409, null);
        Assert.Equal(409, Status(result));
        var e = Envelope(result);
        Assert.False(e.Ok);
        Assert.Equal("The student is already assigned to this bus.", e.Message);
    }

    [Fact]
    public void Failed_NonAjax_RedirectsWithErrorTempData()
    {
        var c = Controller(ajax: false);
        var result = c.CallFailed("Nope.", 409, "/Students/Details/5");
        Assert.Equal("/Students/Details/5", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal("Nope.", c.TempData["Error"]);
    }

    [Fact]
    public void Invalid_Ajax_MapsModelStateKeys()
    {
        var c = Controller(ajax: true);
        c.ModelState.AddModelError("FullName", "Required.");
        c.ModelState.AddModelError("", "The API refused the change.");

        var result = c.CallInvalid()!;
        Assert.Equal(400, Status(result));
        var e = Envelope(result);
        Assert.False(e.Ok);
        Assert.Equal(new[] { "Required." }, e.Errors!["FullName"]);
        Assert.Equal(new[] { "The API refused the change." }, e.Errors![""]);
        Assert.Equal("Please fix the highlighted fields.", e.Message);
    }

    [Fact]
    public void Invalid_NonAjax_ReturnsNull()
    {
        var c = Controller(ajax: false);
        c.ModelState.AddModelError("FullName", "Required.");
        Assert.Null(c.CallInvalid());
    }

    [Fact]
    public void FromApi_404_IsFailed404()
    {
        var result = Controller(ajax: true).CallFromApi(ApiResult<object>.Fail("Student not found.", 404), "Saved.", null);
        Assert.Equal(404, Status(result));
        Assert.Equal("Student not found.", Envelope(result).Message);
    }

    [Fact]
    public void FromApi_Success_IsDone()
    {
        var result = Controller(ajax: true).CallFromApi(ApiResult<object>.Ok(new object()), "Saved.", "/Students/Details/5", "address");
        var e = Envelope(result);
        Assert.True(e.Ok);
        Assert.Equal(new[] { "address" }, e.Regions);
    }

    [Fact]
    public void FromApi_StatusZero_IsServerError()
    {
        // ApiResult never carries 0 today, but a failure without a status must not become 200.
        var result = Controller(ajax: true).CallFromApi(ApiResult<object>.Fail("Unreachable.", 0), "Saved.", null);
        Assert.Equal(502, Status(result));
    }

    [Fact]
    public void RefuseForm_Ajax_FieldErrors_AreKeyedByField()
    {
        var api = ApiResult<object>.Fail("Validation failed.", 400,
            new Dictionary<string, string[]> { ["Email"] = new[] { "Enter a valid email." } });
        var (status, e) = (Status(Controller(ajax: true).CallRefuseForm(api)!), Envelope(Controller(ajax: true).CallRefuseForm(api)!));
        Assert.Equal(400, status);
        Assert.Equal(new[] { "Enter a valid email." }, e.Errors!["Email"]);
        Assert.Equal(BaseAdminController.InvalidFormMessage, e.Message);
    }

    [Fact]
    public void RefuseForm_Ajax_Conflict_IsAFormLevelMessage_NotAGenericOne()
    {
        // A duplicate email or number: the admin sees the real reason in the form and in the toast.
        var result = Controller(ajax: true).CallRefuseForm(ApiResult<object>.Fail("That email is already registered.", 409))!;
        Assert.Equal(409, Status(result));
        var e = Envelope(result);
        Assert.Equal("That email is already registered.", e.Message);
        Assert.Equal(new[] { "That email is already registered." }, e.Errors![""]);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(0)]
    public void RefuseForm_Ajax_ServerFailure_IsAToastNotAFormError(int apiStatus)
    {
        var result = Controller(ajax: true).CallRefuseForm(ApiResult<object>.Fail("An unexpected error occurred.", apiStatus))!;
        Assert.Equal(apiStatus >= 500 ? apiStatus : 502, Status(result));
        Assert.Null(Envelope(result).Errors);
    }

    [Fact]
    public void RefuseForm_NonAjax_AddsTheMessageToTheModel_AndReturnsNull()
    {
        var c = Controller(ajax: false);
        Assert.Null(c.CallRefuseForm(ApiResult<object>.Fail("That email is already registered.", 409)));
        Assert.Equal("That email is already registered.", c.State[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public void RefuseForm_BindingErrorsOnTheForm_AreKeptBesideTheirFields()
    {
        var c = Controller(ajax: true);
        c.State.AddModelError("FullName", "The FullName field is required.");
        var result = c.CallRefuseForm(ApiResult<object>.Fail("Full name is required.", 400))!;
        Assert.Equal(new[] { "The FullName field is required." }, Envelope(result).Errors!["FullName"]);
    }

    [Theory]
    [InlineData("fetch", true)]
    [InlineData("XMLHttpRequest", false)]
    [InlineData(null, false)]
    public void IsAjax_OnlyForFetchMarker(string? header, bool expected)
    {
        var http = new DefaultHttpContext();
        if (header is not null) http.Request.Headers[AjaxRequest.HeaderName] = header;
        Assert.Equal(expected, AjaxRequest.IsAjax(http.Request));
    }
}
