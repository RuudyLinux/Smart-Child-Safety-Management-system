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
    }
}
