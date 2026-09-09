using System.Text;

namespace ChildGuard.Application.Common;

/// <summary>
/// Guardians sign in with their registered contact number, which people type
/// inconsistently ("98200 11111", "+91-98200-11111"). Storing and looking up a
/// single normalized form keeps the unique index and the login lookup in agreement.
/// </summary>
public static class ContactNumber
{
    /// <summary>Keeps digits only, preserving a leading '+' country prefix. Returns null for null/blank input.</summary>
    public static string? Normalize(string? contactNumber)
    {
        if (string.IsNullOrWhiteSpace(contactNumber)) return null;

        var trimmed = contactNumber.Trim();
        var sb = new StringBuilder(trimmed.Length);
        if (trimmed[0] == '+') sb.Append('+');
        foreach (var c in trimmed)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }

        var normalized = sb.ToString();
        return normalized is "" or "+" ? null : normalized;
    }
}
