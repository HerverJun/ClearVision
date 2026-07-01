using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Core.Services;

public interface IVisionAgentBuildApplicationService
{
    Task<VisionAgentBuildReadinessPreviewResult> PreviewBuildReadinessAsync(
        VisionAgentBuildReadinessPreviewRequest request,
        CancellationToken cancellationToken);

    Task<CanonicalBuildOutcome> BuildAsync(
        BuildCommand command,
        CancellationToken cancellationToken);
}
