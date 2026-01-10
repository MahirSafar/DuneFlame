namespace DuneFlame.Application.DTOs.User;

public record BulkEmailRequest(
    string Subject,
    string Content
);