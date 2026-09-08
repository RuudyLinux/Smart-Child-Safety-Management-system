using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

public static class ControllerExtensions
{
    /// <summary>Extracts the authenticated user's numeric id from the JWT "sub" claim.</summary>
    public static int GetUserId(this ControllerBase controller)
    {
        var value = controller.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? controller.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var id) ? id : throw new InvalidOperationException("User id claim missing or invalid.");
    }
}
