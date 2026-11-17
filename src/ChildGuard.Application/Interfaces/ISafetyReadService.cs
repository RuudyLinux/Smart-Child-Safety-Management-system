using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

/// <summary>
/// One student's live safety picture, derived from recorded journey, attendance and
/// transport rows. Every consumer that shows "is this child safe right now?" — the
/// dashboard, the student list, a guardian's linked children, the Guardian app —
/// reads it from here, so the derivation exists once rather than per screen.
/// </summary>
public record StudentSafetySnapshot(
    int StudentId,
    StudentSafetyStatus Status,
    JourneyEventType? LastEventType,
    DateTime? LastEventAt,
    TimeOnly? CheckInTime,
    bool PresentToday,
    bool LateToday,
    string? BusNumber,
    string? RouteName,
    string? DriverName,
    bool HasPhoto)
{
    /// <summary>What a student with no recorded activity looks like, so callers never special-case a missing key.</summary>
    public static StudentSafetySnapshot Unknown(int studentId) =>
        new(studentId, StudentSafetyStatus.NoActivityToday, null, null, null, false, false, null, null, null, false);
}

public interface ISafetyReadService
{
    /// <summary>Snapshots for the given students. Students with no activity are still present in the result.</summary>
    Task<IReadOnlyDictionary<int, StudentSafetySnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<int> studentIds, CancellationToken ct = default);

    /// <summary>Live journey board for every active student, newest activity first.</summary>
    Task<List<StudentJourneyStatusDto>> GetLiveJourneyStatusesAsync(CancellationToken ct = default);

    /// <summary>Attendance counted over the configured trailing window for one student.</summary>
    Task<AttendanceStatsDto> GetAttendanceStatsAsync(int studentId, CancellationToken ct = default);
}
