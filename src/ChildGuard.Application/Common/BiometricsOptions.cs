namespace ChildGuard.Application.Common;

/// <summary>Biometric-data handling, bound from the "Biometrics" configuration section.</summary>
public class BiometricsOptions
{
    /// <summary>
    /// Base64 of a 32-byte key (user-secrets or environment only, never the repo). When set, every new
    /// embedding is stored AES-256-GCM encrypted. Existing plain rows stay readable. Generate with
    /// <c>scripts/dev-setup-secrets.ps1</c> or any 32 random bytes.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>Identifies the active key inside each stored value, so a key can later be rotated.</summary>
    public string EncryptionKeyId { get; set; } = "k1";

    /// <summary>Retired keys (id to base64 key), kept only so values written under them can still be read.</summary>
    public Dictionary<string, string> PreviousKeys { get; set; } = new();

    /// <summary>Most reference faces per guardian, counting the first (primary) one.</summary>
    public int MaxReferencesPerGuardian { get; set; } = 5;
}
