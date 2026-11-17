using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// When the API rejected this session's token during the request, the result is
/// replaced: the admin is signed out and sent to sign-in with a "session expired"
/// note. Background (fetch) requests get a 401 the page script turns into the same
/// redirect. Without this, every page rendered as if the school had no data.
/// </summary>
public class ApiSessionExpiredFilter : IAsyncAlwaysRunResultFilter
{
    public const string ItemKey = "cg.apiSessionExpired";
    public const string LoginUrl = "/Account/Login?expired=true";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (http.Items.ContainsKey(ItemKey) && http.User.Identity?.IsAuthenticated == true)
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = WantsHtml(http.Request)
                ? new RedirectResult(LoginUrl)
                : new JsonResult(new AjaxEnvelope(false, ExpiredMessage, LoginUrl, null, null)) { StatusCode = StatusCodes.Status401Unauthorized };
        }
        await next();
    }

    public const string ExpiredMessage = "Your session has expired. Sign in again.";

    /// <summary>
    /// A page navigation rather than a fetch() call. cg-ajax.js region refreshes ask for HTML too, so its
    /// marker header wins over Accept: those must get the 401 envelope, never a sign-in page to splice in.
    /// </summary>
    public static bool WantsHtml(HttpRequest request) =>
        !AjaxRequest.IsAjax(request) &&
        request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
}
