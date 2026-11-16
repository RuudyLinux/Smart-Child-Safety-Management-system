using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <summary>Why a record is archived. Required and kept in the audit trail.</summary>
public record ArchiveRecordRequest(string Reason);

/// <summary>Optional note for restoring an archived record.</summary>
public record RestoreRecordRequest(string? Reason);

/// <summary>Why a record is permanently deleted. Required and kept in the audit trail.</summary>
public record DeleteRecordRequest(string Reason);

/// <param name="CanDelete">True only when nothing in <paramref name="Blockers"/>: the record has no history.</param>
/// <param name="Blockers">What prevents deletion, in plain words (e.g. "12 attendance records").</param>
/// <param name="AlsoRemoved">What would be removed together with the record (links, photo, enrollment record).</param>
/// <param name="Warnings">Consequences of archiving instead (e.g. an active bus assignment would end).</param>
public record DeletionCheckDto(bool CanDelete, IReadOnlyList<string> Blockers, IReadOnlyList<string> AlsoRemoved, IReadOnlyList<string> Warnings);

/// <summary>Result of an archive, delete or restore.</summary>
public record LifecycleResultDto(string Message, IReadOnlyList<string> Effects);

/// <summary>
/// Full edit of a student's record, including the details added with enrollment. Saved atomically.
/// The original <see cref="UpdateStudentRequest"/> stays for existing callers.
/// </summary>
public record UpdateStudentRecordRequest(
    string FullName, DateTime DateOfBirth, string? Grade, string? Section, string? AdmissionNumber, Gender? Gender);
