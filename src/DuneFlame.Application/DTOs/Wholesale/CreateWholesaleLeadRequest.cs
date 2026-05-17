using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Wholesale;

public class CreateWholesaleLeadRequest
{
    public string FullName { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public WholesaleBusinessType BusinessType { get; set; }
    public WholesaleMonthlyVolume MonthlyVolume { get; set; }
    public string Message { get; set; } = string.Empty;
}
