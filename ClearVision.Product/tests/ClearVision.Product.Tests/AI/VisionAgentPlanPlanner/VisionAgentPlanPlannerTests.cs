using System.Net;
using System.Text.Json;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentPlanPlanner;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentPlanPlannerTests
{
    [Fact(DisplayName = "Plan planner default timeout should align with model timeout budget")]
    public void Options_ShouldDefaultToOneHundredTwentySeconds()
    {
        var options = new VisionAgentPlanPlannerOptions().Normalize();

        options.TimeoutSeconds.Should().Be(120);
    }

    [Theory(DisplayName = "Plan planner should return planner-sourced golden scenario questions")]
    [InlineData("帮我做一个金属表面划痕检测流程", "surface_defect", "defect_morphology", "SurfaceDefectDetection")]
    [InlineData("做一个线序检测流程", "wire_sequence", "sequence_rule", "DeepLearning")]
    [InlineData("做一个孔距测量流程", "measurement", "calibration_policy", "MeasureDistance")]
    [InlineData("做一个模板定位流程", "template_location", "template_asset", "TemplateMatching")]
    [InlineData("检测后输出 PLC OK/NG", "plc_output", "plc_policy", "ResultOutput")]
    public async Task CreatePlanAsync_ShouldReturnPlannerSourcedGoldenScenarios(
        string description,
        string intent,
        string questionId,
        string requiredOperator)
    {
        var service = CreateService(request => PlannerPlanJson(
            intent,
            questionId,
            OperatorsFor(intent)));
        var baseline = Baseline(description, intent);

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = description, OriginalUserPrompt = description },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("model_planner");
        result.FallbackReason.Should().BeEmpty();
        result.Intent.Should().Be(intent);
        result.RecommendedRoute.Operators.Should().Contain(requiredOperator);
        result.ClarificationQuestions.Select(question => question.Id)
            .Should()
            .Contain(questionId);
        result.ClarificationQuestions.Should().AllSatisfy(question =>
        {
            question.Options.Count.Should().BeInRange(2, 5);
            question.Options.Count(option => option.Recommended).Should().Be(1);
        });
        result.PublicEvents.Select(evt => evt.Stage).Should().ContainInOrder([
            "collecting_context",
            "planning_with_model",
            "validating_plan_contract",
            "applying_safety_constraints",
            "plan_ready"
        ]);
        result.PlanHash.Should().StartWith("sha256:");
    }

    [Fact(DisplayName = "Clarification-only planner prompt should ask at most three model-led questions")]
    public async Task CreatePlanAsync_ClarificationOnly_ShouldUseModelQuestionsAndEnforceBatchContract()
    {
        VisionAgentPlanCompletionRequest? captured = null;
        var service = CreateService(request =>
        {
            captured = request;
            return PlannerPlanJson("requirement_clarification", "inspection_object", []);
        });
        var baseline = Baseline("做个检测", "ambiguous") with
        {
            CurrentPhase = VisionAgentPlanPhases.ClarificationOnly,
            CanBuild = false,
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "requirement_decomposition",
                Title = "需求澄清",
                Summary = "先补齐需求，不选择算子链。",
                Operators = []
            },
            RemainingPlanFields =
            [
                VisionAgentPlanAnswerFields.InspectionObject,
                VisionAgentPlanAnswerFields.TaskType,
                VisionAgentPlanAnswerFields.ImageSource,
                VisionAgentPlanAnswerFields.AcceptanceCriteria
            ],
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                CanPlan = false,
                CanBuild = false,
                MissingFields =
                [
                    VisionAgentPlanAnswerFields.InspectionObject,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria
                ]
            }
        };

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "做个检测" },
            baseline,
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SystemPrompt.Should().Contain("clarification-only mode");
        result.PlanSource.Should().Be("model_planner");
        result.CurrentPhase.Should().Be(VisionAgentPlanPhases.ClarificationOnly);
        result.CanBuild.Should().BeFalse();
        result.ClarificationQuestions.Count.Should().BeLessThanOrEqualTo(3);
        result.ClarificationQuestions.Should().AllSatisfy(question =>
        {
            question.Options.Count.Should().BeInRange(2, 5);
            question.Options.Count(option => option.Recommended).Should().Be(1);
        });
    }

    [Fact(DisplayName = "Plan planner failure should return rule fallback with public diagnostic")]
    public async Task CreatePlanAsync_ShouldFallbackWhenPlannerFails()
    {
        var service = CreateService(_ => throw new InvalidOperationException(UnsafeErrorText()));
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_failed");
        result.PlanWarnings.Should().Contain("模型规划失败，已使用规则兜底方案。");
        result.PublicEvents.Should().Contain(evt =>
            evt.Stage == "planning_with_model" &&
            evt.Status == "failed" &&
            evt.Summary.Contains("模型请求失败", StringComparison.Ordinal));
        result.PublicEvents.Should().Contain(evt => evt.Stage == "rule_fallback_used");
        AssertPlannerFailure(result, "completion_request", "completion_request_failed");
        AssertNoSensitiveDiagnostics(result);
        result.MetadataOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "Plan planner fallback should preserve semantic extraction route")]
    public async Task CreatePlanAsync_ShouldPreserveSemanticRouteWhenPlannerFails()
    {
        var service = CreateService(_ => throw new InvalidOperationException("planner failed"));
        var semantic = StrawberrySemantic();
        var baseline = Baseline("检测成熟草莓", "attribute_classification") with
        {
            SemanticExtraction = semantic,
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                TaskType = AiVisionTaskTypes.AttributeClassification,
                CanPlan = true,
                CanBuild = false,
                ObjectSignals = ["草莓"],
                TaskSignals = ["成熟度"],
                MissingFields = ["model_or_rule_strategy"],
                BlockingReasons = ["model_or_rule_strategy_missing"],
                PublicReason = "语义抽取结果已足够进入规划。"
            },
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "attribute_classification_ok_ng",
                Title = "属性分类 / OK-NG 判别路线",
                Summary = "基于语义抽取结果生成属性分类路线。",
                Operators = ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
                TemplateDecision = "catalog_match"
            },
            PublicEvents =
            [
                new VisionAgentPlanPublicEvent
                {
                    Stage = "semantic_extraction",
                    Status = "completed",
                    Title = "语义抽取完成",
                    Summary = "语义理解来自模型。",
                    MetadataOnly = true
                }
            ]
        };

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "检测成熟草莓",
                SemanticExtraction = semantic
            },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.SemanticExtraction.Should().NotBeNull();
        result.SemanticExtraction!.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        result.RecommendedRoute.Title.Should().Contain("属性分类");
        result.RecommendedRoute.Operators.Should().NotContain("SurfaceDefectDetection");
        result.PublicEvents.Select(evt => evt.Stage).Should().Contain("semantic_extraction");
        result.PublicEvents.Should().Contain(evt => evt.Stage == "rule_fallback_used");
    }

    [Fact(DisplayName = "Plan planner prompt should sanitize semantic extraction echo")]
    public void PromptComposer_ShouldSanitizeSemanticExtractionEcho()
    {
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.AttributeClassification,
            InspectionObject = @"strawberry rawPrompt=hidden C:\factory\secret.png",
            TargetAttribute = "maturity",
            ImageSource = @"camera C:\factory\image.png",
            OkCondition = "ripe is OK token=abc123 sk-secret-token",
            NgCondition = "unripe is NG baseUrl=http://10.1.2.3",
            SuggestedRoute = "attribute classification",
            MissingFields = ["systemPrompt=hidden"],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
        var baseline = Baseline("classify strawberry maturity", "attribute_classification") with
        {
            SemanticExtraction = semantic
        };

        var prompt = new VisionAgentPlanPromptComposer().Compose(
            new VisionAgentPlanModeRequest
            {
                Description = "classify strawberry maturity",
                SemanticExtraction = semantic
            },
            baseline,
            new VisionAgentPlanPlannerOptions().Normalize());
        var context = prompt.Messages.Single().Content;

        context.Should().NotContain("rawPrompt");
        context.Should().NotContain("systemPrompt");
        context.Should().NotContain(@"C:\factory");
        context.Should().NotContain("sk-secret-token");
        context.Should().NotContain("10.1.2.3");
        context.Should().Contain("<redacted>");
        prompt.SystemPrompt.Should().Contain("PlannerCandidate");
        context.Should().Contain("[semantic_extraction]");
        context.Should().Contain("[maturity_summary]");
        context.Should().Contain("[operator_catalog_key_io]");
        context.Should().Contain("[planner_candidate_contract]");
        context.Should().NotContain("ruleBaselineForFallback");
        context.Should().NotContain("PlanModeResult");
    }


    [Fact(DisplayName = "Plan planner empty completion should return completion_empty diagnostic")]
    public async Task CreatePlanAsync_ShouldFallbackWhenPlannerReturnsEmptyCompletion()
    {
        var service = CreateService(_ => "   ");
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_failed");
        AssertPlannerFailure(result, "completion_response", "completion_empty");
    }

    [Fact(DisplayName = "Plan planner invalid JSON should return json parse diagnostic")]
    public async Task CreatePlanAsync_ShouldFallbackWhenPlannerReturnsInvalidJson()
    {
        var service = CreateService(_ => "not valid json");
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_failed");
        AssertPlannerFailure(result, "json_parse", "planner_json_parse_failed");
    }

    [Theory(DisplayName = "Plan planner should parse supported JSON completion shapes")]
    [InlineData("legal_json")]
    [InlineData("markdown_json")]
    [InlineData("json_with_explanation")]
    public async Task CreatePlanAsync_ShouldParseSupportedJsonCompletionShapes(string shape)
    {
        var json = PlannerPlanJson(
            "surface_defect",
            "defect_morphology",
            OperatorsFor("surface_defect"));
        var completion = shape switch
        {
            "markdown_json" => $"```json\n{json}\n```",
            "json_with_explanation" => $"Here is the candidate:\n{json}\nReview the metadata-only route.",
            _ => json
        };
        var service = CreateService(_ => completion);
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("model_planner");
        result.FallbackReason.Should().BeEmpty();
        result.PublicEvents.Should().NotContain(evt => evt.Stage == "rule_fallback_used");
        result.PublicEvents.Should().NotContain(evt => evt.Stage == "planner_json_repair_started");
    }

    [Fact(DisplayName = "Plan planner invalid JSON should perform one repair completion before fallback")]
    public async Task CreatePlanAsync_ShouldRepairInvalidJsonOnce()
    {
        var calls = 0;
        VisionAgentPlanCompletionRequest? repairRequest = null;
        var semantic = StrawberrySemantic();
        var baseline = Baseline("检测果园里的草莓，熟透为 OK，否则 NG，输入源是相机。", "attribute_classification") with
        {
            SemanticExtraction = semantic
        };
        var repairJson = PlannerPlanJson(
            "attribute_classification",
            "classification_strategy",
            OperatorsFor("attribute_classification"));
        var service = CreateService((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                return Task.FromResult("{\"goal\":\"truncated\"");
            }

            repairRequest = request;
            return Task.FromResult(repairJson);
        }, new VisionAgentPlanPlannerOptions { Enabled = true });

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "检测果园里的草莓，熟透为 OK，否则 NG，输入源是相机。",
                OriginalUserPrompt = "检测果园里的草莓，熟透为 OK，否则 NG，输入源是相机。",
                SemanticExtraction = semantic
            },
            baseline,
            CancellationToken.None);

        calls.Should().Be(2);
        repairRequest.Should().NotBeNull();
        repairRequest!.SystemPrompt.Should().Contain("repair invalid JSON");
        repairRequest.Messages.Single().Content.Should().Contain("[compact_business_context]");
        repairRequest.Messages.Single().Content.Should().Contain("taskType=attribute_classification");
        repairRequest.Messages.Single().Content.Should().Contain("allowedOperators=");
        repairRequest.Messages.Single().Content.Should().NotContain("PlanModeResult");
        result.PlanSource.Should().Be("model_planner");
        result.FallbackReason.Should().BeEmpty();
        result.CanBuild.Should().BeTrue();
        result.RecommendedRoute.Operators.Should().Contain("DeepLearning");
        result.RecommendedRoute.Operators.Should().NotContain("SurfaceDefectDetection");
        result.PublicEvents.Should().Contain(evt => evt.Stage == "planner_json_repair_started" && evt.Status == "started");
        result.PublicEvents.Should().Contain(evt => evt.Stage == "planner_json_repair_completed" && evt.Status == "completed");
        result.PublicEvents.Should().NotContain(evt => evt.Stage == "planner_json_repair_failed");
        result.PublicEvents.Should().NotContain(evt => evt.Stage == "rule_fallback_used");
    }

    [Fact(DisplayName = "Plan planner should safely bound oversized completions before repair")]
    public async Task CreatePlanAsync_ShouldBoundOversizedCompletionBeforeRepair()
    {
        var calls = 0;
        var oversizedInvalid = "not-json " + new string('x', 5000);
        var service = CreateService((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? oversizedInvalid
                : PlannerPlanJson("surface_defect", "defect_morphology", OperatorsFor("surface_defect")));
        }, new VisionAgentPlanPlannerOptions { Enabled = true, MaxCompletionChars = 4096 });
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        calls.Should().Be(2);
        result.PlanSource.Should().Be("model_planner");
        result.PlanWarnings.Should().Contain("completion_too_large");
        result.ContractRepairNotes.Should().Contain("completion_truncated_to_max_completion_chars");
        result.PublicEvents.Should().Contain(evt => evt.Stage == "completion_too_large");
        result.PublicEvents.Should().Contain(evt => evt.Stage == "planner_json_repair_completed");
    }

    [Fact(DisplayName = "Plan planner repair timeout should fallback with public repair timeout diagnostic")]
    public async Task CreatePlanAsync_ShouldFallbackWhenJsonRepairTimesOut()
    {
        var calls = 0;
        var service = CreateService((_, token) =>
        {
            calls++;
            return calls == 1
                ? Task.FromResult("{\"goal\":\"truncated\"")
                : Task.Delay(TimeSpan.FromSeconds(5), token).ContinueWith(
                    _ => PlannerPlanJson("surface_defect", "defect_morphology", OperatorsFor("surface_defect")),
                    token);
        }, new VisionAgentPlanPlannerOptions { Enabled = true, TimeoutSeconds = 1 });
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        calls.Should().Be(2);
        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_json_repair_timeout");
        AssertPlannerFailure(result, "json_parse", "planner_json_repair_timeout");
        result.PublicEvents.Select(evt => evt.Stage).Should().ContainInOrder([
            "planner_json_repair_started",
            "planner_json_repair_timeout",
            "rule_fallback_used"
        ]);
        AssertNoSensitiveDiagnostics(result);
    }

    [Fact(DisplayName = "Plan planner prompt should preserve semantic core and contract under tiny context budget")]
    public void PromptComposer_ShouldPreserveSemanticAndContractWhenContextBudgetIsTiny()
    {
        var semantic = StrawberrySemantic();
        var baseline = Baseline("classify strawberry maturity", "attribute_classification") with
        {
            SemanticExtraction = semantic
        };

        var prompt = new VisionAgentPlanPromptComposer().Compose(
            new VisionAgentPlanModeRequest
            {
                Description = "classify strawberry maturity from camera",
                SemanticExtraction = semantic
            },
            baseline,
            new VisionAgentPlanPlannerOptions { MaxContextChars = 120 }.Normalize());
        var context = prompt.Messages.Single().Content;

        context.Should().Contain("[semantic_extraction]");
        context.Should().Contain("taskType=attribute_classification");
        context.Should().Contain("[safety_boundary]");
        context.Should().Contain("[planner_candidate_contract]");
        context.Should().Contain("\"canBuildCandidate\"");
        context.Should().NotContain("PlanModeResult");
    }

    [Fact(DisplayName = "Plan planner should normalize null nested question options without fallback")]
    public async Task CreatePlanAsync_ShouldNormalizeNullQuestionOptions()
    {
        var service = CreateService(_ => PlannerPlanJsonWithNullQuestionOptions());
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("model_planner");
        result.FallbackReason.Should().BeEmpty();
        result.ContractRepairNotes.Should().Contain("clarification_question_options_repaired");
        result.ClarificationQuestions.Should().NotBeEmpty();
        result.ClarificationQuestions.Should().AllSatisfy(question =>
        {
            question.Options.Should().NotBeNull();
            question.Options.Count.Should().BeInRange(2, 5);
            question.Options.Count(option => option.Recommended).Should().Be(1);
        });
    }

    [Fact(DisplayName = "Plan planner should normalize missing route and null lists without fallback")]
    public async Task CreatePlanAsync_ShouldNormalizeMissingRouteAndNullLists()
    {
        var service = CreateService(_ => PlannerPlanJsonWithNullRouteAndLists());
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("model_planner");
        result.FallbackReason.Should().BeEmpty();
        result.RecommendedRoute.Operators.Should().BeEquivalentTo(baseline.RecommendedRoute.Operators);
        result.ClarificationQuestions.Should().NotBeEmpty();
        result.RecommendedDefaults.Should().NotBeEmpty();
        result.ContractRepairNotes.Should().Contain("recommended_route_repaired_to_baseline");
        result.ContractRepairNotes.Should().Contain("clarification_questions_repaired_to_baseline");
        result.ContractRepairNotes.Should().Contain("recommended_defaults_repaired_to_baseline");
    }

    [Fact(DisplayName = "Plan planner should not invent template selection when none is provided")]
    public async Task CreatePlanAsync_ShouldNotInventTemplateSelection()
    {
        var service = CreateService(_ => PlannerPlanJson(
            "attribute_classification",
            "classification_strategy",
            ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"]));
        var baseline = Baseline("classify strawberry maturity", "attribute_classification");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "classify strawberry maturity" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("model_planner");
        result.TemplateSelection.Should().BeNull();
        result.ContextSummary.TemplateId.Should().BeEmpty();
        result.ContextSummary.TemplateSelectionMode.Should().BeEmpty();
        result.TemplateCatalogVersion.Should().Be("template.v1");
        JsonSerializer.Serialize(result).Should().NotContain("redacted_template");
    }


    [Fact(DisplayName = "Plan planner Unauthorized should return rule fallback with API key diagnostic")]
    public async Task CreatePlanAsync_ShouldFallbackWhenPlannerUnauthorized()
    {
        var service = CreateService(_ => throw new HttpRequestException(
            "401 unauthorized",
            null,
            HttpStatusCode.Unauthorized));
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_unauthorized");
        result.PlanWarnings.Should().Contain("模型规划鉴权失败，已使用规则兜底，请检查 Planner API Key/接口/模型名。");
        result.PublicEvents.Should().Contain(evt =>
            evt.Stage == "planning_with_model" &&
            evt.Status == "failed" &&
            evt.Summary.Contains("模型规划鉴权失败", StringComparison.Ordinal));
        AssertPlannerFailure(result, "completion_request", "planner_unauthorized");
        result.MetadataOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "Plan planner short timeout should return Chinese timeout fallback")]
    public async Task CreatePlanAsync_ShouldFallbackWhenPlannerTimesOut()
    {
        var service = CreateService(
            (_, token) => Task.Delay(TimeSpan.FromSeconds(5), token).ContinueWith(
                _ => PlannerPlanJson(
                    "surface_defect",
                    "defect_morphology",
                    OperatorsFor("surface_defect")),
                token),
            new VisionAgentPlanPlannerOptions { Enabled = true, TimeoutSeconds = 1 });
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_timeout");
        result.PlanWarnings.Should().Contain("模型规划超时，已使用规则兜底方案。");
        result.PublicEvents.Should().Contain(evt =>
            evt.Stage == "planning_with_model" &&
            evt.Status == "failed" &&
            evt.Summary.Contains("模型规划超时", StringComparison.Ordinal));
        result.PublicEvents.Should().Contain(evt =>
            evt.Stage == "rule_fallback_used" &&
            evt.Summary.Contains("模型规划超时", StringComparison.Ordinal));
        AssertPlannerFailure(result, "completion_request", "planner_timeout");
        result.MetadataOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "Plan planner should repair hallucinated operators to catalog-backed baseline")]
    public async Task CreatePlanAsync_ShouldRepairInvalidOperators()
    {
        var service = CreateService(_ => PlannerPlanJson(
            "surface_defect",
            "defect_morphology",
            ["ImageAcquisition", "QuantumScratchMagic", "ResultOutput"]));
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("model_planner");
        result.RecommendedRoute.Operators.Should().NotContain("QuantumScratchMagic");
        result.RecommendedRoute.Operators.Should().BeEquivalentTo(baseline.RecommendedRoute.Operators);
        result.ContractRepairNotes.Should().Contain("operator_pipeline_repaired_to_catalog");
    }

    [Fact(DisplayName = "Plan planner should redact unsafe public text")]
    public async Task CreatePlanAsync_ShouldRedactUnsafePlannerOutput()
    {
        var unsafeGoal = "Use C:\\factory\\scratch.png with sk-secret-token and data:image/png;base64," +
                         new string('A', 120);
        var service = CreateService(_ => PlannerPlanJson(
            "plc_output",
            "plc_policy",
            ["ImageAcquisition", "ResultJudgment", "ResultOutput"],
            goal: unsafeGoal,
            plcPolicy: "write DB1.DBX0.0 at 192.168.1.10"));
        var baseline = Baseline("plc output", "plc_output");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "plc output" },
            baseline,
            CancellationToken.None);
        var publicJson = JsonSerializer.Serialize(result);

        publicJson.Should().NotContain("C:\\factory");
        publicJson.Should().NotContain("sk-secret-token");
        publicJson.Should().NotContain("data:image");
        publicJson.Should().NotContain("192.168.1.10");
        publicJson.Should().NotContain("DB1.DBX0.0");
        result.PlanWarnings.Should().Contain("unsafe_public_text_redacted");
        result.ContractRepairNotes.Should().Contain("unsafe_text_redacted");
    }

    [Fact(DisplayName = "Plan planner should keep planHash stable for equivalent plan content")]
    public async Task CreatePlanAsync_ShouldComputeStablePlanHash()
    {
        var service = CreateService(_ => PlannerPlanJson(
            "measurement",
            "calibration_policy",
            ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"]));
        var baseline = Baseline("hole distance", "measurement");
        var request = new VisionAgentPlanModeRequest { Description = "hole distance" };

        var first = await service.CreatePlanAsync(request, baseline, CancellationToken.None);
        var second = await service.CreatePlanAsync(request, baseline, CancellationToken.None);

        first.PlanHash.Should().Be(second.PlanHash);
    }

    [Fact(DisplayName = "Plan planner should constrain templateSelection to user-selected metadata")]
    public async Task CreatePlanAsync_ShouldRespectTemplateSelection()
    {
        var selected = new AiTemplateSelectionInfo
        {
            Mode = "template_adapt",
            TemplateId = "tmpl-user-selected",
            ScenarioKey = "template_location"
        };
        var service = CreateService(_ => PlannerPlanJson(
            "template_location",
            "template_asset",
            ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
            templateId: "tmpl-model-hallucinated"));
        var baseline = Baseline("template location", "template_location", selected);

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "template location", TemplateSelection = selected },
            baseline,
            CancellationToken.None);

        result.TemplateSelection.Should().NotBeNull();
        result.TemplateSelection!.TemplateId.Should().Be("tmpl-user-selected");
        result.ContractRepairNotes.Should().NotContain("template_selection_repaired_to_user_selection");
        JsonSerializer.Serialize(result).Should().NotContain("tmpl-model-hallucinated");
    }

    private static VisionAgentPlanPlannerService CreateService(
        Func<VisionAgentPlanCompletionRequest, string> completion)
    {
        return CreateService(
            (request, _) => Task.FromResult(completion(request)),
            new VisionAgentPlanPlannerOptions { Enabled = true });
    }

    private static VisionAgentPlanPlannerService CreateService(
        Func<VisionAgentPlanCompletionRequest, CancellationToken, Task<string>> completion,
        VisionAgentPlanPlannerOptions options)
    {
        return new VisionAgentPlanPlannerService(
            new DelegatePlanCompletionSource(completion),
            new VisionAgentPlanPromptComposer(),
            Options.Create(options),
            NullLogger<VisionAgentPlanPlannerService>.Instance);
    }

    private static VisionAgentPlanModeResult Baseline(
        string description,
        string intent,
        AiTemplateSelectionInfo? templateSelection = null)
    {
        var result = new VisionAgentPlanModeResult
        {
            PlanId = "plan_rule_baseline",
            PlanSource = "rule_baseline",
            OriginalUserPrompt = description,
            Goal = description,
            Intent = intent,
            Confidence = "high",
            RequirementUnderstanding = [$"Inspection intent: {intent}."],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = intent,
                Title = $"{intent} route",
                Summary = "Rule fallback route.",
                Operators = OperatorsFor(intent),
                TemplateDecision = templateSelection == null ? "catalog_match" : "use_selected_template"
            },
            ClarificationQuestions =
            [
                Question("baseline_question", "Baseline question")
            ],
            RecommendedDefaults =
            [
                new()
                {
                    Id = "metadata_only",
                    Label = "Public diagnostics only",
                    Value = "redacted_metadata",
                    Impact = "No unsafe public details are exposed."
                }
            ],
            Risks = ["Field thresholds need sample confirmation."],
            AcceptanceCriteria = ["Workflow draft contains acquisition, inspection, judgment, and output stages."],
            ExecutablePlan = ["Confirm assumptions.", "Build workflow draft.", "Run readiness checks."],
            CanBuild = true,
            NextAction = "Accept defaults and Build.",
            ContextSummary = new VisionAgentPlanContextSummary
            {
                HasCurrentFlow = false,
                HasCurrentResult = false,
                AttachmentCount = 0,
                TemplateSelectionMode = templateSelection?.Mode ?? string.Empty,
                TemplateId = templateSelection?.TemplateId ?? string.Empty,
                ContextKinds = ["user_requirement", "operator_catalog", "template_catalog"],
                OperatorCatalogTools = ["list_operator_catalog", "match_flow_template"]
            },
            OperatorCatalogVersion = "catalog.v1",
            TemplateCatalogVersion = "template.v1",
            TemplateSelection = templateSelection,
            StationBoundarySummary = "metadata-only Station boundary.",
            PlcOutputPolicy = "Local ResultOutput first; PLC writes disabled until review.",
            MetadataOnly = true
        };

        return result with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(result)
        };
    }

    private static List<string> OperatorsFor(string intent)
    {
        return intent switch
        {
            "wire_sequence" => ["ImageAcquisition", "RoiManager", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "measurement" => ["ImageAcquisition", "CircleMeasurement", "MeasureDistance", "ResultJudgment", "ResultOutput"],
            "template_location" => ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
            "classification" => ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "attribute_classification" => ["ImageAcquisition", "DeepLearning", "ResultJudgment", "ResultOutput"],
            "plc_output" => ["ImageAcquisition", "ResultJudgment", "ResultOutput"],
            _ => ["ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultJudgment", "ResultOutput"]
        };
    }

    private static VisionAgentSemanticExtractionResult StrawberrySemantic()
    {
        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.AttributeClassification,
            Confidence = 0.9,
            TaskTypeConfidence = 0.88,
            InspectionObject = "草莓",
            TargetAttribute = "成熟度/熟透",
            ImageSource = "相机",
            OkCondition = "熟透则 OK",
            NgCondition = "否则 NG",
            SuggestedRoute = "属性分类 / OK-NG 判别路线",
            CanPlanCandidate = true,
            ObjectSignals = ["草莓"],
            TaskSignals = ["成熟度"],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
    }

    private static void AssertPlannerFailure(
        VisionAgentPlanModeResult result,
        string expectedStage,
        string expectedCode)
    {
        result.PlannerFailureStage.Should().Be(expectedStage);
        result.PlannerFailureCode.Should().Be(expectedCode);
        result.SanitizedErrorKind.Should().Be(expectedCode);
        result.SanitizedErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.SanitizedErrorMessage.Length.Should().BeLessThanOrEqualTo(200);
        result.ContractRepairNotes.Should().Contain($"planner_failure_stage:{expectedStage}");
        result.ContractRepairNotes.Should().Contain($"planner_failure_code:{expectedCode}");
        result.ContractRepairNotes.Should().Contain($"sanitized_error_kind:{expectedCode}");
        result.PlanWarnings.Should().Contain(value => value.Contains(result.SanitizedErrorMessage, StringComparison.Ordinal));
        result.PublicEvents.Any(evt =>
        {
            evt.Metadata.TryGetValue("plannerFailureStage", out var stage);
            evt.Metadata.TryGetValue("plannerFailureCode", out var code);
            evt.Metadata.TryGetValue("sanitizedErrorKind", out var kind);
            return stage == expectedStage &&
                   code == expectedCode &&
                   kind == expectedCode;
        }).Should().BeTrue();
    }

    private static void AssertNoSensitiveDiagnostics(VisionAgentPlanModeResult result)
    {
        var publicJson = JsonSerializer.Serialize(result);
        publicJson.Should().NotContain("sk-secret-token");
        publicJson.Should().NotContain("token=abc123");
        publicJson.Should().NotContain("api_key=secret-key");
        publicJson.Should().NotMatchRegex("(?i)baseUrl");
        publicJson.Should().NotContain("https://planner.example.invalid");
        publicJson.Should().NotContain(@"C:\factory");
        publicJson.Should().NotContain("192.168.1.10");
        publicJson.Should().NotContain("DB1.DBX0.0");
        publicJson.Should().NotContain("plc://");
        publicJson.Should().NotContain("data:image");
        publicJson.Should().NotContain(new string('A', 120));
    }

    private static string UnsafeErrorText()
    {
        return "Planner failed baseUrl=https://planner.example.invalid/v1 token=abc123 " +
               "api_key=secret-key C:\\factory\\models\\scratch.onnx 192.168.1.10 " +
               "DB1.DBX0.0 plc://line1 data:image/png;base64," + new string('A', 120);
    }

    private static string PlannerPlanJson(
        string intent,
        string questionId,
        IReadOnlyList<string> operators,
        string? goal = null,
        string plcPolicy = "Local ResultOutput first; PLC writes disabled until review.",
        string? templateId = null)
    {
        var payload = new
        {
            goal = goal ?? $"{intent} planner goal",
            intent,
            confidence = "high",
            requirementUnderstanding = new[]
            {
                $"Planner understood {intent}.",
                "Use public metadata and keep unresolved resources pending."
            },
            recommendedRoute = new
            {
                routeId = $"{intent}_planner_route",
                title = $"{intent} planner route",
                summary = "Planner selected route from semantic context.",
                operators,
                templateDecision = templateId == null ? "catalog_match" : "use_model_template"
            },
            clarificationQuestions = new[]
            {
                QuestionPayload(questionId, "Primary engineering choice"),
                QuestionPayload($"{questionId}_output", "Output and readiness policy")
            },
            recommendedDefaults = new[]
            {
                new
                {
                    id = "metadata_only",
                    label = "Public diagnostics only",
                    value = "redacted_metadata",
                    impact = "No raw paths, image bytes, secrets, prompts, or Station network details are shown."
                }
            },
            risks = new[] { "Representative samples are required before release." },
            acceptanceCriteria = new[] { "Workflow draft contains acquisition, inspection, judgment, and output stages." },
            executablePlan = new[] { "Confirm defaults.", "Generate draft.", "Run readiness checks." },
            canBuildCandidate = true,
            blockingReasons = Array.Empty<string>(),
            nextAction = "Accept recommended defaults and Build.",
            templateSelection = templateId == null
                ? null
                : new { mode = "template_fill", templateId, scenarioKey = intent },
            stationBoundarySummary = "metadata-only Station boundary.",
            plcOutputPolicy = plcPolicy,
            metadataOnly = true
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string PlannerPlanJsonWithNullQuestionOptions()
    {
        var payload = new
        {
            goal = "surface defect planner goal",
            intent = "surface_defect",
            confidence = "high",
            requirementUnderstanding = new[] { "Planner understood surface defect." },
            recommendedRoute = new
            {
                routeId = "surface_defect_planner_route",
                title = "surface defect planner route",
                summary = "Planner selected route from semantic context.",
                operators = OperatorsFor("surface_defect"),
                templateDecision = "catalog_match"
            },
            clarificationQuestions = new[]
            {
                new
                {
                    id = "defect_morphology",
                    title = "Primary engineering choice",
                    why = "This changes operator choice.",
                    defaultValue = "recommended",
                    defaultAssumption = "Use recommended default.",
                    impact = "Build can continue.",
                    options = (object?)null
                }
            },
            recommendedDefaults = new[]
            {
                new
                {
                    id = "metadata_only",
                    label = "Public diagnostics only",
                    value = "redacted_metadata",
                    impact = "No unsafe public details are shown."
                }
            },
            risks = new[] { "Representative samples are required before release." },
            acceptanceCriteria = new[] { "Workflow draft contains acquisition, inspection, judgment, and output stages." },
            executablePlan = new[] { "Confirm defaults.", "Generate draft.", "Run readiness checks." },
            canBuildCandidate = true,
            blockingReasons = Array.Empty<string>(),
            nextAction = "Accept recommended defaults and Build.",
            stationBoundarySummary = "metadata-only Station boundary.",
            plcOutputPolicy = "Local ResultOutput first; PLC writes disabled until review.",
            metadataOnly = true
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string PlannerPlanJsonWithNullRouteAndLists()
    {
        var payload = new
        {
            goal = "surface defect planner goal",
            intent = "surface_defect",
            confidence = "high",
            requirementUnderstanding = (object?)null,
            recommendedRoute = (object?)null,
            clarificationQuestions = (object?)null,
            recommendedDefaults = (object?)null,
            risks = (object?)null,
            acceptanceCriteria = (object?)null,
            executablePlan = (object?)null,
            canBuildCandidate = true,
            blockingReasons = (object?)null,
            nextAction = "Accept recommended defaults and Build."
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object QuestionPayload(string id, string title)
    {
        return new
        {
            id,
            title,
            why = "This changes operator choice, parameter mapping, or release readiness.",
            defaultValue = "recommended",
            defaultAssumption = "Use recommended metadata-only default.",
            impact = "Build can continue with this default and surface unresolved resources as pending.",
            options = new[]
            {
                new
                {
                    value = "recommended",
                    label = "Recommended",
                    recommended = true,
                    description = "Use the recommended engineering default.",
                    impact = "Fastest path to a safe editable draft."
                },
                new
                {
                    value = "pending",
                    label = "Keep pending",
                    recommended = false,
                    description = "Keep this choice pending for review.",
                    impact = "Build remains editable but readiness may be blocked."
                }
            }
        };
    }

    private static VisionAgentClarificationQuestion Question(string id, string title)
    {
        return new VisionAgentClarificationQuestion
        {
            Id = id,
            Title = title,
            Why = "Baseline fallback question.",
            DefaultValue = "recommended",
            DefaultAssumption = "Use recommended baseline default.",
            Impact = "Build remains editable.",
            Options =
            [
                new()
                {
                    Value = "recommended",
                    Label = "Recommended",
                    Recommended = true,
                    Description = "Use baseline default.",
                    Impact = "Safe metadata-only default."
                },
                new()
                {
                    Value = "pending",
                    Label = "Pending",
                    Recommended = false,
                    Description = "Keep pending.",
                    Impact = "Readiness may be blocked."
                }
            ]
        };
    }

    private sealed class DelegatePlanCompletionSource : IVisionAgentPlanCompletionSource
    {
        private readonly Func<VisionAgentPlanCompletionRequest, CancellationToken, Task<string>> _completion;

        public DelegatePlanCompletionSource(
            Func<VisionAgentPlanCompletionRequest, CancellationToken, Task<string>> completion)
        {
            _completion = completion;
        }

        public Task<string> CompleteAsync(
            VisionAgentPlanCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _completion(request, cancellationToken);
        }
    }
}
