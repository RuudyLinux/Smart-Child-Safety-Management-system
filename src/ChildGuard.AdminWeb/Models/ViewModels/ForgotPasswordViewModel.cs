using System.ComponentModel.DataAnnotations;

namespace ChildGuard.AdminWeb.Models.ViewModels;

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? InfoMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
