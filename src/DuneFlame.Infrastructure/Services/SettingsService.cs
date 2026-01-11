using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class SettingsService(
    AppDbContext context,
    IDistributedCache cache,
    ILogger<SettingsService> logger) : ISettingsService
{
    private readonly AppDbContext _context = context;
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<SettingsService> _logger = logger;
    private const string CacheKeyPrefix = "setting-";
    private const int CacheDurationHours = 24;

    public async Task<string?> GetSettingAsync(string key)
    {
        try
        {
            var cacheKey = $"{CacheKeyPrefix}{key}";

            // Try to get from cache first
            var cachedValue = await _cache.GetStringAsync(cacheKey);
            if (cachedValue != null)
            {
                _logger.LogInformation("Setting retrieved from cache: {Key}", key);
                return cachedValue;
            }

            // Get from database
            var setting = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                _logger.LogWarning("Setting not found: {Key}", key);
                return null;
            }

            // Cache the result
            var cacheOptions = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(CacheDurationHours));

            await _cache.SetStringAsync(cacheKey, setting.Value, cacheOptions);
            _logger.LogInformation("Setting retrieved from database and cached: {Key}", key);

            return setting.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving setting: {Key}", key);
            throw;
        }
    }

    public async Task SetSettingAsync(string key, string value)
    {
        try
        {
            var setting = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                setting = new AppSetting { Key = key, Value = value };
                _context.AppSettings.Add(setting);
            }
            else
            {
                setting.Value = value;
                _context.AppSettings.Update(setting);
            }

            await _context.SaveChangesAsync();

            // Invalidate cache
            var cacheKey = $"{CacheKeyPrefix}{key}";
            await _cache.RemoveAsync(cacheKey);

            _logger.LogInformation("Setting saved and cache invalidated: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving setting: {Key}", key);
            throw;
        }
    }
}
