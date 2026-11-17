namespace ChildGuard.Domain.Enums;

public enum RecordStatus
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// The card's condition. Whether it is assigned is separate (RfidCard.StudentId): an Active card with no student is
/// available. Only an Active card records checkpoints or can be assigned. Retired is final.
/// </summary>
public enum RfidCardStatus
{
    Active = 1,
    Inactive = 2,
    Lost = 3,
    Damaged = 4,
    Retired = 5
}

public enum Gender
{
    Male = 1,
    Female = 2,
    Other = 3
}

/// <summary>Whether a scanned card UID can be given to a student being enrolled.</summary>
public enum RfidCardAvailability
{
    /// <summary>Never registered: it will be registered and assigned.</summary>
    New = 1,
    /// <summary>Registered, active and not linked to anyone.</summary>
    Available = 2,
    AssignedToStudent = 3,
    /// <summary>Registered but inactive or lost.</summary>
    Blocked = 4
}

/// <summary>
/// A bus's state. Active and Inactive keep the values RecordStatus gave them, so stored rows are unchanged. Only an
/// Active bus takes new student assignments; students already on it stay until moved.
/// </summary>
public enum BusStatus
{
    Active = 1,
    Inactive = 2,
    UnderMaintenance = 3,
    OutOfService = 4
}

/// <summary>Validity of a driver's licence relative to today at the school.</summary>
public enum LicenceState
{
    NotRecorded = 0,
    Valid = 1,
    ExpiringSoon = 2,
    Expired = 3
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

/// <summary>Outcome of one mandatory check (image quality, face detection) in the face verification layers.</summary>
public enum CheckResult
{
    Pass = 1,
    Fail = 2
}

/// <summary>How the image for a verification arrived. A gallery file can identify a person but can never show they are present.</summary>
public enum CaptureSource
{
    Webcam = 1,
    Gallery = 2
}

/// <summary>Identity layer: does the face belong to a guardian registered for this student?</summary>
public enum IdentityResult
{
    Match = 1,
    NoMatch = 2,
    /// <summary>Not enough to decide (for example two guardians matched almost equally). Never approvable.</summary>
    Inconclusive = 3
}

/// <summary>Authorisation layer: is the identified guardian allowed to collect this student?</summary>
public enum AuthorisationResult
{
    Authorised = 1,
    Unauthorised = 2,
    /// <summary>No guardian was identified, so there was nothing to check.</summary>
    NotEvaluated = 3
}

/// <summary>Liveness layer: was a real person present at the camera? NotPerformed is never a pass.</summary>
public enum LivenessResult
{
    Pass = 1,
    Fail = 2,
    NotPerformed = 3
}

public enum VerificationSessionState
{
    Open = 1,
    Completed = 2,
    Expired = 3,
    /// <summary>Ended without a result: too many failed attempts, or replaced by a newer session.</summary>
    Closed = 4
}

/// <summary>Which verification layer an audit row describes. Each layer is logged independently.</summary>
public enum FaceAuditLayer
{
    Quality = 1,
    Detection = 2,
    Identity = 3,
    Authorisation = 4,
    Liveness = 5,
    Decision = 6,
    Reference = 7,
    Access = 8
}

/// <summary>
/// Where a pickup verification stands. Derived (never stored) from Result,
/// PickupApproved, ApprovedByAdminId and AttemptedAt — see GuardianVerification.GetState.
/// </summary>
public enum PickupVerificationState
{
    /// <summary>Face matched; awaiting an admin decision and still inside the expiry window.</summary>
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    /// <summary>Face matched but no decision was made within the expiry window. Terminal.</summary>
    Expired = 4,
    /// <summary>Face did not match and no decision recorded. Can only be rejected.</summary>
    NotMatched = 5
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

/// <summary>Stored lifecycle of a temporary pickup authorisation. Expiry is derived, never stored.</summary>
public enum TemporaryPickupStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Revoked = 5,
    Completed = 6
}

/// <summary>What a temporary pickup means right now (see TemporaryPickupAuthorization.GetState).</summary>
public enum TemporaryPickupState
{
    PendingApproval = 1,
    /// <summary>Approved; the window has not opened yet.</summary>
    Scheduled = 2,
    /// <summary>Approved and inside the window: the handover may happen.</summary>
    Active = 3,
    Completed = 4,
    Rejected = 5,
    Cancelled = 6,
    Revoked = 7,
    Expired = 8
}

/// <summary>What kind of record a bulk import creates.</summary>
public enum ImportCategory
{
    Students = 1,
    Guardians = 2,
    Drivers = 3,
    Routes = 4,
    Buses = 5
}

/// <summary>How the rows of a bulk import were supplied.</summary>
public enum ImportSource
{
    Csv = 1,
    Excel = 2,
    Manual = 3
}

/// <summary>Outcome of a bulk import run.</summary>
public enum ImportStatus
{
    /// <summary>Every row was inserted.</summary>
    Completed = 1,
    /// <summary>The valid rows were inserted; the rest were skipped and are in the error report.</summary>
    CompletedWithErrors = 2,
    /// <summary>Nothing was inserted.</summary>
    Failed = 3
}

/// <summary>The weekdays the school is open in a regular week. Stored as a bit mask.</summary>
[Flags]
public enum SchoolWeekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday
}

/// <summary>How a calendar exception changes its date.</summary>
public enum SchoolDayType
{
    /// <summary>The school is closed.</summary>
    Holiday = 1,
    /// <summary>The school opens but dismisses early; a dismissal time is required.</summary>
    HalfDay = 2,
    /// <summary>The school opens with different start, late or dismissal times.</summary>
    SpecialTiming = 3,
    /// <summary>A normally-closed weekday the school opens on (e.g. a make-up Saturday).</summary>
    WorkingDay = 4
}

/// <summary>What kind of day a date is at the school, after the regular week and calendar exceptions.</summary>
public enum SchoolDayKind
{
    Regular = 1,
    /// <summary>A weekday the school is not open in its regular week.</summary>
    NonWorkingDay = 2,
    Holiday = 3,
    HalfDay = 4,
    SpecialTiming = 5,
    WorkingDay = 6
}
