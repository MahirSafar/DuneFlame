using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.User;

namespace DuneFlame.Application.Interfaces;

public interface IContactService
{
    Task SubmitMessageAsync(ContactMessageRequest request, string? ipAddress);
    Task<PagedResult<ContactMessageResponse>> GetAllAdminAsync(int pageNumber = 1, int pageSize = 10, string? search = null, bool? isRead = null);
    Task MarkAsReadAsync(Guid id);
    Task DeleteAsync(Guid id);
}