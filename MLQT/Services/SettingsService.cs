using MLQT.Services.Interfaces;
using System.Text.Json;

namespace MLQT.Services;

/// <summary>
/// MAUI implementation of settings service using platform-specific Preferences API
/// </summary>
public class SettingsService : ISettingsService
{
    public Task<T> GetAsync<T>(string key, T defaultValue)
    {
        try
        {
            // Handle primitive types directly with Preferences API
            if (typeof(T) == typeof(string))
                return Task.FromResult((T)(object)Preferences.Get(key, (string)(object)defaultValue!));
            if (typeof(T) == typeof(int))
                return Task.FromResult((T)(object)Preferences.Get(key, (int)(object)defaultValue!));
            if (typeof(T) == typeof(bool))
                return Task.FromResult((T)(object)Preferences.Get(key, (bool)(object)defaultValue!));
            if (typeof(T) == typeof(double))
                return Task.FromResult((T)(object)Preferences.Get(key, (double)(object)defaultValue!));
            if (typeof(T) == typeof(float))
                return Task.FromResult((T)(object)Preferences.Get(key, (float)(object)defaultValue!));
            if (typeof(T) == typeof(long))
                return Task.FromResult((T)(object)Preferences.Get(key, (long)(object)defaultValue!));
            if (typeof(T) == typeof(DateTime))
                return Task.FromResult((T)(object)Preferences.Get(key, (DateTime)(object)defaultValue!));

            // For complex types, use JSON serialization
            var json = Preferences.Get(key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return Task.FromResult(defaultValue);

            var result = JsonSerializer.Deserialize<T>(json);
            return Task.FromResult(result ?? defaultValue);
        }
        catch
        {
            return Task.FromResult(defaultValue);
        }
    }

    public Task SetAsync<T>(string key, T value)
    {
        try
        {
            // Handle primitive types directly with Preferences API
            if (value is string strValue)
                Preferences.Set(key, strValue);
            else if (value is int intValue)
                Preferences.Set(key, intValue);
            else if (value is bool boolValue)
                Preferences.Set(key, boolValue);
            else if (value is double doubleValue)
                Preferences.Set(key, doubleValue);
            else if (value is float floatValue)
                Preferences.Set(key, floatValue);
            else if (value is long longValue)
                Preferences.Set(key, longValue);
            else if (value is DateTime dateValue)
                Preferences.Set(key, dateValue);
            else
            {
                // Serialize complex objects to JSON
                var json = JsonSerializer.Serialize(value);
                Preferences.Set(key, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving setting {key}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        try
        {
            Preferences.Remove(key);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing setting {key}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        try
        {
            Preferences.Clear();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing settings: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
