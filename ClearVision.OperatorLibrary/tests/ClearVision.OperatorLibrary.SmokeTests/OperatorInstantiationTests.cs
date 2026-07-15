using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.OperatorLibrary.SmokeTests;

[TestClassification(TestDomain.OperatorLibrary, TestPurpose.Smoke, TestLane.Pr, TestEvidenceType.PackageSmoke, TestOracleType.Contract, TestResourceRequirement.PackageFeed, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-library", Suites = "OperatorLibrarySmoke")]
public class OperatorInstantiationTests
{
    [Fact]
    public void MeanFilterOperator_ShouldInstantiateFromNuGetPackage()
    {
        var op = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);

        Assert.Equal(OperatorType.MeanFilter, op.OperatorType);
    }
}
