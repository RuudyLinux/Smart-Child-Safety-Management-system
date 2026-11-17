using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Common;

/// <summary>The checkpoint the system chose for a scan, and why, in words an admin can check.</summary>
/// <param name="Checkpoint">Null when the student's journey is already complete for today.</param>
public sealed record CheckpointDecision(JourneyEventType? Checkpoint, string Reason, bool OffSchedule);

/// <summary>
/// Automatic checkpoint selection: the next step of the student's journey, read from
/// today's school schedule and the last event already recorded for the student today.
/// Pure: every input is passed in, so the rule is the same for every caller and testable.
///
///  * nothing yet today (or a new trip after a drop-off): on a bus before school starts → Bus boarding,
///    otherwise → School arrival
///  * Bus boarding → School arrival
///  * School arrival → School departure (guardian pickup)
///  * School departure → Bus drop-off when the student travels by bus; otherwise the day is complete
/// The journey rules (JourneyValidationService) still validate the scan itself.
/// </summary>
public static class CheckpointResolver
{
    public static CheckpointDecision Resolve(JourneyEventType? lastToday, bool travelsByBus, SchoolDayInfo today, TimeOnly now)
    {
        var offSchedule = !today.IsSchoolDay;
        var dayNote = today.IsSchoolDay
            ? (today.Title is null ? "school day" : $"{today.Title}")
            : (today.Title is null ? "school closed today" : $"school closed today ({today.Title})");

        switch (lastToday)
        {
            case null:
            case JourneyEventType.BusDropoff:
            {
                var beforeStart = today.StartTime is { } start ? now < start : true;
                if (travelsByBus && beforeStart)
                    return new(JourneyEventType.BusBoarding,
                        today.StartTime is { } s
                            ? $"Bus student, before school starts at {s:HH:mm} ({dayNote}): boarding at the home stop."
                            : $"Bus student, no scan yet today ({dayNote}): boarding at the home stop.",
                        offSchedule);
                return new(JourneyEventType.SchoolArrival,
                    travelsByBus
                        ? $"Bus student, school has started ({dayNote}): arriving at school."
                        : $"No scan yet today ({dayNote}): arriving at school.",
                    offSchedule);
            }

            case JourneyEventType.BusBoarding:
                return new(JourneyEventType.SchoolArrival, $"Boarded the bus earlier today ({dayNote}): arriving at school.", offSchedule);

            case JourneyEventType.SchoolArrival:
            {
                var early = today.DismissalTime is { } d && now < d;
                return new(JourneyEventType.SchoolDeparture,
                    early
                        ? $"At school, before dismissal at {today.DismissalTime:HH:mm}: early pickup, guardian must be verified."
                        : $"At school{(today.DismissalTime is { } dt ? $", after dismissal at {dt:HH:mm}" : "")}: leaving school, guardian must be verified.",
                    offSchedule);
            }

            case JourneyEventType.SchoolDeparture:
                return travelsByBus
                    ? new(JourneyEventType.BusDropoff, "Left school on the bus: drop-off at the home stop.", offSchedule)
                    : new(null, "Already left school today. The journey is complete; choose a checkpoint manually if this is an exception.", offSchedule);

            default:
                return new(null, "No automatic checkpoint for this state. Choose one manually.", offSchedule);
        }
    }
}
