using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public interface IVisionAgentBuildOrchestrator
{
    Task<AiFlowGenerationResult> BuildAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken);
}

public sealed class VisionAgentBuildOrchestrator : IVisionAgentBuildOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly HashSet<string> ForbiddenOperatorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModbusCommunication",
        "HttpRequest",
        "ScriptOperator"
    };

    private readonly IVisionAgentToolRegistry _toolRegistry;
    private readonly IAiFlowGenerationService _generationService;
    private readonly IAgentRunEventSink? _eventSink;
    private readonly AgentRunEventRedactor _redactor;
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator> _logger;

    public VisionAgentBuildOrchestrator(
        IVisionAgentToolRegistry toolRegistry,
        IAiFlowGenerationService generationService,
        AgentRunEventRedactor redactor,
        Microsoft.Extensions.Logging.ILogger<VisionAgentBuildOrchestrator> logger,
        IAgentRunEventSink? eventSink = null)
    {
        _toolRegistry = toolRegistry;
        _generationService = generationService;
        _redactor = redactor;
        _logger = logger;
        _eventSink = eventSink;
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
            var loadPlan = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "plan_generation",
                "plan_snapshot_loader",
                "Load PlanSnapshot, planHash, selections, defaults, current flow, template, attachment, and Station boundary metadata.",
                _ => Task.FromResult(LoadPlan(request, build, plan, publicWarnings)),
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

            var intent = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "resolve_build_intent",
                "build_intent_resolver",
                "Resolve whether Build is new, modify, explain, complete parameters, or refactor.",
                _ => Task.FromResult(ResolveBuildIntent(request, build, loadPlan.Payload)),
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

            var toolContext = BuildToolContext(request, build, loadPlan.Payload.CurrentFlowSnapshot);
            var template = await ResolveTemplateStrategyAsync(
                runId,
                evidence,
                request,
                build,
                loadPlan.Payload,
                toolContext,
                cancellationToken);

            var pipeline = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "operator_pipeline",
                "operator_pipeline_selector",
                "Select and repair the operator pipeline from Plan route, template strategy, and operator catalog.",
                _ => Task.FromResult(SelectOperatorPipeline(loadPlan.Payload, template.Payload, publicWarnings)),
                cancellationToken);

            var parameterMapping = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "parameter_mapping",
                "parameter_mapper",
                "Map user selections and accepted defaults into operator parameters while keeping unknown resources pending.",
                _ => Task.FromResult(MapParameters(loadPlan.Payload, pipeline.Payload)),
                cancellationToken);

            var draft = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "workflow_draft",
                "workflow_drafter",
                "Generate or modify an editable workflow draft under Plan constraints.",
                async ct => await DraftWorkflowAsync(
                    request,
                    loadPlan.Payload,
                    intent.Payload,
                    pipeline.Payload,
                    parameterMapping.Payload,
                    ct),
                cancellationToken,
                AgentRunEventTypes.WorkflowDraftUpdated);

            var validation = await RunRegisteredToolAsync(
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
            if (ToolHasBlockingIssues(validation.Payload))
            {
                var repair = await ExecuteEvidenceStepAsync(
                    runId,
                    evidence,
                    "repair_loop",
                    "auto_repair_once",
                    "Attempt one automatic repair for validation, dry-run, or readiness issues that are safe to fix.",
                    _ => Task.FromResult(RepairDraft(currentDraft, pipeline.Payload, parameterMapping.Payload)),
                    cancellationToken);
                currentDraft = repair.Payload.Draft;
                autoRepairs.Add(repair.Payload.Record);

                validation = await RunRegisteredToolAsync(
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

            var dryRun = await RunRegisteredToolAsync(
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

            var packageReadiness = await RunRegisteredToolAsync(
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

            var stationCompatibility = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "station_compatibility",
                "station_compatibility_checker",
                "Check Station/PLC/Camera compatibility as metadata-only boundaries.",
                _ => Task.FromResult(BuildStationCompatibility(loadPlan.Payload, packageReadiness.Payload)),
                cancellationToken,
                AgentRunEventTypes.StationCompatibilityCompleted);

            var operatorContract = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "operator_contract",
                "operator_contract_checker",
                "Check operator catalog contracts and repaired operator choices.",
                _ => Task.FromResult(BuildOperatorContractReport(pipeline.Payload, validation.Payload)),
                cancellationToken,
                AgentRunEventTypes.OperatorContractCompleted);

            var releaseReview = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "release_review",
                "release_review_gate",
                "Separate canvas Apply readiness from runtime draft and deployment readiness.",
                _ => Task.FromResult(BuildReleaseReview(validation.Payload, dryRun.Payload, packageReadiness.Payload, parameterMapping.Payload)),
                cancellationToken,
                AgentRunEventTypes.ReleaseReviewCompleted);

            var workflowDiff = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "workflow_diff",
                "workflow_diff_builder",
                "Summarize added, modified, preserved, removed, pending, repaired, and deployment-blocked workflow items.",
                _ => Task.FromResult(BuildWorkflowDiff(loadPlan.Payload, currentDraft, parameterMapping.Payload, validation.Payload, packageReadiness.Payload, autoRepairs)),
                cancellationToken);

            var applyGate = await ExecuteEvidenceStepAsync(
                runId,
                evidence,
                "apply_gate",
                "apply_gate_resolver",
                "Resolve Canvas Apply Ready, Runtime Draft Ready, Deployment Ready, or Blocked.",
                _ => Task.FromResult(BuildApplyGate(validation.Payload, dryRun.Payload, packageReadiness.Payload, workflowDiff.Payload)),
                cancellationToken);

            var pendingParameters = MergePendingParameters(parameterMapping.Payload.PendingParameters, request);
            var missingResources = MergeMissingResources(parameterMapping.Payload.MissingResources, validation.Payload, packageReadiness.Payload, request);
            var firstFix = FirstFixRecommendation(applyGate.Payload, missingResources, pendingParameters);
            var result = currentDraft.GenerationResult;
            result.Success = result.Success || currentDraft.CanvasFlow.Operators.Count > 0;
            result.CompletionStatus = result.Success
                ? AiFlowGenerationResult.CompletionStatusCompleted
                : AiFlowGenerationResult.CompletionStatusFailed;
            result.Flow ??= currentDraft.CanvasFlow;
            if (FlowOperatorCount(result.Flow) == 0)
            {
                result.Flow = currentDraft.CanvasFlow;
            }

            result.ValidationPreview = validation.Payload.Data ?? result.ValidationPreview;
            result.DryRunResult = dryRun.Payload.Data ?? result.DryRunResult;
            result.PendingParameters = pendingParameters;
            result.MissingResources = missingResources;
            result.GenerationMode = template.Payload.GenerationMode;
            result.TemplateLockLevel = template.Payload.TemplateLockLevel;
            result.DetectedIntent = intent.Payload.BuildIntent;
            result.TurnIntent = ToTurnIntent(intent.Payload.BuildIntent);
            result.InteractionState = AiInteractionStates.Completed;
            result.ToolTrace.AddRange(evidence.Select(item => (object)item));
            result.StageTimeline.AddRange(evidence.Select(item => new AiGenerationStageDiagnostic
            {
                Stage = item.Stage,
                Status = item.Status,
                Summary = item.OutputSummary,
                DurationMs = item.DurationMs,
                Metadata = new Dictionary<string, string>
                {
                    ["toolName"] = item.ToolName,
                    ["evidenceId"] = item.EvidenceId,
                    ["warningCode"] = item.WarningCode,
                    ["applyImpact"] = item.ApplyImpact,
                    ["deploymentImpact"] = item.DeploymentImpact
                }
            }));
            result.BuildResult = new VisionAgentBuildResult
            {
                BuildId = buildId,
                PlanId = loadPlan.Payload.PlanId,
                PlanHash = loadPlan.Payload.PlanHash,
                BuildIntent = intent.Payload.BuildIntent,
                WorkflowDraft = currentDraft.WorkflowDraft,
                OperatorPipeline = pipeline.Payload.Steps,
                ParameterMapping = parameterMapping.Payload.Mappings,
                PendingParameters = pendingParameters,
                MissingResources = missingResources,
                ValidationPreview = validation.Payload.Data,
                DryRunResult = dryRun.Payload.Data,
                ReadinessReport = packageReadiness.Payload.Data,
                StationCompatibilityReport = stationCompatibility.Payload.Report,
                OperatorContractReport = operatorContract.Payload.Report,
                ReleaseReview = releaseReview.Payload.Report,
                WorkflowDiff = workflowDiff.Payload,
                ApplyGate = applyGate.Payload with
                {
                    FirstFixRecommendation = firstFix
                },
                ToolEvidenceTimeline = evidence,
                AutoRepairs = autoRepairs,
                FirstFixRecommendation = firstFix,
                PublicWarnings = publicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                MetadataOnly = true
            };
            result.AiExplanation = string.IsNullOrWhiteSpace(result.AiExplanation)
                ? "Build Mode executed a metadata-only tool loop under the confirmed Plan and produced an editable workflow draft."
                : _redactor.RedactText(result.AiExplanation);

            _eventSink?.Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ArtifactCreated,
                Stage = "artifact",
                Title = "Build artifact ready",
                Summary = "Replay-safe BuildResult, workflow diff, readiness gates, and editable draft are ready.",
                Status = AgentRunEventStatuses.Completed,
                Payload = new
                {
                    buildId,
                    workflowDiff = result.BuildResult.WorkflowDiff,
                    applyGate = result.BuildResult.ApplyGate,
                    firstFixRecommendation = firstFix,
                    toolEvidenceCount = evidence.Count,
                    metadataOnly = true,
                    redactionPass = true
                }
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Vision Agent Build Orchestrator failed. Error={Error}",
                ex.Message);
            return new AiFlowGenerationResult
            {
                Success = false,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                FailureType = AiFlowGenerationResult.FailureTypeSystemError,
                ErrorMessage = "Vision Agent Build Mode failed while executing the metadata-only tool loop.",
                BuildResult = new VisionAgentBuildResult
                {
                    BuildId = buildId,
                    ToolEvidenceTimeline = evidence,
                    FirstFixRecommendation = "Review public tool evidence and retry Build after fixing the blocked metadata step.",
                    PublicWarnings = publicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ApplyGate = new VisionAgentApplyGate
                    {
                        Blocked = true,
                        Status = "blocked",
                        ApplyBlockers = ["build_orchestrator_failed"],
                        FirstFixRecommendation = "Review public tool evidence and retry Build after fixing the blocked metadata step."
                    }
                }
            };
        }
    }

    private async Task<BuildStepResult<T>> ExecuteEvidenceStepAsync<T>(
        string? runId,
        List<VisionAgentToolEvidence> evidence,
        string stage,
        string toolName,
        string inputSummary,
        Func<CancellationToken, Task<BuildStepResult<T>>> action,
        CancellationToken cancellationToken,
        string? completionEventType = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidenceId = $"ev_{Guid.NewGuid():N}";
        _eventSink?.StageStarted(runId, stage, StageTitle(stage), inputSummary, new
        {
            evidenceId,
            toolName,
            inputSummary = _redactor.RedactText(inputSummary),
            metadataOnly = true,
            redactionPass = true
        });
        _eventSink?.ToolStarted(runId, stage, toolName, new
        {
            evidenceId,
            toolName,
            inputSummary = _redactor.RedactText(inputSummary),
            metadataOnly = true,
            redactionPass = true
        });

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action(cancellationToken);
            stopwatch.Stop();
            var status = NormalizeStatus(result.Status);
            var item = new VisionAgentToolEvidence
            {
                Stage = stage,
                ToolName = toolName,
                InputSummary = _redactor.RedactText(inputSummary),
                OutputSummary = _redactor.RedactText(result.OutputSummary),
                Status = status,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EvidenceId = evidenceId,
                RepairAction = _redactor.RedactText(result.RepairAction),
                WarningCode = _redactor.RedactText(result.WarningCode),
                ApplyImpact = _redactor.RedactText(result.ApplyImpact),
                DeploymentImpact = _redactor.RedactText(result.DeploymentImpact),
                MetadataOnly = true,
                RedactionPass = true
            };
            evidence.Add(item);
            var payload = EvidencePayload(item, result.PayloadDetails);
            if (status == AgentRunEventStatuses.Failed)
            {
                _eventSink?.ToolFailed(runId, stage, toolName, item.DurationMs, item.OutputSummary, payload);
            }
            else
            {
                _eventSink?.ToolCompleted(runId, stage, toolName, item.DurationMs, payload);
            }

            if (!string.IsNullOrWhiteSpace(completionEventType))
            {
                _eventSink?.Append(runId, new AgentRunEventDraft
                {
                    EventType = completionEventType,
                    Stage = stage,
                    Title = StageTitle(stage),
                    Summary = item.OutputSummary,
                    Status = status,
                    Payload = payload
                });
            }

            _eventSink?.StageCompleted(runId, stage, StageTitle(stage), item.OutputSummary, payload);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var item = new VisionAgentToolEvidence
            {
                Stage = stage,
                ToolName = toolName,
                InputSummary = _redactor.RedactText(inputSummary),
                OutputSummary = _redactor.RedactText(ex.Message),
                Status = AgentRunEventStatuses.Failed,
                DurationMs = stopwatch.ElapsedMilliseconds,
                EvidenceId = evidenceId,
                WarningCode = "tool_exception",
                ApplyImpact = "blocked",
                DeploymentImpact = "blocked",
                MetadataOnly = true,
                RedactionPass = true
            };
            evidence.Add(item);
            _eventSink?.ToolFailed(runId, stage, toolName, item.DurationMs, item.OutputSummary, EvidencePayload(item, null));
            throw;
        }
    }

    private async Task<BuildStepResult<VisionAgentToolResult>> RunRegisteredToolAsync(
        string? runId,
        List<VisionAgentToolEvidence> evidence,
        VisionAgentToolContext context,
        string stage,
        string toolName,
        string inputSummary,
        object arguments,
        CancellationToken cancellationToken,
        string completionEventType)
    {
        return await ExecuteEvidenceStepAsync(
            runId,
            evidence,
            stage,
            toolName,
            inputSummary,
            async ct =>
            {
                var result = await _toolRegistry.ExecuteAsync(
                    toolName,
                    context,
                    ToJsonElement(arguments),
                    ct);
                var data = ToJsonElementOrNull(result.Data);
                var hasBlocking = ToolHasBlockingIssues(new VisionAgentToolResult
                {
                    Success = result.Success,
                    Data = result.Data,
                    ErrorCode = result.ErrorCode,
                    ErrorMessage = result.ErrorMessage,
                    PendingActions = result.PendingActions
                });
                return StepResult(
                    result,
                    result.Success
                        ? ToolSummary(toolName, data, hasBlocking)
                        : $"{toolName} failed: {result.ErrorCode}",
                    result.Success && !hasBlocking ? AgentRunEventStatuses.Completed :
                    result.Success ? AgentRunEventStatuses.Blocked : AgentRunEventStatuses.Failed,
                    new
                    {
                        toolName,
                        success = result.Success,
                        errorCode = result.ErrorCode,
                        data = result.Data,
                        pendingActionCount = result.PendingActions.Count,
                        blocking = hasBlocking,
                        metadataOnly = true
                    },
                    warningCode: hasBlocking ? $"{toolName}_blocked" : string.Empty,
                    applyImpact: hasBlocking && toolName == "validate_flow" ? "blocked" : "editable_draft_allowed",
                    deploymentImpact: hasBlocking ? "deployment_blocked" : "no_deployment_blocker");
            },
            cancellationToken,
            completionEventType);
    }

    private BuildStepResult<BuildPlanLoad> LoadPlan(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        VisionAgentPlanModeResult? plan,
        List<string> publicWarnings)
    {
        var computed = VisionAgentOrchestrator.ComputePlanHash(plan);
        var provided = Clean(build?.PlanHash);
        var hashMismatch = plan != null &&
                           !string.IsNullOrWhiteSpace(provided) &&
                           !string.IsNullOrWhiteSpace(computed) &&
                           !string.Equals(provided, computed, StringComparison.OrdinalIgnoreCase);
        if (hashMismatch)
        {
            publicWarnings.Add("plan_hash_mismatch");
            _eventSink?.StageCompleted(
                request.AgentRunId,
                "plan_hash_validation",
                "Plan hash mismatch detected",
                "Build is continuing with the public plan snapshot; review plan provenance before applying.",
                new
                {
                    warningCode = "plan_hash_mismatch",
                    planId = build?.PlanId ?? plan?.PlanId ?? string.Empty,
                    providedPlanHash = provided,
                    computedPlanHash = computed,
                    publicDiagnosticsOnly = true,
                    metadataOnly = true,
                    redactionPass = true
                });
        }

        var currentFlowSnapshot = FirstNonEmpty(build?.CurrentFlowSnapshot, request.ExistingFlowJson);
        var templateSelection = build?.TemplateSelection ?? request.TemplateSelection ?? plan?.TemplateSelection;
        var payload = new BuildPlanLoad
        {
            PlanId = Clean(build?.PlanId) is { Length: > 0 } planId ? planId : Clean(plan?.PlanId),
            PlanHash = string.IsNullOrWhiteSpace(provided) ? Clean(plan?.PlanHash) : provided,
            ComputedPlanHash = computed,
            Plan = plan,
            UserSelections = build?.UserSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AcceptedDefaults = build?.AcceptedDefaults ?? [],
            AcceptedRecommendedDefaults = build?.AcceptedRecommendedDefaults ?? false,
            CurrentFlowSnapshot = currentFlowSnapshot,
            TemplateSelection = templateSelection,
            AttachmentSummary = build?.AttachmentSummary ?? new VisionAgentAttachmentSummary(),
            OperatorCatalogVersion = FirstNonEmpty(build?.OperatorCatalogVersion, plan?.OperatorCatalogVersion),
            StationBoundarySummary = FirstNonEmpty(build?.StationBoundarySummary, plan?.StationBoundarySummary),
            PlcOutputPolicy = FirstNonEmpty(build?.PlcOutputPolicy, plan?.PlcOutputPolicy),
            OriginalUserPrompt = FirstNonEmpty(build?.OriginalUserPrompt, plan?.OriginalUserPrompt, request.Description),
            BuildIntentHint = build?.BuildIntent ?? request.Mode.ToWireValue(),
            HashMismatch = hashMismatch,
            HasCurrentFlow = !string.IsNullOrWhiteSpace(currentFlowSnapshot)
        };

        return StepResult(
            payload,
            hashMismatch
                ? "Plan loaded with plan_hash_mismatch warning."
                : "Plan snapshot and structured BuildFromPlan context loaded.",
            AgentRunEventStatuses.Completed,
            new
            {
                planId = payload.PlanId,
                planHash = payload.PlanHash,
                hashMismatch,
                userSelectionCount = payload.UserSelections.Count,
                acceptedDefaultCount = payload.AcceptedDefaults.Count,
                hasCurrentFlow = payload.HasCurrentFlow,
                templateSelectionMode = payload.TemplateSelection?.Mode ?? string.Empty,
                templateId = payload.TemplateSelection?.TemplateId ?? string.Empty,
                metadataOnly = true
            },
            warningCode: hashMismatch ? "plan_hash_mismatch" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: hashMismatch ? "requires_plan_provenance_review" : "no_deployment_blocker");
    }

    private static BuildStepResult<BuildIntentResolution> ResolveBuildIntent(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        BuildPlanLoad load)
    {
        var candidate = Clean(build?.BuildIntent).ToLowerInvariant();
        if (candidate is "complete_parameters" or "complete-parameters")
        {
            candidate = "review_pending_parameters";
        }

        if (candidate is not ("new" or "modify" or "explain" or "review_pending_parameters" or "refactor"))
        {
            candidate = request.Mode switch
            {
                GenerateFlowMode.Modify => "modify",
                GenerateFlowMode.Explain => "explain",
                GenerateFlowMode.ReviewPendingParameters => "review_pending_parameters",
                GenerateFlowMode.New => "new",
                _ when load.HasCurrentFlow => "modify",
                _ => "new"
            };
        }

        if (candidate == "new" && load.HasCurrentFlow &&
            request.Mode is GenerateFlowMode.Auto or GenerateFlowMode.Modify)
        {
            candidate = "modify";
        }

        return StepResult(
            new BuildIntentResolution(candidate),
            $"Build intent resolved as {candidate}.",
            AgentRunEventStatuses.Completed,
            new
            {
                buildIntent = candidate,
                hasCurrentFlow = load.HasCurrentFlow,
                currentFlowPreserved = load.HasCurrentFlow && candidate != "new",
                metadataOnly = true
            },
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "no_deployment_blocker");
    }

    private async Task<BuildStepResult<TemplateStrategyResolution>> ResolveTemplateStrategyAsync(
        string? runId,
        List<VisionAgentToolEvidence> evidence,
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        BuildPlanLoad load,
        VisionAgentToolContext toolContext,
        CancellationToken cancellationToken)
    {
        var match = await RunRegisteredToolAsync(
            runId,
            evidence,
            toolContext,
            "template_strategy",
            "match_flow_template",
            "Match the request against metadata-only template catalog.",
            new { request = load.OriginalUserPrompt, topN = 3 },
            cancellationToken,
            AgentRunEventTypes.ToolCallCompleted);

        var selectedTemplateId = Clean(load.TemplateSelection?.TemplateId);
        var selectedScenario = Clean(load.TemplateSelection?.ScenarioKey);
        var selectedMode = Clean(load.TemplateSelection?.Mode);
        var candidate = FirstTemplateCandidate(match.Payload.Data);
        var strategy = "catalog_match";
        var templateId = selectedTemplateId;
        var scenarioKey = selectedScenario;
        if (!string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            strategy = selectedMode.Contains("adapt", StringComparison.OrdinalIgnoreCase)
                ? "adapt_selected_template"
                : "use_selected_template";
        }
        else if (candidate != null && candidate.Score >= 0.4)
        {
            templateId = candidate.TemplateId;
            scenarioKey = candidate.ScenarioKey;
        }
        else
        {
            strategy = "no_template";
        }

        VisionAgentToolResult? skeleton = null;
        if (!string.IsNullOrWhiteSpace(templateId) || !string.IsNullOrWhiteSpace(scenarioKey))
        {
            var skeletonStep = await RunRegisteredToolAsync(
                runId,
                evidence,
                toolContext,
                "template_strategy",
                "get_flow_template_skeleton",
                "Load selected or matched template skeleton as read-only metadata.",
                new { templateId, scenarioKey },
                cancellationToken,
                AgentRunEventTypes.ToolCallCompleted);
            skeleton = skeletonStep.Payload;
            if (!skeleton.Success && strategy != "no_template")
            {
                strategy = "catalog_match_without_skeleton";
            }
        }

        var resolution = new TemplateStrategyResolution(
            strategy,
            templateId,
            scenarioKey,
            skeleton?.Success == true ? skeleton.Data : null,
            strategy == "no_template" ? "free_generate" :
            strategy.Contains("adapt", StringComparison.OrdinalIgnoreCase) ? "template_adapt" : "template_fill",
            strategy == "no_template" ? "none" :
            strategy.Contains("adapt", StringComparison.OrdinalIgnoreCase) ? "relaxed" : "strict");

        return StepResult(
            resolution,
            $"Template strategy resolved as {strategy}.",
            AgentRunEventStatuses.Completed,
            new
            {
                strategy,
                templateId,
                scenarioKey,
                candidate = candidate == null
                    ? null
                    : new { candidate.TemplateId, candidate.ScenarioKey, candidate.Score },
                skeletonLoaded = skeleton?.Success == true,
                metadataOnly = true
            },
            warningCode: skeleton?.Success == false ? "template_skeleton_unavailable" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "template_resource_may_remain_pending");
    }

    private static BuildStepResult<OperatorPipelineResolution> SelectOperatorPipeline(
        BuildPlanLoad load,
        TemplateStrategyResolution template,
        List<string> publicWarnings)
    {
        var source = "plan";
        var requested = ReadOperatorTypes(template.TemplateSkeleton).ToList();
        if (requested.Count > 0)
        {
            source = "template";
        }
        else if (load.Plan?.RecommendedRoute.Operators.Count > 0)
        {
            requested = load.Plan.RecommendedRoute.Operators;
        }

        var allowed = VisionAgentReadOnlyCatalog.Schemas.Keys
            .Where(type => !ForbiddenOperatorTypes.Contains(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repaired = new List<VisionAgentOperatorPipelineStep>();
        var invalid = new List<string>();
        foreach (var type in requested.Select(Clean).Where(type => !string.IsNullOrWhiteSpace(type)))
        {
            if (allowed.Contains(type))
            {
                repaired.Add(new VisionAgentOperatorPipelineStep
                {
                    TempId = TempIdFor(type, repaired.Count + 1),
                    OperatorType = type,
                    Source = source,
                    Status = "selected"
                });
            }
            else
            {
                invalid.Add(type);
            }
        }

        if (repaired.Count == 0)
        {
            publicWarnings.Add("operator_pipeline_repaired_to_minimum");
            repaired =
            [
                new() { TempId = "op_cam", OperatorType = "ImageAcquisition", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" },
                new() { TempId = "op_judge", OperatorType = "ResultJudgment", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" },
                new() { TempId = "op_out", OperatorType = "ResultOutput", Source = "repair", Status = "selected", RepairNote = "minimum_pipeline_added" }
            ];
        }

        if (invalid.Count > 0)
        {
            publicWarnings.Add("invalid_operator_removed");
            repaired = repaired.Select(step => step with
            {
                RepairNote = string.IsNullOrWhiteSpace(step.RepairNote)
                    ? "invalid_operator_removed"
                    : step.RepairNote
            }).ToList();
        }

        var resolution = new OperatorPipelineResolution(repaired, invalid);
        return StepResult(
            resolution,
            invalid.Count == 0
                ? $"Selected {repaired.Count} catalog-backed operators."
                : $"Selected {repaired.Count} catalog-backed operators and removed {invalid.Count} invalid operators.",
            AgentRunEventStatuses.Completed,
            new
            {
                operatorTypes = repaired.Select(item => item.OperatorType).ToList(),
                invalidOperators = invalid,
                source,
                metadataOnly = true
            },
            warningCode: invalid.Count > 0 ? "invalid_operator_removed" : string.Empty,
            repairAction: invalid.Count > 0 ? "removed_invalid_operators" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: invalid.Count > 0 ? "operator_contract_repaired" : "no_deployment_blocker");
    }

    private BuildStepResult<ParameterMappingResolution> MapParameters(
        BuildPlanLoad load,
        OperatorPipelineResolution pipeline)
    {
        var mappings = new List<VisionAgentParameterMapping>();
        var pending = new List<AiPendingParameterInfo>();
        var missing = new List<AiMissingResourceInfo>();

        foreach (var op in pipeline.Steps)
        {
            if (!VisionAgentReadOnlyCatalog.Schemas.TryGetValue(op.OperatorType, out var schema))
            {
                continue;
            }

            foreach (var parameter in schema.Parameters)
            {
                var mapped = MapParameterValue(op, parameter, load);
                mappings.Add(mapped);
                if (mapped.Pending)
                {
                    pending.Add(new AiPendingParameterInfo
                    {
                        OperatorId = op.TempId,
                        ActualOperatorId = op.TempId,
                        ParameterNames = [parameter.Name]
                    });
                }

                var missingKind = MissingResourceKind(op.OperatorType, parameter.Name, mapped.Pending);
                if (!string.IsNullOrWhiteSpace(missingKind))
                {
                    missing.Add(new AiMissingResourceInfo
                    {
                        ResourceType = missingKind,
                        ResourceKey = $"{op.TempId}.{parameter.Name}",
                        Description = $"{op.OperatorType}.{parameter.Name} remains pending metadata and was not guessed."
                    });
                }
            }
        }

        var resolution = new ParameterMappingResolution(
            mappings,
            DeduplicatePending(pending),
            DeduplicateMissing(missing));
        return StepResult(
            resolution,
            $"Mapped {mappings.Count} parameter assumptions; {resolution.PendingParameters.Count} pending parameter group(s), {resolution.MissingResources.Count} missing resource(s).",
            AgentRunEventStatuses.Completed,
            new
            {
                mappingCount = mappings.Count,
                pendingParameterCount = resolution.PendingParameters.Count,
                missingResourceCount = resolution.MissingResources.Count,
                selections = load.UserSelections.Keys.ToList(),
                acceptedDefaults = load.AcceptedDefaults,
                metadataOnly = true
            },
            warningCode: resolution.MissingResources.Count > 0 ? "resources_pending" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: resolution.MissingResources.Count > 0 ? "deployment_blocked_until_resources_bound" : "no_deployment_blocker");
    }

    private async Task<BuildStepResult<DraftWorkflowResolution>> DraftWorkflowAsync(
        AiFlowGenerationRequest request,
        BuildPlanLoad load,
        BuildIntentResolution intent,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        CancellationToken cancellationToken)
    {
        var generationRequest = request with
        {
            ExistingFlowJson = load.CurrentFlowSnapshot,
            Mode = ToGenerateFlowMode(intent.BuildIntent),
            TemplateSelection = load.TemplateSelection
        };
        var generation = await _generationService.GenerateFlowAsync(
            generationRequest,
            cancellationToken: cancellationToken);

        var canonical = BuildCanonicalDraft(pipeline, parameters);
        var canvasFlow = FlowOperatorCount(generation.Flow) > 0
            ? generation.Flow as OperatorFlowDto ?? BuildCanvasFlow(load, intent, pipeline, parameters)
            : BuildCanvasFlow(load, intent, pipeline, parameters);
        generation.Flow ??= canvasFlow;

        var resolution = new DraftWorkflowResolution(
            generation,
            canonical.WorkflowDraft,
            canonical.EntryOperatorTempId,
            canvasFlow,
            canonical.AddedNodeIds);
        return StepResult(
            resolution,
            $"Workflow draft produced with {pipeline.Steps.Count} planned operator(s).",
            generation.Success || canvasFlow.Operators.Count > 0
                ? AgentRunEventStatuses.Completed
                : AgentRunEventStatuses.Failed,
            new
            {
                operatorTypes = pipeline.Steps.Select(item => item.OperatorType).ToList(),
                operatorCount = pipeline.Steps.Count,
                canvasOperatorCount = canvasFlow.Operators.Count,
                connectionCount = canonical.ConnectionCount,
                buildIntent = intent.BuildIntent,
                preservedExistingFlow = load.HasCurrentFlow && intent.BuildIntent != "new",
                metadataOnly = true
            },
            applyImpact: canvasFlow.Operators.Count > 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: "requires_readiness_checks");
    }

    private static BuildStepResult<RepairDraftResolution> RepairDraft(
        DraftWorkflowResolution draft,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters)
    {
        var repaired = BuildCanonicalDraft(pipeline, parameters, forceLinearConnections: true);
        var record = new VisionAgentBuildRepairRecord
        {
            Stage = "validate_schema",
            RepairReason = "validation_or_dryrun_blocking_issue",
            DiffSummary = "Rebuilt metadata-only draft connections from the repaired operator pipeline.",
            ResultStatus = "repaired",
            MetadataOnly = true
        };
        var nextDraft = draft with
        {
            WorkflowDraft = repaired.WorkflowDraft,
            EntryOperatorTempId = repaired.EntryOperatorTempId,
            AddedNodeIds = repaired.AddedNodeIds
        };
        return StepResult(
            new RepairDraftResolution(nextDraft, record),
            "One automatic repair rebuilt draft connections from the operator pipeline.",
            AgentRunEventStatuses.Completed,
            new
            {
                repairReason = record.RepairReason,
                diffSummary = record.DiffSummary,
                resultStatus = record.ResultStatus,
                metadataOnly = true
            },
            repairAction: "rebuild_linear_connections",
            applyImpact: "editable_draft_allowed",
            deploymentImpact: "readiness_recheck_required");
    }

    private static BuildStepResult<StationCompatibilityResolution> BuildStationCompatibility(
        BuildPlanLoad load,
        VisionAgentToolResult packageReadiness)
    {
        var missing = ReadCount(packageReadiness.Data, "missingResources");
        var blocking = ReadCount(packageReadiness.Data, "blockingIssues");
        var report = new
        {
            source = "metadata_only_station_compatibility",
            stationTouched = false,
            cameraTouched = false,
            plcTouched = false,
            compatibleForCanvasDraft = true,
            deploymentBlocked = missing > 0 || blocking > 0,
            stationBoundarySummary = load.StationBoundarySummary,
            plcOutputPolicy = load.PlcOutputPolicy,
            missingResourceCount = missing,
            blockingIssueCount = blocking,
            metadataOnly = true
        };
        return StepResult(
            new StationCompatibilityResolution(report),
            missing > 0 || blocking > 0
                ? "Station compatibility is metadata-safe for canvas Apply; deployment remains blocked."
                : "Station compatibility metadata check passed.",
            AgentRunEventStatuses.Completed,
            report,
            warningCode: missing > 0 || blocking > 0 ? "station_deployment_blocked" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: missing > 0 || blocking > 0 ? "deployment_blocked" : "deployment_ready");
    }

    private static BuildStepResult<OperatorContractResolution> BuildOperatorContractReport(
        OperatorPipelineResolution pipeline,
        VisionAgentToolResult validation)
    {
        var invalid = pipeline.InvalidOperators;
        var report = new
        {
            source = "metadata_only_operator_contract",
            operatorCount = pipeline.Steps.Count,
            invalidOperatorsRemoved = invalid,
            validationBlockingIssueCount = ReadCount(validation.Data, "blockingIssues"),
            validationWarningCount = ReadCount(validation.Data, "warnings"),
            catalogBacked = invalid.Count == 0,
            metadataOnly = true
        };
        return StepResult(
            new OperatorContractResolution(report),
            invalid.Count == 0
                ? "Operator contract check used catalog-backed operators."
                : "Operator contract check removed invalid operators before draft validation.",
            AgentRunEventStatuses.Completed,
            report,
            warningCode: invalid.Count > 0 ? "operator_contract_repaired" : string.Empty,
            repairAction: invalid.Count > 0 ? "invalid_operator_removed" : string.Empty,
            applyImpact: "editable_draft_allowed",
            deploymentImpact: invalid.Count > 0 ? "operator_contract_repaired" : "no_deployment_blocker");
    }

    private static BuildStepResult<ReleaseReviewResolution> BuildReleaseReview(
        VisionAgentToolResult validation,
        VisionAgentToolResult dryRun,
        VisionAgentToolResult packageReadiness,
        ParameterMappingResolution parameters)
    {
        var validationBlocking = ReadCount(validation.Data, "blockingIssues");
        var dryRunSucceeded = ReadBool(dryRun.Data, "dryRunSucceeded") != false;
        var deploymentReady = ReadBool(packageReadiness.Data, "readyForDeployment") == true;
        var missing = parameters.MissingResources.Count + ReadCount(packageReadiness.Data, "missingResources");
        var report = new
        {
            source = "metadata_only_release_review",
            canvasApplyReady = validationBlocking == 0,
            runtimeDraftReady = validationBlocking == 0 && dryRunSucceeded,
            deploymentReady,
            deploymentBlocked = !deploymentReady,
            missingResourceCount = missing,
            pendingParameterGroupCount = parameters.PendingParameters.Count,
            metadataOnly = true
        };
        return StepResult(
            new ReleaseReviewResolution(report),
            deploymentReady
                ? "Release review marks the draft deployment-ready."
                : "Release review allows canvas Apply but blocks deployment until pending metadata is resolved.",
            AgentRunEventStatuses.Completed,
            report,
            warningCode: deploymentReady ? string.Empty : "deployment_not_ready",
            applyImpact: validationBlocking == 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: deploymentReady ? "deployment_ready" : "deployment_blocked");
    }

    private static BuildStepResult<VisionAgentWorkflowDiff> BuildWorkflowDiff(
        BuildPlanLoad load,
        DraftWorkflowResolution draft,
        ParameterMappingResolution parameters,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness,
        IReadOnlyList<VisionAgentBuildRepairRecord> repairs)
    {
        var preserved = load.HasCurrentFlow
            ? ReadExistingNodeIds(load.CurrentFlowSnapshot)
            : [];
        var diff = new VisionAgentWorkflowDiff
        {
            AddedNodes = draft.AddedNodeIds,
            ModifiedNodes = parameters.Mappings
                .Where(item => !item.Pending)
                .Select(item => $"{item.TempId}.{item.ParameterName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PreservedNodes = preserved.ToList(),
            RemovedNodes = [],
            AddedOrChangedParameters = parameters.Mappings
                .Select(item => $"{item.TempId}.{item.ParameterName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PendingParameters = parameters.PendingParameters
                .SelectMany(item => item.ParameterNames.Select(name => $"{item.OperatorId}.{name}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MissingResources = parameters.MissingResources
                .Select(item => item.ResourceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ValidationFailures = ReadIssueCodes(validation.Data, "blockingIssues"),
            AutoRepairs = repairs.Select(item => item.DiffSummary).ToList(),
            DeploymentBlockers = ReadIssueCodes(packageReadiness.Data, "blockingIssues")
                .Concat(parameters.MissingResources.Select(item => item.ResourceKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MetadataOnly = true
        };
        return StepResult(
            diff,
            $"Workflow diff: {diff.AddedNodes.Count} added, {diff.PreservedNodes.Count} preserved, {diff.PendingParameters.Count} pending parameter(s).",
            AgentRunEventStatuses.Completed,
            diff,
            warningCode: diff.DeploymentBlockers.Count > 0 ? "deployment_blockers_present" : string.Empty,
            applyImpact: diff.ValidationFailures.Count == 0 ? "editable_draft_allowed" : "blocked",
            deploymentImpact: diff.DeploymentBlockers.Count > 0 ? "deployment_blocked" : "deployment_ready");
    }

    private static BuildStepResult<VisionAgentApplyGate> BuildApplyGate(
        VisionAgentToolResult validation,
        VisionAgentToolResult dryRun,
        VisionAgentToolResult packageReadiness,
        VisionAgentWorkflowDiff diff)
    {
        var validationBlocking = ReadCount(validation.Data, "blockingIssues");
        var dryRunSucceeded = ReadBool(dryRun.Data, "dryRunSucceeded") != false;
        var deploymentReady = ReadBool(packageReadiness.Data, "readyForDeployment") == true &&
                              diff.DeploymentBlockers.Count == 0;
        var canvasReady = validationBlocking == 0;
        var runtimeReady = canvasReady && dryRunSucceeded;
        var gate = new VisionAgentApplyGate
        {
            CanvasApplyReady = canvasReady,
            RuntimeDraftReady = runtimeReady,
            DeploymentReady = deploymentReady,
            Blocked = !canvasReady,
            Status = !canvasReady ? "blocked" :
                deploymentReady ? "deployment_ready" :
                runtimeReady ? "runtime_draft_ready" : "canvas_apply_ready",
            ApplyBlockers = canvasReady ? [] : ReadIssueCodes(validation.Data, "blockingIssues"),
            DeploymentBlockers = deploymentReady
                ? []
                : diff.DeploymentBlockers.Count > 0 ? diff.DeploymentBlockers : ["deployment_metadata_pending"],
            MetadataOnly = true
        };
        return StepResult(
            gate,
            $"Apply gate resolved as {gate.Status}.",
            canvasReady ? AgentRunEventStatuses.Completed : AgentRunEventStatuses.Blocked,
            gate,
            warningCode: deploymentReady ? string.Empty : "deployment_not_ready",
            applyImpact: canvasReady ? "editable_draft_allowed" : "blocked",
            deploymentImpact: deploymentReady ? "deployment_ready" : "deployment_blocked");
    }

    private static VisionAgentToolContext BuildToolContext(
        AiFlowGenerationRequest request,
        VisionAgentBuildFromPlanRequest? build,
        string? currentFlowSnapshot)
    {
        return new VisionAgentToolContext
        {
            UserDescription = FirstNonEmpty(build?.OriginalUserPrompt, request.Description),
            AdditionalContext = request.AdditionalContext,
            SessionId = request.SessionId,
            AgentRunId = request.AgentRunId,
            ExistingFlowJson = currentFlowSnapshot,
            DebugTrace = false,
            RuntimePreviewConsent = false,
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };
    }

    private static VisionAgentParameterMapping MapParameterValue(
        VisionAgentOperatorPipelineStep op,
        OperatorParameterItem parameter,
        BuildPlanLoad load)
    {
        var key = $"{op.OperatorType}.{parameter.Name}";
        if (load.UserSelections.TryGetValue(parameter.Name, out var direct) ||
            load.UserSelections.TryGetValue(key, out direct))
        {
            return new VisionAgentParameterMapping
            {
                TempId = op.TempId,
                OperatorType = op.OperatorType,
                ParameterName = parameter.Name,
                ValueSummary = CleanValue(direct),
                Source = "user_selection",
                Pending = false,
                Impact = "User selection mapped into draft parameter metadata."
            };
        }

        var fallback = DefaultParameterValue(op.OperatorType, parameter.Name);
        var pending = parameter.Required || fallback.Contains("pending", StringComparison.OrdinalIgnoreCase);
        return new VisionAgentParameterMapping
        {
            TempId = op.TempId,
            OperatorType = op.OperatorType,
            ParameterName = parameter.Name,
            ValueSummary = fallback,
            Source = pending ? "pending_metadata" : "accepted_default",
            Pending = pending,
            Impact = pending
                ? "Canvas Apply can continue, but deployment readiness remains blocked until this metadata is bound."
                : "Default metadata keeps the draft editable."
        };
    }

    private static CanonicalDraft BuildCanonicalDraft(
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters,
        bool forceLinearConnections = false)
    {
        var operators = pipeline.Steps.Select(step => new
        {
            tempId = step.TempId,
            operatorType = step.OperatorType,
            displayName = step.OperatorType,
            parameters = parameters.Mappings
                .Where(item => string.Equals(item.TempId, step.TempId, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.ParameterName, item => item.ValueSummary, StringComparer.OrdinalIgnoreCase)
        }).ToList<object>();
        var connections = BuildCanonicalConnections(pipeline.Steps, forceLinearConnections).ToList();
        var draft = new
        {
            operators,
            connections,
            entryOperatorTempId = pipeline.Steps.FirstOrDefault()?.TempId ?? string.Empty,
            metadataOnly = true
        };
        return new CanonicalDraft(
            draft,
            pipeline.Steps.FirstOrDefault()?.TempId ?? string.Empty,
            pipeline.Steps.Select(item => item.TempId).ToList(),
            connections.Count);
    }

    private static IEnumerable<object> BuildCanonicalConnections(
        IReadOnlyList<VisionAgentOperatorPipelineStep> steps,
        bool forceLinearConnections)
    {
        for (var index = 0; index < steps.Count - 1; index++)
        {
            var source = steps[index];
            var target = steps[index + 1];
            VisionAgentReadOnlyCatalog.Schemas.TryGetValue(source.OperatorType, out var sourceSchema);
            VisionAgentReadOnlyCatalog.Schemas.TryGetValue(target.OperatorType, out var targetSchema);
            var sourcePort = sourceSchema?.OutputPorts.FirstOrDefault();
            var targetPort = targetSchema?.InputPorts.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sourcePort) ||
                string.IsNullOrWhiteSpace(targetPort))
            {
                if (!forceLinearConnections)
                {
                    continue;
                }
            }

            yield return new
            {
                sourceTempId = source.TempId,
                sourcePortName = sourcePort ?? "Output",
                targetTempId = target.TempId,
                targetPortName = targetPort ?? "Input"
            };
        }
    }

    private static OperatorFlowDto BuildCanvasFlow(
        BuildPlanLoad load,
        BuildIntentResolution intent,
        OperatorPipelineResolution pipeline,
        ParameterMappingResolution parameters)
    {
        var flow = intent.BuildIntent != "new"
            ? TryReadExistingCanvasFlow(load.CurrentFlowSnapshot) ?? new OperatorFlowDto()
            : new OperatorFlowDto();
        flow.Id = flow.Id == Guid.Empty ? Guid.NewGuid() : flow.Id;
        flow.Name = string.IsNullOrWhiteSpace(flow.Name)
            ? FirstNonEmpty(load.Plan?.Goal, "Vision Agent workflow draft")
            : flow.Name;

        var existingNames = flow.Operators
            .Select(op => op.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var step in pipeline.Steps)
        {
            if (existingNames.Contains(step.TempId))
            {
                continue;
            }

            var op = BuildCanvasOperator(step, parameters, flow.Operators.Count);
            flow.Operators.Add(op);
            existingNames.Add(step.TempId);
        }

        if (flow.Connections.Count == 0)
        {
            AddCanvasConnections(flow);
        }

        return flow;
    }

    private static OperatorDto BuildCanvasOperator(
        VisionAgentOperatorPipelineStep step,
        ParameterMappingResolution parameters,
        int index)
    {
        var id = Guid.NewGuid();
        VisionAgentReadOnlyCatalog.Schemas.TryGetValue(step.OperatorType, out var schema);
        var inputPorts = schema?.InputPorts ?? Array.Empty<string>();
        var outputPorts = schema?.OutputPorts ?? Array.Empty<string>();
        return new OperatorDto
        {
            Id = id,
            Name = step.TempId,
            Type = ToOperatorType(step.OperatorType),
            X = 160 + index * 180,
            Y = 180,
            InputPorts = inputPorts.Select(name => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = name,
                Direction = PortDirection.Input,
                DataType = PortDataType.Any,
                IsRequired = true
            }).ToList(),
            OutputPorts = outputPorts.Select(name => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = name,
                Direction = PortDirection.Output,
                DataType = string.Equals(name, "Image", StringComparison.OrdinalIgnoreCase)
                    ? PortDataType.Image
                    : PortDataType.Any,
                IsRequired = false
            }).ToList(),
            Parameters = parameters.Mappings
                .Where(item => string.Equals(item.TempId, step.TempId, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ParameterDto
                {
                    Id = Guid.NewGuid(),
                    Name = item.ParameterName,
                    DisplayName = item.ParameterName,
                    DataType = "string",
                    Value = item.ValueSummary,
                    DefaultValue = item.ValueSummary,
                    IsRequired = item.Pending
                })
                .ToList(),
            IsEnabled = true
        };
    }

    private static void AddCanvasConnections(OperatorFlowDto flow)
    {
        for (var index = 0; index < flow.Operators.Count - 1; index++)
        {
            var source = flow.Operators[index];
            var target = flow.Operators[index + 1];
            var sourcePort = source.OutputPorts.FirstOrDefault();
            var targetPort = target.InputPorts.FirstOrDefault();
            if (sourcePort == null || targetPort == null)
            {
                continue;
            }

            flow.Connections.Add(new OperatorConnectionDto
            {
                Id = Guid.NewGuid(),
                SourceOperatorId = source.Id,
                SourcePortId = sourcePort.Id,
                TargetOperatorId = target.Id,
                TargetPortId = targetPort.Id
            });
        }
    }

    private object? EvidencePayload(VisionAgentToolEvidence evidence, object? details)
    {
        var payload = _redactor.RedactObject(new
        {
            evidence.Stage,
            evidence.ToolName,
            evidence.InputSummary,
            evidence.OutputSummary,
            evidence.Status,
            evidence.DurationMs,
            evidence.EvidenceId,
            evidence.RepairAction,
            evidence.WarningCode,
            evidence.ApplyImpact,
            evidence.DeploymentImpact,
            evidence.MetadataOnly,
            evidence.RedactionPass,
            details
        });
        if (_redactor.IsRedactionSafe(payload))
        {
            return payload;
        }

        return new
        {
            evidence.Stage,
            evidence.ToolName,
            evidence.InputSummary,
            OutputSummary = "Unsafe metadata was removed before publishing this tool evidence.",
            Status = AgentRunEventStatuses.Completed,
            evidence.DurationMs,
            evidence.EvidenceId,
            evidence.RepairAction,
            WarningCode = "unsafe_metadata_redacted",
            evidence.ApplyImpact,
            DeploymentImpact = "review_public_diagnostics",
            MetadataOnly = true,
            RedactionPass = true,
            DetailsRedacted = true
        };
    }

    private static BuildStepResult<T> StepResult<T>(
        T payload,
        string outputSummary,
        string status,
        object? details,
        string warningCode = "",
        string repairAction = "",
        string applyImpact = "",
        string deploymentImpact = "")
    {
        return new BuildStepResult<T>(
            payload,
            outputSummary,
            status,
            details,
            warningCode,
            repairAction,
            applyImpact,
            deploymentImpact);
    }

    private static TemplateCandidate? FirstTemplateCandidate(object? data)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, "candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in candidates.EnumerateArray())
        {
            return new TemplateCandidate(
                ReadString(item, "templateId") ?? string.Empty,
                ReadString(item, "scenarioKey") ?? string.Empty,
                ReadDouble(item, "score"));
        }

        return null;
    }

    private static IEnumerable<string> ReadOperatorTypes(object? templateSkeleton)
    {
        var root = ToJsonElementOrNull(templateSkeleton);
        if (root == null ||
            !TryGetProperty(root.Value, "operators", out var operators) ||
            operators.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var op in operators.EnumerateArray())
        {
            var type = ReadString(op, "operatorType") ?? ReadString(op, "type");
            if (!string.IsNullOrWhiteSpace(type))
            {
                yield return type;
            }
        }
    }

    private static bool ToolHasBlockingIssues(VisionAgentToolResult result)
    {
        if (!result.Success)
        {
            return true;
        }

        return ReadCount(result.Data, "blockingIssues") > 0 ||
               ReadBool(result.Data, "dryRunSucceeded") == false;
    }

    private static string ToolSummary(string toolName, JsonElement? data, bool blocking)
    {
        if (data == null)
        {
            return $"{toolName} completed with no public data payload.";
        }

        if (toolName == "validate_flow")
        {
            return blocking
                ? "Schema validation found blocking issues."
                : "Schema validation passed with public metadata.";
        }

        if (toolName == "dryrun_flow")
        {
            return ReadBool(data.Value, "dryRunSucceeded") == false
                ? "Metadata dry-run reported a blocked draft."
                : "Metadata dry-run completed successfully.";
        }

        if (toolName == "runtime_package_precheck")
        {
            return ReadBool(data.Value, "readyForDeployment") == true
                ? "Runtime package readiness passed."
                : "Runtime package readiness blocks deployment but not canvas Apply.";
        }

        return $"{toolName} completed.";
    }

    private static List<AiPendingParameterInfo> MergePendingParameters(
        IEnumerable<AiPendingParameterInfo> mapped,
        AiFlowGenerationRequest request)
    {
        return DeduplicatePending(mapped
            .Concat(request.BuildFromPlan?.PlanSnapshot?.RecommendedDefaults
                .Where(item => item.Value.Contains("pending", StringComparison.OrdinalIgnoreCase))
                .Select(item => new AiPendingParameterInfo
                {
                    OperatorId = "plan_default",
                    ActualOperatorId = "plan_default",
                    ParameterNames = [item.Id]
                }) ?? []));
    }

    private static List<AiMissingResourceInfo> MergeMissingResources(
        IEnumerable<AiMissingResourceInfo> mapped,
        VisionAgentToolResult validation,
        VisionAgentToolResult packageReadiness,
        AiFlowGenerationRequest request)
    {
        var resources = mapped.ToList();
        resources.AddRange(ReadMissingResources(validation.Data));
        resources.AddRange(ReadMissingResources(packageReadiness.Data));
        return DeduplicateMissing(resources);
    }

    private static List<AiPendingParameterInfo> DeduplicatePending(IEnumerable<AiPendingParameterInfo> items)
    {
        return items
            .GroupBy(item => $"{item.OperatorId}|{item.ActualOperatorId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => new AiPendingParameterInfo
            {
                OperatorId = group.First().OperatorId,
                ActualOperatorId = group.First().ActualOperatorId,
                ParameterNames = group.SelectMany(item => item.ParameterNames)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(item => item.ParameterNames.Count > 0)
            .ToList();
    }

    private static List<AiMissingResourceInfo> DeduplicateMissing(IEnumerable<AiMissingResourceInfo> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.ResourceType) ||
                           !string.IsNullOrWhiteSpace(item.ResourceKey))
            .GroupBy(item => $"{item.ResourceType}|{item.ResourceKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IEnumerable<AiMissingResourceInfo> ReadMissingResources(object? data)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, "missingResources", out var resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in resources.EnumerateArray())
        {
            var kind = ReadString(item, "resourceType") ??
                       ReadString(item, "resourceKind") ??
                       "missing_resource";
            var key = ReadString(item, "resourceKey") ??
                      $"{ReadString(item, "tempId")}.{ReadString(item, "parameterName")}";
            yield return new AiMissingResourceInfo
            {
                ResourceType = kind,
                ResourceKey = key,
                Description = FirstNonEmpty(
                    ReadString(item, "description"),
                    ReadString(item, "message"),
                    "Missing resource metadata.")
            };
        }
    }

    private static string FirstFixRecommendation(
        VisionAgentApplyGate gate,
        IReadOnlyList<AiMissingResourceInfo> missingResources,
        IReadOnlyList<AiPendingParameterInfo> pendingParameters)
    {
        if (gate.Blocked)
        {
            return "Fix workflow structure blockers before applying the draft to the canvas.";
        }

        var firstMissing = missingResources.FirstOrDefault();
        if (firstMissing != null)
        {
            return $"Bind missing {firstMissing.ResourceType} metadata for {firstMissing.ResourceKey} before deployment.";
        }

        var firstPending = pendingParameters.FirstOrDefault();
        if (firstPending != null)
        {
            return $"Confirm pending parameter metadata on {firstPending.OperatorId} before release.";
        }

        return gate.DeploymentReady
            ? "Review the draft on canvas, then proceed to runtime packaging when ready."
            : "Review readiness gates and resolve deployment blockers before Station deployment.";
    }

    private static object? ToJsonCompatible(object? value)
    {
        return value == null
            ? null
            : JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions);
    }

    private static JsonElement ToJsonElement(object value)
    {
        return JsonSerializer.SerializeToElement(value, JsonOptions);
    }

    private static JsonElement? ToJsonElementOrNull(object? value)
    {
        if (value == null)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(value, JsonOptions);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    private static bool? ReadBool(object? data, string propertyName)
    {
        var root = ToJsonElementOrNull(data);
        return root == null ? null : ReadBool(root.Value, propertyName);
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int ReadCount(object? data, string propertyName)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
    }

    private static List<string> ReadIssueCodes(object? data, string propertyName)
    {
        var root = ToJsonElementOrNull(data);
        if (root == null ||
            !TryGetProperty(root.Value, propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => FirstNonEmpty(ReadString(item, "code"), ReadString(item, "message")))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadExistingNodeIds(string? currentFlowSnapshot)
    {
        if (string.IsNullOrWhiteSpace(currentFlowSnapshot))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(currentFlowSnapshot);
            if (!TryGetProperty(doc.RootElement, "operators", out var operators) ||
                operators.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return operators.EnumerateArray()
                .Select(item => FirstNonEmpty(
                    ReadString(item, "tempId"),
                    ReadString(item, "id"),
                    ReadString(item, "name")))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Take(32)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static OperatorFlowDto? TryReadExistingCanvasFlow(string? currentFlowSnapshot)
    {
        if (string.IsNullOrWhiteSpace(currentFlowSnapshot))
        {
            return null;
        }

        try
        {
            var flow = JsonSerializer.Deserialize<OperatorFlowDto>(currentFlowSnapshot, JsonOptions);
            return flow?.Operators.Count > 0 ? flow : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DefaultParameterValue(string operatorType, string parameterName)
    {
        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-camera-binding>";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-model-resource>";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-template-artifact>";
        }

        if (parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-tolerance>";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "<pending-output-channel>";
        }

        return operatorType switch
        {
            "ResultJudgment" when parameterName.Equals("Rule", StringComparison.OrdinalIgnoreCase) => "OK when inspection score satisfies configured threshold.",
            "Thresholding" when parameterName.Equals("Mode", StringComparison.OrdinalIgnoreCase) => "adaptive_review",
            "TemplateMatching" when parameterName.Equals("MinScore", StringComparison.OrdinalIgnoreCase) => "0.8",
            "TemplateMatching" when parameterName.Equals("MaxMatches", StringComparison.OrdinalIgnoreCase) => "1",
            "DeepLearning" when parameterName.Equals("ConfidenceThreshold", StringComparison.OrdinalIgnoreCase) => "0.6",
            "SurfaceDefectDetection" when parameterName.Equals("ModelKind", StringComparison.OrdinalIgnoreCase) => "surface_defect",
            "SemanticSegmentation" when parameterName.Equals("ModelKind", StringComparison.OrdinalIgnoreCase) => "segmentation",
            "BlobAnalysis" when parameterName.Equals("MinArea", StringComparison.OrdinalIgnoreCase) => "20",
            "BlobAnalysis" when parameterName.Equals("MaxArea", StringComparison.OrdinalIgnoreCase) => "<pending-max-area>",
            "RoiManager" when parameterName.Equals("RoiName", StringComparison.OrdinalIgnoreCase) => "inspection_roi",
            _ => "<pending-parameter>"
        };
    }

    private static string MissingResourceKind(string operatorType, string parameterName, bool pending)
    {
        if (!pending)
        {
            return string.Empty;
        }

        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return "camera_binding";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "model_resource";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "template_artifact";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase))
        {
            return "output_channel";
        }

        if (operatorType.Contains("Measure", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("tolerance", StringComparison.OrdinalIgnoreCase))
        {
            return "measurement_parameter";
        }

        return string.Empty;
    }

    private static OperatorType ToOperatorType(string operatorType)
    {
        if (Enum.TryParse<OperatorType>(operatorType, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return operatorType switch
        {
            "MeasureDistance" => OperatorType.Measurement,
            "SemanticSegmentation" => OperatorType.DeepLearning,
            "ImageCompose" => OperatorType.ImageAdd,
            _ => OperatorType.DeepLearning
        };
    }

    private static GenerateFlowMode ToGenerateFlowMode(string buildIntent)
    {
        return buildIntent switch
        {
            "modify" or "refactor" => GenerateFlowMode.Modify,
            "explain" => GenerateFlowMode.Explain,
            "review_pending_parameters" => GenerateFlowMode.ReviewPendingParameters,
            _ => GenerateFlowMode.New
        };
    }

    private static string ToTurnIntent(string buildIntent)
    {
        return buildIntent switch
        {
            "modify" or "refactor" => AiTurnIntents.ModifyFlow,
            "explain" => AiTurnIntents.ExplainFlow,
            "review_pending_parameters" => AiTurnIntents.ReviewPendingParameters,
            _ => AiTurnIntents.NewFlow
        };
    }

    private static int FlowOperatorCount(object? flow)
    {
        if (flow is OperatorFlowDto dto)
        {
            return dto.Operators.Count;
        }

        var root = ToJsonElementOrNull(flow);
        return root != null &&
               TryGetProperty(root.Value, "operators", out var operators) &&
               operators.ValueKind == JsonValueKind.Array
            ? operators.GetArrayLength()
            : 0;
    }

    private static string StageTitle(string stage)
    {
        return stage switch
        {
            "plan_generation" => "Load Plan",
            "resolve_build_intent" => "Resolve Build intent",
            "template_strategy" => "Resolve template strategy",
            "operator_pipeline" => "Select operator pipeline",
            "parameter_mapping" => "Map parameters",
            "workflow_draft" => "Draft workflow",
            "validate_schema" => "Validate schema",
            "metadata_dry_run" => "Metadata dry-run",
            "package_readiness" => "Package readiness",
            "station_compatibility" => "Station compatibility",
            "operator_contract" => "Operator contract",
            "release_review" => "Release review",
            "repair_loop" => "Repair loop",
            "workflow_diff" => "Workflow diff",
            "apply_gate" => "Apply gate",
            _ => stage
        };
    }

    private static string NormalizeStatus(string? status)
    {
        return status is AgentRunEventStatuses.Completed or
            AgentRunEventStatuses.Failed or
            AgentRunEventStatuses.Blocked or
            AgentRunEventStatuses.Cancelled or
            AgentRunEventStatuses.Running
            ? status
            : AgentRunEventStatuses.Completed;
    }

    private static string TempIdFor(string operatorType, int ordinal)
    {
        return operatorType switch
        {
            "ImageAcquisition" => "op_cam",
            "RoiManager" => "op_roi",
            "SurfaceDefectDetection" => "op_surface_defect",
            "DeepLearning" => "op_detect",
            "SemanticSegmentation" => "op_segment",
            "TemplateMatching" => "op_match",
            "BlobAnalysis" => "op_blob",
            "Thresholding" => "op_threshold",
            "CircleMeasurement" => ordinal <= 2 ? "op_circle_a" : "op_circle_b",
            "MeasureDistance" => "op_distance",
            "ResultJudgment" => "op_judge",
            "ResultOutput" => "op_out",
            _ => $"op_{new string(operatorType.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())}_{ordinal}"
        };
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string CleanValue(string? value)
    {
        var text = Clean(value);
        return string.IsNullOrWhiteSpace(text) ? "<pending-parameter>" : text;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.Select(Clean).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private sealed record BuildStepResult<T>(
        T Payload,
        string OutputSummary,
        string Status,
        object? PayloadDetails,
        string WarningCode = "",
        string RepairAction = "",
        string ApplyImpact = "",
        string DeploymentImpact = "");

    private sealed record BuildPlanLoad
    {
        public string PlanId { get; init; } = string.Empty;
        public string PlanHash { get; init; } = string.Empty;
        public string ComputedPlanHash { get; init; } = string.Empty;
        public VisionAgentPlanModeResult? Plan { get; init; }
        public IReadOnlyDictionary<string, string> UserSelections { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> AcceptedDefaults { get; init; } = [];
        public bool AcceptedRecommendedDefaults { get; init; }
        public string CurrentFlowSnapshot { get; init; } = string.Empty;
        public AiTemplateSelectionInfo? TemplateSelection { get; init; }
        public VisionAgentAttachmentSummary AttachmentSummary { get; init; } = new();
        public string OperatorCatalogVersion { get; init; } = string.Empty;
        public string StationBoundarySummary { get; init; } = string.Empty;
        public string PlcOutputPolicy { get; init; } = string.Empty;
        public string OriginalUserPrompt { get; init; } = string.Empty;
        public string BuildIntentHint { get; init; } = string.Empty;
        public bool HashMismatch { get; init; }
        public bool HasCurrentFlow { get; init; }
    }

    private sealed record BuildIntentResolution(string BuildIntent);

    private sealed record TemplateStrategyResolution(
        string Strategy,
        string TemplateId,
        string ScenarioKey,
        object? TemplateSkeleton,
        string GenerationMode,
        string TemplateLockLevel);

    private sealed record OperatorPipelineResolution(
        List<VisionAgentOperatorPipelineStep> Steps,
        List<string> InvalidOperators);

    private sealed record ParameterMappingResolution(
        List<VisionAgentParameterMapping> Mappings,
        List<AiPendingParameterInfo> PendingParameters,
        List<AiMissingResourceInfo> MissingResources);

    private sealed record CanonicalDraft(
        object WorkflowDraft,
        string EntryOperatorTempId,
        List<string> AddedNodeIds,
        int ConnectionCount);

    private sealed record DraftWorkflowResolution(
        AiFlowGenerationResult GenerationResult,
        object WorkflowDraft,
        string EntryOperatorTempId,
        OperatorFlowDto CanvasFlow,
        List<string> AddedNodeIds);

    private sealed record RepairDraftResolution(
        DraftWorkflowResolution Draft,
        VisionAgentBuildRepairRecord Record);

    private sealed record StationCompatibilityResolution(object Report);

    private sealed record OperatorContractResolution(object Report);

    private sealed record ReleaseReviewResolution(object Report);

    private sealed record TemplateCandidate(
        string TemplateId,
        string ScenarioKey,
        double Score);
}
