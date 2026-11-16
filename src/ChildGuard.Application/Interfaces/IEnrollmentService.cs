using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IEnrollmentService
{
    /// <summary>
    /// Creates the student, creates or links their guardians, gives them an RFID card (or leaves it pending)
    /// and optionally assigns transport, all in one database transaction. Credential emails to new guardians
    /// go out only after the commit. A repeated idempotency key returns the first enrollment unchanged.
    /// A possible duplicate student or a deactivated guardian returns a confirmation request and writes nothing.
    /// </summary>
    Task<EnrollmentOutcome> EnrollAsync(EnrollStudentRequest request, int? adminId, CancellationToken ct = default);

    /// <summary>Whether the admission number is taken and which students share this name and date of birth. Read-only.</summary>
    Task<EnrollmentStudentCheckDto> CheckStudentAsync(EnrollmentStudentCheckRequest request, CancellationToken ct = default);
}

public interface IEnrollmentRepository
{
    Task<EnrollmentRequest?> GetByKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task AddAsync(EnrollmentRequest request, CancellationToken ct = default);
    Task<bool> AdmissionNumberExistsAsync(string admissionNumber, CancellationToken ct = default);
    /// <summary>Students with this name (case-insensitive) and date of birth, in any status.</summary>
    Task<List<Student>> FindSameNameAndBirthDateAsync(string fullName, DateTime dateOfBirth, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction, committing only when it succeeds. Everything it saved is
    /// rolled back on a failed result or when a unique index refuses a write; the latter is reported as
    /// <c>UniqueConflict</c> so the caller can tell a concurrent duplicate from a business failure.
    /// </summary>
    Task<(Result Result, bool UniqueConflict)> RunInTransactionAsync(
        Func<CancellationToken, Task<Result>> work, CancellationToken ct = default);
}
