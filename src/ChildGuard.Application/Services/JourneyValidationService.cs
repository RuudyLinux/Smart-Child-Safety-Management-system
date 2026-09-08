using ChildGuard.Application.Common;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

/// <summary>
/// Single point of truth for "given the student's current state, is the requested
/// checkpoint a valid next event?" Used by the RFID scan pipeline so no checkpoint
/// gets its own hardcoded logic, and repeated/out-of-order scans are rejected
/// instead of creating duplicate records.
/// </summary>
public class JourneyValidationService
{
    /// <summary>
    /// Valid next events keyed by the student's last recorded event (null/BusDropoff = cycle restart).
    /// School Arrival is reachable both after Bus Boarding and directly from "no prior event today",
    /// covering the guardian-drop-off scenario without a second device.
    /// </summary>
    public Result<JourneyEventType> ValidateNextEvent(JourneyEventType? lastEvent, JourneyEventType requested)
    {
        var allowed = lastEvent switch
        {
            null => new[] { JourneyEventType.BusBoarding, JourneyEventType.SchoolArrival },
            JourneyEventType.BusDropoff => new[] { JourneyEventType.BusBoarding, JourneyEventType.SchoolArrival },
            JourneyEventType.BusBoarding => new[] { JourneyEventType.SchoolArrival },
            JourneyEventType.SchoolArrival => new[] { JourneyEventType.SchoolDeparture },
            JourneyEventType.SchoolDeparture => new[] { JourneyEventType.BusDropoff },
            _ => Array.Empty<JourneyEventType>()
        };

        if (allowed.Contains(requested))
            return Result<JourneyEventType>.Success(requested);

        var reason = lastEvent == requested
            ? $"{requested} already recorded — duplicate scan ignored."
            : $"{requested} is not valid after {(lastEvent is null ? "no prior event" : lastEvent.ToString())}. Expected: {string.Join(" or ", allowed)}.";

        return Result<JourneyEventType>.Failure(reason);
    }
}
