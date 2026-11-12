using ChildGuard.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// A browser that navigates away mid-request (e.g. leaving the dashboard while it polls)
/// cancels the request. That is not a server fault: it must not be logged as an error
/// or answered with a 500. A cancellation the client did not cause still is a fault.
/// </summary>
public class ClientCancellationTests
{
    private static DefaultHttpContext Context(CancellationToken aborted)
    {
        var ctx = new DefaultHttpContext { RequestAborted = aborted };
        ctx.Request.Path = "/api/dashboard/summary";
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task RequestAbortedByClient_IsNotTreatedAsServerError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ctx = Context(cts.Token);
        var middleware = new ExceptionHandlingMiddleware(
            c => Task.FromCanceled(c.RequestAborted),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        Assert.Equal(499, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task CancellationNotCausedByClient_IsStillA500()
    {
        var ctx = Context(CancellationToken.None);
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        Assert.Equal(500, ctx.Response.StatusCode);
    }
}
