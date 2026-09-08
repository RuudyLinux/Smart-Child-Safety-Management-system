using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class AttendanceController : BaseAdminController
{
    public AttendanceController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        ViewData["Title"] = "Attendance";
        var query = $"/api/attendance?{(studentId.HasValue ? $"studentId={studentId}&" : "")}{(from.HasValue ? $"from={from:yyyy-MM-dd}&" : "")}{(to.HasValue ? $"to={to:yyyy-MM-dd}&" : "")}";
        var result = await Api.GetAsync<List<AttendanceRecordDto>>(query, ct);
        ViewData["StudentId"] = studentId;
        ViewData["From"] = from;
        ViewData["To"] = to;
        return View(result.Value ?? new());
    }
}
