using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class AppSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty; // Məs: "RewardPercentage"
    public string Value { get; set; } = string.Empty; // Məs: "5"
}
