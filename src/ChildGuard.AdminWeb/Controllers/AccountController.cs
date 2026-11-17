using System.Security.Claims;
using ChildGuard.AdminWeb.Models.ViewModels;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class AccountController : Controller
{
    public const string InvalidCredentials = "Invalid email or password.";
    public const string TooManyAttempts = "Too many sign-in attempts. Wait a minute and try again.";
    public const string ServerUnavailable = "The ChildGuard server is starting up or unavailable. Wait a few seconds and try again.";

    // Free hosting puts the API (and its database) to sleep when idle. While they wake the API can
    // answer 502/503/504 from the proxy, 500 when the first database connection times out, 408/425,
    // or a non-JSON 200 page. All of those are retried until the time budget runs out; only a 4xx
    // from the API itself is a final answer.
    private static readonly TimeSpan SignInBudget = TimeSpan.FromSeconds(100);
    // Growing pauses keep the retries well under the API's sign-in rate limit (10 a minute per caller,
    // and every admin sign-in reaches the API from this server's address).
    private static readonly int[] RetryDelaySeconds = { 3, 6, 10, 15, 20 };
    private static readonly HttpClient WakeClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly ApiClient _api;
    private readonly ApiEndpoint _endpoint;
    private readonly ILogger<AccountController> _log;

    // The long-timeout client: a sign-in that arrives while the API is waking waits for it instead of failing.
    public AccountController(LongRunningApiClient api, ApiEndpoint endpoint, ILogger<AccountController> log)
    {
        _api = api;
        _endpoint = endpoint;
        _log = log;
    }

    [HttpGet]
    public IActionResult Login(bool expired = false)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        if (expired) TempData["Info"] = "Your session expired. Please sign in again.";
        WakeApi();
        return View(new LoginViewModel());
    }

    /// <summary>
    /// Starts waking a sleeping API while the admin types, so the sign-in itself is fast. Fire and forget:
    /// the page never waits for it, and a failure here changes nothing.
    /// </summary>
    private void WakeApi()
    {
        var health = _endpoint.Resolve("/api/health");
        _ = Task.Run(async () =>
        {
            try { using var _ = await WakeClient.GetAsync(health); }
            catch { /* best effort */ }
        });
    }

    /// <summary>
    /// Polled by the sign-in page while the admin types, so it can say "connecting" instead of letting a
    /// sign-in run into a sleeping server. /api/health touches the database, so this wakes it too.
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ApiStatus(CancellationToken ct)
    {
        try
        {
            using var response = await ProbeClient.GetAsync(_endpoint.Resolve("/api/health"), ct);
            return Json(new { ready = response.IsSuccessStatusCode });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Json(new { ready = false });
        }
    }

    /// <summary>A 4xx from the API itself is a final answer; anything else may be a server still waking.</summary>
    internal static bool IsTransient(int status) => status is 0 or 408 or 425 || status >= 500 || status is >= 200 and < 300;

    /// <summary>Signs in, retrying while the API wakes up. Only a 4xx from the API means the credentials were refused.</summary>
    private async Task<(ApiResult<LoginResponse>? Result, string? Error)> SignInAsync(LoginViewModel model, CancellationToken ct)
    {
        // A cold start (web service + database) can take about a minute; past the budget the admin gets an
        // answer instead of a spinning page.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(SignInBudget);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await _api.PostAsync<LoginResponse>("/api/auth/admin/login", new LoginRequest(model.Email, model.Password), budget.Token);
                if (result.Succeeded) return (result, null);
                if (result.StatusCode == 429) return (null, TooManyAttempts);
                if (!IsTransient(result.StatusCode))
                {
                    if (result.StatusCode is 400 or 401 or 403) return (null, InvalidCredentials);
                    _log.LogWarning("Sign-in refused: the API answered {Status}: {Error}", result.StatusCode, result.Error);
                    return (null, ServerUnavailable);
                }
                _log.LogWarning("Sign-in attempt {Attempt}: the API answered {Status} ({Error}); retrying", attempt, result.StatusCode, result.Error);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Sign-in attempt {Attempt} could not reach the API: {Error}: {Message}", attempt, ex.GetType().Name, ex.Message);
            }
            if (budget.IsCancellationRequested)
            {
                _log.LogError("Sign-in gave up after {Attempts} attempts in {Budget}", attempt, SignInBudget);
                return (null, ServerUnavailable);
            }
            var delay = TimeSpan.FromSeconds(RetryDelaySeconds[Math.Min(attempt, RetryDelaySeconds.Length) - 1]);
            try { await Task.Delay(delay, budget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _log.LogError("Sign-in gave up after {Attempts} attempts in {Budget}", attempt, SignInBudget);
                return (null, ServerUnavailable);
            }
        }
    }

    /// <summary>Signing out clears the cookie and returns to the sign-in page with a confirmation.</summary>

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var (result, error) = await SignInAsync(model, ct);
        if (result is null)
        {
            model.ErrorMessage = error;
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Value!.UserId.ToString()),
            new(ClaimTypes.Name, result.Value.FullName),
            new(ClaimTypes.Email, result.Value.Email),
            new(ClaimTypes.Role, result.Value.Role),
            new("api_token", result.Value.Token)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            // "Keep me signed in" is the only thing that makes the cookie survive a
            // browser restart; without it the session ends when the browser closes.
            // The cookie never outlives the API token it carries: a sliding cookie
            // holding an expired token rendered every page empty.
            new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(result.Value.ExpiresAtUtc, DateTimeKind.Utc)),
                AllowRefresh = false
            });

        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Info"] = "You have been signed out.";
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // API always returns a generic success — it never reveals whether the account exists.
        var result = await _api.PostAsync("/api/auth/forgot-password", new ForgotPasswordRequest(model.Email, "Admin"));
        model.InfoMessage = result.Succeeded
            ? "If that account exists, a reset token has been sent."
            : "Could not process the request right now. Please try again.";
        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPassword() => View(new ResetPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _api.PostAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest(model.Email, "Admin", model.ResetToken, model.NewPassword));

        if (!result.Succeeded)
        {
            model.ErrorMessage = result.Error ?? "Could not reset password. Check the token and try again.";
            return View(model);
        }

        model.SuccessMessage = "Password reset. You can sign in now.";
        return View(model);
    }

    public IActionResult Error() => View();
}
