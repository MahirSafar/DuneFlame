using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Products.Commands.RestoreProduct;

public class RestoreProductCommandHandler(IProductService productService)
    : IRequestHandler<RestoreProductCommand, bool>
{
    public async Task<bool> Handle(RestoreProductCommand command, CancellationToken cancellationToken)
    {
        await productService.RestoreAsync(command.Id);
        return true;
    }
}
