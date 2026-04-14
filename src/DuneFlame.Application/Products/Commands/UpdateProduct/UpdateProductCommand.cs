using MediatR;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace DuneFlame.Application.Products.Commands.UpdateProduct;

public record UpdateProductCommand : IRequest<bool>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid CategoryId { get; init; }
    public bool IsActive { get; init; }
    
    // Base properties
    public List<UpdateProductTranslationDto>? Translations { get; init; }
    public List<IFormFile>? Images { get; init; }
    public List<Guid>? DeletedImageIds { get; init; }
    public Guid? SetMainImageId { get; init; }
    public List<UpdateVariantDto> Variants { get; init; } = new();

    // Category Specific Extras
    public Guid? OriginId { get; init; }
    public List<Guid> RoastLevelIds { get; init; } = new();
    public List<Guid> GrindTypeIds { get; init; } = new();
    public List<UpdateFlavourNoteDto> FlavourNotes { get; init; } = new();
}

public record UpdateProductTranslationDto(
    string LanguageCode, 
    string Name, 
    string Description
);

public record UpdateVariantDto(
    Guid? Id,
    string Sku,
    int StockQuantity,
    List<UpdateVariantOptionDto> Options,
    List<UpdateVariantPriceDto>? Prices = null
);

public record UpdateVariantOptionDto(
    Guid ProductAttributeValueId
);

public record UpdateVariantPriceDto(
    string CurrencyCode,
    decimal Price
);

public record UpdateFlavourNoteDto(
    Guid? Id,
    string Name,
    int DisplayOrder = 0,
    List<UpdateFlavourNoteTranslationDto>? Translations = null
);

public record UpdateFlavourNoteTranslationDto(
    string LanguageCode,
    string Name
);
