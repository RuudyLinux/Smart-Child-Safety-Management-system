namespace ChildGuard.Domain.Enums;

public enum RecordStatus
{
    Active = 1,
    Inactive = 2
}

public enum RfidCardStatus
{
    Active = 1,
    Inactive = 2,
    Lost = 3
}

public enum RfidConnectionStatus
{
    Disconnected = 0,
    Connected = 1
}

public enum JourneyEventType
{
    BusBoarding = 1,
    SchoolArrival = 2,
    SchoolDeparture = 3,
    BusDropoff = 4
}

public enum AttendanceEventType
{
    SchoolArrival = 1,
    SchoolDeparture = 2
}

public enum EventSource
{
    Rfid = 1,
    Manual = 2
}

public enum VerificationResult
{
    Match = 1,
    NoMatch = 2
}

public enum NotificationType
{
    Attendance = 1,
    Journey = 2,
    PickupVerification = 3,
    General = 4
}

public enum AppRole
{
    Admin = 1,
    Guardian = 2
}

/// <summary>
/// Where a student currently is, derived from their most recent journey event.
/// Presentation-independent: both the Admin Web and the Guardian app map these to
/// their own wording and colour, and every surface pairs the colour with the label.
/// </summary>
public enum StudentSafetyStatus
{
    /// <summary>No journey event recorded today — nothing is known about the student's whereabouts.</summary>
    NoActivityToday = 0,
    /// <summary>Boarded the bus; school arrival not yet recorded.</summary>
    OnBusToSchool = 1,
    /// <summary>Checked in at school and not yet departed.</summary>
    AtSchool = 2,
    /// <summary>Departed school; bus drop-off not yet recorded.</summary>
    LeftSchool = 3,
    /// <summary>Bus drop-off recorded — the day's journey is complete.</summary>
    DroppedOffHome = 4
}

/// <summary>How urgently an operational alert needs an admin's attention.</summary>
public enum AlertSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}
