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

    // Cached master layout — loaded once from the embedded resource on first use
    private static string? _templateCache;
    private static readonly object _templateLock = new();

    private static string LoadTemplate()
    {
        if (_templateCache is not null)
            return _templateCache;

        lock (_templateLock)
        {
            if (_templateCache is not null)
                return _templateCache;

            var assembly = typeof(SmtpEmailService).Assembly;
            const string resourceName = "DuneFlame.Infrastructure.Templates.email-template.html";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded email template '{resourceName}' not found.");
            using var reader = new StreamReader(stream);
            _templateCache = reader.ReadToEnd();
        }

        return _templateCache;
    }

    // Per-email content templates — each cached independently after first load
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _contentCache = new();

    private static string LoadContentTemplate(string name)
    {
        return _contentCache.GetOrAdd(name, static n =>
        {
            var assembly = typeof(SmtpEmailService).Assembly;
            var resourceName = $"DuneFlame.Infrastructure.Templates.{n}";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded email content template '{resourceName}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }

    /// <summary>
    /// Fills the shared HTML email template with a title and body content.
    /// Replaces {{Title}}, {{Content}}, and {{Year}} placeholders.
    /// </summary>
    private static string ApplyEmailTemplate(string title, string htmlContent)
    {
        return LoadTemplate()
            .Replace("{{Title}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{Content}}", htmlContent)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());
    }

    /// <summary>
    /// Helper that renders a branded CTA button for use inside email content blocks.
    /// </summary>
    private static string CreateButton(string buttonText, string buttonUrl)
    {
        return $@"
<div style=""text-align:center; margin:30px 0;"">
    <a href=""{buttonUrl}"" style=""display:inline-block; background:linear-gradient(135deg,#CC3323,#A3291C); color:#FFFFFF; padding:14px 28px; border-radius:30px; text-decoration:none; font-weight:bold; letter-spacing:1px; font-family:'Helvetica',Arial,sans-serif;"">
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

            var content = LoadContentTemplate("email-verification.html")
                .Replace("{{VerificationLink}}", verificationLink);

            var body = ApplyEmailTemplate("Email Verification", content);
            await SendGenericEmailAsync(to, "Verify Your Email - Dune & Flame", body);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendVerificationEmailAsync: Failed to send verification email to {To}", to);
            throw;
        }
    }

    public async Task SendPasswordResetEmailAsync(string to, string userId, string token, string languageCode = "en")
    {
        try
        {
            var code = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";
            var frontendResetUrl = $"{_clientUrls.BaseUrl}/auth/reset-password?userId={Uri.EscapeDataString(userId)}&email={Uri.EscapeDataString(to)}&token={Uri.EscapeDataString(token)}";

            var (subject, title, buttonText) = code switch
            {
                "ar" => (
                    "إعادة تعيين كلمة المرور - Dune & Flame",
                    "طلب إعادة تعيين كلمة المرور",
                    "إعادة تعيين كلمة المرور"
                ),
                _ => (
                    "Reset Your Password - Dune & Flame",
                    "Password Reset Request",
                    "Reset Password"
                )
            };

            var content = LoadContentTemplate("email-password-reset.html")
                .Replace("{{ResetLink}}", frontendResetUrl)
                .Replace("{{ButtonText}}", buttonText);

            var body = ApplyEmailTemplate(title, content);
            await SendGenericEmailAsync(to, subject, body);
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

            var content = LoadContentTemplate("email-newsletter-verification.html")
                .Replace("{{ConfirmLink}}", link);

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

            var sanitizedName    = System.Net.WebUtility.HtmlEncode(message.Name    ?? "Unknown");
            var sanitizedEmail   = System.Net.WebUtility.HtmlEncode(message.Email   ?? "No Email");
            var sanitizedSubject = System.Net.WebUtility.HtmlEncode(message.Subject ?? "No Subject");
            var sanitizedMessage = System.Net.WebUtility.HtmlEncode(message.Message ?? "No Message")
                                       .Replace("\n", "<br/>");

            var content = LoadContentTemplate("email-contact-alert.html")
                .Replace("{{Name}}",       sanitizedName)
                .Replace("{{Email}}",      sanitizedEmail)
                .Replace("{{Subject}}",    sanitizedSubject)
                .Replace("{{Message}}",    sanitizedMessage)
                .Replace("{{ReceivedAt}}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

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

                var trackButton = CreateButton(buttonText, orderTrackingUrl);

                var content = LoadContentTemplate("email-order-paid.html")
                    .Replace("{{OrderMessage}}", message)
                    .Replace("{{OrderId}}",      orderId.ToString("N"))
                    .Replace("{{Amount}}",       $"${amount:F2}")
                    .Replace("{{TrackButton}}",  trackButton);

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
        public async Task SendOrderShippedAsync(string to, Guid orderId, string trackingNumber = "", string languageCode = "en")
        {
            try
            {
                var code = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";
                var orderTrackingUrl = $"{_clientUrls.BaseUrl}/{code}/dashboard/orders/{orderId}";

                var (subject, title, trackingLabel, trackingPlaceholder, buttonText) = code switch
                {
                    "ar" => (
                        $"تم شحن طلبك #{orderId:N} - Dune & Flame",
                        "تم شحن طلبك",
                        "رقم التتبع:",
                        "<p style='margin: 20px 0; color: #666666;'>سيكون رقم التتبع متاحاً قريباً.</p>",
                        "تتبع طلبك"
                    ),
                    _ => (
                        $"Order #{orderId:N} Shipped - Dune & Flame",
                        "Your Order Has Shipped",
                        "Tracking Number:",
                        "<p style='margin: 20px 0; color: #666666;'>Your tracking number will be available shortly.</p>",
                        "Track Your Order"
                    )
                };

                var trackingInfo = string.IsNullOrWhiteSpace(trackingNumber)
                    ? trackingPlaceholder
                    : $@"<div style=""margin: 25px 0;"">
                            <table width=""100%"" cellpadding=""10"" cellspacing=""0"" style=""border-collapse: collapse;"">
                                <tr>
                                    <td style=""color: #2B1B13; font-weight: bold; border-bottom: 1px solid #FBEDDC; text-align: center;"">{trackingLabel}<br/>
                                    <span style=""font-size: 18px; color: #CC3323; font-weight: bold;"">{System.Net.WebUtility.HtmlEncode(trackingNumber)}</span></td>
                                </tr>
                            </table>
                         </div>";

                var content = LoadContentTemplate("email-order-shipped.html")
                    .Replace("{{OrderId}}",      orderId.ToString("N"))
                    .Replace("{{TrackingInfo}}", trackingInfo)
                    .Replace("{{TrackButton}}",  CreateButton(buttonText, orderTrackingUrl));

                var body = ApplyEmailTemplate(title, content);
                await SendGenericEmailAsync(to, subject, body);
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
        public async Task SendOrderDeliveredAsync(string to, Guid orderId, string languageCode = "en")
        {
            try
            {
                var code = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";
                var shopMoreUrl = $"{_clientUrls.BaseUrl}/{code}/products";

                var (subject, title, buttonText) = code switch
                {
                    "ar" => (
                        $"تم تسليم طلبك #{orderId:N} - Dune & Flame",
                        "تم تسليم طلبك بنجاح",
                        "تسوق المزيد من القهوة"
                    ),
                    _ => (
                        $"Order #{orderId:N} Delivered - Dune & Flame",
                        "Order Delivered Successfully",
                        "Explore More Coffee"
                    )
                };

                var content = LoadContentTemplate("email-order-delivered.html")
                    .Replace("{{OrderId}}",    orderId.ToString("N"))
                    .Replace("{{ShopButton}}", CreateButton(buttonText, shopMoreUrl));

                var body = ApplyEmailTemplate(title, content);
                await SendGenericEmailAsync(to, subject, body);
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
        public async Task SendOrderCancelledAsync(string to, Guid orderId, decimal refundAmount, string languageCode = "en")
        {
            try
            {
                var code = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";
                var productsUrl = $"{_clientUrls.BaseUrl}/{code}/products";

                var (subject, title, buttonText) = code switch
                {
                    "ar" => (
                        $"تم إلغاء طلبك #{orderId:N} - Dune & Flame",
                        "تم إلغاء الطلب",
                        "تصفح مجموعتنا"
                    ),
                    _ => (
                        $"Order #{orderId:N} Cancelled - Dune & Flame",
                        "Order Cancelled",
                        "Browse Our Collection"
                    )
                };

                var content = LoadContentTemplate("email-order-cancelled.html")
                    .Replace("{{OrderId}}",      orderId.ToString("N"))
                    .Replace("{{RefundAmount}}", $"${refundAmount:F2}")
                    .Replace("{{BrowseButton}}", CreateButton(buttonText, productsUrl));

                var body = ApplyEmailTemplate(title, content);
                await SendGenericEmailAsync(to, subject, body);
                _logger?.LogInformation("SendOrderCancelledAsync: Order cancelled email sent to {To} for OrderId {OrderId}", to, orderId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendOrderCancelledAsync: Failed to send order cancelled email to {To} for OrderId {OrderId}", to, orderId);
                throw;
            }
        }

        /// <summary>
        /// Send a beautifully formatted newsletter campaign email.
        /// Wraps raw admin html content into the premium Dune & Flame master template.
        /// </summary>
        public async Task SendNewsletterCampaignAsync(string to, string subject, string htmlContent)
        {
            try
            {
                // Wrap the raw content from the admin editor into our beautiful template
                var body = ApplyEmailTemplate(subject, htmlContent);
                await SendGenericEmailAsync(to, subject, body);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendNewsletterCampaignAsync: Failed to send newsletter to {To}", to);
                throw;
            }
        }

        /// <summary>
        /// Sends a welcome confirmation email to a newly subscribed user.
        /// </summary>
        public async Task SendNewsletterSubscribedAsync(string to, string unsubscribeToken)
        {
            try
            {
                var unsubscribeLink = $"{_clientUrls.BaseUrl}/en/unsubscribe?token={Uri.EscapeDataString(unsubscribeToken)}";

                var content = LoadContentTemplate("email-newsletter-subscribed.html")
                    .Replace("{{UnsubscribeLink}}", unsubscribeLink);

                var body = ApplyEmailTemplate("You're subscribed!", content);
                await SendGenericEmailAsync(to, "Welcome to Dune & Flame Newsletter", body);
                _logger?.LogInformation("SendNewsletterSubscribedAsync: Welcome email sent to {To}", to);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendNewsletterSubscribedAsync: Failed to send welcome email to {To}", to);
                throw;
            }
        }

        /// <summary>
        /// Sends a new-subscriber report to the admin inbox (info@duneflame.com).
        /// </summary>
        public async Task SendNewsletterAdminReportAsync(string subscriberEmail, int totalSubscribers)
        {
            try
            {
                var sanitizedEmail = System.Net.WebUtility.HtmlEncode(subscriberEmail);

                var content = LoadContentTemplate("email-newsletter-admin-report.html")
                    .Replace("{{SubscriberEmail}}",  sanitizedEmail)
                    .Replace("{{TotalSubscribers}}", totalSubscribers.ToString())
                    .Replace("{{SubscribedAt}}",     DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                var body = ApplyEmailTemplate("New Newsletter Subscriber", content);
                await SendGenericEmailAsync(_settings.FromEmail, $"[Newsletter] New subscriber: {sanitizedEmail}", body);
                _logger?.LogInformation("SendNewsletterAdminReportAsync: Admin report sent for subscriber {Email}", subscriberEmail);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendNewsletterAdminReportAsync: Failed to send admin report for {Email}", subscriberEmail);
                throw;
            }
        }

        /// <summary>
        /// Sends a wholesale lead alert to the admin inbox with full lead details.
        /// </summary>
        public async Task SendWholesaleLeadAlertAsync(DuneFlame.Domain.Entities.WholesaleLead lead)
        {
            try
            {
                if (lead == null)
                {
                    _logger?.LogError("SendWholesaleLeadAlertAsync: Lead is null");
                    throw new ArgumentNullException(nameof(lead));
                }

                var sanitizedFullName     = System.Net.WebUtility.HtmlEncode(lead.FullName);
                var sanitizedBusinessName = System.Net.WebUtility.HtmlEncode(lead.BusinessName);
                var sanitizedEmail        = System.Net.WebUtility.HtmlEncode(lead.Email);
                var sanitizedPhone        = System.Net.WebUtility.HtmlEncode(lead.Phone);
                var sanitizedMessage      = System.Net.WebUtility.HtmlEncode(lead.Message ?? string.Empty)
                                                .Replace("\n", "<br/>");

                var content = LoadContentTemplate("email-wholesale-alert.html")
                    .Replace("{{FullName}}",      sanitizedFullName)
                    .Replace("{{BusinessName}}",  sanitizedBusinessName)
                    .Replace("{{Email}}",         sanitizedEmail)
                    .Replace("{{Phone}}",         sanitizedPhone)
                    .Replace("{{BusinessType}}",  lead.BusinessType.ToString())
                    .Replace("{{MonthlyVolume}}", lead.MonthlyVolume.ToString())
                    .Replace("{{Message}}",       sanitizedMessage)
                    .Replace("{{ReceivedAt}}",    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                var body = ApplyEmailTemplate("New Wholesale Lead", content);
                await SendGenericEmailAsync(_settings.FromEmail, $"[Wholesale] New B2B Inquiry from {sanitizedBusinessName}", body);

                _logger?.LogInformation("SendWholesaleLeadAlertAsync: Admin alert sent for wholesale lead from {Email}", lead.Email);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendWholesaleLeadAlertAsync: Failed to send wholesale lead alert for {Email}", lead.Email);
                throw;
            }
        }

        /// <summary>
        /// Sends a thank-you confirmation email to the customer who submitted a wholesale inquiry.
        /// </summary>
        public async Task SendWholesaleLeadConfirmationAsync(string customerEmail, string customerName, string languageCode = "en")
        {
            try
            {
                var code = languageCode?.ToLowerInvariant().Substring(0, Math.Min(2, languageCode?.Length ?? 0)) ?? "en";
                var sanitizedName = System.Net.WebUtility.HtmlEncode(customerName);

                var (subject, title) = code switch
                {
                    "ar" => (
                        "استلمنا طلب الجملة الخاص بك - Dune & Flame",
                        "تم استلام طلب الجملة"
                    ),
                    _ => (
                        "We Received Your Wholesale Inquiry - Dune & Flame",
                        "Wholesale Inquiry Received"
                    )
                };

                var content = LoadContentTemplate("email-wholesale-confirmation.html")
                    .Replace("{{CustomerName}}", sanitizedName);

                var body = ApplyEmailTemplate(title, content);
                await SendGenericEmailAsync(customerEmail, subject, body);

                _logger?.LogInformation("SendWholesaleLeadConfirmationAsync: Confirmation sent to {Email}", customerEmail);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SendWholesaleLeadConfirmationAsync: Failed to send confirmation to {Email}", customerEmail);
                throw;
            }
        }
    }