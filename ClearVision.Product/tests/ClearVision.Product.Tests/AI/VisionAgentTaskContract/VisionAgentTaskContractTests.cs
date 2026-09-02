using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using System.Text.RegularExpressions;

namespace ClearVision.Product.Tests.AI.VisionAgentTaskContract;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentTaskContractTests
{
    [Fact]
    public void Catalog_ShouldExposeExactlyEightCanonicalPrimaryTasksWithRouteContracts()
    {
        AiVisionTaskCatalog.PrimaryTasks.Select(task => task.CanonicalValue).Should().BeEquivalentTo(
        [
            AiVisionTaskTypes.PresenceAbsence,
            AiVisionTaskTypes.AttributeClassification,
            AiVisionTaskTypes.ObjectDetection,
            AiVisionTaskTypes.TemplateLocation,
            AiVisionTaskTypes.SurfaceDefect,
            AiVisionTaskTypes.GeometryMeasurement,
            AiVisionTaskTypes.WireSequence,
            AiVisionTaskTypes.CodeRecognition
        ]);
        AiVisionTaskCatalog.PrimaryTasks.Should().OnlyContain(task =>
            task.IsPrimaryTask && task.UiSelectable && !string.IsNullOrWhiteSpace(task.RouteContractKey));
        var allValues = AiVisionTaskCatalog.PrimaryTasks.SelectMany(task => task.AllValues).ToList();
        allValues.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(allValues.Count);
    }

