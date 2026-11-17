using ChildGuard.AdminWeb.Models.ViewModels;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>Read-only view of the audit trail. Filtering and paging are done by the API.</summary>
public class AuditController : BaseAdminController
{
    public static readonly string[] EntityTypes =
        { "Student", "Guardian", "RfidCard", "RfidDevice", "Admin", "Pickup", "Import", "Driver", "Route", "Bus", "Assignment" };

    public AuditController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(AuditFilter filter, CancellationToken ct)
    {
        SetPage("Audit Log", "System");
        var query = new List<string> { $"page={Math.Max(1, filter.Page)}", "pageSize=50" };
        void Add(string key, object? value)
        {
            if (value is not null && !(value is string s && string.IsNullOrWhiteSpace(s)))
                query.Add($"{key}={Uri.EscapeDataString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!)}");
        }
        Add("entityType", filter.EntityType);
        Add("entityId", filter.EntityId);
        Add("studentId", filter.StudentId);
        Add("guardianId", filter.GuardianId);
        Add("actorType", filter.ActorType);
        Add("action", filter.AuditAction);
        Add("search", filter.Search);
        Add("from", filter.From?.ToString("yyyy-MM-dd"));
        Add("to", filter.To?.ToString("yyyy-MM-dd"));

        var result = await Api.GetAsync<AuditPageDto>("/api/audit?" + string.Join("&", query), ct);
        if (!result.Succeeded) ViewBag.LoadError = result.Error;
        return View(new AuditPageModel(filter, result.Value ?? new AuditPageDto(Array.Empty<AuditEntryDto>(), 0, 1, 50)));
    }
}
