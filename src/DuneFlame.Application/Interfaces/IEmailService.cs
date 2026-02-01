using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string to, string userId, string token);
    Task SendPasswordResetEmailAsync(string to, string userId, string token);
    Task SendNewsletterVerificationAsync(string to, string token);
    Task SendAdminContactAlertAsync(ContactMessage message);
    Task SendGenericEmailAsync(string to, string subject, string body);
    Task SendOrderPaidAsync(string to, Guid orderId, decimal amount, string languageCode = "en");
    Task SendOrderShippedAsync(string to, Guid orderId, string trackingNumber = "");
    Task SendOrderDeliveredAsync(string to, Guid orderId);
    Task SendOrderCancelledAsync(string to, Guid orderId, decimal refundAmount);
}
