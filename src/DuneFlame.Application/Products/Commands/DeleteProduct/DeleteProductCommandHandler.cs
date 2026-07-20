using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Products.Commands.DeleteProduct;

public class DeleteProductCommandHandler(IProductService productService)
    : IRequestHandler<DeleteProductCommand, bool>
{
    public async Task<bool> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        await productService.DeleteAsync(command.Id);
        return true;
    }
}
