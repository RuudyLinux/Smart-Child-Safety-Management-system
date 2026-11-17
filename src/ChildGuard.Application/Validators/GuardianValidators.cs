using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class UpdateGuardianStatusRequestValidator : AbstractValidator<UpdateGuardianStatusRequest>
{
    public UpdateGuardianStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30)
            .Must(c => ContactNumber.Normalize(c) is { Length: >= 8 })
            .WithMessage("Enter a valid contact number.");
    }
}

public class RegisterDeviceTokenRequestValidator : AbstractValidator<RegisterDeviceTokenRequest>
{
    public RegisterDeviceTokenRequestValidator()
    {
        RuleFor(x => x.FcmToken).NotEmpty().MaximumLength(400);
    }
}

public class AdminUpdateGuardianRequestValidator : AbstractValidator<AdminUpdateGuardianRequest>
{
    public AdminUpdateGuardianRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.ContactNumber).NotEmpty().MaximumLength(30)
            .Must(c => ContactNumber.Normalize(c) is { Length: >= 8 })
            .WithMessage("Enter a valid contact number.");
        RuleFor(x => x.Status).IsInEnum();
    }
}

public class AdminUpdateGuardianCredentialsRequestValidator : AbstractValidator<AdminUpdateGuardianCredentialsRequest>
{
    public AdminUpdateGuardianCredentialsRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class CreateGuardianRequestValidator : AbstractValidator<CreateGuardianRequest>
{
    public CreateGuardianRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.ContactNumber).NotEmpty().MaximumLength(30)
            .Must(c => ContactNumber.Normalize(c) is { Length: >= 8 })
            .WithMessage("Enter a valid contact number.");
    }
}
