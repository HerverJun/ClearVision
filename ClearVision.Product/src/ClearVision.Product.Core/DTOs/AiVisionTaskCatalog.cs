namespace ClearVision.Product.Core.DTOs;

public sealed record AiVisionTaskDescriptor(
    string CanonicalValue,
    string RouteContractKey,
    string PlanIntent,
    bool IsPrimaryTask,
    bool UiSelectable,
    IReadOnlyList<string> Aliases)
{
    public IReadOnlyList<string> AllValues { get; } =
        new[] { CanonicalValue }
            .Concat(Aliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

/// <summary>
/// Authoritative task vocabulary shared by requirement recognition, Plan, Build and admission.
/// Compatibility aliases are normalized in memory; only canonical values may resolve task_type.
/// </summary>
public static class AiVisionTaskCatalog
{
    private static readonly IReadOnlyList<AiVisionTaskDescriptor> Tasks =
    [
        Primary(
            AiVisionTaskTypes.PresenceAbsence,
            "presence_detection",
            "presence_absence",
            "presence",
            "presence_detection"),
        Primary(
            AiVisionTaskTypes.AttributeClassification,
            "attribute_classification",
            "attribute_classification",
            AiVisionTaskTypes.Classification,
            "attribute",
            "image_classification"),
        Primary(
            AiVisionTaskTypes.ObjectDetection,
            "object_detection",
            "object_detection",
            "target_detection"),
        Primary(
            AiVisionTaskTypes.TemplateLocation,
            "template_matching",
            "template_location",
            "template_matching",
            "template_match",
            "template_positioning"),
        Primary(
            AiVisionTaskTypes.SurfaceDefect,
            "surface_defect_detection",
            "surface_defect",
            AiVisionTaskTypes.SurfaceOrPoseDefect,
            "surface_defect_detection"),
        Primary(
            AiVisionTaskTypes.GeometryMeasurement,
            "measurement",
            "measurement",
            "measurement",
            "measure"),
        Primary(
            AiVisionTaskTypes.WireSequence,
            "sequence_judgment",
            "wire_sequence",
            "sequence",
            "sequence_judgment"),
        Primary(
            AiVisionTaskTypes.CodeRecognition,
            "code_recognition",
            "code_recognition",
            AiVisionTaskTypes.BarcodeQr,
            "ocr")
    ];

    private static readonly IReadOnlyDictionary<string, AiVisionTaskDescriptor> ByValue =
        Tasks.SelectMany(task => task.AllValues.Select(value => (Value: value, Task: task)))
            .ToDictionary(item => item.Value, item => item.Task, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AiVisionTaskDescriptor> PrimaryTasks => Tasks;

    public static bool TryNormalizePrimary(string? value, out string canonicalValue)
    {
        if (TryGet(value, out var task) && task.IsPrimaryTask)
        {
            canonicalValue = task.CanonicalValue;
            return true;
        }

        canonicalValue = string.Empty;
        return false;
    }

    public static string NormalizePrimaryOrUnknown(string? value) =>
        TryNormalizePrimary(value, out var canonicalValue)
            ? canonicalValue
            : AiVisionTaskTypes.Unknown;

    public static bool TryGet(string? value, out AiVisionTaskDescriptor descriptor)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return ByValue.TryGetValue(normalized, out descriptor!);
    }

    public static string GetRouteContractKey(string? value) =>
        TryGet(value, out var descriptor) && descriptor.IsPrimaryTask
            ? descriptor.RouteContractKey
            : string.Empty;

    public static string GetPlanIntent(string? value) =>
        TryGet(value, out var descriptor) && descriptor.IsPrimaryTask
            ? descriptor.PlanIntent
            : string.Empty;

    private static AiVisionTaskDescriptor Primary(
        string canonicalValue,
        string routeContractKey,
        string planIntent,
        params string[] aliases) =>
        new(
            canonicalValue,
            routeContractKey,
            planIntent,
            IsPrimaryTask: true,
            UiSelectable: true,
            aliases);
}
