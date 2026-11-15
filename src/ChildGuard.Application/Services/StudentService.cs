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

    public StudentService(
        IStudentRepository students,
        IGuardianRepository guardians,
        IJourneyRepository journey,
        IPhotoRepository photos,
        ISafetyReadService safety,
        SchoolClock clock)
    {
        _clock = clock;
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
                snap.LastEventAt, snap.LastEventType, snap.BusNumber);
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
        await _students.SaveChangesAsync(ct);
        return Result<StudentDetailDto>.Success(await ToDetailAsync(student, ct));
    }

    public async Task<Result<StudentDetailDto>> UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result<StudentDetailDto>.Failure("Student not found.");

        student.FullName = request.FullName;
        student.DateOfBirth = request.DateOfBirth;
        student.Grade = request.Grade;
        student.UpdatedAt = DateTime.UtcNow;
        await _students.SaveChangesAsync(ct);

        var withGuardians = await _students.GetWithGuardiansAsync(id, ct);
        return Result<StudentDetailDto>.Success(await ToDetailAsync(withGuardians!, ct));
    }

    public async Task<Result> UpdateStatusAsync(int id, UpdateStudentStatusRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result.Failure("Student not found.");

        student.Status = request.Status;
        student.UpdatedAt = DateTime.UtcNow;
        await _students.SaveChangesAsync(ct);
        return Result.Success();
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
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateGuardianLinkAsync(int studentId, int guardianId, UpdateStudentGuardianRequest request, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(studentId, guardianId, ct);
        if (link is null) return Result.Failure("Guardian is not linked to this student.");

        link.Relationship = request.Relationship;
        link.IsAuthorizedForPickup = request.IsAuthorizedForPickup;
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveGuardianLinkAsync(int studentId, int guardianId, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(studentId, guardianId, ct);
        if (link is null) return Result.Failure("Guardian is not linked to this student.");

        _students.RemoveStudentGuardian(link);
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
            stats, journey, attendance);
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
