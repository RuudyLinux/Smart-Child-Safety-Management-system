using System.Globalization;
using System.Text;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class ReportsController : BaseAdminController
{
    public ReportsController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? studentId, CancellationToken ct)
    {
        SetPage("Reports", "Insights");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var rangeTo = to ?? today;
        var rangeFrom = from ?? rangeTo.AddDays(-29);

        var attendanceTask = Api.GetAsync<List<AttendanceRecordDto>>(BuildAttendanceQuery(rangeFrom, rangeTo, studentId), ct);
        var journeyTask = Api.GetAsync<List<JourneyEventDto>>("/api/journey/recent?take=200", ct);
        var verificationTask = Api.GetAsync<List<GuardianVerificationHistoryDto>>(
            studentId.HasValue ? $"/api/reports/verification?studentId={studentId}" : "/api/reports/verification", ct);
        var studentsTask = Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        var summaryTask = Api.GetAsync<DashboardSummaryDto>("/api/dashboard/summary", ct);
        await Task.WhenAll(attendanceTask, journeyTask, verificationTask, studentsTask, summaryTask);

        var attendance = attendanceTask.Result;
        var journey = journeyTask.Result;
        var verification = verificationTask.Result;
        var students = studentsTask.Result;
        var summary = summaryTask.Result;

        var attendanceRows = attendance.Value ?? new();
        // Journey events have no server-side date filter, so the page narrows the
        // recent window itself rather than showing rows outside the chosen range.
        var journeyRows = (journey.Value ?? new())
            .Where(j => InRange(DateOnly.FromDateTime(j.Timestamp.ToLocalTime()), rangeFrom, rangeTo))
            .Where(j => !studentId.HasValue || j.StudentId == studentId)
            .ToList();
        var verificationRows = (verification.Value ?? new())
            .Where(v => InRange(DateOnly.FromDateTime(v.AttemptedAt.ToLocalTime()), rangeFrom, rangeTo))
            .ToList();

        ViewData["From"] = rangeFrom;
        ViewData["To"] = rangeTo;
        ViewData["StudentId"] = studentId;
        ViewBag.Attendance = attendanceRows;
        ViewBag.Journey = journeyRows;
        ViewBag.Verification = verificationRows;
        ViewBag.Students = students.Value ?? new();
        ViewBag.Summary = summary.Value;
        return View();
    }

    /// <summary>
    /// Exports the attendance rows currently on screen as CSV. Only fields already
    /// visible in the report are written — no identifier is added to the file that
    /// the page itself does not show.
    /// </summary>
    public async Task<IActionResult> ExportAttendance(DateOnly? from, DateOnly? to, int? studentId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rangeTo = to ?? today;
        var rangeFrom = from ?? rangeTo.AddDays(-29);

        var result = await Api.GetAsync<List<AttendanceRecordDto>>(BuildAttendanceQuery(rangeFrom, rangeTo, studentId), ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index), new { from, to, studentId });
        }

        var csv = new StringBuilder();
        csv.AppendLine("Student,Grade,Event,Date,Time,Late");
        foreach (var row in result.Value ?? new())
        {
            csv.AppendLine(string.Join(',',
                Csv(row.StudentName),
                Csv(row.Grade ?? ""),
                Csv(row.EventType.ToString()),
                Csv(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(row.Time.ToString("HH:mm", CultureInfo.InvariantCulture)),
                Csv(row.IsLate ? "Yes" : "No")));
        }

        var fileName = $"childguard-attendance-{rangeFrom:yyyyMMdd}-{rangeTo:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
    }

    private static string BuildAttendanceQuery(DateOnly from, DateOnly to, int? studentId) =>
        $"/api/reports/attendance?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
        + (studentId.HasValue ? $"&studentId={studentId}" : "");

    private static bool InRange(DateOnly value, DateOnly from, DateOnly to) => value >= from && value <= to;

    /// <summary>Quotes a CSV field, doubling any embedded quote, so a name with a comma cannot shift columns.</summary>
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
