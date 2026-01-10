namespace DuneFlame.Application.DTOs.User;

public record ContactMessageRequest(
    string Name,
    string Email,
    string Subject,
    string Message
);