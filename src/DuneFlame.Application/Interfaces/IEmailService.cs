namespace DuneFlame.Application.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string to, string userId, string token);
}
