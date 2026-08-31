using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.DependencyInjection;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public sealed class FileOperatorDispatchJournalTests
{
    [Fact]
    public async Task RestartWithPendingDispatch_ShouldPersistIndeterminateWithoutRawAuthorityValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClearVisionDispatchJournalTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "dispatch.jsonl");
        const string rawSecretTarget = "postgres://admin:secret@database.internal/vision";
        try
        {
            var fingerprint = OperatorDispatchIdentity.CreateResourceBindingFingerprint(
                new Dictionary<string, string>
                {
                    ["DatabaseProfile"] = rawSecretTarget,
                    ["Token"] = "raw-token-must-not-be-written"
                });
            var identity = new OperatorDispatchIdentity(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ExecutionSnapshotSource.PersistedProject,
                ExecutionRunMode.FormalPrimary,
                ExecutionSideEffect.NetworkWrite,
                fingerprint);
            var writer = new FileOperatorDispatchJournal(path);
            var prepared = await writer.PrepareAsync(identity);
            await writer.MarkDispatchedAsync(prepared.CorrelationId);

            var recovered = new FileOperatorDispatchJournal(path);
            var entry = await recovered.TryGetAsync(prepared.CorrelationId);

            entry.Should().NotBeNull();
            entry!.Stage.Should().Be(OperatorDispatchStage.Dispatched);
            entry.Outcome.Should().Be(OperatorDispatchOutcome.Indeterminate);
            entry.FailureCode.Should().Be("PROCESS_RESTART_OUTCOME_UNKNOWN");
            entry.ResourceBindingFingerprint.Should().Be(fingerprint);
            var persisted = await File.ReadAllTextAsync(path);
            persisted.Should().NotContain(rawSecretTarget);
            persisted.Should().NotContain("raw-token-must-not-be-written");
            persisted.Should().NotContain("admin:secret");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CoreRegistration_ShouldDefaultToDurableJournalAndPreserveInjectedFake()
    {
        var defaults = new ServiceCollection();
        defaults.AddVisionRuntimeCoreServices();
        using var defaultProvider = defaults.BuildServiceProvider();
        defaultProvider.GetRequiredService<IOperatorDispatchJournal>()
            .Should().BeOfType<FileOperatorDispatchJournal>();

        var fake = new InMemoryOperatorDispatchJournal();
        var overridden = new ServiceCollection();
        overridden.AddSingleton<IOperatorDispatchJournal>(fake);
        overridden.AddVisionRuntimeCoreServices();
        using var overriddenProvider = overridden.BuildServiceProvider();
        overriddenProvider.GetRequiredService<IOperatorDispatchJournal>()
            .Should().BeSameAs(fake);
    }
}
