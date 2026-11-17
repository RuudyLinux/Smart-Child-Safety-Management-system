using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// Connectivity check page. Deliberately anonymous and deliberately not derived
/// from <see cref="BaseAdminController"/>: its whole job is to work when nothing
/// else does, including when the API is down and nobody can sign in. It performs
/// no privileged work itself — every check runs in the visitor's browser against
/// the API, so what it proves is exactly what a browser can do.
/// </summary>
public class DiagnosticsController : Controller
{
    private readonly IConfiguration _config;
    public DiagnosticsController(IConfiguration config) => _config = config;

    public IActionResult Index()
    {
        ViewData["Title"] = "API Diagnostics";
        ViewData["ApiBaseUrl"] = _config["Api:BaseUrl"] ?? "http://localhost:5275";
        return View();
    }
}
