using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentReadOnlyCatalog
{
    private static readonly VisionAgentOperatorContractCatalog ContractCatalog = new();

    public static IReadOnlyList<OperatorCatalogItem> Operators { get; } = ContractCatalog.Operators
        .OrderBy(item => item.CategoryOrder)
        .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
        .ThenBy(item => item.OperatorType, StringComparer.Ordinal)
        .Select(item => new OperatorCatalogItem(
            item.OperatorType,
            item.DisplayName,
            item.CategoryId,
            item.CategoryOrder,
            item.Category,
            item.Description,
            item.Lifecycle,
            item.LifecycleNote,
            item.DefaultHidden,
            item.DefaultAiRecommendation,
            item.RequiresLifecycleDisclosure,
            item.Keywords,
            item.Tags))
        .ToList();

    public static IReadOnlyDictionary<string, OperatorSchemaItem> Schemas { get; } = ContractCatalog.Operators
        .OrderBy(item => item.OperatorType, StringComparer.Ordinal)
        .ToDictionary(
            item => item.OperatorType,
            item => new OperatorSchemaItem(
                item.OperatorType,
                item.CategoryId,
                item.CategoryOrder,
                item.Category,
                item.Lifecycle,
                item.LifecycleNote,
                item.DefaultHidden,
                item.DefaultAiRecommendation,
                item.RequiresLifecycleDisclosure,
                item.OutputPorts.Select(port => port.Name).ToList(),
                item.InputPorts.Select(port => port.Name).ToList(),
                item.Parameters.Select(parameter => new OperatorParameterItem(
                    parameter.Name,
                    parameter.DisplayName,
                    parameter.DataType,
                    parameter.IsRequired,
                    parameter.DefaultValue,
                    parameter.MinValue,
                    parameter.MaxValue,
                    parameter.Options?
                        .Select(option => option.Value?.ToString() ?? string.Empty)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList() ?? [],
                    parameter.Description)).ToList(),
                item.ParameterConstraints,
                item.OutputAvailabilityRules,
                item.Metadata),
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TemplateItem> Templates { get; } =
    [
        new(
            "wire_sequence_inspection",
            "wire_sequence",
            "Wire sequence inspection",
            ["wire", "sequence", "terminal", "harness"],
            ["ImageAcquisition", "RoiManager", "DeepLearning", "DetectionSequenceJudge", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_roi", "Image"),
                Link("op_roi", "Image", "op_detect", "Image"),
                Link("op_detect", "DetectionList", "op_sequence", "Detections"),
                Link("op_sequence", "IsMatch", "op_judge", "Value"),
                Link("op_judge", "JudgmentResult", "op_out", "Result")
            ]),
        new(
            "template_matching_alignment",
            "template_matching",
            "Template matching alignment",
            ["template", "matching", "alignment", "position"],
            ["ImageAcquisition", "TemplateMatching", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_match", "Image"),
                Link("op_match", "Score", "op_judge", "Value"),
                Link("op_judge", "JudgmentResult", "op_out", "Result")
            ]),
        new(
            "hole_distance_measurement",
            "measurement",
            "Hole distance measurement",
            ["hole", "distance", "spacing", "measurement"],
            ["ImageAcquisition", "CircleMeasurement", "CircleMeasurement", "Measurement", "UnitConvert", "ResultJudgment", "ResultOutput"],
            [
                Link("op_cam", "Image", "op_circle_a", "Image"),
                Link("op_cam", "Image", "op_circle_b", "Image"),
                Link("op_circle_a", "Center", "op_distance", "PointA"),
                Link("op_circle_b", "Center", "op_distance", "PointB"),
                Link("op_distance", "Distance", "op_calibration", "Value"),
                Link("op_calibration", "Result", "op_judge", "Value"),
                Link("op_judge", "JudgmentResult", "op_out", "Result")
            ])
    ];

    private static TemplateConnection Link(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new TemplateConnection(sourceTempId, sourcePortName, targetTempId, targetPortName);
    }
}

internal sealed record OperatorCatalogItem(
    string OperatorType,
    string DisplayName,
    OperatorCategoryId CategoryId,
    int CategoryOrder,
    string Category,
    string Summary,
    OperatorLifecycle Lifecycle,
    string LifecycleNote,
    bool DefaultHidden,
    bool DefaultAiRecommendation,
    bool RequiresLifecycleDisclosure,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Tags);

internal sealed record OperatorSchemaItem(
    string OperatorType,
    OperatorCategoryId CategoryId,
    int CategoryOrder,
    string Category,
    OperatorLifecycle Lifecycle,
    string LifecycleNote,
    bool DefaultHidden,
    bool DefaultAiRecommendation,
    bool RequiresLifecycleDisclosure,
    IReadOnlyList<string> OutputPorts,
    IReadOnlyList<string> InputPorts,
    IReadOnlyList<OperatorParameterItem> Parameters,
    IReadOnlyList<OperatorParameterConstraint> ParameterConstraints,
    IReadOnlyList<OperatorOutputAvailabilityRule> OutputAvailabilityRules,
    OperatorMetadata Metadata);

internal sealed record OperatorParameterItem(
    string Name,
    string DisplayName,
    string DataType,
    bool Required,
    object? DefaultValue,
    object? MinValue,
    object? MaxValue,
    IReadOnlyList<string> Options,
    string Summary);

internal sealed record TemplateItem(
    string TemplateId,
    string ScenarioKey,
    string Name,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> OperatorTypes,
    IReadOnlyList<TemplateConnection> Connections);

internal sealed record TemplateConnection(
    string SourceTempId,
    string SourcePortName,
    string TargetTempId,
    string TargetPortName);
