using System.Diagnostics;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class VisionAgentPlanningDeadlineOptions
{
    public const string SectionName = "AI:VisionAgent:PlanningDeadline";
    public const string ContractVersion = "v1";

    public int TotalBudgetMs { get; set; } = 120_000;
    public int ClientNetworkMarginMs { get; set; } = 15_000;
    public int MinimumRepairBudgetMs { get; set; } = 5_000;

    public VisionAgentPlanningDeadlineOptions Normalize()
    {
        TotalBudgetMs = Math.Clamp(TotalBudgetMs, 1_000, 300_000);
        ClientNetworkMarginMs = Math.Clamp(ClientNetworkMarginMs, 1_000, 60_000);
        MinimumRepairBudgetMs = Math.Clamp(MinimumRepairBudgetMs, 500, TotalBudgetMs);
        return this;
    }
}

public sealed class VisionAgentPlanningDeadlineExceededException : TimeoutException
{
    public VisionAgentPlanningDeadlineExceededException(string stage)
        : base($"Vision Agent planning deadline exceeded during {stage}.")
    {
        Stage = stage;
    }

    public string Stage { get; }
}

internal sealed class VisionAgentPlanningDeadline : IDisposable
{
    private static readonly AsyncLocal<VisionAgentPlanningDeadline?> Ambient = new();
    private readonly VisionAgentPlanningDeadline? _previous;
    private readonly CancellationToken _callerToken;
    private readonly CancellationTokenSource _totalCancellation;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly int _totalBudgetMs;
    private bool _disposed;

    private VisionAgentPlanningDeadline(
        VisionAgentPlanningDeadlineOptions options,
        CancellationToken callerToken,
        int requestedBudgetMs)
    {
        Options = options.Normalize();
        _callerToken = callerToken;
        _totalBudgetMs = requestedBudgetMs > 0
            ? Math.Min(Options.TotalBudgetMs, requestedBudgetMs)
            : Options.TotalBudgetMs;
        _totalCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        _totalCancellation.CancelAfter(Math.Max(1, _totalBudgetMs));
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static VisionAgentPlanningDeadline? Current => Ambient.Value;
    public VisionAgentPlanningDeadlineOptions Options { get; }
    public CancellationToken Token => _totalCancellation.Token;
    public int RemainingMs => Math.Max(0, _totalBudgetMs - (int)_stopwatch.ElapsedMilliseconds);
    public bool TotalBudgetExceeded => !_callerToken.IsCancellationRequested &&
                                       (_totalCancellation.IsCancellationRequested || RemainingMs <= 0);

    public static VisionAgentPlanningDeadline Begin(
        VisionAgentPlanningDeadlineOptions options,
        CancellationToken callerToken,
        int requestedBudgetMs = 0) =>
        new(options, callerToken, requestedBudgetMs);

    public CancellationTokenSource CreateStageCancellation(int maximumStageMs)
    {
        ThrowIfExpired("stage_start");
        var stage = CancellationTokenSource.CreateLinkedTokenSource(Token);
        stage.CancelAfter(Math.Max(1, Math.Min(maximumStageMs, RemainingMs)));
        return stage;
    }

    public void ThrowIfExpired(string stage)
    {
        _callerToken.ThrowIfCancellationRequested();
        if (TotalBudgetExceeded)
            throw new VisionAgentPlanningDeadlineExceededException(stage);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        _totalCancellation.Dispose();
        Ambient.Value = _previous;
    }
}
