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
