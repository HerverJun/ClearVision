using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentPlanFidelity;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentPlanFidelityValidatorTests
{
    [Fact(DisplayName = "Plan fidelity should restore catalog-backed Canny and defect area without silent substitution")]
    public void Validate_ShouldRestoreCannyAndDefectAreaRequirements()
    {
        var plan = Plan(
            "检测金属表面划痕，必须使用 Canny，并输出划痕面积。",
            ["ImageAcquisition", "Thresholding", "BlobAnalysis", "ResultJudgment", "ResultOutput"]) with
        {
            Risks = ["目录没有专用 Canny 算子，只能使用 Thresholding 近似。"]
        };

        var result = new VisionAgentPlanFidelityValidator().Validate(
            new VisionAgentPlanModeRequest
            {
                Description = plan.OriginalUserPrompt,
                OriginalUserPrompt = plan.OriginalUserPrompt
            },
            plan);

        result.Assessment.Satisfied.Should().BeTrue();
        result.Assessment.Repaired.Should().BeTrue();
        result.Assessment.RequiredCapabilities.Should().ContainSingle(item =>
            item.OperatorType == "EdgeDetection" &&
            item.ParameterName == "Method" &&
            item.RequiredValue == "Canny");
        result.Assessment.RequiredOutputSemantics.Should().Contain("defect_area");
        result.Assessment.Evidence.Should().Contain("catalog_parameter_present:EdgeDetection.Method=Canny");
        result.Route.Operators.Should().ContainInOrder(
            "ImageAcquisition",
            "Thresholding",
            "EdgeDetection",
            "SurfaceDefectDetection",
            "BlobAnalysis",
            "ResultJudgment",
            "ResultOutput");
        result.Risks.Should().NotContain(item => item.Contains("没有专用 Canny", StringComparison.Ordinal));
        result.Warnings.Should().Contain("planner_catalog_fact_corrected:canny");
    }

    [Fact(DisplayName = "Plan fidelity should not treat BlobCount as promised defect area")]
    public void Validate_ShouldRequireRealAreaProducer()
    {
        var plan = Plan(
            "检测划痕并计算面积",
            ["ImageAcquisition", "BlobAnalysis", "ResultJudgment", "ResultOutput"]);

        var result = new VisionAgentPlanFidelityValidator().Validate(
            new VisionAgentPlanModeRequest { Description = plan.OriginalUserPrompt },
            plan);

        result.Route.Operators.Should().Contain("SurfaceDefectDetection");
        result.Assessment.AvailableOutputSemantics.Should().Contain("defect_area");
        result.Assessment.Evidence.Should().NotContain(item => item.Contains("BlobCount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Plan fidelity should fail closed when a named catalog capability cannot be proven")]
    public void Validate_ShouldFailClosedWhenCannyCatalogFactIsMissing()
    {
        var plan = Plan(
            "必须使用 Canny 检测边缘",
            ["ImageAcquisition", "Thresholding", "ResultJudgment", "ResultOutput"]);
        var catalog = new CatalogWithoutEdgeDetection();

        var result = new VisionAgentPlanFidelityValidator(catalog).Validate(
            new VisionAgentPlanModeRequest { Description = plan.OriginalUserPrompt },
            plan);

        result.Assessment.Satisfied.Should().BeFalse();
        result.Assessment.MissingCapabilities.Should().Contain("edge_detection_canny");
        result.Assessment.BlockingReasons.Should().Contain("plan_fidelity_missing_capability:edge_detection_canny");
        result.Route.Operators.Should().NotContain("EdgeDetection");
    }

    private static VisionAgentPlanModeResult Plan(string prompt, List<string> operators) => new()
    {
        PlanContractVersion = VisionAgentPlanContractVersions.V2,
        OriginalUserPrompt = prompt,
        Goal = prompt,
        Intent = AiVisionTaskTypes.SurfaceDefect,
        SemanticExtraction = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            TaskType = AiVisionTaskTypes.SurfaceDefect,
            MeasurementTarget = prompt.Contains("面积", StringComparison.Ordinal) ? "defect_area" : string.Empty,
            OutputTarget = prompt.Contains("面积", StringComparison.Ordinal) ? "defect_area" : string.Empty
        },
        RequirementMaturity = new AiRequirementMaturityResult
        {
            TaskType = AiVisionTaskTypes.SurfaceDefect,
            CanPlan = true,
            CanBuild = true
        },
        RecommendedRoute = new VisionAgentRecommendedRoute
        {
            RouteId = "surface_defect",
            Operators = operators
        }
    };

    private sealed class CatalogWithoutEdgeDetection : IVisionAgentOperatorContractCatalog
    {
        private readonly VisionAgentOperatorContractCatalog _inner = new();

        public IReadOnlyCollection<string> OperatorTypes => _inner.OperatorTypes
            .Where(type => !type.Equals("EdgeDetection", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public IReadOnlyCollection<VisionAgentOperatorContract> Operators => _inner.Operators
            .Where(item => !item.OperatorType.Equals("EdgeDetection", StringComparison.OrdinalIgnoreCase))
            .ToList();

        public bool TryGet(string operatorType, out VisionAgentOperatorContract contract)
        {
            if (operatorType.Equals("EdgeDetection", StringComparison.OrdinalIgnoreCase))
            {
                contract = null!;
                return false;
            }

            return _inner.TryGet(operatorType, out contract!);
        }

        public string CanonicalizeOperatorType(string operatorType) => _inner.CanonicalizeOperatorType(operatorType);
    }
}
