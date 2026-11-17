using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Services;

public class PhotoService : IPhotoService
{
    private const string NoGuardian = "Guardian not found.";
    private const string NoStudent = "Student not found.";
    private const string NoGuardianPhoto = "This guardian has no profile photo.";
    private const string NoStudentPhoto = "This student has no profile photo.";
    private const string NoDriver = "Driver not found.";
    private const string NoDriverPhoto = "This driver has no profile photo.";

    private readonly IPhotoRepository _photos;
    private readonly IAuditTrail _audit;

    public PhotoService(IPhotoRepository photos, IAuditTrail? audit = null)
    {
        _photos = photos;
        _audit = audit ?? NullAuditTrail.Instance;
    }

    public async Task<Result<PhotoContentDto>> GetGuardianPhotoAsync(int guardianId, CancellationToken ct = default)
    {
        var photo = await _photos.GetGuardianPhotoAsync(guardianId, ct);
        return photo is null
            ? Result<PhotoContentDto>.Failure(NoGuardianPhoto)
            : Result<PhotoContentDto>.Success(new PhotoContentDto(photo.Data, photo.ContentType, photo.UpdatedAt));
    }

    public async Task<Result<PhotoStateDto>> SaveGuardianPhotoAsync(int guardianId, UploadPhotoRequest request, CancellationToken ct = default)
    {
        if (!await _photos.GuardianExistsAsync(guardianId, ct))
            return Result<PhotoStateDto>.Failure(NoGuardian);

        var validation = ProfileImage.Validate(request.Data);
        if (!validation.Succeeded) return Result<PhotoStateDto>.Failure(validation.Error!);

        var existing = await _photos.GetGuardianPhotoAsync(guardianId, ct);
        // Replacing overwrites the single row for this owner, so an owner can never
        // accumulate stale images and "replace" needs no separate delete step.
        var photo = existing ?? new GuardianPhoto { GuardianId = guardianId };
        Apply(photo, request.Data, validation.Value!);
        if (existing is null) await _photos.AddAsync(photo, ct);
        _audit.Record(new AuditEvent(existing is null ? "GuardianPhotoAdded" : "GuardianPhotoReplaced", "Guardian", guardianId, $"Profile photo of guardian {guardianId} {(existing is null ? "added" : "replaced")}") { GuardianId = guardianId, Details = new { photo.ContentType, photo.ByteSize } });
        await _photos.SaveChangesAsync(ct);

        return Result<PhotoStateDto>.Success(new PhotoStateDto(true, photo.UpdatedAt, photo.ByteSize, photo.ContentType));
    }

    public async Task<Result> RemoveGuardianPhotoAsync(int guardianId, CancellationToken ct = default)
    {
        var photo = await _photos.GetGuardianPhotoAsync(guardianId, ct);
        if (photo is null) return Result.Failure(NoGuardianPhoto);
        _photos.Remove(photo);
        _audit.Record(new AuditEvent("GuardianPhotoRemoved", "Guardian", guardianId, $"Profile photo of guardian {guardianId} removed") { GuardianId = guardianId });
        await _photos.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PhotoContentDto>> GetStudentPhotoAsync(int studentId, CancellationToken ct = default)
    {
        var photo = await _photos.GetStudentPhotoAsync(studentId, ct);
        return photo is null
            ? Result<PhotoContentDto>.Failure(NoStudentPhoto)
            : Result<PhotoContentDto>.Success(new PhotoContentDto(photo.Data, photo.ContentType, photo.UpdatedAt));
    }

    public async Task<Result<PhotoStateDto>> SaveStudentPhotoAsync(int studentId, UploadPhotoRequest request, CancellationToken ct = default)
    {
        if (!await _photos.StudentExistsAsync(studentId, ct))
            return Result<PhotoStateDto>.Failure(NoStudent);

        var validation = ProfileImage.Validate(request.Data);
        if (!validation.Succeeded) return Result<PhotoStateDto>.Failure(validation.Error!);

        var existing = await _photos.GetStudentPhotoAsync(studentId, ct);
        var photo = existing ?? new StudentPhoto { StudentId = studentId };
        Apply(photo, request.Data, validation.Value!);
        if (existing is null) await _photos.AddAsync(photo, ct);
        _audit.Record(new AuditEvent(existing is null ? "StudentPhotoAdded" : "StudentPhotoReplaced", "Student", studentId, $"Profile photo of student {studentId} {(existing is null ? "added" : "replaced")}") { StudentId = studentId, Details = new { photo.ContentType, photo.ByteSize } });
        await _photos.SaveChangesAsync(ct);

        return Result<PhotoStateDto>.Success(new PhotoStateDto(true, photo.UpdatedAt, photo.ByteSize, photo.ContentType));
    }

    public async Task<Result> RemoveStudentPhotoAsync(int studentId, CancellationToken ct = default)
    {
        var photo = await _photos.GetStudentPhotoAsync(studentId, ct);
        if (photo is null) return Result.Failure(NoStudentPhoto);
        _photos.Remove(photo);
        _audit.Record(new AuditEvent("StudentPhotoRemoved", "Student", studentId, $"Profile photo of student {studentId} removed") { StudentId = studentId });
        await _photos.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PhotoContentDto>> GetDriverPhotoAsync(int driverId, CancellationToken ct = default)
    {
        var photo = await _photos.GetDriverPhotoAsync(driverId, ct);
        return photo is null
            ? Result<PhotoContentDto>.Failure(NoDriverPhoto)
            : Result<PhotoContentDto>.Success(new PhotoContentDto(photo.Data, photo.ContentType, photo.UpdatedAt));
    }

    public async Task<Result<PhotoStateDto>> SaveDriverPhotoAsync(int driverId, UploadPhotoRequest request, CancellationToken ct = default)
    {
        if (!await _photos.DriverExistsAsync(driverId, ct))
            return Result<PhotoStateDto>.Failure(NoDriver);

        var validation = ProfileImage.Validate(request.Data);
        if (!validation.Succeeded) return Result<PhotoStateDto>.Failure(validation.Error!);

        var existing = await _photos.GetDriverPhotoAsync(driverId, ct);
        var photo = existing ?? new DriverPhoto { DriverId = driverId };
        Apply(photo, request.Data, validation.Value!);
        if (existing is null) await _photos.AddAsync(photo, ct);
        _audit.Record(new AuditEvent(existing is null ? "DriverPhotoAdded" : "DriverPhotoReplaced", "Driver", driverId,
            $"Profile photo of driver {driverId} {(existing is null ? "added" : "replaced")}") { Details = new { photo.ContentType, photo.ByteSize } });
        await _photos.SaveChangesAsync(ct);

        return Result<PhotoStateDto>.Success(new PhotoStateDto(true, photo.UpdatedAt, photo.ByteSize, photo.ContentType));
    }

    public async Task<Result> RemoveDriverPhotoAsync(int driverId, CancellationToken ct = default)
    {
        var photo = await _photos.GetDriverPhotoAsync(driverId, ct);
        if (photo is null) return Result.Failure(NoDriverPhoto);
        _photos.Remove(photo);
        _audit.Record(new AuditEvent("DriverPhotoRemoved", "Driver", driverId, $"Profile photo of driver {driverId} removed"));
        await _photos.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static void Apply(ProfilePhoto photo, byte[] data, string contentType)
    {
        photo.Data = data;
        // Sniffed, not taken from the upload's declared type.
        photo.ContentType = contentType;
        photo.ByteSize = data.Length;
        photo.UpdatedAt = DateTime.UtcNow;
    }
}
