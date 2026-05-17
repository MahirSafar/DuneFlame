using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Admin;

public record ContactMessageResponse(
    Guid Id,
    string Name,
    string Email,
    string? Phone,
    string? Subject,
    InquiryType InquiryType,
    string Message,
    string? IpAddress,
    bool IsRead,
    string? AdminReply,
    DateTime CreatedAt
);
