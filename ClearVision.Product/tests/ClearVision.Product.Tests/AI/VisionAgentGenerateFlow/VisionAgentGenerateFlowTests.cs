using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentGenerateFlow;

public sealed class VisionAgentGenerateFlowTests
{
    [Fact(DisplayName = "Default GenerateFlow should not trigger VisionAgentLoop")]
    public async Task DefaultGenerateFlow_ShouldNotTriggerAgentService()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent should not run"));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("hello"));

        result.Success.Should().BeTrue();
        agent.CallCount.Should().Be(0);
        result.ToolTrace.Should().BeEmpty();
    }

    [Fact(DisplayName = "Explicit Agent GenerateFlow should trigger controlled agent branch")]
    public async Task ExplicitAgentGenerateFlow_ShouldTriggerAgentBranch()
    {
        var agent = new FakeAgentGenerateFlowService(_ => Task.FromResult(new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            Flow = new OperatorFlowDto(),
            ToolTrace = [new { toolName = "runtime_package_precheck", success = true }]
        }));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("wire sequence")
        {
            UseVisionAgentGenerateFlow = true
        });

        result.Success.Should().BeTrue();
        agent.CallCount.Should().Be(1);
        result.ToolTrace.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "Agent GenerateFlow assistant persistence failure should keep result and surface warning")]
    public async Task AgentGenerateFlow_AssistantPersistenceFailure_ShouldKeepResultAndSurfaceWarning()
    {
        var conversationService = Substitute.For<IConversationalFlowService>();
        ConfigureConversationService(conversationService);
        conversationService.RecordAssistantResponseWithPersistence(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ConversationTurnPayload?>())
            .Returns(new ConversationSessionWriteResult
            {
                Success = false,
                PersistenceStatus = new ConversationPersistenceStatus
                {
                    PrimaryStoreSaved = false,
                    RecoveryBackupSaved = true,
                    ErrorCode = "primary_store_save_failed",
                    PublicMessage = "结果已生成，但本次会话尚未成功保存。"
                }
            });
        var agent = new FakeAgentGenerateFlowService(_ => Task.FromResult(new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            Flow = new OperatorFlowDto(),
            AiExplanation = "Generated draft."
        }));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true },
            conversationService: conversationService);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("wire sequence")
        {
            UseVisionAgentGenerateFlow = true
        });

        result.Success.Should().BeTrue();
        result.Flow.Should().NotBeNull();
        result.PersistenceWarning.Should().NotBeNull();
        result.PersistenceWarning!.Code.Should().Be("primary_store_save_failed");
        result.PersistenceWarning.Message.Should().Contain("尚未成功保存");
    }

    [Fact(DisplayName = "BuildFromPlan GenerateFlow should use dedicated Build pipeline even when use flag is false")]
    public async Task BuildFromPlanGenerateFlow_ShouldUseDedicatedBuildPipeline_WhenUseFlagIsFalse()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent generate should not run"));
        var build = new FakeBuildOrchestrator(_ => Task.FromResult(new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            GenerationMode = "dedicated_build",
            Flow = new OperatorFlowDto()
        }));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = true },
            serviceProvider: ServiceProviderFor(build));

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("start from plan")
        {
            UseVisionAgentGenerateFlow = false,
            BuildFromPlan = BuildFromPlanRequest()
        });

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("dedicated_build");
        build.CallCount.Should().Be(1);
        agent.CallCount.Should().Be(0);
    }

    [Fact(DisplayName = "BuildFromPlan service chain should build confirmed plan without legacy RequirementBrief")]
    public async Task BuildFromPlanGenerateFlow_RealBuildChain_ShouldBuildConfirmedPlanWithoutLegacyBrief()
    {
        var sink = new CapturingAgentRunEventSink();
        var extractor = Substitute.For<IRequirementBriefExtractor>();
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent generate should not run"));
        var buildOrchestrator = CreateRealBuildOrchestrator(sink);
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = true },
            requirementBriefExtractor: extractor,
            serviceProvider: ServiceProviderFor(buildOrchestrator));

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("病灶检测")
        {
            AgentRunId = "ar_real_build_chain",
            UseVisionAgentGenerateFlow = false,
            RequirementMode = AiRequirementModes.Strict,
            BuildFromPlan = BuildableLesionBuildFromPlanRequest()
        });

        result.Success.Should().BeTrue(
            $"{result.ErrorMessage} | {result.FailureSummary?.Code} | {result.FailureSummary?.Message} | {result.RequirementMaturity?.PublicReason}");
        Flow(result).Operators.Should().NotBeEmpty();
        Flow(result).Operators.Select(item => item.Type).Should().Contain(OperatorType.SurfaceDefectDetection);
        result.BuildResult.Should().NotBeNull();
        result.BuildReadiness.Should().NotBeNull();
        result.BuildReadiness!.CanBuild.Should().BeTrue();
        result.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.Should().BeNull();
        result.FailureType.Should().BeNull();
        result.FailureSummary.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
        agent.CallCount.Should().Be(0);
        extractor.DidNotReceiveWithAnyArgs().Extract(default, default, default);
        var publicJson = JsonSerializer.Serialize(new { result, sink.Events }, AgentRunEventJson.Options);
        publicJson.Should().NotContain("请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景");
    }

    [Fact(DisplayName = "BuildFromPlan PlanId mismatch should fail before Build orchestrator")]
    public async Task BuildFromPlanGenerateFlow_PlanIdMismatch_ShouldFailBeforeBuildOrchestrator()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent generate should not run"));
        var build = new FakeBuildOrchestrator(_ => throw new InvalidOperationException("build should not run"));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = true },
            serviceProvider: ServiceProviderFor(build));
        var buildFromPlan = BuildFromPlanRequest();
        buildFromPlan = buildFromPlan with
        {
            PlanId = "plan-top-level",
            PlanSnapshot = buildFromPlan.PlanSnapshot! with
            {
                PlanId = "plan-snapshot"
            }
        };

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("start from plan")
        {
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = buildFromPlan
        });

        result.Success.Should().BeFalse();
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Code.Should().Be("build_from_plan_plan_id_mismatch");
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        build.CallCount.Should().Be(0);
        agent.CallCount.Should().Be(0);
    }

    [Fact(DisplayName = "BuildFromPlan controlled blocker should not fall back to legacy RequirementBriefExtractor")]
    public async Task BuildFromPlanGenerateFlow_ControlledBlocker_ShouldNotFallbackToLegacy()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent generate should not run"));
        var extractor = Substitute.For<IRequirementBriefExtractor>();
        var readiness = new VisionAgentBuildReadinessSnapshot
        {
            CanBuild = false,
            RemainingFields = ["image_source", "acceptance_criteria"],
            Blockers =
            [
                new VisionAgentBuildBlocker
                {
                    Id = "hard_requirement:image_source",
                    Category = VisionAgentBuildBlockerCategories.HardRequirement,
                    Field = "image_source",
                    BlocksBuild = true,
                    ResolutionMode = VisionAgentBuildBlockerResolutionModes.AnswerQuestion
                }
            ],
            PrimaryMessage = "Need canonical fields before Build.",
            ContractVersion = VisionAgentPlanContractVersions.V2
        };
        var build = new FakeBuildOrchestrator(_ => Task.FromResult(new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
            ClarificationRequired = true,
            BuildReadiness = readiness,
            BlockingClarificationFields = ["image_source", "acceptance_criteria"],
            RequirementMaturity = new AiRequirementMaturityResult
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = ["image_source", "acceptance_criteria"],
                PublicReason = "Need canonical fields before Build."
            }
        }));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = true },
            requirementBriefExtractor: extractor,
            serviceProvider: ServiceProviderFor(build));

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("start from plan")
        {
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildFromPlanRequest()
        });

        result.Success.Should().BeFalse();
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusClarificationRequired);
        result.BuildReadiness.Should().BeEquivalentTo(readiness);
        build.CallCount.Should().Be(1);
        agent.CallCount.Should().Be(0);
        extractor.DidNotReceiveWithAnyArgs().Extract(default, default, default);
    }

    [Fact(DisplayName = "BuildFromPlan system exception should return controlled new-pipeline failure without legacy fallback")]
    public async Task BuildFromPlanGenerateFlow_SystemException_ShouldNotFallbackToLegacy()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent generate should not run"));
        var extractor = Substitute.For<IRequirementBriefExtractor>();
        var build = new FakeBuildOrchestrator(_ => throw new InvalidOperationException("boom"));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = true },
            requirementBriefExtractor: extractor,
            serviceProvider: ServiceProviderFor(build));

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("start from plan")
        {
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildFromPlanRequest()
        });

        result.Success.Should().BeFalse();
        result.FailureSummary.Should().NotBeNull();
        result.FailureSummary!.Category.Should().Be("vision_agent_build_from_plan");
        result.FailureSummary.Code.Should().Be("build_from_plan_system_exception");
        result.ErrorMessage.Should().NotContain("boom");
        result.ClarificationRequired.Should().BeFalse();
        result.BuildReadiness.Should().NotBeNull();
        result.BuildReadiness!.CanBuild.Should().BeTrue();
        build.CallCount.Should().Be(1);
        agent.CallCount.Should().Be(0);
        extractor.DidNotReceiveWithAnyArgs().Extract(default, default, default);
    }

    [Fact(DisplayName = "Controlled agent should call ReadOnly Simulation and Precheck tools")]
    public async Task AgentGenerateFlow_ShouldCallToolChain()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var trace = Trace(result);
        trace.Select(item => item.GetProperty("toolName").GetString()).Should().Contain([
            "list_operator_catalog",
            "get_operator_schema",
            "match_flow_template",
            "inspect_current_flow",
            "get_flow_template_skeleton",
            "validate_flow",
            "dryrun_flow",
            "runtime_package_precheck"]);
        trace.Take(5)
            .Select(item => item.GetProperty("permission").GetString())
            .Should()
            .OnlyContain(permission => permission == nameof(VisionAgentToolPermission.ReadOnly));
        trace.TakeLast(3)
            .Select(item => item.GetProperty("toolName").GetString())
            .Should()
            .Equal("validate_flow", "dryrun_flow", "runtime_package_precheck");
    }

    [Fact(DisplayName = "AgentRun events should publish Plan and Build stage boundaries as public metadata")]
    public async Task VisionAgentOrchestrator_ShouldPublishPublicPlanBuildStageEvents()
    {
        var sink = new CapturingAgentRunEventSink();
        AiFlowGenerationRequest? capturedRequest = null;
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Do<AiFlowGenerationRequest>(request => capturedRequest = request),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<AiStreamChunk>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>?>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto()
            }));
        var registry = new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new FlowTemplateMatchTool(),
            new FlowValidationTool()
        ]);
        var buildOrchestrator = new FakeBuildOrchestrator(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto()
            });
        });
        var orchestrator = new VisionAgentOrchestrator(registry, sink, buildOrchestrator);
        var applicationService = BuildApplicationServiceFor(
            orchestrator,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = false },
            sink);

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "metal surface scratch detection",
                OriginalUserPrompt = "metal surface scratch detection",
                CurrentFlowSnapshot = "{\"operators\":[{\"id\":\"camera\"}]}",
                AttachmentSummary = new VisionAgentAttachmentSummary
                {
                    Count = 1,
                    ResourceKinds = ["sample_image_metadata"],
                    PathsRedacted = true
                }
            },
            CancellationToken.None);
        plan = plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) };
        var request = new AiFlowGenerationRequest("metal surface scratch detection", Mode: GenerateFlowMode.New)
        {
            AgentRunId = "ar_test",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers =
                [
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.InspectionObject,
                        Value = "metal surface",
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                    },
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.TaskType,
                        Value = AiVisionTaskTypes.SurfaceDefect,
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                    },
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.ImageSource,
                        Value = "camera",
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                    },
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.AcceptanceCriteria,
                        Value = "scratch is NG",
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                    }
                ],
                UserSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defect_definition"] = "scratch_or_blob"
                },
                AcceptedDefaults = ["defect_definition"],
                CurrentFlowSnapshot = "{\"operators\":[{\"id\":\"camera\"}]}",
                AttachmentSummary = new VisionAgentAttachmentSummary
                {
                    Count = 1,
                    ResourceKinds = ["sample_image_metadata"],
                    PathsRedacted = true
                },
                OperatorCatalogVersion = plan.OperatorCatalogVersion,
                StationBoundarySummary = plan.StationBoundarySummary,
                PlcOutputPolicy = plan.PlcOutputPolicy,
                BuildIntent = "new",
                OriginalUserPrompt = plan.OriginalUserPrompt,
                AcceptedRecommendedDefaults = true
            }
        };
        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                request,
                request.AgentRunId,
                transport: BuildCommandTransports.AgentRun,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.ReadinessBlocked);
        capturedRequest.Should().BeNull();
        plan.Intent.Should().Be("surface_defect");
        plan.RecommendedRoute.RouteId.Should().Be("surface_defect_detection");
        plan.ClarificationQuestions.Select(question => question.Id)
            .Should()
            .Contain(["q_fallback_image_source", "q_fallback_acceptance_criteria"]);
        plan.ClarificationQuestions.Should().OnlyContain(question =>
            question.Options.Count > 0 &&
            question.Options.Any(option => option.Recommended && option.Value.EndsWith("_pending", StringComparison.OrdinalIgnoreCase)));
        plan.ClarificationQuestions.Select(question => question.DefaultValue)
            .Should()
            .OnlyContain(value => value.EndsWith("_pending", StringComparison.OrdinalIgnoreCase));
        plan.CanBuild.Should().BeFalse();
        plan.PublicEvents.Select(evt => evt.Stage).Should().Contain(["collecting_context", "rule_fallback_used", "plan_ready"]);
        plan.PublicEvents.Should().OnlyContain(evt => evt.MetadataOnly);
        sink.Events.Select(evt => evt.Stage).Should().ContainInOrder(["canonical_build_contract", "canonical_build_readiness"]);
        sink.Events.Should().OnlyContain(evt => evt.MetadataOnly);

        var publicJson = JsonSerializer.Serialize(new { plan.PublicEvents, sink.Events });
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("reasoningContent");
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("chainOfThought");
    }

    [Fact(DisplayName = "Plan hash mismatch should publish public diagnostic without hidden prompt fields")]
    public async Task VisionAgentOrchestrator_ShouldPublishPlanHashMismatchDiagnostic()
    {
        var sink = new CapturingAgentRunEventSink();
        var registry = new VisionAgentToolRegistry([new OperatorCatalogTool()]);
        var orchestrator = new VisionAgentOrchestrator(
            registry,
            sink,
            new FakeBuildOrchestrator(_ => Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto()
            })));
        var applicationService = BuildApplicationServiceFor(
            orchestrator,
            new AgentGenerateFlowOptions { Enabled = true, FallbackToLegacyOnFailure = false },
            sink);

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "wire sequence inspection",
                OriginalUserPrompt = "SECRET_RAW_PROMPT"
            },
            CancellationToken.None);

        var request = new AiFlowGenerationRequest("wire sequence inspection", Mode: GenerateFlowMode.New)
        {
            AgentRunId = "ar_hash_mismatch",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = "sha256:mismatched-plan-hash",
                PlanSnapshot = plan,
                BuildIntent = "new",
                OriginalUserPrompt = plan.OriginalUserPrompt,
                MetadataOnly = true
            }
        };
        var result = (await applicationService.BuildAsync(
            BuildCommand.FromGenerationRequest(
                request,
                request.AgentRunId,
                transport: BuildCommandTransports.AgentRun,
                persistResult: false),
            CancellationToken.None)).Result;

        result.Success.Should().BeFalse();
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.StalePlan);
        var diagnostic = sink.Events.Single(evt => evt.Stage == "canonical_build_contract");
        diagnostic.Status.Should().Be(AgentRunEventStatuses.Failed);
        var diagnosticJson = JsonSerializer.Serialize(diagnostic, AgentRunEventJson.Options);
        diagnosticJson.Should().Contain(VisionAgentBuildFailureCodes.StalePlan);
        diagnosticJson.Should().NotContain("SECRET_RAW_PROMPT");
        diagnosticJson.Should().NotContain("systemPrompt");
        diagnosticJson.Should().NotContain("rawPrompt");
        diagnosticJson.Should().NotContain("reasoning_content");
        diagnosticJson.Should().NotContain("chain-of-thought");
        diagnosticJson.Should().NotContain("chainOfThought");
    }

    [Fact(DisplayName = "Wire sequence request should generate draft and model resource pending action")]
    public async Task AgentGenerateFlow_WireSequence_ShouldReturnModelResourcePendingAction()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("terminal wire sequence inspection"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.DeepLearning);
        result.MissingResources.Select(item => item.ResourceType).Should().Contain("model_resource");
        Json(result.PendingActions).GetRawText().Should().Contain("ModelPath");
    }

    [Fact(DisplayName = "Template matching request should generate draft and template artifact pending action")]
    public async Task AgentGenerateFlow_TemplateMatching_ShouldReturnTemplateArtifactPendingAction()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("template matching alignment for bracket"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.TemplateMatching);
        result.MissingResources.Select(item => item.ResourceType).Should().Contain("template_artifact");
        Json(result.PendingActions).GetRawText().Should().Contain("Template");
        Json(result.PendingActions).GetRawText().Should().NotContain("TemplatePath");
    }

    [Fact(DisplayName = "Hole distance measurement request should generate measurement draft")]
    public async Task AgentGenerateFlow_HoleDistance_ShouldReturnMeasurementDraft()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("hole distance measurement in mm"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.CircleMeasurement);
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.Measurement);
    }

    [Fact(DisplayName = "Missing resources should not block workflow draft")]
    public async Task AgentGenerateFlow_MissingResources_ShouldAllowWorkflowDraft()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Flow.Should().NotBeNull();
        var precheck = ValidationPreview(result).GetProperty("deploymentPrecheck");
        precheck.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        precheck.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Existing flow structural error should enter validationPreview blockingIssues")]
    public async Task AgentGenerateFlow_StructuralError_ShouldEnterValidationPreview()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest(
                "validate existing flow",
                existingFlowJson: JsonSerializer.Serialize(BrokenConnectionFlow())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var structural = ValidationPreview(result).GetProperty("structuralValidation");
        Codes(structural, "blockingIssues").Should().Contain("invalid_connection");
    }

    [Fact(DisplayName = "Deployment precheck should not deploy create package or touch station")]
    public async Task AgentGenerateFlow_Precheck_ShouldNeverDeploy()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("terminal wire sequence inspection"),
            CancellationToken.None);

        var precheck = ValidationPreview(result).GetProperty("deploymentPrecheck");
        precheck.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        precheck.GetProperty("deployed").GetBoolean().Should().BeFalse();
        precheck.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        precheck.GetProperty("stationTouched").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Agent failure should return controlled error when fallback is disabled")]
    public async Task AgentGenerateFlowFailure_ShouldReturnControlledError()
    {
        var service = CreateAiFlowGenerationService(
            new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("scripted failure")),
            new AgentGenerateFlowOptions
            {
                Enabled = true,
                FallbackToLegacyOnFailure = false
            });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("wire sequence")
        {
            UseVisionAgentGenerateFlow = true
        });

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        result.ErrorMessage.Should().Contain("Vision Agent GenerateFlow 失败");
    }

    [Fact(DisplayName = "GenerateFlow response should map toolTrace pendingActions missingResources and validationPreview")]
    public async Task GenerateFlowMessageHandler_ShouldMapAgentFields()
    {
        var flow = new OperatorFlowDto();
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<AiStreamChunk>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>?>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = flow,
                MissingResources = [new AiMissingResourceInfo { ResourceType = "model_resource", ResourceKey = "op.ModelPath", Description = "missing" }],
                PendingActions = [new { actionType = "provide_missing_resource" }],
                ValidationPreview = new { deploymentPrecheck = new { workflowDraftAllowed = true } },
                ToolTrace = [new { toolName = "validate_flow", success = true }]
            }));
        var handler = new GenerateFlowMessageHandler(
            generationService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>());

        var json = await handler.HandleAsync(
            "Check wire order on the harness terminal from camera. OK when order is correct, NG otherwise. Use model strategy.",
            useVisionAgentGenerateFlow: true);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("missingResources").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("pendingActions").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("toolTrace").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("validationPreview").GetProperty("deploymentPrecheck")
            .GetProperty("workflowDraftAllowed")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    [Fact(DisplayName = "Controlled agent source guard should exclude RuntimePreview hardware network and process APIs")]
    public void SourceGuard_ShouldExcludeRuntimePreviewHardwareNetworkAndProcess()
    {
        var source = ReadSourceUnder(Path.Combine(
            GetProductRoot(),
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "Agent")) +
                     ReadSourceUnder(Path.Combine(
                         GetProductRoot(),
                         "src",
                         "ClearVision.Product.Infrastructure",
                         "AI",
                         "Tools"));
        var forbidden = new[]
        {
            "CameraTestFrameTool",
            "ReplayFlowWithFrameTool",
            "AcquireSingleFrameAsync",
            "EnumerateCamerasAsync",
            "GetOrCreateByBindingAsync",
            "HttpClient",
            "TcpClient",
            "Socket",
            "File.ReadAllBytes",
            "Cv2.ImRead",
            "Image.FromFile",
            "Process.Start",
            "ProcessStartInfo",
            "cmd.exe",
            "execute_command"
        };

        forbidden.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Mainline guard should keep Agent loop out of default GenerateFlow and keep frontend Agent entry hidden")]
    public void MainlineGuard_ShouldKeepAgentLoopOutOfDefaultGenerateFlowAndFrontend()
    {
        var productRoot = GetProductRoot();
        var generateFlowService = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "AiFlowGenerationService.cs"));
        var frontendSource = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Desktop",
            "wwwroot",
            "src",
            "features",
            "ai",
            "aiPanel.js"));
        var frontendGenerateRequestSource = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Desktop",
            "wwwroot",
            "src",
            "features",
            "ai",
            "aiPanelGenerateRequest.js"));
        var frontendGuardSource = string.Join(Environment.NewLine, frontendSource, frontendGenerateRequestSource);

        generateFlowService.Should().NotContain("VisionAgentLoop");
        generateFlowService.Should().Contain("ShouldRunAgentGenerateFlow");
        generateFlowService.Should().Contain("request.UseVisionAgentGenerateFlow");
        frontendSource.Should().Contain("aiPanelGenerateRequestMixin");
        frontendGuardSource.Should().Contain("_isAgentDeveloperControlsEnabled");
        frontendGuardSource.Should().Contain("_buildAgentGenerateFlowRequestPayload");
        frontendGuardSource.Should().Contain("useVisionAgentGenerateFlow: true");
        frontendGuardSource.Should().NotContain("capture_test_frame");
        frontendGuardSource.Should().NotContain("replay_flow_with_frame");
    }

    [Fact(DisplayName = "Controlled agent should be enabled by default in options")]
    public void AgentGenerateFlowOptions_ShouldDefaultEnabled()
    {
        new AgentGenerateFlowOptions().Enabled.Should().BeTrue();
    }

    private static AiFlowGenerationRequest AgentRequest(string description, string? existingFlowJson = null)
    {
        return new AiFlowGenerationRequest(description, ExistingFlowJson: existingFlowJson)
        {
            UseVisionAgentGenerateFlow = true
        };
    }

    private static VisionAgentGenerateFlowService CreateVisionAgentGenerateFlowService(
        IAgentRunEventSink? eventSink = null)
    {
        var loopOptions = new VisionAgentLoopOptions
        {
            MaxToolRounds = 8,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 64_000
        };
        var registry = new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new OperatorSchemaTool(),
            new OperatorKnowledgeTool(),
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new CurrentFlowInspectTool(),
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePackagePrecheckTool()
        ]);
        var loop = new VisionAgentLoop(
            registry,
            new VisionAgentProtocolParser(),
            new AgentPromptBuilder(),
            Options.Create(loopOptions));

        return new VisionAgentGenerateFlowService(
            loop,
            Options.Create(loopOptions),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService>>(),
            eventSink: eventSink);
    }

    private static VisionAgentBuildOrchestrator CreateRealBuildOrchestrator(
        IAgentRunEventSink? eventSink = null)
    {
        var redactor = new AgentRunEventRedactor();
        var registry = new VisionAgentToolRegistry(
        [
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePackagePrecheckTool()
        ]);
        var toolRunner = new BuildToolRunner(registry, redactor, eventSink);
        return new VisionAgentBuildOrchestrator(
            new BuildPlanContextLoader(eventSink),
            new BuildIntentResolver(),
            new TemplateStrategyResolver(toolRunner),
            new PlanSelectionResolver(),
            new OperatorPipelineSelector(),
            new ParameterMappingService(),
            new WorkflowDraftBuilder(),
            toolRunner,
            new BuildReadinessReviewService(),
            new WorkflowDiffService(),
            new ApplyGateResolver(),
            new BuildResultAssembler(redactor, eventSink),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator>>(),
            eventSink);
    }

    private static AiFlowGenerationService CreateAiFlowGenerationService(
        IVisionAgentGenerateFlowService agentGenerateFlowService,
        AgentGenerateFlowOptions agentOptions,
        IRequirementBriefExtractor? requirementBriefExtractor = null,
        IServiceProvider? serviceProvider = null,
        IConversationalFlowService? conversationService = null)
    {
        if (conversationService == null)
        {
            conversationService = Substitute.For<IConversationalFlowService>();
            ConfigureConversationService(conversationService);
        }
        var turnRouter = Substitute.For<IAiTurnRouter>();
        turnRouter.Route(Arg.Any<AiTurnRouteRequest>())
            .Returns(new AiTurnRoute(
                AiTurnIntents.ChatOrHelp,
                AiInteractionStates.Idle,
                AiRouterConfidence.High,
                ShouldShortCircuit: true,
                Reply: "hello"));
        var promptVersionManager = Substitute.For<IPromptVersionManager>();
        promptVersionManager.GetActiveVersionAsync().Returns(Task.FromResult(new PromptVersion
        {
            Id = Guid.NewGuid(),
            Name = "Test Prompt",
            Content = "test"
        }));
        var operatorFactory = Substitute.For<IOperatorFactory>();
        var flowExecutionService = Substitute.For<IFlowExecutionService>();

        var buildRunHarness = BuildRunServiceFor(serviceProvider, agentOptions, conversationService);
        return new AiFlowGenerationService(
            new AiGenerationOrchestrator(
                Substitute.For<IAiModelSelector>(),
                Substitute.For<IAiConnectorFactory>()),
            new PromptBuilder(operatorFactory),
            conversationService,
            Substitute.For<IAiFlowValidator>(),
            new AutoLayoutService(),
            operatorFactory,
            Substitute.For<IFlowTemplateService>(),
            Substitute.For<IScenarioMatcher>(),
            requirementBriefExtractor ?? Substitute.For<IRequirementBriefExtractor>(),
            turnRouter,
            Substitute.For<ITemplateConstraintValidator>(),
            new AiFlowResponseParser(),
            new DryRunService(flowExecutionService),
            Substitute.For<IHostEnvironment>(),
            promptVersionManager,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService>>(),
            Options.Create(agentOptions),
            agentGenerateFlowService,
            buildRunHarness?.RunService,
            buildRunHarness?.StreamService);
    }

    private static void ConfigureConversationService(IConversationalFlowService conversationService)
    {
        conversationService.PrepareContext(Arg.Any<AiFlowGenerationRequest>())
            .Returns(new ConversationContext
            {
                SessionId = "session",
                Intent = ConversationIntent.New,
                Mode = GenerateFlowMode.Auto
            });
        conversationService.GetSession("session").Returns(new ConversationSession { SessionId = "session" });
        conversationService.GetOrCreateSession(Arg.Any<string?>())
            .Returns(call => new ConversationSession
            {
                SessionId = string.IsNullOrWhiteSpace(call.Arg<string?>())
                    ? "session"
                    : call.Arg<string?>()!.Trim()
            });
        conversationService.RecordAssistantResponseWithPersistence(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ConversationTurnPayload?>())
            .Returns(new ConversationSessionWriteResult
            {
                Success = true,
                Session = new ConversationSession { SessionId = "session" },
                PersistenceStatus = new ConversationPersistenceStatus()
            });
    }

    private static BuildRunHarness? BuildRunServiceFor(
        IServiceProvider? serviceProvider,
        AgentGenerateFlowOptions options,
        IConversationalFlowService conversationService)
    {
        if (serviceProvider?.GetService(typeof(IVisionAgentBuildOrchestrator)) is not IVisionAgentBuildOrchestrator build)
        {
            return null;
        }

        var directory = Path.Combine(Path.GetTempPath(), "clearvision-test-build-run-" + Guid.NewGuid().ToString("N"));
        var redactor = new AgentRunEventRedactor();
        var store = new AgentRunEventStore(directory, redactor);
        var streamService = new AgentRunEventStreamService(store, redactor);
        var journal = new VisionAgentBuildProjectionJournal(store, redactor);
        var projector = new VisionAgentBuildTerminalProjector(
            conversationService,
            journal,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>());
        var applicationService = new VisionAgentBuildApplicationService(
            new BuildOrchestratorExecution(build),
            new VisionAgentPlanAnswerValidator(),
            new VisionAgentPlanRequirementOverlay(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService>>(),
            Options.Create(options));
        var runService = new VisionAgentBuildRunService(
            applicationService,
            streamService,
            conversationService,
            projector,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildRunService>>());
        return new BuildRunHarness(runService, streamService);
    }

    private sealed record BuildRunHarness(
        IVisionAgentBuildRunService RunService,
        IAgentRunEventStreamService StreamService);

    private static IVisionAgentBuildApplicationService BuildApplicationServiceFor(
        IVisionAgentOrchestrator execution,
        AgentGenerateFlowOptions options,
        IAgentRunEventSink? eventSink = null)
    {
        return new VisionAgentBuildApplicationService(
            execution,
            new VisionAgentPlanAnswerValidator(),
            new VisionAgentPlanRequirementOverlay(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService>>(),
            Options.Create(options),
            eventSink);
    }

    private static VisionAgentBuildFromPlanRequest BuildableLesionBuildFromPlanRequest()
    {
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.SurfaceDefect,
            Confidence = 0.94,
            TaskTypeConfidence = 0.92,
            InspectionObject = "病灶",
            DefectType = "lesion",
            ImageSource = "camera",
            OkCondition = "无病灶为 OK",
            NgCondition = "检出病灶为 NG",
            OutputTarget = "local report",
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            ObjectSignals = ["病灶"],
            TaskSignals = [AiVisionTaskTypes.SurfaceDefect],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = "病灶检测"
            },
            semantic);
        var plan = new VisionAgentPlanModeResult
        {
            PlanId = "plan-lesion-build",
            PlanContractVersion = VisionAgentPlanContractVersions.V2,
            OriginalUserPrompt = "病灶检测",
            Goal = "病灶检测",
            Intent = "surface_defect",
            Confidence = "high",
            RequirementUnderstanding = ["Use confirmed metadata to detect lesion-like defects."],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_route",
                Title = "Surface defect route",
                Summary = "Acquisition, defect detection, judgment, and output.",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
                TemplateDecision = "planner_route"
            },
            ClarificationQuestions = [],
            RecommendedDefaults =
            [
                new VisionAgentDefaultAssumption
                {
                    Id = "metadata_only",
                    Label = "Metadata only",
                    Value = "pending_resources",
                    Impact = "No raw resources are guessed."
                }
            ],
            Risks = ["Model resource remains pending before deployment."],
            AcceptanceCriteria = ["OK means no lesion is detected; NG means lesion is detected."],
            ExecutablePlan = ["Build editable draft", "Bind resources", "Review release gates"],
            CanBuild = true,
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ResolvedFields = ["inspection_object", "task_type", "image_source", "acceptance_criteria", "algorithm_strategy"],
                RemainingFields = [],
                PrimaryMessage = "Ready",
                ContractVersion = VisionAgentPlanContractVersions.V2
            },
            ConfirmedPlanAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.InspectionObject,
                    Value = "lesion",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.TaskType,
                    Value = AiVisionTaskTypes.SurfaceDefect,
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Value = "camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    Value = "OK means no lesion is detected; NG means lesion is detected.",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.AlgorithmStrategy,
                    Value = "surface_defect_rule",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            SemanticExtraction = semantic,
            RequirementMaturity = maturity,
            NextAction = "Build",
            OperatorCatalogVersion = "catalog.v1",
            TemplateCatalogVersion = "template.v1",
            StationBoundarySummary = "metadata-only Station boundary",
            PlcOutputPolicy = "local ResultOutput first; PLC writes disabled",
            MetadataOnly = true
        };
        plan = plan with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
        };
        return new VisionAgentBuildFromPlanRequest
        {
            PlanId = plan.PlanId,
            PlanHash = plan.PlanHash,
            PlanSnapshot = plan,
            ConfirmedAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.InspectionObject,
                    Value = "病灶",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.TaskType,
                    Value = AiVisionTaskTypes.SurfaceDefect,
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Value = "camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    Value = "OK means no lesion is detected; NG means lesion is detected.",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.AlgorithmStrategy,
                    Value = "surface_defect_rule",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            UserSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["defect_definition"] = "lesion",
                ["algorithm_strategy"] = "surface_defect_rule"
            },
            AcceptedDefaults = ["metadata_only"],
            AcceptedRecommendedDefaults = true,
            OperatorCatalogVersion = plan.OperatorCatalogVersion,
            StationBoundarySummary = plan.StationBoundarySummary,
            PlcOutputPolicy = plan.PlcOutputPolicy,
            BuildIntent = "new",
            OriginalUserPrompt = plan.OriginalUserPrompt,
            MetadataOnly = true
        };
    }

    private static VisionAgentBuildFromPlanRequest BuildFromPlanRequest()
    {
        var request = new VisionAgentBuildFromPlanRequest
        {
            PlanId = "plan-build-from-plan",
            PlanSnapshot = new VisionAgentPlanModeResult
            {
                PlanId = "plan-build-from-plan",
                PlanContractVersion = VisionAgentPlanContractVersions.V2,
                OriginalUserPrompt = "detect circular hole offset",
                Goal = "detect circular hole offset",
                Intent = AiVisionTaskTypes.GeometryMeasurement,
                Confidence = "high",
                RequirementUnderstanding = ["Measure circular hole center offset."],
                RecommendedRoute = new VisionAgentRecommendedRoute
                {
                    RouteId = "measurement",
                    Title = "Measurement",
                    Summary = "Measure center offset.",
                    Operators = ["ImageAcquisition", "CircleDetection", "Measurement", "ResultOutput"],
                    TemplateDecision = "planner_route"
                },
                CanBuild = true,
                BuildReadiness = new VisionAgentBuildReadinessSnapshot
                {
                    CanBuild = true,
                    ResolvedFields = ["inspection_object", "task_type", "image_source", "acceptance_criteria"],
                    PrimaryMessage = "Ready",
                    ContractVersion = VisionAgentPlanContractVersions.V2
                },
                MetadataOnly = true
            },
            ConfirmedAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.InspectionObject,
                    Value = "circular hole",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.TaskType,
                    Value = AiVisionTaskTypes.GeometryMeasurement,
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = "image_source",
                    Value = "camera",
                    QuestionId = "image_source",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    Value = "center offset within tolerance",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                }
            ],
            MetadataOnly = true
        };
        var hash = VisionAgentOrchestrator.ComputePlanHash(request.PlanSnapshot);
        return request with
        {
            PlanHash = hash,
            PlanSnapshot = request.PlanSnapshot! with { PlanHash = hash }
        };
    }

    private static IServiceProvider ServiceProviderFor(IVisionAgentBuildOrchestrator buildOrchestrator)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IVisionAgentBuildOrchestrator)).Returns(buildOrchestrator);
        return provider;
    }

    private static object BrokenConnectionFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string>())
            },
            connections = new object[]
            {
                Connection("op_missing", "Image", "op_match", "Image")
            }
        };
    }

    private static object Operator(
        string tempId,
        string operatorType,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        return new
        {
            tempId,
            operatorType,
            parameters = parameters ?? new Dictionary<string, string>()
        };
    }

    private static object Connection(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new
        {
            sourceTempId,
            sourcePortName,
            targetTempId,
            targetPortName
        };
    }

    private static OperatorFlowDto Flow(AiFlowGenerationResult result)
    {
        result.Flow.Should().BeOfType<OperatorFlowDto>();
        return (OperatorFlowDto)result.Flow!;
    }

    private static IReadOnlyList<JsonElement> Trace(AiFlowGenerationResult result)
    {
        return Json(result.ToolTrace)
            .EnumerateArray()
            .Select(item => item.Clone())
            .ToList();
    }

    private static JsonElement ValidationPreview(AiFlowGenerationResult result)
    {
        return Json(result.ValidationPreview);
    }

    private static IReadOnlyList<string> Codes(JsonElement payload, string propertyName)
    {
        return payload.GetProperty(propertyName)
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString() ?? string.Empty)
            .ToList();
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static string ReadSourceUnder(string directory)
    {
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string GetProductRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }

    private sealed class FakeAgentGenerateFlowService : IVisionAgentGenerateFlowService
    {
        private readonly Func<AiFlowGenerationRequest, Task<AiFlowGenerationResult>> _handler;

        public FakeAgentGenerateFlowService(Func<AiFlowGenerationRequest, Task<AiFlowGenerationResult>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<AiFlowGenerationResult> GenerateFlowAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _handler(request);
        }
    }

    private sealed class FakeBuildOrchestrator : IVisionAgentBuildOrchestrator
    {
        private readonly Func<AiFlowGenerationRequest, Task<AiFlowGenerationResult>> _handler;

        public FakeBuildOrchestrator(Func<AiFlowGenerationRequest, Task<AiFlowGenerationResult>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<AiFlowGenerationResult> BuildAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _handler(request);
        }
    }

    private sealed class BuildOrchestratorExecution : IVisionAgentOrchestrator
    {
        private readonly IVisionAgentBuildOrchestrator _buildOrchestrator;

        public BuildOrchestratorExecution(IVisionAgentBuildOrchestrator buildOrchestrator)
        {
            _buildOrchestrator = buildOrchestrator;
        }

        public Task<VisionAgentPlanModeResult> CreatePlanAsync(
            VisionAgentPlanModeRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AiFlowGenerationResult> BuildFromPlanAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            return _buildOrchestrator.BuildAsync(request, cancellationToken);
        }
    }

    private sealed class CapturingAgentRunEventSink : IAgentRunEventSink
    {
        public List<AgentRunEventDraft> Events { get; } = [];

        public void Append(string? runId, AgentRunEventDraft draft)
        {
            Events.Add(draft);
        }

        public void StageStarted(string? runId, string stage, string title, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageStarted,
                Stage = stage,
                Title = title,
                Summary = summary,
                Status = AgentRunEventStatuses.Running,
                Payload = payload
            });
        }

        public void StageCompleted(string? runId, string stage, string title, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageCompleted,
                Stage = stage,
                Title = title,
                Summary = summary,
                Status = AgentRunEventStatuses.Completed,
                Payload = payload
            });
        }

        public void ToolStarted(string? runId, string stage, string toolName, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallStarted,
                Stage = stage,
                Title = toolName,
                Status = AgentRunEventStatuses.Running,
                Payload = payload
            });
        }

        public void ToolCompleted(string? runId, string stage, string toolName, long durationMs, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallCompleted,
                Stage = stage,
                Title = toolName,
                Status = AgentRunEventStatuses.Completed,
                Payload = payload
            });
        }

        public void ToolFailed(string? runId, string stage, string toolName, long durationMs, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallFailed,
                Stage = stage,
                Title = toolName,
                Summary = summary,
                Status = AgentRunEventStatuses.Failed,
                Payload = payload
            });
        }
    }
}
