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
