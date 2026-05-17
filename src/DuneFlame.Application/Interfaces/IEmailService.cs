using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string to, string userId, string token);
    Task SendPasswordResetEmailAsync(string to, string userId, string token, string languageCode = "en");
    Task SendNewsletterVerificationAsync(string to, string token);
    Task SendAdminContactAlertAsync(ContactMessage message);
    Task SendGenericEmailAsync(string to, string subject, string body);
    Task SendOrderPaidAsync(string to, Guid orderId, decimal amount, string languageCode = "en");
    Task SendOrderShippedAsync(string to, Guid orderId, string trackingNumber = "", string languageCode = "en");
    Task SendOrderDeliveredAsync(string to, Guid orderId, string languageCode = "en");
    Task SendOrderCancelledAsync(string to, Guid orderId, decimal refundAmount, string languageCode = "en");
    Task SendNewsletterCampaignAsync(string to, string subject, string htmlContent);
    Task SendNewsletterSubscribedAsync(string to, string unsubscribeToken);
    Task SendNewsletterAdminReportAsync(string subscriberEmail, int totalSubscribers);
    Task SendWholesaleLeadAlertAsync(WholesaleLead lead);
    Task SendWholesaleLeadConfirmationAsync(string customerEmail, string customerName, string languageCode = "en");
}
