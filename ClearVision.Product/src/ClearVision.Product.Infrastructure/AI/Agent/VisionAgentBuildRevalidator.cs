using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;

namespace ClearVision.Product.Infrastructure.AI.Agent;

internal sealed class VisionAgentBuildRevalidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<VisionAgentBuildRevalidationResult> RevalidateAsync(
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

        var artifactIdentity = ReadArtifactIdentity(flow, request.Build.PlanHash);
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
        for (var index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];
            if (!mapping.ResourceDependent) continue;
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
                mappings[index] = mapping with
                {
                    Value = decision.ResourceKey,
                    HasExplicitValue = true,
                    ValueSummary = decision.ValueSummary,
                    Source = VisionAgentResourceAuthority.CameraBindingSource,
                    Pending = false,
                    SuggestedReason = "已由相机绑定权威确认。",
                    Impact = "权威绑定仅用于当前 AI 候选重新校验，尚未写入正式工程。"
                };
            }
        }

        var context = new VisionAgentToolContext
        {
            UserDescription = "Revalidate an existing AI Build candidate.",
            AgentRunId = request.Build.RunId,
            ExistingFlowJson = JsonSerializer.Serialize(flow, JsonOptions)
        };
        var arguments = BuildValidationArguments(flow, artifactIdentity, string.Empty);
        var normalized = VisionAgentFlowDraftNormalizer.Normalize(arguments, context);
        var artifactFingerprint = string.Empty;
        var returnedFlowFingerprint = string.Empty;
        VisionTaskRouteAssessment? routeAssessment = null;
        if (artifactIdentity != null && normalized.Success)
        {
            var contractCatalog = new VisionAgentOperatorContractCatalog();
            var graph = WorkflowArtifactFingerprint.ToGraph(normalized.Flow, contractCatalog);
            artifactFingerprint = WorkflowArtifactFingerprint.Compute(
                artifactIdentity.PlanHash,
                artifactIdentity.CatalogVersion,
                artifactIdentity.BuildIntent,
                graph);
            routeAssessment = new VisionTaskRouteContractRegistry().Assess(artifactIdentity.TaskType, graph);
            StampArtifactEvidence(flow, artifactIdentity, artifactFingerprint, routeAssessment);
            returnedFlowFingerprint = WorkflowArtifactFingerprint.ComputeCanvasProjection(
                flow,
                artifactIdentity.PlanHash,
                artifactIdentity.CatalogVersion,
                artifactIdentity.BuildIntent,
                graph,
                contractCatalog);
            context = context with { ExistingFlowJson = JsonSerializer.Serialize(flow, JsonOptions) };
            arguments = BuildValidationArguments(flow, artifactIdentity, artifactFingerprint);
        }

        var validationTool = new FlowValidationTool();
        var dryRunTool = new DryRunFlowTool();
        var validation = await validationTool.ExecuteAsync(context, arguments, cancellationToken);
        var dryRun = await dryRunTool.ExecuteAsync(context, arguments, cancellationToken);
        var missingResources = request.Build.MissingResources
            .Where(resource => !appliedDecisionIds.Contains(resource.CanonicalId))
            .ToList();
        var pendingParameters = mappings.Count(item => item.Pending && !item.ResourceDependent);
        var packageReadiness = await new RuntimePackagePrecheckTool().ExecuteAsync(
            context,
            BuildPrecheckArguments(arguments, validation, dryRun, decisions, appliedDecisionIds),
            cancellationToken);
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
        var gate = new ApplyGateResolver().Build(
            validation,
            dryRun,
            packageReadiness,
            diff,
            artifactFingerprint,
            routeAssessment,
            returnedFlowFingerprint).Payload;
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
                    : gate.Blocked
                        ? "候选缺少可验证的流程身份或任务路由证据，请重新构建。"
                        : request.Build.Validation.FirstFixRecommendation;
        var projectedValidation = VisionAgentPublicBuildProjector.ProjectValidation(
            validation.Data,
            dryRun.Data,
            packageReadiness.Data,
            gate,
            pendingParameters,
            missingResources.Count,
            firstFix);

        var candidateFlowJson = JsonSerializer.Serialize(flow, JsonOptions);
        var persistedCandidateFlow = JsonSerializer.Deserialize<OperatorFlowDto>(candidateFlowJson, JsonOptions)
            ?? throw new InvalidOperationException("Revalidated candidate flow could not be restored.");
        var candidateFlowFingerprint = ExecutionFlowIdentity.ComputeFlowHash(persistedCandidateFlow.ToEntity());
        var build = request.Build with
        {
            AnswerRevision = Math.Max(0, request.AnswerRevision),
            ResourceRevision = Math.Max(0, request.ResourceRevision),
            CandidateFlowFingerprint = candidateFlowFingerprint,
            ParameterMapping = mappings,
            MissingResources = missingResources,
            WorkflowDiff = diff,
            Validation = projectedValidation
        };

        return new VisionAgentBuildRevalidationResult
        {
            Build = build,
            CandidateFlowJson = candidateFlowJson
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

    private static JsonElement BuildValidationArguments(
        OperatorFlowDto flow,
        RevalidationArtifactIdentity? identity,
        string artifactFingerprint)
    {
        var tempIds = flow.Operators.ToDictionary(
            op => op.Id,
            ReadValidationTempId);
        var operators = flow.Operators.Select(op => new
        {
            tempId = tempIds[op.Id],
            operatorType = OperatorTypeAliasResolver.Resolve(op.Type).ToString(),
            parameters = op.Parameters
                .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value ?? group.Last().DefaultValue,
                    StringComparer.OrdinalIgnoreCase)
        }).ToList();
        var connections = flow.Connections.Select(connection =>
        {
            tempIds.TryGetValue(connection.SourceOperatorId, out var sourceTempId);
            tempIds.TryGetValue(connection.TargetOperatorId, out var targetTempId);
            var source = flow.Operators.FirstOrDefault(op => op.Id == connection.SourceOperatorId);
            var target = flow.Operators.FirstOrDefault(op => op.Id == connection.TargetOperatorId);
            return new
            {
                sourceTempId = sourceTempId ?? string.Empty,
                sourcePortName = source?.OutputPorts.FirstOrDefault(port => port.Id == connection.SourcePortId)?.Name ?? string.Empty,
                targetTempId = targetTempId ?? string.Empty,
                targetPortName = target?.InputPorts.FirstOrDefault(port => port.Id == connection.TargetPortId)?.Name ?? string.Empty
            };
        }).ToList();
        var entryOperatorTempId = operators.FirstOrDefault()?.tempId ?? string.Empty;

        return JsonSerializer.SerializeToElement(new
        {
            flow = new
            {
                operators,
                connections,
                entryOperatorTempId
            },
            entryOperatorTempId,
            planHash = identity?.PlanHash ?? string.Empty,
            catalogVersion = identity?.CatalogVersion ?? string.Empty,
            buildIntent = identity?.BuildIntent ?? string.Empty,
            artifactFingerprint
        }, JsonOptions);
    }

    private static JsonElement BuildPrecheckArguments(
        JsonElement validationArguments,
        VisionAgentToolResult validation,
        VisionAgentToolResult dryRun,
        IReadOnlyDictionary<string, VisionAgentResourceDecision> decisions,
        IReadOnlySet<string> appliedDecisionIds)
    {
        var confirmations = appliedDecisionIds
            .Where(decisions.ContainsKey)
            .Select(id => decisions[id])
            .Select(decision => new
            {
                resourceType = decision.ResourceType,
                operatorId = decision.OperatorId,
                parameterName = decision.ParameterName,
                resourceKey = decision.ResourceKey,
                metadataOnly = true
            })
            .ToList();

        return JsonSerializer.SerializeToElement(new
        {
            flow = validationArguments.GetProperty("flow"),
            entryOperatorTempId = validationArguments.GetProperty("entryOperatorTempId"),
            planHash = validationArguments.GetProperty("planHash"),
            catalogVersion = validationArguments.GetProperty("catalogVersion"),
            buildIntent = validationArguments.GetProperty("buildIntent"),
            artifactFingerprint = validationArguments.GetProperty("artifactFingerprint"),
            validationSummary = validation.Data,
            dryRunSummary = dryRun.Data,
            manualResourceConfirmations = confirmations,
            requireReplay = false
        }, JsonOptions);
    }

    private static RevalidationArtifactIdentity? ReadArtifactIdentity(
        OperatorFlowDto flow,
        string expectedPlanHash)
    {
        var planHash = ReadConsistentMetadata(flow, "agentPlanHash");
        var catalogVersion = ReadConsistentMetadata(flow, "agentCatalogVersion");
        var buildIntent = ReadConsistentMetadata(flow, "agentBuildIntent");
        var taskType = VisionTaskRouteContractRegistry.NormalizeTaskType(
            ReadConsistentMetadata(flow, "agentTaskType"));
        if (string.IsNullOrWhiteSpace(planHash) ||
            !planHash.Equals(expectedPlanHash?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(catalogVersion) ||
            string.IsNullOrWhiteSpace(buildIntent) ||
            string.IsNullOrWhiteSpace(taskType))
        {
            return null;
        }

        return new RevalidationArtifactIdentity(planHash, catalogVersion, buildIntent, taskType);
    }

    private static string ReadConsistentMetadata(OperatorFlowDto flow, string key)
    {
        if (flow.Operators.Count == 0)
        {
            return string.Empty;
        }

        var values = flow.Operators
            .Select(op => ReadMetadataString(op.Metadata, key))
            .ToList();
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            return string.Empty;
        }

        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : string.Empty;
    }

    private static string ReadMetadataString(
        IReadOnlyDictionary<string, object?>? metadata,
        string key)
    {
        if (metadata == null || !metadata.TryGetValue(key, out var value) || value == null)
        {
            return string.Empty;
        }

        return value is JsonElement element && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty
            : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static void StampArtifactEvidence(
        OperatorFlowDto flow,
        RevalidationArtifactIdentity identity,
        string artifactFingerprint,
        VisionTaskRouteAssessment routeAssessment)
    {
        foreach (var op in flow.Operators)
        {
            op.Metadata ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            op.Metadata["agentPlanHash"] = identity.PlanHash;
            op.Metadata["agentCatalogVersion"] = identity.CatalogVersion;
            op.Metadata["agentBuildIntent"] = identity.BuildIntent;
            op.Metadata["agentTaskType"] = routeAssessment.TaskType;
            op.Metadata["agentArtifactFingerprint"] = artifactFingerprint;
            op.Metadata["agentRouteSemanticsSatisfied"] = routeAssessment.Satisfied;
            op.Metadata["agentRouteContractVersion"] = routeAssessment.ContractVersion;
        }
    }

    private static string ReadValidationTempId(OperatorDto op)
    {
        var tempId = ReadTempId(op);
        return string.IsNullOrWhiteSpace(tempId) ? op.Id.ToString("D") : tempId;
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

    private sealed record RevalidationArtifactIdentity(
        string PlanHash,
        string CatalogVersion,
        string BuildIntent,
        string TaskType);
}
