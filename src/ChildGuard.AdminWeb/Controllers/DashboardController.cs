using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class DashboardController : BaseAdminController
{
    public DashboardController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Dashboard", "Overview");
        var result = await Api.GetAsync<DashboardSummaryDto>("/api/dashboard/summary", ct);
        if (!result.Succeeded) ViewBag.LoadError = result.Error;
        return View(result.Value);
    }

    /// <summary>
    /// Feeds the topbar alert badge on every page. It returns only what the badge
    /// draws — a count and the top few alerts — so the shell never pulls the whole
    /// dashboard payload just to decide whether to show a dot.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AlertsSummary(CancellationToken ct)
    {
        var result = await Api.GetAsync<DashboardSummaryDto>("/api/dashboard/summary", ct);
        if (!result.Succeeded || result.Value is null)
            return StatusCode(result.StatusCode == 0 ? 503 : result.StatusCode, new { message = result.Error });

        var alerts = result.Value.Alerts;
        return Json(new
        {
            critical = alerts.Count(a => a.Severity == Domain.Enums.AlertSeverity.Critical),
            total = alerts.Count,
            alerts = alerts.Take(5).Select(a => new
            {
                severity = a.Severity.ToString(),
                title = a.Title,
                detail = a.Detail,
                link = a.Link
            })
        });
    }
}
