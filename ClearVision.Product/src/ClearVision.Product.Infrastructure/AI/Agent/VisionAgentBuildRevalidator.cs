using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed class VisionAgentBuildRevalidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<VisionAgentPublicBuildResultV1> RevalidateAsync(
        VisionAgentBuildRevalidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var flow = JsonSerializer.Deserialize<OperatorFlowDto>(request.CandidateFlowJson, JsonOptions)
            ?? throw new InvalidOperationException("Candidate flow could not be restored for revalidation.");
        var fingerprint = ExecutionFlowIdentity.ComputeFlowHash(flow.ToEntity());
        if (!string.Equals(fingerprint, request.Build.CandidateFlowFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Candidate flow fingerprint no longer matches the submitted Build.");
        }

        var parameterIssues = new List<string>();
        var mappings = request.Build.ParameterMapping
            .Select(mapping => ApplyParameterValue(flow, mapping, request.ParameterValues, parameterIssues))
            .ToList();
        var decisions = request.ResourceDecisions
            .Where(VisionAgentResourceAuthority.IsTrustedCameraBindingDecision)
            .GroupBy(decision => decision.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var appliedDecisionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings.Where(item => item.ResourceDependent))
        {
            if (decisions.TryGetValue(mapping.ResourceCanonicalId, out var decision))
            {
                if (!decision.ResourceType.Equals(mapping.ResourceKind, StringComparison.OrdinalIgnoreCase) ||
                    !decision.OperatorId.Equals(mapping.TempId, StringComparison.OrdinalIgnoreCase) ||
                    !decision.ParameterName.Equals(mapping.ParameterName, StringComparison.OrdinalIgnoreCase) ||
                    !ApplyToFlow(flow, mapping, decision.ResourceKey))
                {
                    parameterIssues.Add($"{mapping.CanonicalKey}:resource_authority_mismatch");
                    continue;
                }
                appliedDecisionIds.Add(decision.CanonicalId);
            }
        }

        var context = new VisionAgentToolContext
        {
            UserDescription = "Revalidate an existing AI Build candidate.",
            AgentRunId = request.Build.RunId,
            ExistingFlowJson = JsonSerializer.Serialize(flow, JsonOptions)
        };
        var arguments = JsonSerializer.SerializeToElement(new { flow }, JsonOptions);
        var validationTool = new FlowValidationTool();
        var dryRunTool = new DryRunFlowTool();
        var validation = await validationTool.ExecuteAsync(context, arguments, cancellationToken);
        var dryRun = await dryRunTool.ExecuteAsync(context, arguments, cancellationToken);
        var missingResources = request.Build.MissingResources
            .Where(resource => !appliedDecisionIds.Contains(resource.CanonicalId))
            .ToList();
        var pendingParameters = mappings.Count(item => item.Pending && !item.ResourceDependent);
        var validationBlockers = VisionAgentBuildSupport.ReadCount(validation.Data, "blockingIssues") + parameterIssues.Count;
        var dryRunSucceeded = VisionAgentBuildSupport.ReadBool(dryRun.Data, "dryRunSucceeded") == true;
        var packageReady = validationBlockers == 0 && dryRunSucceeded && pendingParameters == 0 && missingResources.Count == 0;
        var packageReadiness = VisionAgentToolResult.Ok(new
        {
            readyForDeployment = packageReady,
            pendingParameters = mappings.Where(item => item.Pending).Select(item => item.CanonicalKey).ToList(),
            missingResources,
            parameterIssues,
            metadataOnly = true
        });
        var deploymentBlockers = mappings.Where(item => item.Pending).Select(item => $"parameter:{item.CanonicalKey}")
            .Concat(missingResources.Select(item => $"resource:{item.CanonicalId}"))
            .Concat(parameterIssues.Select(issue => $"parameter_validation:{issue}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var diff = request.Build.WorkflowDiff with
        {
            PendingParameters = mappings.Where(item => item.Pending).Select(item => item.CanonicalKey).ToList(),
            MissingResources = missingResources.Select(item => item.CanonicalId).ToList(),
            ValidationFailures = parameterIssues.ToList(),
            DeploymentBlockers = deploymentBlockers
        };
        var gate = new ApplyGateResolver().Build(validation, dryRun, packageReadiness, diff).Payload;
        if (parameterIssues.Count > 0)
        {
            gate = gate with
            {
                CanvasApplyReady = false,
                RuntimeDraftReady = false,
                DeploymentReady = false,
                Blocked = true,
                Status = "blocked",
                ApplyBlockers = gate.ApplyBlockers.Concat(parameterIssues).Distinct().ToList(),
                FirstFixRecommendation = "请修正参数类型、范围或枚举值后重新校验。"
            };
        }
        var firstFix = parameterIssues.Count > 0
            ? "请修正参数类型、范围或枚举值后重新校验。"
            : pendingParameters > 0
                ? "请确认所有普通待处理参数后重新校验。"
                : missingResources.Count > 0
                    ? "请处理仍缺失的 canonical 资源后重新校验。"
                    : request.Build.Validation.FirstFixRecommendation;
        var projectedValidation = VisionAgentPublicBuildProjector.ProjectValidation(
            validation.Data,
            dryRun.Data,
            packageReadiness.Data,
            gate,
            pendingParameters,
            missingResources.Count,
            firstFix);

        return request.Build with
        {
            AnswerRevision = Math.Max(0, request.AnswerRevision),
            ResourceRevision = Math.Max(0, request.ResourceRevision),
            ParameterMapping = mappings,
            MissingResources = missingResources,
            WorkflowDiff = diff,
            Validation = projectedValidation
        };
    }

    private static VisionAgentParameterMapping ApplyParameterValue(
        OperatorFlowDto flow,
        VisionAgentParameterMapping mapping,
        IReadOnlyDictionary<string, JsonElement> values,
        ICollection<string> issues)
    {
        if (!values.TryGetValue(mapping.CanonicalKey, out var value)) return mapping;
        if (mapping.ResourceDependent)
        {
            issues.Add($"{mapping.CanonicalKey}:resource_parameter_requires_authority");
            return mapping;
        }
        if (!TryNormalize(value, mapping, out var normalized, out var error))
        {
            issues.Add($"{mapping.CanonicalKey}:{error}");
            return mapping;
        }

        if (!ApplyToFlow(flow, mapping, normalized))
        {
            issues.Add($"{mapping.CanonicalKey}:parameter_identity_unknown");
            return mapping;
        }
        return mapping with
        {
            Value = normalized,
            HasExplicitValue = true,
            ValueSummary = normalized == null ? "null" : Convert.ToString(normalized, CultureInfo.InvariantCulture) ?? string.Empty,
            Source = "user_confirmed_parameter",
            Pending = false,
            SuggestedReason = "已由用户明确确认。",
            Impact = "确认值仅用于当前 AI 候选重新校验，尚未写入正式工程。"
        };
    }

    private static bool TryNormalize(
        JsonElement value,
        VisionAgentParameterMapping mapping,
        out object? normalized,
        out string error)
    {
        normalized = null;
        error = string.Empty;
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (mapping.IsRequired)
            {
                error = "required_value_missing";
                return false;
            }
            return true;
        }

        switch (mapping.DataType)
        {
            case "bool":
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    normalized = value.GetBoolean();
                    return true;
                }
                error = "boolean_required";
                return false;
            case "int":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer))
                {
                    error = "integer_required";
                    return false;
                }
                normalized = integer;
                return InRange(integer, mapping, out error);
            case "double":
            case "number":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
                {
                    error = "number_required";
                    return false;
                }
                normalized = number;
                return InRange(number, mapping, out error);
            case "string":
            case "text":
            case "guid":
            case "enum":
            case "file":
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = "string_required";
                    return false;
                }
                var text = value.GetString() ?? string.Empty;
                if (mapping.IsRequired && string.IsNullOrWhiteSpace(text))
                {
                    error = "required_value_missing";
                    return false;
                }
                if (mapping.Options.Count > 0 && !mapping.Options.Any(option =>
                        option.Value.Equals(text, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "enum_value_invalid";
                    return false;
                }
                normalized = text;
                return true;
            default:
                error = "parameter_type_unsupported";
                return false;
        }
    }

    private static bool InRange(double value, VisionAgentParameterMapping mapping, out string error)
    {
        error = string.Empty;
        if (TryDouble(mapping.MinValue, out var min) && value < min)
        {
            error = "below_minimum";
            return false;
        }
        if (TryDouble(mapping.MaxValue, out var max) && value > max)
        {
            error = "above_maximum";
            return false;
        }
        return true;
    }

    private static bool TryDouble(object? value, out double number) =>
        double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out number);

    private static bool ApplyToFlow(OperatorFlowDto flow, VisionAgentParameterMapping mapping, object? value)
    {
        var target = flow.Operators.FirstOrDefault(op =>
            ReadTempId(op).Equals(mapping.TempId, StringComparison.OrdinalIgnoreCase));
        var parameter = target?.Parameters.FirstOrDefault(item =>
            item.Name.Equals(mapping.ParameterName, StringComparison.OrdinalIgnoreCase));
        if (parameter == null) return false;
        parameter.Value = value;
        return true;
    }

    private static string ReadTempId(OperatorDto op)
    {
        if (op.Metadata == null || !op.Metadata.TryGetValue("agentTempId", out var value) || value == null)
        {
            return string.Empty;
        }
        return value is JsonElement element && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
