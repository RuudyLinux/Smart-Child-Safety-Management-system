namespace ChildGuard.Infrastructure.Identity;

public class JwtSettings
{
    public string Secret { get; set; } = default!;
    public string Issuer { get; set; } = "ChildGuard";
    public string Audience { get; set; } = "ChildGuardClients";
    public int ExpiryMinutes { get; set; } = 120;
}
