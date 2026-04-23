using DuneFlame.Application.Interfaces;
using FluentValidation;
using System;

namespace DuneFlame.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    private readonly ICategoryService _categoryService;

    public UpdateProductCommandValidator(ICategoryService categoryService)
    {
        _categoryService = categoryService;

        RuleFor(v => v.Id)
            .NotEmpty().WithMessage("Product Id is required.");

        RuleFor(v => v.CategoryId)
            .NotEmpty().WithMessage("Category Id is required.")
            .MustAsync(async (categoryId, cancellation) =>
                await _categoryService.IsLeafCategoryAsync(categoryId))
            .WithMessage("Products can only be assigned to leaf categories (categories with no sub-categories).");
            
        RuleFor(v => v.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
            
        RuleFor(v => v.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleForEach(v => v.Variants).ChildRules(variant =>
        {
            variant.RuleFor(x => x.Sku)
                .NotEmpty().WithMessage("SKU is required.");
                
            variant.RuleFor(x => x.StockQuantity)
                .GreaterThanOrEqualTo(0).WithMessage("Stock quantity cannot be less than 0.");
                
            variant.RuleForEach(x => x.Prices).ChildRules(price => 
            {
                price.RuleFor(p => p.Price)
                     .GreaterThanOrEqualTo(0).WithMessage("Price must be greater than or equal to 0.");
                     
                price.RuleFor(p => p.CurrencyCode)
                     .NotEmpty().WithMessage("CurrencyCode is required.");
            });
        });
    }
}
