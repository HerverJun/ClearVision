// AiConfigStoreTests.cs
// AiConfigStore 单元测试
// 作者：蘅芜君

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClearVision.Product.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class AiConfigStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testModelsFile;
    private readonly string _testLegacyFile;
    private readonly Microsoft.Extensions.Logging.ILogger<AiConfigStore> _mockLogger;
    private readonly IOptions<AiGenerationOptions> _mockOptions;

    public AiConfigStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"cv-ai-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _testModelsFile = Path.Combine(_testDir, "ai_models.json");
        _testLegacyFile = Path.Combine(_testDir, "ai_config.json");

        _mockLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<AiConfigStore>>();
        _mockOptions = Options.Create(new AiGenerationOptions
        {
            Provider = "TestProvider",
            ApiKey = "TestKey",
            Model = "TestModel",
            BaseUrl = "http://test",
            TimeoutSeconds = 60
        });

        CleanupFiles();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }

    private void CleanupFiles()
    {
        if (File.Exists(_testModelsFile))
            File.Delete(_testModelsFile);
        if (File.Exists(_testLegacyFile))
            File.Delete(_testLegacyFile);
    }

    private AiConfigStore CreateStore(IAiPersistenceFaultInjector? faultInjector = null)
    {
        return new AiConfigStore(_mockOptions, _mockLogger, _testDir, faultInjector);
    }

    [Fact]
    public void Constructor_WhenNoFiles_CreatesDefaultFromOptions()
    {
        // Arrange & Act
        var store = CreateStore();
        var all = store.GetAll();

        // Assert
        Assert.Single(all);
        var defaultModel = all[0];
        Assert.Equal("系统默认模型", defaultModel.Name);
        Assert.Equal("TestProvider", defaultModel.Provider);
        Assert.Equal("TestKey", defaultModel.ApiKey);
        Assert.Equal("TestModel", defaultModel.Model);
        Assert.Equal("http://test", defaultModel.BaseUrl);
        Assert.Equal(60000, defaultModel.TimeoutMs);
        Assert.True(defaultModel.IsActive);
        Assert.NotNull(defaultModel.Reasoning);
        Assert.Equal(AiReasoningModes.Auto, defaultModel.Reasoning!.Mode);
        Assert.Equal(AiReasoningEfforts.Medium, defaultModel.Reasoning.Effort);
    }

    [Fact]
    public void Save_ShouldNotPersistApiKeyInAiModelsJson()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "secret-model",
            Name = "Secret Model",
            Provider = "OpenAI Compatible",
            ApiKey = "SecretKeyForDisk",
            Model = "gpt-4o-mini"
        });

        var persistedJson = File.ReadAllText(_testModelsFile);
        Assert.DoesNotContain("SecretKeyForDisk", persistedJson);
        Assert.Contains("\"apiKey\": \"\"", persistedJson);
        Assert.Equal("SecretKeyForDisk", store.GetById("secret-model")!.ApiKey);

        var reloaded = CreateStore();
        Assert.Equal("SecretKeyForDisk", reloaded.GetById("secret-model")!.ApiKey);
    }

    [Fact]
    public void Constructor_WhenAiModelsJsonHasInlineApiKey_MigratesItToSecretStore()
    {
        var legacyModel = new AiModelConfig
        {
            Id = "legacy-inline",
            Name = "Legacy Inline",
            Provider = "OpenAI Compatible",
            ApiKey = "LegacyInlineKey",
            Model = "gpt-4o-mini",
            IsActive = true
        };
        File.WriteAllText(
            _testModelsFile,
            JsonSerializer.Serialize(new[] { legacyModel }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

        var store = CreateStore();

        Assert.Equal("LegacyInlineKey", store.GetById("legacy-inline")!.ApiKey);
        var persistedJson = File.ReadAllText(_testModelsFile);
        Assert.DoesNotContain("LegacyInlineKey", persistedJson);
        Assert.Contains("\"apiKey\": \"\"", persistedJson);

        var reloaded = CreateStore();
        Assert.Equal("LegacyInlineKey", reloaded.GetById("legacy-inline")!.ApiKey);
    }

    [Fact]
    public void Add_Model_Then_GetAll_ReturnsIt()
    {
        var store = CreateStore();
        store.GetAll(); // Ignore default

        var newModel = new AiModelConfig { Id = "test1", Name = "New Model" };
        store.Add(newModel);

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.Id == "test1");
    }

    [Fact]
    public void Add_DuplicateId_ShouldReject()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig { Id = "duplicate-model", Name = "First", ApiKey = "FirstKey" });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            store.Add(new AiModelConfig { Id = "duplicate-model", Name = "Second", ApiKey = "SecondKey" }));

        Assert.Contains("duplicate-model", ex.Message);
        Assert.Single(store.GetAll().Where(model => model.Id == "duplicate-model"));
        Assert.Equal("FirstKey", store.GetById("duplicate-model")!.ApiKey);
    }

    [Fact]
    public void Delete_LastModel_ThrowsException()
    {
        var store = CreateStore();
        var defaultModel = store.GetAll().First();

        var ex = Assert.Throws<InvalidOperationException>(() => store.Delete(defaultModel.Id));
        Assert.Equal("至少需保留一个模型配置", ex.Message);
    }

    [Fact]
    public void Delete_ActiveModel_ActivatesFirstRemaining()
    {
        var store = CreateStore();
        var defaultModel = store.GetAll().First();

        var secondModel = new AiModelConfig { Id = "test2", IsActive = false };
        store.Add(secondModel);

        Assert.True(store.GetById(defaultModel.Id)!.IsActive);

        // Act
        store.Delete(defaultModel.Id);

        // Assert
        var remaining = store.GetAll();
        Assert.Single(remaining);
        Assert.Equal("test2", remaining[0].Id);
        Assert.True(remaining[0].IsActive);
    }

    [Fact]
    public void Delete_ModelWithApiKey_ShouldPruneSecretForFutureReloads()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "delete-secret",
            Name = "Delete Secret",
            Provider = "OpenAI Compatible",
            ApiKey = "DeletedSecretKey",
            Model = "gpt-4o-mini"
        });

        store.Delete("delete-secret");

        store.Add(new AiModelConfig
        {
            Id = "delete-secret",
            Name = "Resurrected",
            Provider = "OpenAI Compatible",
            ApiKey = string.Empty,
            Model = "gpt-4o-mini",
            IsActive = true
        });

        var reloaded = CreateStore();

        Assert.Equal(string.Empty, reloaded.GetById("delete-secret")!.ApiKey);
    }

    [Fact]
    public void SetActive_UpdatesActiveFlag()
    {
        var store = CreateStore();
        var defaultModel = store.GetAll().First();

        var modelA = new AiModelConfig { Id = "A", IsActive = false };
        var modelB = new AiModelConfig { Id = "B", IsActive = false };
        store.Add(modelA);
        store.Add(modelB);

        // Act
        store.SetActive("B");

        // Assert
        Assert.False(store.GetById(defaultModel.Id)!.IsActive);
        Assert.False(store.GetById("A")!.IsActive);
        Assert.True(store.GetById("B")!.IsActive);
    }

    [Fact]
    public void Update_WithNullApiKey_PreservesOldKey()
    {
        var store = CreateStore();
        var model = new AiModelConfig { Id = "test3", ApiKey = "RealSecretKey" };
        store.Add(model);

        var updateReq = new AiModelConfig { ApiKey = null!, Name = "Updated Name" };

        // Act
        var updated = store.Update("test3", updateReq);

        // Assert
        Assert.Equal("Updated Name", updated!.Name);
        Assert.Equal("RealSecretKey", updated.ApiKey); // Old key preserved
    }

    [Fact]
    public void Update_WithEmptyApiKey_PreservesOldKey()
    {
        var store = CreateStore();
        var model = new AiModelConfig { Id = "test4", ApiKey = "RealSecretKey" };
        store.Add(model);

        var updateReq = new AiModelConfig { ApiKey = "", Name = "Updated Name" };

        // Act
        var updated = store.Update("test4", updateReq);

        // Assert
        Assert.Equal("RealSecretKey", updated!.ApiKey); // Old key preserved
    }

    [Fact]
    public void Get_ReturnsActiveModelAsOptions()
    {
        var store = CreateStore();
        var modelA = new AiModelConfig { Id = "A", Provider = "ProviderA", Model = "ModelA", IsActive = false };
        var modelB = new AiModelConfig { Id = "B", Provider = "ProviderB", Model = "ModelB", IsActive = true };

        store.Add(modelA);
        store.Add(modelB);
        store.SetActive("B");

        var options = store.Get();
        Assert.Equal("ProviderB", options.Provider);
        Assert.Equal("ModelB", options.Model);
    }

    [Fact]
    public void Migration_FromOldSingleConfig()
    {
        // 模拟一个旧版的 ai_config.json
        var oldConfigJson = "{\"Provider\":\"LegacyProvider\",\"ApiKey\":\"LegacyKey\",\"Model\":\"LegacyModel\",\"BaseUrl\":\"http://legacy\",\"TimeoutSeconds\":120,\"MaxRetries\":3,\"MaxTokens\":2048,\"Temperature\":0.5}";
        File.WriteAllText(_testLegacyFile, oldConfigJson);

        var store = CreateStore();

        var all = store.GetAll();
        Assert.Single(all);
        var migrated = all[0];

        Assert.Equal("model_migrated", migrated.Id);
        Assert.Equal("LegacyProvider", migrated.Provider);
        Assert.Equal("LegacyKey", migrated.ApiKey);
        Assert.Equal("LegacyModel", migrated.Model);
        Assert.Equal("http://legacy", migrated.BaseUrl);
        Assert.Equal(120000, migrated.TimeoutMs);
        Assert.True(migrated.IsActive);
        Assert.NotNull(migrated.Reasoning);
        Assert.Equal(AiReasoningModes.Auto, migrated.Reasoning!.Mode);
        Assert.Equal(AiReasoningEfforts.Medium, migrated.Reasoning.Effort);
        Assert.False(File.Exists(_testLegacyFile));
        Assert.DoesNotContain("LegacyKey", File.ReadAllText(_testModelsFile));
    }

    [Fact]
    public void Update_WithReasoningSettings_PersistsNormalizedReasoning()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "reasoning-model",
            Name = "Reasoning Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.4"
        });

        var updated = store.Update("reasoning-model", new AiModelConfig
        {
            Name = "Reasoning Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.4",
            Reasoning = new AiReasoningSettings
            {
                Mode = "ON",
                Effort = "HIGH"
            }
        });

        Assert.NotNull(updated);
        Assert.NotNull(updated!.Reasoning);
        Assert.Equal(AiReasoningModes.On, updated.Reasoning!.Mode);
        Assert.Equal(AiReasoningEfforts.High, updated.Reasoning.Effort);
    }

    [Fact]
    public void Update_WhenLockedThinkingModelIsTurnedOff_ShouldThrow()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "deepseek-reasoner",
            Name = "DeepSeek Reasoner",
            Provider = "OpenAI Compatible",
            Model = "deepseek-reasoner"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => store.Update("deepseek-reasoner", new AiModelConfig
        {
            Name = "DeepSeek Reasoner",
            Provider = "OpenAI Compatible",
            Model = "deepseek-reasoner",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.Off,
                Effort = AiReasoningEfforts.Medium
            }
        }));
        Assert.Contains("不支持关闭 reasoning / thinking", ex.Message);
    }

    [Fact]
    public void Update_WhenValidationFails_ShouldNotMutateStoredModel()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "gpt-5-legacy",
            Name = "GPT 5 Legacy",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.4",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.On,
                Effort = AiReasoningEfforts.High
            }
        });

        var ex = Assert.Throws<InvalidOperationException>(() => store.Update("gpt-5-legacy", new AiModelConfig
        {
            Name = "GPT 5 Legacy",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.4",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.Off,
                Effort = AiReasoningEfforts.Medium
            }
        }));

        Assert.Contains("不支持关闭 reasoning / thinking", ex.Message);
        var persisted = store.GetById("gpt-5-legacy");
        Assert.NotNull(persisted);
        Assert.Equal(AiReasoningModes.On, persisted!.Reasoning!.Mode);
        Assert.Equal(AiReasoningEfforts.High, persisted.Reasoning!.Effort);
    }

    [Fact]
    public void Update_WhenGpt51ReasoningIsTurnedOff_ShouldAllowNoneMode()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "gpt-5-1",
            Name = "GPT 5.1",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.1"
        });

        var updated = store.Update("gpt-5-1", new AiModelConfig
        {
            Name = "GPT 5.1",
            Provider = "OpenAI Compatible",
            Model = "gpt-5.1",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.Off,
                Effort = AiReasoningEfforts.Medium
            }
        });

        Assert.NotNull(updated);
        Assert.Equal(AiReasoningModes.Off, updated!.Reasoning!.Mode);
    }

    [Fact]
    public void Update_WhenGpt5ProUsesNonHighEffort_ShouldThrow()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "gpt-5-pro",
            Name = "GPT 5 Pro",
            Provider = "OpenAI Compatible",
            Model = "gpt-5-pro",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.On,
                Effort = AiReasoningEfforts.High
            }
        });

        var ex = Assert.Throws<InvalidOperationException>(() => store.Update("gpt-5-pro", new AiModelConfig
        {
            Name = "GPT 5 Pro",
            Provider = "OpenAI Compatible",
            Model = "gpt-5-pro",
            Reasoning = new AiReasoningSettings
            {
                Mode = AiReasoningModes.On,
                Effort = AiReasoningEfforts.Medium
            }
        }));

        Assert.Contains("仅支持 High 思考强度", ex.Message);
    }

    [Fact]
    public void ResetToDefaults_ShouldReplaceModelsAndDeleteLegacyFile()
    {
        File.WriteAllText(_testLegacyFile, "{\"Provider\":\"LegacyProvider\",\"ApiKey\":\"LegacyKey\",\"Model\":\"LegacyModel\",\"TimeoutSeconds\":30}");

        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "custom-model",
            Name = "Custom Model",
            Provider = "custom-provider",
            ApiKey = "custom-key",
            Model = "custom-model-name"
        });
        store.SetActive("custom-model");

        var resetModels = store.ResetToDefaults();

        Assert.Single(resetModels);
        Assert.Single(store.GetAll());
        var defaultModel = store.GetAll().Single();
        Assert.Equal("model_default", defaultModel.Id);
        Assert.Equal("系统默认模型", defaultModel.Name);
        Assert.Equal("TestProvider", defaultModel.Provider);
        Assert.Equal("TestKey", defaultModel.ApiKey);
        Assert.Equal("TestModel", defaultModel.Model);
        Assert.True(defaultModel.IsActive);
        Assert.False(File.Exists(_testLegacyFile));
        var persistedJson = File.ReadAllText(_testModelsFile);
        Assert.DoesNotContain("TestKey", persistedJson);
        Assert.DoesNotContain("custom-key", persistedJson);
    }

    [Fact]
    public void Update_WithExplicitReplaceAndClearApiKey_ShouldApplyKeyOperation()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "key-model",
            Name = "Key Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "old-secret"
        });

        var replaced = store.Update("key-model", new AiModelConfig
        {
            Name = "Key Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "new-secret"
        }, AiApiKeyUpdateMode.Replace);

        Assert.Equal("new-secret", replaced!.ApiKey);

        var cleared = store.Update("key-model", new AiModelConfig
        {
            Name = "Key Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini"
        }, AiApiKeyUpdateMode.Clear);

        Assert.Equal(string.Empty, cleared!.ApiKey);
        Assert.DoesNotContain("new-secret", File.ReadAllText(_testModelsFile));
    }

    [Fact]
    public void SetDefaultForRole_ShouldBindPlannerRoleAndEnableModel()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "planner-model",
            Name = "Planner Model",
            Provider = "OpenAI Compatible",
            Model = "planner",
            IsEnabled = false,
            RoleBindings = new List<string> { AiModelConfig.RoleGeneration },
            Priority = 50
        });

        var ok = store.SetDefaultForRole("planner-model", AiModelConfig.RolePlanner);

        Assert.True(ok);
        var updated = store.GetById("planner-model")!;
        Assert.True(updated.IsEnabled);
        Assert.Contains(AiModelConfig.RolePlanner, updated.RoleBindings!);
        Assert.Equal(AiModelConfig.RolePlanner, updated.ModelRole);
        Assert.Equal(1, updated.Priority);
    }

    [Fact]
    public void Add_ProductizedFields_ShouldPersistAndHydrate()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "product-model",
            Name = "Product Model",
            DisplayName = "Workbench Planner",
            Provider = "OpenAI Compatible",
            Model = "planner",
            ModelRole = AiModelConfig.RoleShadowEval,
            RoleBindings = new List<string> { AiModelConfig.RoleShadowEval },
            Remark = "offline shadow only",
            Priority = 7,
            IsEnabled = true,
            LastTestStatus = "ok",
            LastTestAt = DateTimeOffset.UtcNow,
            LastTestLatencyMs = 42
        });

        var reloaded = CreateStore().GetById("product-model")!;

        Assert.Equal("Workbench Planner", reloaded.DisplayName);
        Assert.Equal(AiModelConfig.RoleShadowEval, reloaded.ModelRole);
        Assert.Contains(AiModelConfig.RoleShadowEval, reloaded.RoleBindings!);
        Assert.Equal("offline shadow only", reloaded.Remark);
        Assert.Equal(7, reloaded.Priority);
        Assert.True(reloaded.IsEnabled);
        Assert.Equal("ok", reloaded.LastTestStatus);
        Assert.Equal(42, reloaded.LastTestLatencyMs);
        Assert.NotNull(reloaded.CreatedAt);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public void Add_WhenSecretCandidateWriteFails_ShouldKeepOldMemoryAndDurableGeneration()
    {
        var faultInjector = new AiPersistenceTestFaultInjector();
        var store = CreateStore(faultInjector);
        var beforeJson = File.ReadAllText(_testModelsFile);
        const string secret = "must-never-leak-secret";
        faultInjector.FailOnce(
            AiPersistenceStage.ModelSecretCandidateWrite,
            static () => new UnauthorizedAccessException("secret candidate denied"));

        var error = Assert.Throws<AiConfigPersistenceException>(() => store.Add(new AiModelConfig
        {
            Id = "secret-failure",
            Name = "Secret Failure",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = secret
        }));

        Assert.Equal("AI_MODEL_SECRET_PERSISTENCE_FAILED", error.ErrorCode);
        Assert.Equal("candidate_secrets", error.Stage);
        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
        Assert.Null(store.GetById("secret-failure"));
        Assert.Equal(beforeJson, File.ReadAllText(_testModelsFile));
        Assert.Null(CreateStore().GetById("secret-failure"));
        Assert.Empty(Directory.EnumerateFiles(_testDir, "*.candidate"));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_testDir, "ai_model_secrets"), ".candidate-*"));
    }

    [Fact]
    public void Add_WhenCandidateDocumentStageFails_ShouldRecoverCompleteOldGeneration()
    {
        var faultInjector = new AiPersistenceTestFaultInjector();
        var store = CreateStore(faultInjector);
        faultInjector.FailOnce(
            AiPersistenceStage.ModelDocumentPrepared,
            static () => new IOException("candidate document fault"));

        var error = Assert.Throws<AiConfigPersistenceException>(() => store.Add(new AiModelConfig
        {
            Id = "candidate-failure",
            Name = "Candidate Failure",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "candidate-secret"
        }));

        Assert.Equal("AI_MODEL_CANDIDATE_PERSISTENCE_FAILED", error.ErrorCode);
        Assert.Null(store.GetById("candidate-failure"));
        Assert.Null(CreateStore().GetById("candidate-failure"));
    }

    [Fact]
    public void Restart_AfterInterruptionBeforeCommit_ShouldKeepOldGenerationAndDiscardResidue()
    {
        var faultInjector = new AiPersistenceTestFaultInjector();
        var store = CreateStore(faultInjector);
        faultInjector.FailOnce(
            AiPersistenceStage.ModelCommitStarted,
            static () => new AiPersistenceInterruptionException("before_model_commit"));

        Assert.Throws<AiPersistenceInterruptionException>(() => store.Add(new AiModelConfig
        {
            Id = "pre-commit-crash",
            Name = "Pre Commit Crash",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "pre-commit-secret"
        }));

        Assert.Null(store.GetById("pre-commit-crash"));
        var restarted = CreateStore();
        Assert.Null(restarted.GetById("pre-commit-crash"));
        Assert.Empty(Directory.EnumerateFiles(_testDir, "ai_models.json.*.candidate"));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_testDir, "ai_model_secrets"), ".candidate-*"));
    }

    [Fact]
    public void Restart_AfterInterruptionAfterCommit_ShouldLoadCompleteNewGeneration()
    {
        var faultInjector = new AiPersistenceTestFaultInjector();
        var store = CreateStore(faultInjector);
        faultInjector.FailOnce(
            AiPersistenceStage.ModelCommitCompleted,
            static () => new AiPersistenceInterruptionException("after_model_commit"));

        Assert.Throws<AiPersistenceInterruptionException>(() => store.Add(new AiModelConfig
        {
            Id = "post-commit-crash",
            Name = "Post Commit Crash",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "post-commit-secret"
        }));

        Assert.Null(store.GetById("post-commit-crash"));
        var restarted = CreateStore();
        var recovered = restarted.GetById("post-commit-crash");
        Assert.NotNull(recovered);
        Assert.Equal("post-commit-secret", recovered!.ApiKey);
    }

    [Fact]
    public async Task ConcurrentMutations_ShouldSerializeCandidateCommitsAndPersistBoth()
    {
        var faultInjector = new AiPersistenceTestFaultInjector();
        var store = CreateStore(faultInjector);
        using var firstCandidateEntered = new ManualResetEventSlim(false);
        using var releaseFirstCandidate = new ManualResetEventSlim(false);
        var candidatePaths = new List<string>();
        var candidateCount = 0;
        faultInjector.SetHandler((stage, _, path) =>
        {
            if (stage != AiPersistenceStage.ModelDocumentPrepared)
            {
                return;
            }

            lock (candidatePaths)
            {
                candidatePaths.Add(path);
            }

            if (Interlocked.Increment(ref candidateCount) == 1)
            {
                firstCandidateEntered.Set();
                Assert.True(releaseFirstCandidate.Wait(TimeSpan.FromSeconds(5)));
            }
        });

        var first = Task.Run(() => store.Add(new AiModelConfig
        {
            Id = "concurrent-a",
            Name = "Concurrent A",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "secret-a"
        }));
        Assert.True(firstCandidateEntered.Wait(TimeSpan.FromSeconds(5)));

        var second = Task.Run(() => store.Add(new AiModelConfig
        {
            Id = "concurrent-b",
            Name = "Concurrent B",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "secret-b"
        }));
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        releaseFirstCandidate.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(2, candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var restarted = CreateStore();
        Assert.Equal("secret-a", restarted.GetById("concurrent-a")!.ApiKey);
        Assert.Equal("secret-b", restarted.GetById("concurrent-b")!.ApiKey);
    }

    [Fact]
    public async Task ConcurrentStoreInstances_ShouldSharePathAuthorityAndReloadDurableGeneration()
    {
        var firstFault = new AiPersistenceTestFaultInjector();
        var firstStore = CreateStore(firstFault);
        var secondStore = CreateStore();
        using var firstCandidateEntered = new ManualResetEventSlim(false);
        using var releaseFirstCandidate = new ManualResetEventSlim(false);
        var blocked = 0;
        firstFault.SetHandler((stage, authority, _) =>
        {
            if (stage == AiPersistenceStage.ModelDocumentPrepared &&
                authority == "ai_models" &&
                Interlocked.Exchange(ref blocked, 1) == 0)
            {
                firstCandidateEntered.Set();
                Assert.True(releaseFirstCandidate.Wait(TimeSpan.FromSeconds(5)));
            }
        });

        var first = Task.Run(() => firstStore.Add(new AiModelConfig
        {
            Id = "cross-instance-one",
            Name = "Cross Instance One",
            ApiKey = "one-key"
        }));
        Assert.True(firstCandidateEntered.Wait(TimeSpan.FromSeconds(5)));
        var second = Task.Run(() => secondStore.Add(new AiModelConfig
        {
            Id = "cross-instance-two",
            Name = "Cross Instance Two",
            ApiKey = "two-key"
        }));
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        releaseFirstCandidate.Set();
        await Task.WhenAll(first, second);

        var reloaded = CreateStore();
        Assert.Equal("one-key", reloaded.GetById("cross-instance-one")!.ApiKey);
        Assert.Equal("two-key", reloaded.GetById("cross-instance-two")!.ApiKey);
    }

    [Fact]
    public void AllModelMutationKinds_ShouldPersistThroughOneGenerationAuthority()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "authority-model",
            Name = "Authority Model",
            Provider = "OpenAI Compatible",
            Model = "initial",
            ApiKey = "authority-secret",
            IsEnabled = true
        });
        store.Update("authority-model", new AiModelConfig
        {
            Name = "Authority Model Updated",
            Provider = "OpenAI Compatible",
            Model = "updated",
            IsEnabled = true
        });
        Assert.True(store.SetActive("authority-model"));
        Assert.True(store.SetDefaultForRole("authority-model", AiModelConfig.RolePlanner));
        store.UpdateTestStatus("authority-model", "ok", DateTimeOffset.UtcNow, 17);

        var restarted = CreateStore();
        var persisted = restarted.GetById("authority-model")!;
        Assert.Equal("updated", persisted.Model);
        Assert.True(persisted.IsActive);
        Assert.Contains(AiModelConfig.RolePlanner, persisted.RoleBindings!);
        Assert.Equal("ok", persisted.LastTestStatus);
        Assert.Equal(17, persisted.LastTestLatencyMs);
        Assert.Equal("authority-secret", persisted.ApiKey);

        Assert.True(restarted.Delete("authority-model"));
        Assert.Null(CreateStore().GetById("authority-model"));
    }

    [Fact]
    public void Constructor_WhenActiveGenerationIsCorrupt_ShouldRecoverPreviousCompleteGeneration()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "survives-recovery",
            Name = "Survives Recovery",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "surviving-secret"
        });
        store.Add(new AiModelConfig
        {
            Id = "lost-newer-generation",
            Name = "Lost Newer Generation",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "newer-secret"
        });
        File.WriteAllText(_testModelsFile, "{corrupt-active-generation");

        var recovered = CreateStore();

        Assert.Equal("surviving-secret", recovered.GetById("survives-recovery")!.ApiKey);
        Assert.Null(recovered.GetById("lost-newer-generation"));
        using var document = JsonDocument.Parse(File.ReadAllText(_testModelsFile));
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void Constructor_WhenActiveSecretGenerationIsMissing_ShouldRecoverCompleteLegacyBackup()
    {
        var legacyModel = new AiModelConfig
        {
            Id = "legacy-backup-model",
            Name = "Legacy Backup Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "legacy-backup-secret",
            IsActive = true,
            IsEnabled = true
        };
        File.WriteAllText(
            _testModelsFile + ".previous",
            JsonSerializer.Serialize(new[] { legacyModel }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
        File.WriteAllText(
            _testModelsFile,
            JsonSerializer.Serialize(new AiModelGenerationDocument
            {
                GenerationId = "missing-secret-generation",
                Models = new List<AiModelConfig>
                {
                    new()
                    {
                        Id = legacyModel.Id,
                        Name = legacyModel.Name,
                        Provider = legacyModel.Provider,
                        Model = legacyModel.Model,
                        IsActive = true,
                        IsEnabled = true
                    }
                },
                SecretModelIds = new List<string> { legacyModel.Id }
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

        var recovered = CreateStore();

        Assert.Equal("legacy-backup-secret", recovered.GetById(legacyModel.Id)!.ApiKey);
        using var document = JsonDocument.Parse(File.ReadAllText(_testModelsFile));
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var generationId = document.RootElement.GetProperty("generationId").GetString();
        Assert.True(Directory.Exists(Path.Combine(_testDir, "ai_model_secrets", generationId!)));
    }
}
