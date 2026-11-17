using System.Security.Cryptography;

namespace ChildGuard.Application.Common;

/// <summary>
/// The password an admin-created Guardian starts with. Each Guardian gets their own
/// cryptographically random one: it is never sent to any client as a default value
/// and never appears in frontend code — the account is created with it server-side,
/// it is emailed to the Guardian, and the first login forces it to be replaced.
/// </summary>
public static class GuardianCredentialDefaults
{
    public const int InitialPasswordLength = 12;

    // No look-alike characters (0/O, 1/l/I), so it can be typed from an email reliably.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>A new random starting password that meets the guardian password rules
    /// (at least 8 characters, an uppercase letter, a digit).</summary>
    public static string NewInitialPassword()
    {
        const string all = Upper + Lower + Digits;
        var chars = new char[InitialPasswordLength];
        // One of each required class, the rest from the whole set, then shuffled.
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var i = 3; i < chars.Length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
