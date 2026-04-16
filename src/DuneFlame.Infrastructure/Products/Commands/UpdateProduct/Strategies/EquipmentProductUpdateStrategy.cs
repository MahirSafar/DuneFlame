using DuneFlame.Application.Products.Commands.UpdateProduct;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies;

public class EquipmentProductUpdateStrategy : IProductUpdateStrategy
{
    public bool CanHandle(Category category)
    {
        return category?.Slug?.Contains("equipment", System.StringComparison.OrdinalIgnoreCase) == true ||
               category?.Slug?.Contains("grinder", System.StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task ApplyUpdateAsync(Product product, UpdateProductCommand command, AppDbContext context)
    {
        // Use the deserialized Specifications dictionary from the handler
        var specs = command.Specifications;
        if (specs != null && specs.Any())
        {
            if (product.EquipmentProfile == null)
            {
                product.EquipmentProfile = new ProductEquipmentProfile
                {
                    ProductId = product.Id,
                    Specifications = specs
                };
                context.ProductEquipmentProfiles.Add(product.EquipmentProfile);
            }
            else
            {
                product.EquipmentProfile.Specifications = specs;
                context.ProductEquipmentProfiles.Update(product.EquipmentProfile);
            }
        }

        await Task.CompletedTask;
    }
}
