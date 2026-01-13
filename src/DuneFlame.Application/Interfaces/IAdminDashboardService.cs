using DuneFlame.Application.DTOs.Admin;

namespace DuneFlame.Application.Interfaces;

public interface IAdminDashboardService
{
    Task<DashboardStatsDto> GetDashboardStatsAsync();
}
