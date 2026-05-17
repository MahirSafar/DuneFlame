using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.User;

namespace DuneFlame.Application.Interfaces;

public interface IContactService
{
    Task SubmitMessageAsync(ContactMessageRequest request, string? ipAddress);
    Task<PagedResult<ContactMessageResponse>> GetAllAdminAsync(AdminContactsQuery query);
    Task MarkAsReadAsync(Guid id);
    Task DeleteAsync(Guid id);
}