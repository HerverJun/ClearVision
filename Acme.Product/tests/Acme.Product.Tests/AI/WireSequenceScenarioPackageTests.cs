using System.Text.Json;
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.AI;
using Acme.Product.Infrastructure.Services;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public class WireSequenceScenarioPackageTests
{
    [Fact]
    public void ScenarioPackage_ShouldAlignTemplateManifestAndRule()
    {
        var repoRoot = ResolveRepoRoot();
        var packageRoot = ResolveScenarioPackageRoot(repoRoot);
        var templatePath = Path.Combine(packageRoot, "template", "terminal-wire-sequence.flow.template.json");
        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        var rulePath = Path.Combine(packageRoot, "rules", "sequence-rule.v1.json");

        using var template = JsonDocument.Parse(File.ReadAllText(templatePath));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var rule = JsonDocument.Parse(File.ReadAllText(rulePath));

        manifest.RootElement.GetProperty("Version").GetString().Should().Be("1.6.0");
        manifest.RootElement.GetProperty("Constraints").GetProperty("RequiredResources")
            .EnumerateArray().Select(item => item.GetString())
            .Should().Equal("DeepLearning.ModelPath");

        var assetVersions = manifest.RootElement.GetProperty("Assets").EnumerateArray()
            .Where(item => item.GetProperty("ArtifactName").GetString() != "terminal-wire-sequence-video-stream-template")
            .ToDictionary(
                item => item.GetProperty("ArtifactType").GetString()!,
                item => item.GetProperty("ArtifactVersion").GetString()!);
        assetVersions["Template"].Should().Be("1.6.0");
        assetVersions["Model"].Should().Be("1.3.0");
        assetVersions["Rule"].Should().Be("1.6.0");
        var modelAsset = manifest.RootElement.GetProperty("Assets").EnumerateArray()
            .Single(item => item.GetProperty("ArtifactType").GetString() == "Model");
        modelAsset.GetProperty("ArtifactName").GetString().Should().Be("wire-seq-yolo-nms");
        modelAsset.GetProperty("RelativePath").GetString().Should().Be("models/wire-seq-yolo-nms-v1.3.onnx");
        modelAsset.GetProperty("Metadata").GetProperty("outputFormat").GetString().Should().Be("EndToEndNms");

        template.RootElement.GetProperty("requiredResources").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("DeepLearning.ModelPath");
        template.RootElement.GetProperty("tunableParameters").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("DeepLearning.Confidence");

        var operatorTypes = template.RootElement.GetProperty("operators").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .ToList();
        operatorTypes.Should().Equal(
            "ImageAcquisition",
            "DeepLearning",
            "DetectionSequenceJudge",
            "ResultOutput");

        var connections = template.RootElement.GetProperty("connections").EnumerateArray().ToList();
        connections.Should().Contain(item =>
            item.GetProperty("source").GetString() == "op_2" &&
            item.GetProperty("sourcePort").GetString() == "Objects" &&
            item.GetProperty("target").GetString() == "op_3" &&
            item.GetProperty("targetPort").GetString() == "Detections");
        connections.Should().Contain(item =>
            item.GetProperty("source").GetString() == "op_2" &&
            item.GetProperty("sourcePort").GetString() == "PostprocessDiagnostics" &&
            item.GetProperty("target").GetString() == "op_4" &&
            item.GetProperty("targetPort").GetString() == "Data");
        connections.Should().Contain(item =>
            item.GetProperty("source").GetString() == "op_3" &&
            item.GetProperty("sourcePort").GetString() == "Diagnostics" &&
            item.GetProperty("target").GetString() == "op_4" &&
            item.GetProperty("targetPort").GetString() == "Result");
        connections.Should().Contain(item =>
            item.GetProperty("source").GetString() == "op_3" &&
            item.GetProperty("sourcePort").GetString() == "Message" &&
            item.GetProperty("target").GetString() == "op_4" &&
            item.GetProperty("targetPort").GetString() == "Text");

        var deepLearningParams = template.RootElement.GetProperty("operators").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "op_2")
            .GetProperty("params");
        deepLearningParams.GetProperty("EnableInternalNms").GetString().Should().Be("false");
        deepLearningParams.GetProperty("OutputFormat").GetString().Should().Be("EndToEndNms");
        deepLearningParams.GetProperty("Confidence").GetString().Should().Be("0.05");

        var judgeParams = template.RootElement.GetProperty("operators").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "op_3")
            .GetProperty("params");
        judgeParams.GetProperty("MinConfidence").GetString().Should().Be("0.0");

        var manifestSequence = manifest.RootElement.GetProperty("Constraints").GetProperty("ExpectedSequence")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        var ruleSequence = rule.RootElement.GetProperty("expectedSequence")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        var templateSequence = template.RootElement.GetProperty("expectedSequence")
            .EnumerateArray().Select(item => item.GetString()).ToArray();

        templateSequence.Should().Equal("Wire_Black", "Wire_Blue");
        manifestSequence.Should().Equal(templateSequence);
        ruleSequence.Should().Equal(templateSequence);
        rule.RootElement.GetProperty("requiredResources").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("DeepLearning.ModelPath");
        rule.RootElement.GetProperty("thresholdOwner").GetString().Should().Be("DeepLearning");
        rule.RootElement.GetProperty("minConfidence").GetDouble().Should().Be(0.0);
        rule.RootElement.GetProperty("sortBy").GetString().Should().Be("CenterY");
        rule.RootElement.GetProperty("direction").GetString().Should().Be("TopToBottom");

        judgeParams.GetProperty("ExpectedLabels").GetString().Should().Be("Wire_Black,Wire_Blue");
        judgeParams.GetProperty("SortBy").GetString().Should().Be("CenterY");
        judgeParams.GetProperty("Direction").GetString().Should().Be("TopToBottom");
    }

    [Fact]
    public void ScenarioPackage_VideoStreamTemplate_ShouldBeRegisteredAndUseFrameChangeTrigger()
    {
        var repoRoot = ResolveRepoRoot();
        var packageRoot = ResolveScenarioPackageRoot(repoRoot);
        var templatePath = Path.Combine(packageRoot, "template", "terminal-wire-sequence-video-stream.flow.template.json");
        var manifestPath = Path.Combine(packageRoot, "manifest.json");

        using var template = JsonDocument.Parse(File.ReadAllText(templatePath));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var videoAsset = manifest.RootElement.GetProperty("Assets").EnumerateArray()
            .Single(item => item.GetProperty("ArtifactName").GetString() == "terminal-wire-sequence-video-stream-template");
        videoAsset.GetProperty("ArtifactVersion").GetString().Should().Be("1.6.0");
        videoAsset.GetProperty("RelativePath").GetString()
            .Should().Be("template/terminal-wire-sequence-video-stream.flow.template.json");

        template.RootElement.GetProperty("scenarioKey").GetString()
            .Should().Be("wire-sequence-terminal-video-stream");
        template.RootElement.GetProperty("templateVersion").GetString().Should().Be("1.6.0");

        template.RootElement.GetProperty("operators").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .Should().Equal(
                "ImageAcquisition",
                "FrameChangeTrigger",
                "DeepLearning",
                "DetectionSequenceJudge",
                "ResultOutput");

        var triggerParams = template.RootElement.GetProperty("operators").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "FrameChangeTrigger")
            .GetProperty("params");
        triggerParams.GetProperty("Profile").GetString().Should().Be("line_fast_default");
        triggerParams.GetProperty("ShortCircuitWhenNotTriggered").GetString().Should().Be("true");

        template.RootElement.GetProperty("tunableParameters").EnumerateArray().Select(item => item.GetString())
            .Should().Contain("FrameChangeTrigger.MinChangeRatio");
        template.RootElement.GetProperty("tunableParameters").EnumerateArray().Select(item => item.GetString())
            .Should().Contain("DeepLearning.Confidence");

        template.RootElement.GetProperty("parametersNeedingReview").GetProperty("op_3")
            .EnumerateArray().Select(item => item.GetString())
            .Should().Contain("LabelsPath");

        var readiness = template.RootElement.GetProperty("productionReadiness");
        readiness.GetProperty("status").GetString().Should().Be("blockedUntilConfigured");
        readiness.GetProperty("blockingStages").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("run", "deploy", "publish");
        readiness.GetProperty("blockingParameters").GetProperty("op_2")
            .EnumerateArray().Select(item => item.GetString())
            .Should().Equal("RoiX", "RoiY", "RoiW", "RoiH");
        readiness.GetProperty("blockingParameters").GetProperty("op_3")
            .EnumerateArray().Select(item => item.GetString())
            .Should().Equal("ModelPath", "LabelsPath");
    }

    [Fact]
    public void ScenarioPackage_ActiveTemplates_ShouldUseDeclaredOperatorPorts()
    {
        var repoRoot = ResolveRepoRoot();
        var packageRoot = ResolveScenarioPackageRoot(repoRoot);
        var templatePaths = new[]
        {
            Path.Combine(packageRoot, "template", "terminal-wire-sequence.flow.template.json"),
            Path.Combine(packageRoot, "template", "terminal-wire-sequence-video-stream.flow.template.json")
        };
        var validator = new AiFlowValidator(new OperatorFactory());

        foreach (var templatePath in templatePaths)
        {
            using var template = JsonDocument.Parse(File.ReadAllText(templatePath));
            var flow = ConvertScenarioPackageTemplate(template.RootElement);

            var result = validator.Validate(flow);

            result.IsValid.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
            result.Diagnostics
                .Where(IsPortContractDiagnostic)
                .Should().BeEmpty($"{Path.GetFileName(templatePath)} should only use declared operator ports and compatible port types");
        }
    }

    [Fact]
    public void ScenarioPackage_1_3_Release_ShouldPointToFrozenArtifacts()
    {
        var repoRoot = ResolveRepoRoot();
        var packageRoot = ResolveScenarioPackageRoot(repoRoot);
        var releasePath = Path.Combine(packageRoot, "versions", "1.3.0", "release.json");
        using var release = JsonDocument.Parse(File.ReadAllText(releasePath));

        release.RootElement.GetProperty("ManifestPath").GetString().Should().Be("versions/1.3.0/manifest.json");

        var artifacts = release.RootElement.GetProperty("Artifacts").EnumerateArray().ToList();
        artifacts.Single(item => item.GetProperty("ArtifactType").GetString() == "Template")
            .GetProperty("RelativePath").GetString().Should().Be("versions/1.3.0/template/terminal-wire-sequence.flow.template.json");
        artifacts.Single(item => item.GetProperty("ArtifactType").GetString() == "Rule")
            .GetProperty("RelativePath").GetString().Should().Be("versions/1.3.0/rules/sequence-rule.v1.json");
        artifacts.Single(item => item.GetProperty("ArtifactType").GetString() == "Label")
            .GetProperty("RelativePath").GetString().Should().Be("versions/1.3.0/labels/labels.txt");

        var manifestPath = Path.Combine(packageRoot, "versions", "1.3.0", "manifest.json");
        var templatePath = Path.Combine(packageRoot, "versions", "1.3.0", "template", "terminal-wire-sequence.flow.template.json");
        var rulePath = Path.Combine(packageRoot, "versions", "1.3.0", "rules", "sequence-rule.v1.json");
        var labelsPath = Path.Combine(packageRoot, "versions", "1.3.0", "labels", "labels.txt");

        File.Exists(manifestPath).Should().BeTrue();
        File.Exists(templatePath).Should().BeTrue();
        File.Exists(rulePath).Should().BeTrue();
        File.Exists(labelsPath).Should().BeTrue();

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var template = JsonDocument.Parse(File.ReadAllText(templatePath));
        using var rule = JsonDocument.Parse(File.ReadAllText(rulePath));

        manifest.RootElement.GetProperty("Version").GetString().Should().Be("1.3.0");
        template.RootElement.GetProperty("templateVersion").GetString().Should().Be("1.3.0");
        rule.RootElement.GetProperty("ruleVersion").GetString().Should().Be("1.3.0");

        template.RootElement.GetProperty("operators").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .Should().Equal(
                "ImageAcquisition",
                "RoiManager",
                "ImageResize",
                "DeepLearning",
                "BoxNms",
                "DetectionSequenceJudge",
                "ResultOutput");

        template.RootElement.GetProperty("expectedSequence").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("Wire_Black", "Wire_Blue");
        rule.RootElement.GetProperty("direction").GetString().Should().Be("TopToBottom");
        File.ReadAllLines(labelsPath).Should().Equal("Wire_Blue", "Wire_Black");
    }

    [Fact]
    public void ScenarioPackage_Labels_ShouldTrackModelClassOrderSeparatelyFromExpectedSequence()
    {
        var repoRoot = ResolveRepoRoot();
        var packageRoot = ResolveScenarioPackageRoot(repoRoot);
        var templatePath = Path.Combine(packageRoot, "template", "terminal-wire-sequence.flow.template.json");
        var labelsPath = Path.Combine(packageRoot, "labels", "labels.txt");

        using var template = JsonDocument.Parse(File.ReadAllText(templatePath));

        var expectedSequence = template.RootElement.GetProperty("expectedSequence")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var modelLabelOrder = File.ReadAllLines(labelsPath);

        expectedSequence.Should().Equal("Wire_Black", "Wire_Blue");
        modelLabelOrder.Should().Equal("Wire_Blue", "Wire_Black");
        modelLabelOrder.Should().NotEqual(expectedSequence);
    }

    private static string ResolveScenarioPackageRoot(string repoRoot)
    {
        var packageRoot = EnumerateDirectoriesSafe(repoRoot, "scenario-package-wire-sequence")
            .SingleOrDefault();

        packageRoot.Should().NotBeNullOrWhiteSpace();
        return packageRoot!;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var acmeProduct = Path.Combine(current.FullName, "Acme.Product");
            var hasScenarioPackage = Directory.Exists(acmeProduct) &&
                EnumerateDirectoriesSafe(current.FullName, "scenario-package-wire-sequence").Any();
            if (hasScenarioPackage)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Failed to resolve repository root for wire-sequence scenario package tests.");
    }

    private static AiGeneratedFlowJson ConvertScenarioPackageTemplate(JsonElement root)
    {
        var flow = new AiGeneratedFlowJson
        {
            Explanation = root.GetProperty("description").GetString() ?? string.Empty
        };

        foreach (var op in root.GetProperty("operators").EnumerateArray())
        {
            var generated = new AiGeneratedOperator
            {
                TempId = op.GetProperty("id").GetString() ?? string.Empty,
                OperatorType = op.GetProperty("type").GetString() ?? string.Empty,
                DisplayName = op.GetProperty("name").GetString() ?? string.Empty
            };

            if (op.TryGetProperty("params", out var parameters))
            {
                foreach (var parameter in parameters.EnumerateObject())
                {
                    generated.Parameters[parameter.Name] = parameter.Value.ValueKind == JsonValueKind.String
                        ? parameter.Value.GetString() ?? string.Empty
                        : parameter.Value.GetRawText();
                }
            }

            flow.Operators.Add(generated);
        }

        foreach (var connection in root.GetProperty("connections").EnumerateArray())
        {
            flow.Connections.Add(new AiGeneratedConnection
            {
                SourceTempId = connection.GetProperty("source").GetString() ?? string.Empty,
                SourcePortName = connection.GetProperty("sourcePort").GetString() ?? string.Empty,
                TargetTempId = connection.GetProperty("target").GetString() ?? string.Empty,
                TargetPortName = connection.GetProperty("targetPort").GetString() ?? string.Empty
            });
        }

        return flow;
    }

    private static bool IsPortContractDiagnostic(AiValidationDiagnostic diagnostic)
    {
        return diagnostic.Code is
            "missing_source_operator" or
            "missing_target_operator" or
            "missing_output_port" or
            "missing_input_port" or
            "incompatible_port_type";
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root, string searchPattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.Equals(searchPattern, StringComparison.OrdinalIgnoreCase))
                {
                    yield return child;
                    continue;
                }

                if (ShouldSkipDirectory(name))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool ShouldSkipDirectory(string name)
    {
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("TestResults", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("node_modules", StringComparison.OrdinalIgnoreCase);
    }
}
