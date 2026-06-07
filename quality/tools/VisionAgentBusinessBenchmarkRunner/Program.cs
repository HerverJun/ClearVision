using System.Text.Encodings.Web;
using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;

var options = RunnerOptions.Parse(args);
var result = await VisionAgentBusinessBenchmark.RunAsync(options, CancellationToken.None);
options.Output.Directory?.Create();
options.Report.Directory?.Create();
File.WriteAllText(options.Output.FullName, JsonSerializer.Serialize(result, VisionAgentBusinessBenchmark.JsonOptions) + Environment.NewLine);
File.WriteAllText(options.Report.FullName, VisionAgentBusinessBenchmarkMarkdown.Create(result, options.Output), System.Text.Encoding.UTF8);
Console.WriteLine($"wrote {VisionAgentBusinessBenchmark.RepoRelative(options.Output)}");
Console.WriteLine($"wrote {VisionAgentBusinessBenchmark.RepoRelative(options.Report)}");
return result.Summary.Accepted ? 0 : 1;

internal static class VisionAgentBusinessBenchmark
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, double> Thresholds =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["generationSuccessRate"] = 0.95,
            ["structuralValidationPassRate"] = 0.90,
            ["dryRunPassRate"] = 0.85,
            ["previewReadyRate"] = 0.70,
            ["parameterCompletionRate"] = 0.70,
            ["userApplicableRate"] = 0.90
        };

    public static async Task<BenchmarkDocument> RunAsync(
        RunnerOptions options,
        CancellationToken cancellationToken)
    {
        var registry = CreateRegistry();
        var knownToolNames = registry.ListTools()
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cases = CreateCases();
        var invalidToolCases = cases
            .SelectMany(item => item.ExpectedToolCalls
                .Where(tool => !knownToolNames.Contains(tool))
                .Select(tool => $"{item.CaseId}:{tool}"))
            .ToList();
        if (invalidToolCases.Count > 0)
        {
            throw new InvalidOperationException(
                "Benchmark expectedToolCalls contains unregistered tools: " +
                string.Join(", ", invalidToolCases));
        }

        var results = new List<ExecutableBenchmarkCaseResult>();
        foreach (var benchmarkCase in cases)
        {
            results.Add(await RunCaseAsync(registry, benchmarkCase, cancellationToken));
        }

        var metrics = BuildMetrics(results);
        var thresholdResults = Thresholds.ToDictionary(
            item => item.Key,
            item => new BenchmarkThresholdResult(
                metrics[item.Key],
                item.Value,
                metrics[item.Key] >= item.Value),
            StringComparer.OrdinalIgnoreCase);
        var safety = BuildSafety(results);
        var accepted = results.All(item => item.Passed) &&
                       thresholdResults.Values.All(item => item.Passed) &&
                       safety.Violations.Count == 0;
        var workflowRun = VisionAgentWorkflowRunMetadata.FromEnvironment();

        return new BenchmarkDocument(
            "2026-06-05.vision-agent-executable-business-benchmark.v1",
            "vision_agent_executable_business_benchmark",
            workflowRun.GeneratedAtUtc,
            "offline_metadata_only",
            workflowRun,
            new BenchmarkSummary(
                results.Count,
                results.Count(item => item.ActualRuntimePreviewResult != null),
                results.Count(item => item.Passed),
                accepted),
            metrics,
            thresholdResults,
            results
                .GroupBy(item => item.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count()),
            results
                .GroupBy(item => item.TaskType)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count()),
            safety,
            results);
    }

    private static async Task<ExecutableBenchmarkCaseResult> RunCaseAsync(
        VisionAgentToolRegistry registry,
        BenchmarkCase benchmarkCase,
        CancellationToken cancellationToken)
    {
        var actualToolCalls = new List<BenchmarkToolCallResult>();
        VisionAgentToolResult? validationResult = null;
        VisionAgentToolResult? dryRunResult = null;
        VisionAgentToolResult? precheckResult = null;
        VisionAgentToolResult? runtimePreviewResult = null;
        VisionAgentToolResult? captureResult = null;

        var context = new VisionAgentToolContext
        {
            UserDescription = benchmarkCase.UserRequest,
            ExistingFlowJson = benchmarkCase.ExistingFlow == null ? null : SerializeFlow(benchmarkCase.ExistingFlow),
            RuntimePreviewConsent = benchmarkCase.ExpectedToolCalls.Any(RuntimePreviewPermissionGate.IsRuntimePreviewTool) ||
                                    benchmarkCase.ExpectedToolCalls.Any(toolName =>
                                        string.Equals(toolName, RuntimePreviewSimulateMetadataSessionTool.ToolName, StringComparison.OrdinalIgnoreCase)),
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.RuntimePreview,
                VisionAgentToolPermission.DeploymentPrepare
            }
        };

        foreach (var toolName in benchmarkCase.ExpectedToolCalls)
        {
            var arguments = BuildArguments(
                benchmarkCase,
                toolName,
                validationResult,
                dryRunResult,
                captureResult);
            var result = await registry.ExecuteAsync(toolName, context, arguments, cancellationToken);
            actualToolCalls.Add(new BenchmarkToolCallResult(
                toolName,
                ToolPermission(registry, toolName),
                result.Success,
                result.ErrorCode,
                result.ErrorMessage));

            if (string.Equals(toolName, "validate_flow", StringComparison.OrdinalIgnoreCase))
            {
                validationResult = result;
            }
            else if (string.Equals(toolName, "dryrun_flow", StringComparison.OrdinalIgnoreCase))
            {
                dryRunResult = result;
            }
            else if (string.Equals(toolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
            {
                precheckResult = result;
            }
            else if (string.Equals(toolName, RuntimePreviewPermissionGate.CaptureToolName, StringComparison.OrdinalIgnoreCase))
            {
                captureResult = result;
                runtimePreviewResult = result;
            }
            else if (string.Equals(toolName, RuntimePreviewPermissionGate.ReplayToolName, StringComparison.OrdinalIgnoreCase))
            {
                runtimePreviewResult = result;
            }
            else if (string.Equals(toolName, RuntimePreviewSimulateMetadataSessionTool.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                runtimePreviewResult = result;
            }
        }

        var actualValidation = ToElement(validationResult?.Data);
        var actualDryRun = ToElement(dryRunResult?.Data);
        var actualPrecheck = ToElement(precheckResult?.Data);
        var actualRuntimePreview = ToElement(runtimePreviewResult?.Data);
        var actualToolNames = actualToolCalls.Select(item => item.ToolName).ToList();
        var actualCaseMetrics = new CaseExecutionMetrics(
            GenerationSucceeded: validationResult?.Success == true && benchmarkCase.Flow.Operators.Count > 0,
            StructuralValidationPassed: ReadBool(actualValidation, "isValid") == true,
            DryRunPassed: ReadBool(actualDryRun, "dryRunSucceeded") == true,
            PreviewReady: ReadBool(actualRuntimePreview, "previewReady") == true,
            ParametersComplete: MissingResourceCount(actualValidation) == 0 && MissingResourceCount(actualPrecheck) == 0,
            UserApplicable: validationResult?.Success == true &&
                ReadBool(actualValidation, "isValid") == true &&
                (actualPrecheck == null || ReadBool(actualPrecheck, "workflowDraftAllowed") != false));

        var failures = new List<string>();
        if (!actualToolNames.SequenceEqual(benchmarkCase.ExpectedToolCalls, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("actualToolCalls did not match expectedToolCalls.");
        }

        if (benchmarkCase.ExpectedStructurallyValid != actualCaseMetrics.StructuralValidationPassed)
        {
            failures.Add($"structuralValidation expected {benchmarkCase.ExpectedStructurallyValid}.");
        }

        if (benchmarkCase.ExpectsDryRun && benchmarkCase.ExpectedDryRunSucceeded != actualCaseMetrics.DryRunPassed)
        {
            failures.Add($"dryRun expected {benchmarkCase.ExpectedDryRunSucceeded}.");
        }

        if (benchmarkCase.ExpectedPrecheckReady.HasValue &&
            benchmarkCase.ExpectedPrecheckReady.Value != (ReadBool(actualPrecheck, "readyForDeployment") == true))
        {
            failures.Add($"precheckReady expected {benchmarkCase.ExpectedPrecheckReady.Value}.");
        }

        if (benchmarkCase.ExpectedRuntimePreviewReady.HasValue &&
            benchmarkCase.ExpectedRuntimePreviewReady.Value != actualCaseMetrics.PreviewReady)
        {
            failures.Add($"runtimePreviewReady expected {benchmarkCase.ExpectedRuntimePreviewReady.Value}.");
        }

        return new ExecutableBenchmarkCaseResult(
            benchmarkCase.CaseId,
            benchmarkCase.Category,
            benchmarkCase.TaskType,
            benchmarkCase.UserRequest,
            benchmarkCase.ExpectedBusinessActions,
            benchmarkCase.ExpectedToolCalls,
            actualToolCalls,
            actualValidation,
            actualDryRun,
            actualPrecheck,
            actualRuntimePreview,
            actualCaseMetrics,
            failures.Count == 0,
            failures);
    }

    private static JsonElement BuildArguments(
        BenchmarkCase benchmarkCase,
        string toolName,
        VisionAgentToolResult? validationResult,
        VisionAgentToolResult? dryRunResult,
        VisionAgentToolResult? captureResult)
    {
        if (string.Equals(toolName, "match_flow_template", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new { request = benchmarkCase.UserRequest, topN = 3 });
        }

        if (string.Equals(toolName, "get_flow_template_skeleton", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new { templateId = benchmarkCase.TemplateId, scenarioKey = benchmarkCase.TemplateScenarioKey });
        }

        if (string.Equals(toolName, "list_operator_catalog", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new { keyword = benchmarkCase.Category, topN = 10 });
        }

        if (string.Equals(toolName, "get_operator_schema", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new { operatorType = benchmarkCase.SchemaOperatorType });
        }

        if (string.Equals(toolName, "retrieve_operator_knowledge", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new { keyword = benchmarkCase.Category, topN = 5 });
        }

        if (string.Equals(toolName, "inspect_current_flow", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new
            {
                existingFlowJson = benchmarkCase.ExistingFlow == null
                    ? SerializeFlow(benchmarkCase.Flow)
                    : SerializeFlow(benchmarkCase.ExistingFlow)
            });
        }

        if (string.Equals(toolName, "validate_flow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, "dryrun_flow", StringComparison.OrdinalIgnoreCase))
        {
            return Args(new
            {
                flow = benchmarkCase.Flow,
                entryOperatorTempId = benchmarkCase.EntryOperatorTempId
            });
        }

        if (string.Equals(toolName, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
        {
            var payload = new Dictionary<string, object?>
            {
                ["flow"] = benchmarkCase.Flow,
                ["validationSummary"] = validationResult?.Data,
                ["targetStationId"] = benchmarkCase.TargetStationId
            };
            if (dryRunResult?.Data != null)
            {
                payload["dryRunSummary"] = dryRunResult.Data;
            }

            return Args(payload);
        }

        if (string.Equals(toolName, RuntimePreviewPermissionGate.CaptureToolName, StringComparison.OrdinalIgnoreCase))
        {
            return Args(new
            {
                cameraBindingId = benchmarkCase.CameraBindingId,
                operatorTempId = benchmarkCase.EntryOperatorTempId ?? "op_cam",
                reason = "executable benchmark offline metadata"
            });
        }

        if (string.Equals(toolName, RuntimePreviewPermissionGate.ReplayToolName, StringComparison.OrdinalIgnoreCase))
        {
            var captureData = ToElement(captureResult?.Data);
            return Args(new
            {
                flow = benchmarkCase.Flow,
                frameId = ReadString(captureData, "frameId") ?? "offline-frame-benchmark",
                entryOperatorTempId = benchmarkCase.EntryOperatorTempId
            });
        }

        if (string.Equals(toolName, RuntimePreviewSimulateMetadataSessionTool.ToolName, StringComparison.OrdinalIgnoreCase))
        {
            return Args(new
            {
                flow = benchmarkCase.Flow,
                runtimePreviewConsent = true,
                config = new
                {
                    enabled = true,
                    mode = RuntimePreviewModes.MetadataOnly,
                    allowedCameraBindingIds = new[] { benchmarkCase.CameraBindingId },
                    allowedTemplateIds = new[] { "template-a", "catalog-template-a", "catalog-template-b" },
                    allowedModelIds = new[] { "model-a", "model-catalog-a" },
                    allowedFlowIds = Array.Empty<string>(),
                    allowedResourceRoots = Array.Empty<string>(),
                    fallbackToOffline = true,
                    denyExternalPath = true,
                    denyImageBytes = true
                }
            });
        }

        return Args(new { });
    }

    private static IReadOnlyDictionary<string, double> BuildMetrics(
        IReadOnlyList<ExecutableBenchmarkCaseResult> results)
    {
        var previewCases = results.Where(item => item.ActualRuntimePreviewResult != null).ToList();
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["generationSuccessRate"] = Rate(results.Count(item => item.Metrics.GenerationSucceeded), results.Count),
            ["structuralValidationPassRate"] = Rate(results.Count(item => item.Metrics.StructuralValidationPassed), results.Count),
            ["dryRunPassRate"] = Rate(results.Count(item => item.Metrics.DryRunPassed), results.Count),
            ["previewReadyRate"] = Rate(previewCases.Count(item => item.Metrics.PreviewReady), previewCases.Count),
            ["parameterCompletionRate"] = Rate(results.Count(item => item.Metrics.ParametersComplete), results.Count),
            ["userApplicableRate"] = Rate(results.Count(item => item.Metrics.UserApplicable), results.Count)
        };
    }

    private static BenchmarkSafety BuildSafety(IReadOnlyList<ExecutableBenchmarkCaseResult> results)
    {
        var runtimePreviewResults = results
            .Select(item => item.ActualRuntimePreviewResult)
            .Where(item => item != null)
            .Cast<JsonElement>()
            .ToList();
        var violations = new List<string>();
        foreach (var preview in runtimePreviewResults)
        {
            if (ReadBool(preview, "capturedRealFrame") == true) violations.Add("captured_real_frame");
            if (ReadBool(preview, "loadedModelFiles") == true) violations.Add("loaded_model_files");
            if (ReadBool(preview, "accessedHardware") == true) violations.Add("accessed_hardware");
            if (ReadBool(preview, "stationTouched") == true) violations.Add("station_touched");
            if (ReadBool(preview, "binaryIncluded") == true) violations.Add("binary_included");
        }

        return new BenchmarkSafety(
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

    private static VisionAgentToolRegistry CreateRegistry()
    {
        return new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new OperatorSchemaTool(),
            new OperatorKnowledgeTool(),
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new CurrentFlowInspectTool(),
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePreviewSimulateMetadataSessionTool(),
            new RuntimePreviewCaptureStubTool(),
            new RuntimePreviewReplayStubTool(),
            new RuntimePackagePrecheckTool(new BenchmarkStationStatusReader())
        ]);
    }

    private static string ToolPermission(VisionAgentToolRegistry registry, string toolName)
    {
        return registry.TryGet(toolName, out var tool) ? tool.Permission.ToString() : string.Empty;
    }

    private static JsonElement Args(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return doc.RootElement.Clone();
    }

    private static JsonElement? ToElement(object? value)
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

    private static bool? ReadBool(JsonElement? element, string propertyName)
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.Value.EnumerateObject())
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

    private static int MissingResourceCount(JsonElement? element)
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var property in element.Value.EnumerateObject())
        {
            if (string.Equals(property.Name, "missingResources", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Array)
            {
                return property.Value.GetArrayLength();
            }
        }

        return 0;
    }

    private static double Rate(int numerator, int denominator)
    {
        return denominator == 0
            ? 0
            : Math.Round((double)numerator / denominator, 4);
    }

    private static string SerializeFlow(BenchmarkFlow flow)
    {
        return JsonSerializer.Serialize(flow, JsonOptions);
    }

    public static string RepoRelative(FileInfo path)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var fullPath = Path.GetFullPath(path.FullName);
        return Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
    }

    private static IReadOnlyList<BenchmarkCase> CreateCases()
    {
        var validateDryrunPrecheck = new[] { "validate_flow", "dryrun_flow", "runtime_package_precheck" };
        var validateDryrunPreview = new[]
        {
            "validate_flow",
            "dryrun_flow",
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewPermissionGate.ReplayToolName
        };

        return
        [
            Case("VA-BM-001", "wire_sequence", "generate", "Generate a terminal wire sequence inspection draft.", ValidWireFlow(), ["select_template", "choose_mock_camera_binding"], ["match_flow_template", "get_flow_template_skeleton", .. validateDryrunPrecheck], templateId: "wire_sequence_inspection", scenarioKey: "wire_sequence"),
            Case("VA-BM-002", "wire_sequence", "modify_existing_flow", "Update the existing wire sequence rule from red-blue-black to red-black-blue.", ValidWireFlow(), ["inspect_existing_flow", "update_judgment_rule"], ["inspect_current_flow", .. validateDryrunPrecheck], existingFlow: ValidWireFlow()),
            Case("VA-BM-003", "wire_sequence", "missing_resource", "Create a wire sequence draft when no camera binding is selected yet.", MissingCameraWireFlow(), ["request_camera_binding", "keep_workflow_draft"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-004", "wire_sequence", "runtime_preview", "Show offline RuntimePreview metadata for the wire sequence draft.", ValidWireFlow(), ["render_runtime_preview_metadata"], validateDryrunPreview, expectedRuntimePreviewReady: true),
            Case("VA-BM-005", "wire_sequence", "parameter_completion", "Fill the wire sequence ResultOutput channel from the engineer selection.", ValidWireFlow(outputChannelId: "qa-wire-result"), ["complete_output_channel"], validateDryrunPrecheck),
            Case("VA-BM-006", "template_matching", "generate", "Generate a bracket alignment flow using template matching.", ValidTemplateFlow(), ["select_template", "review_min_score"], ["match_flow_template", "get_flow_template_skeleton", .. validateDryrunPrecheck], templateId: "template_matching_alignment", scenarioKey: "template_matching"),
            Case("VA-BM-007", "template_matching", "missing_resource", "Prepare a template matching flow before the template has been selected.", MissingTemplateFlow(), ["request_template_source", "keep_workflow_draft"], ["get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "TemplateMatching", expectedPrecheckReady: false),
            Case("VA-BM-008", "template_matching", "parameter_completion", "Review and fill ROI parameters for template matching.", ValidTemplateFlow(useRoi: true), ["complete_roi_parameters"], validateDryrunPrecheck),
            Case("VA-BM-009", "template_matching", "modify_existing_flow", "Raise the template matching minimum score threshold to 0.86.", ValidTemplateFlow(minScore: "0.86"), ["inspect_existing_flow", "update_score_threshold"], ["inspect_current_flow", .. validateDryrunPrecheck], existingFlow: ValidTemplateFlow()),
            Case("VA-BM-010", "template_matching", "runtime_preview", "Show offline RuntimePreview metadata for bracket template matching.", ValidTemplateFlow(), ["render_runtime_preview_metadata"], validateDryrunPreview, expectedRuntimePreviewReady: true),
            Case("VA-BM-011", "hole_distance", "generate", "Generate a hole distance measurement flow with two circle measurements.", ValidHoleFlow(), ["retrieve_measurement_guidance", "review_calibration"], ["retrieve_operator_knowledge", "get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "MeasureDistance"),
            Case("VA-BM-012", "hole_distance", "missing_resource", "Flag missing calibration review for a hole distance measurement draft.", ValidHoleFlow(), ["calibration.review", "keep_metadata_only"], validateDryrunPrecheck),
            Case("VA-BM-013", "hole_distance", "parameter_completion", "Fill both hole ROI names after the engineer selects them.", ValidHoleFlow(roiA: "hole_a", roiB: "hole_b"), ["complete_hole_rois"], validateDryrunPrecheck),
            Case("VA-BM-014", "hole_distance", "modify_existing_flow", "Tighten the hole distance tolerance to plus/minus 0.03 mm.", ValidHoleFlow(tolerance: "+/-0.03"), ["inspect_existing_flow", "update_tolerance"], ["inspect_current_flow", .. validateDryrunPrecheck], existingFlow: ValidHoleFlow()),
            Case("VA-BM-015", "hole_distance", "precheck", "Run static precheck for a hole distance draft and surface deployment warnings.", ValidHoleFlow(), ["runtimePackagePrecheck.review"], validateDryrunPrecheck),
            Case("VA-BM-016", "missing_resources", "missing_resource", "Generate defect detection but leave the DeepLearning model unresolved.", MissingModelFlow(), ["request_model_source", "keep_workflow_draft"], ["retrieve_operator_knowledge", .. validateDryrunPrecheck], expectedPrecheckReady: false),
            Case("VA-BM-017", "missing_resources", "missing_resource", "Generate a flow while CameraBindingId is still pending.", MissingCameraTemplateFlow(), ["request_camera_binding", "keep_workflow_draft"], ["list_operator_catalog", .. validateDryrunPrecheck], expectedPrecheckReady: false),
            Case("VA-BM-018", "missing_resources", "missing_resource", "Surface missing ResultOutput channel before deployment precheck.", MissingOutputFlow(), ["request_output_channel", "keep_workflow_draft"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-019", "missing_resources", "missing_resource", "Surface missing PLC metadata without writing to PLC.", MissingPlcOutputFlow(), ["request_plc_metadata", "metadata_only_plc_review"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-020", "missing_resources", "missing_resource", "Surface missing template resource and keep the flow as a draft.", MissingTemplateFlow(), ["request_template_source", "keep_workflow_draft"], ["get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "TemplateMatching", expectedPrecheckReady: false),
            Case("VA-BM-021", "modify_existing_flow", "modify_existing_flow", "Add a DeepLearning branch to an existing template matching flow.", ValidTemplateAndModelFlow(), ["inspect_existing_flow", "add_model_branch"], ["inspect_current_flow", "get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "DeepLearning", existingFlow: ValidTemplateFlow()),
            Case("VA-BM-022", "modify_existing_flow", "modify_existing_flow", "Replace a template matching operator with a catalog template variant.", ValidTemplateFlow(templateId: "catalog-template-b"), ["inspect_existing_flow", "replace_template_source"], ["inspect_current_flow", .. validateDryrunPrecheck], existingFlow: ValidTemplateFlow()),
            Case("VA-BM-023", "modify_existing_flow", "runtime_preview", "Add RuntimePreview metadata display to the current Agent workbench result.", ValidTemplateFlow(), ["inspect_existing_flow", "render_runtime_preview_metadata"], ["inspect_current_flow", .. validateDryrunPreview], existingFlow: ValidTemplateFlow(), expectedRuntimePreviewReady: true),
            Case("VA-BM-024", "modify_existing_flow", "modify_existing_flow", "Change ResultJudgment thresholds while preserving existing connections.", ValidTemplateFlow(minScore: "0.91"), ["inspect_existing_flow", "preserve_connections"], ["inspect_current_flow", .. validateDryrunPrecheck], existingFlow: ValidTemplateFlow()),
            Case("VA-BM-025", "parameter_completion", "parameter_completion", "Fill ImageAcquisition CameraId from a catalog selection.", ValidTemplateFlow(cameraParameterName: "CameraId"), ["complete_camera_id"], validateDryrunPrecheck),
            Case("VA-BM-026", "parameter_completion", "parameter_completion", "Fill DeepLearning ModelId instead of ModelPath.", ValidModelIdFlow(), ["complete_model_id"], validateDryrunPrecheck),
            Case("VA-BM-027", "parameter_completion", "parameter_completion", "Fill TemplateMatching TemplateId instead of TemplatePath.", ValidTemplateFlow(templateId: "catalog-template-a"), ["complete_template_id"], validateDryrunPrecheck),
            Case("VA-BM-028", "parameter_completion", "parameter_completion", "Fill ResultOutput OutputChannelId and suppress conflicting Channel prompts.", ValidTemplateFlow(outputChannelId: "result-bus"), ["complete_output_channel_id"], validateDryrunPrecheck),
            Case("VA-BM-029", "parameter_completion", "parameter_completion", "Disable ImageAcquisition FilePath when camera source is selected.", ValidTemplateFlow(includeUnusedFilePath: true), ["disable_conflicting_file_path"], validateDryrunPrecheck),
            Case("VA-BM-030", "runtime_preview", "runtime_preview", "Render RuntimePreview metadata for a single-camera flow.", ValidTemplateFlow(), ["render_runtime_preview_metadata"], validateDryrunPreview, expectedRuntimePreviewReady: true),
            Case("VA-BM-031", "runtime_preview", "runtime_preview", "Block RuntimePreview metadata when multiple ImageAcquisition entries need selection.", MultiCameraFlow(), ["entryOperatorTempId.required"], validateDryrunPreview, expectedStructurallyValid: false, expectedDryRunSucceeded: false, expectedRuntimePreviewReady: false),
            Case("VA-BM-032", "runtime_preview", "runtime_preview", "Keep developer hidden RuntimePreview controls disabled by default.", ValidTemplateFlow(), ["developerHiddenUi.disabled"], validateDryrunPrecheck),
            Case("VA-BM-033", "runtime_preview", "runtime_preview", "Show RuntimePreview metadata without frame bytes, image files, or model files.", ValidTemplateFlow(), ["render_metadata_without_binary"], validateDryrunPreview, expectedRuntimePreviewReady: true),
            Case("VA-BM-034", "precheck", "precheck", "Run static runtime package precheck for a ready draft.", ValidTemplateFlow(), ["runtimePackagePrecheck.review"], validateDryrunPrecheck),
            Case("VA-BM-035", "precheck", "precheck", "Block deployment when mock Station status is offline, without touching a real Station.", ValidTemplateFlow(), ["stationStatus.review", "runtimePackagePrecheck.review"], validateDryrunPrecheck, targetStationId: "offline-station", expectedPrecheckReady: false),
            Case("VA-BM-036", "precheck", "precheck", "Block deployment when structure-only dryrun summary is missing.", ValidTemplateFlow(), ["dryrun.required", "runtimePackagePrecheck.review"], ["validate_flow", "runtime_package_precheck"], expectsDryRun: false, expectedDryRunSucceeded: false, expectedPrecheckReady: false),
            Case("VA-BM-037", "runtime_preview", "runtime_preview_session", "Run metadata-only RuntimePreview governance session simulation and generate an audit report.", ValidTemplateFlow(templateId: "catalog-template-a"), ["create_runtime_preview_session", "catalog_snapshot", "readiness_gate", "metadata_simulation_report"], ["validate_flow", "dryrun_flow", RuntimePreviewSimulateMetadataSessionTool.ToolName], cameraBindingId: "mock-cam-template", expectedRuntimePreviewReady: true),
            Case("VA-BM-038", "scenario_corpus", "scenario_corpus", "Evaluate a remote-controller detection corpus case without loading a model file.", ValidModelIdFlow(), ["scenario_corpus.remote_control_detection", "explain_model_metadata_only"], validateDryrunPrecheck),
            Case("VA-BM-039", "scenario_corpus", "runtime_preview", "Preview a terminal color order corpus case with metadata-only RuntimePreview.", ValidWireFlow(outputChannelId: "qa-terminal-color"), ["scenario_corpus.terminal_color_order", "render_runtime_preview_metadata"], validateDryrunPreview, expectedRuntimePreviewReady: true),
            Case("VA-BM-040", "scenario_corpus", "multi_operator_flow", "Validate a multi-operator flow with template and model metadata handles.", ValidTemplateAndModelFlow(), ["scenario_corpus.multi_operator_flow", "operator_trace.review"], validateDryrunPrecheck),
            Case("VA-BM-041", "package_readiness", "package_readiness", "Explain why a ready template matching draft can proceed to package review while no package is created.", ValidTemplateFlow(templateId: "catalog-template-a"), ["package_readiness_bridge", "packageCreated.false", "deploymentExecuted.false"], validateDryrunPrecheck),
            Case("VA-BM-042", "package_readiness", "runtime_preview_session", "Run a metadata session for package readiness bridge evidence.", ValidTemplateFlow(templateId: "catalog-template-a"), ["create_runtime_preview_session", "package_readiness_bridge", "resource_trace.review"], ["validate_flow", "dryrun_flow", RuntimePreviewSimulateMetadataSessionTool.ToolName], cameraBindingId: "mock-cam-template", expectedRuntimePreviewReady: true),
            Case("VA-BM-043", "governance", "runtime_preview", "Replay governance metadata for a hole distance scenario without reading images.", ValidHoleFlow(), ["session_replay", "audit_timeline.review"], validateDryrunPreview, expectedRuntimePreviewReady: true),
            Case("VA-BM-044", "agent_explanation", "missing_resource", "Explain missing ResultOutput metadata: workflow draft stays editable but package is blocked.", MissingOutputFlow(), ["agent_explain_missing_output", "workflowDraftAllowed.true", "packageBlocked.true"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-045", "agent_explanation", "missing_resource", "Explain missing DeepLearning model metadata before package readiness.", MissingModelFlow(), ["agent_explain_missing_model", "request_model_source", "packageBlocked.true"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-046", "redacted_flow_corpus", "manifest_dry_run", "Review a redacted wire sequence flow as a manifest dry-run without creating a package.", ValidWireFlow(outputChannelId: "qa-redacted-wire"), ["redacted_flow_corpus.wire_sequence", "manifest_dry_run.metadata_hash", "packageCreated.false"], validateDryrunPrecheck),
            Case("VA-BM-047", "redacted_flow_corpus", "manifest_dry_run", "Review a remote-control defect flow with model metadata only.", ValidModelIdFlow(), ["redacted_flow_corpus.remote_control_defect", "model_dependency_trace.review", "realModelFilesLoaded.false"], validateDryrunPrecheck),
            Case("VA-BM-048", "redacted_flow_corpus", "manifest_dry_run", "Explain missing camera binding before package manifest review.", MissingCameraTemplateFlow(), ["redacted_flow_corpus.missing_camera", "camera_binding.required", "packageReviewAllowed.false"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-049", "redacted_flow_corpus", "manifest_dry_run", "Explain missing template metadata before manifest dry-run can pass.", MissingTemplateFlow(), ["redacted_flow_corpus.missing_template", "template_dependency.required", "packageReviewAllowed.false"], ["get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "TemplateMatching", expectedPrecheckReady: false),
            Case("VA-BM-050", "package_readiness_v2", "package_readiness", "Show that workflow edits remain allowed while package review is blocked for a missing output channel.", MissingOutputFlow(), ["workflowDraftAllowed.true", "packageBlocked.true", "output_channel.required"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-051", "package_readiness_v2", "multi_operator_flow", "Trace template and model dependencies for a combined metadata-only inspection flow.", ValidTemplateAndModelFlow(), ["dependency_trace.review", "operator_contract.review", "resource_contract.review"], validateDryrunPrecheck),
            Case("VA-BM-052", "manifest_dry_run", "runtime_preview_session", "Run a metadata session and attach manifest dry-run evidence for a template flow.", ValidTemplateFlow(templateId: "catalog-template-a"), ["create_runtime_preview_session", "manifestDryRunReportId.linked", "manifestArtifactGenerated.false"], ["validate_flow", "dryrun_flow", RuntimePreviewSimulateMetadataSessionTool.ToolName], cameraBindingId: "mock-cam-template", expectedRuntimePreviewReady: true),
            Case("VA-BM-053", "manifest_dry_run", "missing_resource", "Deny manifest review for a model-missing flow while preserving draft edits.", MissingModelFlow(), ["missing_model.dependency_trace", "packageReviewAllowed.false", "workflowDraftAllowed.true"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-054", "governance_v3", "runtime_preview_session", "Replay a governance session by manifest id without touching real package files.", ValidHoleFlow(tolerance: "0.05mm"), ["governance_store.v3", "lookup_by_manifestId", "session_replay.metadata_only"], ["validate_flow", "dryrun_flow", RuntimePreviewSimulateMetadataSessionTool.ToolName], cameraBindingId: "mock-cam-hole", expectedRuntimePreviewReady: true),
            Case("VA-BM-055", "agent_explanation_v2", "missing_resource", "Produce engineer-facing manifest risk explanation for missing PLC metadata without writing to PLC.", MissingPlcOutputFlow(), ["agent_explanation_v2.status", "manifestRisk.high", "plcWriteAttempted.false"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-056", "station_compatibility", "release_review", "Check that a traditional template flow is compatible with the standard release Station profile.", ValidTemplateFlow(templateId: "catalog-template-a"), ["station_compatibility.standard_profile", "releaseReviewAllowed.true", "metadataOnly.true"], validateDryrunPrecheck),
            Case("VA-BM-057", "station_compatibility", "release_review", "Block a two-camera review when the selected Station profile has only one camera slot.", MultiCameraFlow(), ["station_camera_slots_insufficient", "releaseReviewAllowed.false", "workflowDraftAllowed.true"], validateDryrunPrecheck, expectedStructurallyValid: false, expectedDryRunSucceeded: false, expectedPrecheckReady: false),
            Case("VA-BM-058", "station_compatibility", "release_review", "Block DeepLearning metadata on a traditional-only Station profile.", ValidModelIdFlow(), ["station_operator_not_supported.DeepLearning", "releaseReviewAllowed.false", "realStationTouched.false"], validateDryrunPrecheck),
            Case("VA-BM-059", "station_compatibility", "release_review", "Explain runtime version too low before release review.", ValidModelIdFlow(), ["station_runtime_version_too_low", "engineerAction.select_runtime_v14", "metadataOnly.true"], validateDryrunPrecheck),
            Case("VA-BM-060", "operator_contract_validation", "release_review", "Validate ImageAcquisition TemplateMatching and ResultOutput metadata contracts.", ValidTemplateFlow(templateId: "catalog-template-a"), ["operator_contract.validation", "requiredParameters.satisfied", "forbiddenParameters.none"], validateDryrunPrecheck),
            Case("VA-BM-061", "operator_contract_validation", "missing_resource", "Block release review when TemplateMatching lacks TemplateId metadata.", MissingTemplateFlow(), ["operator_contract_missing_parameter.TemplateId", "releaseReviewAllowed.false", "workflowDraftAllowed.true"], ["get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "TemplateMatching", expectedPrecheckReady: false),
            Case("VA-BM-062", "operator_contract_validation", "missing_resource", "Block release review when ResultOutput lacks OutputChannelId metadata.", MissingOutputFlow(), ["operator_contract_missing_parameter.OutputChannelId", "releaseReviewAllowed.false", "packageCreated.false"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-063", "operator_contract_validation", "release_review", "Require engineer approval for a DeepLearning operator contract before release review is allowed.", ValidModelIdFlow(), ["engineer_approval.deep_learning_release_review", "operatorContractsSatisfied.true", "releaseReviewAllowed.false"], validateDryrunPrecheck),
            Case("VA-BM-064", "pre_release_review", "release_review", "Run the full metadata-only pre-release review chain for a traditional inspection flow.", ValidHoleFlow(), ["readiness.package.manifest.station.contract.decision", "releaseReviewAllowed.true", "packageCreated.false"], validateDryrunPrecheck),
            Case("VA-BM-065", "pre_release_review", "missing_resource", "Show workflowDraftAllowed true while releaseReviewAllowed is false for unresolved camera metadata.", MissingCameraTemplateFlow(), ["workflowDraftAllowed.true", "releaseReviewAllowed.false", "camera_binding.required"], validateDryrunPrecheck, expectedPrecheckReady: false),
            Case("VA-BM-066", "pre_release_review", "release_review", "Require multi-station engineer approval for a metadata summary output flow.", ValidTemplateFlow(templateId: "catalog-template-a", outputChannelId: "metadata-summary"), ["engineer_approval.multi_station_review", "requiresEngineerApproval.true", "deploymentExecuted.false"], validateDryrunPrecheck),
            Case("VA-BM-067", "pre_release_review", "release_review", "Block release review when the output channel kind is absent on the target Station profile.", ValidTemplateFlow(templateId: "catalog-template-a", outputChannelId: "qa-template"), ["station_output_channel_kind_missing", "engineerAction.remap_output", "releaseReviewAllowed.false"], validateDryrunPrecheck),
            Case("VA-BM-068", "agent_explanation_v3", "release_review", "Explain why packageReviewAllowed can be true while releaseReviewAllowed is false due to Station compatibility.", ValidModelIdFlow(), ["agent_explanation_v3.workflowDraftVsRelease", "stationCompatibilityExplanation", "nextEngineerAction"], validateDryrunPrecheck),
            Case("VA-BM-069", "governance_v4", "runtime_preview_session", "Persist release review evidence with reviewId stationProfileId manifestId and caseId lookup keys.", ValidTemplateFlow(templateId: "catalog-template-a"), ["governance_store.v4", "lookup_by_reviewId", "lookup_by_stationProfileId"], ["validate_flow", "dryrun_flow", RuntimePreviewSimulateMetadataSessionTool.ToolName], cameraBindingId: "mock-cam-template", expectedRuntimePreviewReady: true),
            Case("VA-BM-070", "redacted_flow_corpus_v2", "manifest_dry_run", "Review the expanded redacted corpus v2 release-blocked operator contract case.", MissingTemplateFlow(), ["redacted_flow_corpus_v2.case_32", "operator_contract_missing_parameter", "redactionPass.true"], ["get_operator_schema", .. validateDryrunPrecheck], schemaOperatorType: "TemplateMatching", expectedPrecheckReady: false),
            .. FinalHardeningCases(validateDryrunPrecheck, validateDryrunPreview)
        ];
    }

    private static IReadOnlyList<BenchmarkCase> FinalHardeningCases(
        IReadOnlyList<string> validateDryrunPrecheck,
        IReadOnlyList<string> validateDryrunPreview)
    {
        var cases = new List<BenchmarkCase>();
        for (var number = 71; number <= 120; number++)
        {
            var offset = number - 71;
            var caseId = $"VA-BM-{number:000}";
            switch (offset % 10)
            {
                case 0:
                    cases.Add(Case(
                        caseId,
                        "release_review_final",
                        "release_review",
                        "Run Release Review Final for a traditional vision draft and keep all real deployment gates closed.",
                        ValidTemplateFlow(templateId: "catalog-template-a"),
                        ["release_decision_matrix.releaseAllowed", "metadataOnly.true", "realResourcesTouched.false"],
                        validateDryrunPrecheck));
                    break;
                case 1:
                    cases.Add(Case(
                        caseId,
                        "station_profile_final",
                        "release_review",
                        "Check low-spec Station profile constraints before any package is created.",
                        ValidHoleFlow(),
                        ["station_profile.low_spec_ipc", "operator_count.review", "packageCreated.false"],
                        validateDryrunPrecheck));
                    break;
                case 2:
                    cases.Add(Case(
                        caseId,
                        "operator_contract_final",
                        "release_review",
                        "Validate final operator contract registry coverage for template matching metadata.",
                        ValidTemplateFlow(templateId: "catalog-template-a"),
                        ["operator_contract_coverage.pass", "TemplateMatching.contract", "ResultOutput.contract"],
                        ["get_operator_schema", .. validateDryrunPrecheck],
                        schemaOperatorType: "TemplateMatching"));
                    break;
                case 3:
                    cases.Add(Case(
                        caseId,
                        "agent_explanation_final",
                        "release_review",
                        "Explain why workflowDraftAllowed can stay true while releaseReviewAllowed is false for missing output metadata.",
                        MissingOutputFlow(),
                        ["agent_explanation_final.firstFixRecommendation", "workflowDraftAllowed.true", "releaseReviewAllowed.false"],
                        validateDryrunPrecheck,
                        expectedPrecheckReady: false));
                    break;
                case 4:
                    cases.Add(Case(
                        caseId,
                        "governance_store_final",
                        "runtime_preview_session",
                        "Persist final governance export evidence with review and Station lookup keys.",
                        ValidTemplateFlow(templateId: "catalog-template-a"),
                        ["governance_export_final.lookupKeys", "release_review_decision.stream", "redactionPass.true"],
                        ["validate_flow", "dryrun_flow", RuntimePreviewSimulateMetadataSessionTool.ToolName],
                        cameraBindingId: "mock-cam-template",
                        expectedRuntimePreviewReady: true));
                    break;
                case 5:
                    cases.Add(Case(
                        caseId,
                        "review_desk_final",
                        "runtime_preview",
                        "Render Review Desk final decision states without exposing ordinary-user controls.",
                        ValidWireFlow(outputChannelId: "qa-wire-result"),
                        ["reviewDesk.releaseAllowed.approvalRequired.blocked", "adminDeveloperGate.required", "domRedacted.true"],
                        validateDryrunPreview,
                        expectedRuntimePreviewReady: true));
                    break;
                case 6:
                    cases.Add(Case(
                        caseId,
                        "source_guard_final",
                        "missing_resource",
                        "Reject package path style metadata before release review and keep package creation disabled.",
                        MissingPlcOutputFlow(),
                        ["source_guard.package_path_denied", "plcWriteAttempted.false", "deploymentExecuted.false"],
                        validateDryrunPrecheck,
                        expectedPrecheckReady: false));
                    break;
                case 7:
                    cases.Add(Case(
                        caseId,
                        "readability_gate_final",
                        "manifest_dry_run",
                        "Generate readable reports with non-empty status decision risk and action fields.",
                        ValidTemplateAndModelFlow(),
                        ["report_readability_gate.pass", "status.non_empty", "action.non_empty"],
                        validateDryrunPrecheck));
                    break;
                case 8:
                    cases.Add(Case(
                        caseId,
                        "remote_ci_evidence_final",
                        "manifest_dry_run",
                        "Attach non-local workflow metadata for final CI artifact assertion.",
                        ValidModelIdFlow(),
                        ["workflowRun.runId.non_local", "artifact.digest.recorded", "headSha.current"],
                        validateDryrunPrecheck));
                    break;
                default:
                    cases.Add(Case(
                        caseId,
                        "redacted_corpus_final",
                        "release_review",
                        "Run a redacted corpus final case through manifest station contract and decision evidence.",
                        ValidHoleFlow(tolerance: "0.04mm"),
                        ["redacted_flow_corpus_final.caseCount60", "station_compatibility_final", "pre_release_review_final"],
                        validateDryrunPrecheck));
                    break;
            }
        }

        return cases;
    }

    private static BenchmarkCase Case(
        string caseId,
        string category,
        string taskType,
        string userRequest,
        BenchmarkFlow flow,
        IReadOnlyList<string> businessActions,
        IReadOnlyList<string> toolCalls,
        string? templateId = null,
        string? scenarioKey = null,
        string? schemaOperatorType = null,
        BenchmarkFlow? existingFlow = null,
        string? targetStationId = null,
        string? cameraBindingId = null,
        bool expectedStructurallyValid = true,
        bool expectsDryRun = true,
        bool expectedDryRunSucceeded = true,
        bool? expectedPrecheckReady = true,
        bool? expectedRuntimePreviewReady = null)
    {
        return new BenchmarkCase
        {
            CaseId = caseId,
            Category = category,
            TaskType = taskType,
            UserRequest = userRequest,
            Flow = flow,
            ExpectedBusinessActions = businessActions,
            ExpectedToolCalls = toolCalls,
            TemplateId = templateId,
            TemplateScenarioKey = scenarioKey,
            SchemaOperatorType = schemaOperatorType ?? flow.Operators.FirstOrDefault()?.OperatorType ?? "ImageAcquisition",
            ExistingFlow = existingFlow,
            TargetStationId = targetStationId,
            CameraBindingId = cameraBindingId ?? "mock-camera-binding",
            ExpectedStructurallyValid = expectedStructurallyValid,
            ExpectsDryRun = expectsDryRun,
            ExpectedDryRunSucceeded = expectedDryRunSucceeded,
            ExpectedPrecheckReady = toolCalls.Any(
                tool => string.Equals(tool, "runtime_package_precheck", StringComparison.OrdinalIgnoreCase))
                ? expectedPrecheckReady
                : null,
            ExpectedRuntimePreviewReady = expectedRuntimePreviewReady
        };
    }

    private static BenchmarkFlow ValidWireFlow(string outputChannelId = "qa-wire")
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
            ]);
    }

    private static BenchmarkFlow MissingCameraWireFlow()
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

    private static BenchmarkFlow ValidTemplateFlow(
        string? minScore = null,
        bool useRoi = false,
        string? templateId = null,
        string? outputChannelId = "qa-template",
        string cameraParameterName = "CameraBindingId",
        bool includeUnusedFilePath = false)
    {
        var cameraParams = new List<(string Key, string Value)>
        {
            ("SourceType", "Camera"),
            (cameraParameterName, cameraParameterName == "CameraId" ? "mock-camera-id" : "mock-cam-template")
        };
        if (includeUnusedFilePath)
        {
            cameraParams.Add(("FilePath", "mock://unused/file-path"));
        }

        var templateParams = new List<(string Key, string Value)>();
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            templateParams.Add(("TemplateId", templateId));
        }
        else
        {
            templateParams.Add(("TemplatePath", "mock://templates/bracket-a.template"));
        }

        if (!string.IsNullOrWhiteSpace(minScore))
        {
            templateParams.Add(("MinScore", minScore));
        }

        if (useRoi)
        {
            templateParams.AddRange(
            [
                ("UseRoi", "true"),
                ("RoiX", "10"),
                ("RoiY", "10"),
                ("RoiWidth", "120"),
                ("RoiHeight", "90")
            ]);
        }

        return Flow(
            [
                Op("op_cam", "ImageAcquisition", cameraParams.ToArray()),
                Op("op_match", "TemplateMatching", templateParams.ToArray()),
                Op("op_judge", "ResultJudgment", ("MinScore", minScore ?? "0.82")),
                Op("op_out", "ResultOutput", ("OutputChannelId", outputChannelId ?? "qa-template"))
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow MissingTemplateFlow()
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
            ]);
    }

    private static BenchmarkFlow MissingCameraTemplateFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera")),
                Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/part.template")),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-template"))
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow MissingOutputFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-output")),
                Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/output.template")),
                Op("op_out", "ResultOutput")
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow MissingPlcOutputFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-plc")),
                Op("op_judge", "ResultJudgment"),
                Op("op_out", "ResultOutput", ("Channel", "plc"))
            ],
            [
                Link("op_cam", "Image", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow MissingModelFlow()
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
            ]);
    }

    private static BenchmarkFlow ValidModelIdFlow()
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
            ]);
    }

    private static BenchmarkFlow ValidTemplateAndModelFlow()
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-combo")),
                Op("op_match", "TemplateMatching", ("TemplatePath", "mock://templates/combo.template")),
                Op("op_detect", "DeepLearning", ("ModelId", "mock-combo-model")),
                Op("op_judge", "ResultJudgment"),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-combo"))
            ],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_cam", "Image", "op_detect", "Image"),
                Link("op_match", "Score", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow ValidHoleFlow(
        string roiA = "hole_a",
        string roiB = "hole_b",
        string tolerance = "+/-0.05")
    {
        return Flow(
            [
                Op("op_cam", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-hole")),
                Op("op_circle_a", "CircleMeasurement", ("Roi", roiA)),
                Op("op_circle_b", "CircleMeasurement", ("Roi", roiB)),
                Op("op_distance", "MeasureDistance", ("Unit", "mm"), ("Tolerance", tolerance)),
                Op("op_judge", "ResultJudgment", ("Tolerance", tolerance)),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-hole"))
            ],
            [
                Link("op_cam", "Image", "op_circle_a", "Image"),
                Link("op_cam", "Image", "op_circle_b", "Image"),
                Link("op_circle_a", "Center", "op_distance", "PointA"),
                Link("op_circle_b", "Center", "op_distance", "PointB"),
                Link("op_distance", "Distance", "op_judge", "Input"),
                Link("op_judge", "Result", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow MultiCameraFlow()
    {
        return Flow(
            [
                Op("op_cam_top", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-top")),
                Op("op_cam_side", "ImageAcquisition", ("SourceType", "Camera"), ("CameraBindingId", "mock-cam-side")),
                Op("op_join", "ImageCompose", ("Mode", "side_by_side")),
                Op("op_out", "ResultOutput", ("OutputChannelId", "qa-multi"))
            ],
            [
                Link("op_cam_top", "Image", "op_join", "ImageA"),
                Link("op_cam_side", "Image", "op_join", "ImageB"),
                Link("op_join", "Image", "op_out", "Input")
            ]);
    }

    private static BenchmarkFlow Flow(
        IReadOnlyList<BenchmarkOperator> operators,
        IReadOnlyList<BenchmarkConnection> connections)
    {
        return new BenchmarkFlow(operators, connections);
    }

    private static BenchmarkOperator Op(
        string tempId,
        string operatorType,
        params (string Key, string Value)[] parameters)
    {
        return new BenchmarkOperator(
            tempId,
            operatorType,
            parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
    }

    private static BenchmarkConnection Link(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new BenchmarkConnection(sourceTempId, sourcePortName, targetTempId, targetPortName);
    }

    private sealed class BenchmarkStationStatusReader : IVisionAgentStationStatusReader
    {
        public Task<VisionAgentStationStatus?> TryReadAsync(
            string targetStationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(targetStationId))
            {
                return Task.FromResult<VisionAgentStationStatus?>(null);
            }

            return Task.FromResult<VisionAgentStationStatus?>(new VisionAgentStationStatus
            {
                StationId = targetStationId,
                IsOnline = targetStationId.Contains("online", StringComparison.OrdinalIgnoreCase),
                Status = targetStationId.Contains("online", StringComparison.OrdinalIgnoreCase)
                    ? "online"
                    : "offline"
            });
        }
    }
}

internal static class VisionAgentBusinessBenchmarkMarkdown
{
    public static string Create(BenchmarkDocument document, FileInfo jsonPath)
    {
        var lines = new List<string>
        {
            "# Vision Agent Executable Business Benchmark",
            "",
            $"- Benchmark: `{document.BenchmarkId}`",
            $"- Generated UTC: `{document.GeneratedAtUtc}`",
            $"- Commit SHA: `{document.WorkflowRun.CommitSha}`",
            $"- Branch: `{document.WorkflowRun.BranchName}`",
            $"- Workflow run: `{document.WorkflowRun.RunId}` attempt `{document.WorkflowRun.RunAttempt}`",
            $"- Mode: `{document.Mode}`",
            $"- Cases: {document.Summary.CaseCount}",
            $"- Accepted: {document.Summary.Accepted}",
            $"- JSON: `{VisionAgentBusinessBenchmark.RepoRelative(jsonPath)}`",
            "",
            "## Executable Design",
            "",
            "- Each case executes registered Vision Agent tools through `VisionAgentToolRegistry`.",
            "- `expectedToolCalls` contains only registered tool names.",
            "- Business-only expectations such as parameter completion, review, or UI intent are stored in `expectedBusinessActions`.",
            "- RuntimePreview remains offline metadata-only through the existing stub tools and offline adapter.",
            "",
            "## Metrics",
            "",
            "| Metric | Actual | Minimum | Passed |",
            "| --- | ---: | ---: | --- |"
        };

        foreach (var item in document.ThresholdResults)
        {
            lines.Add($"| {item.Key} | {item.Value.Actual:P2} | {item.Value.Minimum:P2} | {item.Value.Passed} |");
        }

        lines.AddRange(
        [
            "",
            "## Task Set",
            "",
            "| Case | Category | Type | Business Actions | Expected Tools | Actual Tools | Passed |",
            "| --- | --- | --- | --- | --- | --- | --- |"
        ]);

        foreach (var result in document.Cases)
        {
            lines.Add(
                "| " +
                string.Join(" | ", [
                    result.CaseId,
                    result.Category,
                    result.TaskType,
                    string.Join(", ", result.ExpectedBusinessActions),
                    string.Join(", ", result.ExpectedToolCalls),
                    string.Join(", ", result.ActualToolCalls.Select(item => item.ToolName)),
                    result.Passed.ToString()
                ]) +
                " |");
        }

        lines.AddRange(
        [
            "",
            "## Field Contract",
            "",
            "- `expectedBusinessActions`: non-tool business expectations, such as parameter completion, review, or UI state intent.",
            "- `expectedToolCalls`: registered tool names that must execute in order.",
            "- `actualToolCalls`: tool execution trace with permission, success, and error metadata.",
            "- `actualValidationResult`, `actualDryRunResult`, `actualPrecheckResult`, `actualRuntimePreviewResult`: actual tool outputs used for metrics.",
            "",
            "## Safety",
            "",
            "- No real camera SDK, Station access, image file read, model file load, PLC write, package creation, deployment, or hot load is used.",
            $"- RuntimePreview mode: `{document.Safety.RuntimePreviewMode}`",
            $"- Safety violations: {(document.Safety.Violations.Count == 0 ? "none" : string.Join(", ", document.Safety.Violations))}",
            ""
        ]);

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(FileInfo Output, FileInfo Report)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = Path.Combine("quality", "evals", "reports", "VisionAgent_business_benchmark_baseline.json");
        var report = Path.Combine("quality", "evals", "reports", "VisionAgent_business_benchmark_baseline.md");
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

        return new RunnerOptions(
            new FileInfo(Path.GetFullPath(output)),
            new FileInfo(Path.GetFullPath(report)));
    }
}

internal sealed record BenchmarkCase
{
    public string CaseId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public string UserRequest { get; init; } = string.Empty;
    public BenchmarkFlow Flow { get; init; } = new([], []);
    public BenchmarkFlow? ExistingFlow { get; init; }
    public IReadOnlyList<string> ExpectedBusinessActions { get; init; } = [];
    public IReadOnlyList<string> ExpectedToolCalls { get; init; } = [];
    public string? TemplateId { get; init; }
    public string? TemplateScenarioKey { get; init; }
    public string SchemaOperatorType { get; init; } = "ImageAcquisition";
    public string? TargetStationId { get; init; }
    public string? EntryOperatorTempId { get; init; }
    public string CameraBindingId { get; init; } = "mock-camera-binding";
    public bool ExpectedStructurallyValid { get; init; } = true;
    public bool ExpectsDryRun { get; init; } = true;
    public bool ExpectedDryRunSucceeded { get; init; } = true;
    public bool? ExpectedPrecheckReady { get; init; } = true;
    public bool? ExpectedRuntimePreviewReady { get; init; }
}

internal sealed record BenchmarkFlow(
    IReadOnlyList<BenchmarkOperator> Operators,
    IReadOnlyList<BenchmarkConnection> Connections);

internal sealed record BenchmarkOperator(
    string TempId,
    string OperatorType,
    IReadOnlyDictionary<string, string> Parameters);

internal sealed record BenchmarkConnection(
    string SourceTempId,
    string SourcePortName,
    string TargetTempId,
    string TargetPortName);

internal sealed record BenchmarkDocument(
    string SchemaVersion,
    string BenchmarkId,
    string GeneratedAtUtc,
    string Mode,
    VisionAgentWorkflowRunMetadata WorkflowRun,
    BenchmarkSummary Summary,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyDictionary<string, BenchmarkThresholdResult> ThresholdResults,
    IReadOnlyDictionary<string, int> CategoryCounts,
    IReadOnlyDictionary<string, int> TaskTypeCounts,
    BenchmarkSafety Safety,
    IReadOnlyList<ExecutableBenchmarkCaseResult> Cases);

internal sealed record BenchmarkSummary(
    int CaseCount,
    int RuntimePreviewCaseCount,
    int PassedCaseCount,
    bool Accepted);

internal sealed record BenchmarkThresholdResult(
    double Actual,
    double Minimum,
    bool Passed);

internal sealed record BenchmarkSafety(
    bool RealCameraSdkTouched,
    bool RealStationTouched,
    bool RealImageFilesRead,
    bool RealModelFilesLoaded,
    bool PlcWriteAttempted,
    bool PackageCreated,
    bool HotLoadAttempted,
    string RuntimePreviewMode,
    IReadOnlyList<string> Violations);

internal sealed record ExecutableBenchmarkCaseResult(
    string CaseId,
    string Category,
    string TaskType,
    string UserRequest,
    IReadOnlyList<string> ExpectedBusinessActions,
    IReadOnlyList<string> ExpectedToolCalls,
    IReadOnlyList<BenchmarkToolCallResult> ActualToolCalls,
    JsonElement? ActualValidationResult,
    JsonElement? ActualDryRunResult,
    JsonElement? ActualPrecheckResult,
    JsonElement? ActualRuntimePreviewResult,
    CaseExecutionMetrics Metrics,
    bool Passed,
    IReadOnlyList<string> Failures);

internal sealed record BenchmarkToolCallResult(
    string ToolName,
    string Permission,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record CaseExecutionMetrics(
    bool GenerationSucceeded,
    bool StructuralValidationPassed,
    bool DryRunPassed,
    bool PreviewReady,
    bool ParametersComplete,
    bool UserApplicable);
