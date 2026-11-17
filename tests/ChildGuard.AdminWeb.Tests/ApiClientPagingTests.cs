using System.Net;
using System.Text;
using ChildGuard.AdminWeb.Services;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>Paged API lists: the rows come in the body, the total in X-Total-Count.</summary>
public class ApiClientPagingTests
{
    private sealed class Fixed(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(answer());
    }

    private static ApiClient Client(Func<HttpResponseMessage> answer) =>
        new(new HttpClient(new Fixed(answer)) { BaseAddress = new Uri("http://api.test/") });

    private static HttpResponseMessage Json(string json, string? total)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (total is not null) r.Headers.Add("X-Total-Count", total);
        return r;
    }

    [Fact]
    public async Task ReadsItemsAndTotal()
    {
        var result = await Client(() => Json("[1,2,3]", "41")).GetPagedAsync<int>("/api/x?page=1");
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { 1, 2, 3 }, result.Value!.Items);
        Assert.Equal(41, result.Value.Total);
    }

    [Fact]
    public async Task MissingTotal_IsAnError()
    {
        // A paged call that comes back without its total is an API that did not page: never guess.
        var result = await Client(() => Json("[1]", null)).GetPagedAsync<int>("/api/x?page=1");
        Assert.False(result.Succeeded);
        Assert.Equal(502, result.StatusCode);
    }

    [Fact]
    public async Task ApiError_PassesThrough()
    {
        var result = await Client(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"message\":\"sort must be one of: name.\"}", Encoding.UTF8, "application/json")
        }).GetPagedAsync<int>("/api/x?page=1&sort=bad");
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("sort must be one of: name.", result.Error);
    }
}
