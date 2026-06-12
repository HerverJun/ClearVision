using System.Net;
using System.Text.Json;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentPlanPlanner;

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
            question.Options.Should().Contain(option => option.Recommended);
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

    [Fact(DisplayName = "Plan planner repair failure should return contract repair diagnostic")]
    public async Task CreatePlanAsync_ShouldFallbackWhenPlannerContractRepairFails()
    {
        var service = CreateService(_ => PlannerPlanJsonWithNullQuestionOptions());
        var baseline = Baseline("scratch", "surface_defect");

        var result = await service.CreatePlanAsync(
            new VisionAgentPlanModeRequest { Description = "scratch" },
            baseline,
            CancellationToken.None);

        result.PlanSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("planner_failed");
        AssertPlannerFailure(result, "contract_repair", "planner_contract_repair_failed");
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
        result.ContractRepairNotes.Should().Contain("template_selection_repaired_to_user_selection");
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
            canBuild = true,
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
            canBuild = true,
            blockingReasons = Array.Empty<string>(),
            nextAction = "Accept recommended defaults and Build.",
            stationBoundarySummary = "metadata-only Station boundary.",
            plcOutputPolicy = "Local ResultOutput first; PLC writes disabled until review.",
            metadataOnly = true
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
