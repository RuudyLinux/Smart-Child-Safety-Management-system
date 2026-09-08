using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class JourneyController : BaseAdminController
{
    public JourneyController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Journey";
        var result = await Api.GetAsync<List<JourneyEventDto>>("/api/journey/recent?take=50", ct);
        return View(result.Value ?? new());
    }

    public async Task<IActionResult> Student(int id, CancellationToken ct)
    {
        ViewData["Title"] = "Student Journey Details";
        var result = await Api.GetAsync<List<JourneyEventDto>>($"/api/journey/{id}", ct);
        ViewData["StudentId"] = id;
        return View(result.Value ?? new());
    }
}
