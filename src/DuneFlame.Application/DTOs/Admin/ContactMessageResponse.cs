namespace DuneFlame.Application.DTOs.Admin;

public record ContactMessageResponse(
    Guid Id,
    string Name,
    string Email,
    string Subject,
    string Message,
    string? IpAddress,
    bool IsRead,
    string? AdminReply,
    DateTime CreatedAt
);
