using DuneFlame.Application.DTOs.User;
using FluentValidation;

namespace DuneFlame.Application.Validators;

public class NewsletterValidator : AbstractValidator<NewsletterRequest>
{
    public NewsletterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
    }
}