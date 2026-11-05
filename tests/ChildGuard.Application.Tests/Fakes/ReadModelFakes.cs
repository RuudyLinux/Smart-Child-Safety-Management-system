using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Tests.Fakes;

/// <summary>
/// In-memory doubles for the read-side dependencies the guardian and student
/// services take. They carry no derivation logic of their own: these tests are
/// about link management and credential flow, and a fake that recomputed safety
/// status would be asserting the fake rather than the system.
/// </summary>
internal sealed class FakePhotoRepository : IPhotoRepository
{
    public List<ProfilePhoto> Photos { get; } = new();

    public Task<GuardianPhoto?> GetGuardianPhotoAsync(int guardianId, CancellationToken ct = default) =>
        Task.FromResult(Photos.OfType<GuardianPhoto>().FirstOrDefault(p => p.GuardianId == guardianId));

    public Task<StudentPhoto?> GetStudentPhotoAsync(int studentId, CancellationToken ct = default) =>
        Task.FromResult(Photos.OfType<StudentPhoto>().FirstOrDefault(p => p.StudentId == studentId));

    public Task<bool> GuardianExistsAsync(int guardianId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> StudentExistsAsync(int studentId, CancellationToken ct = default) => Task.FromResult(true);

    public Task AddAsync(ProfilePhoto photo, CancellationToken ct = default)
    {
        Photos.Add(photo);
        return Task.CompletedTask;
    }

    public void Remove(ProfilePhoto photo) => Photos.Remove(photo);

    public Task<Dictionary<int, DateTime>> GetGuardianPhotoTimestampsAsync(
        IReadOnlyCollection<int>? guardianIds = null, CancellationToken ct = default) =>
        Task.FromResult(Photos.OfType<GuardianPhoto>()
            .Where(p => guardianIds is null || guardianIds.Count == 0 || guardianIds.Contains(p.GuardianId))
            .ToDictionary(p => p.GuardianId, p => p.UpdatedAt));

    public Task<Dictionary<int, DateTime>> GetStudentPhotoTimestampsAsync(
        IReadOnlyCollection<int>? studentIds = null, CancellationToken ct = default) =>
        Task.FromResult(Photos.OfType<StudentPhoto>()
            .Where(p => studentIds is null || studentIds.Count == 0 || studentIds.Contains(p.StudentId))
            .ToDictionary(p => p.StudentId, p => p.UpdatedAt));

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Reports every student as having no recorded activity — the honest default for a test with no events.</summary>
internal sealed class FakeSafetyReadService : ISafetyReadService
{
    public Task<IReadOnlyDictionary<int, StudentSafetySnapshot>> GetSnapshotsAsync(
        IReadOnlyCollection<int> studentIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, StudentSafetySnapshot>>(
            studentIds.Distinct().ToDictionary(id => id, StudentSafetySnapshot.Unknown));

    public Task<List<StudentJourneyStatusDto>> GetLiveJourneyStatusesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<StudentJourneyStatusDto>());

    public Task<AttendanceStatsDto> GetAttendanceStatsAsync(int studentId, CancellationToken ct = default) =>
        Task.FromResult(new AttendanceStatsDto(30, 0, 0, 0, 0));
}

internal sealed class FakeJourneyRepository : IJourneyRepository
{
    public List<JourneyEvent> Events { get; } = new();
    public List<Attendance> AttendanceRecords { get; } = new();

    /// <summary>Number of times work ran under the per-student lock — lets tests assert the write path is serialized.</summary>
    public int SerializedRuns { get; private set; }

    public Task<JourneyEvent?> GetLastEventSinceAsync(int studentId, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(Events
            .Where(e => e.StudentId == studentId && e.Timestamp >= sinceUtc)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault());

    public async Task<T> RunSerializedForStudentAsync<T>(int studentId, Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        SerializedRuns++;
        return await work(ct);
    }

    public Task<(List<JourneyEvent> Items, int TotalCount)> GetPageAsync(
        DateTime fromUtc, DateTime toUtcExclusive, int? studentId, int skip, int take, CancellationToken ct = default)
    {
        var matching = Events
            .Where(e => e.Timestamp >= fromUtc && e.Timestamp < toUtcExclusive)
            .Where(e => studentId is null || e.StudentId == studentId)
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.JourneyEventId)
            .ToList();
        return Task.FromResult((matching.Skip(skip).Take(take).ToList(), matching.Count));
    }

    public Task AddJourneyEventAsync(JourneyEvent journeyEvent, CancellationToken ct = default)
    {
        Events.Add(journeyEvent);
        return Task.CompletedTask;
    }

    public Task AddAttendanceAsync(Attendance attendance, CancellationToken ct = default)
    {
        AttendanceRecords.Add(attendance);
        return Task.CompletedTask;
    }

    public Task<List<JourneyEvent>> GetHistoryAsync(int studentId, CancellationToken ct = default) =>
        Task.FromResult(Events.Where(e => e.StudentId == studentId).OrderByDescending(e => e.Timestamp).ToList());

    public Task<List<JourneyEvent>> GetRecentAsync(int take, CancellationToken ct = default) =>
        Task.FromResult(Events.OrderByDescending(e => e.Timestamp).Take(take).ToList());

    public Task<List<Attendance>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default) =>
        Task.FromResult(AttendanceRecords
            .Where(a => studentId is null || a.StudentId == studentId)
            .Where(a => from is null || a.Date >= from)
            .Where(a => to is null || a.Date <= to)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeVerificationRepository : IVerificationRepository
{
    public List<GuardianFaceData> FaceData { get; } = new();
    public List<GuardianVerification> Verifications { get; } = new();

    /// <summary>Set to make the next SaveChangesAsync behave like a concurrent writer already won.</summary>
    public bool SimulateConcurrencyConflictOnNextSave { get; set; }

    public Task<GuardianFaceData?> GetFaceDataAsync(int guardianId, CancellationToken ct = default) =>
        Task.FromResult(FaceData.FirstOrDefault(f => f.GuardianId == guardianId));

    public Task UpsertFaceDataAsync(GuardianFaceData data, CancellationToken ct = default)
    {
        FaceData.RemoveAll(f => f.GuardianId == data.GuardianId);
        FaceData.Add(data);
        return Task.CompletedTask;
    }

    public Task AddVerificationAsync(GuardianVerification verification, CancellationToken ct = default)
    {
        verification.Id = Verifications.Count + 1;
        Verifications.Add(verification);
        return Task.CompletedTask;
    }

    public Task<GuardianVerification?> GetVerificationByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Verifications.FirstOrDefault(v => v.Id == id));

    public Task<List<GuardianVerification>> GetHistoryAsync(int? studentId, DateTime? fromUtc = null, DateTime? toUtcExclusive = null, CancellationToken ct = default) =>
        Task.FromResult(Verifications
            .Where(v => studentId is null || v.StudentId == studentId)
            .Where(v => fromUtc is null || v.AttemptedAt >= fromUtc)
            .Where(v => toUtcExclusive is null || v.AttemptedAt < toUtcExclusive)
            .OrderByDescending(v => v.AttemptedAt)
            .ToList());

    public Task<List<GuardianVerification>> GetHistoryForGuardianAsync(int guardianId, int take, CancellationToken ct = default) =>
        Task.FromResult(Verifications
            .Where(v => v.GuardianId == guardianId)
            .OrderByDescending(v => v.AttemptedAt)
            .Take(take)
            .ToList());

    public Task<Dictionary<int, DateTime>> GetLastActivityByGuardianAsync(CancellationToken ct = default) =>
        Task.FromResult(Verifications
            .GroupBy(v => v.GuardianId)
            .ToDictionary(g => g.Key, g => g.Max(v => v.AttemptedAt)));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        if (SimulateConcurrencyConflictOnNextSave)
        {
            SimulateConcurrencyConflictOnNextSave = false;
            throw new ConcurrencyConflictException("Simulated concurrent write.");
        }
        return Task.CompletedTask;
    }
}
