using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class ReportsController : BaseAdminController
{
    public ReportsController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Reports";
        var attendance = await Api.GetAsync<List<AttendanceRecordDto>>("/api/reports/attendance", ct);
        var verification = await Api.GetAsync<List<GuardianVerificationHistoryDto>>("/api/reports/verification", ct);
        ViewBag.Attendance = attendance.Value ?? new();
        ViewBag.Verification = verification.Value ?? new();
        return View();
    }
}
