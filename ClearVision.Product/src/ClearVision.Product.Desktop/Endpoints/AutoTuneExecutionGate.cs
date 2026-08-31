namespace ClearVision.Product.Desktop.Endpoints;

/// <summary>
/// Process-local hard limits for retained AutoTune execution surfaces.
/// Admission is non-blocking so a saturated caller cannot grow a waiter queue.
/// </summary>
public sealed class AutoTuneExecutionGate
{
    public const int MinimumIterations = 1;
    public const int MaximumIterations = 5;

    private const int DefaultGlobalConcurrency = 2;
    private const int DefaultPerPrincipalConcurrency = 1;
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly Dictionary<string, int> _activeByPrincipal = new(StringComparer.Ordinal);
    private readonly int _globalConcurrency;
    private readonly int _perPrincipalConcurrency;
    private int _activeCount;

    public AutoTuneExecutionGate()
        : this(DefaultGlobalConcurrency, DefaultPerPrincipalConcurrency, DefaultDeadline)
    {
    }

    internal AutoTuneExecutionGate(
        int globalConcurrency,
        int perPrincipalConcurrency,
        TimeSpan deadline)
    {
        if (globalConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(globalConcurrency));
        }

        if (perPrincipalConcurrency <= 0 || perPrincipalConcurrency > globalConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(perPrincipalConcurrency));
        }

        if (deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline));
        }

        _globalConcurrency = globalConcurrency;
        _perPrincipalConcurrency = perPrincipalConcurrency;
        Deadline = deadline;
    }

    public TimeSpan Deadline { get; }

    public static bool IsIterationCountAllowed(int iterations) =>
        iterations is >= MinimumIterations and <= MaximumIterations;

    public AutoTuneExecutionLease? TryAcquire(string principalId)
    {
        if (string.IsNullOrWhiteSpace(principalId))
        {
            return null;
        }

        var canonicalPrincipal = principalId.Trim();
        lock (_sync)
        {
            _activeByPrincipal.TryGetValue(canonicalPrincipal, out var principalCount);
            if (_activeCount >= _globalConcurrency || principalCount >= _perPrincipalConcurrency)
            {
                return null;
            }

            _activeCount++;
            _activeByPrincipal[canonicalPrincipal] = principalCount + 1;
            return new AutoTuneExecutionLease(this, canonicalPrincipal);
        }
    }

    public CancellationTokenSource CreateDeadlineSource(CancellationToken requestAborted)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        source.CancelAfter(Deadline);
        return source;
    }

    internal int ActiveCount
    {
        get
        {
            lock (_sync)
            {
                return _activeCount;
            }
        }
    }

    private void Release(string principalId)
    {
        lock (_sync)
        {
            if (!_activeByPrincipal.TryGetValue(principalId, out var principalCount) || principalCount <= 0)
            {
                return;
            }

            _activeCount--;
            if (principalCount == 1)
            {
                _activeByPrincipal.Remove(principalId);
            }
            else
            {
                _activeByPrincipal[principalId] = principalCount - 1;
            }
        }
    }

    public sealed class AutoTuneExecutionLease : IDisposable
    {
        private AutoTuneExecutionGate? _owner;
        private readonly string _principalId;

        internal AutoTuneExecutionLease(AutoTuneExecutionGate owner, string principalId)
        {
            _owner = owner;
            _principalId = principalId;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_principalId);
        }
    }
}