    [Fact]
    public void FrontendTaskProjection_ShouldExactlyMatchAuthoritativeCatalog()
    {
        var source = ReadFrontendTaskContract();
        var projection = Regex.Matches(
                source,
                @"canonical:\s*'(?<canonical>[^']+)'\s*,\s*aliases:\s*Object\.freeze\(\[(?<aliases>[^\]]*)\]\)")
            .Cast<Match>()
            .ToDictionary(
                match => match.Groups["canonical"].Value,
                match => Regex.Matches(match.Groups["aliases"].Value, @"'(?<alias>[^']+)'")
                    .Cast<Match>()
                    .Select(alias => alias.Groups["alias"].Value)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        projection.Keys.Should().BeEquivalentTo(
            AiVisionTaskCatalog.PrimaryTasks.Select(task => task.CanonicalValue));
        foreach (var task in AiVisionTaskCatalog.PrimaryTasks)
        {
            projection[task.CanonicalValue].Should().BeEquivalentTo(task.Aliases);
        }
    }

    [Fact]
    public void FrontendAnswerOriginPriority_ShouldMatchBackendPolicy()
    {
        var source = ReadFrontendTaskContract();
        var block = Regex.Match(
            source,
            @"PLAN_ANSWER_ORIGIN_PRIORITY\s*=\s*Object\.freeze\(\{(?<entries>.*?)\}\)",
            RegexOptions.Singleline);
        block.Success.Should().BeTrue();
        var projection = Regex.Matches(
                block.Groups["entries"].Value,
                @"(?<origin>[a-z_]+)\s*:\s*(?<priority>\d+)")
            .Cast<Match>()
            .ToDictionary(
                match => match.Groups["origin"].Value,
                match => int.Parse(match.Groups["priority"].Value),
                StringComparer.OrdinalIgnoreCase);
        var expectedOrigins = new[]
        {
            VisionAgentPlanAnswerOrigins.ExplicitUserText,
            VisionAgentPlanAnswerOrigins.ExplicitUserSelection,
            VisionAgentPlanAnswerOrigins.ResourceBound,
            VisionAgentPlanAnswerOrigins.ModelInferred,
            VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault,
            VisionAgentPlanAnswerOrigins.RuleInferred,
            VisionAgentPlanAnswerOrigins.LegacyInferred,
            VisionAgentPlanAnswerOrigins.DefaultAssumption
        };
        var expected = expectedOrigins.ToDictionary(
            origin => origin,
            VisionAgentPlanFieldPolicy.AnswerOriginPriority,
            StringComparer.OrdinalIgnoreCase);

        projection.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("classification", AiVisionTaskTypes.AttributeClassification)]
    [InlineData("barcode_qr", AiVisionTaskTypes.CodeRecognition)]
    [InlineData("surface_or_pose_defect", AiVisionTaskTypes.SurfaceDefect)]
    [InlineData("template_matching", AiVisionTaskTypes.TemplateLocation)]
    [InlineData("measurement", AiVisionTaskTypes.GeometryMeasurement)]
    [InlineData("sequence_judgment", AiVisionTaskTypes.WireSequence)]
    [InlineData("presence_detection", AiVisionTaskTypes.PresenceAbsence)]
    public void Catalog_ShouldNormalizeCompatibilityAndRouteAliases(string raw, string expected)
    {
        AiVisionTaskCatalog.TryNormalizePrimary(raw, out var canonical).Should().BeTrue();
        canonical.Should().Be(expected);
    }

    [Theory]
    [InlineData("plc_output")]
    [InlineData("general_inspection")]
    [InlineData("custom_task")]
    [InlineData("made_up_task")]
    public void Catalog_ShouldRejectNonPrimaryOrUnknownTaskValues(string raw)
    {
        AiVisionTaskCatalog.TryNormalizePrimary(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void EveryCanonicalTask_ShouldUseCatalogRouteKeyAndPlanIntent()
    {
        foreach (var task in AiVisionTaskCatalog.PrimaryTasks)
        {
            VisionTaskRouteContractRegistry.NormalizeTaskType(task.CanonicalValue)
                .Should().Be(task.RouteContractKey);
            VisionAgentRequirementMaturityGate.ToPlanIntent(new AiRequirementMaturityResult
            {
                TaskType = task.CanonicalValue,
                CanPlan = true
            }).Should().Be(task.PlanIntent);
        }
    }

    [Fact]
    public void RuleInference_ShouldRemainLowerTrustAndTaskFreeTextNeedsEvidence()
    {
        VisionAgentPlanFieldPolicy.IsAuthoritativeConfirmationOrigin(VisionAgentPlanAnswerOrigins.RuleInferred)
            .Should().BeFalse();
        VisionAgentPlanFieldPolicy.IsAuthoritativeConfirmationOrigin(VisionAgentPlanAnswerOrigins.LegacyInferred)
            .Should().BeFalse();

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            ConflictPlan(),
            [new VisionAgentPlanAnswer
            {
                Field = VisionAgentPlanAnswerFields.TaskType,
                Value = AiVisionTaskTypes.SurfaceDefect,
                Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
            }],
            null,
            acceptedRecommendedDefaults: false);

        validation.RequirementAnswers.Should().NotContainKey(VisionAgentPlanAnswerFields.TaskType);
        validation.InvalidValues.Should().Contain(item => item.Contains("explicit_text_evidence_missing", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneralAndCustomFallbackOptions_ShouldDeferInsteadOfResolvingTaskType()
    {
        var question = VisionAgentPlanFieldPolicy.BuildFallbackQuestionsForRemaining(
                [VisionAgentPlanAnswerFields.TaskType])
            .Single();

        question.Options.Where(option => option.Value is "general_inspection" or "custom_task")
            .Should().OnlyContain(option =>
                VisionAgentPlanFieldPolicy.NormalizeAnswerEffect(option) ==
                VisionAgentClarificationAnswerEffects.Defer);
    }

    [Fact]
    public void MixedScratchAreaRequirement_ShouldKeepDefectTaskAndExposeAreaAsBusinessTarget()
    {
        const string prompt = "检测金属表面划痕并计算面积";
        var request = new VisionAgentRequirementMaturityRequest { Description = prompt };

        var maturity = VisionAgentRequirementMaturityGate.Evaluate(request);
        var answers = VisionAgentRequirementMaturityGate.ExtractExplicitPlanAnswers(request, null);

        maturity.TaskType.Should().Be(AiVisionTaskTypes.SurfaceDefect);
        answers.Should().Contain(answer =>
            answer.Field == VisionAgentPlanAnswerFields.TaskType &&
            answer.Value == AiVisionTaskTypes.SurfaceDefect &&
            answer.Origin == VisionAgentPlanAnswerOrigins.RuleInferred);
        answers.Should().Contain(answer =>
            answer.Field == VisionAgentPlanAnswerFields.MeasurementTarget &&
            answer.Value == "defect_area");
        answers.Should().Contain(answer =>
            answer.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            answer.Value.Contains("defect_area", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CrackAndPlcOutputRequirement_ShouldKeepDefectAsPrimaryTask()
    {
        const string prompt = "检测产品上的裂纹并输出 PLC OK/NG";
        var request = new VisionAgentRequirementMaturityRequest { Description = prompt };

        var maturity = VisionAgentRequirementMaturityGate.Evaluate(request);
        var answers = VisionAgentRequirementMaturityGate.ExtractExplicitPlanAnswers(request, null);

        maturity.TaskType.Should().Be(AiVisionTaskTypes.SurfaceDefect);
        answers.Should().Contain(answer =>
            answer.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            answer.Value.Contains("PLC", StringComparison.OrdinalIgnoreCase));
        answers.Should().NotContain(answer =>
            answer.Field == VisionAgentPlanAnswerFields.TaskType &&
            answer.Value == AiVisionTaskTypes.PlcOutput);
    }

    [Fact]
    public void ConflictingRuleTaskWithoutExplicitChoice_ShouldFailClosedBeforeOverlay()
    {
        var plan = ConflictPlan();
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [new VisionAgentPlanAnswer
            {
                Field = VisionAgentPlanAnswerFields.TaskType,
                Value = AiVisionTaskTypes.GeometryMeasurement,
                Origin = VisionAgentPlanAnswerOrigins.RuleInferred
            }],
            null,
            acceptedRecommendedDefaults: false);

        validation.ConflictedFields.Should().Contain(VisionAgentPlanAnswerFields.TaskType);
        validation.RequirementAnswers.Should().NotContainKey(VisionAgentPlanAnswerFields.TaskType);
        validation.Warnings.Should().Contain("task_type_conflict");
    }

    [Fact]
    public void ExplicitTaskSelection_ShouldOverrideButRetainConflictAuditTrail()
    {
        var plan = ConflictPlan();
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.TaskType,
                    Value = AiVisionTaskTypes.GeometryMeasurement,
                    Origin = VisionAgentPlanAnswerOrigins.RuleInferred
                },
                new VisionAgentPlanAnswer
                {
                    QuestionId = "q_task_type",
                    Field = VisionAgentPlanAnswerFields.TaskType,
                    Value = AiVisionTaskTypes.SurfaceDefect,
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            acceptedRecommendedDefaults: false);

        validation.ConflictedFields.Should().NotContain(VisionAgentPlanAnswerFields.TaskType);
        validation.RequirementAnswers[VisionAgentPlanAnswerFields.TaskType]
            .Should().Be(AiVisionTaskTypes.SurfaceDefect);
        validation.Warnings.Should().Contain("task_type_conflict_overridden_by_explicit_user");
    }

    [Theory]
    [InlineData("general_inspection")]
    [InlineData("custom_task")]
    [InlineData("made_up_task")]
    public void UnknownTaskSelection_ShouldNotResolveTaskType(string value)
    {
        var plan = ConflictPlan() with
        {
            SemanticExtraction = null,
            RequirementMaturity = new AiRequirementMaturityResult
            {
                TaskType = AiVisionTaskTypes.Unknown,
                MissingFields = [VisionAgentPlanAnswerFields.TaskType],
                BlockingReasons = ["task_type_missing"]
            }
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [new VisionAgentPlanAnswer
            {
                Field = VisionAgentPlanAnswerFields.TaskType,
                Value = value,
                Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
            }],
            null,
            acceptedRecommendedDefaults: false);

        validation.RequirementAnswers.Should().NotContainKey(VisionAgentPlanAnswerFields.TaskType);
        validation.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.TaskType);
        validation.InvalidValues.Should().Contain(item => item.Contains("unsupported_task_type", StringComparison.Ordinal));
    }

    private static VisionAgentPlanModeResult ConflictPlan()
    {
        return new VisionAgentPlanModeResult
        {
            OriginalUserPrompt = "检测金属表面划痕并计算面积",
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                TaskType = AiVisionTaskTypes.SurfaceDefect,
                Source = VisionAgentSemanticSources.Model
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                TaskType = AiVisionTaskTypes.SurfaceDefect,
                CanPlan = true
            },
            ClarificationQuestions =
            [
                new VisionAgentClarificationQuestion
                {
                    Id = "q_task_type",
                    Field = VisionAgentPlanAnswerFields.TaskType,
                    Title = "task type",
                    Options =
                    [
                        new VisionAgentClarificationOption
                        {
                            Value = AiVisionTaskTypes.SurfaceDefect,
                            Label = "surface defect",
                            AnswerEffect = VisionAgentClarificationAnswerEffects.ResolveField
                        },
                        new VisionAgentClarificationOption
                        {
                            Value = AiVisionTaskTypes.GeometryMeasurement,
                            Label = "measurement",
                            AnswerEffect = VisionAgentClarificationAnswerEffects.ResolveField
                        }
                    ]
                }
            ]
        };
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "ClearVision.Product")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the ClearVision repository root.");
    }

    private static string ReadFrontendTaskContract()
    {
        return File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Desktop",
            "wwwroot",
            "src",
            "features",
            "ai",
            "aiTaskContract.js"));
    }
}
