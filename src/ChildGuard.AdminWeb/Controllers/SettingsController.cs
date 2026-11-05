using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// Read-only system/operational settings — distinct from Profile (personal account
/// info + password). Shows the state an admin actually needs at a glance: which
/// checkpoint scanners are configured and their live connection status.
/// </summary>
public class SettingsController : BaseAdminController
{
    private readonly IWebHostEnvironment _env;
    public SettingsController(ApiClient api, IWebHostEnvironment env) : base(api) => _env = env;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Settings", "System");
        var devices = await Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);
        ViewBag.Devices = devices.Value ?? new();
        ViewBag.Environment = _env.EnvironmentName;
        return View();
    }
}
