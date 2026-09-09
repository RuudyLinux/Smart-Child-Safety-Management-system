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
    private readonly ApiClient _api;
    public AccountController(ApiClient api) => _api = api;

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _api.PostAsync<LoginResponse>("/api/auth/admin/login", new LoginRequest(model.Email, model.Password));
        if (!result.Succeeded)
        {
            model.ErrorMessage = "Invalid email or password.";
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
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
