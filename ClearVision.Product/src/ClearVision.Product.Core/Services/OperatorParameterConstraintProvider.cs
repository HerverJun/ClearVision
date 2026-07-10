using System.Collections.ObjectModel;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public static class OperatorParameterRequiredPolicies
{
    public const string Metadata = "metadata";
    public const string Required = "required";
    public const string Optional = "optional";
}

public static class OperatorParameterConditionComparisons
{
    public const string Equal = "equals";
    public const string NotEquals = "not-equals";
    public const string Empty = "empty";
    public const string NotEmpty = "not-empty";
}

public sealed record OperatorParameterCondition(
    string Parameter,
    string Comparison,
    object? Value = null);

public sealed record OperatorParameterConditionSet(
    IReadOnlyList<OperatorParameterCondition>? All = null,
    IReadOnlyList<OperatorParameterCondition>? Any = null);

public sealed record OperatorParameterConstraint(
    string Parameter,
    string RequiredPolicy,
    OperatorParameterConditionSet? RequiredWhen,
    OperatorParameterConditionSet? EnabledWhen,
    OperatorParameterConditionSet? DisabledWhen,
    string? AtLeastOneGroup,
    string? MutuallyExclusiveGroup,
    string? AliasFor,
    bool Deprecated,
    string? ResourceKind,
    string ReasonCode);

public interface IOperatorParameterConstraintProvider
{
    IReadOnlyList<OperatorParameterConstraint> GetConstraints(OperatorType operatorType);
}

public sealed class OperatorParameterConstraintProvider : IOperatorParameterConstraintProvider
{
    public static OperatorParameterConstraintProvider Instance { get; } = new();

    private static readonly IReadOnlyDictionary<OperatorType, IReadOnlyList<OperatorParameterConstraint>> Constraints =
        new ReadOnlyDictionary<OperatorType, IReadOnlyList<OperatorParameterConstraint>>(
            new Dictionary<OperatorType, IReadOnlyList<OperatorParameterConstraint>>
            {
                [OperatorType.ImageAcquisition] = ImageAcquisitionConstraints(),
                [OperatorType.DeepLearning] = DeepLearningConstraints(),
                [OperatorType.EdgeDetection] = EdgeDetectionConstraints(),
                [OperatorType.ResultOutput] = ResultOutputConstraints(),
                [OperatorType.BlobAnalysis] = BlobAnalysisConstraints()
            });

    public IReadOnlyList<OperatorParameterConstraint> GetConstraints(OperatorType operatorType)
    {
        var canonical = OperatorTypeAliasResolver.Resolve(operatorType);
        return Constraints.TryGetValue(canonical, out var constraints) ? constraints : [];
    }

