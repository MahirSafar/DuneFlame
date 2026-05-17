using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Commands.CreateProduct;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Products.Commands.CreateProduct;

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IProductService _productService;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(
        IProductService productService,
        ILogger<CreateProductCommandHandler> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    public async Task<Guid> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        // Deserialize SpecificationsJson if present
        Dictionary<string, string>? specifications = null;
        if (!string.IsNullOrWhiteSpace(command.SpecificationsJson))
        {
            try
            {
                specifications = JsonSerializer.Deserialize<Dictionary<string, string>>(command.SpecificationsJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize SpecificationsJson: {Json}", command.SpecificationsJson);
                throw new ArgumentException("Invalid Specifications JSON format.");
            }
        }

        var request = new CreateProductRequest(
            Name: command.Name,
            Description: command.Description,
            CategoryId: command.CategoryId,
            BrandId: command.BrandId,
            OriginId: command.OriginId,
            RoastLevelIds: command.RoastLevelIds,
            GrindTypeIds: command.GrindTypeIds,
            FlavourNotes: command.FlavourNotes,
            Variants: command.Variants,
            Images: command.Images,
            Translations: command.Translations,
            Specifications: specifications
        );

        _logger.LogInformation("Handling CreateProductCommand for '{Name}'", command.Name);
        var productId = await _productService.CreateAsync(request);
        _logger.LogInformation("Product created with ID {ProductId}", productId);
        return productId;
    }
}
