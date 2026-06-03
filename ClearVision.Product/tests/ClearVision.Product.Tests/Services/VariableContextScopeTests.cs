using ClearVision.Product.Core.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class VariableContextScopeTests
{
    [Fact]
    public void BeginScope_ShouldIsolateVariablesAndCycleCount()
    {
        var context = new VariableContext();
        context.SetValue("shared", "global");
        context.IncrementCycleCount();

        using (context.BeginScope(new VariableContextScope(Guid.NewGuid(), Guid.NewGuid(), "single-run")))
        {
            context.GetValue<string>("shared").Should().BeNull();
            context.CycleCount.Should().Be(0);

            context.SetValue("shared", "scoped");
            context.IncrementCycleCount();

            context.GetValue<string>("shared").Should().Be("scoped");
            context.CycleCount.Should().Be(1);
            context.CurrentScope.ExecutionKind.Should().Be("single-run");
        }

        context.GetValue<string>("shared").Should().Be("global");
        context.CycleCount.Should().Be(1);
        context.CurrentScope.Should().Be(VariableContextScope.Global);
    }
}
