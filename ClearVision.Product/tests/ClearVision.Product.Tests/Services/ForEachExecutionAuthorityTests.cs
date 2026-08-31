using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class ForEachExecutionAuthorityTests
{
    [Fact]
    public void CapabilityAndResourceManifests_ShouldRecursivelyIncludeForEachChild()
    {
        var child = CreateHttpChild();
        var outer = CreateOuterFlow(child);
        var resourceKey = $"Resource:{child.Operators.Single().Id:N}";

        var capabilities = ExecutionCapabilityManifest.Derive(outer).Capabilities;
        var bindings = ExecutionResourceBindingManifest.Build(
            outer,
            "StoredProject",
            new Dictionary<string, string> { ["ProjectRevision"] = "5" });
        var scoped = ExecutionResourceBindingManifest.TryScopeToFlow(
            child,
            bindings,
            out var childBindings,
            out _,
            out _);

        capabilities.Should().HaveFlag(ExecutionSideEffect.NetworkWrite);
        bindings.Should().ContainKey(resourceKey);
        scoped.Should().BeTrue();
        childBindings.Should().ContainKey(resourceKey);
        childBindings.Keys.Count(key => key.StartsWith("Resource:", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void ChildResourceScoping_ShouldRejectEvidenceMissingFromOuterSnapshot()
    {
        var child = CreateHttpChild();

        var scoped = ExecutionResourceBindingManifest.TryScopeToFlow(
            child,
            new Dictionary<string, string> { ["ProjectRevision"] = "5" },
            out _,
            out var code,
            out _);

        scoped.Should().BeFalse();
        code.Should().Be("ADMISSION_NESTED_RESOURCE_BINDING_REQUIRED");
    }

    [Fact]
    public async Task GovernedOuterExecution_ShouldValidateNestedGraphBeforeRawDispatch()
    {
        var child = CreateHttpChild();
        var outer = CreateOuterFlow(child);
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(call =>
        {
            var flow = call.Arg<OperatorFlow>();
            return flow.Name == child.Name
                ? new FlowValidationResult { IsValid = false, Errors = ["nested-invalid"] }
                : new FlowValidationResult { IsValid = true };
        });
        var service = new GovernedFlowExecutionService(engine);
        var snapshot = CreateStoredSnapshot(outer);

        var result = await service.ExecuteWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("nested-invalid");
        engine.Received(2).ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.DidNotReceive().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GovernedOuterExecution_ShouldRejectNestedResourceBeforeEngineValidation()
    {
        var child = CreateHttpChild("file:///client/forged");
        var outer = CreateOuterFlow(child);
        var engine = Substitute.For<IFlowExecutionEngine>();
        var configuration = Substitute.For<IConfigurationService>();
        var config = new AppConfig();
        config.Normalize();
        configuration.GetCurrent().Returns(config);
        var service = new GovernedFlowExecutionService(
            engine,
            new ServerExecutionResourceAuthority(configuration));

        var result = await service.ExecuteWithSnapshotAsync(CreateStoredSnapshot(outer));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RESOURCE_HTTP_DESTINATION_INVALID");
        engine.DidNotReceive().ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.DidNotReceive().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    private static ExecutionSnapshot CreateStoredSnapshot(OperatorFlow flow)
    {
        const long revision = 5;
        var bindings = ExecutionResourceBindingManifest.Build(
            flow,
            "StoredProject",
            new Dictionary<string, string> { ["ProjectRevision"] = revision.ToString() });
        return new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            bindings,
            principal: ExecutionPrincipal.System("foreach-authority-test"));
    }

    private static OperatorFlow CreateOuterFlow(OperatorFlow child)
    {
        var outer = new OperatorFlow("Outer");
        var forEach = new Operator(Guid.NewGuid(), "ForEach", OperatorType.ForEach, 0, 0);
        forEach.AddParameter(new Parameter(
            Guid.NewGuid(),
            "SubGraph",
            "SubGraph",
            string.Empty,
            "object",
            child,
            isRequired: true));
        outer.AddOperator(forEach);
        return outer;
    }

    private static OperatorFlow CreateHttpChild(string url = "https://approved.example.test/resource")
    {
        var child = new OperatorFlow("Child");
        var http = new Operator(Guid.NewGuid(), "Http", OperatorType.HttpRequest, 0, 0);
        http.AddParameter(new Parameter(
            Guid.NewGuid(),
            "Url",
            "Url",
            string.Empty,
            "string",
            url));
        child.AddOperator(http);
        return child;
    }
}
