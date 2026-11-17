using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class JourneyController : BaseAdminController
{
    public JourneyController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Journey Tracking", "Safety Operations");

        var live = await Api.GetAsync<List<StudentJourneyStatusDto>>("/api/journey/live", ct);
        var recent = await Api.GetAsync<List<JourneyEventDto>>("/api/journey/recent?take=40", ct);
        if (!live.Succeeded) ViewBag.LoadError = live.Error;

        ViewBag.Recent = recent.Value ?? new();
        return View(live.Value ?? new());
    }

    public async Task<IActionResult> Student(int id, CancellationToken ct)
    {
        var student = await Api.GetAsync<StudentDetailDto>($"/api/students/{id}", ct);
        if (!student.Succeeded) return NotFound();

        SetPage($"{student.Value!.FullName} — journey", "Safety Operations");
        var events = await Api.GetAsync<List<JourneyEventDto>>($"/api/journey/{id}", ct);
        ViewBag.Student = student.Value;
        return View(events.Value ?? new());
    }
}
