using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class DashboardController : BaseAdminController
{
    public DashboardController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Dashboard";
        var result = await Api.GetAsync<DashboardSummaryDto>("/api/dashboard/summary", ct);
        return View(result.Value);
    }
}
