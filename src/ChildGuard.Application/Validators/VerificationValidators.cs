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

public class AnalyzeImageRequestValidator : AbstractValidator<AnalyzeImageRequest>
{
    /// <summary>Longest accepted base64 text: 2 MB of image bytes, plus a data-URI prefix.</summary>
    public const int MaxBase64Length = 2_900_000;

    public AnalyzeImageRequestValidator()
    {
        RuleFor(x => x.ImageBase64).NotEmpty()
            .WithMessage("An image is required.")
            .MaximumLength(MaxBase64Length)
            .WithMessage("The image is too large. Use a photo under 2 MB.");
        RuleFor(x => x.Purpose)
            .Must(p => p is null or "registration" or "verification")
            .WithMessage("Purpose must be 'registration' or 'verification'.");
    }
}

public class AddFaceReferenceRequestValidator : AbstractValidator<AddFaceReferenceRequest>
{
    public AddFaceReferenceRequestValidator()
    {
        RuleFor(x => x.ImageBase64).NotEmpty()
            .WithMessage("A photo is required.")
            .MaximumLength(AnalyzeImageRequestValidator.MaxBase64Length)
            .WithMessage("The image is too large. Use a photo under 2 MB.");
        RuleFor(x => x.Label).MaximumLength(60)
            .WithMessage("The label can be at most 60 characters.");
    }
}

public class StartVerificationSessionRequestValidator : AbstractValidator<StartVerificationSessionRequest>
{
    public StartVerificationSessionRequestValidator() => RuleFor(x => x.StudentId).GreaterThan(0);
}

public class SessionIdentifyRequestValidator : AbstractValidator<SessionIdentifyRequest>
{
    public SessionIdentifyRequestValidator()
    {
        RuleFor(x => x.Source).IsInEnum().WithMessage("Source must be Webcam or Gallery.");
        RuleFor(x => x.ImageBase64).NotEmpty().WithMessage("An image is required.")
            .MaximumLength(AnalyzeImageRequestValidator.MaxBase64Length)
            .WithMessage("The image is too large. Use a photo under 2 MB.");
    }
}

public class LiveVerifyRequestValidator : AbstractValidator<LiveVerifyRequest>
{
    /// <summary>One camera frame: well above a 960 px JPEG, far below an abusive upload.</summary>
    public const int MaxFrameBase64Length = 700_000;

    /// <summary>All frames together (about 3 MB of image data). Kestrel also caps the request body; this holds whatever hosts the API.</summary>
    public const int MaxTotalBase64Length = 4_000_000;

    public LiveVerifyRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Frames).NotNull().Must(f => f is { Count: > 0 and <= 20 })
            .WithMessage("Send the camera frames of one live check (at most 20).");
        RuleFor(x => x.Frames).Must(f => f is null || f.Sum(x => (long)(x.ImageBase64?.Length ?? 0)) <= MaxTotalBase64Length)
            .WithMessage("The camera frames are too large in total. Send smaller frames.");
        RuleForEach(x => x.Frames).ChildRules(f =>
        {
            f.RuleFor(x => x.ImageBase64).NotEmpty().MaximumLength(MaxFrameBase64Length)
                .WithMessage("A camera frame is missing or too large.");
            f.RuleFor(x => x.TMs).GreaterThanOrEqualTo(0);
        });
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
