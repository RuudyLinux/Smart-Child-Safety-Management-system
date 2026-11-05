namespace ChildGuard.Application.Common;

/// <summary>
/// Validates an uploaded profile image at the trust boundary. The client's stated
/// filename and content type are ignored entirely: the type is decided by the
/// file's own leading bytes, so a script renamed to <c>.jpg</c> is rejected rather
/// than stored and later served back to a browser.
/// </summary>
public static class ProfileImage
{
    /// <summary>Upper bound on a stored image. Large enough for a phone photo, small enough to keep rows cheap.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    public const string AllowedDescription = "JPEG, PNG or WebP, up to 4 MB";

    /// <summary>
    /// Returns the sniffed content type, or a failure describing why the upload was
    /// refused. Only the three formats every target browser and Flutter can render
    /// without a decoder are accepted.
    /// </summary>
    public static Result<string> Validate(byte[] data)
    {
        if (data.Length == 0)
            return Result<string>.Failure("The selected file is empty.");

        if (data.Length > MaxBytes)
            return Result<string>.Failure($"Image is too large. Maximum size is {MaxBytes / (1024 * 1024)} MB.");

        var contentType = Sniff(data);
        return contentType is null
            ? Result<string>.Failure($"Unsupported image format. Allowed: {AllowedDescription}.")
            : Result<string>.Success(contentType);
    }

    /// <summary>Identifies the format from magic bytes. Null when the bytes are not a supported image.</summary>
    private static string? Sniff(byte[] d)
    {
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            return "image/jpeg";

        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 &&
            d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
            return "image/png";

        // RIFF....WEBP
        if (d.Length >= 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46 &&
            d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50)
            return "image/webp";

        return null;
    }
}
