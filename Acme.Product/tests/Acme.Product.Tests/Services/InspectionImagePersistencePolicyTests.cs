using Acme.Product.Application.Services;
using Acme.Product.Core.Enums;
using FluentAssertions;

namespace Acme.Product.Tests.Services;

public sealed class InspectionImagePersistencePolicyTests
{
    [Theory]
    [InlineData(null, InspectionStatus.NG, true)]
    [InlineData(null, InspectionStatus.OK, false)]
    [InlineData("None", InspectionStatus.NG, false)]
    [InlineData("All", InspectionStatus.OK, true)]
    [InlineData("All", InspectionStatus.Error, true)]
    [InlineData("NgOnly", InspectionStatus.NG, true)]
    [InlineData(" ngonly ", InspectionStatus.OK, false)]
    [InlineData("unexpected", InspectionStatus.NG, true)]
    [InlineData("unexpected", InspectionStatus.Error, false)]
    public void ShouldPersistImage_ShouldApplyConfiguredPolicy(
        string? savePolicy,
        InspectionStatus status,
        bool expected)
    {
        var shouldPersist = InspectionImagePersistencePolicy.ShouldPersistImage(savePolicy, status);

        shouldPersist.Should().Be(expected);
    }
}
