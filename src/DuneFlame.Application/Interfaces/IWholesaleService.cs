using DuneFlame.Application.DTOs.Wholesale;

namespace DuneFlame.Application.Interfaces;

public interface IWholesaleService
{
    Task SubmitLeadAsync(CreateWholesaleLeadRequest request);
    Task<PagedResult<WholesaleLeadResponse>> GetAllAdminAsync(int pageNumber, int pageSize);
    Task MarkAsReviewedAsync(Guid id);
}
