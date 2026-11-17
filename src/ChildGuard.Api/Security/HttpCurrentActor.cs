using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.Security;

/// <summary>The audit actor of the current request, read from its authenticated principal.</summary>
public sealed class HttpCurrentActor : ICurrentActor
{
    public HttpCurrentActor(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (accessor.HttpContext is null)
        {
            Type = "System";
            return;
        }
        if (user?.Identity?.IsAuthenticated != true)
        {
            Type = "Anonymous";
            return;
        }

        Name = user.FindFirst(ClaimTypes.Name)?.Value;
        if (user.HasClaim("scanner", "device"))
        {
            Type = "Device";
            Id = int.TryParse(user.FindFirst(ApiKeyAuthenticationHandler.DeviceIdClaim)?.Value, out var deviceId) ? deviceId : null;
            return;
        }

        Type = user.IsInRole(nameof(AppRole.Admin)) ? "Admin" : user.IsInRole(nameof(AppRole.Guardian)) ? "Guardian" : "Anonymous";
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Id = int.TryParse(sub, out var id) ? id : null;
    }

    public string Type { get; }
    public int? Id { get; }
    public string? Name { get; }
}
