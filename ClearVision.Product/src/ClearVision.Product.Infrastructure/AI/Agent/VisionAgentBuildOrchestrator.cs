using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentBuildOrchestrator
{
    Task<AiFlowGenerationResult> BuildAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentBuildOrchestrator : IVisionAgentBuildOrchestrator
{
    private const int MaxRepairRounds = 2;

    private readonly IAgentRunEventSink? _eventSink;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator> _logger;
    private readonly BuildPlanContextLoader _contextLoader;
    private readonly BuildIntentResolver _intentResolver;
    private readonly TemplateStrategyResolver _templateStrategyResolver;
    private readonly PlanSelectionResolver _planSelectionResolver;
    private readonly OperatorPipelineSelector _pipelineSelector;
    private readonly ParameterMappingService _parameterMapper;
    private readonly WorkflowDraftBuilder _workflowDraftBuilder;
    private readonly BuildToolRunner _toolRunner;
    private readonly BuildReadinessReviewService _readinessReview;
    private readonly WorkflowDiffService _workflowDiffService;
    private readonly ApplyGateResolver _applyGateResolver;
    private readonly BuildResultAssembler _resultAssembler;

    public VisionAgentBuildOrchestrator(
        BuildPlanContextLoader contextLoader,
        BuildIntentResolver intentResolver,
        TemplateStrategyResolver templateStrategyResolver,
        PlanSelectionResolver planSelectionResolver,
        OperatorPipelineSelector pipelineSelector,
        ParameterMappingService parameterMapper,
        WorkflowDraftBuilder workflowDraftBuilder,
        BuildToolRunner toolRunner,
        BuildReadinessReviewService readinessReview,
        WorkflowDiffService workflowDiffService,
        ApplyGateResolver applyGateResolver,
        BuildResultAssembler resultAssembler,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator> logger,
        IAgentRunEventSink? eventSink = null)
    {
        _eventSink = eventSink;
        _logger = logger;
        _contextLoader = contextLoader;
        _intentResolver = intentResolver;
        _templateStrategyResolver = templateStrategyResolver;
        _planSelectionResolver = planSelectionResolver;
        _pipelineSelector = pipelineSelector;
        _parameterMapper = parameterMapper;
        _workflowDraftBuilder = workflowDraftBuilder;
        _toolRunner = toolRunner;
        _readinessReview = readinessReview;
        _workflowDiffService = workflowDiffService;
        _applyGateResolver = applyGateResolver;
        _resultAssembler = resultAssembler;
    }

    public async Task<AiFlowGenerationResult> BuildAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runId = request.AgentRunId;
        var buildId = $"build_{Guid.NewGuid():N}";
        var evidence = new List<VisionAgentToolEvidence>();
        var publicWarnings = new List<string>();
        var autoRepairs = new List<VisionAgentBuildRepairRecord>();
        var build = request.BuildFromPlan;
        var plan = build?.PlanSnapshot;

        try
        {
            var loadPlan = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "plan_generation",
                "plan_snapshot_loader",
                "加载 PlanSnapshot、planHash、选择项、默认值、当前流程、模板、附件和工站边界元数据。",
                _ => Task.FromResult(_contextLoader.Load(request, build, plan, publicWarnings)),
                cancellationToken);
            _eventSink?.StageCompleted(
                runId,
                "assumption_confirmation",
                "假设已确认",
                "构建模式已收到结构化选择和已接受默认值。",
                new
                {
                    acceptedRecommendedDefaults = loadPlan.Payload.AcceptedRecommendedDefaults,
                    defaultCount = loadPlan.Payload.AcceptedDefaults.Count,
                    plcOutputPolicy = loadPlan.Payload.PlcOutputPolicy,
                    metadataOnly = true,
                    redactionPass = true
                });

            var maturityGate = EnforceMaturityGate(request, loadPlan.Payload);
            if (maturityGate != null)
            {
                _eventSink?.StageCompleted(
                    runId,
                    "requirement_maturity_gate",
                    "Build blocked by requirement maturity gate",
                    maturityGate.RequirementMaturity?.PublicReason ?? "Requirement maturity gate blocked Build.",
                    new
                    {
                        maturity = maturityGate.RequirementMaturity?.Maturity ?? string.Empty,
                        taskType = maturityGate.RequirementMaturity?.TaskType ?? string.Empty,
                        canBuild = maturityGate.RequirementMaturity?.CanBuild ?? false,
                        blockingReasons = maturityGate.DecisionTrace?.BlockingReasons ?? [],
                        resolvedFields = loadPlan.Payload.ResolvedFields,
                        remainingFields = loadPlan.Payload.RemainingFields,
                        answerSetFingerprint = loadPlan.Payload.AnswerSetFingerprint,
                        requirementMode = loadPlan.Payload.RequirementMode,
                        metadataOnly = true,
                        redactionPass = true
                    });
                return maturityGate;
            }

            var intent = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "resolve_build_intent",
                "build_intent_resolver",
                "解析本次构建是新建、修改、解释、补全参数还是重构。",
                _ => Task.FromResult(_intentResolver.Resolve(request, build, loadPlan.Payload)),
                cancellationToken);
            _eventSink?.StageCompleted(
                runId,
                "requirement_parsing",
                "需求解析完成",
                "结构化计划、选择项和仅元数据上下文已准备好交给构建工具。",
                new
                {
                    buildIntent = intent.Payload.BuildIntent,
                    hasExistingFlow = loadPlan.Payload.HasCurrentFlow,
                    attachmentCount = loadPlan.Payload.AttachmentSummary.Count,
                    templateSelectionMode = loadPlan.Payload.TemplateSelection?.Mode ?? string.Empty,
                    metadataOnly = true,
                    redactionPass = true
                });

            var toolContext = _contextLoader.BuildToolContext(
                request,
                build,
                loadPlan.Payload.CurrentFlowSnapshot);
            var template = await _templateStrategyResolver.ResolveAsync(
                runId,
                evidence,
                loadPlan.Payload,
                toolContext,
                cancellationToken);

            var planSelection = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "plan_selection",
                "plan_selection_resolver",
                "Resolve confirmed user strategy and planner route into an effective catalog-backed Build route.",
                _ => Task.FromResult(_planSelectionResolver.Resolve(loadPlan.Payload, template.Payload, publicWarnings)),
                cancellationToken);
            var blockingSelectionReasons = planSelection.Payload.BlockingReasons
                .Where(PlanSelectionResolver.IsHardOrStrategyBlocker)
                .ToList();
            if (blockingSelectionReasons.Count > 0)
            {
                var maturityRequest = BuildMaturityRequest(request, build, loadPlan.Payload);
                return BuildMaturityBlockedResult(
                    request,
                    maturityRequest,
                    loadPlan.Payload.EffectiveRequirement.Maturity,
                    blockingSelectionReasons);
            }

            var pipeline = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "operator_pipeline",
                "operator_pipeline_selector",
                "根据计划路线、模板策略和算子目录选择并修复算子链。",
                _ => Task.FromResult(_pipelineSelector.Select(loadPlan.Payload, template.Payload, planSelection.Payload, publicWarnings)),
                cancellationToken);

            var parameterMapping = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "parameter_mapping",
                "parameter_mapper",
                "将用户选择和已接受默认值映射到算子参数，并保持未知资源待确认。",
                _ => Task.FromResult(_parameterMapper.Map(loadPlan.Payload, pipeline.Payload, planSelection.Payload)),
                cancellationToken);

            var draft = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "workflow_draft",
                "workflow_drafter",
                "在计划约束下生成或修改可编辑工作流草稿。",
                async ct => await _workflowDraftBuilder.DraftAsync(
                    request,
                    loadPlan.Payload,
                    intent.Payload,
                    pipeline.Payload,
                    parameterMapping.Payload,
                    ct),
                cancellationToken,
                AgentRunEventTypes.WorkflowDraftUpdated);

            var validation = await _toolRunner.RunRegisteredToolAsync(
                runId,
                evidence,
                toolContext,
                "validate_schema",
                "validate_flow",
                "校验流程结构、连线、已知端口、算子目录和缺失资源占位。",
                new
                {
                    flow = draft.Payload.WorkflowDraft,
                    entryOperatorTempId = draft.Payload.EntryOperatorTempId
                },
                cancellationToken,
                AgentRunEventTypes.ReadinessChecked);

            var currentDraft = draft.Payload;
            for (var repairRound = 1;
                 repairRound <= MaxRepairRounds && ShouldAttemptRepair(validation.Payload, out var repairIssueCodes);
                 repairRound++)
            {
                var repair = await _toolRunner.ExecuteEvidenceStepAsync(
                    runId,
                    evidence,
                    "repair_loop",
                    $"auto_repair_round_{repairRound}",
                    $"对可安全修复的校验问题尝试第 {repairRound} 轮自动修复：{string.Join(",", repairIssueCodes)}。",
                    _ => Task.FromResult(_workflowDraftBuilder.Repair(
                        currentDraft,
                        pipeline.Payload,
                        parameterMapping.Payload,
                        repairIssueCodes,
                        repairRound)),
                    cancellationToken);
                currentDraft = repair.Payload.Draft;
                autoRepairs.Add(repair.Payload.Record);

                validation = await _toolRunner.RunRegisteredToolAsync(
                    runId,
                    evidence,
                    toolContext,
                    "validate_schema",
                    "validate_flow",
                    "自动修复后重新执行结构校验。",
                    new
                    {
                        flow = currentDraft.WorkflowDraft,
                        entryOperatorTempId = currentDraft.EntryOperatorTempId
                    },
                    cancellationToken,
                    AgentRunEventTypes.ReadinessChecked);
            }

            var dryRun = await _toolRunner.RunRegisteredToolAsync(
                runId,
                evidence,
                toolContext,
                "metadata_dry_run",
                "dryrun_flow",
                "对拓扑、IO、参数完整性和运行边界执行仅元数据预演。",
                new
                {
                    flow = currentDraft.WorkflowDraft,
                    entryOperatorTempId = currentDraft.EntryOperatorTempId
                },
                cancellationToken,
                AgentRunEventTypes.ManifestDryRunCompleted);

            var packageReadiness = await _toolRunner.RunRegisteredToolAsync(
                runId,
                evidence,
                toolContext,
                "package_readiness",
                "runtime_package_precheck",
                "在不创建运行包、不加载文件、不触碰工站资源的前提下检查运行包就绪。",
                new
                {
                    flow = currentDraft.WorkflowDraft,
                    validationSummary = validation.Payload.Data,
                    dryRunSummary = dryRun.Payload.Data,
                    requireReplay = false
                },
                cancellationToken,
                AgentRunEventTypes.PackageReadinessChecked);

            var stationCompatibility = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "station_compatibility",
                "station_compatibility_checker",
                "按仅元数据边界检查工站、PLC 和相机兼容性。",
                _ => Task.FromResult(_readinessReview.BuildStationCompatibility(loadPlan.Payload, packageReadiness.Payload)),
                cancellationToken,
                AgentRunEventTypes.StationCompatibilityCompleted);

            var operatorContract = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "operator_contract",
                "operator_contract_checker",
                "检查算子目录契约和已修复的算子选择。",
                _ => Task.FromResult(_readinessReview.BuildOperatorContractReport(pipeline.Payload, validation.Payload)),
                cancellationToken,
                AgentRunEventTypes.OperatorContractCompleted);

            var releaseReview = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "release_review",
                "release_review_gate",
                "区分画布应用就绪、运行草稿就绪和部署就绪。",
                _ => Task.FromResult(_readinessReview.BuildReleaseReview(validation.Payload, dryRun.Payload, packageReadiness.Payload, parameterMapping.Payload)),
                cancellationToken,
                AgentRunEventTypes.ReleaseReviewCompleted);

            var workflowDiff = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "workflow_diff",
                "workflow_diff_builder",
                "汇总新增、修改、保留、移除、待确认、已修复和部署阻断的流程项。",
                _ => Task.FromResult(_workflowDiffService.Build(loadPlan.Payload, currentDraft, parameterMapping.Payload, validation.Payload, packageReadiness.Payload, autoRepairs)),
                cancellationToken);

            var applyGate = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "apply_gate",
                "apply_gate_resolver",
                "解析画布可应用、运行草稿就绪、部署就绪或阻断状态。",
                _ => Task.FromResult(_applyGateResolver.Build(validation.Payload, dryRun.Payload, packageReadiness.Payload, workflowDiff.Payload)),
                cancellationToken);

            return _resultAssembler.Assemble(new BuildResultAssemblyInput(
                runId,
                buildId,
                request,
                loadPlan.Payload,
                intent.Payload,
                planSelection.Payload,
                template.Payload,
                pipeline.Payload,
                parameterMapping.Payload,
                currentDraft,
                validation.Payload,
                dryRun.Payload,
                packageReadiness.Payload,
                stationCompatibility.Payload,
                operatorContract.Payload,
                releaseReview.Payload,
                workflowDiff.Payload,
                applyGate.Payload,
                evidence,
                autoRepairs,
                publicWarnings));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                _logger,
                "Vision Agent Build Orchestrator failed. Error={Error}",
                ex.Message);
            return _resultAssembler.Failure(buildId, evidence, publicWarnings);
        }
    }

    private static AiFlowGenerationResult? EnforceMaturityGate(
        AiFlowGenerationRequest request,
        BuildPlanLoad load)
    {
        var plan = load.Plan;
        var maturityRequest = BuildMaturityRequest(request, request.BuildFromPlan, load);
        if (plan != null)
        {
            var readiness = VisionAgentPlanBuildReadiness.Evaluate(
                plan,
                load.BuildDecisions,
                load.AcceptedDefaults,
                load.AcceptedRecommendedDefaults,
                load.ValidatedPlanAnswers,
                load.EffectiveRequirement,
                load.RequirementMode);
            if (readiness.CanBuild)
            {
                return null;
            }

            var planMaturity = load.EffectiveRequirement.Maturity;
            return BuildMaturityBlockedResult(
                request,
                maturityRequest,
                planMaturity,
                readiness.BlockingReasons);
        }

        if (plan?.CanBuild == true &&
            plan.RequirementMaturity == null &&
            request.BuildFromPlan?.RequirementMaturity == null)
        {
            return null;
        }

        var maturity = load.EffectiveRequirement.Maturity;
        var blocked = plan?.CanBuild == false ||
                      request.BuildFromPlan?.RequirementMaturity?.CanBuild == false ||
                      !maturity.CanBuild ||
                      maturity.Maturity is AiRequirementMaturity.AbstractGoal or AiRequirementMaturity.Ambiguous or AiRequirementMaturity.ChatOrHelp;
        if (!blocked)
        {
            return null;
        }

        return BuildMaturityBlockedResult(
            request,
            maturityRequest,
            maturity,
            maturity.BlockingReasons.Count > 0 ? maturity.BlockingReasons : null);
    }

    private static VisionAgentRequirementMaturityRequest BuildMaturityRequest(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        BuildPlanLoad? load = null)
    {
        return new VisionAgentRequirementMaturityRequest
        {
            Description = build?.OriginalUserPrompt ?? request.Description,
            AdditionalContext = request.AdditionalContext,
            Mode = build?.BuildIntent ?? request.Mode.ToWireValue(),
            HasCurrentFlow = !string.IsNullOrWhiteSpace(request.ExistingFlowJson) ||
                             !string.IsNullOrWhiteSpace(build?.CurrentFlowSnapshot),
            TemplateSelection = build?.TemplateSelection ?? request.TemplateSelection,
            RequirementMode = load?.RequirementMode ?? request.RequirementMode
        };
    }

    private static AiFlowGenerationResult BuildMaturityBlockedResult(
        AiFlowGenerationRequest request,
        VisionAgentRequirementMaturityRequest maturityRequest,
        AiRequirementMaturityResult maturity,
        IReadOnlyList<string>? overrideBlockingReasons)
    {
        var trace = VisionAgentRequirementMaturityGate.BuildDecisionTrace(
            maturityRequest,
            maturity,
            "build_blocked",
            "clarifying",
            "maturity_gate_blocked");
        var fields = (maturity.MissingFields.Count > 0
                ? maturity.MissingFields
                : overrideBlockingReasons?.Count > 0
                    ? overrideBlockingReasons.Select(NormalizeBlockingField).ToList()
                    : ["inspection_object", "task_type", "image_source", "acceptance_criteria"])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blockingReasons = overrideBlockingReasons?.Count > 0
            ? overrideBlockingReasons.ToList()
            : maturity.BlockingReasons;
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
            ClarificationRequired = true,
            ErrorMessage = maturity.PublicReason,
            AiExplanation = maturity.PublicReason,
            FailureSummary = new AiFailureSummary
            {
                Category = "requirement_maturity",
                Code = "maturity_gate_blocked",
                Message = maturity.PublicReason,
                RepairTarget = "请补充检测对象、任务类型、图像来源、判定标准和输出目标后再构建。"
            },
            RequirementBrief = new AiRequirementBrief
            {
                IntentType = maturity.Maturity,
                RequirementMode = request.RequirementMode,
                Confidence = 0.25,
                HasOpenQuestions = true,
                ClarificationRequired = true,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                MissingFacts = fields.ToList(),
                RequiredFields = fields.ToList(),
                BlockingClarificationFields = fields.ToList(),
                NonBlockingMissingFields = maturity.MissingFields.Except(fields, StringComparer.OrdinalIgnoreCase).ToList(),
                ClarificationQuestions = fields.Select(field => new AiClarificationQuestion
                {
                    Field = field,
                    Question = field switch
                    {
                        "inspection_object" => "请说明要检测的产品或部件对象。",
                        "task_type" => "请说明任务类型：缺陷、测量、线序、OCR/读码、有无/漏装或分类。",
                        "image_source" => "请说明图像来源是相机、图片文件还是先只做元数据规划。",
                        "acceptance_criteria" => "请说明 OK/NG 判定标准或输出目标。",
                        _ => "请补充该字段后再构建。"
                    },
                    Required = true,
                    Reason = "Build 前硬门禁要求需求成熟度达到可构建。",
                    Priority = "high",
                    Options = []
                }).ToList()
            },
            BlockingClarificationFields = fields.ToList(),
            NonBlockingMissingFields = maturity.MissingFields.Except(fields, StringComparer.OrdinalIgnoreCase).ToList(),
            RequirementMaturity = maturity,
            DecisionTrace = trace with { BlockingReasons = blockingReasons.ToList() },
            TurnIntent = AiTurnIntents.NewFlow,
            InteractionState = AiInteractionStates.Clarifying,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private static string NormalizeBlockingField(string reason)
    {
        var value = (reason ?? string.Empty).Trim();
        var tail = value
            .Replace("hard_requirement:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("strategy_confirmation:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("resource_pending:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_missing", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(':');
        var tailField = VisionAgentPlanFieldPolicy.ResolveQuestionField(new VisionAgentClarificationQuestion
        {
            Id = tail,
            Field = tail
        });
        if (!string.IsNullOrWhiteSpace(tailField))
        {
            return tailField;
        }

        if (value.Contains("strategy_confirmation", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("model_or_rule_strategy", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("classification_strategy", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("algorithm_strategy", StringComparison.OrdinalIgnoreCase))
        {
            return VisionAgentPlanAnswerFields.AlgorithmStrategy;
        }

        foreach (var field in VisionAgentPlanFieldPolicy.CanonicalFields)
        {
            if (value.Contains(field, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return tail;
    }

    private static bool ShouldAttemptRepair(
        VisionAgentToolResult validation,
        out IReadOnlyList<string> issueCodes)
    {
        issueCodes = VisionAgentBuildSupport.ReadIssueCodes(validation.Data, "blockingIssues");
        if (!BuildToolRunner.ToolHasBlockingIssues(validation))
        {
            return false;
        }

        var repairable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unknown_operator",
            "unknown_parameter",
            "missing_required_parameter",
            "missing_required_input",
            "missing_port",
            "invalid_connection",
            "incompatible_port_type",
            "missing_model_resource",
            "missing_template_resource",
            "missing_calibration_parameter"
        };
        return issueCodes.Any(repairable.Contains);
    }
}
