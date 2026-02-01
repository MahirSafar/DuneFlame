using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class AdminDashboardService(
    AppDbContext context,
    ILogger<AdminDashboardService> logger) : IAdminDashboardService
{
    private readonly AppDbContext _context = context;
    private readonly ILogger<AdminDashboardService> _logger = logger;

    public async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var oneMonthAgo = now.AddMonths(-1);
            var oneWeekAgo = now.AddDays(-7);

            // 1. Calculate Total Revenue (from paid orders)
            var totalRevenue = await _context.Orders
                .Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered)
                .SumAsync(o => o.TotalAmount);

            // 2. Calculate Previous Month Revenue for growth calculation
            var previousMonthRevenue = await _context.Orders
                .Where(o => (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered)
                         && o.CreatedAt >= oneMonthAgo.AddMonths(-1) && o.CreatedAt < oneMonthAgo)
                .SumAsync(o => o.TotalAmount);

            var revenueGrowthPercentage = previousMonthRevenue > 0
                ? ((double)(totalRevenue - previousMonthRevenue) / (double)previousMonthRevenue) * 100
                : 0;

            // 3. Count Active Orders (NOT Delivered or Cancelled)
            var activeOrders = await _context.Orders
                .Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
                .CountAsync();

            // 4. Count Pending Shipment Orders (Paid status)
            var pendingShipmentOrders = await _context.Orders
                .Where(o => o.Status == OrderStatus.Paid)
                .CountAsync();

            // 5. Count Total Users
            var totalUsers = await _context.Users.CountAsync();

            // 6. Count New Users This Week (approximated using orders - users who placed orders this week)
            var newUsersThisWeek = await _context.Orders
                .Where(o => o.CreatedAt >= oneWeekAgo)
                .Select(o => o.UserId)
                .Distinct()
                .CountAsync();

            // 7. Count Total Products
            var totalProducts = await _context.Products.CountAsync();

            // 8. Count Low Stock Products (StockInKg < 5.0)
            var lowStockCount = await _context.Products
                .Where(p => p.StockInKg < 5.0m)
                .CountAsync();

            // 9. Get Recent Activities (latest 5 orders + latest 5 products)
            var recentOrders = await _context.Orders
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new { o.Id, o.CreatedAt })
                .ToListAsync();

            var recentProducts = await _context.Products
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .Include(p => p.Translations)
                .Select(p => new { p.Id, p.CreatedAt, Translation = p.Translations.FirstOrDefault(t => t.LanguageCode == "en") })
                .ToListAsync();

            var recentActivities = new List<DashboardActivityDto>();

            // Map orders to activities
            foreach (var order in recentOrders)
            {
                recentActivities.Add(new DashboardActivityDto
                {
                    Id = order.Id,
                    Type = "Order",
                    Message = "New order placed",
                    Time = order.CreatedAt
                });
            }

            // Map products to activities
            foreach (var product in recentProducts)
            {
                var productName = product.Translation?.Name ?? "Unknown";
                recentActivities.Add(new DashboardActivityDto
                {
                    Id = product.Id,
                    Type = "Product",
                    Message = $"New product added: {productName}",
                    Time = product.CreatedAt
                });
            }

            // Combine, sort by time descending, and take top 10
            recentActivities = [.. recentActivities
                .OrderByDescending(a => a.Time)
                .Take(10)];

            // 10. Build Revenue Chart Data (last 7 days)
            var revenueChartData = await BuildRevenueChartDataAsync(DateTime.MinValue, DateTime.MaxValue);

            // 11. Build Order Status Distribution
            var orderStatusData = await BuildOrderStatusDataAsync();

            // 12. Build Top Products (by sales count)
            var topProductsData = await BuildTopProductsDataAsync();

            var dashboardStats = new DashboardStatsDto
            {
                TotalRevenue = totalRevenue,
                RevenueGrowthPercentage = revenueGrowthPercentage,
                ActiveOrders = activeOrders,
                PendingShipmentOrders = pendingShipmentOrders,
                TotalUsers = totalUsers,
                NewUsersThisWeek = newUsersThisWeek,
                TotalProducts = totalProducts,
                LowStockCount = lowStockCount,
                RecentActivities = recentActivities,
                RevenueChart = revenueChartData,
                OrderStatus = orderStatusData,
                TopProducts = topProductsData
            };

            _logger.LogInformation(
                "Dashboard stats retrieved: Revenue={Revenue}, ActiveOrders={ActiveOrders}, TotalUsers={TotalUsers}",
                totalRevenue, activeOrders, totalUsers);

            return dashboardStats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard stats");
            throw;
        }
    }

    private async Task<List<RevenueChartDto>> BuildRevenueChartDataAsync(DateTime startDate, DateTime endDate)
    {
        // Get all orders for the date range (only paid orders)
        var orders = await _context.Orders
            .Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered)
            .Select(o => new { o.CreatedAt, o.TotalAmount })
            .ToListAsync();

        var chartData = new List<RevenueChartDto>();
        var now = DateTime.UtcNow;

        // Loop from i = 6 down to 0 (last 7 days)
        for (int i = 6; i >= 0; i--)
        {
            var currentDate = now.AddDays(-i).Date;

            // Filter orders for the current date
            var dayOrders = orders.Where(o => o.CreatedAt.Date == currentDate).ToList();

            // Calculate daily revenue and order count
            var dailyRevenue = dayOrders.Sum(o => o.TotalAmount);
            var dailyOrderCount = dayOrders.Count;

            // Format date as "MMM dd" (e.g., "Jan 01")
            var formattedDate = currentDate.ToString("MMM dd");

            chartData.Add(new RevenueChartDto
            {
                Date = formattedDate,
                Revenue = dailyRevenue,
                Orders = dailyOrderCount
            });
        }

        return chartData;
    }

    private async Task<List<OrderStatusDto>> BuildOrderStatusDataAsync()
    {
        // Define status colors based on requirements
        var statusColors = new Dictionary<OrderStatus, string>
        {
            { OrderStatus.Pending, "#f59e0b" },      // Orange
            { OrderStatus.Paid, "#3b82f6" },         // Blue
            { OrderStatus.Shipped, "#8b5cf6" },      // Purple
            { OrderStatus.Delivered, "#10b981" },    // Green
            { OrderStatus.Cancelled, "#ef4444" }     // Red
        };

        // Group orders by status and get counts
        var orderCounts = await _context.Orders
            .GroupBy(o => o.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var statusData = new List<OrderStatusDto>();

        // Map to OrderStatusDto with assigned colors
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            var count = orderCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
            statusData.Add(new OrderStatusDto
            {
                Status = status.ToString(),
                Count = count,
                Color = statusColors.TryGetValue(status, out var color) ? color : "#6b7280"
            });
        }

        return statusData;
    }

    private async Task<List<TopProductDto>> BuildTopProductsDataAsync()
    {
        // Query OrderItems, group by ProductName
        var topProducts = await _context.OrderItems
            .GroupBy(oi => oi.ProductName)
            .Select(g => new
            {
                ProductName = g.Key,
                Sales = g.Sum(oi => oi.Quantity),
                Revenue = g.Sum(oi => oi.UnitPrice * oi.Quantity)
            })
            .OrderByDescending(x => x.Sales)
            .Take(5)
            .ToListAsync();

        // Map to TopProductDto
        return topProducts.Select(p => new TopProductDto
        {
            Id = Guid.Empty, // ProductPriceId is not used in summary view
            Name = p.ProductName,
            Sales = p.Sales,
            Revenue = p.Revenue
        }).ToList();
    }
}
