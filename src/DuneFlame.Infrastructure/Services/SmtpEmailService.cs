using System.Net;
using System.Net.Mail;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

public class SmtpEmailService(IOptions<EmailSettings> settings) : IEmailService
{
    private readonly EmailSettings _settings = settings.Value;

    // --- ƏSAS KÖMƏKÇİ METOD (CORE) ---
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

    // --- DAY 3: NEWSLETTER & CONTACT ---

    public async Task SendNewsletterVerificationAsync(string to, string token)
    {
        var link = $"https://localhost:7190/api/v1/newsletter/verify?token={token}";
        await SendGenericEmailAsync(to, "Confirm Subscription",
            $"Please confirm your subscription by clicking here: <a href='{link}'>Confirm</a>");
    }

    public async Task SendAdminContactAlertAsync(ContactMessage message)
    {
        var body = $@"<h3>New Contact Message</h3>
                      <p><b>From:</b> {message.Name} ({message.Email})</p>
                      <p><b>Subject:</b> {message.Subject}</p>
                      <p><b>Message:</b><br/>{message.Message}</p>";

        await SendGenericEmailAsync(_settings.FromEmail, "New Contact Form Submission", body);
    }

    // --- DAY 2: AUTHENTICATION (Update edilmiş versiya) ---

    public async Task SendVerificationEmailAsync(string to, string userId, string token)
    {
        var verificationLink = $"https://localhost:7190/api/v1/auth/verify-email?userId={userId}&token={WebUtility.UrlEncode(token)}";
        await SendGenericEmailAsync(to, "Email Verification",
            $"Welcome! Verify your email here: <a href='{verificationLink}'>Verify Email</a>");
    }

    public async Task SendPasswordResetEmailAsync(string to, string userId, string token)
    {
        // Front-end linki (gələcəkdə react tərəfdə olacaq)
        var resetLink = $"https://localhost:7190/reset-password?userId={userId}&token={WebUtility.UrlEncode(token)}";
        await SendGenericEmailAsync(to, "Reset Password",
            $"Click here to reset your password: <a href='{resetLink}'>Reset Password</a>");
    }
}