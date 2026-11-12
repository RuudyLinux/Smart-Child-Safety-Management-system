using System.Security.Cryptography;
using System.Text;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Infrastructure.FaceService;

/// <summary>AES-256-GCM with a random 96-bit nonce per value; the guardian id is authenticated as associated data.</summary>
public sealed class AesGcmEmbeddingProtector : IEmbeddingProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _activeKeyId;
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    public AesGcmEmbeddingProtector(BiometricsOptions options)
    {
        _activeKeyId = options.EncryptionKeyId;
        if (string.IsNullOrWhiteSpace(_activeKeyId) || _activeKeyId.Contains(':'))
            throw new InvalidOperationException("Biometrics:EncryptionKeyId must be a short id without ':'.");

        _keys[_activeKeyId] = DecodeKey(options.EncryptionKey!, "Biometrics:EncryptionKey");
        foreach (var (id, key) in options.PreviousKeys)
            _keys.TryAdd(id, DecodeKey(key, $"Biometrics:PreviousKeys:{id}"));
    }

    public string Protect(string embeddingJson, int guardianId)
    {
        var plain = Encoding.UTF8.GetBytes(embeddingJson);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_keys[_activeKeyId], TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, Aad(guardianId));

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);
        return $"{EmbeddingFormat.ProtectedPrefix}{_activeKeyId}:{Convert.ToBase64String(payload)}";
    }

    public string Unprotect(string stored, int guardianId)
    {
        if (!EmbeddingFormat.IsProtected(stored)) return stored;

        var rest = stored[EmbeddingFormat.ProtectedPrefix.Length..];
        var split = rest.IndexOf(':');
        if (split <= 0) throw new EmbeddingProtectionException("Stored face data is malformed.");
        if (!_keys.TryGetValue(rest[..split], out var key))
            throw new EmbeddingProtectionException("Face data was encrypted with a key that is no longer configured.");

        try
        {
            var payload = Convert.FromBase64String(rest[(split + 1)..]);
            if (payload.Length < NonceSize + TagSize) throw new EmbeddingProtectionException("Stored face data is malformed.");

            var plain = new byte[payload.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plain, Aad(guardianId));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new EmbeddingProtectionException("Stored face data could not be decrypted (wrong key, or the value was altered).");
        }
    }

    private static byte[] Aad(int guardianId) => Encoding.UTF8.GetBytes($"guardian:{guardianId}");

    private static byte[] DecodeKey(string base64, string setting)
    {
        try
        {
            var key = Convert.FromBase64String(base64);
            if (key.Length == 32) return key;
        }
        catch (FormatException) { /* reported below */ }
        throw new InvalidOperationException($"{setting} must be the base64 of exactly 32 bytes.");
    }
}
