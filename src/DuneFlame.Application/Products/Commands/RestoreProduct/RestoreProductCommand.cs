using MediatR;

namespace DuneFlame.Application.Products.Commands.RestoreProduct;

public record RestoreProductCommand(Guid Id) : IRequest<bool>;
