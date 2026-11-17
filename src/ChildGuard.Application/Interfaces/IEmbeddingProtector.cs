namespace ChildGuard.Application.Interfaces;

/// <summary>A stored embedding could not be read: wrong or missing key, or the value was altered.</summary>
public sealed class EmbeddingProtectionException : Exception
{
    public EmbeddingProtectionException(string message) : base(message) { }
}

/// <summary>
/// Protects face embeddings at rest. A stored value is either plain JSON (written before a key
/// was configured) or "enc:v1:{keyId}:{payload}". Each value is bound to its guardian, so a row
/// copied to another guardian does not decrypt.
/// </summary>
public interface IEmbeddingProtector
{
    string Protect(string embeddingJson, int guardianId);

    /// <summary>Returns the embedding JSON for either form. Throws <see cref="EmbeddingProtectionException"/> if it cannot be read.</summary>
    string Unprotect(string stored, int guardianId);
}

public static class EmbeddingFormat
{
    public const string ProtectedPrefix = "enc:v1:";
    public static bool IsProtected(string stored) => stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
}
