using ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ChildGuard.AdminWeb.Controllers;

[Authorize]
public abstract class BaseAdminController : Controller
{
    protected readonly ApiClient Api;
    protected BaseAdminController(ApiClient api) => Api = api;

    /// <summary>
    /// Sets the title and the navigation section the layout renders in the topbar
    /// and breadcrumb, so every page states where it sits without repeating the
    /// markup for it.
    /// </summary>
    protected void SetPage(string title, string? section = null)
    {
        ViewData["Title"] = title;
        ViewData["Section"] = section;
    }

    public const string InvalidFormMessage = "Please fix the highlighted fields.";

    /// <summary>
    /// A successful write. AJAX: a 200 envelope that refreshes <paramref name="regions"/> in place, or, with no
    /// regions, sends the browser to <paramref name="redirectUrl"/>. Without JavaScript: the classic
    /// Post-Redirect-Get with a success toast.
    /// </summary>
    protected IActionResult Done(string message, string? redirectUrl = null, params string[] regions)
    {
        if (AjaxRequest.IsAjax(Request))
        {
            var refresh = regions is { Length: > 0 };
            // A redirect loads a new page: the toast goes with it the classic way.
            if (!refresh && redirectUrl is not null) TempData["Success"] = message;
            return Envelope(200, new AjaxEnvelope(true, message, refresh ? null : redirectUrl, refresh ? regions : null, null));
        }
        TempData["Success"] = message;
        return Redirect(redirectUrl ?? LocalReferer());
    }

    /// <summary>A refused or failed write: an error envelope with the matching status, or PRG with an error toast.</summary>
    protected IActionResult Failed(string message, int status = 400, string? redirectUrl = null)
    {
        if (AjaxRequest.IsAjax(Request))
            return Envelope(status, new AjaxEnvelope(false, message, null, null, null));
        TempData["Error"] = message;
        return Redirect(redirectUrl ?? LocalReferer());
    }

    /// <summary>
    /// Form validation errors. AJAX: a 400 envelope keyed by field. Otherwise null, so the caller re-renders its
    /// view with the errors as before: <c>return Invalid(ModelState) ?? View(model);</c>
    /// </summary>
    protected IActionResult? Invalid(ModelStateDictionary modelState, string? message = null)
    {
        if (!AjaxRequest.IsAjax(Request)) return null;
        var errors = modelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
        return Envelope(400, new AjaxEnvelope(false, message ?? InvalidFormMessage, null, null, errors));
    }

    /// <summary>Maps an API call's outcome to <see cref="Done"/> or <see cref="Failed"/> with the API's own status.</summary>
    protected IActionResult FromApi<T>(ApiResult<T> result, string successMessage, string? redirectUrl = null, params string[] regions)
    {
        if (result.Succeeded) return Done(successMessage, redirectUrl, regions);
        if (AjaxRequest.IsAjax(Request) && AddApiErrors(result)) return Invalid(ModelState)!;
        return Failed(result.Error ?? "The request failed.", result.StatusCode is >= 400 and <= 599 ? result.StatusCode : 502, redirectUrl);
    }

    /// <summary>Copies the API's field validation errors into ModelState; false when it sent none.</summary>
    protected bool AddApiErrors<T>(ApiResult<T> result)
    {
        if (result.StatusCode != 400 || result.FieldErrors is not { Count: > 0 }) return false;
        foreach (var (field, messages) in result.FieldErrors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return true;
    }

    private static JsonResult Envelope(int status, AjaxEnvelope envelope) => new(envelope) { StatusCode = status };

    /// <summary>Where a no-JS post returns when the action names no page: the referring page on this site, else home.</summary>
    private string LocalReferer()
    {
        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase))
            return uri.PathAndQuery;
        return "/";
    }
}
