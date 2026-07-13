namespace ClearVision.Product.Infrastructure.Operators;

public enum DeepLearningTaskType
{
    ObjectDetection = 0,
    ImageClassification = 1,
    SemanticSegmentation = 2,
    Auto = 3
}

internal sealed record OnnxOutputSignature(string Name, int[] Dimensions);

internal sealed record DeepLearningTaskResolution(
    DeepLearningTaskType TaskType,
    string Source,
    string Evidence);

internal static class DeepLearningTaskResolver
{
    public static bool TryParse(string? raw, out DeepLearningTaskType taskType)
    {
        taskType = DeepLearningTaskType.ObjectDetection;
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Equals("ObjectDetection", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Detection", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("ImageClassification", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Classification", StringComparison.OrdinalIgnoreCase))
        {
            taskType = DeepLearningTaskType.ImageClassification;
            return true;
        }

        if (raw.Equals("SemanticSegmentation", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("Segmentation", StringComparison.OrdinalIgnoreCase))
        {
            taskType = DeepLearningTaskType.SemanticSegmentation;
            return true;
        }

        if (raw.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            taskType = DeepLearningTaskType.Auto;
            return true;
        }

        return false;
    }

    public static DeepLearningTaskResolution Resolve(
        DeepLearningTaskType requestedTask,
        string? catalogType,
        IReadOnlyCollection<OnnxOutputSignature> outputSignatures)
    {
        if (requestedTask != DeepLearningTaskType.Auto)
        {
            return new DeepLearningTaskResolution(requestedTask, "Explicit", requestedTask.ToString());
        }

        if (TryResolveCatalogType(catalogType, out var catalogTask))
        {
            return new DeepLearningTaskResolution(catalogTask, "ModelCatalog", catalogType!.Trim());
        }

        var inferred = outputSignatures
            .Select(signature => (Signature: signature, Task: InferFromShape(signature.Dimensions)))
            .Where(item => item.Task.HasValue)
            .ToArray();
        var distinctTasks = inferred
            .Select(item => item.Task!.Value)
            .Distinct()
            .ToArray();

        if (distinctTasks.Length == 1)
        {
            var evidence = string.Join(
                "; ",
                inferred
                    .Where(item => item.Task == distinctTasks[0])
                    .Select(item => $"{item.Signature.Name}=[{string.Join(',', item.Signature.Dimensions)}]"));
            return new DeepLearningTaskResolution(distinctTasks[0], "OutputShape", evidence);
        }

        var signatures = outputSignatures.Count == 0
            ? "none"
            : string.Join(
                "; ",
                outputSignatures.Select(item => $"{item.Name}=[{string.Join(',', item.Dimensions)}]"));
        var reason = distinctTasks.Length > 1
            ? "outputs indicate multiple task families"
            : "no output shape uniquely identifies a supported task";
        throw new InvalidOperationException(
            $"TaskType=Auto could not reliably resolve the ONNX task because {reason}. " +
            $"Outputs: {signatures}. Set TaskType explicitly or provide a model catalog type.");
    }

    public static bool TryResolveCatalogType(string? catalogType, out DeepLearningTaskType taskType)
    {
        taskType = DeepLearningTaskType.ObjectDetection;
        if (string.IsNullOrWhiteSpace(catalogType))
        {
            return false;
        }

        switch (catalogType.Trim().ToLowerInvariant())
        {
            case "detection":
            case "object_detection":
            case "yolo":
                taskType = DeepLearningTaskType.ObjectDetection;
                return true;
            case "classification":
            case "image_classification":
            case "classifier":
                taskType = DeepLearningTaskType.ImageClassification;
                return true;
            case "segmentation":
            case "semantic_segmentation":
                taskType = DeepLearningTaskType.SemanticSegmentation;
                return true;
            default:
                return false;
        }
    }

    private static DeepLearningTaskType? InferFromShape(IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count == 1 && dimensions[0] > 1)
        {
            return DeepLearningTaskType.ImageClassification;
        }

        if (dimensions.Count == 2)
        {
            if (dimensions[0] == 1 && dimensions[1] > 1 && dimensions[1] is not (6 or 7))
            {
                return DeepLearningTaskType.ImageClassification;
            }

            if (dimensions[0] > 1 && dimensions[1] is 6 or 7)
            {
                return DeepLearningTaskType.ObjectDetection;
            }
        }

        if (dimensions.Count == 3 && IsBatchDimension(dimensions[0]))
        {
            var first = dimensions[1];
            var second = dimensions[2];
            var featureDimension = Math.Min(first, second);
            var candidateDimension = Math.Max(first, second);
            if (featureDimension >= 6 && featureDimension <= 512 && candidateDimension >= 1)
            {
                return DeepLearningTaskType.ObjectDetection;
            }
        }

        if (dimensions.Count == 4 && IsBatchDimension(dimensions[0]))
        {
            var nchwClassification = dimensions[1] > 1 && dimensions[2] == 1 && dimensions[3] == 1;
            var nhwcClassification = dimensions[3] > 1 && dimensions[1] == 1 && dimensions[2] == 1;
            if (nchwClassification || nhwcClassification)
            {
                return DeepLearningTaskType.ImageClassification;
            }

            var nchwSegmentation = dimensions[1] > 1 && dimensions[2] > 1 && dimensions[3] > 1;
            var nhwcSegmentation = dimensions[3] > 1 && dimensions[1] > 1 && dimensions[2] > 1;
            if (nchwSegmentation || nhwcSegmentation)
            {
                return DeepLearningTaskType.SemanticSegmentation;
            }
        }

        return null;
    }

    private static bool IsBatchDimension(int value) => value is 1 or -1 or 0;
}
