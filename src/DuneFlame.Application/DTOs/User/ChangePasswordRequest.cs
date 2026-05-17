namespace DuneFlame.Application.DTOs.User;

public class ChangePasswordRequest
{
    public string? OldPassword { get; set; }
    public string NewPassword { get; set; } = string.Empty;
}
