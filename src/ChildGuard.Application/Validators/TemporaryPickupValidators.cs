using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class CreateTemporaryPickupRequestValidator : AbstractValidator<CreateTemporaryPickupRequest>
{
    public CreateTemporaryPickupRequestValidator()
    {
        RuleFor(x => x.StudentId).GreaterThan(0).WithMessage("Choose the child to be collected.");
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Enter the full name of the person collecting.")
            .MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Enter the contact number of the person collecting.")
            .MaximumLength(20)
            .Matches(@"^\+?[0-9 ()\-]{7,20}$").WithMessage("Enter a valid contact number.");
        RuleFor(x => x.Relationship).NotEmpty().WithMessage("Say how this person is related to the child.")
            .MaximumLength(50);
        RuleFor(x => x.IdentityNote).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.PhotoBase64).NotEmpty().WithMessage("A photo of the person collecting is required.")
            .MaximumLength(AnalyzeImageRequestValidator.MaxBase64Length)
            .WithMessage("The photo is too large. Use a photo under 2 MB.");
        RuleFor(x => x.ValidUntilUtc).GreaterThan(x => x.ValidFromUtc).WithMessage("The end time must be after the start time.");
    }
}

public class TemporaryPickupHandoverRequestValidator : AbstractValidator<TemporaryPickupHandoverRequest>
{
    public TemporaryPickupHandoverRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ImageBase64).NotEmpty().WithMessage("Capture a photo of the person collecting.")
            .MaximumLength(AnalyzeImageRequestValidator.MaxBase64Length)
            .WithMessage("The image is too large.");
    }
}

public class ScanTemporaryPickupRequestValidator : AbstractValidator<ScanTemporaryPickupRequest>
{
    public ScanTemporaryPickupRequestValidator() =>
        RuleFor(x => x.Token).NotEmpty().WithMessage("Scan or paste the QR code.").MaximumLength(200);
}
