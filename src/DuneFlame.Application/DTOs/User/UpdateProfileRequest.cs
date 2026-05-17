using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.User;

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public IFormFile? Image { get; set; }
}
