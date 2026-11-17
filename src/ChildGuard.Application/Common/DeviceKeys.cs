using System.Security.Cryptography;
using System.Text;

namespace ChildGuard.Application.Common;

/// <summary>
/// Per-scanner API keys. A key is 32 random bytes (base64url) with a "cgd_" prefix;
/// only its SHA-256 is stored. A high-entropy random key needs no salt/slow hash —
/// guessing it is infeasible, and SHA-256 lets the handler look it up by index.
/// </summary>
public static class DeviceKeys
{
    public const string Prefix = "cgd_";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