    private static IReadOnlyList<OperatorParameterConstraint> ImageAcquisitionConstraints()
    {
        var fileMode = All(Equals("SourceType", "File"));
        var cameraMode = All(Equals("SourceType", "Camera"));
        return
        [
            Constraint("SourceType", OperatorParameterRequiredPolicies.Required, reasonCode: "IMAGE_SOURCE_TYPE_REQUIRED"),
            Constraint(
                "FilePath",
                OperatorParameterRequiredPolicies.Optional,
                requiredWhen: fileMode,
                disabledWhen: cameraMode,
                resourceKind: "image_file",
                reasonCode: "IMAGE_FILE_REQUIRED_FOR_FILE_SOURCE"),
            Constraint(
                "CameraId",
                OperatorParameterRequiredPolicies.Optional,
                requiredWhen: cameraMode,
                disabledWhen: fileMode,
                atLeastOneGroup: "image-camera-source",
                resourceKind: "camera_binding",
                reasonCode: "IMAGE_CAMERA_REQUIRED_FOR_CAMERA_SOURCE"),
            Constraint(
                "CameraBindingId",
                OperatorParameterRequiredPolicies.Optional,
                requiredWhen: cameraMode,
                disabledWhen: fileMode,
                atLeastOneGroup: "image-camera-source",
                aliasFor: "CameraId",
                resourceKind: "camera_binding",
                reasonCode: "IMAGE_CAMERA_BINDING_ALIAS"),
            Constraint("ExposureTime", disabledWhen: fileMode, reasonCode: "IMAGE_CAMERA_SETTING_DISABLED_FOR_FILE_SOURCE"),
            Constraint("Gain", disabledWhen: fileMode, reasonCode: "IMAGE_CAMERA_SETTING_DISABLED_FOR_FILE_SOURCE"),
            Constraint("TriggerMode", disabledWhen: fileMode, reasonCode: "IMAGE_CAMERA_SETTING_DISABLED_FOR_FILE_SOURCE"),
            Constraint("sourceType", OperatorParameterRequiredPolicies.Optional, aliasFor: "SourceType", deprecated: true, reasonCode: "IMAGE_SOURCE_TYPE_LEGACY_ALIAS"),
            Constraint("cameraId", OperatorParameterRequiredPolicies.Optional, aliasFor: "CameraId", deprecated: true, reasonCode: "IMAGE_CAMERA_ID_LEGACY_ALIAS")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> DeepLearningConstraints()
    {
        return
        [
            Constraint(
                "ModelPath",
                OperatorParameterRequiredPolicies.Required,
                atLeastOneGroup: "deep-learning-model-source",
                mutuallyExclusiveGroup: "deep-learning-model-source",
                resourceKind: "model_resource",
                reasonCode: "DEEP_LEARNING_MODEL_SOURCE_REQUIRED"),
            Constraint(
                "ModelId",
                OperatorParameterRequiredPolicies.Required,
                atLeastOneGroup: "deep-learning-model-source",
                mutuallyExclusiveGroup: "deep-learning-model-source",
                resourceKind: "model_resource",
                reasonCode: "DEEP_LEARNING_MODEL_SOURCE_REQUIRED"),
            Constraint(
                "ModelCatalogPath",
                OperatorParameterRequiredPolicies.Optional,
                disabledWhen: Any(Empty("ModelId"), NotEmpty("ModelPath")),
                resourceKind: "model_catalog",
                reasonCode: "DEEP_LEARNING_CATALOG_REQUIRES_MODEL_ID"),
            Constraint(
                "LabelsPath",
                OperatorParameterRequiredPolicies.Optional,
                resourceKind: "model_labels",
                reasonCode: "DEEP_LEARNING_LABELS_OPTIONAL_FALLBACK"),
            Constraint(
                "GpuDeviceId",
                disabledWhen: All(Equals("UseGpu", false)),
                reasonCode: "DEEP_LEARNING_GPU_DEVICE_DISABLED_WITHOUT_GPU"),
            Constraint(
                "EnableInternalNms",
                disabledWhen: All(Equals("OutputFormat", "EndToEndNms")),
                reasonCode: "DEEP_LEARNING_MODEL_OWNS_END_TO_END_NMS"),
            Constraint(
                "NmsIouThreshold",
                requiredWhen: All(Equals("OutputFormat", "RawYolo"), Equals("EnableInternalNms", true)),
                disabledWhen: Any(Equals("OutputFormat", "EndToEndNms"), Equals("EnableInternalNms", false)),
                reasonCode: "DEEP_LEARNING_NMS_THRESHOLD_ACTIVE_FOR_INTERNAL_NMS")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> EdgeDetectionConstraints()
    {
        var onnxMode = All(Equals("Method", "OnnxEdge"));
        var cannyMode = All(NotEquals("Method", "OnnxEdge"));
        return
        [
            Constraint(
                "EdgeModelPath",
                OperatorParameterRequiredPolicies.Optional,
                requiredWhen: onnxMode,
                disabledWhen: cannyMode,
                atLeastOneGroup: "edge-model-source",
                mutuallyExclusiveGroup: "edge-model-source",
                resourceKind: "model_resource",
                reasonCode: "EDGE_ONNX_MODEL_SOURCE_REQUIRED"),
            Constraint(
                "EdgeModelId",
                OperatorParameterRequiredPolicies.Optional,
                requiredWhen: onnxMode,
                disabledWhen: cannyMode,
                atLeastOneGroup: "edge-model-source",
                mutuallyExclusiveGroup: "edge-model-source",
                resourceKind: "model_resource",
                reasonCode: "EDGE_ONNX_MODEL_SOURCE_REQUIRED"),
            Constraint(
                "ModelCatalogPath",
                OperatorParameterRequiredPolicies.Optional,
                disabledWhen: Any(NotEquals("Method", "OnnxEdge"), Empty("EdgeModelId"), NotEmpty("EdgeModelPath")),
                resourceKind: "model_catalog",
                reasonCode: "EDGE_MODEL_CATALOG_REQUIRES_MODEL_ID"),
            Constraint(
                "EdgeBinarizationThreshold",
                OperatorParameterRequiredPolicies.Optional,
                disabledWhen: cannyMode,
                reasonCode: "EDGE_BINARIZATION_ONLY_FOR_ONNX")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> ResultOutputConstraints()
    {
        return
        [
            Constraint(
                "SaveToFile",
                resourceKind: "output_file",
                reasonCode: "RESULT_OUTPUT_OPTIONAL_INTERNAL_FILE_WRITE")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> BlobAnalysisConstraints()
    {
        var colorFilterDisabled = All(Equals("EnableColorFilter", false));
        return
        [
            Constraint(
                "FeatureFilter",
                OperatorParameterRequiredPolicies.Optional,
                reasonCode: "BLOB_FEATURE_FILTER_OPTIONAL"),
            Constraint("HueLow", disabledWhen: colorFilterDisabled, reasonCode: "BLOB_HSV_ONLY_WITH_COLOR_FILTER"),
            Constraint("HueHigh", disabledWhen: colorFilterDisabled, reasonCode: "BLOB_HSV_ONLY_WITH_COLOR_FILTER"),
            Constraint("SatLow", disabledWhen: colorFilterDisabled, reasonCode: "BLOB_HSV_ONLY_WITH_COLOR_FILTER"),
            Constraint("SatHigh", disabledWhen: colorFilterDisabled, reasonCode: "BLOB_HSV_ONLY_WITH_COLOR_FILTER"),
            Constraint("ValLow", disabledWhen: colorFilterDisabled, reasonCode: "BLOB_HSV_ONLY_WITH_COLOR_FILTER"),
            Constraint("ValHigh", disabledWhen: colorFilterDisabled, reasonCode: "BLOB_HSV_ONLY_WITH_COLOR_FILTER")
        ];
    }

    private static OperatorParameterConstraint Constraint(
        string parameter,
        string requiredPolicy = OperatorParameterRequiredPolicies.Metadata,
        OperatorParameterConditionSet? requiredWhen = null,
        OperatorParameterConditionSet? enabledWhen = null,
        OperatorParameterConditionSet? disabledWhen = null,
        string? atLeastOneGroup = null,
        string? mutuallyExclusiveGroup = null,
        string? aliasFor = null,
        bool deprecated = false,
        string? resourceKind = null,
        string reasonCode = "PARAMETER_CONSTRAINT")
    {
        return new OperatorParameterConstraint(
            parameter,
            requiredPolicy,
            requiredWhen,
            enabledWhen,
            disabledWhen,
            atLeastOneGroup,
            mutuallyExclusiveGroup,
            aliasFor,
            deprecated,
            resourceKind,
            reasonCode);
    }

    private static OperatorParameterCondition Equals(string parameter, object value) =>
        new(parameter, OperatorParameterConditionComparisons.Equal, value);

    private static OperatorParameterCondition NotEquals(string parameter, object value) =>
        new(parameter, OperatorParameterConditionComparisons.NotEquals, value);

    private static OperatorParameterCondition Empty(string parameter) =>
        new(parameter, OperatorParameterConditionComparisons.Empty);

    private static OperatorParameterCondition NotEmpty(string parameter) =>
        new(parameter, OperatorParameterConditionComparisons.NotEmpty);

    private static OperatorParameterConditionSet All(params OperatorParameterCondition[] conditions) =>
        new(All: conditions);

    private static OperatorParameterConditionSet Any(params OperatorParameterCondition[] conditions) =>
        new(Any: conditions);
}

public sealed record OperatorParameterConstraintState(
    OperatorParameterConstraint Constraint,
    bool EffectiveRequired,
    bool EffectiveDisabled);

public sealed record OperatorParameterConstraintViolation(
    string Code,
    IReadOnlyList<string> ParameterNames,
    string? ResourceKind,
    string ReasonCode);

public static class OperatorParameterConstraintEvaluator
{
    public static IReadOnlyList<OperatorParameterConstraintState> ResolveStates(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var normalizedValues = NormalizeValues(metadata, values);
        var metadataByName = metadata.Parameters.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        return metadata.ParameterConstraints
            .Select(constraint =>
            {
                var rawRequired = metadataByName.TryGetValue(constraint.Parameter, out var parameter) && parameter.IsRequired;
                var required = constraint.RequiredPolicy switch
                {
                    OperatorParameterRequiredPolicies.Required => true,
                    OperatorParameterRequiredPolicies.Optional => false,
                    _ => rawRequired
                };

                if (constraint.RequiredWhen is not null)
                {
                    required = Evaluate(constraint.RequiredWhen, normalizedValues);
                }

                var enabled = constraint.EnabledWhen is null || Evaluate(constraint.EnabledWhen, normalizedValues);
                var disabled = !enabled ||
                               (constraint.DisabledWhen is not null && Evaluate(constraint.DisabledWhen, normalizedValues));
                return new OperatorParameterConstraintState(
                    constraint,
                    EffectiveRequired: required && !disabled,
                    EffectiveDisabled: disabled);
            })
            .ToArray();
    }

    public static IReadOnlyList<OperatorParameterConstraintViolation> Validate(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values)
    {
        var normalizedValues = NormalizeValues(metadata, values);
        var states = ResolveStates(metadata, normalizedValues);
        var violations = new List<OperatorParameterConstraintViolation>();

        foreach (var group in states
                     .Where(item => !string.IsNullOrWhiteSpace(item.Constraint.AtLeastOneGroup))
                     .GroupBy(item => item.Constraint.AtLeastOneGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var active = group.Where(item => item.EffectiveRequired && !item.EffectiveDisabled).ToArray();
            if (active.Length == 0)
            {
                continue;
            }

            var names = group.Select(item => item.Constraint.Parameter).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Any(name => !IsMissing(GetValue(normalizedValues, name))))
            {
                continue;
            }

            var primary = active[0].Constraint;
            violations.Add(new OperatorParameterConstraintViolation(
                "at-least-one",
                names,
                primary.ResourceKind,
                primary.ReasonCode));
        }

        foreach (var group in states
                     .Where(item => !string.IsNullOrWhiteSpace(item.Constraint.MutuallyExclusiveGroup))
                     .GroupBy(item => item.Constraint.MutuallyExclusiveGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var configured = group
                .Select(item => item.Constraint.Parameter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !IsMissing(GetValue(normalizedValues, name)))
                .ToArray();
            if (configured.Length < 2)
            {
                continue;
            }

            var primary = group.First().Constraint;
            violations.Add(new OperatorParameterConstraintViolation(
                "mutually-exclusive",
                configured,
                primary.ResourceKind,
                primary.ReasonCode));
        }

        foreach (var state in states.Where(item =>
                     item.EffectiveRequired &&
                     string.IsNullOrWhiteSpace(item.Constraint.AtLeastOneGroup) &&
                     IsMissing(GetValue(normalizedValues, item.Constraint.Parameter))))
        {
            violations.Add(new OperatorParameterConstraintViolation(
                "required",
                [state.Constraint.Parameter],
                state.Constraint.ResourceKind,
                state.Constraint.ReasonCode));
        }

        return violations
            .GroupBy(item => $"{item.Code}|{string.Join('|', item.ParameterNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public static bool IsMissing(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ||
               text.StartsWith("<pending", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("todo", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> NormalizeValues(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values)
    {
        var normalized = metadata.Parameters
            .Where(parameter => parameter.DefaultValue is not null)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.DefaultValue,
                StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            normalized[pair.Key] = pair.Value;
        }

        foreach (var alias in metadata.ParameterConstraints.Where(item => !string.IsNullOrWhiteSpace(item.AliasFor)))
        {
            if (!normalized.ContainsKey(alias.AliasFor!) && normalized.TryGetValue(alias.Parameter, out var aliasValue))
            {
                normalized[alias.AliasFor!] = aliasValue;
            }

            if (!normalized.ContainsKey(alias.Parameter) && normalized.TryGetValue(alias.AliasFor!, out var canonicalValue))
            {
                normalized[alias.Parameter] = canonicalValue;
            }
        }

        return normalized;
    }

    private static bool Evaluate(
        OperatorParameterConditionSet set,
        IReadOnlyDictionary<string, object?> values)
    {
        var all = set.All;
        var any = set.Any;
        var allMatches = all is null || all.Count == 0 || all.All(condition => Evaluate(condition, values));
        var anyMatches = any is null || any.Count == 0 || any.Any(condition => Evaluate(condition, values));
        return allMatches && anyMatches;
    }

    private static bool Evaluate(
        OperatorParameterCondition condition,
        IReadOnlyDictionary<string, object?> values)
    {
        var value = GetValue(values, condition.Parameter);
        return condition.Comparison switch
        {
            OperatorParameterConditionComparisons.Equal => ValuesEqual(value, condition.Value),
            OperatorParameterConditionComparisons.NotEquals => !ValuesEqual(value, condition.Value),
            OperatorParameterConditionComparisons.Empty => IsMissing(value),
            OperatorParameterConditionComparisons.NotEmpty => !IsMissing(value),
            _ => false
        };
    }

    private static object? GetValue(IReadOnlyDictionary<string, object?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool == rightBool;
        }

        if (bool.TryParse(left?.ToString(), out var parsedLeft) &&
            bool.TryParse(right?.ToString(), out var parsedRight))
        {
            return parsedLeft == parsedRight;
        }

        return string.Equals(left?.ToString()?.Trim(), right?.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
