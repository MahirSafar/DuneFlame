using DuneFlame.Application.DTOs.User;

namespace DuneFlame.Application.Interfaces;

public interface INewsletterService
{
    Task SubscribeAsync(NewsletterRequest request);
    Task<bool> VerifyEmailAsync(string token);
    Task UnsubscribeAsync(string token);
}