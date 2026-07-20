using DuneFlame.Application.Products.Commands.UpdateProduct;

namespace DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies;

public interface IProductUpdateStrategy
{
    bool CanHandle(Category category);
    Task ApplyUpdateAsync(Product product, UpdateProductCommand command, AppDbContext context);
}
