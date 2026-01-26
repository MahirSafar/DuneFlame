namespace DuneFlame.Application.DTOs.Shipping;

/// <summary>
/// Data Transfer Object for City information.
/// Used for returning city data to the frontend for address selection.
/// </summary>
public class CityDto
{
    /// <summary>
    /// Unique identifier of the city.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Name of the city (e.g., "Dubai", "New York", "Toronto").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the country this city belongs to.
    /// </summary>
    public Guid CountryId { get; set; }
}
