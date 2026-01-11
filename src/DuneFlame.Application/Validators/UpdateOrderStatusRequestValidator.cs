using DuneFlame.Application.DTOs.Admin;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .InclusiveBetween(0, 4)
            .WithMessage("Status must be a valid OrderStatus value (0-4).");
    }
}
