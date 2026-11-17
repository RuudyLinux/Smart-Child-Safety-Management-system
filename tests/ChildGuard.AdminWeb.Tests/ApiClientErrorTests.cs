using System.Net;
using System.Text;
using ChildGuard.AdminWeb.Services;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>The API's validation errors reach AdminWeb keyed by field, so forms can show them inline.</summary>
public class ApiClientErrorTests
{
    private sealed class Fixed(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private static ApiClient Client(HttpStatusCode status, string json) =>
        new(new HttpClient(new Fixed(status, json)) { BaseAddress = new Uri("http://api.test/") });

    [Fact]
    public async Task ValidationErrors_AreKeyedByPropertyName()
    {
        // The shape ExceptionHandlingMiddleware writes for a FluentValidation failure.
        var json = "{\"message\":\"Validation failed.\",\"errors\":[{\"PropertyName\":\"FullName\",\"ErrorMessage\":\"Full name is required.\"},{\"PropertyName\":\"FullName\",\"ErrorMessage\":\"Too short.\"},{\"PropertyName\":\"Grade\",\"ErrorMessage\":\"Too long.\"}]}";
        var result = await Client(HttpStatusCode.BadRequest, json).PutAsync<object>("/api/students/1/record", new { });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(new[] { "Full name is required.", "Too short." }, result.FieldErrors!["FullName"]);
        Assert.Equal(new[] { "Too long." }, result.FieldErrors!["Grade"]);
        // The single message is unchanged for every existing caller.
        Assert.Equal("Full name is required. Too short. Too long.", result.Error);
    }

    [Fact]
    public async Task PlainMessage_HasNoFieldErrors()
    {
        var result = await Client(HttpStatusCode.Conflict, "{\"message\":\"Bus is full.\"}").PostAsync<object>("/api/x", new { });
        Assert.Equal("Bus is full.", result.Error);
        Assert.Null(result.FieldErrors);
    }
}
