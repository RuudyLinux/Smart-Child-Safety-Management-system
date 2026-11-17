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

    // Free hosting puts the API to sleep when idle; it answers 502/503/504 while it wakes.
    private static readonly int[] WakingStatuses = { 502, 503, 504 };
    private const int SignInAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly HttpClient WakeClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly ApiClient _api;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _log;

    // The long-timeout client: a sign-in that arrives while the API is waking waits for it instead of failing.
    public AccountController(LongRunningApiClient api, IConfiguration config, ILogger<AccountController> log)
    {
        _api = api;
        _config = config;
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
        var baseUrl = _config["Api:BaseUrl"] ?? "http://localhost:5275";
        _ = Task.Run(async () =>
        {
            try { using var _ = await WakeClient.GetAsync(new Uri(new Uri(baseUrl), "/api/health")); }
            catch { /* best effort */ }
        });
    }

    /// <summary>Signs in, retrying while the API wakes up. Only a 401 from the API means the credentials are wrong.</summary>
    private async Task<(ApiResult<LoginResponse>? Result, string? Error)> SignInAsync(LoginViewModel model, CancellationToken ct)
    {
        // A cold start takes well under a minute; past this the admin gets an answer instead of a spinning page.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(100));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await _api.PostAsync<LoginResponse>("/api/auth/admin/login", new LoginRequest(model.Email, model.Password), budget.Token);
                if (result.Succeeded) return (result, null);
                if (result.StatusCode is 400 or 401 or 403) return (null, InvalidCredentials);
                if (result.StatusCode == 429) return (null, TooManyAttempts);
                if (!WakingStatuses.Contains(result.StatusCode)) return (null, ServerUnavailable);
                _log.LogWarning("Sign-in attempt {Attempt}: the API answered {Status} while waking", attempt, result.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Sign-in attempt {Attempt} could not reach the API: {Error}", attempt, ex.GetType().Name);
            }
            if (attempt >= SignInAttempts || budget.IsCancellationRequested) return (null, ServerUnavailable);
            try { await Task.Delay(RetryDelay, budget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (null, ServerUnavailable); }
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
