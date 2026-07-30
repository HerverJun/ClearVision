using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal static class VisionAgentPublicBuildProjector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static VisionAgentPublicBuildResultV1? Project(
        AiFlowGenerationResult result,
        AiFlowGenerationRequest request,
        string runId,
        string submittedBuildFingerprint,
        string buildIdentity)
    {
        var build = result.BuildResult;
        if (build == null)
        {
            return null;
        }

        var flow = ReadFlow(build.Flow ?? result.Flow);
        var pendingParameters = build.ParameterMapping
            .Where(item => item.Pending && !item.ResourceDependent)
            .ToList();
        var missingResources = (build.MissingResources.Count > 0 ? build.MissingResources : result.MissingResources)
            .Where(item => !string.Equals(item.Status, VisionAgentResourceStatuses.Bound, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validation = ProjectValidation(
            build.ValidationPreview ?? result.ValidationPreview,
            build.DryRunResult ?? result.DryRunResult,
            build.ReadinessReport,
            build.ApplyGate,
            pendingParameters.Count,
            missingResources.Count,
            build.FirstFixRecommendation);

        return new VisionAgentPublicBuildResultV1
        {
            RunId = runId,
            BuildId = build.BuildId,
            ClientOperationId = request.ClientOperationId,
            BuildIdentity = buildIdentity,
            SubmittedBuildFingerprint = submittedBuildFingerprint,
            PlanId = build.PlanId,
            PlanHash = build.PlanHash,
            AnswerSetFingerprint = build.AnswerSetFingerprint,
            AnswerRevision = Math.Max(0, request.BuildFromPlan?.AnswerRevision ?? 0),
            ResourceRevision = Math.Max(0, request.BuildFromPlan?.ResourceRevision ?? 0),
            ProjectBaseline = request.ProjectBaseline is null ? null : request.ProjectBaseline with { },
            CandidateFlowFingerprint = flow == null
                ? string.Empty
                : ExecutionFlowIdentity.ComputeFlowHash(flow.ToEntity()),
            OperatorCount = flow?.Operators.Count ?? build.OperatorPipeline.Count,
            ConnectionCount = flow?.Connections.Count ?? 0,
            OperatorPipeline = build.OperatorPipeline.ToList(),
            ParameterMapping = build.ParameterMapping.Select(ToPublicParameter).ToList(),
            MissingResources = missingResources,
            WorkflowDiff = build.WorkflowDiff,
            Validation = validation,
            PublicTimeline = build.ToolEvidenceTimeline
                .Where(item => item.MetadataOnly && item.RedactionPass)
                .ToList(),
            PublicWarnings = build.PublicWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MetadataOnly = true,
            RedactionPass = true
        };
    }

    public static VisionAgentBuildValidationV1 ProjectValidation(
        object? validationSource,
        object? dryRunSource,
        object? manifestSource,
        VisionAgentApplyGate gate,
        int pendingParameterCount,
        int missingResourceCount,
        string? firstFixRecommendation)
    {
        var validation = Element(validationSource);
        var dryRun = Element(dryRunSource);
        var manifest = Element(manifestSource);
        var structuralBlockers = ArrayCount(validation, "blockingIssues");
        var structuralWarnings = ArrayCount(validation, "warnings");
        var structurallyValid = Bool(validation, "isValid") ?? structuralBlockers == 0;
        var dryRunBlockers = ArrayCount(dryRun, "blockingIssues");
        var dryRunWarnings = ArrayCount(dryRun, "warnings");
        var dryRunSucceeded = Bool(dryRun, "dryRunSucceeded") ?? (structurallyValid && dryRunBlockers == 0);
        var manifestReady = Bool(manifest, "readyForDeployment") ??
            (structurallyValid && dryRunSucceeded && pendingParameterCount == 0 && missingResourceCount == 0);
        var handoffEligible = structurallyValid && dryRunSucceeded && !gate.Blocked && gate.RuntimeDraftReady &&
            pendingParameterCount == 0 && missingResourceCount == 0;
        var firstFix = FirstNonBlank(firstFixRecommendation, gate.FirstFixRecommendation,
            structuralBlockers > 0 ? "请先修复流程结构阻断，再重新校验。" :
            pendingParameterCount > 0 ? "请先确认待处理参数，再重新校验。" :
            missingResourceCount > 0 ? "请先完成可安全绑定的资源决策，再重新校验。" :
            handoffEligible ? "当前候选已具备下一阶段审核条件。" : "请检查运行预演和公开阻断后重新校验。");

        return new VisionAgentBuildValidationV1
        {
            Structural = Check("structural", "结构校验", structurallyValid, structuralBlockers, structuralWarnings,
                structurallyValid ? "流程结构、端口与算子合同已通过。" : "流程结构仍有阻断。"),
            DryRun = Check("dry-run", "运行预演", dryRunSucceeded, dryRunBlockers, dryRunWarnings,
                dryRunSucceeded ? "仅元数据预演已完成。" : "运行预演未通过。"),
            Manifest = Check("manifest", "清单预检", manifestReady,
                manifestReady ? 0 : pendingParameterCount + missingResourceCount, 0,
                manifestReady ? "运行清单与资源声明已通过预检。" : "仍有参数或资源等待处理。"),
            ApplyGate = gate,
            HandoffEligible = handoffEligible,
            ReadinessStatus = handoffEligible ? "handoff_eligible" :
                structurallyValid && dryRunSucceeded ? "inputs_pending" : "blocked",
            FirstFixRecommendation = firstFix,
            MetadataOnly = true
        };
    }

    private static VisionAgentBuildCheckV1 Check(
        string id,
        string label,
        bool passed,
        int blockers,
        int warnings,
        string summary) => new()
    {
        Id = id,
        Label = label,
        Status = blockers > 0 ? "failed" : passed ? "passed" : "pending",
        Summary = summary,
        BlockerCount = Math.Max(0, blockers),
        WarningCount = Math.Max(0, warnings)
    };

    private static OperatorFlowDto? ReadFlow(object? source)
    {
        if (source is OperatorFlowDto flow)
        {
            return flow;
        }

        if (source == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OperatorFlowDto>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static VisionAgentParameterMapping ToPublicParameter(VisionAgentParameterMapping parameter) =>
        parameter with
        {
            Value = Scalar(parameter.Value),
            DefaultValue = Scalar(parameter.DefaultValue),
            MinValue = Scalar(parameter.MinValue),
            MaxValue = Scalar(parameter.MaxValue),
            RequiredWhen = ToPublicConditionSet(parameter.RequiredWhen),
            EnabledWhen = ToPublicConditionSet(parameter.EnabledWhen),
            DisabledWhen = ToPublicConditionSet(parameter.DisabledWhen)
        };

    private static VisionAgentParameterConditionSet? ToPublicConditionSet(VisionAgentParameterConditionSet? set)
    {
        if (set is null) return null;
        static List<VisionAgentParameterCondition> Project(IEnumerable<VisionAgentParameterCondition> conditions) =>
            conditions.Select(condition => condition with { Value = Scalar(condition.Value) }).ToList();
        return new VisionAgentParameterConditionSet
        {
            AllConditions = Project(set.AllConditions),
            AnyConditions = Project(set.AnyConditions)
        };
    }

    private static object? Scalar(object? value)
    {
        if (value == null || value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return value;
        }
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number when element.TryGetDouble(out var number) => number,
                _ => null
            };
        }
        return null;
    }

    private static JsonElement? Element(object? source)
    {
        if (source == null) return null;
        try
        {
            return source is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(source, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? Bool(JsonElement? source, string propertyName)
    {
        if (!Property(source, propertyName, out var property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int ArrayCount(JsonElement? source, string propertyName) =>
        Property(source, propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.GetArrayLength()
            : 0;

    private static bool Property(JsonElement? source, string name, out JsonElement value)
    {
        if (source is { ValueKind: JsonValueKind.Object })
        {
            foreach (var property in source.Value.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
