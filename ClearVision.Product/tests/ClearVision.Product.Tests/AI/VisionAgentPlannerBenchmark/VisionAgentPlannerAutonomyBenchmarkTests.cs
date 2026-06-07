using System.Text.Json;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentPlannerBenchmark;

public sealed class VisionAgentPlannerAutonomyBenchmarkTests
{
    [Fact(DisplayName = "planner autonomy benchmark report should expose required execution fields")]
    public void PlannerAutonomyReport_ShouldExposeRequiredExecutionFields()
    {
        using var doc = LoadReport();
        var root = doc.RootElement;

        root.GetProperty("benchmarkId").GetString().Should().Be("vision_agent_planner_autonomy_benchmark");
        root.GetProperty("mode").GetString().Should().Be("offline_metadata_only");
        root.GetProperty("summary").GetProperty("plannerCaseCount").GetInt32().Should().Be(15);
        root.GetProperty("summary").GetProperty("permissionNegativeCaseCount").GetInt32().Should().Be(6);
        root.GetProperty("summary").GetProperty("accepted").GetBoolean().Should().BeTrue();

        foreach (var item in root.GetProperty("cases").EnumerateArray()
                     .Concat(root.GetProperty("permissionNegativeCases").EnumerateArray()))
        {
            HasProperty(item, "expectedBusinessActions").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "allowedTools").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "plannerMessages").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "plannedToolCalls").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "policyDecisions").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualToolCalls").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualValidationResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualDryRunResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualPrecheckResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "actualRuntimePreviewResult").Should().BeTrue(item.GetProperty("caseId").GetString());
            HasProperty(item, "finalWorkflowDraftAllowed").Should().BeTrue(item.GetProperty("caseId").GetString());
            item.GetProperty("passed").GetBoolean().Should().BeTrue(item.GetProperty("caseId").GetString());
        }
    }

    [Fact(DisplayName = "planner autonomy benchmark should cover required business scenarios")]
    public void PlannerAutonomyReport_ShouldCoverRequiredBusinessScenarios()
    {
        using var doc = LoadReport();
        var taskTypes = doc.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Select(item => item.GetProperty("taskType").GetString())
            .ToList();

        taskTypes.Should().Contain([
            "wire_sequence_generation",
            "template_matching_generation",
            "hole_distance_generation",
            "modify_existing_flow",
            "missing_camera_binding",
            "missing_model_path",
            "missing_template_path",
            "parameter_completion_review",
            "runtime_preview_authorized",
            "runtime_preview_unauthorized",
            "non_whitelist_tool_rejected",
            "deployment_prepare_only_precheck",
            "planner_max_rounds_controlled_failure",
            "final_draft_edits_existing_flow",
            "final_workflow_draft_new_flow"]);
    }

    [Fact(DisplayName = "permission negative benchmark should record denials in decisions trace and pending actions")]
    public void PermissionNegativeReport_ShouldRecordDenialsTraceAndPendingActions()
    {
        using var doc = LoadReport();
        var negativeCases = doc.RootElement.GetProperty("permissionNegativeCases")
            .EnumerateArray()
            .ToList();

        negativeCases.Should().HaveCount(6);
        foreach (var item in negativeCases)
        {
            item.GetProperty("policyDecisions")
                .EnumerateArray()
                .Should()
                .Contain(decision => decision.GetProperty("allowed").GetBoolean() == false, item.GetProperty("caseId").GetString());
            item.GetProperty("toolTrace")
                .EnumerateArray()
                .Should()
                .Contain(trace => trace.GetProperty("success").GetBoolean() == false, item.GetProperty("caseId").GetString());
            item.GetProperty("pendingActions").GetArrayLength().Should().BeGreaterThan(0, item.GetProperty("caseId").GetString());
        }

        var errorCodes = negativeCases
            .SelectMany(item => item.GetProperty("policyDecisions").EnumerateArray())
            .Where(item => item.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("errorCode").GetString())
            .ToList();

        errorCodes.Should().Contain([
            "runtime_preview_consent_required",
            "runtime_preview_permission_denied",
            "tool_permission_denied",
            "config_write_denied",
            "tool_not_whitelisted",
            "deployment_prepare_tool_denied"]);
    }

    [Fact(DisplayName = "runtime preview denials should not block workflow draft allowance")]
    public void RuntimePreviewDenials_ShouldNotBlockWorkflowDraftAllowance()
    {
        using var doc = LoadReport();
        var runtimePreviewDeniedCases = doc.RootElement.GetProperty("permissionNegativeCases")
            .EnumerateArray()
            .Where(item =>
                item.GetProperty("caseId").GetString() is "VA-PERM-001" or "VA-PERM-002")
            .ToList();

        runtimePreviewDeniedCases.Should().HaveCount(2);
        runtimePreviewDeniedCases.Should().OnlyContain(item =>
            item.GetProperty("finalWorkflowDraftAllowed").GetBoolean());
    }

    [Fact(DisplayName = "planner benchmark source guard should exclude real runtime and resource APIs")]
    public void PlannerBenchmarkSourceGuard_ShouldExcludeRealRuntimeAndResourceApis()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "quality",
            "tools",
            "VisionAgentPlannerAutonomyBenchmarkRunner",
            "Program.cs"));
        var forbiddenFragments = new[]
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".bmp",
            ".tif",
            ".tiff",
            "AcquireSingleFrameAsync",
            "EnumerateCamerasAsync",
            "GetOrCreateByBindingAsync",
            "CameraTestFrameTool",
            "ReplayFlowWithFrameTool",
            "HttpClient",
            "TcpClient",
            "Socket",
            "System.Net.",
            "File.ReadAllBytes",
            "Image.FromFile",
            "Cv2.ImRead",
            "Process.Start",
            "System.Diagnostics.Process",
            "deploy_runtime_package",
            "hot_reload",
            "plc_write",
            string.Concat("Ac", "me.Product.")
        };

        foreach (var fragment in forbiddenFragments)
        {
            source.Should().NotContain(fragment);
        }
    }

    private static JsonDocument LoadReport()
    {
        var path = Path.Combine(
            GetRepoRoot(),
            "quality",
            "evals",
            "reports",
            "planner_autonomy_benchmark.json");

        File.Exists(path).Should().BeTrue("the planner autonomy benchmark report is a quality artifact");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out _);
    }

    private static string GetRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
    }
}
