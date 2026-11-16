using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IPhotoRepository
{
    Task<GuardianPhoto?> GetGuardianPhotoAsync(int guardianId, CancellationToken ct = default);
    Task<StudentPhoto?> GetStudentPhotoAsync(int studentId, CancellationToken ct = default);
    Task<bool> GuardianExistsAsync(int guardianId, CancellationToken ct = default);
    Task<bool> StudentExistsAsync(int studentId, CancellationToken ct = default);
    Task<DriverPhoto?> GetDriverPhotoAsync(int driverId, CancellationToken ct = default);
    Task<bool> DriverExistsAsync(int driverId, CancellationToken ct = default);
    Task<Dictionary<int, DateTime>> GetDriverPhotoTimestampsAsync(IReadOnlyCollection<int>? driverIds = null, CancellationToken ct = default);
    Task AddAsync(ProfilePhoto photo, CancellationToken ct = default);
    void Remove(ProfilePhoto photo);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Owner id to last-updated time, for every owner that has a photo. Lists use
    /// this to decide between an image and initials without loading any blob, and
    /// the timestamp doubles as the cache-busting token on the image URL.
    /// </summary>
    Task<Dictionary<int, DateTime>> GetGuardianPhotoTimestampsAsync(IReadOnlyCollection<int>? guardianIds = null, CancellationToken ct = default);
    Task<Dictionary<int, DateTime>> GetStudentPhotoTimestampsAsync(IReadOnlyCollection<int>? studentIds = null, CancellationToken ct = default);
}

/// <summary>
/// Stores and serves profile images for guardians and students. Uploads are
/// validated here — at the trust boundary — rather than in any client, and images
/// are addressed only by owner id, so no storage path is ever exposed.
/// </summary>
public interface IPhotoService
{
    Task<Result<PhotoContentDto>> GetGuardianPhotoAsync(int guardianId, CancellationToken ct = default);
    Task<Result<PhotoStateDto>> SaveGuardianPhotoAsync(int guardianId, UploadPhotoRequest request, CancellationToken ct = default);
    Task<Result> RemoveGuardianPhotoAsync(int guardianId, CancellationToken ct = default);

    Task<Result<PhotoContentDto>> GetStudentPhotoAsync(int studentId, CancellationToken ct = default);
    Task<Result<PhotoStateDto>> SaveStudentPhotoAsync(int studentId, UploadPhotoRequest request, CancellationToken ct = default);
    Task<Result> RemoveStudentPhotoAsync(int studentId, CancellationToken ct = default);

    Task<Result<PhotoContentDto>> GetDriverPhotoAsync(int driverId, CancellationToken ct = default);
    Task<Result<PhotoStateDto>> SaveDriverPhotoAsync(int driverId, UploadPhotoRequest request, CancellationToken ct = default);
    Task<Result> RemoveDriverPhotoAsync(int driverId, CancellationToken ct = default);
}
