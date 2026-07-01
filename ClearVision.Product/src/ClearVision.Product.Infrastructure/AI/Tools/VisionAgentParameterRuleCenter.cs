namespace ClearVision.Product.Infrastructure.AI.Tools;

internal enum VisionAgentParameterRuleScope
{
    FlowValidation,
    DeploymentPrecheck
}

internal static class VisionAgentParameterRuleCenter
{
    public static IReadOnlyList<VisionAgentMissingResource> CollectMissingResources(
        VisionAgentFlowDraft flow,
        VisionAgentParameterRuleScope scope)
    {
        var missingResources = new List<VisionAgentMissingResource>();

        foreach (var op in flow.Operators)
        {
            AddImageAcquisitionResources(op, missingResources);
            AddDeepLearningResources(op, missingResources);
            AddTemplateMatchingResources(op, missingResources);
            AddResultOutputResources(op, missingResources);
            AddPlcResources(op, missingResources);

            if (scope == VisionAgentParameterRuleScope.DeploymentPrecheck)
            {
                AddDeploymentOnlyResources(op, missingResources);
            }
        }

        return missingResources;
    }

    private static void AddImageAcquisitionResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!IsOperatorType(op, "ImageAcquisition"))
        {
            return;
        }

        var sourceType = GetParameter(op.Parameters, "SourceType");
        if (IsFileSource(sourceType))
        {
            AddMissingAtLeastOne(
                op,
                missingResources,
                "camera_binding",
                ["FilePath"],
                "ImageAcquisition.FilePath is not configured for file source.");
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "camera_binding",
            ["CameraBindingId", "CameraId"],
            "ImageAcquisition.CameraBindingId is not configured.");
    }

    private static void AddDeepLearningResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!IsOperatorType(op, "DeepLearning") &&
            !IsOperatorType(op, "OnnxInference") &&
            !IsOperatorType(op, "SemanticSegmentation") &&
            !IsOperatorType(op, "AnomalyDetection"))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "model_resource",
            ["ModelPath", "ModelId", "ModelCatalogPath"],
            $"{op.OperatorType}.ModelPath or ModelId is not configured.");
    }

    private static void AddTemplateMatchingResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!IsOperatorType(op, "TemplateMatching"))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "template_artifact",
            ["Template", "TemplateId", "TemplatePath"],
            "TemplateMatching.Template input or TemplateId is not configured.");
    }

    private static void AddResultOutputResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!IsOperatorType(op, "ResultOutput"))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "output_channel",
            ["OutputChannelId", "OutputChannel", "Channel"],
            "ResultOutput output channel metadata is not configured.");

        var channel = GetFirstPresentParameter(op.Parameters, "OutputChannel", "OutputChannelId", "Channel");
        if (string.Equals(channel, "file", StringComparison.OrdinalIgnoreCase))
        {
            AddMissingAtLeastOne(
                op,
                missingResources,
                "output_file",
                ["FilePath", "OutputPath"],
                "ResultOutput.FilePath is not configured for file output.");
        }

        if (string.Equals(channel, "plc", StringComparison.OrdinalIgnoreCase))
        {
            AddMissingAtLeastOne(
                op,
                missingResources,
                "plc_address",
                ["PlcAddress", "PLCParameters"],
                "ResultOutput.PlcAddress is not configured for PLC output.");
        }
    }

    private static void AddPlcResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        if (!op.OperatorType.Contains("Plc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddMissingAtLeastOne(
            op,
            missingResources,
            "plc_address",
            ["PlcAddress", "PLCParameters"],
            $"{op.OperatorType} PLC parameters are missing or pending.");
    }

    private static void AddDeploymentOnlyResources(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources)
    {
        // Reserved for future static deployment precheck-only rules. Runtime
        // resources stay metadata-only; no package, Station, PLC, or adapter is touched here.
    }

    private static void AddMissingAtLeastOne(
        VisionAgentFlowOperator op,
        List<VisionAgentMissingResource> missingResources,
        string resourceKind,
        IReadOnlyList<string> parameterNames,
        string message)
    {
        if (parameterNames.Any(name => !IsMissingParameter(op.Parameters, name)))
        {
            return;
        }

        AddMissingResource(
            missingResources,
            resourceKind,
            parameterNames[0],
            op.TempId,
            op.OperatorType,
            message);
    }

    private static void AddMissingResource(
        List<VisionAgentMissingResource> missingResources,
        string resourceKind,
        string parameterName,
        string tempId,
        string operatorType,
        string message)
    {
        if (missingResources.Any(resource =>
                string.Equals(resource.TempId, tempId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(resource.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        missingResources.Add(new VisionAgentMissingResource(
            resourceKind,
            parameterName,
            tempId,
            operatorType,
            message));
    }

    private static string? GetFirstPresentParameter(
        IReadOnlyDictionary<string, string?> parameters,
        params string[] parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            var value = GetParameter(parameters, parameterName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetParameter(
        IReadOnlyDictionary<string, string?> parameters,
        string parameterName)
    {
        return parameters.TryGetValue(parameterName, out var value) ? value : null;
    }

    private static bool IsOperatorType(VisionAgentFlowOperator op, string operatorType)
    {
        return string.Equals(op.OperatorType, operatorType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileSource(string? sourceType)
    {
        return string.Equals(sourceType, "file", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceType, "image", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceType, "path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingParameter(
        IReadOnlyDictionary<string, string?> parameters,
        string parameterName)
    {
        if (!parameters.TryGetValue(parameterName, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.StartsWith("<pending", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("todo", StringComparison.OrdinalIgnoreCase);
    }
}
