using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
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

        metadata.Should().HaveCount(
            OperatorExposureCatalog.Entries.Count(entry =>
                entry.Exposure is OperatorExposure.PackagePublic or OperatorExposure.PackageInternal));
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
                contract.Variants.Should().NotBeEmpty();
                contract.Variants.Should().OnlyContain(variant =>
                    !string.IsNullOrWhiteSpace(variant.Mode) &&
                    !string.IsNullOrWhiteSpace(variant.Depth) &&
                    variant.Channels > 0 &&
                    !string.IsNullOrWhiteSpace(variant.Condition) &&
                    !string.IsNullOrWhiteSpace(variant.EvidenceLevel));
                contract.Variants
                    .GroupBy(variant => (variant.Mode, variant.Depth, variant.Channels))
                    .Should().OnlyContain(group => group.Count() == 1);

                var allowed = contract.Variants
                    .Where(variant => variant.Admission == ImageContractAdmission.Allowed)
                    .ToArray();
                contract.SupportedDepths.OrderBy(value => value, StringComparer.Ordinal)
                    .Should().Equal(allowed.Select(variant => variant.Depth)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal));
                contract.SupportedChannels.OrderBy(value => value)
                    .Should().Equal(allowed.Select(variant => variant.Channels)
                        .Distinct()
                        .OrderBy(value => value));
            }
        }
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

            foreach (var rule in contract.Variants.Where(rule =>
                         rule.Verification == ImageContractVerification.VerifiedConversion))
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

        metadata[OperatorType.Thresholding].ImageInputContracts.Single().Variants
            .Select(rule => rule.Mode)
            .Should().Contain(["Fixed", "Otsu", "Triangle"]);
        metadata[OperatorType.Filtering].ImageInputContracts.Single().Variants
            .Select(rule => rule.Mode)
            .Should().Contain(["Gaussian", "Mean", "Median:Identity", "Median:Kernel3Or5", "Median:Kernel7Plus", "Bilateral"]);
        metadata[OperatorType.HistogramAnalysis].ImageInputContracts.Single().SupportedDepths
            .Should().Equal("CV_8U");
        metadata[OperatorType.SharpnessEvaluation].ImageInputContracts.Single().Variants
            .Select(rule => rule.Mode)
            .Should().Contain(mode => mode.StartsWith("Laplacian:", StringComparison.Ordinal));
    }

    [Fact]
    public void ConservativeDefault_ShouldDiscloseUnverifiedLegacyCompatibility()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToList();
        var compatibilityVariants = metadata
            .Where(item => item.InputPorts.Any(port => port.DataType == PortDataType.Image))
            .SelectMany(item => item.ImageInputContracts)
            .SelectMany(contract => contract.Variants)
            .Where(variant => variant.EvidenceLevel == "E0_SOURCE_AUDIT")
            .ToList();

        compatibilityVariants.Should().NotBeEmpty();
        compatibilityVariants.Should().OnlyContain(variant =>
            variant.Admission == ImageContractAdmission.Allowed &&
            variant.Verification == ImageContractVerification.LegacyCompatibilityAllowance &&
            variant.Depth == "CV_8U" &&
            (variant.Channels == 1 || variant.Channels == 3 || variant.Channels == 4));
        compatibilityVariants.Should().NotContain(variant =>
            variant.Verification == ImageContractVerification.VerifiedSupport ||
            variant.Verification == ImageContractVerification.VerifiedConversion);
    }

    [Fact]
    public void ExactVariants_ShouldNotInferDepthChannelCartesianProducts()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToDictionary(item => item.Type);
        var threshold = metadata[OperatorType.Thresholding].ImageInputContracts.Single();

        threshold.Variants.Should().ContainSingle(variant =>
            variant.Mode == "Fixed" &&
            variant.Depth == "CV_64F" &&
            variant.Channels == 1 &&
            variant.Admission == ImageContractAdmission.Allowed);
        threshold.Variants.Should().ContainSingle(variant =>
            variant.Mode == "Fixed" &&
            variant.Depth == "CV_64F" &&
            variant.Channels == 3 &&
            variant.Admission == ImageContractAdmission.Rejected &&
            variant.Verification == ImageContractVerification.VerifiedRejection);
    }

    [Fact]
    public void ShadingCorrectionContracts_ShouldResolveMethodColorModeAndExactInputType()
    {
        var metadata = new OperatorFactory().GetMetadata(OperatorType.ShadingCorrection)!;
        var image = metadata.ImageInputContracts.Single(contract => contract.InputPort == "Image");
        var background = metadata.ImageInputContracts.Single(contract => contract.InputPort == "Background");

        image.Variants.Select(variant => variant.Mode).Distinct().Should().BeEquivalentTo(
            "DivideByBackground:LumaOnly",
            "DivideByBackground:PerChannel",
            "GaussianModel:LumaOnly",
            "GaussianModel:PerChannel",
            "MorphologicalTopHat:LumaOnly",
            "MorphologicalTopHat:PerChannel");
        image.Variants.Should().ContainSingle(variant =>
            variant.Mode == "GaussianModel:LumaOnly" &&
            variant.Depth == "CV_64F" &&
            variant.Channels == 3 &&
            variant.Admission == ImageContractAdmission.Rejected &&
            variant.Verification == ImageContractVerification.VerifiedRejection);
        image.Variants.Should().ContainSingle(variant =>
            variant.Mode == "GaussianModel:PerChannel" &&
            variant.Depth == "CV_64F" &&
            variant.Channels == 3 &&
            variant.Admission == ImageContractAdmission.Allowed &&
            variant.OutputDepthPolicy.Contains("CV_64F", StringComparison.Ordinal));

        background.Variants.Should().ContainSingle(variant =>
            variant.Mode == "GaussianModel:LumaOnly" &&
            variant.Depth == "CV_8U" &&
            variant.Channels == 3 &&
            variant.Admission == ImageContractAdmission.Rejected);
        background.Variants.Should().ContainSingle(variant =>
            variant.Mode == "DivideByBackground:PerChannel" &&
            variant.Depth == "CV_64F" &&
            variant.Channels == 3 &&
            variant.Admission == ImageContractAdmission.Allowed);
    }

    [Fact]
    public void CompactPresentation_ShouldPreserveExactPairsAndEvidenceSemantics()
    {
        var metadata = new OperatorFactory().GetAllMetadata().ToDictionary(item => item.Type);
        var threshold = metadata[OperatorType.Thresholding].ImageInputContracts.Single().Presentation;
        var fixedAllowed = threshold.ExactVariantGroups
            .Where(group => group.Mode == "Fixed" && group.Admission == ImageContractAdmission.Allowed)
            .SelectMany(group => group.ExactInputTypes)
            .ToHashSet(StringComparer.Ordinal);

        fixedAllowed.Should().Contain("CV_64FC1");
        fixedAllowed.Should().NotContain("CV_64FC3");
        threshold.ExactVariantGroups.Should().Contain(group =>
            group.Mode == "Fixed" &&
            group.Admission == ImageContractAdmission.Rejected &&
            group.ExactInputTypes.Contains("CV_64FC3", StringComparer.Ordinal));

        var compatibilityOnly = metadata.Values
            .SelectMany(item => item.ImageInputContracts)
            .Select(contract => contract.Presentation)
            .First(item => item.CompatibilityOnly);
        compatibilityOnly.HasProductionSupport.Should().BeFalse();
        compatibilityOnly.VerifiedSupportVariantCount.Should().Be(0);
        compatibilityOnly.VerifiedConversionVariantCount.Should().Be(0);
        compatibilityOnly.EvidenceSummary.Should().Be(
            ImageContractPresentationBuilder.LegacyCompatibilityNotice);
    }

    [Fact]
    public void LegacyPublicContractSurface_ShouldRemainAvailableWithoutChangingJsonShape()
    {
#pragma warning disable CS0618
        var legacy = new ImageInputContract(
            "Image",
            ["CV_8U"],
            [1],
            ["CV_8U"],
            "Legacy",
            "None",
            "Preserve",
            "8-bit",
            [],
            "NotApplicable",
            "IMAGE_DEPTH_UNSUPPORTED",
            OperatorImageContractResolver.ContractVersion,
            ImageContractStatus.Restricted,
            "E0_SOURCE_AUDIT");

        legacy.Status.Should().Be(ImageContractStatus.Restricted);
        legacy.ModeRestrictions.Should().ContainSingle();
        legacy.EvidenceLevel.Should().Be("E0_SOURCE_AUDIT");
#pragma warning restore CS0618
        legacy.Variants.Should().ContainSingle(variant =>
            variant.Admission == ImageContractAdmission.Allowed &&
            variant.Verification == ImageContractVerification.LegacyCompatibilityAllowance);

        var json = System.Text.Json.JsonSerializer.Serialize(legacy);
        json.Should().Contain("\"Variants\"");
        json.Should().NotContain("\"Presentation\"");
        json.Should().NotContain("\"Status\"");
        json.Should().NotContain("\"ModeRestrictions\"");
    }
}
