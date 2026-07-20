using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler(IProductService productService)
    : IRequestHandler<UpdateProductCommand, bool>
{
    public Task<bool> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
        => productService.UpdateAsync(command);
}
