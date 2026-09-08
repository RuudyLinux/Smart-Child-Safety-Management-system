using ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

[Authorize]
public abstract class BaseAdminController : Controller
{
    protected readonly ApiClient Api;
    protected BaseAdminController(ApiClient api) => Api = api;
}
