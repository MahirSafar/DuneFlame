using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.User;
using DuneFlame.Domain.Enums;
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

        RuleFor(x => x.Phone)
            .MaximumLength(20)
            .When(x => x.Phone is not null);

        RuleFor(x => x.Subject)
            .MaximumLength(150)
            .MustBeSafeInput()
            .When(x => x.Subject is not null);

        RuleFor(x => x.InquiryType)
            .IsInEnum()
            .WithMessage("InquiryType must be one of: GeneralInquiry, OrderIssue, Wholesale, Support.");

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(1000)
            .MustBeSafeInput();
    }
}