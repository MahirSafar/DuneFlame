using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DuneFlame.Application.Products.Commands.CreateProduct;

public class CreateProductCommandHandler(IProductService productService, ILogger<CreateProductCommandHandler> logger)
    : IRequestHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        Dictionary<string, string>? specifications = null;
        if (!string.IsNullOrWhiteSpace(command.SpecificationsJson))
        {
            try
            {
                specifications = JsonSerializer.Deserialize<Dictionary<string, string>>(command.SpecificationsJson);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deserialize SpecificationsJson: {Json}", command.SpecificationsJson);
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

        logger.LogInformation("Handling CreateProductCommand for '{Name}'", command.Name);
        var productId = await productService.CreateAsync(request);
        logger.LogInformation("Product created with ID {ProductId}", productId);
        return productId;
    }
}
