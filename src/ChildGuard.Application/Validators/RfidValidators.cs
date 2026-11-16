using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class UpdateRfidStatusRequestValidator : AbstractValidator<UpdateRfidStatusRequest>
{
    public UpdateRfidStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}

public class RfidScanRequestValidator : AbstractValidator<RfidScanRequest>
{
    public RfidScanRequestValidator()
    {
        RuleFor(x => x.CardUid).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RfidDeviceId).GreaterThan(0);
        RuleFor(x => x.Checkpoint).IsInEnum();
        RuleFor(x => x.ScanId).MaximumLength(64)
            .Matches("^[A-Za-z0-9._:-]+$").When(x => !string.IsNullOrWhiteSpace(x.ScanId))
            .WithMessage("ScanId may contain only letters, digits, '.', '_', ':' and '-'.");
    }
}

public class RfidIdentifyRequestValidator : AbstractValidator<RfidIdentifyRequest>
{
    public RfidIdentifyRequestValidator()
    {
        RuleFor(x => x.CardUid).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RfidDeviceId).GreaterThan(0);
    }
}

public class RfidCardCheckRequestValidator : AbstractValidator<RfidCardCheckRequest>
{
    public RfidCardCheckRequestValidator() => RuleFor(x => x.CardUid).NotEmpty().MaximumLength(100);
}

public class CreateRfidDeviceRequestValidator : AbstractValidator<CreateRfidDeviceRequest>
{
    public CreateRfidDeviceRequestValidator() => RuleFor(x => x.DeviceLabel).NotEmpty().MaximumLength(50);
}

public class UpdateRfidDeviceRequestValidator : AbstractValidator<UpdateRfidDeviceRequest>
{
    public UpdateRfidDeviceRequestValidator() => RuleFor(x => x.DeviceLabel).NotEmpty().MaximumLength(50);
}
