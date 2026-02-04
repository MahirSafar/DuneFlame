namespace DuneFlame.Application.DTOs.Order;

public record AddressDto(
    string Street,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? Email = null,
    string? PhoneNumber = null,
    string? FirstName = null,
    string? LastName = null
)
{
    public override string ToString() => $"{Street}, {City}, {State} {PostalCode}, {Country}";
}
