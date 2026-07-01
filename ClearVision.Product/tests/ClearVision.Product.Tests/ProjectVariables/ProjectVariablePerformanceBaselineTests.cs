using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Core.ProjectVariables;
using FluentAssertions;
using Xunit.Abstractions;

namespace ClearVision.Product.Tests.ProjectVariables;

public sealed class ProjectVariablePerformanceBaselineTests
{
    private readonly ITestOutputHelper _output;

    public ProjectVariablePerformanceBaselineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BindingIndexAndSession_ShouldHandleBaselineShape()
    {
        const int variableCount = 100;
        const int targetBindingCount = 500;
        const int runCount = 10_000;
        var variableIds = Enumerable.Range(0, variableCount).Select(_ => Guid.NewGuid()).ToArray();
        var operatorIds = Enumerable.Range(0, targetBindingCount).Select(_ => Guid.NewGuid()).ToArray();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = variableIds.Select((id, index) => new ProjectGlobalVariableDefinition
            {
                Id = id,
                Name = $"perf.v{index}",
                DisplayName = $"Perf {index}",
                ValueType = ProjectGlobalVariableValueType.Int64,
                InitialValue = JsonSerializer.SerializeToElement(0L),
                Order = index
            }).ToList(),
            TargetBindings = operatorIds.Select((operatorId, index) => new ProjectGlobalVariableTargetBinding
            {
                Id = Guid.NewGuid(),
                VariableId = variableIds[index % variableCount],
                OperatorId = operatorId,
                ParameterId = Guid.NewGuid(),
                OperatorName = $"Operator {index}",
                ParameterName = "Value"
            }).ToList()
        };

        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var index = ProjectVariableBindingIndex.Build(schema);
        using var session = new ProjectVariableSession(schema);
        var lookupCount = 0;
        for (var run = 0; run < runCount; run++)
        {
            foreach (var operatorId in operatorIds)
            {
                foreach (var binding in index.GetTargets(operatorId))
                {
                    session.TryGetValue(binding.VariableId, out _).Should().BeTrue();
                    lookupCount++;
                }
            }

            session.Increment(variableIds[run % variableCount], 1, ProjectVariableUpdatedBy.VariableIncrement);
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
        _output.WriteLine(
            "Project variable baseline: variables={0}, targets={1}, runs={2}, lookups={3}, elapsedMs={4}, allocatedBytes={5}",
            variableCount,
            targetBindingCount,
            runCount,
            lookupCount,
            stopwatch.ElapsedMilliseconds,
            allocated);

        lookupCount.Should().Be(targetBindingCount * runCount);
        session.GetSnapshots().Should().HaveCount(variableCount);
        index.HasBindings.Should().BeTrue();
    }
}
