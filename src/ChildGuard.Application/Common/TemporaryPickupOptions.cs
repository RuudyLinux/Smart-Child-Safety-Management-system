namespace ChildGuard.Application.Common;

/// <summary>Temporary pickup limits, bound from "TemporaryPickup". The 24-hour cap is a hard ceiling.</summary>
public class TemporaryPickupOptions
{
    /// <summary>Absolute maximum length of an authorisation window. Configuration can only lower it.</summary>
    public const int HardMaxWindowHours = 24;

    public int MaxWindowHours { get; set; } = HardMaxWindowHours;

    /// <summary>How far ahead a request may start.</summary>
    public int MaxAdvanceDays { get; set; } = 14;

    /// <summary>A start time slightly in the past is accepted (clock drift, time spent filling the form).</summary>
    public int StartGraceMinutes { get; set; } = 10;

    public TimeSpan MaxWindow => TimeSpan.FromHours(Math.Clamp(MaxWindowHours, 1, HardMaxWindowHours));
}
