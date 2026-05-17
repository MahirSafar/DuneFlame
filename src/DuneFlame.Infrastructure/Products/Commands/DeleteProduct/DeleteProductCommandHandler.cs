using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Commands.DeleteProduct;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Products.Commands.DeleteProduct;

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, bool>
{
    private readonly IProductService _productService;
    private readonly ILogger<DeleteProductCommandHandler> _logger;

    public DeleteProductCommandHandler(
        IProductService productService,
        ILogger<DeleteProductCommandHandler> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    public async Task<bool> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling DeleteProductCommand for ID {Id}", command.Id);
        await _productService.DeleteAsync(command.Id);
        _logger.LogInformation("Product {Id} deleted", command.Id);
        return true;
    }
}
