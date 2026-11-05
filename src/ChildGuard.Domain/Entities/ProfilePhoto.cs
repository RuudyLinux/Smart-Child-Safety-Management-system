namespace ChildGuard.Domain.Entities;

/// <summary>
/// A stored profile image. Bytes live in the database rather than on disk so the
/// image cannot be addressed by a server path, survives a redeploy with the data,
/// and is removed by the same cascade that removes its owner. Callers only ever
/// receive it through an authorized endpoint, never as a file location.
/// </summary>
public abstract class ProfilePhoto
{
    public int Id { get; set; }

    /// <summary>Sniffed image type — set from the file's own bytes, never from the client's claim.</summary>
    public string ContentType { get; set; } = default!;

    public byte[] Data { get; set; } = default!;

    /// <summary>Byte length, kept alongside so listings can report size without loading the blob.</summary>
    public int ByteSize { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class GuardianPhoto : ProfilePhoto
{
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
}

public class StudentPhoto : ProfilePhoto
{
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
}
