using System.Text.Json;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.ProjectVariables;

public sealed class ProjectVariableSessionTests
{
    [Fact]
    public void SetValue_WhenCalledAcrossRuns_KeepsCurrentValueAndIncrementsVersion()
    {
        var variableId = Guid.NewGuid();
        using var session = new ProjectVariableSession(new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.ng_count",
                    DisplayName = "NG Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(0)
                }
            ]
        });

        session.SetValue(variableId, 3L, ProjectVariableUpdatedBy.VariableWrite, Guid.NewGuid());
        var snapshot = session.Increment(variableId, 2, ProjectVariableUpdatedBy.VariableIncrement, Guid.NewGuid());

        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(5L);
        snapshot.Version.Should().Be(2);
    }

    [Fact]
    public void SetValue_WhenComplexObjectProvided_RejectsValue()
    {
        var variableId = Guid.NewGuid();
        using var session = new ProjectVariableSession(new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "global.text",
                    DisplayName = "Text",
                    ValueType = ProjectGlobalVariableValueType.String,
                    InitialValue = JsonSerializer.SerializeToElement("")
                }
            ]
        });

        var act = () => session.SetValue(variableId, new { Large = "object" }, ProjectVariableUpdatedBy.OperatorOutput);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateSnapshotClone_ShouldCopyCurrentValuesAndVersions()
    {
        var variableId = Guid.NewGuid();
        using var session = new ProjectVariableSession(CreateSchema(variableId, 1));
        session.SetValue(variableId, 5L, ProjectVariableUpdatedBy.StudioManual);

        using var clone = session.CreateSnapshotClone();
        clone.SetValue(variableId, 7L, ProjectVariableUpdatedBy.VariableWrite);

        session.TryGetSnapshot(variableId, out var original).Should().BeTrue();
        clone.TryGetSnapshot(variableId, out var cloned).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(original.Value).Should().Be(5L);
        original.Version.Should().Be(1);
        ProjectVariableValueConverter.ToObject(cloned.Value).Should().Be(7L);
        cloned.Version.Should().Be(2);
    }

    [Fact]
    public async Task IncrementAtomic_WhenCalledConcurrently_ShouldKeepExactValueAndVersion()
    {
        var variableId = Guid.NewGuid();
        using var session = new ProjectVariableSession(CreateSchema(variableId, 0));

        await Task.WhenAll(Enumerable.Range(0, 1000)
            .Select(_ => Task.Run(() => session.IncrementAtomic(variableId, 1, ProjectVariableUpdatedBy.VariableIncrement))));

        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(1000L);
        snapshot.Version.Should().Be(1000);
    }

    [Fact]
    public void IncrementAtomic_ShouldSupportNegativeDeltaRangeResetAndOverflow()
    {
        var variableId = Guid.NewGuid();
        using var session = new ProjectVariableSession(new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(10L),
                    Min = -5,
                    Max = 20
                }
            ]
        });

        session.IncrementAtomic(variableId, -3, ProjectVariableUpdatedBy.VariableIncrement).NewValue.Should().Be(7L);
        var reset = session.IncrementAtomic(
            variableId,
            2,
            ProjectVariableUpdatedBy.VariableIncrement,
            resetCondition: "GreaterThan",
            resetThreshold: 5,
            resetValue: 0);
        reset.WasReset.Should().BeTrue();
        reset.NewValue.Should().Be(2L);

        var range = () => session.IncrementAtomic(variableId, -100, ProjectVariableUpdatedBy.VariableIncrement);
        range.Should().Throw<InvalidOperationException>().WithMessage("*below Min*");

        var overflowVariableId = Guid.NewGuid();
        using var overflowSession = new ProjectVariableSession(CreateSchema(overflowVariableId, long.MaxValue));
        var overflow = () => overflowSession.IncrementAtomic(overflowVariableId, 1, ProjectVariableUpdatedBy.VariableIncrement);
        overflow.Should().Throw<OverflowException>();
    }

    [Fact]
    public async Task RegistryReplace_ShouldPublishNewSessionWithoutDisposingOldReferences()
    {
        var registry = new ProjectVariableSessionRegistry();
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var oldSession = registry.GetOrCreate(projectId, CreateSchema(variableId, 1));
        oldSession.SetValue(variableId, 3L, ProjectVariableUpdatedBy.StudioManual);

        var newSession = registry.Replace(projectId, CreateSchema(variableId, 5));

        registry.GetOrCreate(projectId, CreateSchema(variableId, 9)).Should().BeSameAs(newSession);
        oldSession.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        oldSession.TryGetValue(variableId, out var oldValue).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(oldValue).Should().Be(4L);
        newSession.TryGetValue(variableId, out var newValue).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(newValue).Should().Be(5L);

        var tasks = Enumerable.Range(0, 200).Select(index => Task.Run(() =>
        {
            if (index % 3 == 0)
            {
                registry.Replace(projectId, CreateSchema(variableId, index));
            }
            else
            {
                var session = registry.GetOrCreate(projectId, CreateSchema(variableId, index));
                session.SetValue(variableId, index, ProjectVariableUpdatedBy.StudioManual);
            }
        }));

        await Task.WhenAll(tasks);
        registry.GetOrCreate(projectId, CreateSchema(variableId, 0))
            .TryGetValue(variableId, out _)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void RegistryTryRemove_ShouldNotDisposeActiveReference()
    {
        var registry = new ProjectVariableSessionRegistry();
        var variableId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var session = registry.GetOrCreate(projectId, CreateSchema(variableId, 1));

        registry.TryRemove(projectId).Should().BeTrue();

        session.SetValue(variableId, 2L, ProjectVariableUpdatedBy.StudioManual);
        session.TryGetValue(variableId, out var value).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(value).Should().Be(2L);
    }

    [Fact]
    public void Registry_WhenStateStoreContainsSnapshot_ShouldLoadCurrentValueAcrossRegistryInstances()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var projectId = Guid.NewGuid();
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var firstRegistry = new ProjectVariableSessionRegistry(new JsonFileProjectVariableStateStore(root));
            var firstSession = firstRegistry.GetOrCreate(projectId, schema);
            firstSession.SetValue(variableId, long.MaxValue, ProjectVariableUpdatedBy.StudioManual);
            firstRegistry.Save(projectId, firstSession);

            var secondRegistry = new ProjectVariableSessionRegistry(new JsonFileProjectVariableStateStore(root));
            var secondSession = secondRegistry.GetOrCreate(projectId, CreateSchema(variableId, 1));

            secondSession.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
            ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(long.MaxValue);
            snapshot.Version.Should().Be(1);
            snapshot.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };
    }
}
