using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.User;

public record ContactMessageRequest(
    string Name,
    string Email,
    string? Phone,
    string? Subject,
    InquiryType InquiryType,
    string Message
);