using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class AttendanceController : BaseAdminController
{
    public AttendanceController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? studentId, CancellationToken ct)
    {
        SetPage("Attendance", "Safety Operations");

        // A blank page defaults to the last seven days rather than the whole
        // history, which is both faster and the range an admin usually wants.
        var today = SchoolTime.TodayDate;
        var rangeTo = to ?? today;
        var rangeFrom = from ?? rangeTo.AddDays(-6);

        var query = $"/api/attendance?from={rangeFrom:yyyy-MM-dd}&to={rangeTo:yyyy-MM-dd}"
                    + (studentId.HasValue ? $"&studentId={studentId}" : "");
        var records = await Api.GetAsync<List<AttendanceRecordDto>>(query, ct);
        if (!records.Succeeded) ViewBag.LoadError = records.Error;

        var summary = await Api.GetAsync<DashboardSummaryDto>("/api/dashboard/summary", ct);

        ViewData["From"] = rangeFrom;
        ViewData["To"] = rangeTo;
        ViewData["StudentId"] = studentId;
        ViewBag.Summary = summary.Value;

        var rows = records.Value ?? new();
        ViewBag.Grades = rows
            .Select(r => r.Grade)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Arrivals per day drive the trend chart; departures are excluded so a
        // full school day is not counted as two attendances.
        ViewBag.DailyArrivals = rows
            .Where(r => r.EventType == AttendanceEventType.SchoolArrival)
            .GroupBy(r => r.Date)
            .Select(g => (Date: g.Key, Present: g.Select(r => r.StudentId).Distinct().Count(), Late: g.Count(r => r.IsLate)))
            .OrderBy(g => g.Date)
            .ToList();

        return View(rows);
    }
}
