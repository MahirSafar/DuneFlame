namespace DuneFlame.Application.DTOs.Admin;

public class DashboardStatsDto
{
    public decimal TotalRevenue { get; set; }
    public double RevenueGrowthPercentage { get; set; }
    public int ActiveOrders { get; set; }
    public int PendingShipmentOrders { get; set; }
    public int TotalUsers { get; set; }
    public int NewUsersThisWeek { get; set; }
    public int TotalProducts { get; set; }
    public int LowStockCount { get; set; }
    public List<DashboardActivityDto> RecentActivities { get; set; } = [];

    // --- CHART & ANALYTICS PROPERTIES ---
    public List<RevenueChartDto> RevenueChart { get; set; } = [];
    public List<OrderStatusDto> OrderStatus { get; set; } = [];
    public List<TopProductDto> TopProducts { get; set; } = [];
}

public class DashboardActivityDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Time { get; set; }
}

public class RevenueChartDto
{
    public string Date { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Orders { get; set; }
}

public class OrderStatusDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Color { get; set; } = string.Empty;
}

public class TopProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Sales { get; set; }
    public decimal Revenue { get; set; }
}
