using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Common;

/// <summary>
/// Plain-language checkpoint names for text people read: guardian notifications
/// and desk messages. Enum identifiers ("SchoolArrival") never reach a person.
/// </summary>
public static class JourneyEventNames
{
    public static string Label(JourneyEventType type) => type switch
    {
        JourneyEventType.BusBoarding => "Bus boarding",
        JourneyEventType.SchoolArrival => "School arrival",
        JourneyEventType.SchoolDeparture => "School departure",
        JourneyEventType.BusDropoff => "Bus drop-off",
        _ => type.ToString()
    };
}
