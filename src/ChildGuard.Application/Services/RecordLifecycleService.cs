using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class RecordLifecycleService : IRecordLifecycleService
{
    public const string HistoryAppeared = "This record gained history while it was being deleted, so it was kept. Archive it instead.";

    private readonly ILifecycleRepository _repo;
    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;
    private readonly ITransportationService _transport;
    private readonly IAuditTrail _audit;
    private readonly SchoolClock _clock;

    public RecordLifecycleService(ILifecycleRepository repo, IStudentRepository students, IGuardianRepository guardians,
        ITransportationService transport, IAuditTrail audit, SchoolClock clock)
    {
        _repo = repo;
        _students = students;
        _guardians = guardians;
        _transport = transport;
        _audit = audit;
        _clock = clock;
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    // ------------------------------------------------------------------ students

    public async Task<Result<DeletionCheckDto>> CheckStudentAsync(int studentId, CancellationToken ct = default)
    {
        var d = await _repo.GetStudentDependenciesAsync(studentId, ct);
        return d is null ? Result<DeletionCheckDto>.Failure("Student not found.") : Result<DeletionCheckDto>.Success(StudentCheck(d));
    }

    private static DeletionCheckDto StudentCheck(StudentDependencies d)
    {
        var blockers = new List<string>();
        if (d.Attendance > 0) blockers.Add(Count(d.Attendance, "attendance record", "attendance records"));
        if (d.JourneyEvents > 0) blockers.Add(Count(d.JourneyEvents, "journey event", "journey events"));
        if (d.Verifications > 0) blockers.Add(Count(d.Verifications, "pickup verification", "pickup verifications"));
        if (d.VerificationSessions > 0) blockers.Add(Count(d.VerificationSessions, "verification session", "verification sessions"));
        if (d.TemporaryPickups > 0) blockers.Add(Count(d.TemporaryPickups, "temporary pickup", "temporary pickups"));
        if (d.BusAssignments > 0) blockers.Add(Count(d.BusAssignments, "bus assignment (current or past)", "bus assignments (current or past)"));
        if (d.HasCard) blockers.Add("an assigned RFID card");
        else if (d.CardHistory > 0) blockers.Add(Count(d.CardHistory, "past RFID card assignment", "past RFID card assignments"));

        var also = new List<string>();
        if (d.GuardianLinks > 0) also.Add($"{Count(d.GuardianLinks, "guardian link", "guardian links")} (the guardian accounts are kept)");
        if (d.EnrollmentRecords > 0) also.Add("the enrollment record");
        if (d.HasPhoto) also.Add("the profile photo");

        var warnings = new List<string>();
        if (d.ActiveBusNumber is not null) warnings.Add($"Archiving ends the current assignment to bus {d.ActiveBusNumber}; it stays in the transport history.");
        if (d.HasCard) warnings.Add("The RFID card stays linked but cannot record checkpoints while the student is archived.");

        return new DeletionCheckDto(blockers.Count == 0, blockers, also, warnings);
    }

    public async Task<LifecycleOutcome> ArchiveStudentAsync(int studentId, string reason, int adminId, CancellationToken ct = default)
    {
        var effects = new List<string>();
        string? error = null;
        var result = await _repo.RunInTransactionAsync(async tx =>
        {
            var student = await _students.GetByIdAsync(studentId, tx);
            if (student is null) return Result.Failure(error = "Student not found.");
            if (student.ArchivedAt is not null) return Result.Failure(error = "This student is already archived.");

            // An archived student does not keep a seat. The assignment is ended (kept as history) under the transport rules.
            var active = await _repo.GetStudentDependenciesAsync(studentId, tx);
            if (active?.ActiveBusAssignmentId is int assignmentId)
            {
                var ended = await _transport.SetAssignmentStatusAsync(assignmentId, RecordStatus.Inactive, tx, adminId);
                if (!ended.Succeeded) return Result.Failure(error = ended.Error!);
                effects.Add($"Assignment to bus {active.ActiveBusNumber} ended.");
            }

            var now = _clock.UtcNow;
            var wasActive = student.Status == RecordStatus.Active;
            student.Status = RecordStatus.Inactive;
            student.ArchivedAt = now;
            student.ArchivedByAdminId = adminId;
            student.ArchiveReason = reason.Trim();
            student.UpdatedAt = now;
            _audit.Record(new AuditEvent("StudentArchived", "Student", studentId, $"Student {student.FullName} archived")
            {
                StudentId = studentId, Reason = student.ArchiveReason,
                Details = new { previousStatus = wasActive ? "Active" : "Inactive", effects }
            });
            await _students.SaveChangesAsync(tx);
            return Result.Success();
        }, ct);

        if (!result.Succeeded) return error == "Student not found." ? LifecycleOutcome.Missing("Student") : LifecycleOutcome.Fail(result.Error!);
        return LifecycleOutcome.Ok("Student archived. Their records and history are kept.", effects.ToArray());
    }

    public async Task<LifecycleOutcome> RestoreStudentAsync(int studentId, string? reason, int adminId, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(studentId, ct);
        if (student is null) return LifecycleOutcome.Missing("Student");
        if (student.ArchivedAt is null) return LifecycleOutcome.Fail("This student is not archived.");

        var previous = new { archivedAt = student.ArchivedAt, archivedBy = student.ArchivedByAdminId, archiveReason = student.ArchiveReason };
        student.Status = RecordStatus.Active;
        student.ArchivedAt = null;
        student.ArchivedByAdminId = null;
        student.ArchiveReason = null;
        student.UpdatedAt = _clock.UtcNow;
        _audit.Record(new AuditEvent("StudentRestored", "Student", studentId, $"Student {student.FullName} restored")
        { StudentId = studentId, Reason = Blank(reason), Details = previous });
        await _students.SaveChangesAsync(ct);
        return LifecycleOutcome.Ok("Student restored and active again.", "Bus transport is not restored automatically; assign a bus if needed.");
    }

    public async Task<LifecycleOutcome> DeleteStudentAsync(int studentId, string reason, int adminId, CancellationToken ct = default)
    {
        var refusal = (DeletionCheckDto?)null;
        var snapshot = (object?)null;
        var result = await _repo.RunInTransactionAsync(async tx =>
        {
            var student = await _students.GetByIdAsync(studentId, tx);
            if (student is null) return Result.Failure("Student not found.");
            var d = (await _repo.GetStudentDependenciesAsync(studentId, tx))!;
            var check = StudentCheck(d);
            if (!check.CanDelete) { refusal = check; return Result.Failure("Refused."); }

            snapshot = new
            {
                student.FullName, dateOfBirth = DateOnly.FromDateTime(student.DateOfBirth), student.Grade, student.Section,
                student.AdmissionNumber, removed = check.AlsoRemoved
            };
            var name = student.FullName;
            if (!await _repo.DeleteStudentAsync(studentId, tx)) return Result.Failure(HistoryAppeared);
            _audit.Record(new AuditEvent("StudentDeleted", "Student", studentId, $"Student {name} permanently deleted")
            { StudentId = studentId, Reason = reason.Trim(), Details = snapshot });
            await _students.SaveChangesAsync(tx);
            return Result.Success();
        }, ct);

        if (refusal is not null) return LifecycleOutcome.Refused(refusal);
        if (!result.Succeeded) return result.Error == "Student not found." ? LifecycleOutcome.Missing("Student") : LifecycleOutcome.Fail(result.Error!);
        return LifecycleOutcome.Ok("Student permanently deleted. The audit log keeps a record of the deletion.");
    }

    // ------------------------------------------------------------------ guardians

    public async Task<Result<DeletionCheckDto>> CheckGuardianAsync(int guardianId, CancellationToken ct = default)
    {
        var d = await _repo.GetGuardianDependenciesAsync(guardianId, ct);
        return d is null ? Result<DeletionCheckDto>.Failure("Guardian not found.") : Result<DeletionCheckDto>.Success(GuardianCheck(d));
    }

    private static DeletionCheckDto GuardianCheck(GuardianDependencies d)
    {
        var blockers = new List<string>();
        if (d.Links > 0) blockers.Add($"linked to {Count(d.Links, "student", "students")} ({string.Join(", ", d.LinkedStudents)}); unlink them first");
        if (d.Verifications > 0) blockers.Add(Count(d.Verifications, "pickup verification", "pickup verifications"));
        if (d.TemporaryPickups > 0) blockers.Add(Count(d.TemporaryPickups, "temporary pickup request", "temporary pickup requests"));
        if (d.Notifications > 0) blockers.Add(Count(d.Notifications, "notification", "notifications"));

        var also = new List<string>();
        if (d.HasFaceData || d.FaceReferences > 0) also.Add("registered face data");
        if (d.TrustedDevices > 0) also.Add(Count(d.TrustedDevices, "trusted device", "trusted devices"));
        if (d.HasPhoto) also.Add("the profile photo");

        var warnings = new List<string>();
        if (d.SoleAuthorizedPickupFor.Count > 0)
            warnings.Add($"The only active guardian authorised to collect {string.Join(", ", d.SoleAuthorizedPickupFor)}. Authorise another guardian first.");
        warnings.Add("Archiving signs the guardian out of every device; they cannot sign in or be verified for pickup until restored.");

        return new DeletionCheckDto(blockers.Count == 0, blockers, also, warnings);
    }

    public async Task<LifecycleOutcome> ArchiveGuardianAsync(int guardianId, string reason, int adminId, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(guardianId, ct);
        if (g is null) return LifecycleOutcome.Missing("Guardian");
        if (g.ArchivedAt is not null) return LifecycleOutcome.Fail("This guardian is already archived.");

        var d = (await _repo.GetGuardianDependenciesAsync(guardianId, ct))!;
        var now = _clock.UtcNow;
        var wasActive = g.Status == RecordStatus.Active;
        g.Status = RecordStatus.Inactive;
        g.ArchivedAt = now;
        g.ArchivedByAdminId = adminId;
        g.ArchiveReason = reason.Trim();
        // No push messages to an archived account; the app registers a fresh token after a restore and sign-in.
        g.FcmToken = null;
        g.UpdatedAt = now;
        var revoked = await _guardians.RevokeTrustedDevicesAsync(guardianId, now, null, ct);

        var effects = new List<string> { "Signed out of every device." };
        if (d.SoleAuthorizedPickupFor.Count > 0)
            effects.Add($"No active guardian is now authorised to collect {string.Join(", ", d.SoleAuthorizedPickupFor)}.");
        _audit.Record(new AuditEvent("GuardianArchived", "Guardian", guardianId, $"Guardian {g.FullName} archived")
        {
            GuardianId = guardianId, Reason = g.ArchiveReason,
            Details = new { previousStatus = wasActive ? "Active" : "Inactive", trustedDevicesRevoked = revoked, soleAuthorizedPickupFor = d.SoleAuthorizedPickupFor }
        });
        await _guardians.SaveChangesAsync(ct);
        return LifecycleOutcome.Ok("Guardian archived. Their links and history are kept.", effects.ToArray());
    }

    public async Task<LifecycleOutcome> RestoreGuardianAsync(int guardianId, string? reason, int adminId, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(guardianId, ct);
        if (g is null) return LifecycleOutcome.Missing("Guardian");
        if (g.ArchivedAt is null) return LifecycleOutcome.Fail("This guardian is not archived.");

        var previous = new { archivedAt = g.ArchivedAt, archivedBy = g.ArchivedByAdminId, archiveReason = g.ArchiveReason };
        g.Status = RecordStatus.Active;
        g.ArchivedAt = null;
        g.ArchivedByAdminId = null;
        g.ArchiveReason = null;
        g.UpdatedAt = _clock.UtcNow;
        _audit.Record(new AuditEvent("GuardianRestored", "Guardian", guardianId, $"Guardian {g.FullName} restored")
        { GuardianId = guardianId, Reason = Blank(reason), Details = previous });
        await _guardians.SaveChangesAsync(ct);
        return LifecycleOutcome.Ok("Guardian restored. They can sign in again (a one-time code is required on every device).");
    }

    public async Task<LifecycleOutcome> DeleteGuardianAsync(int guardianId, string reason, int adminId, CancellationToken ct = default)
    {
        var refusal = (DeletionCheckDto?)null;
        var result = await _repo.RunInTransactionAsync(async tx =>
        {
            var g = await _guardians.GetByIdAsync(guardianId, tx);
            if (g is null) return Result.Failure("Guardian not found.");
            var check = GuardianCheck((await _repo.GetGuardianDependenciesAsync(guardianId, tx))!);
            if (!check.CanDelete) { refusal = check; return Result.Failure("Refused."); }

            // Name and masked email only: contact details and face data leave with the record.
            var snapshot = new { g.FullName, email = EmailMasking.Mask(g.Email), removed = check.AlsoRemoved };
            var name = g.FullName;
            if (!await _repo.DeleteGuardianAsync(guardianId, tx)) return Result.Failure(HistoryAppeared);
            _audit.Record(new AuditEvent("GuardianDeleted", "Guardian", guardianId, $"Guardian {name} permanently deleted")
            { GuardianId = guardianId, Reason = reason.Trim(), Details = snapshot });
            await _guardians.SaveChangesAsync(tx);
            return Result.Success();
        }, ct);

        if (refusal is not null) return LifecycleOutcome.Refused(refusal);
        if (!result.Succeeded) return result.Error == "Guardian not found." ? LifecycleOutcome.Missing("Guardian") : LifecycleOutcome.Fail(result.Error!);
        return LifecycleOutcome.Ok("Guardian permanently deleted. The audit log keeps a record of the deletion.");
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
