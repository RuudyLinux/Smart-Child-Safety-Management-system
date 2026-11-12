using System.Net;
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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var context = _httpContextAccessor.HttpContext;
        var token = context?.User.FindFirst("api_token")?.Value;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);

        // The API no longer accepts this session's token (expired, or the signing key
        // changed). Flag it so ApiSessionExpiredFilter ends the session instead of the
        // page rendering empty data as if there were none.
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token) && context is not null)
            context.Items[ApiSessionExpiredFilter.ItemKey] = true;

        return response;
    }
}
