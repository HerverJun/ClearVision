using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Application.Services;

public interface IInspectionImagePersistenceService
{
    Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default);
}

public sealed class NullInspectionImagePersistenceService : IInspectionImagePersistenceService
{
    public static NullInspectionImagePersistenceService Instance { get; } = new();

    private NullInspectionImagePersistenceService()
    {
    }

    public Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
