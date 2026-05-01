using System.Diagnostics;
using Acme.Product.Core.DTOs;

namespace Acme.Product.Infrastructure.AI;

public sealed class AiGenerationPipelineContext
{
    private readonly List<AiGenerationStageDiagnostic> _timeline = new();

    public IReadOnlyList<AiGenerationStageDiagnostic> Timeline => _timeline;

    public void AddStage(
        string stage,
        string status,
        string summary,
        TimeSpan duration,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        _timeline.Add(new AiGenerationStageDiagnostic
        {
            Stage = stage,
            Status = status,
            Summary = summary,
            DurationMs = Math.Max(0, (long)duration.TotalMilliseconds),
            Metadata = metadata?.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>()
        });
    }

    public T Measure<T>(
        string stage,
        Func<T> action,
        Func<T, string> summary,
        Func<T, IReadOnlyDictionary<string, string>>? metadata = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = action();
            AddStage(stage, "completed", summary(result), sw.Elapsed, metadata?.Invoke(result));
            return result;
        }
        catch (Exception ex)
        {
            AddStage(stage, "failed", ex.Message, sw.Elapsed);
            throw;
        }
    }

    public async Task<T> MeasureAsync<T>(
        string stage,
        Func<Task<T>> action,
        Func<T, string> summary,
        Func<T, IReadOnlyDictionary<string, string>>? metadata = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            AddStage(stage, "completed", summary(result), sw.Elapsed, metadata?.Invoke(result));
            return result;
        }
        catch (Exception ex)
        {
            AddStage(stage, "failed", ex.Message, sw.Elapsed);
            throw;
        }
    }
}
