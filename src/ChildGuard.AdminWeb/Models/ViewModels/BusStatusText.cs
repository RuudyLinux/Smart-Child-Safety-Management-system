using ChildGuard.Domain.Enums;

namespace ChildGuard.AdminWeb.Models.ViewModels;

public static class BusStatusText
{
    public static string Label(BusStatus s) => s switch
    {
        BusStatus.Active => "In service",
        BusStatus.UnderMaintenance => "Under maintenance",
        BusStatus.OutOfService => "Out of service",
        _ => "Inactive"
    };
}
