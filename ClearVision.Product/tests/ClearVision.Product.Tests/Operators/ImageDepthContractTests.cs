using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Operators;

public sealed class ImageDepthContractTests
{
    [Fact]
    public void FormalRuntimeImageOperators_ShouldHaveAuthoritativeContracts()
    {
        var metadata = new OperatorFactory().GetAllMetadata().OrderBy(item => item.Type).ToList();
        var imageInputs = metadata
            .Where(item => item.InputPorts.Any(port => port.DataType == PortDataType.Image))
            .ToList();
        var imageOutputs = metadata
            .Where(item => item.OutputPorts.Any(port => port.DataType == PortDataType.Image))
            .ToList();

        metadata.Should().HaveCount(158);
        imageInputs.Should().HaveCount(97);
        imageOutputs.Should().HaveCount(98);

        foreach (var item in imageInputs)
        {
            var imagePortNames = item.InputPorts
                .Where(port => port.DataType == PortDataType.Image)
                .Select(port => port.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            item.ImageInputContracts.Select(contract => contract.InputPort)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Should().Equal(imagePortNames, item.Type.ToString());

            foreach (var contract in item.ImageInputContracts)
            {
                contract.ContractVersion.Should().Be(OperatorImageContractResolver.ContractVersion);
                contract.FailureCode.Should().NotBeNullOrWhiteSpace();
                contract.InputDepthPolicy.Should().NotBeNullOrWhiteSpace();
                contract.ImplicitConversionPolicy.Should().NotBeNullOrWhiteSpace();
                contract.OutputDepthPolicy.Should().NotBeNullOrWhiteSpace();
                contract.DynamicRangePolicy.Should().NotBeNullOrWhiteSpace();
                contract.NonFinitePolicy.Should().NotBeNullOrWhiteSpace();
                contract.EvidenceLevel.Should().NotBeNullOrWhiteSpace();
            }
        }

        imageInputs
            .Where(item => item.Lifecycle == OperatorLifecycle.Stable)
            .SelectMany(item => item.ImageInputContracts.Select(contract => (item.Type, contract)))
            .Should()
            .OnlyContain(item => item.contract.Status != ImageContractStatus.Unknown);
    }

    [Fact]
    public void ConvertedContracts_ShouldDeclareExplicitPoliciesWithoutGenericImplicitMinMax()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToList();
        var contracts = metadata.SelectMany(item => item.ImageInputContracts.Select(contract => (item.Type, contract)));

        foreach (var (type, contract) in contracts)
        {
            if (type != OperatorType.ImageNormalize)
            {
                contract.ImplicitConversionPolicy.Should().NotContainEquivalentOf("MinMax");
            }

            foreach (var rule in contract.ModeRestrictions.Where(rule => rule.Status == ImageContractStatus.Converted))
            {
                rule.ConversionPolicy.Should().NotBeNullOrWhiteSpace();
                rule.OutputDepthPolicy.Should().NotBeNullOrWhiteSpace();
                rule.DynamicRangePolicy.Should().NotBeNullOrWhiteSpace();
                rule.FailureCode.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public void PriorityContracts_ShouldExposeEvidenceBackedModeRestrictions()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToDictionary(item => item.Type);

        metadata[OperatorType.Thresholding].ImageInputContracts.Single().ModeRestrictions
            .Select(rule => rule.Mode)
            .Should().Contain(["Otsu", "Triangle"]);
        metadata[OperatorType.Filtering].ImageInputContracts.Single().ModeRestrictions
            .Select(rule => rule.Mode)
            .Should().BeEquivalentTo(["Gaussian", "Mean", "Median", "Bilateral"]);
        metadata[OperatorType.HistogramAnalysis].ImageInputContracts.Single().SupportedDepths
            .Should().Equal("CV_8U");
        metadata[OperatorType.SharpnessEvaluation].ImageInputContracts.Single().ModeRestrictions
            .Select(rule => rule.Mode)
            .Should().BeEquivalentTo(["Laplacian", "Brenner", "Tenengrad", "SMD"]);
    }
}
