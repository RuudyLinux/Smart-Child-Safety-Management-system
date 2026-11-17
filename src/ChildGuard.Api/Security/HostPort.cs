namespace ChildGuard.Api.Security;

/// <summary>
/// Listens on the port the host assigns. Render, Railway, Fly.io, Heroku, Cloud Run and similar hosts pass it
/// in the <c>PORT</c> environment variable; the app then listens on every address on that port. An explicit
/// <c>ASPNETCORE_URLS</c> or <c>--urls</c> always wins, and without <c>PORT</c> nothing changes.
/// </summary>
public static class HostPort
{
    public static void Apply(WebApplicationBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["urls"])) return;
        var port = Environment.GetEnvironmentVariable("PORT");
        if (int.TryParse(port, out var number) && number is > 0 and <= 65535)
            builder.WebHost.UseUrls($"http://+:{number}");
    }
}
