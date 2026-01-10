using System.Net;
using System.Net.Mail;
using DuneFlame.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

public class SmtpEmailService(IOptions<EmailSettings> settings) : IEmailService
{
    private readonly EmailSettings _settings = settings.Value;

    public async Task SendVerificationEmailAsync(string to, string userId, string token)
    {
        // Gələcəkdə bunu front-end url-i ilə əvəz edəcəyik
        var verificationLink = $"https://localhost:7190/api/v1/auth/verify-email?userId={userId}&token={WebUtility.UrlEncode(token)}";

        var message = new MailMessage(_settings.FromEmail, to)
        {
            Subject = "Dune & Flame - Email Verification",
            Body = $@"<h3>Welcome to Dune & Flame!</h3>
                     <p>Please verify your email by clicking the link below:</p>
                     <a href='{verificationLink}'>Verify My Email</a>",
            IsBodyHtml = true
        };

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            Credentials = new NetworkCredential(_settings.Username, _settings.Password),
            EnableSsl = true
        };

        await client.SendMailAsync(message);
    }
}