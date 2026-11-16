using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class StudentService : IStudentService
{
    /// <summary>How much recent history a student profile shows before linking out to the full log.</summary>
    private const int ProfileHistoryRows = 12;

    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;
    private readonly IJourneyRepository _journey;
    private readonly IPhotoRepository _photos;
    private readonly ISafetyReadService _safety;
    private readonly SchoolClock _clock;
    private readonly IAuditTrail _audit;

    public StudentService(
        IStudentRepository students,
        IGuardianRepository guardians,
        IJourneyRepository journey,
        IPhotoRepository photos,
        ISafetyReadService safety,
        SchoolClock clock,
        IAuditTrail? audit = null)
    {
        _clock = clock;
        _audit = audit ?? NullAuditTrail.Instance;
        _students = students;
        _guardians = guardians;
        _journey = journey;
        _photos = photos;
        _safety = safety;
    }

    public async Task<List<StudentListItemDto>> GetAllAsync(string? search, CancellationToken ct = default)
    {
        var students = await _students.GetAllAsync(search, ct);
        if (students.Count == 0) return new List<StudentListItemDto>();

        var ids = students.Select(s => s.StudentId).ToList();
        var snapshots = await _safety.GetSnapshotsAsync(ids, ct);
        var photos = await _photos.GetStudentPhotoTimestampsAsync(ids, ct);

        return students.Select(s =>
        {
            var snap = Snapshot(snapshots, s.StudentId);
            return new StudentListItemDto(
                s.StudentId, s.FullName, s.Grade, s.Status,
                s.RfidCard is not null ? RfidMasking.Mask(s.RfidCard.CardUid) : null,
                s.StudentGuardians.Count,
                photos.ContainsKey(s.StudentId),
                s.StudentGuardians.FirstOrDefault()?.Guardian?.FullName,
                snap.Status, snap.PresentToday, snap.CheckInTime,
                snap.LastEventAt, snap.LastEventType, snap.BusNumber,
                s.ArchivedAt, s.AdmissionNumber, s.Section);
        }).ToList();
    }

    public async Task<Result<StudentDetailDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var student = await _students.GetWithGuardiansAsync(id, ct);
        return student is null
            ? Result<StudentDetailDto>.Failure("Student not found.")
            : Result<StudentDetailDto>.Success(await ToDetailAsync(student, ct));
    }

    public async Task<Result<StudentDetailDto>> CreateAsync(CreateStudentRequest request, CancellationToken ct = default)
    {
        var student = new Student
        {
            FullName = request.FullName,
            DateOfBirth = request.DateOfBirth,
            Grade = request.Grade
        };
        await _students.AddAsync(student, ct);
        await _audit.SaveWithAuditAsync(_students.SaveChangesAsync,
            () => new AuditEvent("StudentCreated", "Student", student.StudentId, $"Student {student.FullName} created")
            {
                StudentId = student.StudentId,
                Details = new { student.FullName, dateOfBirth = DateOnly.FromDateTime(student.DateOfBirth), student.Grade }
            }, ct);
        // The admission number comes from the new id, so it is assigned once the student is saved.
        student.AdmissionNumber = await AdmissionNumbers.NextFreeAsync(student.StudentId, _clock.Today,
            n => _students.AdmissionNumberTakenAsync(n, student.StudentId, ct));
        await _students.SaveChangesAsync(ct);
        return Result<StudentDetailDto>.Success(await ToDetailAsync(student, ct));
    }

    public async Task<Result<StudentDetailDto>> UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result<StudentDetailDto>.Failure("Student not found.");

        var changes = AuditDetails.Changes(
            ("fullName", student.FullName, request.FullName),
            ("dateOfBirth", DateOnly.FromDateTime(student.DateOfBirth), DateOnly.FromDateTime(request.DateOfBirth)),
            ("grade", student.Grade, request.Grade));
        student.FullName = request.FullName;
        student.DateOfBirth = request.DateOfBirth;
        student.Grade = request.Grade;
        student.UpdatedAt = DateTime.UtcNow;
        if (changes.Count > 0)
            _audit.Record(new AuditEvent("StudentUpdated", "Student", id, $"Student {student.FullName} updated: {string.Join(", ", changes.Keys)}")
            { StudentId = id, Details = changes });
        await _students.SaveChangesAsync(ct);

        var withGuardians = await _students.GetWithGuardiansAsync(id, ct);
        return Result<StudentDetailDto>.Success(await ToDetailAsync(withGuardians!, ct));
    }

    public async Task<Result> UpdateStatusAsync(int id, UpdateStudentStatusRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result.Failure("Student not found.");

        if (student.Status != request.Status)
            _audit.Record(new AuditEvent(request.Status == RecordStatus.Active ? "StudentActivated" : "StudentDeactivated", "Student", id,
                $"Student {student.FullName} {(request.Status == RecordStatus.Active ? "activated" : "deactivated")}")
            { StudentId = id, Details = new { from = student.Status, to = request.Status } });
        student.Status = request.Status;
        student.UpdatedAt = DateTime.UtcNow;
        // Activating an archived record restores it: an active student is never left marked as archived.
        if (request.Status == RecordStatus.Active && student.ArchivedAt is not null)
        {
            _audit.Record(new AuditEvent("StudentRestored", "Student", id, $"Student {student.FullName} restored by activation")
            { StudentId = id, Details = new { archivedAt = student.ArchivedAt, archiveReason = student.ArchiveReason } });
            student.ArchivedAt = null;
            student.ArchivedByAdminId = null;
            student.ArchiveReason = null;
        }
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public const string AdmissionNumberTaken = "Another student already has this admission number.";

    public async Task<Result<StudentDetailDto>> UpdateRecordAsync(int id, UpdateStudentRecordRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result<StudentDetailDto>.Failure("Student not found.");

        static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var admission = Blank(request.AdmissionNumber);
        if (admission is not null && await _students.AdmissionNumberTakenAsync(admission, id, ct))
            return Result<StudentDetailDto>.Failure(AdmissionNumberTaken);

        var name = request.FullName.Trim();
        var dob = DateTime.SpecifyKind(request.DateOfBirth.Date, DateTimeKind.Unspecified);
        var changes = AuditDetails.Changes(
            ("fullName", student.FullName, name),
            ("dateOfBirth", DateOnly.FromDateTime(student.DateOfBirth), DateOnly.FromDateTime(dob)),
            ("grade", student.Grade, Blank(request.Grade)),
            ("section", student.Section, Blank(request.Section)),
            ("admissionNumber", student.AdmissionNumber, admission),
            ("gender", student.Gender, request.Gender));
        if (changes.Count > 0)
        {
            student.FullName = name;
            student.DateOfBirth = dob;
            student.Grade = Blank(request.Grade);
            student.Section = Blank(request.Section);
            student.AdmissionNumber = admission;
            student.Gender = request.Gender;
            student.UpdatedAt = DateTime.UtcNow;
            _audit.Record(new AuditEvent("StudentUpdated", "Student", id, $"Student {name} updated: {string.Join(", ", changes.Keys)}")
            { StudentId = id, Details = changes });
            // The unique index backs the check above when two admins save the same number at once.
            if (!await _students.TrySaveChangesAsync(ct)) return Result<StudentDetailDto>.Failure(AdmissionNumberTaken);
        }

        var withGuardians = await _students.GetWithGuardiansAsync(id, ct);
        return Result<StudentDetailDto>.Success(await ToDetailAsync(withGuardians!, ct));
    }

    public async Task<Result<StudentDetailDto>> UpdateAddressAsync(int id, AddressDto address, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result<StudentDetailDto>.Failure("Student not found.");

        var updated = address.ToEntity();
        var changed = AuditDetails.ChangedAddressParts(student.Address, updated);
        if (changed.Count > 0)
        {
            student.Address = updated;
            student.UpdatedAt = DateTime.UtcNow;
            _audit.Record(new AuditEvent("StudentAddressUpdated", "Student", id,
                updated is null ? $"Address of {student.FullName} cleared" : $"Address of {student.FullName} updated")
            { StudentId = id, Details = new { changedParts = changed } });
            await _students.SaveChangesAsync(ct);
        }

        var withGuardians = await _students.GetWithGuardiansAsync(id, ct);
        return Result<StudentDetailDto>.Success(await ToDetailAsync(withGuardians!, ct));
    }

    public async Task<Result> AssignGuardianAsync(int studentId, AssignGuardianRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(studentId, ct);
        if (student is null) return Result.Failure("Student not found.");

        var guardian = await _guardians.GetByIdAsync(request.GuardianId, ct);
        if (guardian is null) return Result.Failure("Guardian not found.");

        var existing = await _students.GetStudentGuardianAsync(studentId, request.GuardianId, ct);
        if (existing is not null)
            return Result.Failure("Guardian already associated with this student.");

        await _students.AddStudentGuardianAsync(new StudentGuardian
        {
            StudentId = studentId,
            GuardianId = request.GuardianId,
            Relationship = request.Relationship,
            IsAuthorizedForPickup = request.IsAuthorizedForPickup
        }, ct);
        _audit.Record(new AuditEvent("GuardianLinked", "Student", studentId, $"{guardian.FullName} linked to {student.FullName}")
        {
            StudentId = studentId, GuardianId = guardian.GuardianId,
            Details = new { relationship = request.Relationship, isAuthorizedForPickup = request.IsAuthorizedForPickup }
        });
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateGuardianLinkAsync(int studentId, int guardianId, UpdateStudentGuardianRequest request, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(studentId, guardianId, ct);
        if (link is null) return Result.Failure("Guardian is not linked to this student.");

        var changes = AuditDetails.Changes(
            ("relationship", link.Relationship, request.Relationship),
            ("isAuthorizedForPickup", link.IsAuthorizedForPickup, request.IsAuthorizedForPickup));
        link.Relationship = request.Relationship;
        link.IsAuthorizedForPickup = request.IsAuthorizedForPickup;
        if (changes.Count > 0)
            _audit.Record(new AuditEvent("GuardianLinkUpdated", "Student", studentId,
                $"Link between student {studentId} and guardian {guardianId} updated: {string.Join(", ", changes.Keys)}")
            { StudentId = studentId, GuardianId = guardianId, Details = changes });
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveGuardianLinkAsync(int studentId, int guardianId, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(studentId, guardianId, ct);
        if (link is null) return Result.Failure("Guardian is not linked to this student.");

        _students.RemoveStudentGuardian(link);
        _audit.Record(new AuditEvent("GuardianUnlinked", "Student", studentId,
            $"Guardian {guardianId} unlinked from student {studentId}; both records kept")
        {
            StudentId = studentId, GuardianId = guardianId,
            Details = new { relationship = link.Relationship, wasAuthorizedForPickup = link.IsAuthorizedForPickup }
        });
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<StudentDetailDto> ToDetailAsync(Student s, CancellationToken ct)
    {
        var snapshots = await _safety.GetSnapshotsAsync(new[] { s.StudentId }, ct);
        var snap = Snapshot(snapshots, s.StudentId);
        var stats = await _safety.GetAttendanceStatsAsync(s.StudentId, ct);

        var studentPhoto = await _photos.GetStudentPhotoTimestampsAsync(new[] { s.StudentId }, ct);
        var guardianIds = s.StudentGuardians.Select(sg => sg.GuardianId).ToList();
        var guardianPhotos = await _photos.GetGuardianPhotoTimestampsAsync(guardianIds, ct);

        var journey = (await _journey.GetHistoryAsync(s.StudentId, ct))
            .Take(ProfileHistoryRows)
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, s.FullName, e.EventType, e.Timestamp, e.Source, e.BusId, e.Bus?.BusNumber, e.Route?.RouteName))
            .ToList();

        var attendance = (await _journey.GetAttendanceAsync(s.StudentId, null, null, ct))
            .Take(ProfileHistoryRows)
            .Select(a => new AttendanceRecordDto(
                a.AttendanceId, a.StudentId, s.FullName, s.Grade, a.EventType, a.Date, a.Time, a.Status,
                a.EventType == AttendanceEventType.SchoolArrival && a.Time > TimeOnly.MinValue && IsLate(a, snap)))
            .ToList();

        var assignments = await _students.GetTransportHistoryAsync(s.StudentId, ct);
        var current = assignments.FirstOrDefault(a => a.Status == RecordStatus.Active);
        var transport = current is null ? null : new StudentTransportDto(
            current.Id, current.BusId, current.Bus.BusNumber, current.Bus.RegistrationNumber, current.Bus.Status,
            current.RouteId, current.Route?.RouteName, current.PickupStop?.Name, current.PickupStop?.PickupTime,
            current.DropStop?.Name, current.DropStop?.DropTime,
            current.Bus.DriverId, current.Bus.Driver?.FullName, current.Bus.Driver?.Phone, current.AssignedAt);
        var history = assignments.Select(a => new StudentTransportHistoryDto(a.Id, a.Bus.BusNumber, a.Route?.RouteName,
            a.PickupStop?.Name, a.DropStop?.Name, a.AssignedAt, a.EndedAt, a.Status)).ToList();

        return new StudentDetailDto(
            s.StudentId, s.FullName, s.DateOfBirth, s.Grade, s.Status,
            s.RfidCard is not null ? RfidMasking.Mask(s.RfidCard.CardUid) : null,
            s.StudentGuardians.Select(sg => new GuardianSummaryDto(
                sg.GuardianId, sg.Guardian.FullName, sg.Guardian.Email, sg.Guardian.Phone,
                sg.Relationship, sg.IsAuthorizedForPickup,
                guardianPhotos.ContainsKey(sg.GuardianId),
                sg.Guardian.FaceData is not null && sg.Guardian.FaceData.IsActive)).ToList(),
            s.CreatedAt,
            studentPhoto.ContainsKey(s.StudentId),
            studentPhoto.TryGetValue(s.StudentId, out var photoAt) ? photoAt : null,
            snap.Status, snap.PresentToday, snap.CheckInTime, snap.LastEventAt, snap.LastEventType,
            snap.BusNumber, snap.RouteName, snap.DriverName,
            s.RfidCard?.Status, s.RfidCard?.AssignedAt,
            stats, journey, attendance,
            s.Section, s.AdmissionNumber, s.Gender, AddressDto.From(s.Address),
            s.ArchivedAt, s.ArchiveReason,
            transport, history);
    }

    /// <summary>
    /// A historic arrival is only flagged late when it is today's — the school's late
    /// cutoff lives in the safety read model, and back-dating it row by row here would
    /// duplicate that policy in a second place.
    /// </summary>
    private bool IsLate(Attendance a, StudentSafetySnapshot snap) =>
        snap.LateToday && a.Date == _clock.Today && a.Time == snap.CheckInTime;

    private static StudentSafetySnapshot Snapshot(IReadOnlyDictionary<int, StudentSafetySnapshot> map, int studentId) =>
        map.TryGetValue(studentId, out var found) ? found : StudentSafetySnapshot.Unknown(studentId);
}
