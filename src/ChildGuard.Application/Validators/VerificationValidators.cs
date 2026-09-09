using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class RegisterFaceRequestValidator : AbstractValidator<RegisterFaceRequest>
{
    public RegisterFaceRequestValidator()
    {
        RuleFor(x => x.ImageBase64).NotEmpty()
            .WithMessage("A captured image is required to register a face.");
    }
}

public class FaceVerifyRequestValidator : AbstractValidator<FaceVerifyRequest>
{
    public FaceVerifyRequestValidator()
    {
        RuleFor(x => x.StudentId).GreaterThan(0);
        RuleFor(x => x.GuardianId).GreaterThan(0);
        RuleFor(x => x.ImageBase64).NotEmpty()
            .WithMessage("A captured image is required to verify a face.");
    }
}

public class ApprovePickupRequestValidator : AbstractValidator<ApprovePickupRequest>
{
    public ApprovePickupRequestValidator()
    {
        RuleFor(x => x.VerificationId).GreaterThan(0);
    }
}

public class RejectPickupRequestValidator : AbstractValidator<RejectPickupRequest>
{
    public RejectPickupRequestValidator()
    {
        RuleFor(x => x.VerificationId).GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}
