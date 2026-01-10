using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.User;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class ContactMessageValidator : AbstractValidator<ContactMessageRequest>
{
    public ContactMessageValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .MustBeSafeInput();

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Subject)
            .NotEmpty()
            .MaximumLength(150)
            .MustBeSafeInput();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(1000)
            .MustBeSafeInput();
    }
}