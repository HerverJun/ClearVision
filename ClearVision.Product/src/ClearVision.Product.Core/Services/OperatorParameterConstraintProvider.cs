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
                [OperatorType.BlobAnalysis] = BlobAnalysisConstraints(),
                [OperatorType.ImageSave] = ImageSaveConstraints(),
                [OperatorType.TextSave] = TextSaveConstraints(),
                [OperatorType.MitsubishiMcCommunication] = MitsubishiMcCommunicationConstraints(),
                [OperatorType.TcpCommunication] = TcpCommunicationConstraints()
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
                aliasFor: "CameraId",
                deprecated: true,
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

    private static IReadOnlyList<OperatorParameterConstraint> ImageSaveConstraints()
    {
        return
        [
            Constraint(
                "Directory",
                OperatorParameterRequiredPolicies.Required,
                resourceKind: "output_file",
                reasonCode: "IMAGE_SAVE_DIRECTORY_REQUIRED"),
            Constraint(
                "FileNameTemplate",
                OperatorParameterRequiredPolicies.Required,
                reasonCode: "IMAGE_SAVE_FILE_NAME_REQUIRED"),
            Constraint("Quality", reasonCode: "IMAGE_SAVE_QUALITY"),
            Constraint(
                "FolderPath",
                OperatorParameterRequiredPolicies.Optional,
                aliasFor: "Directory",
                deprecated: true,
                resourceKind: "output_file",
                reasonCode: "IMAGE_SAVE_DIRECTORY_LEGACY_ALIAS"),
            Constraint(
                "FileName",
                OperatorParameterRequiredPolicies.Optional,
                aliasFor: "FileNameTemplate",
                deprecated: true,
                reasonCode: "IMAGE_SAVE_FILE_NAME_LEGACY_ALIAS"),
            Constraint(
                "JpegQuality",
                OperatorParameterRequiredPolicies.Optional,
                aliasFor: "Quality",
                deprecated: true,
                reasonCode: "IMAGE_SAVE_QUALITY_LEGACY_ALIAS")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> TextSaveConstraints()
    {
        return
        [
            Constraint(
                "FilePath",
                OperatorParameterRequiredPolicies.Required,
                resourceKind: "output_file",
                reasonCode: "TEXT_SAVE_FILE_PATH_REQUIRED")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> MitsubishiMcCommunicationConstraints()
    {
        var operatorEndpointRequired = All(Equals("UseGlobalFallback", false));
        var readOperation = All(Equals("Operation", "Read"));
        var writeOperation = All(Equals("Operation", "Write"));
        var pollingOperation = All(
            Equals("Operation", "Read"),
            Equals("PollingMode", "WaitForValue"));

        return
        [
            Constraint(
                "IpAddress",
                requiredWhen: operatorEndpointRequired,
                resourceKind: "plc_endpoint",
                reasonCode: "MITSUBISHI_OPERATOR_IP_REQUIRED_WITHOUT_GLOBAL_FALLBACK"),
            Constraint(
                "Port",
                requiredWhen: operatorEndpointRequired,
                reasonCode: "MITSUBISHI_OPERATOR_PORT_REQUIRED_WITHOUT_GLOBAL_FALLBACK"),
            Constraint(
                "Address",
                OperatorParameterRequiredPolicies.Required,
                resourceKind: "plc_address",
                reasonCode: "MITSUBISHI_PLC_ADDRESS_REQUIRED"),
            Constraint(
                "Length",
                enabledWhen: readOperation,
                reasonCode: "MITSUBISHI_READ_LENGTH_ONLY_FOR_READ"),
            Constraint(
                "WriteValue",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: writeOperation,
                reasonCode: "MITSUBISHI_WRITE_VALUE_ONLY_FOR_WRITE"),
            Constraint(
                "PollingMode",
                enabledWhen: readOperation,
                reasonCode: "MITSUBISHI_POLLING_ONLY_FOR_READ"),
            Constraint(
                "PollingCondition",
                enabledWhen: pollingOperation,
                reasonCode: "MITSUBISHI_POLLING_CONDITION_ONLY_WHEN_WAITING"),
            Constraint(
                "PollingValue",
                enabledWhen: pollingOperation,
                reasonCode: "MITSUBISHI_POLLING_VALUE_ONLY_WHEN_WAITING"),
            Constraint(
                "PollingTimeout",
                enabledWhen: pollingOperation,
                reasonCode: "MITSUBISHI_POLLING_TIMEOUT_ONLY_WHEN_WAITING"),
            Constraint(
                "PollingInterval",
                enabledWhen: pollingOperation,
                reasonCode: "MITSUBISHI_POLLING_INTERVAL_ONLY_WHEN_WAITING")
        ];
    }

    private static IReadOnlyList<OperatorParameterConstraint> TcpCommunicationConstraints()
    {
        var profileRequired = Any(
            Equals("UseGlobalProfile", true),
            Equals("Mode", "Server"));
        var legacyClient = All(
            Empty("ProfileId"),
            Equals("UseGlobalProfile", false),
            Equals("Mode", "Client"));
        var waitResponse = All(Equals("WaitResponse", true));
        var parseEnabled = All(
            Equals("WaitResponse", true),
            NotEquals("ResponseParseMode", "None"));
        var regexParse = All(
            Equals("WaitResponse", true),
            Equals("ResponseParseMode", "Regex"));
        var keyValueParse = All(
            Equals("WaitResponse", true),
            Equals("ResponseParseMode", "KeyValue"));
        var delimitedParse = All(
            Equals("WaitResponse", true),
            Equals("ResponseParseMode", "Delimited"));
        var fixedWidthParse = All(
            Equals("WaitResponse", true),
            Equals("ResponseParseMode", "FixedWidth"));
        var delimitedOrFixedWidth = When(
            all: [Equals("WaitResponse", true)],
            any:
            [
                Equals("ResponseParseMode", "Delimited"),
                Equals("ResponseParseMode", "FixedWidth")
            ]);
        return
        [
            Constraint(
                "ProfileId",
                OperatorParameterRequiredPolicies.Optional,
                requiredWhen: profileRequired,
                resourceKind: "tcp_profile",
                reasonCode: "TCP_PROFILE_REQUIRED_FOR_GLOBAL_OR_SERVER_MODE"),
            Constraint(
                "Mode",
                disabledWhen: All(NotEmpty("ProfileId")),
                reasonCode: "TCP_MODE_IGNORED_WHEN_PROFILE_CONFIGURED"),
            Constraint(
                "IpAddress",
                requiredWhen: legacyClient,
                enabledWhen: legacyClient,
                resourceKind: "network_endpoint",
                reasonCode: "TCP_LEGACY_CLIENT_HOST_REQUIRED"),
            Constraint(
                "Port",
                requiredWhen: legacyClient,
                enabledWhen: legacyClient,
                reasonCode: "TCP_LEGACY_CLIENT_PORT_REQUIRED"),
            Constraint(
                "Timeout",
                enabledWhen: legacyClient,
                reasonCode: "TCP_LEGACY_CLIENT_TIMEOUT_ONLY_WITHOUT_PROFILE"),
            Constraint(
                "Encoding",
                enabledWhen: legacyClient,
                reasonCode: "TCP_LEGACY_CLIENT_ENCODING_ONLY_WITHOUT_PROFILE"),
            Constraint(
                "UseFixedSendData",
                disabledWhen: All(NotEmpty("PayloadTemplate")),
                reasonCode: "TCP_PAYLOAD_TEMPLATE_OWNS_PAYLOAD_SELECTION"),
            Constraint(
                "ResponseTimeoutMs",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_TIMEOUT_ONLY_WHEN_WAITING"),
            Constraint(
                "FailOnParseError",
                enabledWhen: waitResponse,
                reasonCode: "TCP_PARSE_FAILURE_POLICY_ONLY_WHEN_WAITING"),
            Constraint(
                "FailOnUnexpectedResponse",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_FAILURE_POLICY_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseParseMode",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_PARSE_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseFieldName",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: parseEnabled,
                reasonCode: "TCP_RESPONSE_FIELD_ONLY_WHEN_PARSING"),
            Constraint(
                "ResponseFieldNames",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: delimitedOrFixedWidth,
                reasonCode: "TCP_RESPONSE_FIELD_NAMES_ONLY_FOR_POSITIONAL_PARSE"),
            Constraint(
                "RequiredResponseFields",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: waitResponse,
                reasonCode: "TCP_REQUIRED_RESPONSE_FIELDS_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseFieldWidths",
                requiredWhen: fixedWidthParse,
                enabledWhen: fixedWidthParse,
                reasonCode: "TCP_FIXED_WIDTHS_REQUIRED_FOR_FIXED_WIDTH_PARSE"),
            Constraint(
                "ResponseRegexPattern",
                requiredWhen: regexParse,
                enabledWhen: regexParse,
                reasonCode: "TCP_REGEX_PATTERN_REQUIRED_FOR_REGEX_PARSE"),
            Constraint(
                "ResponseRegexIgnoreCase",
                enabledWhen: regexParse,
                reasonCode: "TCP_REGEX_OPTIONS_ONLY_FOR_REGEX_PARSE"),
            Constraint(
                "ResponseKeyValuePairDelimiter",
                requiredWhen: keyValueParse,
                enabledWhen: keyValueParse,
                atLeastOneGroup: "tcp-key-value-pair-delimiters",
                reasonCode: "TCP_KEY_VALUE_PAIR_DELIMITER_ONLY_FOR_KEY_VALUE_PARSE"),
            Constraint(
                "ResponseKeyValuePairDelimiters",
                requiredWhen: keyValueParse,
                enabledWhen: keyValueParse,
                atLeastOneGroup: "tcp-key-value-pair-delimiters",
                reasonCode: "TCP_KEY_VALUE_PAIR_DELIMITERS_ONLY_FOR_KEY_VALUE_PARSE"),
            Constraint(
                "ResponseKeyValueSeparator",
                requiredWhen: keyValueParse,
                enabledWhen: keyValueParse,
                atLeastOneGroup: "tcp-key-value-separators",
                reasonCode: "TCP_KEY_VALUE_SEPARATOR_ONLY_FOR_KEY_VALUE_PARSE"),
            Constraint(
                "ResponseKeyValueSeparators",
                requiredWhen: keyValueParse,
                enabledWhen: keyValueParse,
                atLeastOneGroup: "tcp-key-value-separators",
                reasonCode: "TCP_KEY_VALUE_SEPARATORS_ONLY_FOR_KEY_VALUE_PARSE"),
            Constraint(
                "ResponseDelimiter",
                requiredWhen: delimitedParse,
                enabledWhen: delimitedParse,
                atLeastOneGroup: "tcp-response-delimiters",
                reasonCode: "TCP_DELIMITER_ONLY_FOR_DELIMITED_PARSE"),
            Constraint(
                "ResponseDelimiters",
                requiredWhen: delimitedParse,
                enabledWhen: delimitedParse,
                atLeastOneGroup: "tcp-response-delimiters",
                reasonCode: "TCP_DELIMITERS_ONLY_FOR_DELIMITED_PARSE"),
            Constraint(
                "ResponseIndex",
                enabledWhen: delimitedOrFixedWidth,
                reasonCode: "TCP_RESPONSE_INDEX_ONLY_FOR_POSITIONAL_PARSE"),
            Constraint(
                "TrimResponseBeforeParse",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_TRIM_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseStartMarker",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_FRAME_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseEndMarker",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_FRAME_ONLY_WHEN_WAITING"),
            Constraint(
                "FailOnMissingResponseFrame",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_FRAME_POLICY_ONLY_WHEN_WAITING"),
            Constraint(
                "ExpectedResponse",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: waitResponse,
                reasonCode: "TCP_EXPECTED_RESPONSE_ONLY_WHEN_WAITING"),
            Constraint(
                "RejectedResponse",
                OperatorParameterRequiredPolicies.Optional,
                enabledWhen: waitResponse,
                reasonCode: "TCP_REJECTED_RESPONSE_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseMatchMode",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_MATCH_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseMatchIgnoreCase",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_MATCH_ONLY_WHEN_WAITING"),
            Constraint(
                "ResponseMatchSource",
                enabledWhen: waitResponse,
                reasonCode: "TCP_RESPONSE_MATCH_ONLY_WHEN_WAITING")
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

    private static OperatorParameterConditionSet When(
        IReadOnlyList<OperatorParameterCondition>? all = null,
        IReadOnlyList<OperatorParameterCondition>? any = null) =>
        new(All: all, Any: any);
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

public sealed record OperatorParameterAliasDiagnostic(
    string Code,
    string CanonicalParameter,
    string AliasParameter,
    string Message);

public sealed record OperatorParameterCanonicalizationResult(
    IReadOnlyDictionary<string, object?> EffectiveValues,
    IReadOnlyDictionary<string, object?> ExplicitValues,
    IReadOnlyList<OperatorParameterAliasDiagnostic> Diagnostics);

public static class OperatorParameterValueSemantics
{
    private const string PendingPrefix = "<pending-";

    public static bool IsPendingSentinel(object? value)
    {
        var text = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Equals("<pending>", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!text.StartsWith(PendingPrefix, StringComparison.OrdinalIgnoreCase) ||
            !text.EndsWith('>'))
        {
            return false;
        }

        var payloadLength = text.Length - PendingPrefix.Length - 1;
        if (payloadLength <= 0)
        {
            return false;
        }

        for (var index = PendingPrefix.Length; index < text.Length - 1; index++)
        {
            if (char.IsWhiteSpace(text[index]) || text[index] is '<' or '>')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsMissing(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) || IsPendingSentinel(text);
    }
}

public static class OperatorParameterConstraintEvaluator
{
    public static IReadOnlyList<OperatorParameterConstraintState> ResolveStates(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var normalizedValues = Canonicalize(metadata, values, explicitParameterNames).EffectiveValues;
        return ResolveStatesCore(metadata, normalizedValues);
    }

    private static IReadOnlyList<OperatorParameterConstraintState> ResolveStatesCore(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> normalizedValues)
    {
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
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null,
        bool requireExplicitResourceConfiguration = false)
    {
        var canonicalization = Canonicalize(metadata, values, explicitParameterNames);
        var normalizedValues = canonicalization.EffectiveValues;
        var states = ResolveStatesCore(metadata, normalizedValues);
        var violations = new List<OperatorParameterConstraintViolation>();

        bool IsConfigured(OperatorParameterConstraintState state)
        {
            var effectiveValue = GetValue(normalizedValues, state.Constraint.Parameter);
            if (IsMissing(effectiveValue))
            {
                return false;
            }

            if (IsInactiveResourceSwitch(effectiveValue))
            {
                return true;
            }

            if (!requireExplicitResourceConfiguration ||
                string.IsNullOrWhiteSpace(state.Constraint.ResourceKind))
            {
                return true;
            }

            return canonicalization.ExplicitValues.TryGetValue(state.Constraint.Parameter, out var explicitValue) &&
                   !IsMissing(explicitValue);
        }

        foreach (var group in states
                     .Where(item => !string.IsNullOrWhiteSpace(item.Constraint.AtLeastOneGroup))
                     .GroupBy(item => item.Constraint.AtLeastOneGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var active = group.Where(item => item.EffectiveRequired && !item.EffectiveDisabled).ToArray();
            if (active.Length == 0)
            {
                continue;
            }

            var names = active
                .Select(item => item.Constraint.Parameter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (active.Any(IsConfigured))
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
            var active = group.Where(item => !item.EffectiveDisabled).ToArray();
            if (active.Length == 0)
            {
                continue;
            }

            var configured = active
                .Select(item => item.Constraint.Parameter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !IsMissing(GetValue(normalizedValues, name)))
                .ToArray();
            if (configured.Length < 2)
            {
                continue;
            }

            var primary = active[0].Constraint;
            violations.Add(new OperatorParameterConstraintViolation(
                "mutually-exclusive",
                configured,
                primary.ResourceKind,
                primary.ReasonCode));
        }

        foreach (var state in states.Where(item =>
                     item.EffectiveRequired &&
                     string.IsNullOrWhiteSpace(item.Constraint.AtLeastOneGroup) &&
                     !IsConfigured(item)))
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
        return OperatorParameterValueSemantics.IsMissing(value);
    }

    public static OperatorParameterCanonicalizationResult Canonicalize(
        OperatorMetadata metadata,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlySet<string>? explicitParameterNames = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);

        var metadataByExactName = metadata.Parameters
            .GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var metadataByName = metadata.Parameters
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var constraintsByExactName = metadata.ParameterConstraints
            .GroupBy(constraint => constraint.Parameter, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var constraintsByName = metadata.ParameterConstraints
            .GroupBy(constraint => constraint.Parameter, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var aliasConstraints = metadata.ParameterConstraints
            .Where(item => !string.IsNullOrWhiteSpace(item.AliasFor))
            .ToArray();
        var aliasNames = aliasConstraints
            .Select(item => item.Parameter)
            .ToHashSet(StringComparer.Ordinal);

        string NormalizeName(string name)
        {
            if (metadataByExactName.TryGetValue(name, out var exactParameter))
            {
                return exactParameter.Name;
            }

            if (constraintsByExactName.TryGetValue(name, out var exactConstraint))
            {
                return exactConstraint.Parameter;
            }

            if (metadataByName.TryGetValue(name, out var parameter))
            {
                return parameter.Name;
            }

            if (constraintsByName.TryGetValue(name, out var constraint))
            {
                return constraint.Parameter;
            }

            return name;
        }

        var explicitNames = explicitParameterNames is null
            ? null
            : explicitParameterNames.ToHashSet(StringComparer.Ordinal);
        var rawExplicit = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (explicitNames is not null &&
                !explicitNames.Contains(pair.Key) &&
                !explicitNames.Contains(NormalizeName(pair.Key)))
            {
                continue;
            }

            rawExplicit[NormalizeName(pair.Key)] = pair.Value;
        }

        var explicitValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rawExplicit.Where(pair => !aliasNames.Contains(pair.Key)))
        {
            explicitValues[pair.Key] = pair.Value;
        }
        var diagnostics = new List<OperatorParameterAliasDiagnostic>();

        foreach (var aliasGroup in aliasConstraints
                     .GroupBy(item => NormalizeName(item.AliasFor!), StringComparer.OrdinalIgnoreCase))
        {
            var canonicalName = aliasGroup.Key;
            var hasCanonical = rawExplicit.TryGetValue(canonicalName, out var canonicalValue);
            var configuredAliases = aliasGroup
                .Where(item => rawExplicit.ContainsKey(item.Parameter))
                .Select(item => (Constraint: item, Value: rawExplicit[item.Parameter]))
                .ToArray();

            if (hasCanonical)
            {
                explicitValues[canonicalName] = canonicalValue;
                foreach (var alias in configuredAliases.Where(alias => !ValuesEqual(canonicalValue, alias.Value)))
                {
                    diagnostics.Add(new OperatorParameterAliasDiagnostic(
                        "canonical-overrides-alias",
                        canonicalName,
                        alias.Constraint.Parameter,
                        $"{canonicalName} overrides conflicting alias {alias.Constraint.Parameter}."));
                }

                continue;
            }

            if (configuredAliases.Length == 0)
            {
                continue;
            }

            var selected = configuredAliases[0];
            explicitValues[canonicalName] = selected.Value;
            foreach (var alias in configuredAliases.Skip(1).Where(alias => !ValuesEqual(selected.Value, alias.Value)))
            {
                diagnostics.Add(new OperatorParameterAliasDiagnostic(
                    "alias-conflict",
                    canonicalName,
                    alias.Constraint.Parameter,
                    $"Alias {selected.Constraint.Parameter} overrides conflicting alias {alias.Constraint.Parameter} for {canonicalName}."));
            }
        }

        var effectiveValues = metadata.Parameters
            .Where(parameter => parameter.DefaultValue is not null)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.DefaultValue,
                StringComparer.OrdinalIgnoreCase);
        foreach (var pair in explicitValues)
        {
            effectiveValues[pair.Key] = pair.Value;
        }

        foreach (var alias in aliasConstraints)
        {
            var canonicalName = NormalizeName(alias.AliasFor!);
            if (effectiveValues.TryGetValue(canonicalName, out var canonicalValue))
            {
                effectiveValues[alias.Parameter] = canonicalValue;
            }
        }

        return new OperatorParameterCanonicalizationResult(
            new ReadOnlyDictionary<string, object?>(effectiveValues),
            new ReadOnlyDictionary<string, object?>(explicitValues),
            diagnostics);
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

    private static bool IsInactiveResourceSwitch(object? value)
    {
        return value is bool boolean
            ? !boolean
            : bool.TryParse(value?.ToString(), out var parsed) && !parsed;
    }

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
