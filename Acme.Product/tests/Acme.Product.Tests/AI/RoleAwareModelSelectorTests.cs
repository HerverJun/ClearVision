using Acme.Product.Infrastructure.AI;
using Acme.Product.Infrastructure.AI.Runtime;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public sealed class RoleAwareModelSelectorTests
{
    [Fact]
    public void SelectModelForRole_WhenRoleBindingExists_ReturnsBoundModel()
    {
        var generationModel = CreateModel("gen-1", isActive: true, priority: 10, roleBindings: new[] { "generation" });
        var reasoningModel = CreateModel("reason-1", isActive: false, priority: 20, roleBindings: new[] { "reasoning" });
        var registry = new FakeModelRegistry(generationModel, reasoningModel);
        var sut = new RoleAwareAiModelSelector(registry);

        var result = sut.SelectModelForRole("reasoning");

        result.Id.Should().Be("reason-1");
    }

    [Fact]
    public void SelectModelForRole_WhenNoRoleBinding_ReturnsActiveModel()
    {
        var activeModel = CreateModel("active-1", isActive: true, priority: 10, roleBindings: new[] { "generation" });
        var registry = new FakeModelRegistry(activeModel);
        var sut = new RoleAwareAiModelSelector(registry);

        var result = sut.SelectModelForRole("vision");

        result.Id.Should().Be("active-1");
    }

    [Fact]
    public void SelectModelForRole_WhenMultipleBindingsExist_ReturnsHighestPriority()
    {
        var lowPri = CreateModel("low", isActive: false, priority: 50, roleBindings: new[] { "fallback" });
        var highPri = CreateModel("high", isActive: false, priority: 10, roleBindings: new[] { "fallback" });
        var gen = CreateModel("gen", isActive: true, priority: 100, roleBindings: new[] { "generation" });
        var registry = new FakeModelRegistry(gen, lowPri, highPri);
        var sut = new RoleAwareAiModelSelector(registry);

        var result = sut.SelectModelForRole("fallback");

        result.Id.Should().Be("high");
    }

    [Fact]
    public void SelectGenerationModel_WhenRoleBindingExists_PrefersBoundModel()
    {
        var boundModel = CreateModel("bound", isActive: false, priority: 5, roleBindings: new[] { "generation" });
        var activeModel = CreateModel("active", isActive: true, priority: 100, roleBindings: new[] { "fallback" });
        var registry = new FakeModelRegistry(activeModel, boundModel);
        var sut = new RoleAwareAiModelSelector(registry);

        var result = sut.SelectGenerationModel();

        result.Id.Should().Be("bound");
    }

    [Fact]
    public void SelectGenerationModel_WhenGenerationBindingsHaveSamePriority_PrefersActiveModel()
    {
        var oldModel = CreateModel("old", isActive: false, priority: 100, roleBindings: new[] { "generation" });
        var activeModel = CreateModel("active", isActive: true, priority: 100, roleBindings: new[] { "generation" });
        var registry = new FakeModelRegistry(oldModel, activeModel);
        var sut = new RoleAwareAiModelSelector(registry);

        var result = sut.SelectGenerationModel();

        result.Id.Should().Be("active");
    }

    [Fact]
    public void ActiveAiModelSelector_SelectModelForRole_AlwaysReturnsActive()
    {
        var activeModel = CreateModel("active", isActive: true, priority: 10, roleBindings: new[] { "generation" });
        var otherModel = CreateModel("other", isActive: false, priority: 5, roleBindings: new[] { "reasoning" });
        var registry = new FakeModelRegistry(activeModel, otherModel);
        var sut = new ActiveAiModelSelector(registry);

        var result = sut.SelectModelForRole("reasoning");

        result.Id.Should().Be("active");
    }

    [Fact]
    public void SelectModelForRoleWithReason_WhenRoleBindingExists_ReturnsRoleBindingReason()
    {
        var genModel = CreateModel("gen", isActive: true, priority: 10, roleBindings: new[] { "generation" });
        var visionModel = CreateModel("vision", isActive: false, priority: 5, roleBindings: new[] { "vision" });
        var registry = new FakeModelRegistry(genModel, visionModel);
        var sut = new RoleAwareAiModelSelector(registry);

        var (model, reason) = sut.SelectModelForRoleWithReason("vision");

        model.Id.Should().Be("vision");
        reason.Should().Be("role_binding:vision");
    }

    [Fact]
    public void SelectModelForRoleWithReason_WhenNoRoleBinding_ReturnsActiveReason()
    {
        var activeModel = CreateModel("active", isActive: true, priority: 10, roleBindings: new[] { "generation" });
        var registry = new FakeModelRegistry(activeModel);
        var sut = new RoleAwareAiModelSelector(registry);

        var (model, reason) = sut.SelectModelForRoleWithReason("vision");

        model.Id.Should().Be("active");
        reason.Should().Be("active");
    }

    [Fact]
    public void ActiveAiModelSelector_SelectModelForRoleWithReason_ReturnsActiveReason()
    {
        var activeModel = CreateModel("active", isActive: true, priority: 10, roleBindings: new[] { "generation" });
        var registry = new FakeModelRegistry(activeModel);
        var sut = new ActiveAiModelSelector(registry);

        var (model, reason) = sut.SelectModelForRoleWithReason("reasoning");

        model.Id.Should().Be("active");
        reason.Should().Be("active");
    }

    private static AiModelConfig CreateModel(string id, bool isActive, int priority, string[] roleBindings)
    {
        return new AiModelConfig
        {
            Id = id,
            Name = $"Model {id}",
            IsActive = isActive,
            Priority = priority,
            RoleBindings = roleBindings.ToList(),
            Model = "test-model",
            Provider = "OpenAI Compatible"
        };
    }

    private sealed class FakeModelRegistry : IAiModelRegistry
    {
        private readonly List<AiModelConfig> _models;

        public FakeModelRegistry(params AiModelConfig[] models)
        {
            _models = models.ToList();
        }

        public AiModelConfig GetActiveModel()
        {
            return _models.FirstOrDefault(m => m.IsActive) ?? _models.First();
        }

        public IReadOnlyList<AiModelConfig> GetAllModels()
        {
            return _models;
        }
    }
}
