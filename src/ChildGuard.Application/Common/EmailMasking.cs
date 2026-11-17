namespace ChildGuard.Application.Common;

/// <summary>Shows enough of an email address to recognise the inbox ("a***@gmail.com") without revealing it.</summary>
public static class EmailMasking
{
    public static string? Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        if (at < 1 || at == email.Length - 1) return null;

        var local = email[..at];
        var domain = email[(at + 1)..];
        return $"{local[0]}***@{domain}";
    }
}
