using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class JsonConfigurationService : IConfigurationService
{
    private readonly string _configPath;
    private readonly ILogger<JsonConfigurationService> _logger;
    private readonly ReaderWriterLockSlim _lock = new();
    private AppConfig _cachedConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public JsonConfigurationService(ILogger<JsonConfigurationService> logger)
        : this(logger, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"))
    {
    }

    public JsonConfigurationService(ILogger<JsonConfigurationService> logger, string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Configuration path cannot be empty.", nameof(configPath));
        }

        _logger = logger;
        _configPath = configPath;
        _cachedConfig = Normalize(new AppConfig());
    }

    public async Task<AppConfig> LoadAsync()
    {
        try
        {
            AppConfig loaded;
            if (File.Exists(_configPath))
            {
                var json = await File.ReadAllTextAsync(_configPath, Encoding.UTF8);
                loaded = Normalize(JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig());
                _logger.LogInformation("Configuration loaded from {Path}", _configPath);
            }
            else
            {
                _logger.LogWarning("Configuration file not found. Creating defaults at {Path}", _configPath);
                loaded = Normalize(new AppConfig());
                await SaveAsync(loaded);
                return Clone(loaded);
            }

            _lock.EnterWriteLock();
            try
            {
                _cachedConfig = Clone(loaded);
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return Clone(loaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration, using normalized defaults.");
            var fallback = Normalize(new AppConfig());
            _lock.EnterWriteLock();
            try
            {
                _cachedConfig = Clone(fallback);
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return Clone(fallback);
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var snapshot = Normalize(Clone(config));

        _lock.EnterWriteLock();
        try
        {
            snapshot.Revision = Math.Max(snapshot.Revision, _cachedConfig.Revision) + 1;
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var tempPath = _configPath + ".tmp";
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, _configPath, overwrite: true);
            _cachedConfig = Clone(snapshot);
            _logger.LogInformation("Configuration saved to {Path}. Revision={Revision}", _configPath, snapshot.Revision);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration.");
            throw;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        await Task.CompletedTask;
    }

    public AppConfig GetCurrent()
    {
        _lock.EnterReadLock();
        try
        {
            return Clone(_cachedConfig);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.Normalize();
        return config;
    }

    private static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return Normalize(JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig());
    }
}
