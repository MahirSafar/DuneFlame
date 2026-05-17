using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Admin;

public class AdminContactsQuery
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public bool? IsRead { get; set; }
    public InquiryType? InquiryType { get; set; }
}
