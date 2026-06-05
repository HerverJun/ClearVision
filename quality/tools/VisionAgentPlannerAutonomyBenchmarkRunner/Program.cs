using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using Microsoft.Extensions.Options;

var options = PlannerBenchmarkRunnerOptions.Parse(args);
var result = await VisionAgentPlannerAutonomyBenchmark.RunAsync(options, CancellationToken.None);
options.Output.Directory?.Create();
options.Report.Directory?.Create();
var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
File.WriteAllText(
    options.Output.FullName,
    JsonSerializer.Serialize(result, VisionAgentPlannerAutonomyBenchmark.JsonOptions) + Environment.NewLine,
    utf8NoBom);
File.WriteAllText(
    options.Report.FullName,
    VisionAgentPlannerAutonomyBenchmarkMarkdown.Create(result, options.Output),
    utf8NoBom);
Console.WriteLine($"wrote {VisionAgentPlannerAutonomyBenchmark.RepoRelative(options.Output)}");
Console.WriteLine($"wrote {VisionAgentPlannerAutonomyBenchmark.RepoRelative(options.Report)}");
return result.Summary.Accepted ? 0 : 1;

internal static class VisionAgentPlannerAutonomyBenchmark
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly IReadOnlyList<string> RuntimeDeniedErrorCodes =
    [
        RuntimePreviewPermissionGate.ConsentRequiredErrorCode,
        RuntimePreviewPermissionGate.PermissionDeniedErrorCode,
        "tool_permission_denied"
    ];

    public static async Task<PlannerBenchmarkDocument> RunAsync(
        PlannerBenchmarkRunnerOptions options,
        CancellationToken cancellationToken)
    {
        var plannerCases = CreatePlannerAutonomyCases();
        var permissionCases = CreatePermissionNegativeCases();
        var plannerResults = new List<PlannerBenchmarkCaseResult>();
        var permissionResults = new List<PlannerBenchmarkCaseResult>();

        foreach (var testCase in plannerCases)
        {
            plannerResults.Add(await RunCaseAsync(testCase, cancellationToken));
        }

        foreach (var testCase in permissionCases)
        {
            permissionResults.Add(await RunCaseAsync(testCase, cancellationToken));
        }

        var allResults = plannerResults.Concat(permissionResults).ToList();
        var safety = BuildSafety(allResults);
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["plannerAutonomyPassRate"] = Rate(plannerResults.Count(item => item.Passed), plannerResults.Count),
            ["permissionNegativePassRate"] = Rate(permissionResults.Count(item => item.Passed), permissionResults.Count),
            ["policyDecisionCoverageRate"] = Rate(allResults.Count(item => item.PolicyDecisions.Count > 0), allResults.Count),
            ["toolTraceCoverageRate"] = Rate(allResults.Count(item => item.ToolTrace.Count > 0), allResults.Count),
            ["runtimePreviewDeniedDraftAllowedRate"] = Rate(
                allResults.Count(item =>
                    item.PolicyDecisions.Any(decision =>
                        RuntimeDeniedErrorCodes.Contains(decision.ErrorCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)) &&
                    item.FinalWorkflowDraftAllowed),
                allResults.Count(item =>
                    item.PolicyDecisions.Any(decision =>
                        RuntimeDeniedErrorCodes.Contains(decision.ErrorCode ?? string.Empty, StringComparer.OrdinalIgnoreCase))))
        };
        var accepted = plannerResults.Count == 15 &&
                       permissionResults.Count == 6 &&
                       allResults.All(item => item.Passed) &&
                       safety.Violations.Count == 0;

        return new PlannerBenchmarkDocument(
            "2026-06-05.vision-agent-planner-autonomy-benchmark.v1",
            "vision_agent_planner_autonomy_benchmark",
            "2026-06-05T00:00:00Z",
            "offline_metadata_only",
            new PlannerBenchmarkSummary(
                plannerResults.Count,
                permissionResults.Count,
                allResults.Count,
                allResults.Count(item => item.Passed),
                accepted),
            metrics,
            safety,
            plannerResults,
            permissionResults);
    }

    private static async Task<PlannerBenchmarkCaseResult> RunCaseAsync(
        PlannerBenchmarkCase testCase,
        CancellationToken cancellationToken)
    {
        var recorder = new ToolExecutionRecorder();
        var registry = CreateRegistry(recorder);
        var parser = new VisionAgentProtocolParser();
        var policy = new AgentToolCallPolicy();
        var source = new RecordingPlannerCompletionSource(testCase, recorder, parser, policy);
        var planner = new VisionAgentPlannerService(
            source,
            parser,
            policy,
            new AgentPlannerPromptBuilder());
        var loop = new VisionAgentLoop(
            registry,
            parser,
            new AgentPromptBuilder(),
            Options.Create(new VisionAgentLoopOptions
            {
                MaxToolRounds = testCase.MaxToolRounds,
                MaxToolCallsPerRound = 4,
                MaxToolResultChars = 24_000
            }));
        var generationRequest = new AiFlowGenerationRequest(
            testCase.UserRequest,
            ExistingFlowJson: testCase.ExistingFlow == null ? null : SerializeFlow(testCase.ExistingFlow),
            Mode: testCase.ExistingFlow == null ? GenerateFlowMode.New : GenerateFlowMode.Modify)
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Planner,
            RuntimePreviewConsent = testCase.RuntimePreviewConsent
        };
        var toolContext = new VisionAgentToolContext
        {
            UserDescription = testCase.UserRequest,
            ExistingFlowJson = generationRequest.ExistingFlowJson,
            RuntimePreviewConsent = testCase.RuntimePreviewConsent,
            DebugTrace = true,
            AllowedPermissions = testCase.AllowedPermissions
        };

        VisionAgentLoopResult? loopResult = null;
        AgentToolCallPolicyViolationException? policyViolation = null;
        try
        {
            loopResult = await loop.RunAsync(
                new VisionAgentLoopRequest
                {
                    UserPrompt = testCase.UserRequest,
                    ToolContext = toolContext,
                    CompleteAsync = (messages, ct) => planner.CompleteAsync(
                        new AgentPlannerCompletionRequest
                        {
                            GenerationRequest = generationRequest,
                            Messages = messages
                        },
                        ct)
                },
                cancellationToken);
        }
        catch (AgentToolCallPolicyViolationException ex)
        {
            policyViolation = ex;
        }

        var toolTrace = BuildToolTrace(loopResult, source, policyViolation);
        var policyDecisions = source.PolicyDecisions
            .Concat(BuildExecutionPermissionDecisions(loopResult))
            .ToList();
        var pendingActions = BuildPendingActions(loopResult, policyDecisions, policyViolation);
        var actualToolCalls = BuildActualToolCalls(toolTrace);
        var actualValidation = recorder.LastData("validate_flow");
        var actualDryRun = recorder.LastData("dryrun_flow");
        var actualPrecheck = recorder.LastData("runtime_package_precheck") ??
                             BuildDeniedResult(toolTrace, "runtime_package_precheck");
        var actualRuntimePreview = recorder.LastData(RuntimePreviewPermissionGate.ReplayToolName) ??
                                   recorder.LastData(RuntimePreviewPermissionGate.CaptureToolName) ??
                                   BuildDeniedRuntimePreviewResult(toolTrace, toolContext);
        var finalWorkflowDraftAllowed = ReadWorkflowDraftAllowed(loopResult?.FinalContent, actualPrecheck);
        var finalContent = loopResult?.FinalContent ?? string.Empty;
        var failures = BuildFailures(
            testCase,
            loopResult,
            policyViolation,
            source.PolicyDecisions,
            toolTrace,
            actualValidation,
            actualDryRun,
            actualPrecheck,
            actualRuntimePreview,
            finalWorkflowDraftAllowed,
            pendingActions);

        return new PlannerBenchmarkCaseResult(
            testCase.CaseId,
            testCase.Category,
            testCase.TaskType,
            testCase.UserRequest,
            generationRequest.ExistingFlowJson,
            testCase.ExpectedBusinessActions,
            policy.ListAllowedToolNames(testCase.RuntimePreviewConsent),
            new PlannerContextSnapshot(
                testCase.RuntimePreviewConsent,
                testCase.AllowedPermissions.Select(item => item.ToString()).OrderBy(item => item).ToList()),
            source.PlannerMessages,
            source.PlannedToolCalls,
            policyDecisions,
            actualToolCalls,
            actualValidation,
            actualDryRun,
            actualPrecheck,
            actualRuntimePreview,
            toolTrace,
            pendingActions,
            finalContent,
            finalWorkflowDraftAllowed,
            loopResult?.Success ?? false,
            policyViolation == null ? loopResult?.FailureType : "planner_policy_denied",
            policyViolation?.ErrorCode,
            policyViolation?.Message ?? loopResult?.ErrorMessage,
            failures.Count == 0,
            failures);
    }

    private static IReadOnlyList<PlannerPolicyDecision> BuildExecutionPermissionDecisions(
        VisionAgentLoopResult? loopResult)
    {
        if (loopResult == null)
        {
            return [];
        }

        return loopResult.ToolTrace
            .Where(trace => !trace.Success && !string.IsNullOrWhiteSpace(trace.ErrorCode))
            .Select(trace => new PlannerPolicyDecision(
                "tool_execution_permission",
                0,
                trace.ToolName,
                false,
                trace.ErrorCode,
                trace.ErrorMessage))
            .ToList();
    }

    private static IReadOnlyList<VisionAgentToolTrace> BuildToolTrace(
        VisionAgentLoopResult? loopResult,
        RecordingPlannerCompletionSource source,
        AgentToolCallPolicyViolationException? policyViolation)
    {
        if (loopResult != null)
        {
            return loopResult.ToolTrace;
        }

        return source.PlannedToolCalls
            .Select(call => new VisionAgentToolTrace
            {
                ToolName = call.ToolName,
                Arguments = call.Arguments,
                Success = false,
                ErrorCode = policyViolation?.ErrorCode ?? "planner_policy_denied",
                ErrorMessage = policyViolation?.Message,
                Permission = string.Empty,
                PermissionDecision = new
                {
                    allowed = false,
                    reason = policyViolation?.ErrorCode ?? "planner_policy_denied"
                }
            })
            .ToList();
    }

    private static IReadOnlyList<VisionAgentPendingAction> BuildPendingActions(
        VisionAgentLoopResult? loopResult,
        IReadOnlyList<PlannerPolicyDecision> decisions,
        AgentToolCallPolicyViolationException? policyViolation)
    {
        var actions = loopResult?.PendingActions.ToList() ?? new List<VisionAgentPendingAction>();
        var deniedDecisions = decisions
            .Where(item => !item.Allowed && !string.IsNullOrWhiteSpace(item.ToolName))
            .ToList();
        if (policyViolation != null && deniedDecisions.Count == 0)
        {
            deniedDecisions.Add(new PlannerPolicyDecision(
                "planner_policy",
                0,
                "planner_tool_call",
                false,
                policyViolation.ErrorCode,
                policyViolation.Message));
        }

        foreach (var decision in deniedDecisions)
        {
            if (actions.Any(action =>
                    string.Equals(action.ActionType, "AuthorizeRuntimePreview", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(decision.ErrorCode, RuntimePreviewPermissionGate.ConsentRequiredErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actions.Add(new VisionAgentPendingAction
            {
                ActionType = "review_tool_policy_denial",
                Title = decision.ErrorCode ?? "tool_policy_denied",
                Summary = decision.ErrorMessage ?? $"Planner tool '{decision.ToolName}' was denied.",
                RequiresUserConfirmation = true,
                Payload = new
                {
                    decision.Stage,
                    decision.ToolName,
                    decision.ErrorCode,
                    decision.ErrorMessage
                }
            });
        }

        return actions;
    }

    private static IReadOnlyList<PlannerToolCallResult> BuildActualToolCalls(
        IReadOnlyList<VisionAgentToolTrace> toolTrace)
    {
        return toolTrace.Select(trace => new PlannerToolCallResult(
                trace.ToolName,
                trace.Permission,
                trace.Success,
                trace.ErrorCode,
                trace.ErrorMessage))
            .ToList();
    }

    private static IReadOnlyList<string> BuildFailures(
        PlannerBenchmarkCase testCase,
        VisionAgentLoopResult? loopResult,
        AgentToolCallPolicyViolationException? policyViolation,
        IReadOnlyList<PlannerPolicyDecision> policyDecisions,
        IReadOnlyList<VisionAgentToolTrace> toolTrace,
        JsonElement? actualValidation,
        JsonElement? actualDryRun,
        JsonElement? actualPrecheck,
        JsonElement? actualRuntimePreview,
        bool finalWorkflowDraftAllowed,
        IReadOnlyList<VisionAgentPendingAction> pendingActions)
    {
        var failures = new List<string>();
        var actualToolNames = toolTrace.Select(item => item.ToolName).ToList();
        if (testCase.ExpectedActualTools.Count > 0 &&
            !actualToolNames.SequenceEqual(testCase.ExpectedActualTools, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("actualToolCalls did not match expected planned execution trace.");
        }

        foreach (var errorCode in testCase.ExpectedPolicyErrorCodes)
        {
            var found = policyDecisions.Any(item =>
                            string.Equals(item.ErrorCode, errorCode, StringComparison.OrdinalIgnoreCase)) ||
                        toolTrace.Any(item =>
                            string.Equals(item.ErrorCode, errorCode, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(policyViolation?.ErrorCode, errorCode, StringComparison.OrdinalIgnoreCase);
            if (!found)
            {
                failures.Add($"expected policy error '{errorCode}' was not observed.");
            }
        }

        if (testCase.ExpectedLoopSuccess.HasValue &&
            testCase.ExpectedLoopSuccess.Value != (loopResult?.Success ?? false))
        {
            failures.Add($"loop success expected {testCase.ExpectedLoopSuccess.Value}.");
        }

        if (!string.IsNullOrWhiteSpace(testCase.ExpectedFailureType))
        {
            var actualFailureType = policyViolation == null ? loopResult?.FailureType : "planner_policy_denied";
            if (!string.Equals(actualFailureType, testCase.ExpectedFailureType, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"failureType expected {testCase.ExpectedFailureType}.");
            }
        }

        if (testCase.ExpectedValidationValid.HasValue &&
            testCase.ExpectedValidationValid.Value != (ReadBool(actualValidation, "isValid") == true))
        {
            failures.Add($"validation isValid expected {testCase.ExpectedValidationValid.Value}.");
        }

        if (testCase.ExpectedDryRunSucceeded.HasValue &&
            testCase.ExpectedDryRunSucceeded.Value != (ReadBool(actualDryRun, "dryRunSucceeded") == true))
        {
            failures.Add($"dryrun expected {testCase.ExpectedDryRunSucceeded.Value}.");
        }

        if (testCase.ExpectedPrecheckReady.HasValue &&
            testCase.ExpectedPrecheckReady.Value != (ReadBool(actualPrecheck, "readyForDeployment") == true))
        {
            failures.Add($"precheck readyForDeployment expected {testCase.ExpectedPrecheckReady.Value}.");
        }

        if (testCase.ExpectedRuntimePreviewReady.HasValue &&
            testCase.ExpectedRuntimePreviewReady.Value != (ReadBool(actualRuntimePreview, "previewReady") == true))
        {
            failures.Add($"RuntimePreview previewReady expected {testCase.ExpectedRuntimePreviewReady.Value}.");
        }

        if (testCase.ExpectedWorkflowDraftAllowed.HasValue &&
            testCase.ExpectedWorkflowDraftAllowed.Value != finalWorkflowDraftAllowed)
        {
            failures.Add($"workflowDraftAllowed expected {testCase.ExpectedWorkflowDraftAllowed.Value}.");
        }

        foreach (var actionType in testCase.ExpectedPendingActionTypes)
        {
            if (!pendingActions.Any(action =>
                    string.Equals(action.ActionType, actionType, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"pending action '{actionType}' was not observed.");
            }
        }

        if (testCase.RequireDeniedTrace &&
            !toolTrace.Any(item => !item.Success) &&
            policyViolation == null)
        {
            failures.Add("expected a denied toolTrace entry.");
        }

        return failures;
    }

    private static PlannerBenchmarkSafety BuildSafety(
        IReadOnlyList<PlannerBenchmarkCaseResult> results)
    {
        var violations = new List<string>();
        foreach (var preview in results
                     .Select(item => item.ActualRuntimePreviewResult)
                     .Where(item => item != null)
                     .Cast<JsonElement>())
        {
            if (ReadBool(preview, "capturedRealFrame") == true) violations.Add("captured_real_frame");
            if (ReadBool(preview, "loadedModelFiles") == true) violations.Add("loaded_model_files");
            if (ReadBool(preview, "accessedHardware") == true) violations.Add("accessed_hardware");
            if (ReadBool(preview, "stationTouched") == true) violations.Add("station_touched");
            if (ReadBool(preview, "binaryIncluded") == true) violations.Add("binary_included");
        }

        foreach (var toolName in results
                     .SelectMany(item => item.PlannedToolCalls.Select(call => call.ToolName)
                         .Concat(item.ActualToolCalls.Select(call => call.ToolName))))
        {
            if (ContainsAny(toolName, "shell", "cmd", "powershell", "system_command", "process", "network"))
            {
                violations.Add($"unsafe_tool_name:{toolName}");
            }
        }

        return new PlannerBenchmarkSafety(
            RealCameraSdkTouched: false,
            RealStationTouched: false,
            RealImageFilesRead: false,
            RealModelFilesLoaded: false,
            PlcWriteAttempted: false,
            PackageCreated: false,
            HotLoadAttempted: false,
            RuntimePreviewMode: "offline_metadata_only",
            Violations: violations.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static VisionAgentToolRegistry CreateRegistry(ToolExecutionRecorder recorder)
    {
        return new VisionAgentToolRegistry(
        [
            Wrap(new OperatorCatalogTool(), recorder),
            Wrap(new OperatorSchemaTool(), recorder),
            Wrap(new OperatorKnowledgeTool(), recorder),
            Wrap(new FlowTemplateMatchTool(), recorder),
            Wrap(new FlowTemplateSkeletonTool(), recorder),
            Wrap(new CurrentFlowInspectTool(), recorder),
            Wrap(new FlowValidationTool(), recorder),
            Wrap(new DryRunFlowTool(), recorder),
            Wrap(new RuntimePreviewCaptureStubTool(), recorder),
            Wrap(new RuntimePreviewReplayStubTool(), recorder),
            Wrap(new RuntimePackagePrecheckTool(), recorder)
        ]);
    }

    private static IVisionAgentTool Wrap(IVisionAgentTool tool, ToolExecutionRecorder recorder)
    {
        return new RecordingTool(tool, recorder);
    }

    private static IReadOnlyList<PlannerBenchmarkCase> CreatePlannerAutonomyCases()
    {
        return
        [
            PlannerCase(
                "VA-PL-001",
                "planner_autonomy",
                "wire_sequence_generation",
                "Generate a wire sequence inspection flow with camera input, model detection, judgement, and QA output.",
                ValidWireFlow(),
                ["select_template", "generate_workflow_draft", "validate_flow", "dryrun_flow", "precheck_deployment"],
                StandardGenerationSteps(),
                expectedActualTools:
                [
                    "match_flow_template",
                    "get_flow_template_skeleton",
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ]),
            PlannerCase(
                "VA-PL-002",
                "planner_autonomy",
                "template_matching_generation",
                "Generate a bracket template matching flow and keep template resource metadata in the draft.",
                ValidTemplateFlow(templateId: "mock-template-bracket"),
                ["select_template", "generate_workflow_draft", "validate_flow", "dryrun_flow", "precheck_deployment"],
                StandardGenerationSteps(),
                expectedActualTools:
                [
                    "match_flow_template",
                    "get_flow_template_skeleton",
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ]),
            PlannerCase(
                "VA-PL-003",
                "planner_autonomy",
                "hole_distance_generation",
                "Generate a hole distance measurement flow with two circle measurements and a distance tolerance judgement.",
                ValidHoleFlow(),
                ["select_template", "generate_workflow_draft", "validate_flow", "dryrun_flow", "precheck_deployment"],
                StandardGenerationSteps(),
                expectedActualTools:
                [
                    "match_flow_template",
                    "get_flow_template_skeleton",
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ]),
            PlannerCase(
                "VA-PL-004",
                "planner_autonomy",
                "modify_existing_flow",
                "Modify the existing template flow to tighten the minimum score and keep output routing.",
                ValidTemplateFlow(minScore: "0.91", templateId: "mock-template-existing"),
                ["inspect_existing_flow", "apply_draft_edits", "validate_flow", "dryrun_flow", "precheck_deployment"],
                ModifyExistingFlowSteps(),
                existingFlow: ValidTemplateFlow(minScore: "0.82", templateId: "mock-template-existing"),
                expectedActualTools:
                [
                    "inspect_current_flow",
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ]),
            PlannerCase(
                "VA-PL-005",
                "planner_autonomy",
                "missing_camera_binding",
                "Generate a wire sequence draft but leave the camera binding as a pending resource.",
                MissingCameraWireFlow(),
                ["generate_workflow_draft", "surface_missing_camera_binding", "validate_flow", "dryrun_flow", "precheck_deployment"],
                ReviewMissingResourceSteps(),
                expectedActualTools:
                [
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ],
                expectedPrecheckReady: false),
            PlannerCase(
                "VA-PL-006",
                "planner_autonomy",
                "missing_model_path",
                "Generate a deep learning draft but leave ModelPath or ModelId as a pending resource.",
                MissingModelFlow(),
                ["generate_workflow_draft", "surface_missing_model", "validate_flow", "dryrun_flow", "precheck_deployment"],
                ReviewMissingResourceSteps(),
                expectedActualTools:
                [
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ],
                expectedPrecheckReady: false),
            PlannerCase(
                "VA-PL-007",
                "planner_autonomy",
                "missing_template_path",
                "Generate a template matching draft but leave TemplatePath or TemplateId as a pending resource.",
                MissingTemplateFlow(),
                ["generate_workflow_draft", "surface_missing_template", "validate_flow", "dryrun_flow", "precheck_deployment"],
                ReviewMissingResourceSteps(),
                expectedActualTools:
                [
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ],
                expectedPrecheckReady: false),
            PlannerCase(
                "VA-PL-008",
                "planner_autonomy",
                "parameter_completion_review",
                "Review pending parameters after the user supplied camera binding, template id, model id, and output channel.",
                ValidTemplateAndModelFlow(),
                ["apply_parameter_completion", "review_effective_rules", "validate_flow", "dryrun_flow", "precheck_deployment"],
                ReviewMissingResourceSteps(finalAction: "parameterCompletionReviewed"),
                expectedActualTools:
                [
                    "validate_flow",
                    "dryrun_flow",
                    "runtime_package_precheck"
                ]),
            PlannerCase(
                "VA-PL-009",
                "planner_autonomy",
                "runtime_preview_authorized",
                "Validate the template flow and run an authorized offline RuntimePreview replay.",
                ValidTemplateFlow(templateId: "mock-template-preview"),
                ["validate_flow", "dryrun_flow", "authorize_runtime_preview", "preview_metadata_only"],
                RuntimePreviewSteps(),
                runtimePreviewConsent: true,
                allowedPermissions: FullPermissions(),
                expectedActualTools:
                [
                    "validate_flow",
                    "dryrun_flow",
                    RuntimePreviewPermissionGate.CaptureToolName,
                    RuntimePreviewPermissionGate.ReplayToolName
                ],
                expectedPrecheckReady: null,
                expectedRuntimePreviewReady: true),
            PlannerCase(
                "VA-PL-010",
                "planner_autonomy",
                "runtime_preview_unauthorized",
                "Attempt RuntimePreview without consent and still allow the workflow draft.",
                ValidTemplateFlow(templateId: "mock-template-preview-denied"),
                ["validate_flow", "request_runtime_preview_authorization", "keep_workflow_draft_allowed"],
                RuntimePreviewDeniedThenFinalSteps(),
                runtimePreviewConsent: false,
                allowedPermissions: StandardPermissions(),
                expectedActualTools:
                [
                    "validate_flow",
                    RuntimePreviewPermissionGate.CaptureToolName
                ],
                expectedPolicyErrors: [RuntimePreviewPermissionGate.ConsentRequiredErrorCode],
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedRuntimePreviewReady: false,
                expectedPendingActions: ["AuthorizeRuntimePreview"],
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PL-011",
                "planner_autonomy",
                "non_whitelist_tool_rejected",
                "Reject a planner attempt to call a camera binding listing helper that is not in the tool whitelist.",
                ValidTemplateFlow(templateId: "mock-template-policy"),
                ["reject_non_whitelist_tool"],
                [PlannerStep.Tool("list_camera_bindings", _ => Args(new { scope = "benchmark" }))],
                expectedActualTools: ["list_camera_bindings"],
                expectedPolicyErrors: ["tool_not_whitelisted"],
                expectedLoopSuccess: false,
                expectedFailureType: "planner_policy_denied",
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: false,
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PL-012",
                "planner_autonomy",
                "deployment_prepare_only_precheck",
                "Reject a DeploymentPrepare-like planner action that is not runtime_package_precheck.",
                ValidTemplateFlow(templateId: "mock-template-policy"),
                ["reject_non_precheck_deployment_prepare_tool"],
                [PlannerStep.Tool("stage_runtime_package_metadata", _ => Args(new { mode = "metadata_only" }))],
                expectedActualTools: ["stage_runtime_package_metadata"],
                expectedPolicyErrors: ["deployment_prepare_tool_denied"],
                expectedLoopSuccess: false,
                expectedFailureType: "planner_policy_denied",
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: false,
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PL-013",
                "planner_autonomy",
                "planner_max_rounds_controlled_failure",
                "Force the mock planner to exceed the loop tool round limit.",
                ValidTemplateFlow(templateId: "mock-template-round-limit"),
                ["surface_controlled_planner_round_limit_failure"],
                [],
                repeatToolName: "list_operator_catalog",
                maxToolRounds: 1,
                expectedActualTools: ["list_operator_catalog"],
                expectedLoopSuccess: false,
                expectedFailureType: "failed_with_tool_limit",
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: false),
            PlannerCase(
                "VA-PL-014",
                "planner_autonomy",
                "final_draft_edits_existing_flow",
                "Use final draftEdits to edit an existing flow without creating a new flow object.",
                ValidTemplateFlow(minScore: "0.93", templateId: "mock-template-draft-edits"),
                ["inspect_existing_flow", "validate_flow", "return_draft_edits"],
                DraftEditsSteps(),
                existingFlow: ValidTemplateFlow(minScore: "0.80", templateId: "mock-template-draft-edits"),
                expectedActualTools:
                [
                    "inspect_current_flow",
                    "validate_flow"
                ],
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null),
            PlannerCase(
                "VA-PL-015",
                "planner_autonomy",
                "final_workflow_draft_new_flow",
                "Use final workflowDraft to generate a new flow after validation.",
                ValidModelIdFlow(),
                ["validate_flow", "return_workflow_draft"],
                FinalWorkflowDraftSteps(),
                expectedActualTools:
                [
                    "validate_flow"
                ],
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null)
        ];
    }

    private static IReadOnlyList<PlannerBenchmarkCase> CreatePermissionNegativeCases()
    {
        return
        [
            PlannerCase(
                "VA-PERM-001",
                "permission_negative",
                "runtime_preview_consent_false_capture_replay",
                "Reject capture and replay when RuntimePreviewConsent is false.",
                ValidTemplateFlow(templateId: "mock-template-preview-negative"),
                ["reject_runtime_preview_without_consent", "record_pending_authorization", "keep_workflow_draft_allowed"],
                RuntimePreviewDeniedCaptureReplaySteps(),
                runtimePreviewConsent: false,
                allowedPermissions: FullPermissions(),
                expectedActualTools:
                [
                    RuntimePreviewPermissionGate.CaptureToolName,
                    RuntimePreviewPermissionGate.ReplayToolName
                ],
                expectedPolicyErrors: [RuntimePreviewPermissionGate.ConsentRequiredErrorCode],
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedRuntimePreviewReady: false,
                expectedPendingActions: ["AuthorizeRuntimePreview"],
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PERM-002",
                "permission_negative",
                "runtime_preview_permission_missing",
                "Reject RuntimePreview when consent exists but RuntimePreview permission is missing.",
                ValidTemplateFlow(templateId: "mock-template-preview-no-permission"),
                ["reject_runtime_preview_permission_missing", "keep_workflow_draft_allowed"],
                RuntimePreviewDeniedThenFinalSteps(),
                runtimePreviewConsent: true,
                allowedPermissions: PermissionsWithoutRuntimePreview(),
                expectedActualTools:
                [
                    "validate_flow",
                    RuntimePreviewPermissionGate.CaptureToolName
                ],
                expectedPolicyErrors: [RuntimePreviewPermissionGate.PermissionDeniedErrorCode],
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedRuntimePreviewReady: false,
                expectedPendingActions: ["AuthorizeRuntimePreview"],
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PERM-003",
                "permission_negative",
                "deployment_prepare_permission_missing",
                "Reject runtime_package_precheck when DeploymentPrepare permission is not allowed.",
                ValidTemplateFlow(templateId: "mock-template-no-deployment-permission"),
                ["reject_deployment_prepare_permission_missing"],
                [PlannerStep.Tool("runtime_package_precheck", PrecheckArgs)],
                allowedPermissions: PermissionsWithoutDeploymentPrepare(),
                expectedActualTools: ["runtime_package_precheck"],
                expectedPolicyErrors: ["tool_permission_denied"],
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: true,
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PERM-004",
                "permission_negative",
                "config_write_permanently_denied",
                "Reject any ConfigWrite-like planner action.",
                ValidTemplateFlow(templateId: "mock-template-config-write"),
                ["reject_config_write"],
                [PlannerStep.Tool("write_config_metadata", _ => Args(new { key = "agent" }))],
                expectedActualTools: ["write_config_metadata"],
                expectedPolicyErrors: ["config_write_denied"],
                expectedLoopSuccess: false,
                expectedFailureType: "planner_policy_denied",
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: false,
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PERM-005",
                "permission_negative",
                "non_whitelist_tool_denied",
                "Reject a planner tool call that is not registered or whitelisted.",
                ValidTemplateFlow(templateId: "mock-template-whitelist-negative"),
                ["reject_non_whitelist_tool"],
                [PlannerStep.Tool("propose_parameter_patch", _ => Args(new { tempId = "op_match" }))],
                expectedActualTools: ["propose_parameter_patch"],
                expectedPolicyErrors: ["tool_not_whitelisted"],
                expectedLoopSuccess: false,
                expectedFailureType: "planner_policy_denied",
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: false,
                requireDeniedTrace: true),
            PlannerCase(
                "VA-PERM-006",
                "permission_negative",
                "deployment_prepare_other_tool_denied",
                "Reject a DeploymentPrepare tool proposal other than runtime_package_precheck.",
                ValidTemplateFlow(templateId: "mock-template-deployment-negative"),
                ["reject_non_precheck_deployment_prepare_tool"],
                [PlannerStep.Tool("station_package_metadata_prepare", _ => Args(new { target = "metadata" }))],
                expectedActualTools: ["station_package_metadata_prepare"],
                expectedPolicyErrors: ["deployment_prepare_tool_denied"],
                expectedLoopSuccess: false,
                expectedFailureType: "planner_policy_denied",
                expectedValidationValid: null,
                expectedDryRunSucceeded: null,
                expectedPrecheckReady: null,
                expectedWorkflowDraftAllowed: false,
                requireDeniedTrace: true)
        ];
    }

    private static PlannerBenchmarkCase PlannerCase(
        string caseId,
        string category,
        string taskType,
        string userRequest,
        PlannerFlow flow,
        IReadOnlyList<string> expectedBusinessActions,
        IReadOnlyList<PlannerStep> steps,
        PlannerFlow? existingFlow = null,
        bool runtimePreviewConsent = false,
        IReadOnlySet<VisionAgentToolPermission>? allowedPermissions = null,
        IReadOnlyList<string>? expectedActualTools = null,
        IReadOnlyList<string>? expectedPolicyErrors = null,
        IReadOnlyList<string>? expectedPendingActions = null,
        string? repeatToolName = null,
        int maxToolRounds = 10,
        bool? expectedLoopSuccess = true,
        string? expectedFailureType = null,
        bool? expectedValidationValid = true,
        bool? expectedDryRunSucceeded = true,
        bool? expectedPrecheckReady = true,
        bool? expectedRuntimePreviewReady = null,
        bool? expectedWorkflowDraftAllowed = true,
        bool requireDeniedTrace = false)
    {
        return new PlannerBenchmarkCase
        {
            CaseId = caseId,
            Category = category,
            TaskType = taskType,
            UserRequest = userRequest,
            Flow = flow,
            ExistingFlow = existingFlow,
            ExpectedBusinessActions = expectedBusinessActions,
            Steps = steps,
            RuntimePreviewConsent = runtimePreviewConsent,
            AllowedPermissions = allowedPermissions ?? StandardPermissions(),
            ExpectedActualTools = expectedActualTools ?? [],
            ExpectedPolicyErrorCodes = expectedPolicyErrors ?? [],
            ExpectedPendingActionTypes = expectedPendingActions ?? [],
            RepeatToolName = repeatToolName,
            MaxToolRounds = maxToolRounds,
            ExpectedLoopSuccess = expectedLoopSuccess,
            ExpectedFailureType = expectedFailureType,
            ExpectedValidationValid = expectedValidationValid,
            ExpectedDryRunSucceeded = expectedDryRunSucceeded,
            ExpectedPrecheckReady = expectedPrecheckReady,
            ExpectedRuntimePreviewReady = expectedRuntimePreviewReady,
            ExpectedWorkflowDraftAllowed = expectedWorkflowDraftAllowed,
            RequireDeniedTrace = requireDeniedTrace
        };
    }

    private static IReadOnlyList<PlannerStep> StandardGenerationSteps()
    {
        return
        [
            PlannerStep.Tool("match_flow_template", state => Args(new { request = state.Case.UserRequest, topN = 3 })),
            PlannerStep.Tool("get_flow_template_skeleton", _ => Args(new { templateId = "vision-template", scenarioKey = "planner_autonomy" })),
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Tool("dryrun_flow", FlowArgs),
            PlannerStep.Tool("runtime_package_precheck", PrecheckArgs),
            PlannerStep.Final(state => FinalWorkflowDraft(state.Case.Flow, true, "workflowDraft"))
        ];
    }

    private static IReadOnlyList<PlannerStep> ModifyExistingFlowSteps()
    {
        return
        [
            PlannerStep.Tool("inspect_current_flow", state => Args(new
            {
                existingFlowJson = SerializeFlow(state.Case.ExistingFlow ?? state.Case.Flow)
            })),
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Tool("dryrun_flow", FlowArgs),
            PlannerStep.Tool("runtime_package_precheck", PrecheckArgs),
            PlannerStep.Final(state => FinalDraftEdits(state.Case.Flow, true))
        ];
    }

    private static IReadOnlyList<PlannerStep> ReviewMissingResourceSteps(string finalAction = "workflowDraft")
    {
        return
        [
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Tool("dryrun_flow", FlowArgs),
            PlannerStep.Tool("runtime_package_precheck", PrecheckArgs),
            PlannerStep.Final(state => FinalWorkflowDraft(state.Case.Flow, true, finalAction))
        ];
    }

    private static IReadOnlyList<PlannerStep> RuntimePreviewSteps()
    {
        return
        [
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Tool("dryrun_flow", FlowArgs),
            PlannerStep.Tool(RuntimePreviewPermissionGate.CaptureToolName, CaptureArgs),
            PlannerStep.Tool(RuntimePreviewPermissionGate.ReplayToolName, ReplayArgs),
            PlannerStep.Final(state => FinalWorkflowDraft(state.Case.Flow, true, "runtimePreviewAuthorized"))
        ];
    }

    private static IReadOnlyList<PlannerStep> RuntimePreviewDeniedThenFinalSteps()
    {
        return
        [
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Tool(RuntimePreviewPermissionGate.CaptureToolName, CaptureArgs),
            PlannerStep.Final(state => FinalWorkflowDraft(state.Case.Flow, true, "runtimePreviewAuthorizationPending"))
        ];
    }

    private static IReadOnlyList<PlannerStep> RuntimePreviewDeniedCaptureReplaySteps()
    {
        return
        [
            PlannerStep.Tool(RuntimePreviewPermissionGate.CaptureToolName, CaptureArgs),
            PlannerStep.Tool(RuntimePreviewPermissionGate.ReplayToolName, ReplayArgs),
            PlannerStep.Final(state => FinalWorkflowDraft(state.Case.Flow, true, "runtimePreviewAuthorizationPending"))
        ];
    }

    private static IReadOnlyList<PlannerStep> DraftEditsSteps()
    {
        return
        [
            PlannerStep.Tool("inspect_current_flow", state => Args(new
            {
                existingFlowJson = SerializeFlow(state.Case.ExistingFlow ?? state.Case.Flow)
            })),
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Final(state => FinalDraftEdits(state.Case.Flow, true))
        ];
    }

    private static IReadOnlyList<PlannerStep> FinalWorkflowDraftSteps()
    {
        return
        [
            PlannerStep.Tool("validate_flow", FlowArgs),
            PlannerStep.Final(state => FinalWorkflowDraft(state.Case.Flow, true, "workflowDraft"))
        ];
    }

    private static JsonElement FlowArgs(PlannerCaseRuntimeState state)
    {
        return Args(new
        {
            flow = state.Case.Flow,
            entryOperatorTempId = state.Case.EntryOperatorTempId
        });
    }

    private static JsonElement PrecheckArgs(PlannerCaseRuntimeState state)
    {
        var validationSummary = (object?)state.Recorder.LastData("validate_flow");
        var dryRunSummary = (object?)state.Recorder.LastData("dryrun_flow") ?? new
        {
            dryRunSucceeded = true,
            warnings = Array.Empty<object>(),
            blockingIssues = Array.Empty<object>()
        };

        return Args(new
        {
            flow = state.Case.Flow,
            validationSummary,
            dryRunSummary,
            targetStationId = state.Case.TargetStationId
        });
    }

    private static JsonElement CaptureArgs(PlannerCaseRuntimeState state)
    {
        return Args(new
        {
            cameraBindingId = state.Case.CameraBindingId,
            operatorTempId = state.Case.EntryOperatorTempId ?? "op_cam",
            reason = "planner autonomy benchmark offline metadata"
        });
    }

    private static JsonElement ReplayArgs(PlannerCaseRuntimeState state)
    {
        var captureData = state.Recorder.LastData(RuntimePreviewPermissionGate.CaptureToolName);
        return Args(new
        {
            flow = state.Case.Flow,
            frameId = ReadString(captureData, "frameId") ?? "offline-frame-planner-benchmark",
            entryOperatorTempId = state.Case.EntryOperatorTempId
        });
    }

    private static string FinalWorkflowDraft(PlannerFlow flow, bool workflowDraftAllowed, string action)
    {
        return SerializeProtocol(new
        {
            kind = "final",
            finalResponse = "planner completed",
            finalAction = action,
            workflowDraftAllowed,
            workflowDraft = flow
        });
    }

    private static string FinalDraftEdits(PlannerFlow flow, bool workflowDraftAllowed)
    {
        return SerializeProtocol(new
        {
            kind = "final",
            finalResponse = "planner completed",
            finalAction = "draftEdits",
            workflowDraftAllowed,
            draftEdits = new object[]
            {
                new
                {
                    op = "replace_flow",
                    path = "$",
                    value = flow
                }
            }
        });
    }

    public static string ToolCallProtocol(string id, string name, JsonElement arguments)
    {
        return SerializeProtocol(new
        {
            kind = "tool_call",
            toolCalls = new object[]
            {
                new
                {
                    id,
                    name,
                    arguments
                }
            }
        });
    }

    public static JsonElement Args(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return doc.RootElement.Clone();
    }

    public static string SerializeProtocol(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static JsonElement? ToElement(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.Clone();
        }

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return doc.RootElement.Clone();
    }

    private static JsonElement? BuildDeniedRuntimePreviewResult(
        IReadOnlyList<VisionAgentToolTrace> toolTrace,
        VisionAgentToolContext context)
    {
        var trace = toolTrace.LastOrDefault(item => RuntimePreviewPermissionGate.IsRuntimePreviewTool(item.ToolName));
        if (trace == null)
        {
            return null;
        }

        return Args(new
        {
            previewReady = false,
            toolName = trace.ToolName,
            runtimePreviewConsent = context.RuntimePreviewConsent,
            errorCode = trace.ErrorCode,
            errorMessage = trace.ErrorMessage,
            artifacts = Array.Empty<object>(),
            warnings = new object[]
            {
                new
                {
                    code = trace.ErrorCode,
                    message = trace.ErrorMessage
                }
            },
            blockingIssues = Array.Empty<object>(),
            capturedRealFrame = false,
            loadedModelFiles = false,
            accessedHardware = false,
            stationTouched = false,
            binaryIncluded = false
        });
    }

    private static JsonElement? BuildDeniedResult(
        IReadOnlyList<VisionAgentToolTrace> toolTrace,
        string toolName)
    {
        var trace = toolTrace.LastOrDefault(item =>
            string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
        if (trace == null || trace.Success)
        {
            return null;
        }

        return Args(new
        {
            success = false,
            toolName = trace.ToolName,
            errorCode = trace.ErrorCode,
            errorMessage = trace.ErrorMessage,
            workflowDraftAllowed = false,
            readyForDeployment = false
        });
    }

    private static bool ReadWorkflowDraftAllowed(string? finalContent, JsonElement? actualPrecheck)
    {
        if (!string.IsNullOrWhiteSpace(finalContent))
        {
            try
            {
                using var doc = JsonDocument.Parse(finalContent);
                var finalValue = ReadBool(doc.RootElement, "workflowDraftAllowed");
                if (finalValue.HasValue)
                {
                    return finalValue.Value;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return ReadBool(actualPrecheck, "workflowDraftAllowed") == true;
    }

    private static bool? ReadBool(JsonElement? element, string propertyName)
    {
        if (element == null)
        {
            return null;
        }

        return ReadBool(element.Value, propertyName);
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return null;
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.Value.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static double Rate(int numerator, int denominator)
    {
        return denominator == 0
            ? 0
            : Math.Round((double)numerator / denominator, 4);
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string SerializeFlow(PlannerFlow flow)
    {
        return JsonSerializer.Serialize(flow, JsonOptions);
    }

    public static string RepoRelative(FileInfo file)
    {
        var fullPath = Path.GetFullPath(file.FullName);
        var root = FindRepoRoot(Directory.GetCurrentDirectory());
        return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
    }

    private static string FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "quality")) &&
                Directory.Exists(Path.Combine(directory.FullName, "ClearVision.Product")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(start);
    }

    private static IReadOnlySet<VisionAgentToolPermission> StandardPermissions()
    {
        return new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation,
            VisionAgentToolPermission.DeploymentPrepare
        };
    }

    private static IReadOnlySet<VisionAgentToolPermission> FullPermissions()
    {
        return new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation,
            VisionAgentToolPermission.RuntimePreview,
            VisionAgentToolPermission.DeploymentPrepare
        };
    }

    private static IReadOnlySet<VisionAgentToolPermission> PermissionsWithoutRuntimePreview()
    {
        return new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation,
            VisionAgentToolPermission.DeploymentPrepare
        };
    }

    private static IReadOnlySet<VisionAgentToolPermission> PermissionsWithoutDeploymentPrepare()
    {
        return new HashSet<VisionAgentToolPermission>
        {
            VisionAgentToolPermission.ReadOnly,
            VisionAgentToolPermission.Simulation,
            VisionAgentToolPermission.RuntimePreview
        };
    }

    private static PlannerFlow ValidWireFlow(string outputChannelId = "qa-wire")
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-wire")),
                Op("op_roi", "RoiManager", ("RoiName", "terminal_strip")),
                Op("op_detect", "DeepLearning", ("ModelId", "mock-wire-sequence-model")),
                Op("op_judge", "ResultJudgment", ("Rule", "wire_order_matches_expected")),
                Op("op_out", "ResultOutput", ("OutputChannelId", outputChannelId))
            ],
            [
                Link("op_cam", "Image", "op_roi", "Image"),
                Link("op_roi", "RoiImage", "op_detect", "Image"),
                Link("op_detect", "Detections", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow MissingCameraWireFlow()
    {
        var flow = ValidWireFlow();
        return flow with
        {
            Operators = flow.Operators.Select(op =>
                op.TempId == "op_cam"
                    ? Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"))
                    : op).ToList()
        };
    }

    private static PlannerFlow ValidTemplateFlow(
        string? minScore = null,
        string? templateId = null)
    {
        var templateParams = new List<(string Key, string Value)>();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            templateParams.Add(("TemplatePath", "mock://templates/bracket-a.template"));
        }
        else
        {
            templateParams.Add(("TemplateId", templateId));
        }

        if (!string.IsNullOrWhiteSpace(minScore))
        {
            templateParams.Add(("MinScore", minScore));
        }

        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-template")),
                Op("op_match", "TemplateMatching", templateParams.ToArray()),
                Op("op_judge", "ResultJudgment", ("MinScore", minScore ?? "0.82")),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-template"))
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow MissingTemplateFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-template")),
                Op("op_match", "TemplateMatching"),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-template"))
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow MissingModelFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-model")),
                Op("op_detect", "DeepLearning"),
                Op("op_judge", "ResultJudgment"),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-model"))
            ],
            [
                Link("op_cam", "Image", "op_detect", "Image"),
                Link("op_detect", "Detections", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow ValidModelIdFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-model")),
                Op("op_detect", "DeepLearning", ("ModelId", "mock-model-catalog-item")),
                Op("op_judge", "ResultJudgment"),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-model"))
            ],
            [
                Link("op_cam", "Image", "op_detect", "Image"),
                Link("op_detect", "Detections", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow ValidTemplateAndModelFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-combo")),
                Op("op_match", "TemplateMatching", ("TemplateId", "mock-template-combo")),
                Op("op_detect", "DeepLearning", ("ModelId", "mock-combo-model")),
                Op("op_judge", "ResultJudgment"),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-combo"))
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_cam", "Image", "op_detect", "Image"),
                Link("op_match", "Score", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow ValidHoleFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-hole")),
                Op("op_circle_a", "CircleMeasurement", ("Roi", "hole_a")),
                Op("op_circle_b", "CircleMeasurement", ("Roi", "hole_b")),
                Op("op_distance", "MeasureDistance", ("Unit", "mm"), ("Tolerance", "+/-0.05")),
                Op("op_judge", "ResultJudgment", ("Tolerance", "+/-0.05")),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-hole"))
            ],
            [
                Link("op_cam", "Image", "op_circle_a", "Image"),
                Link("op_cam", "Image", "op_circle_b", "Image"),
                Link("op_circle_a", "Center", "op_distance", "PointA"),
                Link("op_circle_b", "Center", "op_distance", "PointB"),
                Link("op_distance", "Distance", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ],
            "op_cam");
    }

    private static PlannerFlow Flow(
        IReadOnlyList<PlannerOperator> operators,
        IReadOnlyList<PlannerConnection> connections,
        string? entryOperatorTempId)
    {
        return new PlannerFlow(operators, connections, entryOperatorTempId);
    }

    private static PlannerOperator Op(
        string tempId,
        string operatorType,
        params (string Key, string Value)[] parameters)
    {
        return new PlannerOperator(
            tempId,
            operatorType,
            parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
    }

    private static PlannerConnection Link(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new PlannerConnection(sourceTempId, sourcePortName, targetTempId, targetPortName);
    }
}

internal sealed class RecordingPlannerCompletionSource : IVisionAgentPlannerCompletionSource
{
    private readonly PlannerBenchmarkCase _case;
    private readonly ToolExecutionRecorder _recorder;
    private readonly VisionAgentProtocolParser _parser;
    private readonly AgentToolCallPolicy _policy;
    private int _nextStepIndex;
    private int _round;

    public RecordingPlannerCompletionSource(
        PlannerBenchmarkCase testCase,
        ToolExecutionRecorder recorder,
        VisionAgentProtocolParser parser,
        AgentToolCallPolicy policy)
    {
        _case = testCase;
        _recorder = recorder;
        _parser = parser;
        _policy = policy;
    }

    public List<PlannerMessageSnapshot> PlannerMessages { get; } = new();
    public List<PlannedToolCall> PlannedToolCalls { get; } = new();
    public List<PlannerPolicyDecision> PolicyDecisions { get; } = new();

    public Task<string> CompleteAsync(
        AgentPlannerCompletionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _round++;
        CapturePlannerMessages(request, _round);

        string completion;
        if (!string.IsNullOrWhiteSpace(_case.RepeatToolName))
        {
            completion = VisionAgentPlannerAutonomyBenchmark.ToolCallProtocol(
                $"call_{_case.CaseId}_{_round}",
                _case.RepeatToolName!,
                VisionAgentPlannerAutonomyBenchmark.Args(new { keyword = _case.Category, topN = 3 }));
        }
        else if (_nextStepIndex < _case.Steps.Count)
        {
            var step = _case.Steps[_nextStepIndex++];
            if (step.IsTool)
            {
                var state = new PlannerCaseRuntimeState(_case, _recorder);
                completion = VisionAgentPlannerAutonomyBenchmark.ToolCallProtocol(
                    $"call_{_case.CaseId}_{_nextStepIndex}",
                    step.ToolName!,
                    step.ArgumentsFactory!(state));
            }
            else
            {
                var state = new PlannerCaseRuntimeState(_case, _recorder);
                completion = step.FinalFactory!(state);
            }
        }
        else
        {
            completion = VisionAgentPlannerAutonomyBenchmark.SerializeProtocol(new
            {
                kind = "final",
                finalResponse = "planner completed",
                workflowDraftAllowed = true
            });
        }

        RecordPolicy(request, completion, _round);
        return Task.FromResult(completion);
    }

    private void CapturePlannerMessages(
        AgentPlannerCompletionRequest request,
        int round)
    {
        PlannerMessages.Add(new PlannerMessageSnapshot(
            round,
            "planner_prompt",
            Truncate(request.PlannerPrompt)));
        foreach (var message in request.Messages)
        {
            PlannerMessages.Add(new PlannerMessageSnapshot(
                round,
                message.Role,
                Truncate(message.Content)));
        }
    }

    private void RecordPolicy(
        AgentPlannerCompletionRequest request,
        string completion,
        int round)
    {
        var parsed = _parser.Parse(completion);
        if (!parsed.IsToolCall)
        {
            PolicyDecisions.Add(new PlannerPolicyDecision(
                "planner_final",
                round,
                string.Empty,
                true,
                null,
                null));
            return;
        }

        var runtimePreviewConsent = RuntimePreviewPermissionGate.HasConsent(request.GenerationRequest);
        foreach (var call in parsed.ToolCalls)
        {
            PlannedToolCalls.Add(new PlannedToolCall(
                round,
                call.Id,
                call.Name,
                call.Arguments.Clone()));
            var decision = _policy.ValidateToolName(call.Name, runtimePreviewConsent);
            PolicyDecisions.Add(new PlannerPolicyDecision(
                "planner_policy",
                round,
                call.Name,
                decision.Allowed,
                decision.ErrorCode,
                decision.ErrorMessage));
        }
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 900
            ? value
            : value[..900] + "...";
    }
}

internal sealed class ToolExecutionRecorder
{
    private readonly List<RecordedToolExecution> _executions = new();
    private readonly object _lock = new();

    public void Add(RecordedToolExecution execution)
    {
        lock (_lock)
        {
            _executions.Add(execution);
        }
    }

    public JsonElement? LastData(string toolName)
    {
        lock (_lock)
        {
            return _executions
                .Where(item => string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Data)
                .LastOrDefault(item => item != null);
        }
    }
}

internal sealed class RecordingTool : IVisionAgentTool
{
    private readonly IVisionAgentTool _inner;
    private readonly ToolExecutionRecorder _recorder;

    public RecordingTool(IVisionAgentTool inner, ToolExecutionRecorder recorder)
    {
        _inner = inner;
        _recorder = recorder;
    }

    public string Name => _inner.Name;
    public string DisplayName => _inner.DisplayName;
    public string Description => _inner.Description;
    public string Category => _inner.Category;
    public VisionAgentToolPermission Permission => _inner.Permission;
    public JsonElement ParametersSchema => _inner.ParametersSchema;

    public async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var result = await _inner.ExecuteAsync(context, arguments, cancellationToken);
        _recorder.Add(new RecordedToolExecution(
            _inner.Name,
            _inner.Permission.ToString(),
            result.Success,
            result.ErrorCode,
            result.ErrorMessage,
            VisionAgentPlannerAutonomyBenchmark.ToElement(result.Data),
            arguments.Clone()));
        return result;
    }
}

internal sealed record PlannerBenchmarkRunnerOptions(FileInfo Output, FileInfo Report)
{
    public static PlannerBenchmarkRunnerOptions Parse(string[] args)
    {
        var output = Path.Combine("quality", "evals", "reports", "planner_autonomy_benchmark.json");
        var report = Path.Combine("quality", "evals", "reports", "planner_autonomy_benchmark.md");
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                output = args[++i];
            }
            else if (string.Equals(args[i], "--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                report = args[++i];
            }
        }

        return new PlannerBenchmarkRunnerOptions(
            new FileInfo(Path.GetFullPath(output)),
            new FileInfo(Path.GetFullPath(report)));
    }
}

internal sealed record PlannerBenchmarkCase
{
    public string CaseId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public string UserRequest { get; init; } = string.Empty;
    public PlannerFlow Flow { get; init; } = new([], [], null);
    public PlannerFlow? ExistingFlow { get; init; }
    public string? EntryOperatorTempId => Flow.EntryOperatorTempId;
    public string? TargetStationId { get; init; }
    public string CameraBindingId { get; init; } = "mock-camera-binding";
    public IReadOnlyList<string> ExpectedBusinessActions { get; init; } = [];
    public IReadOnlyList<PlannerStep> Steps { get; init; } = [];
    public bool RuntimePreviewConsent { get; init; }
    public IReadOnlySet<VisionAgentToolPermission> AllowedPermissions { get; init; } =
        new HashSet<VisionAgentToolPermission>();
    public IReadOnlyList<string> ExpectedActualTools { get; init; } = [];
    public IReadOnlyList<string> ExpectedPolicyErrorCodes { get; init; } = [];
    public IReadOnlyList<string> ExpectedPendingActionTypes { get; init; } = [];
    public string? RepeatToolName { get; init; }
    public int MaxToolRounds { get; init; } = 10;
    public bool? ExpectedLoopSuccess { get; init; } = true;
    public string? ExpectedFailureType { get; init; }
    public bool? ExpectedValidationValid { get; init; } = true;
    public bool? ExpectedDryRunSucceeded { get; init; } = true;
    public bool? ExpectedPrecheckReady { get; init; } = true;
    public bool? ExpectedRuntimePreviewReady { get; init; }
    public bool? ExpectedWorkflowDraftAllowed { get; init; } = true;
    public bool RequireDeniedTrace { get; init; }
}

internal sealed record PlannerStep(
    string? ToolName,
    Func<PlannerCaseRuntimeState, JsonElement>? ArgumentsFactory,
    Func<PlannerCaseRuntimeState, string>? FinalFactory)
{
    public bool IsTool => !string.IsNullOrWhiteSpace(ToolName);

    public static PlannerStep Tool(
        string toolName,
        Func<PlannerCaseRuntimeState, JsonElement> argumentsFactory) =>
        new(toolName, argumentsFactory, null);

    public static PlannerStep Final(Func<PlannerCaseRuntimeState, string> finalFactory) =>
        new(null, null, finalFactory);
}

internal sealed record PlannerCaseRuntimeState(
    PlannerBenchmarkCase Case,
    ToolExecutionRecorder Recorder);

internal sealed record PlannerFlow(
    IReadOnlyList<PlannerOperator> Operators,
    IReadOnlyList<PlannerConnection> Connections,
    string? EntryOperatorTempId);

internal sealed record PlannerOperator(
    string TempId,
    string OperatorType,
    IReadOnlyDictionary<string, string> Parameters);

internal sealed record PlannerConnection(
    string SourceTempId,
    string SourcePortName,
    string TargetTempId,
    string TargetPortName);

internal sealed record PlannerBenchmarkDocument(
    string SchemaVersion,
    string BenchmarkId,
    string GeneratedAtUtc,
    string Mode,
    PlannerBenchmarkSummary Summary,
    IReadOnlyDictionary<string, double> Metrics,
    PlannerBenchmarkSafety Safety,
    IReadOnlyList<PlannerBenchmarkCaseResult> Cases,
    IReadOnlyList<PlannerBenchmarkCaseResult> PermissionNegativeCases);

internal sealed record PlannerBenchmarkSummary(
    int PlannerCaseCount,
    int PermissionNegativeCaseCount,
    int TotalCaseCount,
    int PassedCaseCount,
    bool Accepted);

internal sealed record PlannerBenchmarkSafety(
    bool RealCameraSdkTouched,
    bool RealStationTouched,
    bool RealImageFilesRead,
    bool RealModelFilesLoaded,
    bool PlcWriteAttempted,
    bool PackageCreated,
    bool HotLoadAttempted,
    string RuntimePreviewMode,
    IReadOnlyList<string> Violations);

internal sealed record PlannerBenchmarkCaseResult(
    string CaseId,
    string Category,
    string TaskType,
    string UserRequest,
    string? ExistingFlowJson,
    IReadOnlyList<string> ExpectedBusinessActions,
    IReadOnlyList<string> AllowedTools,
    PlannerContextSnapshot Context,
    IReadOnlyList<PlannerMessageSnapshot> PlannerMessages,
    IReadOnlyList<PlannedToolCall> PlannedToolCalls,
    IReadOnlyList<PlannerPolicyDecision> PolicyDecisions,
    IReadOnlyList<PlannerToolCallResult> ActualToolCalls,
    JsonElement? ActualValidationResult,
    JsonElement? ActualDryRunResult,
    JsonElement? ActualPrecheckResult,
    JsonElement? ActualRuntimePreviewResult,
    IReadOnlyList<VisionAgentToolTrace> ToolTrace,
    IReadOnlyList<VisionAgentPendingAction> PendingActions,
    string FinalContent,
    bool FinalWorkflowDraftAllowed,
    bool LoopSuccess,
    string? FailureType,
    string? ErrorCode,
    string? ErrorMessage,
    bool Passed,
    IReadOnlyList<string> Failures);

internal sealed record PlannerContextSnapshot(
    bool RuntimePreviewConsent,
    IReadOnlyList<string> AllowedPermissions);

internal sealed record PlannerMessageSnapshot(
    int Round,
    string Role,
    string Content);

internal sealed record PlannedToolCall(
    int Round,
    string Id,
    string ToolName,
    JsonElement Arguments);

internal sealed record PlannerPolicyDecision(
    string Stage,
    int Round,
    string ToolName,
    bool Allowed,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record PlannerToolCallResult(
    string ToolName,
    string Permission,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record RecordedToolExecution(
    string ToolName,
    string Permission,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement? Data,
    JsonElement Arguments);

internal static class VisionAgentPlannerAutonomyBenchmarkMarkdown
{
    public static string Create(PlannerBenchmarkDocument document, FileInfo jsonPath)
    {
        var lines = new List<string>
        {
            "# Vision Agent Planner Autonomy Benchmark",
            "",
            $"- Benchmark: `{document.BenchmarkId}`",
            $"- Generated UTC: `{document.GeneratedAtUtc}`",
            $"- Mode: `{document.Mode}`",
            $"- Planner cases: {document.Summary.PlannerCaseCount}",
            $"- Permission negative cases: {document.Summary.PermissionNegativeCaseCount}",
            $"- Accepted: {document.Summary.Accepted}",
            $"- JSON: `{VisionAgentPlannerAutonomyBenchmark.RepoRelative(jsonPath)}`",
            "",
            "## Executable Design",
            "",
            "- Keeps the existing executable toolchain benchmark unchanged.",
            "- Adds a planner-autonomy path with mock planner completions, `VisionAgentPlannerService`, `VisionAgentLoop`, and `AgentToolCallPolicy`.",
            "- The runner never calls an external model; mock completions emit the same `tool_call` / final protocol that the loop parses.",
            "- Registered tools remain static/offline only: read-only catalog tools, structure validation, structure dryrun, runtime package precheck, and offline RuntimePreview stubs.",
            "",
            "## Field Contract",
            "",
            "- `expectedBusinessActions`: business expectations that are not tool names.",
            "- `allowedTools`: policy-provided names visible to the mock planner.",
            "- `plannedToolCalls`: tool calls selected by the mock planner protocol.",
            "- `policyDecisions`: planner-policy and execution-permission decisions.",
            "- `actualToolCalls`: loop trace of tools that executed or were denied.",
            "- `actualValidationResult`, `actualDryRunResult`, `actualPrecheckResult`, `actualRuntimePreviewResult`: actual tool outputs or deterministic denial payloads.",
            "- `finalWorkflowDraftAllowed`: final draft permission from the planner response or precheck result.",
            "",
            "## Planner Autonomy Cases",
            "",
            "| Case | Type | Planned Tools | Actual Tools | Draft Allowed | Passed |",
            "| --- | --- | --- | --- | --- | --- |"
        };

        foreach (var result in document.Cases)
        {
            lines.Add(Row(result));
        }

        lines.AddRange(
        [
            "",
            "## Permission Negative Cases",
            "",
            "| Case | Type | Denials | Pending Actions | Draft Allowed | Passed |",
            "| --- | --- | --- | --- | --- | --- |"
        ]);

        foreach (var result in document.PermissionNegativeCases)
        {
            lines.Add(
                "| " +
                string.Join(" | ", [
                    result.CaseId,
                    result.TaskType,
                    string.Join(", ", result.PolicyDecisions
                        .Where(item => !item.Allowed)
                        .Select(item => item.ErrorCode)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)),
                    string.Join(", ", result.PendingActions
                        .Select(item => item.ActionType)
                        .Distinct(StringComparer.OrdinalIgnoreCase)),
                    result.FinalWorkflowDraftAllowed.ToString(),
                    result.Passed.ToString()
                ]) +
                " |");
        }

        lines.AddRange(
        [
            "",
            "## Safety",
            "",
            "- No real camera SDK, real Station access, image file read, model file load, PLC write, package creation, deployment, or hot load is used.",
            $"- RuntimePreview mode: `{document.Safety.RuntimePreviewMode}`",
            $"- Safety violations: {(document.Safety.Violations.Count == 0 ? "none" : string.Join(", ", document.Safety.Violations))}",
            ""
        ]);

        return string.Join(Environment.NewLine, lines);
    }

    private static string Row(PlannerBenchmarkCaseResult result)
    {
        return "| " +
               string.Join(" | ", [
                   result.CaseId,
                   result.TaskType,
                   string.Join(", ", result.PlannedToolCalls.Select(item => item.ToolName)),
                   string.Join(", ", result.ActualToolCalls.Select(item => item.ToolName)),
                   result.FinalWorkflowDraftAllowed.ToString(),
                   result.Passed.ToString()
               ]) +
               " |";
    }
}
