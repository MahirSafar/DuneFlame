using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.User;
using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface INewsletterService
{
    Task SubscribeAsync(NewsletterRequest request);
    Task<bool> VerifyEmailAsync(string token);
    Task UnsubscribeAsync(string token);
    Task SendToAllAsync(BulkEmailRequest request);
    Task<PagedResult<NewsletterSubscription>> GetAllSubscribersAsync(int pageNumber, int pageSize, string? search);
}