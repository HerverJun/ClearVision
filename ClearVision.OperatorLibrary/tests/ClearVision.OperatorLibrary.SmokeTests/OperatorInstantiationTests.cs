using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.OperatorLibrary.SmokeTests;

public class OperatorInstantiationTests
{
    [Fact]
    public void MeanFilterOperator_ShouldInstantiateFromNuGetPackage()
    {
        var op = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        Assert.Equal(OperatorType.MeanFilter, op.OperatorType);
    }
}
