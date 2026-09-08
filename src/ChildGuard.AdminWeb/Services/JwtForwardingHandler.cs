using System.Net.Http.Headers;

namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// Attaches the admin's API JWT (stored as a claim in the AdminWeb auth cookie
/// after login) to every outgoing call to ChildGuard.Api.
/// </summary>
public class JwtForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public JwtForwardingHandler(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = _httpContextAccessor.HttpContext?.User.FindFirst("api_token")?.Value;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, ct);
    }
}
