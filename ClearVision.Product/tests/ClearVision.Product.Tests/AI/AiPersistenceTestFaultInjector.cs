using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Tests.AI;

internal sealed class AiPersistenceTestFaultInjector : IAiPersistenceFaultInjector
{
    private readonly object _gate = new();
    private Action<AiPersistenceStage, string, string>? _handler;

    public void SetHandler(Action<AiPersistenceStage, string, string>? handler)
    {
        lock (_gate)
        {
            _handler = handler;
        }
    }

    public void FailOnce(AiPersistenceStage targetStage, Func<Exception> exceptionFactory)
    {
        var fired = 0;
        SetHandler((stage, _, _) =>
        {
            if (stage == targetStage && Interlocked.Exchange(ref fired, 1) == 0)
            {
                throw exceptionFactory();
            }
        });
    }

    public void OnStage(AiPersistenceStage stage, string authority, string path)
    {
        Action<AiPersistenceStage, string, string>? handler;
        lock (_gate)
        {
            handler = _handler;
        }

        handler?.Invoke(stage, authority, path);
    }
}
