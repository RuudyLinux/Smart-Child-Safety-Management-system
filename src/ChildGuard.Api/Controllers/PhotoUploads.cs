using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Turns a multipart upload into the bytes the photo service validates. The
/// declared filename and content type are deliberately not carried forward — the
/// stored type is decided from the file's own bytes, so a mislabelled or hostile
/// upload cannot smuggle a type past the check.
/// </summary>
public static class PhotoUploads
{
    /// <summary>Request cap, slightly above the image cap so an oversized file is rejected with a clear message rather than a connection reset.</summary>
    public const long RequestSizeLimit = ProfileImage.MaxBytes + (512 * 1024);

    public static async Task<Result<UploadPhotoRequest>> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Result<UploadPhotoRequest>.Failure("Select an image to upload.");

        if (file.Length > ProfileImage.MaxBytes)
            return Result<UploadPhotoRequest>.Failure(
                $"Image is too large. Maximum size is {ProfileImage.MaxBytes / (1024 * 1024)} MB.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return Result<UploadPhotoRequest>.Success(new UploadPhotoRequest(buffer.ToArray()));
    }
}
