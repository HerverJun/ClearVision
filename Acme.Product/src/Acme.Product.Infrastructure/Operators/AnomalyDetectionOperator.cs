using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Infrastructure.AI.Anomaly;
using Acme.Product.Infrastructure.AI.Runtime;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "异常检测",
    Description = "Runs a simplified PatchCore-style anomaly detector with train/inference modes and feature-bank persistence.",
    Category = "AI检测",
    IconName = "anomaly-detection",
    Keywords = new[] { "anomaly", "patchcore", "feature bank", "异常检测" },
    Version = "1.0.0",
    Tags = new[] { "experimental", "industrial-remediation", "anomaly-detection" }
)]
[AlgorithmInfo(
    Name = "Simplified PatchCore",
    CoreApi = "OpenCvSharp + memory-bank nearest-neighbor",
    TimeComplexity = "O(P * B)",
    SpaceComplexity = "O(B)",
    Dependencies = new[] { "OpenCvSharp" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = false)]
[InputPort("NormalImages", "Normal Images", PortDataType.Any, IsRequired = false)]
[OutputPort("AnomalyScore", "Anomaly Score", PortDataType.Float)]
[OutputPort("IsAnomaly", "Is Anomaly", PortDataType.Boolean)]
[OutputPort("AnomalyMap", "Anomaly Map", PortDataType.Image)]
[OutputPort("AnomalyMask", "Anomaly Mask", PortDataType.Image)]
[OutputPort("FeatureBankPath", "Feature Bank Path", PortDataType.String)]
[OutputPort("PatchCount", "Patch Count", PortDataType.Integer)]
[OutputPort("ThresholdUsed", "Threshold Used", PortDataType.Float)]
[OutputPort("Diagnostics", "Diagnostics", PortDataType.Any)]
[OperatorParam("Mode", "Mode", "enum", DefaultValue = "inference", Options = new[] { "inference|Inference", "train|Train" })]
[OperatorParam("FeatureBankPath", "Feature Bank Path", "file", DefaultValue = "", IsRequired = false)]
[OperatorParam("SaveFeatureBankPath", "Save Feature Bank Path", "file", DefaultValue = "", IsRequired = false)]
[OperatorParam("ModelId", "Model Id", "string", DefaultValue = "", IsRequired = false)]
[OperatorParam("ModelCatalogPath", "Model Catalog Path", "file", DefaultValue = "", IsRequired = false)]
[OperatorParam("Backbone", "Backbone", "string", DefaultValue = "simple_patchcore", IsRequired = false)]
[OperatorParam("FeatureExtractorId", "Feature Extractor Id", "string", DefaultValue = "lab_gradient_stats", IsRequired = false)]
[OperatorParam("EmbeddingModelId", "Embedding Model Id", "string", DefaultValue = "", IsRequired = false)]
[OperatorParam("EmbeddingModelPath", "Embedding Model Path", "file", DefaultValue = "", IsRequired = false)]
[OperatorParam("PatchSize", "Patch Size", "int", DefaultValue = 32, Min = 4, Max = 256)]
[OperatorParam("PatchStride", "Patch Stride", "int", DefaultValue = 16, Min = 1, Max = 256)]
[OperatorParam("CoresetRatio", "Coreset Ratio", "double", DefaultValue = 0.2, Min = 0.01, Max = 1.0)]
[OperatorParam("Threshold", "Threshold", "double", DefaultValue = 0.35, Min = 0.0, Max = 1.0)]
[OperatorParam("EnableCandidateProfile", "Enable Candidate Profile", "bool", DefaultValue = false)]
[OperatorParam("CandidateProfile", "Candidate Profile", "enum", DefaultValue = "default", Options = new[] { "default|Default", "mvtec_lite_v2|MVTec Lite v2" })]
[OperatorParam("CandidateFallbackMode", "Candidate Fallback Mode", "enum", DefaultValue = "UseDefault", Options = new[] { "UseDefault|Use Default", "Fail|Fail" })]
public sealed class AnomalyDetectionOperator : OperatorBase
{
    private static readonly string[] SupportedCatalogTypes = ["anomaly_detection", "anomaly_feature_bank", "feature_bank"];
    private static readonly string[] SupportedEmbeddingCatalogTypes = ["anomaly_embedding", "embedding"];
    private const string CandidateProfileDefault = "default";
    private const string CandidateProfileMvtecLiteV2 = "mvtec_lite_v2";
    private const string CandidateFallbackUseDefault = "UseDefault";
    private const string CandidateFallbackFail = "Fail";
    private const int MvtecLiteV2PatchSize = 16;
    private const int MvtecLiteV2PatchStride = 8;
    private const double MvtecLiteV2CoresetRatio = 0.02;
    private const double MvtecLiteV2Threshold = 0.10;
    private const string MvtecLiteV2Backbone = "simple_patchcore";
    private const string MvtecLiteV2FeatureExtractorId = "lab_gradient_stats";

    public AnomalyDetectionOperator(ILogger<AnomalyDetectionOperator> logger)
        : base(logger)
    {
    }

    public override OperatorType OperatorType => OperatorType.AnomalyDetection;

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(GetStringParam(@operator, "Mode", "inference"));
        var candidateProfile = ResolveCandidateProfile(@operator);
        if (!IsSupportedCandidateProfile(candidateProfile.Profile))
        {
            return OperatorExecutionOutput.Failure("CandidateProfile must be 'default' or 'mvtec_lite_v2'.");
        }

        if (!IsSupportedCandidateFallbackMode(candidateProfile.FallbackMode))
        {
            return OperatorExecutionOutput.Failure("CandidateFallbackMode must be 'UseDefault' or 'Fail'.");
        }

        var baseThreshold = GetDoubleParam(@operator, "Threshold", 0.35, 0.0, 1.0);
        var baseOptions = new SimplePatchCoreOptions
        {
            PatchSize = GetIntParam(@operator, "PatchSize", 32, 4, 256),
            PatchStride = GetIntParam(@operator, "PatchStride", 16, 1, 256),
            CoresetRatio = GetDoubleParam(@operator, "CoresetRatio", 0.2, 0.01, 1.0),
            Backbone = GetStringParam(@operator, "Backbone", "simple_patchcore"),
            FeatureExtractorId = GetStringParam(@operator, "FeatureExtractorId", "lab_gradient_stats"),
            EmbeddingModelId = GetStringParam(@operator, "EmbeddingModelId", string.Empty),
            EmbeddingModelPath = GetStringParam(@operator, "EmbeddingModelPath", string.Empty)
        };
        var threshold = baseThreshold;
        var options = baseOptions;
        if (mode == "train")
        {
            if (candidateProfile.Enabled && candidateProfile.Profile == CandidateProfileMvtecLiteV2)
            {
                threshold = MvtecLiteV2Threshold;
                options = ApplyMvtecLiteV2Profile(baseOptions);
                candidateProfile = candidateProfile with { Applied = true };
            }

            if (!TryGetNormalImages(inputs, out var normalImages, out var normalImagesError))
            {
                return OperatorExecutionOutput.Failure(normalImagesError);
            }

            var embeddingTarget = ResolveEmbeddingModelTarget(@operator, null, options.FeatureExtractorId);
            if (RequiresOnnxEmbedding(options.FeatureExtractorId) && string.IsNullOrWhiteSpace(embeddingTarget.Path))
            {
                return OperatorExecutionOutput.Failure("Embedding model is required when FeatureExtractorId is 'onnx_embedding'.");
            }

            if (!string.IsNullOrWhiteSpace(embeddingTarget.Path))
            {
                options = CloneOptions(
                    options,
                    featureExtractorId: "onnx_embedding",
                    embeddingModelId: embeddingTarget.ModelId,
                    embeddingModelPath: embeddingTarget.Path);
            }

            SimplePatchCoreFeatureBank bank;
            try
            {
                bank = await RunCpuBoundWork(
                    () => SimplePatchCoreDetector.BuildFeatureBank(normalImages.Select(x => x.GetMat()), options),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Anomaly feature-bank training failed.");
                return OperatorExecutionOutput.Failure($"Anomaly training failed: {ex.Message}");
            }

            var saveTarget = ResolveFeatureBankSaveTarget(@operator);
            if (!string.IsNullOrWhiteSpace(saveTarget.Path))
            {
                try
                {
                    SimplePatchCoreDetector.Save(saveTarget.Path, bank);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to save anomaly feature bank.");
                    return OperatorExecutionOutput.Failure($"Failed to save feature bank: {ex.Message}");
                }
            }

            var previewImage = ResolvePreviewImage(inputs, normalImages);
            SimplePatchCoreAnalysisResult analysisResult;
            try
            {
                analysisResult = await RunCpuBoundWork(
                    () => SimplePatchCoreDetector.Analyze(previewImage.GetMat(), bank, threshold, options),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Anomaly preview inference failed after training.");
                return OperatorExecutionOutput.Failure($"Anomaly preview failed: {ex.Message}");
            }

            return OperatorExecutionOutput.Success(CreateOutputs(analysisResult, bank, saveTarget, embeddingTarget, "train", options, threshold, candidateProfile));
        }

        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return OperatorExecutionOutput.Failure("Input image is required for anomaly inference.");
        }

        FeatureBankResolution featureBankTarget;
        try
        {
            featureBankTarget = ResolveFeatureBankInputTarget(@operator);
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }

        SimplePatchCoreFeatureBank loadedBank;
        try
        {
            loadedBank = SimplePatchCoreDetector.Load(featureBankTarget.Path);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load anomaly feature bank.");
            return OperatorExecutionOutput.Failure($"Failed to load feature bank: {ex.Message}");
        }

        var inferenceOptions = CloneOptions(
            baseOptions,
            patchSize: loadedBank.PatchSize,
            patchStride: loadedBank.PatchStride,
            backbone: loadedBank.Backbone,
            featureExtractorId: loadedBank.FeatureExtractorId,
            embeddingModelId: loadedBank.EmbeddingModelId,
            embeddingModelPath: loadedBank.EmbeddingModelPath);
        if (candidateProfile.Enabled && candidateProfile.Profile == CandidateProfileMvtecLiteV2)
        {
            if (!IsMvtecLiteV2FeatureBankCompatible(loadedBank, out var fallbackReason))
            {
                if (candidateProfile.FallbackMode == CandidateFallbackFail)
                {
                    return OperatorExecutionOutput.Failure($"Candidate profile mvtec_lite_v2 is incompatible with the loaded feature bank: {fallbackReason}");
                }

                threshold = baseThreshold;
                candidateProfile = candidateProfile with { Applied = false, FallbackReason = fallbackReason };
            }
            else
            {
                threshold = MvtecLiteV2Threshold;
                inferenceOptions = ApplyMvtecLiteV2Profile(inferenceOptions);
                candidateProfile = candidateProfile with { Applied = true };
            }
        }

        var resolvedEmbeddingTarget = ResolveInferenceEmbeddingModelTarget(@operator, loadedBank);
        if (RequiresOnnxEmbedding(inferenceOptions.FeatureExtractorId) && string.IsNullOrWhiteSpace(resolvedEmbeddingTarget.Path))
        {
            return OperatorExecutionOutput.Failure("Embedding model is required for ONNX anomaly inference.");
        }

        try
        {
            var result = await RunCpuBoundWork(
                () => SimplePatchCoreDetector.Analyze(imageWrapper.GetMat(), loadedBank, threshold, inferenceOptions),
                cancellationToken);
            return OperatorExecutionOutput.Success(CreateOutputs(result, loadedBank, featureBankTarget, resolvedEmbeddingTarget, "inference", inferenceOptions, threshold, candidateProfile));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Anomaly inference failed.");
            return OperatorExecutionOutput.Failure($"Anomaly inference failed: {ex.Message}");
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var mode = NormalizeMode(GetStringParam(@operator, "Mode", "inference"));
        if (mode is not ("inference" or "train"))
        {
            return ValidationResult.Invalid("Mode must be 'inference' or 'train'.");
        }

        var candidateProfile = ResolveCandidateProfile(@operator);
        if (!IsSupportedCandidateProfile(candidateProfile.Profile))
        {
            return ValidationResult.Invalid("CandidateProfile must be 'default' or 'mvtec_lite_v2'.");
        }

        if (!IsSupportedCandidateFallbackMode(candidateProfile.FallbackMode))
        {
            return ValidationResult.Invalid("CandidateFallbackMode must be 'UseDefault' or 'Fail'.");
        }

        var backbone = GetStringParam(@operator, "Backbone", "simple_patchcore").Trim();
        if (!string.IsNullOrWhiteSpace(backbone) &&
            !string.Equals(backbone, "simple_patchcore", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backbone, "patchcore", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Backbone currently supports 'simple_patchcore' only.");
        }

        var featureExtractorId = GetStringParam(@operator, "FeatureExtractorId", "lab_gradient_stats").Trim();
        if (!string.IsNullOrWhiteSpace(featureExtractorId) &&
            !string.Equals(featureExtractorId, "lab_gradient_stats", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(featureExtractorId, "onnx_embedding", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("FeatureExtractorId currently supports 'lab_gradient_stats' or 'onnx_embedding' only.");
        }

        _ = GetIntParam(@operator, "PatchSize", 32, 4, 256);
        _ = GetIntParam(@operator, "PatchStride", 16, 1, 256);
        _ = GetDoubleParam(@operator, "CoresetRatio", 0.2, 0.01, 1.0);
        _ = GetDoubleParam(@operator, "Threshold", 0.35, 0.0, 1.0);

        if (mode == "inference")
        {
            try
            {
                _ = ResolveFeatureBankInputTarget(@operator);
            }
            catch (Exception ex)
            {
                return ValidationResult.Invalid(ex.Message);
            }
        }

        if (mode == "train" && RequiresOnnxEmbedding(featureExtractorId))
        {
            try
            {
                _ = ResolveEmbeddingModelTarget(@operator, null, featureExtractorId);
            }
            catch (Exception ex)
            {
                return ValidationResult.Invalid(ex.Message);
            }
        }

        if (mode == "inference" && RequiresOnnxEmbedding(featureExtractorId))
        {
            var hasExplicitEmbedding =
                !string.IsNullOrWhiteSpace(GetStringParam(@operator, "EmbeddingModelPath", string.Empty)) ||
                !string.IsNullOrWhiteSpace(GetStringParam(@operator, "EmbeddingModelId", string.Empty));
            if (hasExplicitEmbedding)
            {
                try
                {
                    _ = ResolveEmbeddingModelTarget(@operator, null, featureExtractorId);
                }
                catch (Exception ex)
                {
                    return ValidationResult.Invalid(ex.Message);
                }
            }
        }

        return ValidationResult.Valid();
    }

    private static Dictionary<string, object> CreateOutputs(
        SimplePatchCoreAnalysisResult result,
        SimplePatchCoreFeatureBank bank,
        FeatureBankResolution featureBank,
        EmbeddingModelResolution embeddingTarget,
        string mode,
        SimplePatchCoreOptions options,
        double requestedThreshold,
        CandidateProfileState candidateProfile)
    {
        var diagnostics = new Dictionary<string, object>
        {
            ["Mode"] = mode,
            ["ResolvedFeatureBankPath"] = featureBank.Path,
            ["FeatureBankSource"] = featureBank.Source,
            ["FeatureBankModelId"] = featureBank.ModelId,
            ["FeatureBankCatalogPath"] = featureBank.CatalogPath,
            ["Backbone"] = bank.Backbone,
            ["FeatureExtractorId"] = bank.FeatureExtractorId,
            ["FeatureSchemaVersion"] = bank.FeatureSchemaVersion,
            ["EmbeddingModelId"] = bank.EmbeddingModelId,
            ["EmbeddingModelPath"] = bank.EmbeddingModelPath,
            ["ResolvedEmbeddingPath"] = embeddingTarget.Path,
            ["EmbeddingSource"] = embeddingTarget.Source,
            ["TrainingImageCount"] = bank.TrainingImageCount,
            ["PatchSize"] = bank.PatchSize,
            ["PatchStride"] = bank.PatchStride,
            ["RequestedThreshold"] = requestedThreshold,
            ["ThresholdUsed"] = result.ThresholdUsed,
            ["CandidateProfileEnabled"] = candidateProfile.Enabled,
            ["CandidateProfile"] = candidateProfile.Profile,
            ["CandidateProfileApplied"] = candidateProfile.Applied,
            ["CandidateFallbackMode"] = candidateProfile.FallbackMode,
            ["CandidateProfileFallbackReason"] = candidateProfile.FallbackReason,
            ["MeanNearestDistance"] = bank.MeanNearestDistance,
            ["StdNearestDistance"] = bank.StdNearestDistance
        };

        result.ScoreMap.Dispose();

        return new Dictionary<string, object>
        {
            ["AnomalyScore"] = result.Score,
            ["IsAnomaly"] = result.IsAnomaly,
            ["AnomalyMap"] = new ImageWrapper(result.Heatmap),
            ["AnomalyMask"] = new ImageWrapper(result.Mask),
            ["FeatureBankPath"] = featureBank.Path,
            ["PatchCount"] = result.PatchCount,
            ["ThresholdUsed"] = result.ThresholdUsed,
            ["Diagnostics"] = diagnostics
        };
    }

    private EmbeddingModelResolution ResolveEmbeddingModelTarget(Operator @operator, SimplePatchCoreFeatureBank? bank, string featureExtractorId)
    {
        if (!RequiresOnnxEmbedding(featureExtractorId))
        {
            return EmbeddingModelResolution.Empty;
        }

        var explicitPath = GetStringParam(@operator, "EmbeddingModelPath", string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolvedExplicitPath = Path.GetFullPath(explicitPath);
            if (!File.Exists(resolvedExplicitPath))
            {
                throw new InvalidOperationException($"Embedding model file not found: {resolvedExplicitPath}");
            }

            return new EmbeddingModelResolution(resolvedExplicitPath, "ExplicitPath", string.Empty, GetStringParam(@operator, "ModelCatalogPath", string.Empty));
        }

        var embeddingModelId = GetStringParam(@operator, "EmbeddingModelId", string.Empty);
        var catalogPath = GetStringParam(@operator, "ModelCatalogPath", string.Empty);
        if (!string.IsNullOrWhiteSpace(embeddingModelId))
        {
            var resolvedCatalogPath = ModelCatalog.ResolveExplicitOrCatalogPath(
                explicitPath: null,
                embeddingModelId,
                catalogPath,
                SupportedEmbeddingCatalogTypes,
                out _);

            if (!File.Exists(resolvedCatalogPath))
            {
                throw new InvalidOperationException($"Embedding model file not found: {resolvedCatalogPath}");
            }

            return new EmbeddingModelResolution(Path.GetFullPath(resolvedCatalogPath), "ModelCatalog", embeddingModelId, catalogPath);
        }

        if (bank != null)
        {
            if (!string.IsNullOrWhiteSpace(bank.EmbeddingModelPath) && File.Exists(bank.EmbeddingModelPath))
            {
                return new EmbeddingModelResolution(Path.GetFullPath(bank.EmbeddingModelPath), "FeatureBankMetadataPath", bank.EmbeddingModelId, catalogPath);
            }

            if (!string.IsNullOrWhiteSpace(bank.EmbeddingModelId))
            {
                var resolvedCatalogPath = ModelCatalog.ResolveExplicitOrCatalogPath(
                    explicitPath: null,
                    bank.EmbeddingModelId,
                    catalogPath,
                    SupportedEmbeddingCatalogTypes,
                    out _);

                if (File.Exists(resolvedCatalogPath))
                {
                    return new EmbeddingModelResolution(Path.GetFullPath(resolvedCatalogPath), "FeatureBankMetadataModelId", bank.EmbeddingModelId, catalogPath);
                }
            }
        }

        return EmbeddingModelResolution.Empty;
    }

    private EmbeddingModelResolution ResolveInferenceEmbeddingModelTarget(Operator @operator, SimplePatchCoreFeatureBank bank)
    {
        if (!RequiresOnnxEmbedding(bank.FeatureExtractorId))
        {
            return EmbeddingModelResolution.Empty;
        }

        if (!string.IsNullOrWhiteSpace(bank.EmbeddingModelPath) && File.Exists(bank.EmbeddingModelPath))
        {
            return new EmbeddingModelResolution(Path.GetFullPath(bank.EmbeddingModelPath), "FeatureBankMetadataPath", bank.EmbeddingModelId, GetStringParam(@operator, "ModelCatalogPath", string.Empty));
        }

        if (!string.IsNullOrWhiteSpace(bank.EmbeddingModelId))
        {
            var catalogPath = GetStringParam(@operator, "ModelCatalogPath", string.Empty);
            var resolvedCatalogPath = ModelCatalog.ResolveExplicitOrCatalogPath(
                explicitPath: null,
                bank.EmbeddingModelId,
                catalogPath,
                SupportedEmbeddingCatalogTypes,
                out _);

            if (File.Exists(resolvedCatalogPath))
            {
                return new EmbeddingModelResolution(Path.GetFullPath(resolvedCatalogPath), "FeatureBankMetadataModelId", bank.EmbeddingModelId, catalogPath);
            }
        }

        return EmbeddingModelResolution.Empty;
    }

    private FeatureBankResolution ResolveFeatureBankInputTarget(Operator @operator)
    {
        var explicitPath = GetStringParam(@operator, "FeatureBankPath", string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolvedExplicitPath = Path.GetFullPath(explicitPath);
            if (!File.Exists(resolvedExplicitPath))
            {
                throw new InvalidOperationException($"Feature bank file not found: {resolvedExplicitPath}");
            }

            return new FeatureBankResolution(resolvedExplicitPath, "ExplicitPath", string.Empty, string.Empty);
        }

        var modelId = GetStringParam(@operator, "ModelId", string.Empty);
        var catalogPath = GetStringParam(@operator, "ModelCatalogPath", string.Empty);
        var resolved = ModelCatalog.ResolveExplicitOrCatalogPath(
            explicitPath: null,
            modelId,
            catalogPath,
            SupportedCatalogTypes,
            out _);

        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException($"Feature bank file not found: {resolved}");
        }

        return new FeatureBankResolution(Path.GetFullPath(resolved), "ModelCatalog", modelId, catalogPath);
    }

    private FeatureBankResolution ResolveFeatureBankSaveTarget(Operator @operator)
    {
        var explicitSavePath = GetStringParam(@operator, "SaveFeatureBankPath", string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitSavePath))
        {
            return new FeatureBankResolution(Path.GetFullPath(explicitSavePath), "ExplicitSavePath", string.Empty, string.Empty);
        }

        var featureBankPath = GetStringParam(@operator, "FeatureBankPath", string.Empty);
        if (!string.IsNullOrWhiteSpace(featureBankPath))
        {
            return new FeatureBankResolution(Path.GetFullPath(featureBankPath), "FeatureBankPath", string.Empty, string.Empty);
        }

        var modelId = GetStringParam(@operator, "ModelId", string.Empty);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new FeatureBankResolution(string.Empty, "Unspecified", string.Empty, string.Empty);
        }

        var resolved = ModelCatalog.ResolveExplicitOrCatalogPath(
            explicitPath: null,
            modelId,
            GetStringParam(@operator, "ModelCatalogPath", string.Empty),
            SupportedCatalogTypes,
            out _);
        return new FeatureBankResolution(Path.GetFullPath(resolved), "ModelCatalog", modelId, GetStringParam(@operator, "ModelCatalogPath", string.Empty));
    }

    private static ImageWrapper ResolvePreviewImage(Dictionary<string, object>? inputs, IReadOnlyList<ImageWrapper> normalImages)
    {
        return ImageWrapper.TryGetFromInputs(inputs, "Image", out var preview) && preview != null
            ? preview
            : normalImages[0];
    }

    private static bool TryGetNormalImages(Dictionary<string, object>? inputs, out List<ImageWrapper> images, out string error)
    {
        images = [];
        error = string.Empty;

        if (inputs == null || !inputs.TryGetValue("NormalImages", out var normalImagesObj) || normalImagesObj == null)
        {
            error = "NormalImages is required in train mode.";
            return false;
        }

        if (normalImagesObj is IEnumerable<ImageWrapper> wrappers)
        {
            images = wrappers.Where(x => x != null).ToList();
            if (images.Count == 0)
            {
                error = "NormalImages is empty.";
                return false;
            }

            return true;
        }

        if (normalImagesObj is IEnumerable<object> values)
        {
            foreach (var value in values)
            {
                if (ImageWrapper.TryGetFromObject(value, out var image) && image != null)
                {
                    images.Add(image);
                }
            }
        }

        if (images.Count == 0)
        {
            error = "NormalImages must contain at least one valid image.";
            return false;
        }

        return true;
    }

    private static string NormalizeMode(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? "inference"
            : raw.Trim().ToLowerInvariant();
    }

    private sealed record FeatureBankResolution(string Path, string Source, string ModelId, string CatalogPath);
    private sealed record EmbeddingModelResolution(string Path, string Source, string ModelId, string CatalogPath)
    {
        public static EmbeddingModelResolution Empty => new(string.Empty, "None", string.Empty, string.Empty);
    }

    private sealed record CandidateProfileState(
        bool Enabled,
        string Profile,
        string FallbackMode,
        bool Applied = false,
        string FallbackReason = "");

    private CandidateProfileState ResolveCandidateProfile(Operator @operator)
    {
        return new CandidateProfileState(
            GetBoolParam(@operator, "EnableCandidateProfile", false),
            NormalizeCandidateProfile(GetStringParam(@operator, "CandidateProfile", CandidateProfileDefault)),
            NormalizeCandidateFallbackMode(GetStringParam(@operator, "CandidateFallbackMode", CandidateFallbackUseDefault)));
    }

    private static string NormalizeCandidateProfile(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? CandidateProfileDefault
            : raw.Trim().ToLowerInvariant();
    }

    private static string NormalizeCandidateFallbackMode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CandidateFallbackUseDefault;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "usedefault" or "use_default" or "use-default" => CandidateFallbackUseDefault,
            "fail" => CandidateFallbackFail,
            _ => raw.Trim()
        };
    }

    private static bool IsSupportedCandidateProfile(string profile)
    {
        return profile is CandidateProfileDefault or CandidateProfileMvtecLiteV2;
    }

    private static bool IsSupportedCandidateFallbackMode(string fallbackMode)
    {
        return fallbackMode is CandidateFallbackUseDefault or CandidateFallbackFail;
    }

    private static SimplePatchCoreOptions ApplyMvtecLiteV2Profile(SimplePatchCoreOptions source)
    {
        return CloneOptions(
            source,
            patchSize: MvtecLiteV2PatchSize,
            patchStride: MvtecLiteV2PatchStride,
            coresetRatio: MvtecLiteV2CoresetRatio,
            backbone: MvtecLiteV2Backbone,
            featureExtractorId: MvtecLiteV2FeatureExtractorId,
            embeddingModelId: string.Empty,
            embeddingModelPath: string.Empty);
    }

    private static bool IsMvtecLiteV2FeatureBankCompatible(SimplePatchCoreFeatureBank bank, out string fallbackReason)
    {
        if (bank.PatchSize != MvtecLiteV2PatchSize)
        {
            fallbackReason = $"Feature bank patch size {bank.PatchSize} does not match mvtec_lite_v2 patch size {MvtecLiteV2PatchSize}.";
            return false;
        }

        if (bank.PatchStride != MvtecLiteV2PatchStride)
        {
            fallbackReason = $"Feature bank patch stride {bank.PatchStride} does not match mvtec_lite_v2 patch stride {MvtecLiteV2PatchStride}.";
            return false;
        }

        if (!string.Equals(bank.Backbone, MvtecLiteV2Backbone, StringComparison.OrdinalIgnoreCase))
        {
            fallbackReason = $"Feature bank backbone '{bank.Backbone}' does not match mvtec_lite_v2 backbone '{MvtecLiteV2Backbone}'.";
            return false;
        }

        if (!string.Equals(bank.FeatureExtractorId, MvtecLiteV2FeatureExtractorId, StringComparison.OrdinalIgnoreCase))
        {
            fallbackReason = $"Feature bank extractor '{bank.FeatureExtractorId}' does not match mvtec_lite_v2 extractor '{MvtecLiteV2FeatureExtractorId}'.";
            return false;
        }

        fallbackReason = string.Empty;
        return true;
    }

    private static bool RequiresOnnxEmbedding(string featureExtractorId)
    {
        return string.Equals(featureExtractorId?.Trim(), "onnx_embedding", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetConfiguredStringParam(Operator @operator, string name)
    {
        var parameter = @operator.Parameters.FirstOrDefault(p => p.Name == name);
        if (parameter?.Value == null)
        {
            return null;
        }

        var value = parameter.Value.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static SimplePatchCoreOptions CloneOptions(
        SimplePatchCoreOptions source,
        int? patchSize = null,
        int? patchStride = null,
        double? coresetRatio = null,
        string? backbone = null,
        string? featureExtractorId = null,
        string? embeddingModelId = null,
        string? embeddingModelPath = null)
    {
        return new SimplePatchCoreOptions
        {
            PatchSize = patchSize ?? source.PatchSize,
            PatchStride = patchStride ?? source.PatchStride,
            CoresetRatio = coresetRatio ?? source.CoresetRatio,
            Backbone = backbone ?? source.Backbone,
            FeatureExtractorId = featureExtractorId ?? source.FeatureExtractorId,
            EmbeddingModelId = embeddingModelId ?? source.EmbeddingModelId,
            EmbeddingModelPath = embeddingModelPath ?? source.EmbeddingModelPath
        };
    }
}
