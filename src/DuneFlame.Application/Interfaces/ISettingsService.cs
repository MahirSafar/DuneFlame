namespace DuneFlame.Application.Interfaces;

public interface ISettingsService
{
    Task<string?> GetSettingAsync(string key);
    Task SetSettingAsync(string key, string value);
}
