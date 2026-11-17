namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// Where the API lives, read once from <c>Api:BaseUrl</c>. Any host, port and path prefix works:
/// <c>http://localhost:5275</c>, <c>http://192.168.1.20:8080</c>, <c>https://api.school.org</c> or
/// <c>https://school.org/childguard-api</c>. A value without a scheme gets https (http for a local
/// address). A value that cannot be an API address stops startup with a message saying so.
/// </summary>
public sealed class ApiEndpoint
{
    public const string DefaultBaseUrl = "http://localhost:5275";

    /// <summary>Absolute http(s) address, always ending in "/" so relative paths keep any prefix.</summary>
    public Uri BaseUri { get; }

    public ApiEndpoint(Uri baseUri) => BaseUri = baseUri;

    public static ApiEndpoint From(IConfiguration config) => new(Parse(config["Api:BaseUrl"]));

    public static Uri Parse(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
            raw = (IsLocal(raw) ? "http://" : "https://") + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException(
                "Api:BaseUrl must be the API's http(s) address, e.g. https://api.example.org or http://192.168.1.20:5275.");

        var path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
        return new UriBuilder(uri) { Path = path }.Uri;
    }

    /// <summary>An API path such as "/api/health" under the base address, keeping any path prefix.</summary>
    public Uri Resolve(string path) => new(BaseUri, path.TrimStart('/'));

    /// <summary>The base address without the trailing slash, for display and for browser-side joins.</summary>
    public override string ToString() => BaseUri.ToString().TrimEnd('/');

    private static bool IsLocal(string hostAndRest)
    {
        var host = hostAndRest.Split('/', ':')[0].ToLowerInvariant();
        return host is "localhost" or "127.0.0.1" or "" || host.StartsWith("192.168.") || host.StartsWith("10.")
               || host.EndsWith(".local");
    }
}

/// <summary>
/// ApiClient callers use absolute paths ("/api/students"), which HttpClient resolves against the host root
/// and so would drop a path prefix in Api:BaseUrl. This puts the prefix back.
/// </summary>
public sealed class ApiPathBaseHandler : DelegatingHandler
{
    private readonly ApiEndpoint _endpoint;
    public ApiPathBaseHandler(ApiEndpoint endpoint) => _endpoint = endpoint;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var prefix = _endpoint.BaseUri.AbsolutePath;
        if (prefix != "/" && request.RequestUri is { IsAbsoluteUri: true } uri
            && uri.Authority == _endpoint.BaseUri.Authority
            && !uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            request.RequestUri = _endpoint.Resolve(uri.PathAndQuery);
        return base.SendAsync(request, ct);
    }
}
