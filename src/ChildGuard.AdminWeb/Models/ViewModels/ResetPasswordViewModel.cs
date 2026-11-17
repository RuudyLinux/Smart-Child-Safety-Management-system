using System.ComponentModel.DataAnnotations;

namespace ChildGuard.AdminWeb.Models.ViewModels;

public class ResetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string ResetToken { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}
