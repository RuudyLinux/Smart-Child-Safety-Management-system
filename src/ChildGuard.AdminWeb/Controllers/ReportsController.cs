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

    private const int JourneyPageSize = 50;

    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? studentId, int journeyPage = 1, CancellationToken ct = default)
    {
        SetPage("Reports", "Insights");

        var today = SchoolTime.TodayDate;
        var rangeTo = to ?? today;
        var rangeFrom = from ?? rangeTo.AddDays(-29);
        journeyPage = Math.Max(journeyPage, 1);

        // Journey and verification are filtered by date on the server, so nothing in
        // the chosen range is silently cut off by a fixed "latest N" window.
        var attendanceTask = Api.GetAsync<List<AttendanceRecordDto>>(BuildAttendanceQuery(rangeFrom, rangeTo, studentId), ct);
        var journeyTask = Api.GetAsync<JourneyReportPageDto>(
            $"/api/reports/journey/events?{RangeQuery(rangeFrom, rangeTo, studentId)}&page={journeyPage}&pageSize={JourneyPageSize}", ct);
        var verificationTask = Api.GetAsync<List<GuardianVerificationHistoryDto>>(
            $"/api/reports/verification?{RangeQuery(rangeFrom, rangeTo, studentId)}", ct);
        var studentsTask = Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        var summaryTask = Api.GetAsync<DashboardSummaryDto>("/api/dashboard/summary", ct);
        await Task.WhenAll(attendanceTask, journeyTask, verificationTask, studentsTask, summaryTask);

        var attendance = attendanceTask.Result;
        var journey = journeyTask.Result;
        var verification = verificationTask.Result;
        var students = studentsTask.Result;
        var summary = summaryTask.Result;

        ViewBag.JourneyError = journey.Succeeded ? null : journey.Error;
        ViewData["From"] = rangeFrom;
        ViewData["To"] = rangeTo;
        ViewData["StudentId"] = studentId;
        ViewBag.Attendance = attendance.Value ?? new();
        ViewBag.JourneyPage = journey.Value
            ?? new JourneyReportPageDto(Array.Empty<JourneyEventDto>(), 0, 1, JourneyPageSize, rangeFrom, rangeTo, studentId);
        ViewBag.Verification = verification.Value ?? new();
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
        var today = SchoolTime.TodayDate;
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

    private static string RangeQuery(DateOnly from, DateOnly to, int? studentId) =>
        $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" + (studentId.HasValue ? $"&studentId={studentId}" : "");

    /// <summary>Quotes a CSV field, doubling any embedded quote, so a name with a comma cannot shift columns.</summary>
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
