using System.Net;
using System.Text.Json;
using FluentValidation;

namespace ChildGuard.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException vex)
        {
            _logger.LogWarning(vex, "Validation failed for {Path}", context.Request.Path);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            var errors = vex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage });
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Validation failed.", errors }));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away (page navigation, refresh, closed tab). Nothing failed
            // server-side and nobody is listening for a response.
            _logger.LogInformation("Request {Path} was cancelled by the client", context.Request.Path);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499; // Client Closed Request (nginx convention)
        }
        catch (Exception ex) when (ExceedsDatabasePacketLimit(ex))
        {
            // A photo bigger than the database server's max_allowed_packet. The request
            // is refused cleanly; nothing was written.
            _logger.LogWarning(ex, "Upload for {Path} exceeds the database max_allowed_packet", context.Request.Path);
            context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                message = "This image is too large for the database server to store. Use a smaller photo, " +
                          "or ask the administrator to raise MySQL's max_allowed_packet (16M recommended)."
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "An unexpected error occurred." }));
        }
    }

    private static bool ExceedsDatabasePacketLimit(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains("max_allowed_packet", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
