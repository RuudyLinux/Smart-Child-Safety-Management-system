using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

/// <summary>Outcome of an archive, restore or delete. Exactly one of Done, Error, Blocked is set (or NotFound).</summary>
public sealed record LifecycleOutcome(LifecycleResultDto? Done, string? Error, DeletionCheckDto? Blocked, bool NotFound = false)
{
    public static LifecycleOutcome Ok(string message, params string[] effects) => new(new LifecycleResultDto(message, effects), null, null);
    public static LifecycleOutcome Fail(string error) => new(null, error, null);
    public static LifecycleOutcome Refused(DeletionCheckDto check) => new(null, null, check);
    public static LifecycleOutcome Missing(string what) => new(null, $"{what} not found.", null, true);
}

/// <summary>
/// Archive, restore and permanent deletion of students and guardians. Archiving keeps every record and its
/// history; deletion is allowed only for a record with no history. Every action is audited with its reason.
/// </summary>
public interface IRecordLifecycleService
{
    Task<Result<DeletionCheckDto>> CheckStudentAsync(int studentId, CancellationToken ct = default);
    Task<LifecycleOutcome> ArchiveStudentAsync(int studentId, string reason, int adminId, CancellationToken ct = default);
    Task<LifecycleOutcome> RestoreStudentAsync(int studentId, string? reason, int adminId, CancellationToken ct = default);
    Task<LifecycleOutcome> DeleteStudentAsync(int studentId, string reason, int adminId, CancellationToken ct = default);

    Task<Result<DeletionCheckDto>> CheckGuardianAsync(int guardianId, CancellationToken ct = default);
    Task<LifecycleOutcome> ArchiveGuardianAsync(int guardianId, string reason, int adminId, CancellationToken ct = default);
    Task<LifecycleOutcome> RestoreGuardianAsync(int guardianId, string? reason, int adminId, CancellationToken ct = default);
    Task<LifecycleOutcome> DeleteGuardianAsync(int guardianId, string reason, int adminId, CancellationToken ct = default);
}

public sealed record StudentDependencies(
    int Attendance, int JourneyEvents, int Verifications, int VerificationSessions, int TemporaryPickups,
    int BusAssignments, int CardHistory, bool HasCard, int GuardianLinks, int EnrollmentRecords, bool HasPhoto,
    int? ActiveBusAssignmentId, string? ActiveBusNumber);

public sealed record GuardianDependencies(
    int Links, int Verifications, int TemporaryPickups, int Notifications, int FaceReferences, bool HasFaceData,
    int TrustedDevices, bool HasPhoto, IReadOnlyList<string> LinkedStudents, IReadOnlyList<string> SoleAuthorizedPickupFor);

public interface ILifecycleRepository
{
    Task<StudentDependencies?> GetStudentDependenciesAsync(int studentId, CancellationToken ct = default);
    Task<GuardianDependencies?> GetGuardianDependenciesAsync(int guardianId, CancellationToken ct = default);

    /// <summary>
    /// Removes the student with its guardian links, photo and enrollment record. False (nothing removed) when the
    /// database refuses because history references the student.
    /// </summary>
    Task<bool> DeleteStudentAsync(int studentId, CancellationToken ct = default);

    /// <summary>Removes the guardian with its face data, sign-in artifacts and photo. False when history references it.</summary>
    Task<bool> DeleteGuardianAsync(int guardianId, CancellationToken ct = default);

    /// <summary>One transaction; committed only when <paramref name="work"/> succeeds.</summary>
    Task<Result> RunInTransactionAsync(Func<CancellationToken, Task<Result>> work, CancellationToken ct = default);
}
