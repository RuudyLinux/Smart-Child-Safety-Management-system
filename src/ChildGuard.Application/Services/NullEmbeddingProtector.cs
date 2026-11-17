using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.Services;

/// <summary>Used when no Biometrics:EncryptionKey is configured: stores plain JSON, and refuses (never guesses) a value that is encrypted.</summary>
public sealed class NullEmbeddingProtector : IEmbeddingProtector
{
    public string Protect(string embeddingJson, int guardianId) => embeddingJson;

    public string Unprotect(string stored, int guardianId) =>
        EmbeddingFormat.IsProtected(stored)
            ? throw new EmbeddingProtectionException("Face data is encrypted but Biometrics:EncryptionKey is not configured.")
            : stored;
}
