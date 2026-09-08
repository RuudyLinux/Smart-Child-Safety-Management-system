using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class ProfileController : BaseAdminController
{
    public ProfileController(ApiClient api) : base(api) { }

    public IActionResult Index()
    {
        ViewData["Title"] = "Profile";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>("/api/auth/change-password", new ChangePasswordRequest(currentPassword, newPassword), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Password changed." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
