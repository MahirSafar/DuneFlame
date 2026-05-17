using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class WholesaleLead : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public WholesaleBusinessType BusinessType { get; set; }
    public WholesaleMonthlyVolume MonthlyVolume { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsReviewed { get; set; } = false;
}
