using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record AddressDto(
    string? Line1, string? Line2, string? Locality, string? Landmark,
    string? City, string? District, string? State, string? PostalCode, string? Country)
{
    public static AddressDto? From(PostalAddress? a) => a is null ? null
        : new(a.Line1, a.Line2, a.Locality, a.Landmark, a.City, a.District, a.State, a.PostalCode, a.Country);

    /// <summary>Trimmed copy as an entity, or null when every part is blank.</summary>
    public PostalAddress? ToEntity()
    {
        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var a = new PostalAddress
        {
            Line1 = T(Line1), Line2 = T(Line2), Locality = T(Locality), Landmark = T(Landmark),
            City = T(City), District = T(District), State = T(State), PostalCode = T(PostalCode), Country = T(Country)
        };
        return a.Line1 is null && a.Line2 is null && a.Locality is null && a.Landmark is null && a.City is null
               && a.District is null && a.State is null && a.PostalCode is null && a.Country is null
            ? null
            : a;
    }
}

public record EnrollmentStudentDto(
    string FullName, DateTime DateOfBirth, string? Grade, string? Section,
    string? AdmissionNumber, Gender? Gender, AddressDto? Address);

/// <summary>
/// One guardian of the student being enrolled: either an existing account the admin explicitly
/// selected (<paramref name="ExistingGuardianId"/>), or a new account built from the remaining fields.
/// </summary>
/// <param name="ConfirmInactiveAccount">
/// Required (true) to link an existing guardian whose account is deactivated. The account stays deactivated.
/// </param>
public record EnrollmentGuardianDto(
    int? ExistingGuardianId,
    string? FullName, string? Email, string? ContactNumber,
    AddressDto? Address, bool SameAsStudentAddress,
    string? Relationship, bool IsAuthorizedForPickup,
    bool ConfirmInactiveAccount = false);

/// <param name="RouteId">Optional; the bus's own route is used when omitted (existing transport rule).</param>
public record EnrollmentTransportDto(int BusId, int? RouteId);

/// <param name="IdempotencyKey">Generated once per wizard session and resent on retries; a repeat returns the first result.</param>
/// <param name="CardUid">Null or blank leaves the student with RFID pending.</param>
/// <param name="DuplicateStudentOverrideReason">
/// Required when a student with the same name and date of birth already exists (for example twins);
/// recorded with the enrollment. Ignored when there is no such student.
/// </param>
public record EnrollStudentRequest(
    string IdempotencyKey,
    EnrollmentStudentDto Student,
    IReadOnlyList<EnrollmentGuardianDto> Guardians,
    string? CardUid,
    EnrollmentTransportDto? Transport,
    string? DuplicateStudentOverrideReason = null);

public record PossibleDuplicateStudentDto(
    int StudentId, string FullName, DateTime DateOfBirth, string? Grade, string? Section,
    string? AdmissionNumber, RecordStatus Status);

public record InactiveGuardianDto(int GuardianId, string FullName, string Email, RecordStatus Status);

/// <summary>
/// The enrollment needs the admin's explicit decision before anything is written: a possible duplicate
/// student (answer with DuplicateStudentOverrideReason) and/or deactivated guardians (answer with
/// ConfirmInactiveAccount on each).
/// </summary>
public record EnrollmentConfirmationDto(
    string Message,
    IReadOnlyList<PossibleDuplicateStudentDto> PossibleDuplicates,
    IReadOnlyList<InactiveGuardianDto> InactiveGuardians);

/// <summary>Early, read-only check of step 1 of the wizard.</summary>
public record EnrollmentStudentCheckRequest(string FullName, DateTime DateOfBirth, string? AdmissionNumber);

public record EnrollmentStudentCheckDto(bool AdmissionNumberTaken, IReadOnlyList<PossibleDuplicateStudentDto> PossibleDuplicates);

/// <summary>What an enrollment attempt came to. Exactly one of the three is set.</summary>
public sealed record EnrollmentOutcome(EnrollmentResultDto? Enrollment, string? Error, EnrollmentConfirmationDto? Confirmation)
{
    public static EnrollmentOutcome Done(EnrollmentResultDto r) => new(r, null, null);
    public static EnrollmentOutcome Failed(string error) => new(null, error, null);
    public static EnrollmentOutcome NeedsConfirmation(EnrollmentConfirmationDto c) => new(null, null, c);
    /// <summary>The idempotency key belongs to a completed enrollment with different content.</summary>
    public bool KeyReused { get; init; }
}

/// <param name="Created">True when the account was created by this enrollment (and so was sent its credentials).</param>
/// <param name="CredentialEmailSent">Null for a linked existing guardian.</param>
public record EnrolledGuardianDto(int GuardianId, string FullName, string? Relationship, bool IsAuthorizedForPickup,
    bool Created, bool? CredentialEmailSent, string? EmailError);

/// <param name="AlreadyEnrolled">True when this idempotency key had already completed; nothing new was written.</param>
public record EnrollmentResultDto(
    int StudentId, string StudentName, bool AlreadyEnrolled,
    IReadOnlyList<EnrolledGuardianDto> Guardians,
    string? MaskedCardUid, bool RfidPending,
    string? BusNumber, string? RouteName);

public record GuardianLookupDto(
    int GuardianId, string FullName, string Email, string? Phone, RecordStatus Status,
    bool FaceRegistered, IReadOnlyList<string> LinkedStudents);

public record RfidCardCheckRequest(string CardUid);

public record RfidCardCheckDto(
    string MaskedCardUid, RfidCardAvailability Availability, bool CanAssign,
    RfidCardStatus? Status, int? StudentId, string? StudentName, string Message);
