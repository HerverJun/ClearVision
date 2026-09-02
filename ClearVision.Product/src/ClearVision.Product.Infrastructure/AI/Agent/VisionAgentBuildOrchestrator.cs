using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
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
    private readonly VisionTaskRouteContractRegistry _routeContractRegistry;

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
        IAgentRunEventSink? eventSink = null,
        VisionTaskRouteContractRegistry? routeContractRegistry = null)
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
        _routeContractRegistry = routeContractRegistry ?? new VisionTaskRouteContractRegistry();
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

            if (loadPlan.Payload.HashMismatch)
            {
                _eventSink?.StageCompleted(
                    runId,
                    "plan_hash_validation",
                    "计划哈希不一致",
                    "BuildFromPlan 已拒绝继续，未对不可信 Plan 生成任何工作流产物。",
                    new
                    {
                        warningCode = "plan_hash_mismatch",
                        planId = loadPlan.Payload.PlanId,
                        providedPlanHash = request.BuildFromPlan?.PlanHash ?? string.Empty,
                        computedPlanHash = loadPlan.Payload.ComputedPlanHash,
                        failClosed = true,
                        metadataOnly = true
                    });
                return _resultAssembler.Failure(
                    buildId,
                    evidence,
                    publicWarnings,
                    failureCode: "plan_hash_mismatch");
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
                    entryOperatorTempId = draft.Payload.EntryOperatorTempId,
                    artifactFingerprint = draft.Payload.Artifact.ArtifactFingerprint,
                    planHash = loadPlan.Payload.PlanHash,
                    catalogVersion = draft.Payload.Artifact.CatalogVersion,
                    buildIntent = intent.Payload.BuildIntent
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
                        loadPlan.Payload,
                        intent.Payload,
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
                        entryOperatorTempId = currentDraft.EntryOperatorTempId,
                        artifactFingerprint = currentDraft.Artifact.ArtifactFingerprint,
                        planHash = loadPlan.Payload.PlanHash,
                        catalogVersion = currentDraft.Artifact.CatalogVersion,
                        buildIntent = intent.Payload.BuildIntent
                    },
                    cancellationToken,
                 AgentRunEventTypes.ReadinessChecked);
            }

            var routeAssessment = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "route_semantics",
                "vision_task_route_contract",
                "按正式任务路由合同检查图像源、任务处理节点、必填数据端口和结果路径。",
                _ =>
                {
                    var assessment = _routeContractRegistry.Assess(
                        ResolveTaskType(loadPlan.Payload),
                        currentDraft.Artifact.Graph,
                        loadPlan.Payload.Plan?.PlanFidelity.RequiredOutputSemantics);
                    return Task.FromResult(VisionAgentBuildSupport.StepResult(
                        assessment,
                        assessment.Satisfied
                            ? "任务路由语义合同已满足。"
                            : $"任务路由语义合同已阻断：{string.Join(",", assessment.BlockingReasons)}。",
                        assessment.Satisfied
                            ? AgentRunEventStatuses.Completed
                            : AgentRunEventStatuses.Blocked,
                        assessment,
                        warningCode: assessment.Satisfied ? string.Empty : assessment.BlockingReasons.FirstOrDefault() ?? "route_semantics_blocked",
                        applyImpact: assessment.Satisfied ? "editable_draft_allowed" : "blocked",
                        deploymentImpact: assessment.Satisfied ? "requires_readiness_checks" : "blocked"));
                },
                cancellationToken);
            WorkflowDraftBuilder.StampRouteAssessment(
                currentDraft.Artifact.CanvasProjection,
                routeAssessment.Payload);

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
                    entryOperatorTempId = currentDraft.EntryOperatorTempId,
                    artifactFingerprint = currentDraft.Artifact.ArtifactFingerprint,
                    planHash = loadPlan.Payload.PlanHash,
                    catalogVersion = currentDraft.Artifact.CatalogVersion,
                    buildIntent = intent.Payload.BuildIntent
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
                    artifactFingerprint = currentDraft.Artifact.ArtifactFingerprint,
                    planHash = loadPlan.Payload.PlanHash,
                    catalogVersion = currentDraft.Artifact.CatalogVersion,
                    buildIntent = intent.Payload.BuildIntent,
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
                _ => Task.FromResult(_applyGateResolver.Build(
                    validation.Payload,
                    dryRun.Payload,
                    packageReadiness.Payload,
                    workflowDiff.Payload,
                    currentDraft.Artifact.ArtifactFingerprint,
                    routeAssessment.Payload,
                    currentDraft.Artifact.ReturnedFlowSemanticFingerprint)),
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
                routeAssessment.Payload,
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

    private static string ResolveTaskType(BuildPlanLoad load)
    {
        return load.TaskType;
    }
}
