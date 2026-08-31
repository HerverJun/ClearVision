using System.Runtime.CompilerServices;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Calibration, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "execution-authority")]
public sealed class CalibrationAssetCandidateSafetyTests
{
    public static TheoryData<OperatorType, string> CalibrationOperators => new()
    {
        { OperatorType.CameraCalibration, "CameraCalibrationOperator.cs" },
        { OperatorType.FisheyeCalibration, "FisheyeCalibrationOperator.cs" },
        { OperatorType.StereoCalibration, "StereoCalibrationOperator.cs" },
        { OperatorType.NPointCalibration, "NPointCalibrationOperator.cs" },
        { OperatorType.TranslationRotationCalibration, "TranslationRotationCalibrationOperator.cs" },
        { OperatorType.HandEyeCalibration, "HandEyeCalibrationOperator.cs" }
    };

    [Theory]
    [MemberData(nameof(CalibrationOperators))]
    public void Metadata_DeclaresOpaqueAssetCandidateContract_AndDropsRawSavePaths(
        OperatorType operatorType,
        string _)
    {
        var metadata = new OperatorFactory().GetMetadata(operatorType);

        metadata.Should().NotBeNull();
        metadata!.Parameters.Should().ContainSingle(parameter =>
            parameter.Name == "CalibrationAssetId" &&
            parameter.DataType == "string");
        foreach (var legacyPathParameter in new[] { "CalibrationOutputPath", "SavePath" })
        {
            metadata.Parameters.Should().NotContain(parameter =>
                parameter.Name == legacyPathParameter);
        }
        metadata.OutputPorts.Should().ContainSingle(port =>
            port.Name == "CalibrationAssetId" &&
            port.DataType == PortDataType.String);
        metadata.OutputPorts.Should().ContainSingle(port =>
            port.Name == "CalibrationAssetCandidate" &&
            port.DataType == PortDataType.Boolean);
        metadata.OutputPorts.Should().ContainSingle(port =>
            port.Name == "CalibrationContentHash" &&
            port.DataType == PortDataType.String);
    }

    [Theory]
    [MemberData(nameof(CalibrationOperators))]
    public void OperatorSource_HasNoFilesystemWritePrimitive(
        OperatorType operatorType,
        string sourceFileName)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Operators",
            sourceFileName);
        var source = File.ReadAllText(sourcePath);

        foreach (var forbiddenPrimitive in new[]
                 {
                     "File.WriteAllText(",
                     "File.WriteAllBytes(",
                     "File.AppendAllText(",
                     "File.AppendAllLines(",
                     "File.Create(",
                     "File.OpenWrite(",
                     "File.Copy(",
                     "File.Move(",
                     "File.Delete(",
                     "Directory.CreateDirectory("
                 })
        {
            source.Should().NotContain(
                forbiddenPrimitive,
                $"{operatorType} must only emit a governed calibration asset candidate");
        }
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the ClearVision repository root.");
    }
}

internal static class CalibrationAssetCandidateAssertions
{
    public static void ShouldMatchGovernedSavePayload(
        IReadOnlyDictionary<string, object> output,
        string expectedAssetId)
    {
        output["CalibrationAssetId"].Should().Be(expectedAssetId);
        output["CalibrationAssetCandidate"].Should().Be(true);

        var calibrationData = output["CalibrationData"].Should().BeOfType<string>().Subject;
        using var payload = JsonDocument.Parse(calibrationData);
        output["CalibrationContentHash"].Should().Be(
            ProjectAssetJson.ComputePayloadHash(payload.RootElement));
    }
}
