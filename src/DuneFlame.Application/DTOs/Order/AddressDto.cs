namespace DuneFlame.Application.DTOs.Order;

public record AddressDto(
    string Street,
    string City,
    string State,
    string PostalCode,
    string Country
)
{
    public override string ToString() => $"{Street}, {City}, {State} {PostalCode}, {Country}";
}
