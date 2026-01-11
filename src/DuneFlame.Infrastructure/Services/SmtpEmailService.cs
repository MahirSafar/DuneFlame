using System.Net;
using System.Net.Mail;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

public class SmtpEmailService(IOptions<EmailSettings> settings) : IEmailService
{
    private readonly EmailSettings _settings = settings.Value;

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

    public async Task SendGenericEmailAsync(string to, string subject, string body)
    {
        var message = new MailMessage(_settings.FromEmail, to)
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            Credentials = new NetworkCredential(_settings.Username, _settings.Password),
            EnableSsl = true
        };

        await client.SendMailAsync(message);
    }

    public async Task SendVerificationEmailAsync(string to, string userId, string token)
    {
        // Link points to API, which then redirects to Frontend
        var verificationLink = $"https://localhost:7190/api/v1/auth/verify-email?userId={userId}&token={WebUtility.UrlEncode(token)}";

        var body = GetHtmlTemplate(
            "Verify Your Account",
            "Welcome to DuneFlame! We're excited to have you. Please click the button below to confirm your email address and activate your account.",
            "Confirm Email",
            verificationLink);

        await SendGenericEmailAsync(to, "Confirm Your Email - DuneFlame", body);
    }

    public async Task SendPasswordResetEmailAsync(string to, string userId, string token)
    {
        // Password reset links usually go directly to the Frontend (Next.js)
        var frontendResetUrl = $"http://localhost:3000/auth/reset-password?userId={userId}&token={WebUtility.UrlEncode(token)}";

        var body = GetHtmlTemplate(
            "Reset Your Password",
            "Forgot your password? No worries. Click the button below to set a new password for your account.",
            "Reset Password",
            frontendResetUrl);

        await SendGenericEmailAsync(to, "Reset Password Request - DuneFlame", body);
    }

    public async Task SendNewsletterVerificationAsync(string to, string token)
    {
        var link = $"https://localhost:7190/api/v1/newsletter/verify?token={WebUtility.UrlEncode(token)}";

        var body = GetHtmlTemplate(
            "Confirm Subscription",
            "Stay updated with our latest collections and offers! Confirm your subscription to our newsletter below.",
            "Subscribe Now",
            link);

        await SendGenericEmailAsync(to, "Newsletter Subscription - DuneFlame", body);
    }

    public async Task SendAdminContactAlertAsync(ContactMessage message)
    {
        var body = $@"
            <div style='font-family: sans-serif; border: 1px solid #ddd; padding: 25px; border-radius: 8px;'>
                <h2 style='color: #e67e22;'>New Support Request</h2>
                <p><strong>Name:</strong> {message.Name}</p>
                <p><strong>Email:</strong> {message.Email}</p>
                <p><strong>Subject:</strong> {message.Subject}</p>
                <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                <p><strong>Message:</strong><br/>{message.Message}</p>
            </div>";

        await SendGenericEmailAsync(_settings.FromEmail, $"[Contact Form] {message.Subject}", body);
    }
}