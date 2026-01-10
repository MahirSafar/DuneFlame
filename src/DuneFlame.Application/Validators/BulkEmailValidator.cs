using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.User;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class BulkEmailValidator : AbstractValidator<BulkEmailRequest>
{
    public BulkEmailValidator()
    {
        RuleFor(x => x.Subject)
            .NotEmpty()
            .MaximumLength(200)
            .MustBeSafeInput();

        RuleFor(x => x.Content)
            .NotEmpty()
            .MustBeSafeInput();
    }
}