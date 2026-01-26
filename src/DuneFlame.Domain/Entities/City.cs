using DuneFlame.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a city within a country for the shipping system.
/// Cities can be used for address validation and localized shipping options.
/// </summary>
public class City : BaseEntity
{
    /// <summary>
    /// Name of the city (e.g., "New York", "Dubai", "Toronto").
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the Country this city belongs to.
    /// </summary>
    public Guid CountryId { get; set; }

    // Navigation property
    public Country? Country { get; set; }
}
