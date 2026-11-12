using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class RegisterGuardianRequestValidator : AbstractValidator<RegisterGuardianRequest>
{
    public RegisterGuardianRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Role).NotEmpty().Must(r => r is "Admin" or "Guardian")
            .WithMessage("Role must be Admin or Guardian.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Role).NotEmpty().Must(r => r is "Admin" or "Guardian")
            .WithMessage("Role must be Admin or Guardian.");
        RuleFor(x => x.ResetToken).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

// ---- Guardian OTP login flow ----

public class GuardianLoginRequestValidator : AbstractValidator<GuardianLoginRequest>
{
    public GuardianLoginRequestValidator()
    {
        RuleFor(x => x.ContactNumber).NotEmpty().MaximumLength(30)
            .Must(c => ContactNumber.Normalize(c) is { Length: >= 8 })
            .WithMessage("Enter a valid contact number.");
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.DeviceToken).MaximumLength(200);
    }
}

public class VerifyGuardianOtpRequestValidator : AbstractValidator<VerifyGuardianOtpRequest>
{
    public VerifyGuardianOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{4,8}$").WithMessage("Enter the code sent to your contact number.");
        RuleFor(x => x.DeviceLabel).MaximumLength(100);
    }
}

public class ResendGuardianOtpRequestValidator : AbstractValidator<ResendGuardianOtpRequest>
{
    public ResendGuardianOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty();
    }
}

public class GuardianResetPasswordRequestValidator : AbstractValidator<GuardianResetPasswordRequest>
{
    public GuardianResetPasswordRequestValidator()
    {
        RuleFor(x => x.ResetTicket).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
        RuleFor(x => x.ConfirmPassword).NotEmpty()
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
