using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Tests;

internal static class TestExecutionAuthorityScope
{
    public static IDisposable Enter(
        ExecutionSnapshotSource source = ExecutionSnapshotSource.PersistedProject,
        ExecutionRunMode runMode = ExecutionRunMode.FormalPrimary) =>
        ExecutionAuthorityContext.Enter(new ExecutionAuthorityScope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            source,
            runMode,
            Guid.NewGuid().ToString("N"),
            new ExecutionPrincipal("test-engineer", "Test Engineer", "Engineer", true),
            new Dictionary<string, string>()));
}
