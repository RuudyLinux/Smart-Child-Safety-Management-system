namespace ChildGuard.Application.Common;

/// <summary>School transport policy (configuration section "Transport").</summary>
public sealed class TransportOptions
{
    /// <summary>
    /// When false (the default) a student's assignment always uses the bus's own route.
    /// A school whose buses serve more than one route can set it to true; the chosen route
    /// must then exist and be active.
    /// </summary>
    public bool AllowRouteOverride { get; set; }
}
