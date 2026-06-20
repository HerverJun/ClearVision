using System.Text.Json;
using ClearVision.Product.Core.ProjectVariables;
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
}
