using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DuneFlame.Domain.Entities;

public class CustomerBasket
{
    [Key]
    public string Id { get; set; } = string.Empty;

    public string Items { get; set; } = "[]"; // Serialized JSON of Basket Items

    public DateTimeOffset ExpiresAt { get; set; }
}