using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// School time management: the regular week and the calendar of holidays, half days,
/// special timings and extra working days. All rules live in the API; this page only
/// shows them and posts changes.
/// </summary>
public class SchoolTimeController : BaseAdminController
{
    /// <summary>Upcoming exceptions listed under the calendar.</summary>
    private const int UpcomingDays = 180;

    public SchoolTimeController(ApiClient api) : base(api) { }

    [HttpGet]
    public async Task<IActionResult> Index(int? year, int? month, CancellationToken ct)
    {
        SetPage("School Time", "System");

        var today = await Api.GetAsync<SchoolDayDto>("/api/school-calendar/today", ct);
        var anchor = today.Succeeded ? today.Value!.Date : DateOnly.FromDateTime(DateTime.UtcNow);
        var first = new DateOnly(
            year is >= 2000 and <= 2100 ? year.Value : anchor.Year,
            month is >= 1 and <= 12 ? month.Value : anchor.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var calendar = await Api.GetAsync<SchoolCalendarViewDto>($"/api/school-calendar?from={first:yyyy-MM-dd}&to={last:yyyy-MM-dd}", ct);
        if (!calendar.Succeeded)
        {
            TempData["Error"] = calendar.Error;
            return View("Index", new SchoolTimeViewModel(first, null, new List<SchoolCalendarDayDto>(), anchor));
        }

        var upcoming = await Api.GetAsync<List<SchoolCalendarDayDto>>(
            $"/api/school-calendar/days?from={anchor:yyyy-MM-dd}&to={anchor.AddDays(UpcomingDays):yyyy-MM-dd}", ct);

        return View("Index", new SchoolTimeViewModel(first, calendar.Value, upcoming.Succeeded ? upcoming.Value! : new List<SchoolCalendarDayDto>(), anchor));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(List<DayOfWeek>? workingDays, TimeOnly startTime, TimeOnly lateArrivalAfter,
        TimeOnly missingCheckInAfter, TimeOnly dismissalTime, int pickupGraceMinutes, int attendanceWindowDays, CancellationToken ct)
    {
        var request = new UpdateSchoolTimingSettingsRequest(workingDays ?? new List<DayOfWeek>(), startTime, lateArrivalAfter,
            missingCheckInAfter, dismissalTime, pickupGraceMinutes, attendanceWindowDays);
        var result = await Api.PutAsync<SchoolTimingSettingsDto>("/api/school-calendar/settings", request, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "School timings saved." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDay(int? id, DateOnly date, SchoolDayType dayType, string? title,
        TimeOnly? startTime, TimeOnly? lateArrivalAfter, TimeOnly? dismissalTime, CancellationToken ct)
    {
        var request = new SchoolCalendarDayRequest(date, dayType, title ?? string.Empty, startTime, lateArrivalAfter, dismissalTime);
        var result = id is > 0
            ? await Api.PutAsync<SchoolCalendarDayDto>($"/api/school-calendar/days/{id}", request, ct)
            : await Api.PostAsync<SchoolCalendarDayDto>("/api/school-calendar/days", request, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"{Describe(dayType)} on {date:d MMM yyyy} saved."
            : result.Error;
        return RedirectToAction(nameof(Index), new { year = date.Year, month = date.Month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDay(int id, int? year, int? month, CancellationToken ct)
    {
        var result = await Api.PostAsync($"/api/school-calendar/days/{id}/deactivate", new { }, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Calendar entry removed." : result.Error;
        return RedirectToAction(nameof(Index), new { year, month });
    }

    public static string Describe(SchoolDayType type) => type switch
    {
        SchoolDayType.Holiday => "Holiday",
        SchoolDayType.HalfDay => "Half day",
        SchoolDayType.SpecialTiming => "Special timing",
        _ => "Working day"
    };

    public static string Describe(SchoolDayKind kind) => kind switch
    {
        SchoolDayKind.Regular => "School day",
        SchoolDayKind.NonWorkingDay => "Closed",
        SchoolDayKind.Holiday => "Holiday",
        SchoolDayKind.HalfDay => "Half day",
        SchoolDayKind.SpecialTiming => "Special timing",
        _ => "Working day"
    };
}

/// <param name="Month">First day of the month shown.</param>
/// <param name="Calendar">Null when the API could not be reached.</param>
public record SchoolTimeViewModel(DateOnly Month, SchoolCalendarViewDto? Calendar, List<SchoolCalendarDayDto> Upcoming, DateOnly Today);
