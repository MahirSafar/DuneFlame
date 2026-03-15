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

    /// <summary>
    /// Master email template wrapper - wraps any HTML content in a beautiful, responsive design.
    /// Uses Dune & Flame branding with dark brown (#2b1b13) and amber/gold (#f59e0b) accents.
    /// </summary>
    private string ApplyEmailTemplate(string title, string htmlContent)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
</head>
<body style=""margin: 0; padding: 0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f5f5f5;"">
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #f5f5f5; padding: 40px 20px;"">
        <tr>
            <td align=""center"">
                <!-- Main Container -->
                <table width=""600"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.08); overflow: hidden; max-width: 100%;"">

                    <!-- Header -->
                    <tr>
                        <td style=""background: linear-gradient(135deg, #2b1b13 0%, #3d2a1f 100%); padding: 35px 40px; text-align: center;"">
                            <h1 style=""color: #f59e0b; margin: 0; font-size: 32px; font-weight: 700; letter-spacing: 4px; text-transform: uppercase; text-shadow: 2px 2px 4px rgba(0,0,0,0.3);"">
                                Dune & Flame
                            </h1>
                            <p style=""color: #d4a574; margin: 8px 0 0 0; font-size: 13px; letter-spacing: 2px; text-transform: uppercase;"">Premium Coffee Experience</p>
                        </td>
                    </tr>

                    <!-- Title Bar -->
                    <tr>
                        <td style=""background-color: #f59e0b; padding: 20px 40px; text-align: center;"">
                            <h2 style=""color: #ffffff; margin: 0; font-size: 22px; font-weight: 600; letter-spacing: 1px;"">{System.Net.WebUtility.HtmlEncode(title)}</h2>
                        </td>
                    </tr>

                    <!-- Content Area -->
                    <tr>
                        <td style=""padding: 45px 40px; color: #333333; font-size: 16px; line-height: 1.7;"">
                            {htmlContent}
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style=""background-color: #fafafa; padding: 30px 40px; text-align: center; border-top: 1px solid #e5e5e5;"">
                            <p style=""margin: 0 0 10px 0; color: #666666; font-size: 13px; line-height: 1.6;"">
                                © {DateTime.Now.Year} Dune & Flame. All rights reserved.
                            </p>
                            <p style=""margin: 0; color: #999999; font-size: 12px;"">
                                Premium coffee roasted with passion and expertise.
                            </p>
                            <div style=""margin-top: 15px; padding-top: 15px; border-top: 1px solid #e5e5e5;"">
                                <p style=""margin: 0; color: #aaaaaa; font-size: 11px;"">
                                    If you didn't request this email, you can safely ignore it.
                                </p>
                            </div>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    /// <summary>
    /// Helper method to create a styled button for use within email templates.
    /// </summary>
    private string CreateButton(string buttonText, string buttonUrl)
    {
        return $@"
<div style=""text-align: center; margin: 30px 0;"">
    <a href=""{buttonUrl}"" style=""display: inline-block; background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); color: #ffffff; padding: 15px 40px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 12px rgba(245, 158, 11, 0.3); transition: all 0.3s ease;"">
        {System.Net.WebUtility.HtmlEncode(buttonText)}
    </a>
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
            var verificationLink = $"{_clientUrls.ApiBaseUrl}/api/v1/auth/verify-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

            var content = $@"
<p style=""margin: 0 0 20px 0; text-align: center;"">Welcome to <strong>Dune & Flame</strong>! We're excited to have you join our community of coffee lovers.</p>
<p style=""margin: 0 0 30px 0; text-align: center;"">Please verify your email address to activate your account and start exploring our premium coffee collection.</p>
{CreateButton("Verify Email Address", verificationLink)}
<p style=""margin: 30px 0 0 0; text-align: center; color: #666666; font-size: 14px;"">
    This verification link will expire in 24 hours for security reasons.
</p>";

            var body = ApplyEmailTemplate("Email Verification", content);
            await SendGenericEmailAsync(to, "Verify Your Email - Dune & Flame", body);
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
            var frontendResetUrl = $"{_clientUrls.BaseUrl}/auth/reset-password?userId={Uri.EscapeDataString(userId)}&email={Uri.EscapeDataString(to)}&token={Uri.EscapeDataString(token)}";

            var content = $@"
<p style=""margin: 0 0 20px 0; text-align: center;"">We received a request to reset your password. No worries - it happens to everyone!</p>
<p style=""margin: 0 0 30px 0; text-align: center;"">Click the button below to create a new password for your account.</p>
{CreateButton("Reset Password", frontendResetUrl)}
<p style=""margin: 30px 0 0 0; text-align: center; color: #666666; font-size: 14px;"">
    This password reset link will expire in 1 hour for security reasons.<br/>
    If you didn't request a password reset, please ignore this email.
</p>";

            var body = ApplyEmailTemplate("Password Reset Request", content);
            await SendGenericEmailAsync(to, "Reset Your Password - Dune & Flame", body);
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

            var content = $@"
<p style=""margin: 0 0 20px 0; text-align: center;"">Thank you for subscribing to the <strong>Dune & Flame</strong> newsletter!</p>
<p style=""margin: 0 0 30px 0; text-align: center;"">Stay updated with our latest premium coffee collections, exclusive offers, and brewing tips.</p>
{CreateButton("Confirm Subscription", link)}
<p style=""margin: 30px 0 0 0; text-align: center; color: #666666; font-size: 14px;"">
    You can unsubscribe at any time by clicking the link in our emails.
</p>";

            var body = ApplyEmailTemplate("Newsletter Subscription", content);
            await SendGenericEmailAsync(to, "Confirm Your Subscription - Dune & Flame", body);
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

            var sanitizedName = System.Net.WebUtility.HtmlEncode(message.Name ?? "Unknown");
            var sanitizedEmail = System.Net.WebUtility.HtmlEncode(message.Email ?? "No Email");
            var sanitizedSubject = System.Net.WebUtility.HtmlEncode(message.Subject ?? "No Subject");
            var sanitizedMessage = System.Net.WebUtility.HtmlEncode(message.Message ?? "No Message");

            var content = $@"
<div style=""background-color: #fef3c7; border-left: 4px solid #f59e0b; padding: 20px; margin-bottom: 25px; border-radius: 4px;"">
    <p style=""margin: 0; color: #92400e; font-weight: 600;"">⚠️ New Customer Support Request</p>
</div>

<table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 20px;"">
    <tr>
        <td style=""padding: 12px 0; border-bottom: 1px solid #e5e5e5;"">
            <strong style=""color: #2b1b13;"">Customer Name:</strong><br/>
            <span style=""color: #555555;"">{sanitizedName}</span>
        </td>
    </tr>
    <tr>
        <td style=""padding: 12px 0; border-bottom: 1px solid #e5e5e5;"">
            <strong style=""color: #2b1b13;"">Email Address:</strong><br/>
            <a href=""mailto:{sanitizedEmail}"" style=""color: #f59e0b; text-decoration: none;"">{sanitizedEmail}</a>
        </td>
    </tr>
    <tr>
        <td style=""padding: 12px 0; border-bottom: 1px solid #e5e5e5;"">
            <strong style=""color: #2b1b13;"">Subject:</strong><br/>
            <span style=""color: #555555;"">{sanitizedSubject}</span>
        </td>
    </tr>
    <tr>
        <td style=""padding: 20px 0;"">
            <strong style=""color: #2b1b13;"">Message:</strong><br/>
            <div style=""margin-top: 10px; padding: 15px; background-color: #f9fafb; border-radius: 6px; color: #374151; line-height: 1.6;"">
                {sanitizedMessage.Replace("\n", "<br/>")}
            </div>
        </td>
    </tr>
</table>

<p style=""margin: 0; text-align: center; color: #999999; font-size: 13px;"">
    Received: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
</p>";

            var body = ApplyEmailTemplate("New Contact Form Submission", content);
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
                var baseLanguageCode = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";
                var orderTrackingUrl = $"{_clientUrls.BaseUrl}/{baseLanguageCode}/dashboard/orders/{orderId}";
                var (subject, title, message, buttonText) = GetLocalizedOrderConfirmation(languageCode, orderId, amount);

                var content = $@"
<div style=""text-align: center; margin-bottom: 30px;"">
    <div style=""display: inline-block; background-color: #dcfce7; border: 2px solid #16a34a; border-radius: 50%; width: 60px; height: 60px; line-height: 60px; font-size: 32px;"">
        ✓
    </div>
</div>
<p style=""margin: 0 0 25px 0; text-align: center; font-size: 17px;"">{message}</p>
<div style=""background-color: #fef9e7; border: 1px solid #f59e0b; border-radius: 8px; padding: 20px; margin: 25px 0;"">
    <table width=""100%"" cellpadding=""5"" cellspacing=""0"">
        <tr>
            <td style=""color: #2b1b13; font-weight: 600;"">Order ID:</td>
            <td style=""color: #555555; text-align: right; font-family: monospace;"">{orderId:N}</td>
        </tr>
        <tr>
            <td style=""color: #2b1b13; font-weight: 600;"">Total Amount:</td>
            <td style=""color: #555555; text-align: right; font-weight: 700; font-size: 18px;"">${amount:F2}</td>
        </tr>
    </table>
</div>
{CreateButton(buttonText, orderTrackingUrl)}";

                var body = ApplyEmailTemplate(title, content);
                await SendGenericEmailAsync(to, subject, body);
                _logger?.LogInformation(
                    "SendOrderPaidAsync: Order paid email sent to {To} for OrderId {OrderId} in language {Language}",
                    to, orderId, baseLanguageCode);
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
                var orderTrackingUrl = $"{_clientUrls.BaseUrl}/en/dashboard/orders/{orderId}";

                var trackingInfo = string.IsNullOrWhiteSpace(trackingNumber)
                    ? "<p style='margin: 20px 0; color: #666666;'>Your tracking number will be available shortly.</p>"
                    : $@"<div style=""background-color: #fef9e7; border: 1px solid #f59e0b; border-radius: 8px; padding: 20px; margin: 25px 0; text-align: center;"">
                            <p style=""margin: 0 0 10px 0; color: #2b1b13; font-weight: 600;"">Tracking Number:</p>
                            <p style=""margin: 0; font-family: monospace; font-size: 18px; color: #f59e0b; font-weight: 700;"">{System.Net.WebUtility.HtmlEncode(trackingNumber)}</p>
                         </div>";

                var content = $@"
<div style=""text-align: center; margin-bottom: 30px;"">
    <div style=""display: inline-block; background-color: #dbeafe; border: 2px solid #3b82f6; border-radius: 50%; width: 60px; height: 60px; line-height: 60px; font-size: 32px;"">
        📦
    </div>
</div>
<p style=""margin: 0 0 20px 0; text-align: center; font-size: 17px;"">
    Great news! Your order <strong>#{orderId:N}</strong> is on its way to you.
</p>
{trackingInfo}
<p style=""margin: 20px 0; text-align: center; color: #666666;"">
    You can track your shipment status anytime through your dashboard.
</p>
{CreateButton("Track Your Order", orderTrackingUrl)}";

                var body = ApplyEmailTemplate("Your Order Has Shipped", content);
                await SendGenericEmailAsync(to, $"Order #{orderId:N} Shipped - Dune & Flame", body);
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

                var content = $@"
<div style=""text-align: center; margin-bottom: 30px;"">
    <div style=""display: inline-block; background-color: #dcfce7; border: 2px solid #16a34a; border-radius: 50%; width: 60px; height: 60px; line-height: 60px; font-size: 32px;"">
        🎉
    </div>
</div>
<p style=""margin: 0 0 20px 0; text-align: center; font-size: 17px;"">
    Your order <strong>#{orderId:N}</strong> has been successfully delivered!
</p>
<p style=""margin: 0 0 25px 0; text-align: center; color: #666666;"">
    We hope you enjoy your premium coffee. If you have any questions or need assistance, our support team is here to help.
</p>
<div style=""background-color: #fef9e7; border-radius: 8px; padding: 20px; margin: 25px 0; text-align: center;"">
    <p style=""margin: 0 0 10px 0; color: #2b1b13; font-size: 15px;"">💡 <strong>Brewing Tip:</strong></p>
    <p style=""margin: 0; color: #555555; font-size: 14px; line-height: 1.6;"">
        For the best flavor, store your coffee in an airtight container away from light and heat. Grind just before brewing for maximum freshness!
    </p>
</div>
{CreateButton("Explore More Coffee", shopMoreUrl)}";

                var body = ApplyEmailTemplate("Order Delivered Successfully", content);
                await SendGenericEmailAsync(to, $"Order #{orderId:N} Delivered - Dune & Flame", body);
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
                var productsUrl = $"{_clientUrls.BaseUrl}/en/products";

                var content = $@"
<div style=""text-align: center; margin-bottom: 30px;"">
    <div style=""display: inline-block; background-color: #fee2e2; border: 2px solid #ef4444; border-radius: 50%; width: 60px; height: 60px; line-height: 60px; font-size: 32px;"">
        ❌
    </div>
</div>
<p style=""margin: 0 0 20px 0; text-align: center; font-size: 17px;"">
    Your order <strong>#{orderId:N}</strong> has been cancelled.
</p>
<div style=""background-color: #fef9e7; border: 1px solid #f59e0b; border-radius: 8px; padding: 20px; margin: 25px 0;"">
    <table width=""100%"" cellpadding=""5"" cellspacing=""0"">
        <tr>
            <td style=""color: #2b1b13; font-weight: 600;"">Refund Amount:</td>
            <td style=""color: #555555; text-align: right; font-weight: 700; font-size: 18px;"">${refundAmount:F2}</td>
        </tr>
        <tr>
            <td style=""color: #2b1b13; font-weight: 600;"">Processing Time:</td>
            <td style=""color: #555555; text-align: right;"">3-5 business days</td>
        </tr>
        <tr>
            <td colspan=""2"" style=""padding-top: 15px; color: #666666; font-size: 14px;"">
                The refund will be processed to your original payment method.
            </td>
        </tr>
    </table>
</div>
<p style=""margin: 20px 0; text-align: center; color: #666666;"">
    If you have any questions about this cancellation or need assistance, please contact our support team.
</p>
{CreateButton("Browse Our Collection", productsUrl)}";

                var body = ApplyEmailTemplate("Order Cancelled", content);
                await SendGenericEmailAsync(to, $"Order #{orderId:N} Cancelled - Dune & Flame", body);
                _logger?.LogInformation("SendOrderCancelledAsync: Order cancelled email sent to {To} for OrderId {OrderId}", to, orderId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendOrderCancelledAsync: Failed to send order cancelled email to {To} for OrderId {OrderId}", to, orderId);
                throw;
            }
        }
    }