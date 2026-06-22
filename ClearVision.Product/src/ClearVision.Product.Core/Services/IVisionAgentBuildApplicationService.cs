using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Core.Services;

public interface IVisionAgentBuildApplicationService
{
    Task<CanonicalBuildOutcome> BuildAsync(
        BuildCommand command,
        CancellationToken cancellationToken);
}
