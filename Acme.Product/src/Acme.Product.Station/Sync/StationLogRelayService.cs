using System.Threading.Channels;
using Acme.Product.Runtime.Abstractions;
using Microsoft.Extensions.Options;

namespace Acme.Product.Station.Sync;

public sealed class StationLogRelayService
{
    private readonly StationIdentityResolver _identityResolver;
    private readonly StationLocalSettingsStore _settingsStore;
    private readonly Channel<StationLogSummaryDto> _channel;
    private readonly Queue<DateTimeOffset> _recentAccepted = new();
    private readonly object _rateLimitSync = new();
    private readonly int _maxSummariesPerMinute;
    private string? _lastFingerprint;
    private DateTimeOffset _lastFingerprintAtUtc;

    public StationLogRelayService(
        StationIdentityResolver identityResolver,
        StationLocalSettingsStore settingsStore,
        IOptions<StationSyncOptions> options)
    {
        _identityResolver = identityResolver;
        _settingsStore = settingsStore;
        _maxSummariesPerMinute = Math.Max(1, options.Value.MaxLogSummariesPerMinute);
        _channel = Channel.CreateBounded<StationLogSummaryDto>(
            new BoundedChannelOptions(Math.Max(1, options.Value.LogQueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public bool TryEnqueue(string level, string source, string message, Exception? exception = null)
    {
        if (!IsReportable(level, message))
        {
            return false;
        }

        if (!TryConsumeLogBudget(level, source, message, exception))
        {
            return false;
        }

        var identity = _identityResolver.GetOrCreate();
        var sequenceId = _settingsStore.NextLogSequenceId();
        return _channel.Writer.TryWrite(new StationLogSummaryDto
        {
            StationId = identity.StationId,
            SequenceId = sequenceId,
            MessageId = $"log_{identity.StationId}_{sequenceId}_{Guid.NewGuid():N}",
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = NormalizeLevel(level),
            Source = source,
            RenderedMessage = Truncate(Scrub(message), 1000),
            ExceptionType = exception?.GetType().Name,
            ExceptionMessage = Truncate(Scrub(exception?.Message), 1000),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public bool TryRead(out StationLogSummaryDto log)
    {
        return _channel.Reader.TryRead(out log!);
    }

    private static bool IsReportable(string level, string message)
    {
        var normalized = NormalizeLevel(level);
        return normalized is "WARN" or "ERROR" or "FATAL" ||
               message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("异常", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("错误", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLevel(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "WARNING" => "WARN",
            "WARN" => "WARN",
            "ERROR" => "ERROR",
            "FATAL" => "FATAL",
            _ => "INFO"
        };
    }

    private bool TryConsumeLogBudget(string level, string source, string message, Exception? exception)
    {
        var now = DateTimeOffset.UtcNow;
        var fingerprint = $"{NormalizeLevel(level)}|{source}|{message}|{exception?.GetType().FullName}";

        lock (_rateLimitSync)
        {
            if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal) &&
                now - _lastFingerprintAtUtc < TimeSpan.FromSeconds(5))
            {
                return false;
            }

            while (_recentAccepted.Count > 0 && now - _recentAccepted.Peek() >= TimeSpan.FromMinutes(1))
            {
                _recentAccepted.Dequeue();
            }

            if (_recentAccepted.Count >= _maxSummariesPerMinute)
            {
                return false;
            }

            _recentAccepted.Enqueue(now);
            _lastFingerprint = fingerprint;
            _lastFingerprintAtUtc = now;
            return true;
        }
    }

    private static string? Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var scrubbed = value;
        foreach (var key in new[] { "StationSync:SharedToken", "X-Station-Token", "X-ClearVision-Station-Token" })
        {
            scrubbed = System.Text.RegularExpressions.Regex.Replace(
                scrubbed,
                $@"(?i)\b{System.Text.RegularExpressions.Regex.Escape(key)}\b\s*[:=]\s*[^\s,;]+",
                $"{key}=redacted");
            scrubbed = scrubbed.Replace(key, $"{key}(redacted)", StringComparison.OrdinalIgnoreCase);
        }

        return scrubbed;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
