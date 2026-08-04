using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed class ApplyGateResolver
{
    internal BuildStepResult<VisionAgentApplyGate> Build(
        VisionAgentToolResult validation,
        VisionAgentToolResult dryRun,
        VisionAgentToolResult packageReadiness,
        VisionAgentWorkflowDiff diff,
        string compiledFingerprint = "",
        VisionTaskRouteAssessment? routeAssessment = null,
        string returnedFlowSemanticFingerprint = "")
    {
        var validationBlocking = VisionAgentBuildSupport.ReadCount(validation.Data, "blockingIssues");
        var validationFingerprint = ReadFingerprint(validation.Data, "validationFingerprint", "artifactFingerprint");
        var dryRunFingerprint = ReadFingerprint(dryRun.Data, "dryRunFingerprint", "artifactFingerprint");
        var precheckFingerprint = ReadFingerprint(packageReadiness.Data, "precheckFingerprint", "artifactFingerprint");
        var route = routeAssessment ?? new VisionTaskRouteAssessment
        {
            Supported = false,
            Satisfied = false,
            TaskType = "missing",
            RequiresUserReview = true,
            BlockingReasons = ["route_semantics_evidence_missing"]
        };

        var fingerprints = new[]
        {
            compiledFingerprint,
            validationFingerprint,
            dryRunFingerprint,
            precheckFingerprint,
            returnedFlowSemanticFingerprint
        };
        var fingerprintConsistent = fingerprints.All(value => !string.IsNullOrWhiteSpace(value)) &&
                                    fingerprints.Skip(1).All(value => string.Equals(
                                        fingerprints[0],
                                        value,
                                        StringComparison.OrdinalIgnoreCase)) &&
                                    VisionAgentBuildSupport.ReadBool(validation.Data, "fingerprintConsistent") == true &&
                                    VisionAgentBuildSupport.ReadBool(dryRun.Data, "fingerprintConsistent") == true &&
                                    VisionAgentBuildSupport.ReadBool(packageReadiness.Data, "fingerprintConsistent") == true;
        var dryRunSucceeded = VisionAgentBuildSupport.ReadBool(dryRun.Data, "dryRunSucceeded") == true;
        var precheckReady = VisionAgentBuildSupport.ReadBool(packageReadiness.Data, "readyForDeployment") == true;
        var routeReady = route.Supported && route.Satisfied;
        var canvasReady = validationBlocking == 0 && fingerprintConsistent && routeReady;
        var runtimeReady = canvasReady && dryRunSucceeded;
        var deploymentReady = runtimeReady &&
                              precheckReady &&
                              diff.DeploymentBlockers.Count == 0;
        var blockers = new List<string>();
        blockers.AddRange(VisionAgentBuildSupport.ReadIssueCodes(validation.Data, "blockingIssues"));
        if (!routeReady)
        {
            blockers.AddRange(route.BlockingReasons);
        }

        if (!fingerprintConsistent)
        {
            blockers.Add("artifact_fingerprint_inconsistent");
        }

        var deploymentBlockers = deploymentReady
            ? []
            : diff.DeploymentBlockers.Count > 0
                ? diff.DeploymentBlockers.ToList()
                : precheckReady ? ["deployment_metadata_pending"] : VisionAgentBuildSupport.ReadIssueCodes(packageReadiness.Data, "blockingIssues");
        if (deploymentBlockers.Count == 0 && !precheckReady)
        {
            deploymentBlockers.Add("deployment_metadata_pending");
        }

        var disposition = deploymentReady
            ? "deployment_ready"
            : runtimeReady
                ? "runtime_draft_ready"
                : canvasReady
                    ? "editable_only"
                    : "blocked";
        var status = disposition switch
        {
            "deployment_ready" => "deployment_ready",
            "runtime_draft_ready" => "runtime_draft_ready",
            "editable_only" => "canvas_apply_ready",
            _ => "blocked"
        };
        var gate = new VisionAgentApplyGate
        {
            CanvasApplyReady = canvasReady,
            RuntimeDraftReady = runtimeReady,
            DeploymentReady = deploymentReady,
            Blocked = !canvasReady,
            Status = status,
            ApplyBlockers = blockers
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DeploymentBlockers = deploymentBlockers
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ArtifactFingerprint = compiledFingerprint,
            CompiledFingerprint = compiledFingerprint,
            ValidationFingerprint = validationFingerprint,
            DryRunFingerprint = dryRunFingerprint,
            PrecheckFingerprint = precheckFingerprint,
            ReturnedFlowSemanticFingerprint = returnedFlowSemanticFingerprint,
            ArtifactFingerprintConsistent = fingerprintConsistent,
            RouteSemanticsSatisfied = routeReady,
            ArtifactDisposition = disposition,
            MetadataOnly = true
        };

        return VisionAgentBuildSupport.StepResult(
            gate,
            $"应用门禁已解析为 {DisplayGateStatus(gate.Status)}。",
            canvasReady ? AgentRunEventStatuses.Completed : AgentRunEventStatuses.Blocked,
            gate,
            warningCode: deploymentReady ? string.Empty : "deployment_not_ready",
            applyImpact: canvasReady ? disposition : "blocked",
            deploymentImpact: deploymentReady ? "deployment_ready" : "deployment_blocked");
    }

    private static string ReadFingerprint(object? data, params string[] propertyNames)
    {
        var root = VisionAgentBuildSupport.ToJsonElementOrNull(data);
        if (root == null)
        {
            return string.Empty;
        }

        foreach (var propertyName in propertyNames)
        {
            var value = VisionAgentBuildSupport.ReadString(root.Value, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string DisplayGateStatus(string status)
    {
        return status switch
        {
            "blocked" => "已阻断",
            "deployment_ready" => "部署就绪",
            "runtime_draft_ready" => "运行草稿就绪",
            "canvas_apply_ready" => "画布可应用",
            _ => status
        };
    }
}
