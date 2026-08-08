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

    private AiConfigStore CreateStore()
    {
        return new AiConfigStore(_mockOptions, _mockLogger, _testDir);
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

        var resurrectedModel = new AiModelConfig
        {
            Id = "delete-secret",
            Name = "Resurrected",
            Provider = "OpenAI Compatible",
            ApiKey = string.Empty,
            Model = "gpt-4o-mini",
            IsActive = true
        };
        File.WriteAllText(
            _testModelsFile,
            JsonSerializer.Serialize(new[] { resurrectedModel }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

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
    public void Update_WhenModelDocumentWriteFails_ShouldRollbackMemorySecretAndJson()
    {
        var store = CreateStore();
        store.Add(new AiModelConfig
        {
            Id = "rollback-model",
            Name = "Rollback Model",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o-mini",
            ApiKey = "original-secret"
        });
        var originalJson = File.ReadAllBytes(_testModelsFile);
        store.PersistenceHook = stage =>
        {
            if (stage == AiConfigPersistenceStage.BeforeModelDocumentWrite)
            {
                throw new IOException("Injected model JSON write failure.");
            }
        };

        var exception = Assert.Throws<AiConfigPersistenceException>(() => store.Update(
            "rollback-model",
            new AiModelConfig
            {
                Name = "Mutated Name",
                Provider = "OpenAI Compatible",
                Model = "gpt-4o-mini",
                ApiKey = "replacement-secret"
            },
            AiApiKeyUpdateMode.Replace));

        Assert.True(exception.RollbackSucceeded);
        Assert.Equal("Rollback Model", store.GetById("rollback-model")!.Name);
        Assert.Equal("original-secret", store.GetById("rollback-model")!.ApiKey);
        Assert.Equal(originalJson, File.ReadAllBytes(_testModelsFile));
        store.PersistenceHook = null;

        var reloaded = CreateStore().GetById("rollback-model")!;
        Assert.Equal("Rollback Model", reloaded.Name);
        Assert.Equal("original-secret", reloaded.ApiKey);
        Assert.Empty(Directory.EnumerateFiles(_testDir, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentMutations_ShouldKeepMemorySecretsAndModelJsonConsistent()
    {
        var store = CreateStore();
        for (var index = 0; index < 8; index++)
        {
            store.Add(new AiModelConfig
            {
                Id = $"concurrent-{index}",
                Name = $"Concurrent {index}",
                Provider = "OpenAI Compatible",
                Model = "gpt-4o-mini",
                ApiKey = $"initial-secret-{index}"
            });
        }

        var mutations = new List<Task>();
        for (var index = 0; index < 6; index++)
        {
            var captured = index;
            mutations.Add(Task.Run(() => store.Update(
                $"concurrent-{captured}",
                new AiModelConfig
                {
                    Name = $"Updated {captured}",
                    Provider = "OpenAI Compatible",
                    Model = "gpt-4o-mini",
                    ApiKey = $"updated-secret-{captured}"
                },
                AiApiKeyUpdateMode.Replace)));
            mutations.Add(Task.Run(() => store.UpdateTestStatus(
                $"concurrent-{captured}",
                "ok",
                DateTimeOffset.UtcNow,
                captured + 10)));
        }

        mutations.Add(Task.Run(() => store.SetActive("concurrent-5")));
        mutations.Add(Task.Run(() => store.SetDefaultForRole("concurrent-4", AiModelConfig.RolePlanner)));
        mutations.Add(Task.Run(() => store.Delete("concurrent-6")));
        mutations.Add(Task.Run(() => store.Delete("concurrent-7")));
        await Task.WhenAll(mutations);

        var memory = store.GetAll();
        var reloaded = CreateStore().GetAll();
        Assert.Equal(memory.Select(model => model.Id).OrderBy(id => id), reloaded.Select(model => model.Id).OrderBy(id => id));
        Assert.DoesNotContain(reloaded, model => model.Id is "concurrent-6" or "concurrent-7");
        Assert.Single(reloaded.Where(model => model.IsActive));
        Assert.Equal("concurrent-5", reloaded.Single(model => model.IsActive).Id);
        Assert.Contains(AiModelConfig.RolePlanner, reloaded.Single(model => model.Id == "concurrent-4").RoleBindings!);
        for (var index = 0; index < 6; index++)
        {
            var model = reloaded.Single(item => item.Id == $"concurrent-{index}");
            Assert.Equal($"Updated {index}", model.Name);
            Assert.Equal($"updated-secret-{index}", model.ApiKey);
            Assert.Equal("ok", model.LastTestStatus);
        }

        using var persisted = JsonDocument.Parse(File.ReadAllText(_testModelsFile));
        Assert.Equal(reloaded.Count, persisted.RootElement.GetArrayLength());
        Assert.DoesNotContain("updated-secret", File.ReadAllText(_testModelsFile));
        Assert.Empty(Directory.EnumerateFiles(_testDir, "*.tmp", SearchOption.AllDirectories));
    }
}
