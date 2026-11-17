namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// The one JSON shape every AJAX-aware AdminWeb action answers with (wwwroot/js/cg-ajax.js reads it).
/// <para><c>Regions</c> names the page parts to re-render in place; <c>Redirect</c> sends the browser to a new
/// page instead; <c>Errors</c> is keyed by form field name (ModelState key, "" for form-level messages).</para>
/// </summary>
public sealed record AjaxEnvelope(
    bool Ok,
    string? Message,
    string? Redirect,
    IReadOnlyList<string>? Regions,
    IReadOnlyDictionary<string, string[]>? Errors);

/// <summary>Recognises requests made by cg-ajax.js.</summary>
public static class AjaxRequest
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "fetch";

    public static bool IsAjax(HttpRequest request) =>
        string.Equals(request.Headers[HeaderName].ToString(), HeaderValue, StringComparison.Ordinal);
}
