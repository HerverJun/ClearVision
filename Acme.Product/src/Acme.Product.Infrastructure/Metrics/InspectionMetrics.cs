using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Metrics;

public sealed class InspectionMetrics
{
    public const string MeterName = "Acme.Product.Inspection";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter;
    private readonly Histogram<double> _detectionLatency;
    private readonly Histogram<double> _flowExecutionLatency;
    private readonly UpDownCounter<int> _activeWorkers;
    private readonly Counter<long> _inspectionsTotal;
    private readonly Counter<long> _inspectionsFailed;
    private readonly ObservableGauge<int> _activeSessionsGauge;

    private int _activeSessionsCount;

    public InspectionMetrics()
    {
        _meter = new Meter(MeterName, MeterVersion);

        _detectionLatency = _meter.CreateHistogram<double>(
            "inspection.detection.latency_ms",
            unit: "ms",
            description: "Single inspection latency.");

        _flowExecutionLatency = _meter.CreateHistogram<double>(
            "inspection.flow_execution.latency_ms",
            unit: "ms",
            description: "Flow execution latency.");

        _activeWorkers = _meter.CreateUpDownCounter<int>(
            "inspection.workers.active",
            description: "Current active inspection worker count.");

        _inspectionsTotal = _meter.CreateCounter<long>(
            "inspection.total",
            unit: "1",
            description: "Total completed inspection count.");

        _inspectionsFailed = _meter.CreateCounter<long>(
            "inspection.failed",
            unit: "1",
            description: "Failed inspection count.");

        _activeSessionsGauge = _meter.CreateObservableGauge(
            "inspection.sessions.active",
            () => new Measurement<int>(_activeSessionsCount),
            description: "Current active realtime inspection sessions.");
    }

    public void RecordDetectionLatency(double latencyMs, string status = "OK")
    {
        _detectionLatency.Record(
            latencyMs,
            new KeyValuePair<string, object?>("status", status));
    }

    public void RecordFlowExecutionLatency(double latencyMs, bool success)
    {
        _flowExecutionLatency.Record(
            latencyMs,
            new KeyValuePair<string, object?>("success", success));
    }

    public void IncrementActiveWorkers()
    {
        _activeWorkers.Add(1);
    }

    public void DecrementActiveWorkers()
    {
        _activeWorkers.Add(-1);
    }

    public void RecordInspectionCompleted(string status, int defectCount = 0)
    {
        _inspectionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("has_defects", defectCount > 0));
    }

    public void RecordInspectionFailed(string errorType)
    {
        _inspectionsFailed.Add(
            1,
            new KeyValuePair<string, object?>("error_type", errorType));
    }

    public void UpdateActiveSessions(int count)
    {
        Interlocked.Exchange(ref _activeSessionsCount, count);
    }

    public Meter GetMeter() => _meter;
}

public sealed class InspectionContext
{
    private static readonly AsyncLocal<InspectionContext?> CurrentContext = new();

    public static InspectionContext? Current
    {
        get => CurrentContext.Value;
        private set => CurrentContext.Value = value;
    }

    public Guid CorrelationId { get; } = Guid.NewGuid();

    public Guid? ProjectId { get; set; }

    public Guid? SessionId { get; set; }

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public static IDisposable BeginScope(Guid? projectId = null, Guid? sessionId = null)
    {
        var previous = Current;
        Current = new InspectionContext
        {
            ProjectId = projectId,
            SessionId = sessionId
        };

        return new ContextScope(previous);
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly InspectionContext? _previousContext;
        private bool _disposed;

        public ContextScope(InspectionContext? previousContext)
        {
            _previousContext = previousContext;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Current = _previousContext;
            _disposed = true;
        }
    }
}

public static class InspectionLoggingExtensions
{
    public static IDisposable? BeginInspectionScope(
        this ILogger logger,
        Guid correlationId,
        Guid? projectId = null,
        Guid? sessionId = null)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["ProjectId"] = projectId,
            ["SessionId"] = sessionId
        });
    }
}
