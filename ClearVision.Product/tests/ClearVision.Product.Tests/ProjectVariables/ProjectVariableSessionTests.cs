using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;

namespace ClearVision.Product.Tests.ProjectVariables;

[TestClassification(TestDomain.Data, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "data-platform")]
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
    public async Task RegistryTryPublishSchemaAndPersist_ShouldMigrateCurrentValueWithoutDisposingOldReferences()
    {
        var registry = new ProjectVariableSessionRegistry();
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var oldSession = registry.GetOrCreate(projectId, CreateSchema(variableId, 1));
        oldSession.SetValue(variableId, 3L, ProjectVariableUpdatedBy.StudioManual);

        registry.TryPublishSchemaAndPersist(projectId, CreateSchema(variableId, 5), out var newSession, out var error)
            .Should()
            .BeTrue(error);

        registry.GetOrCreate(projectId, CreateSchema(variableId, 9)).Should().BeSameAs(newSession);
        oldSession.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        oldSession.TryGetValue(variableId, out var oldValue).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(oldValue).Should().Be(4L);
        newSession.TryGetValue(variableId, out var newValue).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(newValue).Should().Be(3L);

        var tasks = Enumerable.Range(0, 200).Select(index => Task.Run(() =>
        {
            if (index % 3 == 0)
            {
                registry.TryPublishSchemaAndPersist(projectId, CreateSchema(variableId, index), out _, out _);
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
    public void RegistryTryPublishSchemaAndPersist_WhenSchemaHashUnchanged_ShouldReuseSessionAndSkipReset()
    {
        var registry = new ProjectVariableSessionRegistry();
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var session = registry.GetOrCreate(projectId, CreateSchema(variableId, 1));
        session.SetValue(variableId, 128L, ProjectVariableUpdatedBy.StudioManual);

        registry.TryPublishSchemaAndPersist(projectId, CreateSchema(variableId, 1), out var after, out var error)
            .Should()
            .BeTrue(error);

        after.Should().BeSameAs(session);
        after.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(128L);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public void RegistryTryPublishSchemaAndPersist_WhenCurrentValueIsIncompatible_ShouldResetWithMigrationMetadata()
    {
        var registry = new ProjectVariableSessionRegistry();
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var session = registry.GetOrCreate(projectId, CreateSchema(variableId, 1));
        session.SetValue(variableId, 10L, ProjectVariableUpdatedBy.StudioManual);

        registry.TryPublishSchemaAndPersist(
                projectId,
                CreateSchema(variableId, 25, min: 20),
                out var migrated,
                out var error)
            .Should()
            .BeTrue(error);

        migrated.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(25L);
        snapshot.Version.Should().Be(2);
        snapshot.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.Reset);
        snapshot.RunId.Should().BeNull();
        snapshot.OperatorId.Should().BeNull();
    }

    [Fact]
    public void RegistryTryCommitAndPersist_WhenSaveFails_ShouldKeepAuthoritativeMemoryAndStoredState()
    {
        var store = new RecordingStateStore();
        var registry = new ProjectVariableSessionRegistry(store);
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var schema = CreateSchema(variableId, 1);
        registry.TryMutateAndPersist(
                projectId,
                schema,
                session => session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual),
                out var authoritative,
                out var seedError)
            .Should()
            .BeTrue(seedError);
        var expectedVersions = authoritative.GetSnapshots()
            .ToDictionary(snapshot => snapshot.VariableId, snapshot => snapshot.Version);
        using var working = authoritative.CreateSnapshotClone();
        working.SetValue(variableId, 12L, ProjectVariableUpdatedBy.OperatorOutput);
        store.FailSaves = true;

        var committed = registry.TryCommitAndPersist(projectId, working, expectedVersions, out var current, out var error);

        committed.Should().BeFalse();
        error.Should().Contain("GV030");
        current.Should().BeSameAs(authoritative);
        registry.GetOrCreate(projectId, schema).TryGetSnapshot(variableId, out var memorySnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(memorySnapshot.Value).Should().Be(4L);
        memorySnapshot.Version.Should().Be(1);
        var storedSnapshot = store.SavedSnapshots.Should().ContainSingle().Subject;
        ProjectVariableValueConverter.ToObject(storedSnapshot.Value).Should().Be(4L);
        storedSnapshot.Version.Should().Be(1);
    }

    [Fact]
    public void RegistryTryMutateAndPersist_WhenStateDirectoryIsReadOnly_ShouldReturnGv030AndKeepAuthoritativeState()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableReadOnlyRegistry", Guid.NewGuid().ToString("N"));
        try
        {
            var fileSystem = new PhaseFailingProjectVariableStateFileSystem();
            var store = new JsonFileProjectVariableStateStore(root, fileSystem);
            var registry = new ProjectVariableSessionRegistry(store);
            var projectId = Guid.NewGuid();
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            registry.TryMutateAndPersist(
                    projectId,
                    schema,
                    session => session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual),
                    out var authoritative,
                    out var seedError)
                .Should()
                .BeTrue(seedError);

            fileSystem.FailCreateDirectories = true;
            var mutated = registry.TryMutateAndPersist(
                projectId,
                schema,
                session => session.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual),
                out var current,
                out var error);

            mutated.Should().BeFalse();
            error.Should().Contain("GV030");
            current.Should().BeSameAs(authoritative);
            fileSystem.FailCreateDirectories = false;
            LoadLongAndVersion(store, ProjectVariableSessionRegistry.ToProjectScopeId(projectId), schema, variableId)
                .Should()
                .Be((4L, 1L));
            registry.GetOrCreate(projectId, schema).TryGetSnapshot(variableId, out var memorySnapshot).Should().BeTrue();
            ProjectVariableValueConverter.ToObject(memorySnapshot.Value).Should().Be(4L);
            memorySnapshot.Version.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RegistryTryMutateAndPersist_WhenSchemaGenerationIsStale_ShouldRejectBeforeChangingState()
    {
        var store = new RecordingStateStore();
        var registry = new ProjectVariableSessionRegistry(store);
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var authoritativeSchema = CreateSchema(variableId, 1);
        var staleCallerSchema = CreateSchema(variableId, 5);
        var authoritative = registry.GetOrCreate(projectId, authoritativeSchema);
        authoritative.SetValue(variableId, 7L, ProjectVariableUpdatedBy.StudioManual);

        var mutated = registry.TryMutateAndPersist(
            projectId,
            staleCallerSchema,
            session => session.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual),
            out var current,
            out var error);

        mutated.Should().BeFalse();
        error.Should().Contain("GV025");
        current.Should().BeSameAs(authoritative);
        store.SavedSnapshots.Should().BeEmpty();
        registry.GetOrCreate(projectId, authoritativeSchema).TryGetSnapshot(variableId, out var memorySnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(memorySnapshot.Value).Should().Be(7L);
        memorySnapshot.Version.Should().Be(1);
    }

    [Fact]
    public void RegistryTryMutateAndPersist_WhenExpectedVersionIsStale_ShouldRejectBeforeChangingState()
    {
        var store = new RecordingStateStore();
        var registry = new ProjectVariableSessionRegistry(store);
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var schema = CreateSchema(variableId, 1);
        registry.TryMutateAndPersist(
                projectId,
                schema,
                session => session.SetValue(variableId, 7L, ProjectVariableUpdatedBy.StudioManual),
                out var authoritative,
                out var seedError)
            .Should()
            .BeTrue(seedError);

        var mutated = registry.TryMutateAndPersist(
            projectId,
            schema,
            session => session.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual),
            new Dictionary<Guid, long> { [variableId] = 0 },
            out var current,
            out var error);

        mutated.Should().BeFalse();
        error.Should().Contain("GV025").And.Contain("changed from version 0 to 1");
        current.Should().BeSameAs(authoritative);
        registry.GetOrCreate(projectId, schema).TryGetSnapshot(variableId, out var memorySnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(memorySnapshot.Value).Should().Be(7L);
        memorySnapshot.Version.Should().Be(1);
        var storedSnapshot = store.SavedSnapshots.Should().ContainSingle().Subject;
        ProjectVariableValueConverter.ToObject(storedSnapshot.Value).Should().Be(7L);
        storedSnapshot.Version.Should().Be(1);
    }

    [Fact]
    public void RegistryTryCommitAndPersist_WhenSaveSucceeds_ShouldPublishPersistedCandidate()
    {
        var store = new RecordingStateStore();
        var registry = new ProjectVariableSessionRegistry(store);
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var schema = CreateSchema(variableId, 1);
        var authoritative = registry.GetOrCreate(projectId, schema);
        var expectedVersions = authoritative.GetSnapshots()
            .ToDictionary(snapshot => snapshot.VariableId, snapshot => snapshot.Version);
        using var working = authoritative.CreateSnapshotClone();
        working.SetValue(variableId, 12L, ProjectVariableUpdatedBy.OperatorOutput);

        var committed = registry.TryCommitAndPersist(projectId, working, expectedVersions, out var published, out var error);

        committed.Should().BeTrue(error);
        registry.GetOrCreate(projectId, schema).Should().BeSameAs(published);
        published.TryGetSnapshot(variableId, out var memorySnapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(memorySnapshot.Value).Should().Be(12L);
        memorySnapshot.Version.Should().Be(1);
        var storedSnapshot = store.SavedSnapshots.Should().ContainSingle().Subject;
        ProjectVariableValueConverter.ToObject(storedSnapshot.Value).Should().Be(12L);
        storedSnapshot.Version.Should().Be(1);
    }

    [Fact]
    public void RegistryTryCommitAndPersist_WhenSchemaGenerationChangedBackToSameHash_ShouldRejectStaleWorkingCopy()
    {
        var registry = new ProjectVariableSessionRegistry();
        var projectId = Guid.NewGuid();
        var variableId = Guid.NewGuid();
        var schemaA = CreateSchema(variableId, 1);
        var schemaB = CreateSchema(variableId, 2);
        var authoritative = registry.GetOrCreate(projectId, schemaA);
        authoritative.SchemaGeneration.Should().Be(0);
        var expectedVersions = authoritative.GetSnapshots()
            .ToDictionary(snapshot => snapshot.VariableId, snapshot => snapshot.Version);
        using var staleWorking = authoritative.CreateSnapshotClone();
        staleWorking.SetValue(variableId, 12L, ProjectVariableUpdatedBy.OperatorOutput);

        registry.TryPublishSchemaAndPersist(projectId, schemaB, out var schemaBSession, out var schemaBError)
            .Should()
            .BeTrue(schemaBError);
        schemaBSession.SchemaGeneration.Should().Be(1);
        registry.TryPublishSchemaAndPersist(projectId, schemaA, out var schemaASession, out var schemaAError)
            .Should()
            .BeTrue(schemaAError);
        schemaASession.SchemaGeneration.Should().Be(2);

        var committed = registry.TryCommitAndPersist(projectId, staleWorking, expectedVersions, out var current, out var error);

        committed.Should().BeFalse();
        error.Should().Contain("GV025").And.Contain("schema generation");
        current.Should().BeSameAs(schemaASession);
        current.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(1L);
        snapshot.Version.Should().Be(0);
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
            firstRegistry.TryMutateAndPersist(
                    projectId,
                    schema,
                    session => session.SetValue(variableId, long.MaxValue, ProjectVariableUpdatedBy.StudioManual),
                    out _,
                    out var error)
                .Should()
                .BeTrue(error);

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

    [Fact]
    public void JsonFileStateStore_Load_WhenTempExistsAndMainIsMissing_ShouldPromoteInterruptedCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableTempRecovery", Guid.NewGuid().ToString("N"));
        try
        {
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var store = new JsonFileProjectVariableStateStore(root);
            store.Save(
                scopeId,
                schema,
                [new ProjectVariableValueSnapshot(variableId, JsonSerializer.SerializeToElement(42L), 1, DateTimeOffset.UtcNow, ProjectVariableUpdatedBy.StudioManual, null, null)]);
            var filePath = Directory.EnumerateFiles(root, "*.json").Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
            var tempPath = filePath + ".tmp";
            File.Move(filePath, tempPath);

            var snapshots = store.Load(scopeId, schema);

            snapshots.Should().ContainSingle().Which.Version.Should().Be(1);
            File.Exists(filePath).Should().BeTrue();
            File.Exists(tempPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenInterruptedCommitSchemaMismatches_ShouldDiscardTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableTempSchemaMismatch", Guid.NewGuid().ToString("N"));
        try
        {
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var oldSchema = CreateSchema(variableId, 1);
            var requestedSchema = CreateSchema(variableId, 2);
            var store = new JsonFileProjectVariableStateStore(root);
            store.Save(
                scopeId,
                oldSchema,
                [new ProjectVariableValueSnapshot(variableId, JsonSerializer.SerializeToElement(42L), 1, DateTimeOffset.UtcNow, ProjectVariableUpdatedBy.StudioManual, null, null)]);
            var filePath = Directory.EnumerateFiles(root, "*.json").Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
            var tempPath = filePath + ".tmp";
            File.Move(filePath, tempPath);

            var restartedStore = new JsonFileProjectVariableStateStore(root);
            var snapshots = restartedStore.Load(scopeId, requestedSchema);

            snapshots.Should().BeEmpty();
            File.Exists(filePath).Should().BeFalse();
            File.Exists(tempPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenMainAndTempExist_ShouldKeepCommittedFileAndDeleteTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableTempDiscard", Guid.NewGuid().ToString("N"));
        try
        {
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var store = new JsonFileProjectVariableStateStore(root);
            store.Save(
                scopeId,
                schema,
                [new ProjectVariableValueSnapshot(variableId, JsonSerializer.SerializeToElement(42L), 1, DateTimeOffset.UtcNow, ProjectVariableUpdatedBy.StudioManual, null, null)]);
            var filePath = Directory.EnumerateFiles(root, "*.json").Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, File.ReadAllText(filePath));

            var snapshots = store.Load(scopeId, schema);

            snapshots.Should().ContainSingle().Which.Version.Should().Be(1);
            File.Exists(filePath).Should().BeTrue();
            File.Exists(tempPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenLastGoodMatchesRequestedSchema_ShouldPreferLastGoodOverMismatchedCurrent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableSchemaHashRecovery", Guid.NewGuid().ToString("N"));
        try
        {
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var oldSchema = CreateSchema(variableId, 1);
            var newSchema = CreateSchema(variableId, 2);
            var store = new JsonFileProjectVariableStateStore(root);
            store.Save(
                scopeId,
                oldSchema,
                [new ProjectVariableValueSnapshot(variableId, JsonSerializer.SerializeToElement(3L), 1, DateTimeOffset.UtcNow, ProjectVariableUpdatedBy.StudioManual, null, null)]);
            store.Save(
                scopeId,
                newSchema,
                [new ProjectVariableValueSnapshot(variableId, JsonSerializer.SerializeToElement(9L), 2, DateTimeOffset.UtcNow, ProjectVariableUpdatedBy.StudioManual, null, null)]);

            var snapshots = store.Load(scopeId, oldSchema);

            using var session = new ProjectVariableSession(oldSchema, snapshots);
            session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
            ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(3L);
            snapshot.Version.Should().Be(1);
            LoadLongAndVersion(new JsonFileProjectVariableStateStore(root), scopeId, newSchema, variableId)
                .Should()
                .Be((9L, 2L));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenCurrentIsCorruptAndLastGoodMatches_ShouldRestoreCommittedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableCorruptRestore", Guid.NewGuid().ToString("N"));
        try
        {
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var store = new JsonFileProjectVariableStateStore(root);
            SaveSnapshot(store, scopeId, schema, variableId, 3L, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 9L, 2);
            var filePath = GetCommittedStateFilePath(root);
            var lastGoodPath = GetLastGoodStateFilePath(filePath);
            File.Exists(lastGoodPath).Should().BeTrue();
            File.WriteAllText(filePath, "{not-json", Encoding.UTF8);

            LoadLongAndVersion(store, scopeId, schema, variableId).Should().Be((3L, 1L));

            File.Exists(filePath + ".corrupt").Should().BeTrue();
            var recoveryJournal = ReadSingleRecoveryJournalEntry(GetRecoveryJournalPath(filePath));
            recoveryJournal.GetProperty("eventType").GetString().Should().Be("main-corrupt-restored-last-good");
            recoveryJournal.GetProperty("scopeId").GetString().Should().Be(scopeId);
            recoveryJournal.GetProperty("schemaHash").GetString().Should()
                .Be(ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema));
            recoveryJournal.GetProperty("sourcePath").GetString().Should().Be(lastGoodPath);
            recoveryJournal.GetProperty("targetPath").GetString().Should().Be(filePath);
            File.Delete(lastGoodPath);
            LoadLongAndVersion(new JsonFileProjectVariableStateStore(root), scopeId, schema, variableId)
                .Should()
                .Be((3L, 1L));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenCurrentSchemaMismatchesAndLegacyMatches_ShouldMigrateLegacyBySchemaHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableLegacySchemaHash", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var currentSchema = CreateSchema(variableId, 1);
            var requestedSchema = CreateSchema(variableId, 2);
            SaveSnapshot(new JsonFileProjectVariableStateStore(currentRoot), scopeId, currentSchema, variableId, 11L, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, requestedSchema, variableId, 22L, 2);

            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            LoadLongAndVersion(store, scopeId, requestedSchema, variableId).Should().Be((22L, 2L));
            LoadLongAndVersion(new JsonFileProjectVariableStateStore(currentRoot), scopeId, requestedSchema, variableId)
                .Should()
                .Be((22L, 2L));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenCurrentAndLegacyMatchSchema_ShouldMigrateHigherGenerationLegacy()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableLegacyGeneration", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(currentRoot), scopeId, schema, variableId, 11L, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 44L, 4);
            var legacyFilePath = GetCommittedStateFilePath(legacyRoot);
            ReadStateFileGeneration(GetCommittedStateFilePath(currentRoot)).Should().Be(1);
            ReadStateFileGeneration(legacyFilePath).Should().Be(4);

            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            LoadLongAndVersion(store, scopeId, schema, variableId).Should().Be((44L, 4L));
            var migratedFilePath = GetCommittedStateFilePath(currentRoot);
            ReadStateFileGeneration(migratedFilePath).Should().Be(4);
            var recoveryJournal = ReadSingleRecoveryJournalEntry(GetRecoveryJournalPath(migratedFilePath));
            recoveryJournal.GetProperty("eventType").GetString().Should().Be("legacy-state-migrated");
            recoveryJournal.GetProperty("sourcePath").GetString().Should().Be(legacyFilePath);
            recoveryJournal.GetProperty("targetPath").GetString().Should().Be(migratedFilePath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenCurrentIsCorruptAndLegacyMatches_ShouldMigrateLegacy()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableLegacyCorruptCurrent", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(currentRoot), scopeId, schema, variableId, 11L, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 33L, 3);
            var currentFilePath = Directory.EnumerateFiles(currentRoot, "*.json")
                .Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
            File.WriteAllText(currentFilePath, "{not-json", Encoding.UTF8);

            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            LoadLong(store, scopeId, schema, variableId).Should().Be(33L);
            File.Exists(currentFilePath + ".corrupt").Should().BeTrue();
            LoadLongAndVersion(new JsonFileProjectVariableStateStore(currentRoot), scopeId, schema, variableId)
                .Should()
                .Be((33L, 3L));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenLegacyMainSchemaMismatchesButLastGoodMatches_ShouldMigrateLastGood()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableLegacyLastGoodSchemaHash", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var oldSchema = CreateSchema(variableId, 1);
            var requestedSchema = CreateSchema(variableId, 2);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, requestedSchema, variableId, 44L, 4);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, oldSchema, variableId, 11L, 1);

            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            LoadLongAndVersion(store, scopeId, requestedSchema, variableId).Should().Be((44L, 4L));
            LoadLongAndVersion(new JsonFileProjectVariableStateStore(currentRoot), scopeId, requestedSchema, variableId)
                .Should()
                .Be((44L, 4L));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenLegacyMainIsCorruptButLastGoodMatches_ShouldMigrateLastGood()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableLegacyLastGoodCorrupt", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 55L, 5);
            var legacyFilePath = Directory.EnumerateFiles(legacyRoot, "*.json")
                .Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
            var legacyLastGoodPath = Path.Combine(
                legacyRoot,
                $"{Path.GetFileNameWithoutExtension(legacyFilePath)}.last-good.json");
            File.Copy(legacyFilePath, legacyLastGoodPath);
            File.WriteAllText(legacyFilePath, "{not-json", Encoding.UTF8);

            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            LoadLongAndVersion(store, scopeId, schema, variableId).Should().Be((55L, 5L));
            LoadLongAndVersion(new JsonFileProjectVariableStateStore(currentRoot), scopeId, schema, variableId)
                .Should()
                .Be((55L, 5L));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Save_WhenWriteTempFails_ShouldKeepCommittedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableWriteFailure", Guid.NewGuid().ToString("N"));
        try
        {
            var fileSystem = new PhaseFailingProjectVariableStateFileSystem();
            var store = new JsonFileProjectVariableStateStore(root, fileSystem);
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 3L, 1);

            fileSystem.FailWrites = true;
            var act = () => SaveSnapshot(store, scopeId, schema, variableId, 9L, 2);

            act.Should().Throw<IOException>().WithMessage("*write failed*");
            Directory.EnumerateFiles(root, "*.tmp").Should().BeEmpty();
            fileSystem.FailWrites = false;
            LoadLong(store, scopeId, schema, variableId).Should().Be(3L);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Save_WhenCreateDirectoryFails_ShouldKeepCommittedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableReadOnlyDirectory", Guid.NewGuid().ToString("N"));
        try
        {
            var fileSystem = new PhaseFailingProjectVariableStateFileSystem();
            var store = new JsonFileProjectVariableStateStore(root, fileSystem);
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 3L, 1);

            fileSystem.FailCreateDirectories = true;
            var act = () => SaveSnapshot(store, scopeId, schema, variableId, 9L, 2);

            act.Should().Throw<UnauthorizedAccessException>().WithMessage("*read-only*");
            fileSystem.FailCreateDirectories = false;
            LoadLongAndVersion(store, scopeId, schema, variableId).Should().Be((3L, 1L));
            Directory.EnumerateFiles(root, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Save_WhenLastGoodCopyFails_ShouldKeepCommittedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableCopyFailure", Guid.NewGuid().ToString("N"));
        try
        {
            var fileSystem = new PhaseFailingProjectVariableStateFileSystem();
            var store = new JsonFileProjectVariableStateStore(root, fileSystem);
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 3L, 1);

            fileSystem.FailCopies = true;
            var act = () => SaveSnapshot(store, scopeId, schema, variableId, 9L, 2);

            act.Should().Throw<IOException>().WithMessage("*copy failed*");
            Directory.EnumerateFiles(root, "*.tmp").Should().BeEmpty();
            fileSystem.FailCopies = false;
            LoadLong(store, scopeId, schema, variableId).Should().Be(3L);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Save_WhenMoveFails_ShouldKeepCommittedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableMoveFailure", Guid.NewGuid().ToString("N"));
        try
        {
            var fileSystem = new PhaseFailingProjectVariableStateFileSystem();
            var store = new JsonFileProjectVariableStateStore(root, fileSystem);
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 3L, 1);

            fileSystem.FailMoves = true;
            var act = () => SaveSnapshot(store, scopeId, schema, variableId, 9L, 2);

            act.Should().Throw<IOException>().WithMessage("*move failed*");
            Directory.EnumerateFiles(root, "*.tmp").Should().BeEmpty();
            fileSystem.FailMoves = false;
            LoadLong(store, scopeId, schema, variableId).Should().Be(3L);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Delete_ShouldRemoveCurrentLegacyAndRecoveryArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableDeleteCleanup", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(Guid.NewGuid());
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(currentRoot), scopeId, schema, variableId, 3L, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(currentRoot), scopeId, schema, variableId, 4L, 2);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 5L, 3);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 6L, 4);
            WriteRecoveryArtifacts(GetCommittedStateFilePath(currentRoot));
            WriteRecoveryArtifacts(GetCommittedStateFilePath(legacyRoot));

            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            store.Delete(scopeId);

            Directory.EnumerateFileSystemEntries(currentRoot).Should().BeEmpty();
            Directory.EnumerateFileSystemEntries(legacyRoot).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_DefaultPath_ShouldUseLocalAppDataProjectVariableStates()
    {
        var store = new JsonFileProjectVariableStateStore();
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVision",
            "ProjectVariableStates");

        store.BasePath.Should().Be(expectedRoot);
        store.BasePath.Should().NotContain(AppDomain.CurrentDomain.BaseDirectory);
        store.BasePath.Should().NotContain("App_Data");
    }

    [Fact]
    public void JsonFileStateStore_SaveWithPersistenceRevision_ShouldWriteMetadataEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var saveId = Guid.NewGuid();
            var snapshots = new[]
            {
                new ProjectVariableValueSnapshot(
                    variableId,
                    JsonSerializer.SerializeToElement(42L),
                    3,
                    DateTimeOffset.UtcNow,
                    ProjectVariableUpdatedBy.StudioManual,
                    Guid.NewGuid(),
                    Guid.NewGuid())
            };

            store.Save(scopeId, schema, snapshots, persistenceRevision: 7, saveId);

            var metadata = store.LoadMetadata(scopeId);
            metadata.Should().NotBeNull();
            metadata!.ScopeId.Should().Be(scopeId);
            metadata.PersistenceRevision.Should().Be(7);
            metadata.SchemaHash.Should().Be(ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema));
            metadata.StateHash.Should().Be(ProjectVariableStateHash.Compute(schema, snapshots));
            metadata.SaveId.Should().Be(saveId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_LoadMetadata_WhenValueChangesButStateHashIsOld_ShouldThrowGv041()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var snapshots = new[]
            {
                new ProjectVariableValueSnapshot(
                    variableId,
                    JsonSerializer.SerializeToElement(42L),
                    3,
                    DateTimeOffset.UtcNow,
                    ProjectVariableUpdatedBy.StudioManual,
                    Guid.NewGuid(),
                    Guid.NewGuid())
            };
            store.Save(scopeId, schema, snapshots, persistenceRevision: 7, Guid.NewGuid());
            var filePath = GetCommittedStateFilePath(root);
            MutateFirstVariable(filePath, variable => variable["value"] = "43");

            var act = () => store.LoadMetadata(scopeId);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("version")]
    [InlineData("updatedAtUtc")]
    [InlineData("updatedBy")]
    [InlineData("runId")]
    [InlineData("operatorId")]
    public void JsonFileStateStore_LoadMetadata_WhenStateIdentityFieldsChangeButStateHashIsOld_ShouldThrowGv041(string fieldName)
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var snapshots = new[]
            {
                new ProjectVariableValueSnapshot(
                    variableId,
                    JsonSerializer.SerializeToElement(42L),
                    3,
                    DateTimeOffset.UtcNow,
                    ProjectVariableUpdatedBy.StudioManual,
                    Guid.NewGuid(),
                    Guid.NewGuid())
            };
            store.Save(scopeId, schema, snapshots, persistenceRevision: 7, Guid.NewGuid());
            var filePath = GetCommittedStateFilePath(root);
            MutateFirstVariable(filePath, variable =>
            {
                variable[fieldName] = fieldName switch
                {
                    "version" => 4,
                    "updatedAtUtc" => DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
                    "updatedBy" => nameof(ProjectVariableUpdatedBy.Reset),
                    _ => Guid.NewGuid().ToString("D")
                };
            });

            var act = () => store.LoadMetadata(scopeId);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_LoadMetadata_WhenStateHashIsMissing_ShouldReturnLegacyComputedHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            var snapshots = new[]
            {
                new ProjectVariableValueSnapshot(
                    variableId,
                    JsonSerializer.SerializeToElement(42L),
                    3,
                    DateTimeOffset.UtcNow,
                    ProjectVariableUpdatedBy.StudioManual,
                    Guid.NewGuid(),
                    Guid.NewGuid())
            };
            store.Save(scopeId, schema, snapshots, persistenceRevision: 0, saveId: null);
            var filePath = GetCommittedStateFilePath(root);
            var node = LoadStateNode(filePath);
            node.Remove("stateHash");
            SaveStateNode(filePath, node);

            var metadata = store.LoadMetadata(scopeId);

            metadata.Should().NotBeNull();
            metadata!.StateHash.Should().Be(ProjectVariableStateHash.Compute(schema, snapshots));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_LoadMetadata_ShouldComputeHashIndependentOfVariableFileOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var firstVariableId = Guid.NewGuid();
            var secondVariableId = Guid.NewGuid();
            var schema = new ProjectGlobalVariableSchema
            {
                Variables =
                [
                    CreateDefinition(firstVariableId, 1, "stats.first"),
                    CreateDefinition(secondVariableId, 2, "stats.second")
                ]
            };
            var snapshots = new[]
            {
                new ProjectVariableValueSnapshot(
                    secondVariableId,
                    JsonSerializer.SerializeToElement(22L),
                    2,
                    DateTimeOffset.UtcNow,
                    ProjectVariableUpdatedBy.StudioManual,
                    Guid.NewGuid(),
                    Guid.NewGuid()),
                new ProjectVariableValueSnapshot(
                    firstVariableId,
                    JsonSerializer.SerializeToElement(11L),
                    1,
                    DateTimeOffset.UtcNow,
                    ProjectVariableUpdatedBy.OperatorOutput,
                    Guid.NewGuid(),
                    Guid.NewGuid())
            };
            store.Save(scopeId, schema, snapshots, persistenceRevision: 7, Guid.NewGuid());
            var before = store.LoadMetadata(scopeId);
            var filePath = GetCommittedStateFilePath(root);
            var node = LoadStateNode(filePath);
            var variables = node["variables"]!.AsArray();
            var first = variables[0];
            variables.RemoveAt(0);
            variables.Add(first);
            SaveStateNode(filePath, node);

            var after = store.LoadMetadata(scopeId);

            after.Should().NotBeNull();
            after!.StateHash.Should().Be(before!.StateHash);
            after.StateHash.Should().Be(ProjectVariableStateHash.Compute(schema, snapshots));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_LoadAndRegistry_WhenValueChangesButStateHashIsOld_ShouldThrowGv041()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var projectId = Guid.NewGuid();
            var scopeId = ProjectVariableSessionRegistry.ToProjectScopeId(projectId);
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            store.Save(scopeId, schema, [CreateSnapshot(variableId, 42L, 3)]);
            var filePath = GetCommittedStateFilePath(root);
            MutateFirstVariable(filePath, variable => variable["value"] = "43");

            var metadataAct = () => store.LoadMetadata(scopeId);
            var loadAct = () => store.Load(scopeId, schema);
            var registry = new ProjectVariableSessionRegistry(store);
            var registryAct = () => registry.GetOrCreate(projectId, schema);

            metadataAct.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
            loadAct.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
            registryAct.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
            registry.TryRemove(projectId).Should().BeFalse();
            File.Exists(filePath + ".corrupt").Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("version")]
    [InlineData("updatedAtUtc")]
    [InlineData("updatedBy")]
    [InlineData("runId")]
    [InlineData("operatorId")]
    public void JsonFileStateStore_Load_WhenStateIdentityFieldsChangeButStateHashIsOld_ShouldThrowGv041(string fieldName)
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            store.Save(scopeId, schema, [CreateSnapshot(variableId, 42L, 3)]);
            var filePath = GetCommittedStateFilePath(root);
            MutateFirstVariable(filePath, variable =>
            {
                variable[fieldName] = fieldName switch
                {
                    "version" => 4,
                    "updatedAtUtc" => DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
                    "updatedBy" => nameof(ProjectVariableUpdatedBy.Reset),
                    _ => Guid.NewGuid().ToString("D")
                };
            });

            var act = () => store.Load(scopeId, schema);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenMainHashIsTamperedAndLastGoodIsValid_ShouldRestoreLastGoodAndKeepCorruptEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 11L, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 22L, 2);
            var filePath = GetCommittedStateFilePath(root);
            var lastGoodPath = GetLastGoodStateFilePath(filePath);
            MutateFirstVariable(filePath, variable => variable["value"] = "99");

            var loaded = LoadLongAndVersion(store, scopeId, schema, variableId);

            loaded.Should().Be((11L, 1L));
            File.Exists(filePath + ".corrupt").Should().BeTrue();
            File.ReadAllText(filePath, Encoding.UTF8).Should().Be(File.ReadAllText(lastGoodPath, Encoding.UTF8));
            var recoveryJournal = ReadSingleRecoveryJournalEntry(GetRecoveryJournalPath(filePath));
            recoveryJournal.GetProperty("eventType").GetString().Should().Be("main-hash-mismatch-restored-last-good");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenMainAndLastGoodHashesAreTampered_ShouldThrowGv041()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 11L, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 22L, 2);
            var filePath = GetCommittedStateFilePath(root);
            MutateFirstVariable(filePath, variable => variable["value"] = "99");
            MutateFirstVariable(GetLastGoodStateFilePath(filePath), variable => variable["value"] = "88");

            var act = () => store.Load(scopeId, schema);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
            File.Exists(filePath + ".corrupt").Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenLegacyStateHashIsMissing_ShouldLoadAndMigrateWithComputedHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 55L, 5);
            var legacyFilePath = GetCommittedStateFilePath(legacyRoot);
            var node = LoadStateNode(legacyFilePath);
            node.Remove("stateHash");
            SaveStateNode(legacyFilePath, node);
            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            var loaded = LoadLongAndVersion(store, scopeId, schema, variableId);
            var metadata = store.LoadMetadata(scopeId);

            loaded.Should().Be((55L, 5L));
            metadata.Should().NotBeNull();
            metadata!.StateHash.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenLegacyStateHashIsTampered_ShouldRejectLegacyCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var currentRoot = Path.Combine(root, "current");
            var legacyRoot = Path.Combine(root, "legacy");
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(new JsonFileProjectVariableStateStore(legacyRoot), scopeId, schema, variableId, 55L, 5);
            MutateFirstVariable(GetCommittedStateFilePath(legacyRoot), variable => variable["value"] = "77");
            var store = new JsonFileProjectVariableStateStore(currentRoot, legacyRoot);

            var act = () => store.Load(scopeId, schema);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GV041*");
            Directory.EnumerateFiles(currentRoot, "*.json").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStateStore_Load_WhenInterruptedTempHashIsTampered_ShouldDiscardTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClearVisionProjectVariableState", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonFileProjectVariableStateStore(root);
            var scopeId = $"project:{Guid.NewGuid():D}";
            var variableId = Guid.NewGuid();
            var schema = CreateSchema(variableId, 1);
            SaveSnapshot(store, scopeId, schema, variableId, 55L, 5);
            var filePath = GetCommittedStateFilePath(root);
            var tempPath = filePath + ".tmp";
            File.Move(filePath, tempPath);
            MutateFirstVariable(tempPath, variable => variable["value"] = "77");

            var snapshots = store.Load(scopeId, schema);

            snapshots.Should().BeEmpty();
            File.Exists(tempPath).Should().BeFalse();
            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void StationSettingsPaths_ProjectVariableStates_ShouldUseStationDataRoot()
    {
        var localAppDataRoot = Path.Combine(Path.GetTempPath(), "ClearVisionStationPathTests", Guid.NewGuid().ToString("N"));
        var expectedRoot = Path.Combine(
            localAppDataRoot,
            StationSettingsPaths.StationAppDataDirectoryName,
            StationSettingsPaths.ProjectVariableStatesDirectoryName);

        var stateRoot = StationSettingsPaths.GetStationProjectVariableStatesPath(localAppDataRoot);

        stateRoot.Should().Be(expectedRoot);
        stateRoot.Should().Contain(StationSettingsPaths.StationAppDataDirectoryName);
        stateRoot.Should().NotContain(AppDomain.CurrentDomain.BaseDirectory);
        stateRoot.Should().NotContain("App_Data");
    }

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue, long? min = null, long? max = null)
    {
        var variable = CreateDefinition(variableId, initialValue, "stats.count");
        if (min.HasValue)
        {
            variable.Min = min.Value;
        }

        if (max.HasValue)
        {
            variable.Max = max.Value;
        }

        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                variable
            ]
        };
    }

    private static ProjectGlobalVariableDefinition CreateDefinition(Guid variableId, long initialValue, string name) =>
        new()
        {
            Id = variableId,
            Name = name,
            DisplayName = "Count",
            ValueType = ProjectGlobalVariableValueType.Int64,
            InitialValue = JsonSerializer.SerializeToElement(initialValue),
            ManualWriteAllowed = true
        };

    private static ProjectVariableValueSnapshot CreateSnapshot(Guid variableId, long value, long version) =>
        new(
            variableId,
            JsonSerializer.SerializeToElement(value),
            version,
            DateTimeOffset.UtcNow,
            ProjectVariableUpdatedBy.StudioManual,
            Guid.NewGuid(),
            Guid.NewGuid());

    private static void SaveSnapshot(
        IProjectVariableStateStore store,
        string scopeId,
        ProjectGlobalVariableSchema schema,
        Guid variableId,
        long value,
        long version)
    {
        store.Save(
            scopeId,
            schema,
            [new ProjectVariableValueSnapshot(variableId, JsonSerializer.SerializeToElement(value), version, DateTimeOffset.UtcNow, ProjectVariableUpdatedBy.StudioManual, null, null)]);
    }

    private static long LoadLong(
        IProjectVariableStateStore store,
        string scopeId,
        ProjectGlobalVariableSchema schema,
        Guid variableId)
    {
        return LoadLongAndVersion(store, scopeId, schema, variableId).Value;
    }

    private static (long Value, long Version) LoadLongAndVersion(
        IProjectVariableStateStore store,
        string scopeId,
        ProjectGlobalVariableSchema schema,
        Guid variableId)
    {
        using var session = new ProjectVariableSession(schema, store.Load(scopeId, schema));
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        return (Convert.ToInt64(ProjectVariableValueConverter.ToObject(snapshot.Value)), snapshot.Version);
    }

    private static string GetCommittedStateFilePath(string root)
    {
        return Directory.EnumerateFiles(root, "*.json")
            .Single(path => !path.EndsWith(".last-good.json", StringComparison.Ordinal));
    }

    private static string GetLastGoodStateFilePath(string committedFilePath)
    {
        return Path.Combine(
            Path.GetDirectoryName(committedFilePath)!,
            $"{Path.GetFileNameWithoutExtension(committedFilePath)}.last-good.json");
    }

    private static string GetRecoveryJournalPath(string committedFilePath) => committedFilePath + ".journal";

    private static JsonObject LoadStateNode(string filePath) =>
        JsonNode.Parse(File.ReadAllText(filePath, Encoding.UTF8))!.AsObject();

    private static void SaveStateNode(string filePath, JsonObject node)
    {
        File.WriteAllText(filePath, node.ToJsonString(ProjectVariableJson.Options), Encoding.UTF8);
    }

    private static void MutateFirstVariable(string filePath, Action<JsonObject> mutate)
    {
        var node = LoadStateNode(filePath);
        var variable = node["variables"]!.AsArray()[0]!.AsObject();
        mutate(variable);
        SaveStateNode(filePath, node);
    }

    private static long ReadStateFileGeneration(string filePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(filePath, Encoding.UTF8));
        return document.RootElement.GetProperty("generation").GetInt64();
    }

    private static JsonElement ReadSingleRecoveryJournalEntry(string journalPath)
    {
        File.Exists(journalPath).Should().BeTrue();
        var line = File.ReadLines(journalPath).Single();
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static void WriteRecoveryArtifacts(string filePath)
    {
        File.WriteAllText(filePath + ".tmp", "temp", Encoding.UTF8);
        File.WriteAllText(filePath + ".corrupt", "corrupt", Encoding.UTF8);
        File.WriteAllText(filePath + ".journal", "journal", Encoding.UTF8);
    }

    private sealed class RecordingStateStore : IProjectVariableStateStore
    {
        public bool FailSaves { get; set; }

        public IReadOnlyList<ProjectVariableValueSnapshot> SavedSnapshots { get; private set; } = [];

        public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema)
        {
            return SavedSnapshots.Select(CloneSnapshot).ToList();
        }

        public void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
        {
            if (FailSaves)
            {
                throw new IOException("simulated state-store failure");
            }

            SavedSnapshots = snapshots.Select(CloneSnapshot).ToList();
        }

        public void Delete(string scopeId)
        {
            SavedSnapshots = [];
        }

        private static ProjectVariableValueSnapshot CloneSnapshot(ProjectVariableValueSnapshot snapshot)
        {
            return snapshot with { Value = snapshot.Value.Clone() };
        }
    }

    private sealed class PhaseFailingProjectVariableStateFileSystem : IProjectVariableStateFileSystem
    {
        public bool FailCreateDirectories { get; set; }

        public bool FailWrites { get; set; }

        public bool FailCopies { get; set; }

        public bool FailMoves { get; set; }

        public void CreateDirectory(string path)
        {
            if (FailCreateDirectories)
            {
                throw new UnauthorizedAccessException("read-only directory");
            }

            Directory.CreateDirectory(path);
        }

        public bool FileExists(string path) => File.Exists(path);

        public string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            if (FailWrites)
            {
                throw new IOException("write failed");
            }

            File.WriteAllText(path, contents, encoding);
        }

        public void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            if (FailCopies)
            {
                throw new IOException("copy failed");
            }

            File.Copy(sourceFileName, destFileName, overwrite);
        }

        public void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            if (FailMoves)
            {
                throw new IOException("move failed");
            }

            File.Move(sourceFileName, destFileName, overwrite);
        }

        public void DeleteFile(string path) => File.Delete(path);
    }
}
