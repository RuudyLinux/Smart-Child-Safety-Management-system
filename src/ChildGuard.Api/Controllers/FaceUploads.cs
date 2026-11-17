namespace ChildGuard.Api.Controllers;

/// <summary>Request-size caps for face images sent as base64 JSON.</summary>
public static class FaceUploads
{
    /// <summary>One image: 2 MB of bytes is about 2.8 MB of base64, plus the JSON wrapper.</summary>
    public const long SingleImageRequestLimit = 3 * 1024 * 1024;

    /// <summary>A liveness burst: at most 16 frames, each well under 200 KB at the sizes the desk sends.</summary>
    public const long LiveBurstRequestLimit = 4 * 1024 * 1024;
}
