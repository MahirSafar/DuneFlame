namespace DuneFlame.Application.DTOs.Product;

public record CreateOriginRequest(
    string Name
);

public record OriginResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
