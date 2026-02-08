using System.Net;
using System.Net.Mail;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

public class SmtpEmailService(
    IOptions<EmailSettings> settings,
    IOptions<ClientUrls> clientUrls,
    ILogger<SmtpEmailService>? logger = null) : IEmailService
{
    private readonly EmailSettings _settings = settings.Value;
    private readonly ClientUrls _clientUrls = clientUrls.Value;
    private readonly ILogger<SmtpEmailService>? _logger = logger;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 1000;

    // --- Private Helper for HTML Branding ---
    private string GetHtmlTemplate(string title, string message, string buttonText, string buttonUrl)
    {
        return $@"
        <div style='font-family: ""Segoe UI"", Tahoma, Geneva, Verdana, sans-serif; background-color: #f9f9f9; padding: 40px 0;'>
            <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.05);'>
                <div style='background-color: #e67e22; padding: 30px; text-align: center;'>
                    <h1 style='color: #ffffff; margin: 0; font-size: 24px; text-transform: uppercase; letter-spacing: 3px;'>Dune Flame</h1>
                </div>
                <div style='padding: 40px; text-align: center; color: #333333;'>
                    <h2 style='color: #2c3e50; margin-bottom: 20px;'>{title}</h2>
                    <p style='font-size: 16px; line-height: 1.8; color: #555555;'>{message}</p>
                    <div style='margin-top: 35px;'>
                        <a href='{buttonUrl}' style='background-color: #e67e22; color: #ffffff; padding: 14px 35px; text-decoration: none; border-radius: 6px; font-weight: bold; font-size: 16px; display: inline-block;'>{buttonText}</a>
                    </div>
                </div>
                <div style='background-color: #f1f1f1; padding: 20px; text-align: center; font-size: 12px; color: #888888;'>
                    <p>© {DateTime.Now.Year} DuneFlame. All rights reserved.</p>
                    <p>If you didn't request this email, please ignore it.</p>
                </div>
            </div>
        </div>";
    }

    /// <summary>
    /// Send generic email with retry logic and comprehensive error handling.
    /// Validates inputs, handles SMTP exceptions, and logs all operations.
    /// </summary>
    public async Task SendGenericEmailAsync(string to, string subject, string body)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger?.LogError("SendGenericEmailAsync: Recipient email is null or empty");
            throw new ArgumentException("Recipient email cannot be null or empty", nameof(to));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            _logger?.LogWarning("SendGenericEmailAsync: Subject is empty for recipient {To}", to);
            subject = "(No Subject)";
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            _logger?.LogError("SendGenericEmailAsync: Body is null or empty for recipient {To}", to);
            throw new ArgumentException("Email body cannot be null or empty", nameof(body));
        }

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                _logger?.LogInformation("SendGenericEmailAsync: Attempt {Attempt}/{MaxRetries} to send email to {To}", 
                    attempt, MaxRetries, to);

                using var message = new MailMessage(_settings.FromEmail, to)
                {
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                using var client = new SmtpClient(_settings.Host, _settings.Port)
                {
                    Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                    EnableSsl = true,
                    Timeout = 10000 // 10 second timeout
                };

                await client.SendMailAsync(message);

                _logger?.LogInformation("SendGenericEmailAsync: Email sent successfully to {To}", to);
                return; // Success - exit retry loop
            }
            catch (SmtpException ex)
            {
                lastException = ex;
                _logger?.LogWarning(ex, "SendGenericEmailAsync: SMTP error on attempt {Attempt}/{MaxRetries} for recipient {To}. " +
                    "StatusCode: {StatusCode}, Message: {Message}",
                    attempt, MaxRetries, to, ex.StatusCode, ex.Message);

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelayMs * attempt); // Exponential backoff
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger?.LogError(ex, "SendGenericEmailAsync: Unexpected error on attempt {Attempt}/{MaxRetries} for recipient {To}",
                    attempt, MaxRetries, to);

                if (attempt < MaxRetries && IsRetryableException(ex))
                {
                    await Task.Delay(RetryDelayMs * attempt);
                }
                else
                {
                    throw;
                }
            }
        }

        // All retries exhausted
        _logger?.LogError(lastException, "SendGenericEmailAsync: All {MaxRetries} retry attempts failed for recipient {To}",
            MaxRetries, to);
        throw new InvalidOperationException($"Failed to send email to {to} after {MaxRetries} attempts", lastException);
    }

    public async Task SendVerificationEmailAsync(string to, string userId, string token)
    {
        try
        {
            // Link points to API with verification endpoint, then redirects to Frontend
            var verificationLink = $"{_clientUrls.ApiBaseUrl}/api/v1/auth/verify-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

            var body = GetHtmlTemplate(
                "Verify Your Account",
                "Welcome to DuneFlame! We're excited to have you. Please click the button below to confirm your email address and activate your account.",
                "Confirm Email",
                verificationLink);

            await SendGenericEmailAsync(to, "Confirm Your Email - DuneFlame", body);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendVerificationEmailAsync: Failed to send verification email to {To}", to);
            throw;
        }
    }

    public async Task SendPasswordResetEmailAsync(string to, string userId, string token)
    {
        try
        {
            // Password reset links go directly to the Frontend
            var frontendResetUrl = $"{_clientUrls.BaseUrl}/auth/reset-password?userId={Uri.EscapeDataString(userId)}&email={Uri.EscapeDataString(to)}&token={Uri.EscapeDataString(token)}";

            var body = GetHtmlTemplate(
                "Reset Your Password",
                "Forgot your password? No worries. Click the button below to set a new password for your account.",
                "Reset Password",
                frontendResetUrl);

            await SendGenericEmailAsync(to, "Reset Password Request - DuneFlame", body);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendPasswordResetEmailAsync: Failed to send password reset email to {To}", to);
            throw;
        }
    }

    public async Task SendNewsletterVerificationAsync(string to, string token)
    {
        try
        {
            var link = $"{_clientUrls.ApiBaseUrl}/api/v1/newsletter/verify?token={Uri.EscapeDataString(token)}";

            var body = GetHtmlTemplate(
                "Confirm Subscription",
                "Stay updated with our latest collections and offers! Confirm your subscription to our newsletter below.",
                "Subscribe Now",
                link);

            await SendGenericEmailAsync(to, "Newsletter Subscription - DuneFlame", body);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendNewsletterVerificationAsync: Failed to send newsletter verification email to {To}", to);
            throw;
        }
    }

    public async Task SendAdminContactAlertAsync(ContactMessage message)
    {
        try
        {
            if (message == null)
            {
                _logger?.LogError("SendAdminContactAlertAsync: Contact message is null");
                throw new ArgumentNullException(nameof(message));
            }

            // Sanitize message content to prevent HTML injection
            var sanitizedName = System.Net.WebUtility.HtmlEncode(message.Name ?? "Unknown");
            var sanitizedEmail = System.Net.WebUtility.HtmlEncode(message.Email ?? "No Email");
            var sanitizedSubject = System.Net.WebUtility.HtmlEncode(message.Subject ?? "No Subject");
            var sanitizedMessage = System.Net.WebUtility.HtmlEncode(message.Message ?? "No Message");

            var body = $@"
            <div style='font-family: sans-serif; border: 1px solid #ddd; padding: 25px; border-radius: 8px;'>
                <h2 style='color: #e67e22;'>New Support Request</h2>
                <p><strong>Name:</strong> {sanitizedName}</p>
                <p><strong>Email:</strong> {sanitizedEmail}</p>
                <p><strong>Subject:</strong> {sanitizedSubject}</p>
                <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                <p><strong>Message:</strong><br/>{sanitizedMessage}</p>
                <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                <p style='font-size: 12px; color: #888;'>Received: {DateTime.UtcNow:O}</p>
            </div>";

            await SendGenericEmailAsync(_settings.FromEmail, $"[Contact Form] {sanitizedSubject}", body);

            _logger?.LogInformation("SendAdminContactAlertAsync: Admin alert sent for contact from {Email}", sanitizedEmail);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendAdminContactAlertAsync: Failed to send admin contact alert");
            throw;
        }
    }

        /// <summary>
        /// Determine if an exception is retryable (transient vs permanent).
        /// </summary>
        private static bool IsRetryableException(Exception ex)
        {
            if (ex is TimeoutException or IOException)
                return true;

            if (ex is SmtpException smtpEx && smtpEx.StatusCode == SmtpStatusCode.ServiceNotAvailable)
                return true;

            return false;
        }

        /// <summary>
        /// Send order paid confirmation email with order details in the specified language.
        /// Link includes locale parameter for i18n support.
        /// </summary>
        public async Task SendOrderPaidAsync(string to, Guid orderId, decimal amount, string languageCode = "en")
        {
            try
            {
                // Extract base language code (e.g., "en" from "en-US")
                var baseLanguageCode = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";

                // Build localized order tracking URL
                var orderTrackingUrl = $"{_clientUrls.BaseUrl}/{baseLanguageCode}/dashboard/orders/{orderId}";

                // Get localized subject and message based on language code
                var (subject, title, message, buttonText) = GetLocalizedOrderConfirmation(languageCode, orderId, amount);

                var body = GetHtmlTemplate(
                    title,
                    message,
                    buttonText,
                    orderTrackingUrl);

                await SendGenericEmailAsync(to, subject, body);
                _logger?.LogInformation(
                    "SendOrderPaidAsync: Order paid email sent to {To} for OrderId {OrderId} in language {Language} with URL {TrackingUrl}",
                    to, orderId, baseLanguageCode, orderTrackingUrl);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendOrderPaidAsync: Failed to send order paid email to {To} for OrderId {OrderId}", to, orderId);
                throw;
            }
        }

        /// <summary>
        /// Get localized order confirmation strings based on language code.
        /// Supports: en (English), ar (Arabic)
        /// Handles language codes in various formats (e.g., "en-US" -> "en", "ar-SA" -> "ar")
        /// </summary>
        private (string Subject, string Title, string Message, string ButtonText) GetLocalizedOrderConfirmation(string languageCode, Guid orderId, decimal amount)
        {
            // Extract first 2 characters to handle formats like "en-US", "ar-SA"
            var code = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode.Length)) ?? "en";

            return code switch
            {
                "ar" => (
                    $"تم تأكيد الدفع للطلب #{orderId:N} - DuneFlame",
                    "تم تأكيد الدفع",
                    $"شكراً لك على دفعك! تم تأكيد طلبك #{orderId:N} بمبلغ ${amount:F2}. " +
                    "نحن نقوم بمعالجة طلبك وسيتم شحنه قريباً. ستتلقى بريد إلكتروني لتأكيد الشحن قريباً.",
                    "تتبع طلبك"
                ),
                _ => ( // Default to English
                    $"Payment Confirmed for Order #{orderId:N} - DuneFlame",
                    "Payment Confirmed",
                    $"Thank you for your payment! Your order #{orderId:N} totaling ${amount:F2} has been confirmed. " +
                    "We're processing your order and will ship it soon. You'll receive a shipping confirmation email shortly.",
                    "Track Your Order"
                )
            };
        }


        /// <summary>
        /// Send order shipped confirmation email with optional tracking number.
        /// </summary>
        public async Task SendOrderShippedAsync(string to, Guid orderId, string trackingNumber = "")
        {
            try
            {
                var trackingInfo = string.IsNullOrWhiteSpace(trackingNumber)
                    ? "Your tracking number will be available shortly."
                    : $"Your tracking number is: <strong>{System.Net.WebUtility.HtmlEncode(trackingNumber)}</strong>";

                var orderTrackingUrl = $"{_clientUrls.BaseUrl}/en/dashboard/orders/{orderId}";

                var body = GetHtmlTemplate(
                    "Your Order Has Shipped",
                    $"Great news! Your order #{orderId:N} is on its way. {trackingInfo} " +
                    "You can track your shipment through our website.",
                    "Track Shipment",
                    orderTrackingUrl);

                await SendGenericEmailAsync(to, $"Your Order #{orderId:N} Has Shipped - DuneFlame", body);
                _logger?.LogInformation("SendOrderShippedAsync: Order shipped email sent to {To} for OrderId {OrderId}", to, orderId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendOrderShippedAsync: Failed to send order shipped email to {To} for OrderId {OrderId}", to, orderId);
                throw;
            }
        }

        /// <summary>
        /// Send order delivered confirmation email.
        /// </summary>
        public async Task SendOrderDeliveredAsync(string to, Guid orderId)
        {
            try
            {
                var shopMoreUrl = $"{_clientUrls.BaseUrl}/en/products";

                var body = GetHtmlTemplate(
                    "Your Order Has Been Delivered",
                    $"Your order #{orderId:N} has been delivered! We hope you enjoy your purchase. " +
                    "If you have any questions or need assistance, please don't hesitate to contact us.",
                    "Shop More",
                    shopMoreUrl);

                await SendGenericEmailAsync(to, $"Order #{orderId:N} Delivered - DuneFlame", body);
                _logger?.LogInformation("SendOrderDeliveredAsync: Order delivered email sent to {To} for OrderId {OrderId}", to, orderId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendOrderDeliveredAsync: Failed to send order delivered email to {To} for OrderId {OrderId}", to, orderId);
                throw;
            }
        }

        /// <summary>
        /// Send order cancelled confirmation email with refund information.
        /// </summary>
        public async Task SendOrderCancelledAsync(string to, Guid orderId, decimal refundAmount)
        {
            try
            {
                var body = GetHtmlTemplate(
                    "Order Cancelled",
                    $"Your order #{orderId:N} has been cancelled. A refund of ${refundAmount:F2} will be processed to your original payment method " +
                    "within 3-5 business days. If you have any questions, please contact our support team.",
                    "Browse Products",
                    "http://localhost:3000/products");

                await SendGenericEmailAsync(to, $"Order #{orderId:N} Cancelled - DuneFlame", body);
                _logger?.LogInformation("SendOrderCancelledAsync: Order cancelled email sent to {To} for OrderId {OrderId}", to, orderId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendOrderCancelledAsync: Failed to send order cancelled email to {To} for OrderId {OrderId}", to, orderId);
                throw;
            }
        }
    }