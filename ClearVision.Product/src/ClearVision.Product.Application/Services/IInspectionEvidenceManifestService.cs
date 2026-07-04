using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Application.Services;

public interface IInspectionEvidenceManifestService
{
    Task CaptureAsync(InspectionResult result, CancellationToken cancellationToken = default);

    Task<InspectionEvidenceSummary> GetSummaryAsync(
        InspectionHistoryDetail result,
        CancellationToken cancellationToken = default);

    Task<InspectionEvidenceManifestReadResult> GetManifestAsync(
        Guid projectId,
        Guid resultId,
        CancellationToken cancellationToken = default);

    Task<InspectionEvidenceExportResult> ExportAsync(
        Guid projectId,
        Guid resultId,
        CancellationToken cancellationToken = default);

    Task<InspectionEvidenceRetentionCleanupResult> ApplyRetentionAsync(CancellationToken cancellationToken = default);
}

public sealed class NullInspectionEvidenceManifestService : IInspectionEvidenceManifestService
{
    public static NullInspectionEvidenceManifestService Instance { get; } = new();

    private NullInspectionEvidenceManifestService()
    {
    }

    public Task CaptureAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<InspectionEvidenceSummary> GetSummaryAsync(
        InspectionHistoryDetail result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InspectionEvidenceSummary
        {
            HasEvidenceManifest = false,
            EvidenceStatus = "disabled",
            Message = "Evidence manifest capture is disabled."
        });
    }

    public Task<InspectionEvidenceManifestReadResult> GetManifestAsync(
        Guid projectId,
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InspectionEvidenceManifestReadResult
        {
            Found = false,
            Status = "disabled",
            ErrorCode = "EvidenceDisabled",
            Message = "Evidence manifest capture is disabled.",
            Summary = new InspectionEvidenceSummary
            {
                EvidenceStatus = "disabled",
                Message = "Evidence manifest capture is disabled."
            }
        });
    }

    public Task<InspectionEvidenceExportResult> ExportAsync(
        Guid projectId,
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InspectionEvidenceExportResult
        {
            Success = false,
            Status = "disabled",
            ErrorCode = "EvidenceDisabled",
            Message = "Evidence manifest capture is disabled."
        });
    }

    public Task<InspectionEvidenceRetentionCleanupResult> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InspectionEvidenceRetentionCleanupResult());
    }
}
