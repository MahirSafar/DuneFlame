using DuneFlame.Application.DTOs.Wholesale;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class CreateWholesaleLeadRequestValidator : AbstractValidator<CreateWholesaleLeadRequest>
{
    public CreateWholesaleLeadRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.BusinessName)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .NotEmpty()
            .MaximumLength(30);

        RuleFor(x => x.BusinessType)
            .IsInEnum()
            .WithMessage("BusinessType must be one of: Cafe, Restaurant, Office, Distributor.");

        RuleFor(x => x.MonthlyVolume)
            .IsInEnum()
            .WithMessage("MonthlyVolume must be one of: Under5kg, From5To20kg, From20To50kg, Over50kg.");

        RuleFor(x => x.Message)
            .MaximumLength(1000);
    }
}
