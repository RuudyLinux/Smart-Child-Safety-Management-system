using ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

[Authorize]
public abstract class BaseAdminController : Controller
{
    protected readonly ApiClient Api;
    protected BaseAdminController(ApiClient api) => Api = api;

    /// <summary>
    /// Sets the title and the navigation section the layout renders in the topbar
    /// and breadcrumb, so every page states where it sits without repeating the
    /// markup for it.
    /// </summary>
    protected void SetPage(string title, string? section = null)
    {
        ViewData["Title"] = title;
        ViewData["Section"] = section;
    }
}
