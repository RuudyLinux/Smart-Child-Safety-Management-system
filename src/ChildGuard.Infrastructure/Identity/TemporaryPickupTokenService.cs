using System.Security.Cryptography;
using System.Text;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Infrastructure.Identity;

/// <summary>
/// QR token = "CGTP1.{id}.{nonce}.{mac}", mac = HMAC-SHA256(key, "tp:{id}:{nonce}") in base64url. The key is derived
/// from the JWT secret with a purpose label, so a QR token can never be used as a login token or the reverse.
/// The nonce lives on the record and is cleared on cancel, revoke and handover, which kills every shared copy.
/// </summary>
public sealed class TemporaryPickupTokenService : ITemporaryPickupTokenService
{
    private const string Prefix = "CGTP1";
    private readonly byte[] _key;

    public TemporaryPickupTokenService(string secret)
    {
        _key = new HMACSHA256(Encoding.UTF8.GetBytes(secret)).ComputeHash(Encoding.UTF8.GetBytes("childguard:temporary-pickup:v1"));
    }

    public string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(18));

    public string CreateToken(int id, string nonce) => $"{Prefix}.{id}.{nonce}.{Mac(id, nonce)}";

    public int? ReadId(string token)
    {
        var parts = (token ?? "").Trim().Split('.');
        return parts.Length == 4 && parts[0] == Prefix && int.TryParse(parts[1], out var id) && id > 0 ? id : null;
    }

    public bool Verify(string token, int id, string? nonce)
    {
        if (string.IsNullOrEmpty(nonce)) return false;
        var parts = (token ?? "").Trim().Split('.');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var tid) || tid != id) return false;
        var presentedNonce = Encoding.UTF8.GetBytes(parts[2]);
        var actualNonce = Encoding.UTF8.GetBytes(nonce);
        var nonceOk = CryptographicOperations.FixedTimeEquals(presentedNonce, actualNonce);
        var macOk = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[3]), Encoding.UTF8.GetBytes(Mac(id, nonce)));
        return nonceOk && macOk;
    }

    private string Mac(int id, string nonce) => Base64Url(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"tp:{id}:{nonce}")));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
