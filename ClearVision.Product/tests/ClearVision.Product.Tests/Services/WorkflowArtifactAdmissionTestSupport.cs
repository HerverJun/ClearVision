using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Tests.Services;

internal static class WorkflowArtifactAdmissionTestSupport
{
    public static IWorkflowArtifactAdmissionGate CreateGate()
    {
        var factory = new OperatorFactory();
        return new WorkflowArtifactAdmissionGate(
            new WorkflowLegacyScanner(factory),
            new WorkflowLegacyRepairService(factory),
            new DiscardingQuarantineStore());
    }

    private sealed class DiscardingQuarantineStore : IWorkflowArtifactQuarantineStore
    {
        public void Preserve(WorkflowArtifactQuarantineRecord record)
        {
        }
    }
}
