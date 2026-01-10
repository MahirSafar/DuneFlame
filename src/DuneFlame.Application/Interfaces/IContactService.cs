using DuneFlame.Application.DTOs.User;

namespace DuneFlame.Application.Interfaces;

public interface IContactService
{
    Task SubmitMessageAsync(ContactMessageRequest request, string? ipAddress);
}