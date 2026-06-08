using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
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
    private readonly IAgentRunEventSink? _eventSink;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator> _logger;
    private readonly BuildPlanContextLoader _contextLoader;
    private readonly BuildIntentResolver _intentResolver;
    private readonly TemplateStrategyResolver _templateStrategyResolver;
    private readonly OperatorPipelineSelector _pipelineSelector;
    private readonly ParameterMappingService _parameterMapper;
    private readonly WorkflowDraftBuilder _workflowDraftBuilder;
    private readonly BuildToolRunner _toolRunner;
    private readonly BuildReadinessReviewService _readinessReview;
    private readonly WorkflowDiffService _workflowDiffService;
    private readonly ApplyGateResolver _applyGateResolver;
    private readonly BuildResultAssembler _resultAssembler;

    public VisionAgentBuildOrchestrator(
        IVisionAgentToolRegistry toolRegistry,
        IAiFlowGenerationService generationService,
        AgentRunEventRedactor redactor,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator> logger,
        IAgentRunEventSink? eventSink = null)
    {
        _eventSink = eventSink;
        _logger = logger;
        _toolRunner = new BuildToolRunner(toolRegistry, redactor, eventSink);
        _contextLoader = new BuildPlanContextLoader(eventSink);
        _intentResolver = new BuildIntentResolver();
        _templateStrategyResolver = new TemplateStrategyResolver(_toolRunner);
        _pipelineSelector = new OperatorPipelineSelector();
        _parameterMapper = new ParameterMappingService();
        _workflowDraftBuilder = new WorkflowDraftBuilder(generationService);
        _readinessReview = new BuildReadinessReviewService();
        _workflowDiffService = new WorkflowDiffService();
        _applyGateResolver = new ApplyGateResolver();
        _resultAssembler = new BuildResultAssembler(redactor, eventSink);
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
                "Load PlanSnapshot, planHash, selections, defaults, current flow, template, attachment, and Station boundary metadata.",
                _ => Task.FromResult(_contextLoader.Load(request, build, plan, publicWarnings)),
                cancellationToken);
            _eventSink?.StageCompleted(
                runId,
                "assumption_confirmation",
                "Assumptions confirmed",
                "Build Mode received structured selections and accepted defaults.",
                new
                {
                    acceptedRecommendedDefaults = loadPlan.Payload.AcceptedRecommendedDefaults,
                    defaultCount = loadPlan.Payload.AcceptedDefaults.Count,
                    plcOutputPolicy = loadPlan.Payload.PlcOutputPolicy,
                    metadataOnly = true,
                    redactionPass = true
                });

            var intent = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "resolve_build_intent",
                "build_intent_resolver",
                "Resolve whether Build is new, modify, explain, complete parameters, or refactor.",
                _ => Task.FromResult(_intentResolver.Resolve(request, build, loadPlan.Payload)),
                cancellationToken);
            _eventSink?.StageCompleted(
                runId,
                "requirement_parsing",
                "Requirement parsing complete",
                "Structured plan, selections, and metadata-only context are ready for Build tools.",
                new
                {
                    buildIntent = intent.Payload.BuildIntent,
                    hasExistingFlow = loadPlan.Payload.HasCurrentFlow,
                    attachmentCount = loadPlan.Payload.AttachmentSummary.Count,
                    templateSelectionMode = loadPlan.Payload.TemplateSelection?.Mode ?? string.Empty,
                    metadataOnly = true,
                    redactionPass = true
                });

            var toolContext = VisionAgentBuildSupport.BuildToolContext(
                request,
                build,
                loadPlan.Payload.CurrentFlowSnapshot);
            var template = await _templateStrategyResolver.ResolveAsync(
                runId,
                evidence,
                loadPlan.Payload,
                toolContext,
                cancellationToken);

            var pipeline = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "operator_pipeline",
                "operator_pipeline_selector",
                "Select and repair the operator pipeline from Plan route, template strategy, and operator catalog.",
                _ => Task.FromResult(_pipelineSelector.Select(loadPlan.Payload, template.Payload, publicWarnings)),
                cancellationToken);

            var parameterMapping = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "parameter_mapping",
                "parameter_mapper",
                "Map user selections and accepted defaults into operator parameters while keeping unknown resources pending.",
                _ => Task.FromResult(_parameterMapper.Map(loadPlan.Payload, pipeline.Payload)),
                cancellationToken);

            var draft = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "workflow_draft",
                "workflow_drafter",
                "Generate or modify an editable workflow draft under Plan constraints.",
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
                "Validate workflow schema, connections, known ports, operator catalog, and missing resource placeholders.",
                new
                {
                    flow = draft.Payload.WorkflowDraft,
                    entryOperatorTempId = draft.Payload.EntryOperatorTempId
                },
                cancellationToken,
                AgentRunEventTypes.ReadinessChecked);

            var currentDraft = draft.Payload;
            if (VisionAgentBuildSupport.ToolHasBlockingIssues(validation.Payload))
            {
                var repair = await _toolRunner.ExecuteEvidenceStepAsync(
                    runId,
                    evidence,
                    "repair_loop",
                    "auto_repair_once",
                    "Attempt one automatic repair for validation, dry-run, or readiness issues that are safe to fix.",
                    _ => Task.FromResult(_workflowDraftBuilder.Repair(currentDraft, pipeline.Payload, parameterMapping.Payload)),
                    cancellationToken);
                currentDraft = repair.Payload.Draft;
                autoRepairs.Add(repair.Payload.Record);

                validation = await _toolRunner.RunRegisteredToolAsync(
                    runId,
                    evidence,
                    toolContext,
                    "validate_schema",
                    "validate_flow",
                    "Re-run schema validation after automatic repair.",
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
                "Run metadata-only dry-run for topology, IO, parameter completeness, and runtime boundaries.",
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
                "Check runtime package readiness without creating packages, loading files, or touching Station resources.",
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
                "Check Station/PLC/Camera compatibility as metadata-only boundaries.",
                _ => Task.FromResult(_readinessReview.BuildStationCompatibility(loadPlan.Payload, packageReadiness.Payload)),
                cancellationToken,
                AgentRunEventTypes.StationCompatibilityCompleted);

            var operatorContract = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "operator_contract",
                "operator_contract_checker",
                "Check operator catalog contracts and repaired operator choices.",
                _ => Task.FromResult(_readinessReview.BuildOperatorContractReport(pipeline.Payload, validation.Payload)),
                cancellationToken,
                AgentRunEventTypes.OperatorContractCompleted);

            var releaseReview = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "release_review",
                "release_review_gate",
                "Separate canvas Apply readiness from runtime draft and deployment readiness.",
                _ => Task.FromResult(_readinessReview.BuildReleaseReview(validation.Payload, dryRun.Payload, packageReadiness.Payload, parameterMapping.Payload)),
                cancellationToken,
                AgentRunEventTypes.ReleaseReviewCompleted);

            var workflowDiff = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "workflow_diff",
                "workflow_diff_builder",
                "Summarize added, modified, preserved, removed, pending, repaired, and deployment-blocked workflow items.",
                _ => Task.FromResult(_workflowDiffService.Build(loadPlan.Payload, currentDraft, parameterMapping.Payload, validation.Payload, packageReadiness.Payload, autoRepairs)),
                cancellationToken);

            var applyGate = await _toolRunner.ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "apply_gate",
                "apply_gate_resolver",
                "Resolve Canvas Apply Ready, Runtime Draft Ready, Deployment Ready, or Blocked.",
                _ => Task.FromResult(_applyGateResolver.Build(validation.Payload, dryRun.Payload, packageReadiness.Payload, workflowDiff.Payload)),
                cancellationToken);

            return _resultAssembler.Assemble(new BuildResultAssemblyInput(
                runId,
                buildId,
                request,
                loadPlan.Payload,
                intent.Payload,
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
}
