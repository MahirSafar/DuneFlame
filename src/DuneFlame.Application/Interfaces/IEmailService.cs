using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string to, string userId, string token);
    Task SendPasswordResetEmailAsync(string to, string userId, string token);
    Task SendNewsletterVerificationAsync(string to, string token);
    Task SendAdminContactAlertAsync(ContactMessage message);
    Task SendGenericEmailAsync(string to, string subject, string body);
}
