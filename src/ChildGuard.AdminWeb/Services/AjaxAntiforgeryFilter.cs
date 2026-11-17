using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;

namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// When [ValidateAntiForgeryToken] refuses an AJAX request (a page opened before the admin signed in again, or
/// a changed key ring), the framework answers an empty 400 and the page could only say "try again", which can
/// never work with that token. This replaces only that answer, for AJAX requests, with an envelope that asks for
/// a reload. The validation itself is untouched: the request was already refused before this runs.
/// </summary>
public sealed class AjaxAntiforgeryFilter : IAlwaysRunResultFilter
{
    public const string ExpiredPageMessage = "This page has expired. Reload the page, then make the change again.";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IAntiforgeryValidationFailedResult && AjaxRequest.IsAjax(context.HttpContext.Request))
            context.Result = new JsonResult(new AjaxEnvelope(false, ExpiredPageMessage, null, null, null))
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
