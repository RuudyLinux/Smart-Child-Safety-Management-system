using System.ComponentModel.DataAnnotations;

namespace ChildGuard.AdminWeb.Models.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Issues a persistent auth cookie instead of a session one, so the admin stays signed in across browser restarts.</summary>
    public bool RememberMe { get; set; }

    public string? ErrorMessage { get; set; }
}
