using DuneFlame.Application.DTOs.Product;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.Products.Commands.CreateProduct;

/// <summary>
/// Command to create a new product. Wraps CreateProductRequest and carries form files.
/// </summary>
public record CreateProductCommand : IRequest<Guid>
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid CategoryId { get; init; }
    public Guid? BrandId { get; init; }
    public Guid? OriginId { get; init; }
    public List<Guid>? RoastLevelIds { get; init; }
    public List<Guid>? GrindTypeIds { get; init; }
    public List<FlavourNoteCreateDto>? FlavourNotes { get; init; }
    public List<VariantCreateDto> Variants { get; init; } = [];
    public List<IFormFile>? Images { get; init; }
    public List<ProductTranslationCreateDto>? Translations { get; init; }

    /// <summary>Accepts JSON string from [FromForm] for robust multipart binding.</summary>
    public string? SpecificationsJson { get; init; }
    /// <summary>Not bound from form; populated by the handler after deserializing SpecificationsJson.</summary>
    public Dictionary<string, string>? Specifications { get; set; }
}
