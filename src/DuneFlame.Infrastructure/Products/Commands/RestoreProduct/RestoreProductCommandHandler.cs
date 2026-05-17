using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Commands.RestoreProduct;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Products.Commands.RestoreProduct;

public class RestoreProductCommandHandler : IRequestHandler<RestoreProductCommand, bool>
{
    private readonly IProductService _productService;
    private readonly ILogger<RestoreProductCommandHandler> _logger;

    public RestoreProductCommandHandler(
        IProductService productService,
        ILogger<RestoreProductCommandHandler> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    public async Task<bool> Handle(RestoreProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling RestoreProductCommand for ID {Id}", command.Id);
        await _productService.RestoreAsync(command.Id);
        _logger.LogInformation("Product {Id} restored", command.Id);
        return true;
    }
}
