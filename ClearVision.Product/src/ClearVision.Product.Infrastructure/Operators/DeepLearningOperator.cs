// DeepLearningOperator.cs
// 深度学习算子 - 使用 ONNX 模型进行 AI 缺陷检测
// 作者：蘅芜君

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// YOLO 模型版本
/// </summary>
public enum YoloVersion
{
    /// <summary>
    /// 自动检测
    /// </summary>
    Auto = 0,

    /// <summary>
    /// YOLOv5
    /// </summary>
    YOLOv5 = 5,

    /// <summary>
    /// YOLOv6
    /// </summary>
    YOLOv6 = 6,

    /// <summary>
    /// YOLOv8
    /// </summary>
    YOLOv8 = 8,

    /// <summary>
    /// YOLOv11
    /// </summary>
    YOLOv11 = 11
}

public enum DetectionOutputFormat
{
    Auto = 0,
    RawYolo = 1,
    EndToEndNms = 2
}

/// <summary>
/// 深度学习算子 - 使用 ONNX 模型进行 AI 缺陷检测
/// 支持 YOLOv5, YOLOv6, YOLOv8, YOLOv11 等多种模型格式
/// </summary>
[OperatorMeta(
    DisplayName = "深度学习",
    Description = "统一 ONNX 深度学习推理入口，支持目标检测、图像分类和语义分割；默认保持历史 YOLO 目标检测行为。",
    CategoryId = OperatorCategoryId.AiInference,
    IconName = "ai",
    Keywords = new[] { "深度学习", "AI", "模型", "推理", "缺陷识别", "目标检测", "图像分类", "语义分割", "ONNX", "YOLO", "Deep learning" },
    Version = "1.1.1"
)]
[OperatorParameterRule("TaskType", ReasonCode = "DEEP_LEARNING_TASK_TYPE")]
[OperatorParameterRule("ModelPath", RequiredPolicy = OperatorParameterRequiredPolicy.Required,
    AtLeastOneGroup = "deep-learning-model-source", MutuallyExclusiveGroup = "deep-learning-model-source",
    ResourceKind = OperatorResourceKind.ModelResource, ReasonCode = "DEEP_LEARNING_MODEL_SOURCE_REQUIRED")]
[OperatorParameterRule("ModelId", RequiredPolicy = OperatorParameterRequiredPolicy.Required,
    AtLeastOneGroup = "deep-learning-model-source", MutuallyExclusiveGroup = "deep-learning-model-source",
    ResourceKind = OperatorResourceKind.ModelResource, ReasonCode = "DEEP_LEARNING_MODEL_SOURCE_REQUIRED")]
[OperatorParameterRule("ModelCatalogPath", RequiredPolicy = OperatorParameterRequiredPolicy.Optional,
    DisabledWhenAny = new[] { "ModelId:empty", "ModelPath:not-empty" },
    ResourceKind = OperatorResourceKind.ModelCatalog, ReasonCode = "DEEP_LEARNING_CATALOG_REQUIRES_MODEL_ID")]
[OperatorParameterRule("LabelsPath", RequiredPolicy = OperatorParameterRequiredPolicy.Optional,
    DisabledWhenAll = new[] { "TaskType==SemanticSegmentation" }, HiddenWhenAll = new[] { "TaskType==SemanticSegmentation" },
    IgnoredWhenAll = new[] { "TaskType==SemanticSegmentation" }, ResourceKind = OperatorResourceKind.ModelLabels,
    ReasonCode = "DEEP_LEARNING_LABELS_FOR_DETECTION_OR_CLASSIFICATION")]
[OperatorParameterRule("Confidence", DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_CONFIDENCE_ONLY_FOR_DETECTION")]
[OperatorParameterRule("ModelVersion", DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_YOLO_VERSION_ONLY_FOR_DETECTION")]
[OperatorParameterRule("InputSize", DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_DETECTION_INPUT_SIZE_ONLY_FOR_DETECTION")]
[OperatorParameterRule("TargetClasses", RequiredPolicy = OperatorParameterRequiredPolicy.Optional, DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_TARGET_CLASSES_ONLY_FOR_DETECTION")]
[OperatorParameterRule("GpuDeviceId", DisabledWhenAll = new[] { "ExecutionProvider==CPU" }, ReasonCode = "DEEP_LEARNING_GPU_DEVICE_DISABLED_WITHOUT_GPU")]
[OperatorParameterRule("UseGpu", DisabledWhenAny = new[] { "ExecutionProvider==CPU", "ExecutionProvider==CUDA" }, ReasonCode = "DEEP_LEARNING_USE_GPU_ONLY_FOR_AUTO_PROVIDER")]
[OperatorParameterRule("EnableInternalNms", DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation", "OutputFormat==EndToEndNms" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_MODEL_OWNS_END_TO_END_NMS")]
[OperatorParameterRule("NmsIouThreshold",
    RequiredWhenAll = new[] { "OutputFormat==RawYolo", "EnableInternalNms==true" },
    RequiredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==Auto" },
    DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation", "OutputFormat==EndToEndNms", "EnableInternalNms==false" },
    HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation", "OutputFormat==EndToEndNms", "EnableInternalNms==false" },
    IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation", "OutputFormat==EndToEndNms", "EnableInternalNms==false" },
    ReasonCode = "DEEP_LEARNING_NMS_THRESHOLD_ACTIVE_FOR_INTERNAL_NMS")]
[OperatorParameterRule("OutputFormat", DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_OUTPUT_FORMAT_ONLY_FOR_DETECTION")]
[OperatorParameterRule("DetectionMode", DisabledWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ImageClassification", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_DETECTION_MODE_ONLY_FOR_DETECTION")]
[OperatorParameterRule("TopK", DisabledWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_TOP_K_ONLY_FOR_CLASSIFICATION")]
[OperatorParameterRule("ClassificationInputSize", DisabledWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_INPUT_SIZE_ONLY_FOR_CLASSIFICATION")]
[OperatorParameterRule("ClassificationScoreMode", DisabledWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, HiddenWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, IgnoredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SCORE_MODE_ONLY_FOR_CLASSIFICATION")]
[OperatorParameterRule("ClassNames", RequiredPolicy = OperatorParameterRequiredPolicy.Optional, DisabledWhenAll = new[] { "TaskType==ObjectDetection" }, HiddenWhenAll = new[] { "TaskType==ObjectDetection" }, IgnoredWhenAll = new[] { "TaskType==ObjectDetection" }, ReasonCode = "DEEP_LEARNING_CLASS_NAMES_FOR_CLASSIFICATION_OR_SEGMENTATION")]
[OperatorParameterRule("SegmentationInputSize", DisabledWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, HiddenWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, IgnoredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_INPUT_SIZE_ONLY_FOR_SEGMENTATION")]
[OperatorParameterRule("NumClasses", DisabledWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, HiddenWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, IgnoredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_CLASS_COUNT_ONLY_FOR_SEGMENTATION")]
[OperatorParameterRule("MaxClassMasks", DisabledWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, HiddenWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, IgnoredWhenAny = new[] { "TaskType==ObjectDetection", "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_CLASS_MASKS_ONLY_FOR_SEGMENTATION")]
[OperatorParameterRule("ExecutionProvider", ReasonCode = "DEEP_LEARNING_EXECUTION_PROVIDER")]
[OperatorParameterRule("ScaleToUnitRange", DisabledWhenAll = new[] { "TaskType==ObjectDetection" }, HiddenWhenAll = new[] { "TaskType==ObjectDetection" }, IgnoredWhenAll = new[] { "TaskType==ObjectDetection" }, ReasonCode = "DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION")]
[OperatorParameterRule("ChannelOrder", DisabledWhenAll = new[] { "TaskType==ObjectDetection" }, HiddenWhenAll = new[] { "TaskType==ObjectDetection" }, IgnoredWhenAll = new[] { "TaskType==ObjectDetection" }, ReasonCode = "DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION")]
[OperatorParameterRule("Mean", DisabledWhenAll = new[] { "TaskType==ObjectDetection" }, HiddenWhenAll = new[] { "TaskType==ObjectDetection" }, IgnoredWhenAll = new[] { "TaskType==ObjectDetection" }, ReasonCode = "DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION")]
[OperatorParameterRule("Std", DisabledWhenAll = new[] { "TaskType==ObjectDetection" }, HiddenWhenAll = new[] { "TaskType==ObjectDetection" }, IgnoredWhenAll = new[] { "TaskType==ObjectDetection" }, ReasonCode = "DEEP_LEARNING_PREPROCESS_ONLY_FOR_CLASSIFICATION_OR_SEGMENTATION")]
[OperatorOutputRule("DetectionList", AvailableWhenAll = new[] { "TaskType==ObjectDetection" }, ReasonCode = "DEEP_LEARNING_DETECTION_OUTPUT")]
[OperatorOutputRule("Defects", AvailableWhenAll = new[] { "TaskType==ObjectDetection", "DetectionMode==Defect" }, ReasonCode = "DEEP_LEARNING_DEFECT_OUTPUT")]
[OperatorOutputRule("DefectCount", AvailableWhenAll = new[] { "TaskType==ObjectDetection", "DetectionMode==Defect" }, ReasonCode = "DEEP_LEARNING_DEFECT_OUTPUT")]
[OperatorOutputRule("Objects", AvailableWhenAll = new[] { "TaskType==ObjectDetection", "DetectionMode==Object" }, ReasonCode = "DEEP_LEARNING_OBJECT_OUTPUT")]
[OperatorOutputRule("ObjectCount", AvailableWhenAll = new[] { "TaskType==ObjectDetection", "DetectionMode==Object" }, ReasonCode = "DEEP_LEARNING_OBJECT_OUTPUT")]
[OperatorOutputRule("TopClassLabel", AvailableWhenAll = new[] { "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_CLASSIFICATION_OUTPUT")]
[OperatorOutputRule("TopClassConfidence", AvailableWhenAll = new[] { "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_CLASSIFICATION_OUTPUT")]
[OperatorOutputRule("ClassificationTopK", AvailableWhenAll = new[] { "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_CLASSIFICATION_OUTPUT")]
[OperatorOutputRule("ClassificationResult", AvailableWhenAll = new[] { "TaskType==ImageClassification" }, ReasonCode = "DEEP_LEARNING_CLASSIFICATION_OUTPUT")]
[OperatorOutputRule("SegmentationMap", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorOutputRule("ColoredMap", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorOutputRule("ClassMasks", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorOutputRule("ClassCount", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorOutputRule("ClassMaskCount", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorOutputRule("OmittedClassMaskCount", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorOutputRule("PresentClasses", AvailableWhenAll = new[] { "TaskType==SemanticSegmentation" }, ReasonCode = "DEEP_LEARNING_SEGMENTATION_OUTPUT")]
[OperatorGenerationDependency(typeof(DeepLearningTaskResolver))]
[OperatorGenerationDependency(typeof(SemanticSegmentationOperator))]
[OperatorGenerationDependency(typeof(DeepLearningLabelResolver))]
[InputPort("Image", "输入图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "结果图像", PortDataType.Image)]
[OutputPort("OriginalImage", "原始图像", PortDataType.Image)]
[OutputPort("DetectionList", "检测列表", PortDataType.DetectionList)]
[OutputPort("Defects", "缺陷列表", PortDataType.DetectionList)]
[OutputPort("DefectCount", "缺陷数量", PortDataType.Integer)]
[OutputPort("Objects", "目标列表", PortDataType.DetectionList)]
[OutputPort("ObjectCount", "目标数量", PortDataType.Integer)]
[OutputPort("TaskType", "实际任务类型", PortDataType.String)]
[OutputPort("RequestedTaskType", "请求任务类型", PortDataType.String)]
[OutputPort("TaskResolutionSource", "任务识别来源", PortDataType.String)]
[OutputPort("TaskResolutionEvidence", "任务识别依据", PortDataType.String)]
[OutputPort("StatusCode", "状态码", PortDataType.String)]
[OutputPort("StatusMessage", "状态信息", PortDataType.String)]
[OutputPort("TopClassLabel", "最高类别", PortDataType.String)]
[OutputPort("TopClassConfidence", "最高类别置信度", PortDataType.Float)]
[OutputPort("ClassificationTopK", "分类 Top-K", PortDataType.Any)]
[OutputPort("ClassificationResult", "分类结果", PortDataType.Any)]
[OutputPort("SegmentationMap", "分割类别图", PortDataType.Image)]
[OutputPort("ColoredMap", "分割可视化", PortDataType.Image)]
[OutputPort("ClassMasks", "类别掩码", PortDataType.Any)]
[OutputPort("ClassCount", "分割类别数", PortDataType.Integer)]
[OutputPort("ClassMaskCount", "类别掩码数", PortDataType.Integer)]
[OutputPort("OmittedClassMaskCount", "未输出类别掩码数", PortDataType.Integer)]
[OutputPort("PresentClasses", "出现类别", PortDataType.Any)]
[OperatorParam("TaskType", "任务类型", "enum", Description = "默认 ObjectDetection 保持旧流程；Auto 仅在模型目录类型或输出形状能唯一判定时生效。", DefaultValue = "ObjectDetection", Options = new[] { "ObjectDetection|目标检测", "ImageClassification|图像分类", "SemanticSegmentation|语义分割", "Auto|可靠自动识别" })]
[OperatorParam("ModelPath", "模型路径", "file", DefaultValue = "")]
[OperatorParam("Confidence", "置信度阈值", "double", DefaultValue = 0.5, Min = 0.0, Max = 1.0)]
[OperatorParam("ModelVersion", "YOLO版本", "enum", DefaultValue = "Auto", Options = new[] { "Auto|自动检测", "YOLOv5|YOLOv5", "YOLOv6|YOLOv6", "YOLOv8|YOLOv8", "YOLOv11|YOLOv11" })]
[OperatorParam("InputSize", "输入尺寸", "int", DefaultValue = 640, Min = 320, Max = 1280)]
[OperatorParam("UseGpu", "使用GPU", "bool", DefaultValue = true)]
[OperatorParam("GpuDeviceId", "GPU设备ID", "int", DefaultValue = 0, Min = 0, Max = 15)]
[OperatorParam("TargetClasses", "目标类别", "string", Description = "检测目标类别（逗号分隔，如 person,car），为空则检测所有类别", DefaultValue = "")]
[OperatorParam("LabelsPath", "标签文件路径", "file", Description = "无 ONNX metadata names 时的后备标签文件路径（每行一个标签）；模型包含 metadata names 时忽略此项。为空时查找模型目录 labels.txt，仍不可用则执行失败。", DefaultValue = "")]
[OperatorParam("EnableInternalNms", "启用内部NMS", "bool", Description = "仅用于 RawYolo 输出的后处理开关；OutputFormat=EndToEndNms 时信任 ONNX 模型内部候选框抑制/NMS，平台侧不再额外拆出 BoxNms。", DefaultValue = true)]
[OperatorParam("NmsIouThreshold", "NMS IoU Threshold", "double", Description = "内部 NMS 与预览 NMS 使用的 IoU 阈值。", DefaultValue = 0.45, Min = 0.0, Max = 1.0)]
[OperatorParam("OutputFormat", "输出格式", "enum", Description = "Auto 自动识别；RawYolo 表示原始 YOLO 输出；EndToEndNms 表示模型已输出 NMS 后的 [x1,y1,x2,y2,score,class] 检测结果。", DefaultValue = "Auto", Options = new[] { "Auto|自动识别", "RawYolo|原始 YOLO", "EndToEndNms|端到端 NMS" })]
[OperatorParam("DetectionMode", "检测模式", "enum", Description = "缺陷检测：检出目标视为缺陷(NG)；目标检测：检出目标视为正常(OK)", DefaultValue = "Defect", Options = new[] { "Defect|缺陷检测", "Object|目标检测" })]
[OperatorParam("TopK", "分类 Top-K", "int", DefaultValue = 5, Min = 1, Max = 100)]
[OperatorParam("ClassificationInputSize", "分类输入尺寸", "string", DefaultValue = "Auto", Description = "Auto 使用模型目录或 ONNX 静态输入尺寸；也可填写 Width,Height。")]
[OperatorParam("ClassificationScoreMode", "分类分数模式", "enum", DefaultValue = "Auto", Options = new[] { "Auto|自动识别 logits/概率", "Logits|执行 Softmax", "Probabilities|概率直出" })]
[OperatorParam("ClassNames", "类别名称", "string", DefaultValue = "", Description = "JSON 数组或逗号分隔；ONNX metadata names 和模型目录 class_names 优先。")]
[OperatorParam("SegmentationInputSize", "分割输入尺寸", "string", DefaultValue = "Auto", Description = "Auto 使用模型目录或 ONNX 静态输入尺寸；也可填写 Width,Height。")]
[OperatorParam("NumClasses", "分割类别数", "int", DefaultValue = 21, Min = 2, Max = 4096)]
[OperatorParam("MaxClassMasks", "最大类别掩码数", "int", DefaultValue = 32, Min = 0, Max = 4096)]
[OperatorParam("ExecutionProvider", "执行后端", "enum", DefaultValue = "Auto", Options = new[] { "Auto|跟随 UseGpu", "CPU|CPU", "CUDA|CUDA 优先并允许 CPU 回退" })]
[OperatorParam("ScaleToUnitRange", "缩放到 0-1", "bool", DefaultValue = true)]
[OperatorParam("ChannelOrder", "通道顺序", "enum", DefaultValue = "RGB", Options = new[] { "RGB|RGB", "BGR|BGR" })]
[OperatorParam("Mean", "归一化均值", "string", DefaultValue = "0,0,0")]
[OperatorParam("Std", "归一化标准差", "string", DefaultValue = "1,1,1")]
[OutputPort("ResolvedModelPath", "Resolved Model Path", PortDataType.String)]
[OutputPort("ResolvedModelId", "Resolved Model Id", PortDataType.String)]
[OutputPort("ResolvedModelCatalogPath", "Resolved Model Catalog Path", PortDataType.String)]
[OutputPort("ModelSource", "Model Source", PortDataType.String)]
[OutputPort("ModelProvenance", "Model Provenance", PortDataType.Any)]
[OutputPort("PostprocessDiagnostics", "Postprocess Diagnostics", PortDataType.Any)]
[OutputPort("OutputFormat", "Output Format", PortDataType.String)]
[OperatorParam("ModelId", "Model Id", "string", DefaultValue = "")]
[OperatorParam("ModelCatalogPath", "Model Catalog Path", "file", DefaultValue = "")]
public class DeepLearningOperator : OperatorBase
{
    private static readonly string[] SupportedCatalogTypes =
    [
        "detection", "object_detection", "deep_learning", "yolo",
        "classification", "image_classification", "classifier",
        "segmentation", "semantic_segmentation"
    ];
    private static readonly DeepLearningRuntimeOptions RuntimeOptions = DeepLearningRuntimeOptions.Load();

    public override OperatorType OperatorType => OperatorType.DeepLearning;

    public DeepLearningOperator(ILogger<DeepLearningOperator> logger) : base(logger) { }

    /// <summary>
    /// 模型缓存 - 避免重复加载
    /// </summary>
    private static readonly ConcurrentDictionary<string, CachedModelSession> _modelCache = new(StringComparer.OrdinalIgnoreCase);

    // Test-only race seam. Null in production, so the normal path performs no allocation,
    // scheduling, or synchronization beyond the single null check.
    internal static Action<OnnxModelFileIdentity>? InitialModelIdentityObservedForTests { get; set; }

    /// <summary>
    /// Serializes cache replacement. Model loading is rare, while one stable gate prevents a
    /// second unbounded per-path lock table from being created by model churn.
    /// </summary>
    private static readonly SemaphoreSlim _modelCacheLoadLock = new(1, 1);
    private const int DefaultMaxCachedModels = 3;
    private const int DefaultOnnxRuntimeThreadCount = 4;
    private const long DefaultTensorPoolMaxBytes = 256L * 1024 * 1024;
    private const int DefaultNmsCandidateLimit = 10000;
    private static readonly LinkedList<string> _modelAccessOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> _modelAccessNodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _modelCacheEvictionLock = new();
    private static readonly ConcurrentDictionary<int, int[]> _inputTensorDimensions = new();

    /// <summary>
    /// 默认输入尺寸（YOLO 模型常用 640x640）
    /// </summary>
    private const int DefaultInputSize = 640;

    /// <summary>
    /// 类别颜色映射
    /// </summary>
    private static readonly Scalar[] ClassColors = new[]
    {
        new Scalar(0, 255, 0),     // 绿色
        new Scalar(255, 0, 0),     // 蓝色
        new Scalar(0, 0, 255),     // 红色
        new Scalar(255, 255, 0),   // 青色
        new Scalar(255, 0, 255),   // 紫色
        new Scalar(0, 255, 255),   // 黄色
        new Scalar(128, 128, 255), // 粉色
        new Scalar(128, 255, 128)  // 浅绿
    };

    private sealed record DeepLearningRuntimeOptions(
        int MaxCachedModels,
        int InterOpThreads,
        int IntraOpThreads,
        long TensorPoolMaxBytes)
    {
        public static DeepLearningRuntimeOptions Load()
        {
            return new DeepLearningRuntimeOptions(
                ReadInt("Performance__DeepLearning__MaxCachedModels", "CV_DEEPLEARNING_MAX_CACHED_MODELS", DefaultMaxCachedModels, 1, 64),
                ReadInt("Performance__DeepLearning__InterOpThreads", "CV_DEEPLEARNING_INTER_OP_THREADS", DefaultOnnxRuntimeThreadCount, 1, 128),
                ReadInt("Performance__DeepLearning__IntraOpThreads", "CV_DEEPLEARNING_INTRA_OP_THREADS", DefaultOnnxRuntimeThreadCount, 1, 128),
                ReadLong("Performance__DeepLearning__TensorPoolMaxBytes", "CV_DEEPLEARNING_TENSOR_POOL_MAX_BYTES", DefaultTensorPoolMaxBytes, 0, long.MaxValue));
        }

        private static int ReadInt(string configurationKey, string environmentKey, int fallback, int min, int max)
        {
            var configured = Environment.GetEnvironmentVariable(configurationKey);
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = Environment.GetEnvironmentVariable(environmentKey);
            }

            return int.TryParse(configured, out var parsed)
                ? Math.Clamp(parsed, min, max)
                : fallback;
        }

        private static long ReadLong(string configurationKey, string environmentKey, long fallback, long min, long max)
        {
            var configured = Environment.GetEnvironmentVariable(configurationKey);
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = Environment.GetEnvironmentVariable(environmentKey);
            }

            if (!long.TryParse(configured, out var parsed))
            {
                return fallback;
            }

            return Math.Min(Math.Max(parsed, min), max);
        }
    }

    /// <summary>
    /// COCO 80类标签名映射
    /// </summary>
    private sealed class LabelSourceInfo
    {
        public required string[] Labels { get; init; }
        public required string Source { get; init; }
        public string Path { get; init; } = string.Empty;
        public bool IsFileBacked { get; init; }
    }

    private sealed class CachedModelSession
    {
        private int _leaseCount;
        private int _disposeRequested;
        private int _disposed;

        public CachedModelSession(InferenceSession session)
        {
            Session = session;
        }

        public InferenceSession Session { get; }

        public bool TryAcquire([NotNullWhen(true)] out ModelSessionLease? lease)
        {
            lease = null;

            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return false;
            }

            Interlocked.Increment(ref _leaseCount);

            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                Release();
                return false;
            }

            lease = new ModelSessionLease(this);
            return true;
        }

        public void MarkForDisposal()
        {
            Interlocked.Exchange(ref _disposeRequested, 1);
            TryDispose();
        }

        private void Release()
        {
            var remainingLeases = Interlocked.Decrement(ref _leaseCount);
            if (remainingLeases < 0)
            {
                throw new InvalidOperationException("Model session lease count dropped below zero.");
            }

            if (remainingLeases == 0)
            {
                TryDispose();
            }
        }

        private void TryDispose()
        {
            if (Volatile.Read(ref _disposeRequested) == 0 || Volatile.Read(ref _leaseCount) != 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Session.Dispose();
            }
        }

        public sealed class ModelSessionLease : IDisposable
        {
            private CachedModelSession? _owner;

            public ModelSessionLease(CachedModelSession owner)
            {
                _owner = owner;
            }

            public InferenceSession Session => _owner?.Session ?? throw new ObjectDisposedException(nameof(ModelSessionLease));

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Release();
            }
        }
    }

    private sealed class LabelContract
    {
        public required string[] ResolvedLabels { get; init; }
        public required string[] MetadataLabels { get; init; }
        public required string[] ExternalLabels { get; init; }
        public required string ResolvedLabelSource { get; init; }
        public string ResolvedLabelPath { get; init; } = string.Empty;
        public string ValidationStatus { get; init; } = "Unknown";
        public string? ValidationMessage { get; init; }
        public bool IsValid => string.IsNullOrWhiteSpace(ValidationMessage);
    }

    /// <summary>
    /// 执行算子核心逻辑
    /// </summary>
    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        // 1. 获取输入图像
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return OperatorExecutionOutput.Failure("未提供输入图像");
        }

        // 2. 获取参数
        var explicitModelPath = GetStringParam(@operator, "ModelPath", string.Empty);
        var modelId = GetStringParam(@operator, "ModelId", string.Empty);
        var requestedTaskRaw = GetStringParam(@operator, "TaskType", "ObjectDetection");
        if (!DeepLearningTaskResolver.TryParse(requestedTaskRaw, out var requestedTaskType))
        {
            return OperatorExecutionOutput.Failure(
                "TaskType must be ObjectDetection, ImageClassification, SemanticSegmentation or Auto.");
        }

        // 3. 验证模型路径
        if (string.IsNullOrWhiteSpace(explicitModelPath) && string.IsNullOrWhiteSpace(modelId))
        {
            return OperatorExecutionOutput.Failure("未指定模型路径或模型标识");
        }

        ResolvedModelTarget modelTarget;
        try
        {
            modelTarget = ResolveModelTarget(@operator);
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }

        var modelPath = modelTarget.ResolvedPath;

        if (!File.Exists(modelPath))
        {
            return OperatorExecutionOutput.Failure($"模型文件不存在: {modelPath}");
        }

        // 4. 解码图像
        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return OperatorExecutionOutput.Failure("无法解码输入图像");
        }

        var originalWidth = src.Width;
        var originalHeight = src.Height;

        // 5. 加载模型（支持GPU加速 - P3-O3.1）
        var executionProvider = GetStringParam(@operator, "ExecutionProvider", "Auto");
        if (!TryResolveSessionGpuMode(@operator, executionProvider, out var useGpu, out var providerError))
        {
            return OperatorExecutionOutput.Failure(providerError);
        }

        var gpuDeviceId = GetIntParam(@operator, "GpuDeviceId", 0, 0, 15);
        using var modelSessionLease = await AcquireModelSessionWithVerifiedExecutionProviderAsync(modelPath, useGpu, gpuDeviceId, cancellationToken).ConfigureAwait(false);
        if (modelSessionLease == null)
        {
            return OperatorExecutionOutput.Failure("模型加载失败");
        }

        var session = modelSessionLease.Session;

        DeepLearningTaskResolution taskResolution;
        try
        {
            taskResolution = DeepLearningTaskResolver.Resolve(
                requestedTaskType,
                modelTarget.Entry?.Type,
                GetOutputSignatures(session));
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }

        if (taskResolution.TaskType == DeepLearningTaskType.ImageClassification)
        {
            return ExecuteImageClassification(
                @operator,
                src,
                session,
                modelTarget,
                requestedTaskType,
                taskResolution,
                cancellationToken);
        }

        if (taskResolution.TaskType == DeepLearningTaskType.SemanticSegmentation)
        {
            return await ExecuteSemanticSegmentationAsync(
                @operator,
                src,
                session,
                modelTarget,
                requestedTaskType,
                taskResolution,
                cancellationToken).ConfigureAwait(false);
        }

        var confidenceThreshold = GetFloatParam(@operator, "Confidence", 0.5f, 0.0f, 1.0f);
        var inputSize = GetIntParam(@operator, "InputSize", DefaultInputSize);
        var yoloVersionStr = GetStringParam(@operator, "ModelVersion", "Auto");
        var yoloVersion = ParseYoloVersion(yoloVersionStr);
        var targetClassesStr = GetStringParam(@operator, "TargetClasses", string.Empty);
        var labelsPath = ResolveLabelsPath(@operator);
        var enableInternalNms = GetBoolParam(@operator, "EnableInternalNms", true);
        var nmsIouThreshold = GetFloatParam(@operator, "NmsIouThreshold", 0.45f, 0.0f, 1.0f);
        DetectionOutputFormat outputFormat;
        try
        {
            outputFormat = ParseDetectionOutputFormat(GetStringParam(@operator, "OutputFormat", "Auto"));
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }

        var labels = Array.Empty<string>();
        var unresolvedTargetClasses = new List<string>();
        HashSet<int>? targetClasses = null;

        // 6. 预处理图像
        var labelContract = ResolveLabelContract(session, labelsPath, modelPath, targetClassesStr);
        if (!labelContract.IsValid)
        {
            return OperatorExecutionOutput.Failure(labelContract.ValidationMessage!);
        }

        labels = labelContract.ResolvedLabels;
        unresolvedTargetClasses = FindUnresolvedTargetClasses(targetClassesStr, labels);
        if (unresolvedTargetClasses.Count > 0)
        {
            const string labelSource = "the active labels";
            return OperatorExecutionOutput.Failure(
                $"Failed to resolve TargetClasses [{string.Join(", ", unresolvedTargetClasses)}] against {labelSource}. Set LabelsPath or place labels.txt next to the model.");
        }

        targetClasses = ParseTargetClasses(targetClassesStr, labels);
        Logger.LogInformation(
            "[DeepLearning] Using {Count} labels. TargetClasses={TargetStr}, LabelSource={LabelSource}, ValidationStatus={ValidationStatus}",
            labels.Length,
            targetClassesStr,
            labelContract.ResolvedLabelSource,
            labelContract.ValidationStatus);
        Logger.LogInformation(
            "[DeepLearning] Label contract resolved. LabelContractSource={LabelContractSource}, LabelContractStatus={LabelContractStatus}",
            labelContract.ResolvedLabelSource,
            labelContract.ValidationStatus);

        using var inputTensor = PreprocessImageLease(src, inputSize);
        Logger.LogDebug("[DeepLearning] 输入张量形状: [1, 3, {InputSize}, {InputSize}]", inputSize, inputSize);

        // 7. 执行推理
        var inferenceOutput = RunInference(session, inputTensor.Tensor, labels.Length);
        var outputTensor = inferenceOutput.Tensor;
        Logger.LogInformation(
            "[DeepLearning] Output tensor selected. OutputTensorName={OutputTensorName}, OutputTensorShape={OutputTensorShape}, SelectionRule={SelectionRule}",
            inferenceOutput.OutputName,
            string.Join(", ", inferenceOutput.OutputShape),
            inferenceOutput.SelectionRule);

        // 8. 自动检测 YOLO 版本
        Logger.LogDebug("[DeepLearning] 参数ModelVersion: '{YoloVersionStr}', 解析为: {YoloVersion}", yoloVersionStr, yoloVersion);

        if (yoloVersion == YoloVersion.Auto)
        {
            yoloVersion = DetectYoloVersion(outputTensor, labels.Length);
        }
        Logger.LogInformation("[DeepLearning] 最终使用YOLO版本: {YoloVersion}, 置信度阈值: {Confidence}", yoloVersion, confidenceThreshold);

        // 9. 后处理
        var postprocessResult = PostprocessResultsWithDiagnostics(outputTensor, confidenceThreshold, originalWidth, originalHeight, inputSize, yoloVersion, outputFormat, labels.Length, targetClasses, enableInternalNms, nmsIouThreshold);
        var detections = postprocessResult.Detections;
        Logger.LogInformation("[DeepLearning] 检测到目标数量: {DetectionCount}", detections.Count);

        var detectionMode = GetStringParam(@operator, "DetectionMode", "Defect");
        var isObjectMode = detectionMode.Equals("Object", StringComparison.OrdinalIgnoreCase);

        // 10. 绘制结果
        var visualizationDetections = BuildVisualizationDetections(
            detections,
            confidenceThreshold,
            enableInternalNms || postprocessResult.ResolvedOutputFormat == DetectionOutputFormat.EndToEndNms,
            nmsIouThreshold);
        var outputImage = DrawResults(src, visualizationDetections, labels, detectionMode);

        // 11. 构建输出 - Sprint 1 Task 1.2: 使用 DetectionList 类型
        var outputDetections = new List<Core.ValueObjects.DetectionResult>(detections.Count);
        foreach (var detection in detections)
        {
            outputDetections.Add(new Core.ValueObjects.DetectionResult
            {
                Label = GetClassName(detection.ClassId, labels),
                Confidence = detection.Confidence,
                X = detection.X,
                Y = detection.Y,
                Width = detection.Width,
                Height = detection.Height
            });
        }

        var detectionList = new DetectionList(outputDetections);
        // 输出原始图像（不带任何绘制），供下游节点重新绘制
        var originalImage = src.Clone();

        var additionalData = new Dictionary<string, object>
        {
            { "DetectionMode", detectionMode },
            { "TaskType", DeepLearningTaskType.ObjectDetection.ToString() },
            { "RequestedTaskType", requestedTaskType.ToString() },
            { "TaskResolutionSource", taskResolution.Source },
            { "TaskResolutionEvidence", taskResolution.Evidence },
            { "StatusCode", "OK" },
            { "StatusMessage", "Success" },
            { "InternalNmsEnabled", enableInternalNms || postprocessResult.ResolvedOutputFormat == DetectionOutputFormat.EndToEndNms },
            { "NmsIouThreshold", nmsIouThreshold },
            { "OutputFormat", postprocessResult.ResolvedOutputFormat.ToString() },
            { "RawCandidateCount", postprocessResult.Diagnostics.RawCandidateCount },
            { "PostprocessDiagnostics", postprocessResult.Diagnostics.ToPayload() },
            { "VisualizationDetectionCount", visualizationDetections.Count },
            { "DetectionList", detectionList },
            { "OriginalImage", new ImageWrapper(originalImage) },
            { "LabelSource", labelContract.ResolvedLabelSource },
            { "ResolvedLabels", labelContract.ResolvedLabels },
            { "ModelMetadataLabels", labelContract.MetadataLabels },
            { "LabelsPath", labelContract.ResolvedLabelPath },
            { "LabelValidationStatus", labelContract.ValidationStatus },
            { "ResolvedModelPath", modelTarget.ResolvedPath },
            { "ResolvedModelId", modelTarget.ModelId },
            { "ResolvedModelCatalogPath", modelTarget.CatalogPath },
            { "ModelSource", modelTarget.Source },
            { "ModelProvenance", modelTarget.ToProvenancePayload() }
        };
        if (isObjectMode)
        {
            additionalData["Objects"] = detectionList;
            additionalData["ObjectCount"] = detections.Count;
        }
        else
        {
            additionalData["Defects"] = detectionList;
            additionalData["DefectCount"] = detections.Count;
        }

        Logger.LogInformation("[DeepLearning] 执行完毕. 检测总数: {Count}, 过滤后输出: {DefectCount}", detections.Count, detections.Count);

        return OperatorExecutionOutput.Success(CreateImageOutput(outputImage, additionalData));
    }

    private static IReadOnlyList<OnnxOutputSignature> GetOutputSignatures(InferenceSession session)
    {
        return session.OutputMetadata
            .Select(pair => new OnnxOutputSignature(pair.Key, pair.Value.Dimensions.ToArray()))
            .ToArray();
    }

    private bool TryResolveSessionGpuMode(
        Operator @operator,
        string executionProvider,
        out bool useGpu,
        out string error)
    {
        error = string.Empty;
        useGpu = false;
        switch (executionProvider.Trim().ToUpperInvariant())
        {
            case "AUTO":
                useGpu = GetBoolParam(@operator, "UseGpu", true);
                return true;
            case "CPU":
                return true;
            case "CUDA":
                useGpu = true;
                return true;
            default:
                error = "ExecutionProvider must be Auto, CPU or CUDA.";
                return false;
        }
    }

    private OperatorExecutionOutput ExecuteImageClassification(
        Operator @operator,
        Mat source,
        InferenceSession session,
        ResolvedModelTarget modelTarget,
        DeepLearningTaskType requestedTaskType,
        DeepLearningTaskResolution taskResolution,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputContract = ResolveClassificationInputContract(@operator, session, modelTarget.Entry);
            var channelOrder = GetStringParam(@operator, "ChannelOrder", "RGB").Trim().ToUpperInvariant();
            if (channelOrder is not ("RGB" or "BGR"))
            {
                return OperatorExecutionOutput.Failure("ChannelOrder must be RGB or BGR.");
            }

            if (!TryParseFloatTriplet(GetStringParam(@operator, "Mean", "0,0,0"), out var mean) ||
                !TryParseFloatTriplet(GetStringParam(@operator, "Std", "1,1,1"), out var std) ||
                std.Any(value => value <= 0f))
            {
                return OperatorExecutionOutput.Failure("Mean/Std must contain 3 numeric values and Std must be > 0.");
            }

            var scaleToUnitRange = GetBoolParam(@operator, "ScaleToUnitRange", true);
            var inputName = session.InputMetadata.Keys.Single();
            var tensor = PreprocessClassification(
                source,
                inputContract.Width,
                inputContract.Height,
                inputContract.Layout,
                channelOrder,
                mean,
                std,
                scaleToUnitRange);

            using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
            var classificationOutputs = new List<(string Name, float[] Values, int[] Shape)>();
            foreach (var resultValue in results)
            {
                try
                {
                    var outputTensor = resultValue.AsTensor<float>();
                    if (TryExtractClassificationValues(outputTensor, out var values))
                    {
                        classificationOutputs.Add((resultValue.Name, values, outputTensor.Dimensions.ToArray()));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Ignoring non-float classification output {OutputName}.", resultValue.Name);
                }
            }

            if (classificationOutputs.Count != 1)
            {
                return OperatorExecutionOutput.Failure(
                    $"ImageClassification requires exactly one [classes], [1,classes] or [1,classes,1,1] float output; found {classificationOutputs.Count}.");
            }

            var selected = classificationOutputs[0];
            var (labels, labelSource) = ResolveClassificationLabels(@operator, session, modelTarget.Entry, selected.Values.Length);
            var topK = GetIntParam(@operator, "TopK", 5, min: 1, max: 100);
            var scoreMode = GetStringParam(@operator, "ClassificationScoreMode", "Auto");
            var postprocess = PostprocessClassification(selected.Values, labels, topK, scoreMode);

            var outputImage = source.Clone();
            DrawClassificationResult(outputImage, postprocess.TopPrediction);
            var originalImage = source.Clone();
            var classificationTopK = postprocess.Predictions
                .Select(prediction => new Dictionary<string, object>
                {
                    ["Rank"] = prediction.Rank,
                    ["ClassId"] = prediction.ClassId,
                    ["Label"] = prediction.Label,
                    ["Confidence"] = prediction.Confidence
                })
                .ToArray();
            var classificationResult = new Dictionary<string, object>
            {
                ["ClassId"] = postprocess.TopPrediction.ClassId,
                ["Label"] = postprocess.TopPrediction.Label,
                ["Confidence"] = postprocess.TopPrediction.Confidence,
                ["TopK"] = classificationTopK
            };

            var output = new Dictionary<string, object>
            {
                ["OriginalImage"] = new ImageWrapper(originalImage),
                ["TopClassLabel"] = postprocess.TopPrediction.Label,
                ["TopClassConfidence"] = postprocess.TopPrediction.Confidence,
                ["ClassificationTopK"] = classificationTopK,
                ["ClassificationResult"] = classificationResult,
                ["OutputFormat"] = "ImageClassification",
                ["PostprocessDiagnostics"] = new Dictionary<string, object>
                {
                    ["TaskType"] = DeepLearningTaskType.ImageClassification.ToString(),
                    ["OutputName"] = selected.Name,
                    ["OutputShape"] = selected.Shape,
                    ["InputWidth"] = inputContract.Width,
                    ["InputHeight"] = inputContract.Height,
                    ["InputLayout"] = inputContract.Layout.ToString(),
                    ["InputSizeSource"] = inputContract.Source,
                    ["LabelSource"] = labelSource,
                    ["ResolvedScoreMode"] = postprocess.ResolvedScoreMode
                },
                ["ResolvedModelPath"] = modelTarget.ResolvedPath,
                ["ResolvedModelId"] = modelTarget.ModelId,
                ["ResolvedModelCatalogPath"] = modelTarget.CatalogPath,
                ["ModelSource"] = modelTarget.Source,
                ["ModelProvenance"] = modelTarget.ToProvenancePayload()
            };
            AddTaskContract(output, requestedTaskType, taskResolution);
            return OperatorExecutionOutput.Success(CreateImageOutput(outputImage, output));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Image classification execution failed.");
            return OperatorExecutionOutput.Failure($"Image classification failed: {ex.Message}");
        }
    }

    private async Task<OperatorExecutionOutput> ExecuteSemanticSegmentationAsync(
        Operator @operator,
        Mat source,
        InferenceSession session,
        ResolvedModelTarget modelTarget,
        DeepLearningTaskType requestedTaskType,
        DeepLearningTaskResolution taskResolution,
        CancellationToken cancellationToken)
    {
        var segmentationOperator = BuildSegmentationOperator(@operator, session, modelTarget);
        var executor = new SemanticSegmentationOperator(NullLogger<SemanticSegmentationOperator>.Instance);
        var result = await executor.ExecuteWithSessionAsync(
            segmentationOperator,
            source,
            session,
            modelTarget,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.OutputData == null)
        {
            return result;
        }

        var output = result.OutputData;
        if (output.TryGetValue("ColoredMap", out var coloredValue) && coloredValue is ImageWrapper coloredMap)
        {
            output["Image"] = new ImageWrapper(coloredMap.MatReadOnly.Clone());
        }

        output["OriginalImage"] = new ImageWrapper(source.Clone());
        output["OutputFormat"] = "SemanticSegmentation";
        output["PostprocessDiagnostics"] = new Dictionary<string, object>
        {
            ["TaskType"] = DeepLearningTaskType.SemanticSegmentation.ToString(),
            ["ClassCount"] = output.GetValueOrDefault("ClassCount", 0),
            ["ClassMaskCount"] = output.GetValueOrDefault("ClassMaskCount", 0)
        };
        AddTaskContract(output, requestedTaskType, taskResolution);
        return result;
    }

    private Operator BuildSegmentationOperator(
        Operator source,
        InferenceSession session,
        ResolvedModelTarget modelTarget)
    {
        var op = new Operator(source.Name, OperatorType.SemanticSegmentation, source.Position.X, source.Position.Y);
        AddParameter(op, "ModelPath", modelTarget.ResolvedPath, "file");
        AddParameter(op, "ModelId", string.Empty, "string");
        AddParameter(op, "ModelCatalogPath", string.Empty, "file");
        AddParameter(op, "InputSize", ResolveSegmentationInputSize(source, session, modelTarget.Entry), "string");
        AddParameter(op, "NumClasses", ResolveSegmentationClassCount(source, session, modelTarget.Entry), "int");
        AddParameter(op, "ClassNames", GetStringParam(source, "ClassNames", string.Empty), "string");
        AddParameter(op, "MaxClassMasks", GetIntParam(source, "MaxClassMasks", 32), "int");
        AddParameter(op, "ExecutionProvider", "cpu", "enum");
        AddParameter(op, "ScaleToUnitRange", GetBoolParam(source, "ScaleToUnitRange", true), "bool");
        AddParameter(op, "ChannelOrder", GetStringParam(source, "ChannelOrder", "RGB"), "enum");
        AddParameter(op, "Mean", GetStringParam(source, "Mean", "0,0,0"), "string");
        AddParameter(op, "Std", GetStringParam(source, "Std", "1,1,1"), "string");
        return op;
    }

    private ClassificationInputContract ResolveClassificationInputContract(
        Operator @operator,
        InferenceSession session,
        ModelCatalogEntry? catalogEntry)
    {
        var inputMetadata = session.InputMetadata.Single();
        var dimensions = inputMetadata.Value.Dimensions.ToArray();
        var layout = ResolveTensorLayout(dimensions);
        var configured = GetStringParam(@operator, "ClassificationInputSize", "Auto");
        if (!configured.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseImageSize(configured, out var width, out var height))
            {
                throw new InvalidOperationException("ClassificationInputSize must be Auto, N or Width,Height.");
            }

            return new ClassificationInputContract(width, height, layout, "Parameter");
        }

        if (catalogEntry?.InputSize is { Length: 2 } catalogSize && catalogSize[0] > 0 && catalogSize[1] > 0)
        {
            return new ClassificationInputContract(catalogSize[0], catalogSize[1], layout, "ModelCatalog");
        }

        if (TryGetStaticInputSize(dimensions, layout, out var modelWidth, out var modelHeight))
        {
            return new ClassificationInputContract(modelWidth, modelHeight, layout, "OnnxInputShape");
        }

        throw new InvalidOperationException(
            "Classification input size is dynamic and no catalog input_size is available. Set ClassificationInputSize explicitly.");
    }

    private string ResolveSegmentationInputSize(
        Operator @operator,
        InferenceSession session,
        ModelCatalogEntry? catalogEntry)
    {
        var configured = GetStringParam(@operator, "SegmentationInputSize", "Auto");
        if (!configured.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseImageSize(configured, out var width, out var height))
            {
                throw new InvalidOperationException("SegmentationInputSize must be Auto, N or Width,Height.");
            }

            return $"{width},{height}";
        }

        if (catalogEntry?.InputSize is { Length: 2 } catalogSize && catalogSize[0] > 0 && catalogSize[1] > 0)
        {
            return $"{catalogSize[0]},{catalogSize[1]}";
        }

        var dimensions = session.InputMetadata.Single().Value.Dimensions.ToArray();
        var layout = ResolveTensorLayout(dimensions);
        if (TryGetStaticInputSize(dimensions, layout, out var modelWidth, out var modelHeight))
        {
            return $"{modelWidth},{modelHeight}";
        }

        var legacySize = GetIntParam(@operator, "InputSize", DefaultInputSize, min: 1, max: 8192);
        return $"{legacySize},{legacySize}";
    }

    private int ResolveSegmentationClassCount(
        Operator @operator,
        InferenceSession session,
        ModelCatalogEntry? catalogEntry)
    {
        var configured = GetIntParam(@operator, "NumClasses", 21, min: 2, max: 4096);
        if (configured != 21)
        {
            return configured;
        }

        if (catalogEntry?.NumClasses > 1)
        {
            return catalogEntry.NumClasses;
        }

        var configuredClassNames = ParseClassNames(GetStringParam(@operator, "ClassNames", string.Empty));
        if (configuredClassNames.Length > 1)
        {
            return configuredClassNames.Length;
        }

        var metadataClassNames = DeepLearningLabelResolver.GetMetadataLabels(session);
        if (metadataClassNames.Length > 1)
        {
            return metadataClassNames.Length;
        }

        var inputLayout = ResolveTensorLayout(session.InputMetadata.Single().Value.Dimensions);
        return InferSegmentationClassCountFromOutputShapes(
            configured,
            inputLayout == TensorLayout.Nchw,
            GetOutputSignatures(session));
    }

    internal static int InferSegmentationClassCountFromOutputShapes(
        int fallback,
        bool channelsFirst,
        IReadOnlyCollection<OnnxOutputSignature> outputSignatures)
    {
        var candidates = outputSignatures
            .Where(signature => signature.Dimensions.Length == 4)
            .Select(signature => signature.Dimensions[channelsFirst ? 1 : 3])
            .Where(value => value > 1)
            .Distinct()
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : fallback;
    }

    private static DenseTensor<float> PreprocessClassification(
        Mat source,
        int width,
        int height,
        TensorLayout layout,
        string channelOrder,
        IReadOnlyList<float> mean,
        IReadOnlyList<float> std,
        bool scaleToUnitRange)
    {
        using var bgr = NormalizeToBgr8(source);
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);
        var indexer = resized.GetGenericIndexer<Vec3b>();
        var tensor = layout == TensorLayout.Nchw
            ? new DenseTensor<float>([1, 3, height, width])
            : new DenseTensor<float>([1, height, width, 3]);
        var scale = scaleToUnitRange ? 1.0f / 255.0f : 1.0f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = indexer[y, x];
                var channels = channelOrder == "RGB"
                    ? new[] { pixel.Item2, pixel.Item1, pixel.Item0 }
                    : new[] { pixel.Item0, pixel.Item1, pixel.Item2 };
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = ((channels[channel] * scale) - mean[channel]) / std[channel];
                    if (layout == TensorLayout.Nchw)
                    {
                        tensor[0, channel, y, x] = value;
                    }
                    else
                    {
                        tensor[0, y, x, channel] = value;
                    }
                }
            }
        }

        return tensor;
    }

    private static bool TryExtractClassificationValues(Tensor<float> tensor, out float[] values)
    {
        values = [];
        var dimensions = tensor.Dimensions.ToArray();
        var valid = dimensions.Length switch
        {
            1 => dimensions[0] > 1,
            2 => dimensions[0] == 1 && dimensions[1] > 1,
            4 => dimensions[0] == 1 &&
                 ((dimensions[1] > 1 && dimensions[2] == 1 && dimensions[3] == 1) ||
                  (dimensions[3] > 1 && dimensions[1] == 1 && dimensions[2] == 1)),
            _ => false
        };
        if (!valid)
        {
            return false;
        }

        values = tensor.ToArray();
        return values.Length > 1;
    }

    internal static ClassificationPostprocessResult PostprocessClassification(
        IReadOnlyList<float> rawScores,
        IReadOnlyList<string> labels,
        int topK,
        string scoreMode)
    {
        if (rawScores.Count == 0 || rawScores.Any(score => !float.IsFinite(score)))
        {
            throw new InvalidOperationException("Classification output must contain finite scores.");
        }

        if (labels.Count != rawScores.Count)
        {
            throw new InvalidOperationException(
                $"Classification label count {labels.Count} does not match output class count {rawScores.Count}.");
        }

        var normalizedMode = scoreMode.Trim();
        var isProbabilityVector = rawScores.All(score => score is >= 0f and <= 1f) &&
                                  Math.Abs(rawScores.Sum(score => (double)score) - 1.0) <= 0.01;
        string resolvedMode;
        double[] probabilities;
        if (normalizedMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            resolvedMode = isProbabilityVector ? "Probabilities" : "Logits";
            probabilities = isProbabilityVector
                ? rawScores.Select(score => (double)score).ToArray()
                : Softmax(rawScores);
        }
        else if (normalizedMode.Equals("Logits", StringComparison.OrdinalIgnoreCase))
        {
            resolvedMode = "Logits";
            probabilities = Softmax(rawScores);
        }
        else if (normalizedMode.Equals("Probabilities", StringComparison.OrdinalIgnoreCase))
        {
            if (!isProbabilityVector)
            {
                throw new InvalidOperationException(
                    "ClassificationScoreMode=Probabilities requires finite scores in [0,1] whose sum is approximately 1.");
            }

            resolvedMode = "Probabilities";
            probabilities = rawScores.Select(score => (double)score).ToArray();
        }
        else
        {
            throw new InvalidOperationException(
                "ClassificationScoreMode must be Auto, Logits or Probabilities.");
        }

        var count = Math.Min(Math.Max(topK, 1), probabilities.Length);
        var predictions = probabilities
            .Select((probability, classId) => new { probability, classId })
            .OrderByDescending(item => item.probability)
            .ThenBy(item => item.classId)
            .Take(count)
            .Select((item, index) => new ClassificationPrediction(
                index + 1,
                item.classId,
                labels[item.classId],
                item.probability))
            .ToArray();
        return new ClassificationPostprocessResult(predictions[0], predictions, resolvedMode);
    }

    private (string[] Labels, string Source) ResolveClassificationLabels(
        Operator @operator,
        InferenceSession session,
        ModelCatalogEntry? catalogEntry,
        int classCount)
    {
        var metadataLabels = DeepLearningLabelResolver.GetMetadataLabels(session);
        if (metadataLabels.Length > 0)
        {
            return ValidateLabels(metadataLabels, "ModelMetadata", classCount);
        }

        if (catalogEntry?.ResolvedClassNames is { Length: > 0 } catalogLabels)
        {
            return ValidateLabels(catalogLabels, "ModelCatalog", classCount);
        }

        var configured = ParseClassNames(GetStringParam(@operator, "ClassNames", string.Empty));
        if (configured.Length > 0)
        {
            return ValidateLabels(configured, "ClassNames", classCount);
        }

        var labelsPath = ResolveLabelsPath(@operator);
        if (!string.IsNullOrWhiteSpace(labelsPath) && File.Exists(labelsPath))
        {
            return ValidateLabels(DeepLearningLabelResolver.ReadLabelsFromFile(labelsPath), "LabelsPath", classCount);
        }

        return (Enumerable.Range(0, classCount).Select(index => $"class_{index}").ToArray(), "GeneratedClassIndex");
    }

    private static (string[] Labels, string Source) ValidateLabels(
        IReadOnlyCollection<string> labels,
        string source,
        int classCount)
    {
        if (labels.Count != classCount)
        {
            throw new InvalidOperationException(
                $"Classification label count {labels.Count} from {source} does not match output class count {classCount}.");
        }

        return (labels.ToArray(), source);
    }

    private static string[] ParseClassNames(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return raw.TrimStart().StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<string[]>(raw)?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? []
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"ClassNames must be a JSON array or comma-separated list: {ex.Message}", ex);
        }
    }

    private static void DrawClassificationResult(Mat image, ClassificationPrediction prediction)
    {
        var label = $"{prediction.Label}: {prediction.Confidence:P1}";
        Cv2.Rectangle(image, new Rect(0, 0, image.Width, Math.Min(42, image.Height)), new Scalar(0, 0, 0), -1);
        Cv2.PutText(image, label, new Point(8, Math.Min(30, image.Height - 4)), HersheyFonts.HersheySimplex, 0.75, new Scalar(0, 255, 0), 2);
    }

    private static double[] Softmax(IReadOnlyList<float> logits)
    {
        var maximum = logits.Max();
        var exponentials = logits.Select(value => Math.Exp(value - maximum)).ToArray();
        var sum = exponentials.Sum();
        if (!double.IsFinite(sum) || sum <= 0.0)
        {
            throw new InvalidOperationException("Classification logits could not be normalized with Softmax.");
        }

        return exponentials.Select(value => value / sum).ToArray();
    }

    private static TensorLayout ResolveTensorLayout(IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count != 4)
        {
            throw new InvalidOperationException(
                $"Deep learning image input must be rank 4. Actual shape: [{string.Join(',', dimensions)}].");
        }

        var nchw = dimensions[1] is 3 or -1 or 0;
        var nhwc = dimensions[3] is 3 or -1 or 0;
        if (nchw == nhwc)
        {
            throw new InvalidOperationException(
                $"Unable to determine NCHW/NHWC input layout from [{string.Join(',', dimensions)}].");
        }

        return nchw ? TensorLayout.Nchw : TensorLayout.Nhwc;
    }

    private static bool TryGetStaticInputSize(
        IReadOnlyList<int> dimensions,
        TensorLayout layout,
        out int width,
        out int height)
    {
        width = layout == TensorLayout.Nchw ? dimensions[3] : dimensions[2];
        height = layout == TensorLayout.Nchw ? dimensions[2] : dimensions[1];
        return width > 0 && height > 0;
    }

    private static bool TryParseImageSize(string raw, out int width, out int height)
    {
        width = 0;
        height = 0;
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var square) && square > 0)
        {
            width = square;
            height = square;
            return true;
        }

        return parts.Length == 2 &&
               int.TryParse(parts[0], out width) &&
               int.TryParse(parts[1], out height) &&
               width > 0 &&
               height > 0;
    }

    private static bool TryParseFloatTriplet(string raw, out float[] values)
    {
        values = [];
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        values = new float[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!float.TryParse(parts[index], out values[index]) || !float.IsFinite(values[index]))
            {
                values = [];
                return false;
            }
        }

        return true;
    }

    private static void AddTaskContract(
        IDictionary<string, object> output,
        DeepLearningTaskType requestedTaskType,
        DeepLearningTaskResolution resolution)
    {
        output["TaskType"] = resolution.TaskType.ToString();
        output["RequestedTaskType"] = requestedTaskType.ToString();
        output["TaskResolutionSource"] = resolution.Source;
        output["TaskResolutionEvidence"] = resolution.Evidence;
        output["StatusCode"] = "OK";
        output["StatusMessage"] = "Success";
    }

    private static void AddParameter(Operator @operator, string name, object value, string dataType)
    {
        @operator.AddParameter(new Parameter(
            Guid.NewGuid(),
            name,
            name,
            string.Empty,
            dataType,
            value,
            null,
            null,
            false,
            null));
    }

    internal sealed record ClassificationPrediction(
        int Rank,
        int ClassId,
        string Label,
        double Confidence);

    internal sealed record ClassificationPostprocessResult(
        ClassificationPrediction TopPrediction,
        IReadOnlyList<ClassificationPrediction> Predictions,
        string ResolvedScoreMode);

    private sealed record ClassificationInputContract(
        int Width,
        int Height,
        TensorLayout Layout,
        string Source);

    private enum TensorLayout
    {
        Nchw = 0,
        Nhwc = 1
    }

    private async Task<CachedModelSession.ModelSessionLease?> AcquireModelSessionWithVerifiedExecutionProviderAsync(
        string modelPath,
        bool useGpu = true,
        int gpuDeviceId = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var observedIdentity = OnnxModelFileIdentity.Capture(modelPath);
            InitialModelIdentityObservedForTests?.Invoke(observedIdentity);

            await _modelCacheLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Re-hash after entering the replacement gate. Returning the initially observed
                // cache key would let a same-path replacement race hand a new lease the retired
                // session. Cache hits are therefore decided only from this lock-protected
                // observation.
                var currentIdentity = OnnxModelFileIdentity.Capture(observedIdentity.CanonicalPath);
                var variantPrefix = BuildModelVariantPrefix(currentIdentity.CanonicalPath, useGpu, gpuDeviceId);
                var cacheKey = BuildModelCacheKey(variantPrefix, currentIdentity.ContentSha256);

                if (TryAcquireCachedModel(cacheKey, out var cachedLease))
                {
                    return cachedLease;
                }

                // Load from one exact byte snapshot and re-check the resulting key. The file may
                // have changed between the lock-protected hash and this snapshot.
                var snapshot = OnnxModelFileIdentity.CaptureSnapshot(currentIdentity.CanonicalPath);
                variantPrefix = BuildModelVariantPrefix(snapshot.Identity.CanonicalPath, useGpu, gpuDeviceId);
                cacheKey = BuildModelCacheKey(variantPrefix, snapshot.Identity.ContentSha256);
                if (TryAcquireCachedModel(cacheKey, out cachedLease))
                {
                    return cachedLease;
                }

                var sessionOptions = new SessionOptions
                {
                    InterOpNumThreads = RuntimeOptions.InterOpThreads,
                    IntraOpNumThreads = RuntimeOptions.IntraOpThreads,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                var activeExecutionProvider = "CPU";

                if (useGpu && GpuAvailabilityChecker.IsCudaAvailable)
                {
                    if (TryAppendTensorRtExecutionProvider(sessionOptions, gpuDeviceId, out var tensorRtFailureReason))
                    {
                        activeExecutionProvider = "TensorRT";
                        Logger.LogInformation("[DeepLearning] TensorRT execution provider enabled, device ID: {DeviceId}", gpuDeviceId);
                    }
                    else
                    {
                        if (GpuAvailabilityChecker.IsTensorRtAvailable)
                        {
                            Logger.LogWarning(
                                "[DeepLearning] TensorRT detected but not enabled. Falling back to CUDA. Reason: {Reason}",
                                tensorRtFailureReason);
                        }

                        try
                        {
                            sessionOptions.AppendExecutionProvider_CUDA(gpuDeviceId);
                            activeExecutionProvider = "CUDA";
                            Logger.LogInformation("[DeepLearning] CUDA execution provider enabled, device ID: {DeviceId}", gpuDeviceId);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "[DeepLearning] GPU execution provider enable failed, falling back to CPU");
                        }
                    }
                }
                else
                {
                    Logger.LogInformation("[DeepLearning] Using CPU execution provider");
                }

                // Construct from the bytes that were hashed so the cache identity and the loaded
                // graph cannot diverge through a same-path replacement race.
                var session = new InferenceSession(snapshot.Content, sessionOptions);
                var cacheEntry = new CachedModelSession(session);

                lock (_modelCacheEvictionLock)
                {
                    RetireSupersededModelVersions(variantPrefix, cacheKey);
                    EvictModelsIfNeeded();
                    _modelCache[cacheKey] = cacheEntry;
                    TouchModelCacheKey(cacheKey);
                }

                Logger.LogDebug(
                    "[DeepLearning] Model version {ModelSha256} loaded successfully with execution provider: {ExecutionProvider}",
                    snapshot.Identity.ContentSha256,
                    activeExecutionProvider);
                if (!cacheEntry.TryAcquire(out var createdLease))
                {
                    throw new InvalidOperationException("Newly created model session could not be acquired.");
                }

                return createdLease;
            }
            finally
            {
                _modelCacheLoadLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DeepLearning] Failed to load model with verified execution provider");
            return null;
        }
    }

    private bool TryAcquireCachedModel(
        string cacheKey,
        [NotNullWhen(true)] out CachedModelSession.ModelSessionLease? lease)
    {
        lease = null;
        if (!_modelCache.TryGetValue(cacheKey, out var cachedSessionEntry))
        {
            return false;
        }

        if (cachedSessionEntry.TryAcquire(out lease))
        {
            TouchModelCacheKey(cacheKey);
            return true;
        }

        lock (_modelCacheEvictionLock)
        {
            if (_modelCache.TryRemove(new KeyValuePair<string, CachedModelSession>(cacheKey, cachedSessionEntry)))
            {
                RemoveModelAccessNode(cacheKey);
            }
        }

        lease = null;
        return false;
    }

    private static string BuildModelVariantPrefix(string canonicalPath, bool useGpu, int gpuDeviceId) =>
        $"{canonicalPath}|gpu:{useGpu}|device:{gpuDeviceId}|";

    private static string BuildModelCacheKey(string variantPrefix, string contentSha256) =>
        $"{variantPrefix}sha256:{contentSha256}";

    private bool TryAppendTensorRtExecutionProvider(SessionOptions sessionOptions, int gpuDeviceId, out string failureReason)
    {
        failureReason = string.Empty;

        if (!GpuAvailabilityChecker.IsTensorRtAvailable)
        {
            failureReason = "TensorRT was not detected on this machine.";
            return false;
        }

        try
        {
            var tensorRtMethod = typeof(SessionOptions)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "AppendExecutionProvider_TensorRT", StringComparison.Ordinal) &&
                    method.GetParameters().Length == 1);

            if (tensorRtMethod is null)
            {
                failureReason = "The current OnnxRuntime package does not expose TensorRT provider APIs.";
                return false;
            }

            var optionsParameterType = tensorRtMethod.GetParameters()[0].ParameterType;
            var providerOptions = Activator.CreateInstance(optionsParameterType);
            if (providerOptions is null)
            {
                failureReason = $"Unable to create TensorRT provider options of type '{optionsParameterType.FullName}'.";
                return false;
            }

            SetTensorRtDeviceId(providerOptions, gpuDeviceId);
            tensorRtMethod.Invoke(sessionOptions, new[] { providerOptions });
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            failureReason = ex.InnerException.Message;
            return false;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private static void SetTensorRtDeviceId(object providerOptions, int gpuDeviceId)
    {
        var optionsType = providerOptions.GetType();
        var candidateProperties = new[] { "DeviceId", "GpuDeviceId" };

        foreach (var propertyName in candidateProperties)
        {
            var property = optionsType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanWrite || property.PropertyType != typeof(int))
            {
                continue;
            }

            property.SetValue(providerOptions, gpuDeviceId);
            return;
        }
    }

    public static void UnloadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return;

        var canonicalPath = new FileInfo(Path.GetFullPath(modelPath)).FullName;
        var keyPrefix = $"{canonicalPath}|gpu:";
        var keysToRemove = _modelCache.Keys
            .Where(k => k.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        lock (_modelCacheEvictionLock)
        {
            foreach (var key in keysToRemove)
            {
                if (_modelCache.TryRemove(key, out var session))
                {
                    session.MarkForDisposal();
                }

                RemoveModelAccessNode(key);
            }
        }
    }

    private void TouchModelCacheKey(string cacheKey)
    {
        lock (_modelCacheEvictionLock)
        {
            if (!_modelCache.ContainsKey(cacheKey))
            {
                return;
            }

            RemoveModelAccessNode(cacheKey);
            _modelAccessNodes[cacheKey] = _modelAccessOrder.AddLast(cacheKey);
        }
    }

    private void EvictModelsIfNeeded()
    {
        while (_modelCache.Count >= RuntimeOptions.MaxCachedModels && _modelAccessOrder.Count > 0)
        {
            var oldestKey = _modelAccessOrder.First!.Value;
            RemoveModelAccessNode(oldestKey);

            if (_modelCache.TryRemove(oldestKey, out var oldSession))
            {
                oldSession.MarkForDisposal();
                Logger.LogInformation("[DeepLearning] 驱逐模型缓存: {Key}", oldestKey);
            }
        }
    }

    private static void RetireSupersededModelVersions(string variantPrefix, string protectedKey)
    {
        var supersededKeys = _modelCache.Keys
            .Where(key =>
                key.StartsWith(variantPrefix, StringComparison.OrdinalIgnoreCase) &&
                !key.Equals(protectedKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in supersededKeys)
        {
            if (_modelCache.TryRemove(key, out var superseded))
            {
                superseded.MarkForDisposal();
            }

            RemoveModelAccessNode(key);
        }
    }

    private static void RemoveModelAccessNode(string cacheKey)
    {
        if (_modelAccessNodes.TryGetValue(cacheKey, out var node))
        {
            _modelAccessOrder.Remove(node);
            _modelAccessNodes.Remove(cacheKey);
        }
    }

    /// <summary>
    /// 预处理图像（P3-O3.2: 使用ArrayPool优化内存分配）
    /// </summary>
    private DenseTensor<float> PreprocessImage(Mat src, int inputSize)
    {
        using var lease = PreprocessImageLease(src, inputSize);
        return new DenseTensor<float>(lease.Tensor.ToArray(), GetInputTensorDimensions(inputSize).ToArray());
    }

    private InputTensorLease PreprocessImageLease(Mat src, int inputSize)
    {
        using var normalizedSrc = NormalizeToBgr8(src);

        // 1. 计算缩放比例（保持宽高比）
        var scale = Math.Min((float)inputSize / normalizedSrc.Width, (float)inputSize / normalizedSrc.Height);
        var newWidth = (int)(normalizedSrc.Width * scale);
        var newHeight = (int)(normalizedSrc.Height * scale);

        // 2. Resize
        using var resized = new Mat();
        Cv2.Resize(normalizedSrc, resized, new Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);

        // 3. 创建填充画布（640x640）
        using var padded = new Mat(inputSize, inputSize, MatType.CV_8UC3, new Scalar(114, 114, 114));
        var xOffset = (inputSize - newWidth) / 2;
        var yOffset = (inputSize - newHeight) / 2;

        // 将 resized 图像复制到画布中央
        var roi = new Rect(xOffset, yOffset, newWidth, newHeight);
        using var targetRoi = new Mat(padded, roi);
        resized.CopyTo(targetRoi);

        // 4. 转换为 float 并归一化（除以 255）
        // Direct byte-to-float conversion avoids a full CV_32FC3 intermediate Mat.

        // 5. 提取数据并转换为 CHW 格式（P3-O3.2: 使用ArrayPool）
        // YOLO 模型期望 RGB 顺序，OpenCV 使用 BGR 顺序
        var tensorSize = 1 * 3 * inputSize * inputSize;
        var tensorBytes = (long)tensorSize * sizeof(float);
        var returnToPool = tensorBytes <= RuntimeOptions.TensorPoolMaxBytes;
        var tensorData = returnToPool
            ? ArrayPool<float>.Shared.Rent(tensorSize)
            : new float[tensorSize];
        var matData = padded.GetGenericIndexer<Vec3b>();
        var channelSize = inputSize * inputSize;
        const double scaleToUnit = 1.0 / 255.0;

        for (int h = 0; h < inputSize; h++)
        {
            for (int w = 0; w < inputSize; w++)
            {
                var pixel = matData[h, w];
                var pixelIndex = h * inputSize + w;
                // CHW 格式: [batch, channel, height, width]
                // OpenCV BGR -> 模型 RGB: Item2=R, Item1=G, Item0=B
                tensorData[pixelIndex] = (float)(pixel.Item2 * scaleToUnit);
                tensorData[channelSize + pixelIndex] = (float)(pixel.Item1 * scaleToUnit);
                tensorData[(channelSize * 2) + pixelIndex] = (float)(pixel.Item0 * scaleToUnit);
            }
        }

        return new InputTensorLease(tensorData, tensorSize, GetInputTensorDimensions(inputSize), returnToPool);
    }

    private static int[] GetInputTensorDimensions(int inputSize)
    {
        return _inputTensorDimensions.GetOrAdd(inputSize, static size => new[] { 1, 3, size, size });
    }

    private sealed class InputTensorLease : IDisposable
    {
        private readonly float[] _buffer;
        private readonly bool _returnToPool;
        private int _disposed;

        public InputTensorLease(float[] buffer, int length, int[] dimensions, bool returnToPool)
        {
            _buffer = buffer;
            _returnToPool = returnToPool;
            Tensor = new DenseTensor<float>(buffer.AsMemory(0, length), dimensions);
        }

        public DenseTensor<float> Tensor { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_returnToPool)
            {
                ArrayPool<float>.Shared.Return(_buffer);
            }
        }
    }

    private static Mat NormalizeToBgr8(Mat src)
    {
        if (src.Empty())
        {
            throw new ArgumentException("Source image must not be empty.", nameof(src));
        }

        var normalizedDepth = new Mat();
        try
        {
            ConvertToByteDepth(src, normalizedDepth);
        }
        catch
        {
            normalizedDepth.Dispose();
            throw;
        }

        if (normalizedDepth.Channels() == 3)
        {
            return normalizedDepth;
        }

        var converted = new Mat();
        try
        {
            switch (normalizedDepth.Channels())
            {
                case 1:
                    Cv2.CvtColor(normalizedDepth, converted, ColorConversionCodes.GRAY2BGR);
                    return converted;
                case 4:
                    Cv2.CvtColor(normalizedDepth, converted, ColorConversionCodes.BGRA2BGR);
                    return converted;
                default:
                    throw new NotSupportedException($"Unsupported channel count for deep learning preprocessing: {normalizedDepth.Channels()}.");
            }
        }
        catch
        {
            converted.Dispose();
            throw;
        }
        finally
        {
            normalizedDepth.Dispose();
        }
    }

    private static void ConvertToByteDepth(Mat src, Mat dst)
    {
        var targetType = MatType.MakeType(MatType.CV_8U, src.Channels());
        switch (src.Depth())
        {
            case MatType.CV_8U:
                src.CopyTo(dst);
                return;
            case MatType.CV_16U:
                src.ConvertTo(dst, targetType, 1.0 / 256.0);
                return;
            case MatType.CV_32F:
            case MatType.CV_64F:
                var (floatMin, floatMax) = GetGlobalMinMax(src);
                if (floatMin >= 0d && floatMax <= 1d)
                {
                    src.ConvertTo(dst, targetType, 255.0);
                    return;
                }

                if (floatMin >= 0d && floatMax <= 255d)
                {
                    src.ConvertTo(dst, targetType);
                    return;
                }

                if (floatMin >= 0d && floatMax <= 65535d)
                {
                    src.ConvertTo(dst, targetType, 1.0 / 256.0);
                    return;
                }

                throw new InvalidOperationException("DeepLearning preprocessing only supports float images in [0,1], [0,255], or [0,65535].");
            default:
                throw new NotSupportedException($"Unsupported image depth for deep learning preprocessing: {src.Depth()}.");
        }
    }

    private static (double Min, double Max) GetGlobalMinMax(Mat src)
    {
        if (src.Channels() == 1)
        {
            double minValue;
            double maxValue;
            Cv2.MinMaxLoc(src, out minValue, out maxValue);
            return (minValue, maxValue);
        }

        Cv2.Split(src, out var channels);
        try
        {
            var minValue = double.PositiveInfinity;
            var maxValue = double.NegativeInfinity;
            foreach (var channel in channels)
            {
                double channelMin;
                double channelMax;
                Cv2.MinMaxLoc(channel, out channelMin, out channelMax);
                minValue = Math.Min(minValue, channelMin);
                maxValue = Math.Max(maxValue, channelMax);
            }

            return (minValue, maxValue);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private readonly record struct InferenceTensorSelection(
        DenseTensor<float> Tensor,
        string OutputName,
        int[] OutputShape,
        string SelectionRule);

    /// <summary>
    /// 执行推理
    /// </summary>
    private InferenceTensorSelection RunInference(InferenceSession session, DenseTensor<float> inputTensor, int knownLabelCount)
    {
        var inputName = session.InputMetadata.Keys.First();
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = session.Run(inputs);
        var outputNames = new List<string>();
        var outputShapes = new List<int[]>();
        var outputTensors = new List<Tensor<float>>();

        foreach (var output in results)
        {
            try
            {
                var tensor = output.AsTensor<float>();
                outputNames.Add(output.Name);
                outputShapes.Add(tensor.Dimensions.ToArray());
                outputTensors.Add(tensor);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "[DeepLearning] Ignoring non-float output tensor: {OutputName}", output.Name);
            }
        }

        if (outputTensors.Count == 0)
        {
            throw new InvalidOperationException("No float output tensor was produced by the model.");
        }

        var selection = SelectDetectionOutputIndex(outputNames, outputShapes, knownLabelCount);
        var selectedShape = outputShapes[selection.SelectedIndex];
        var selectedTensor = outputTensors[selection.SelectedIndex];
        var selectedDenseTensor = selectedTensor as DenseTensor<float>
            ?? CopyTensorToDense(selectedTensor, selectedShape);

        return new InferenceTensorSelection(
            selectedDenseTensor,
            outputNames[selection.SelectedIndex],
            selectedShape,
            selection.SelectionRule);
    }

    private static DenseTensor<float> CopyTensorToDense(Tensor<float> source, int[] shape)
    {
        var values = new float[source.Length];
        var index = 0;
        foreach (var value in source)
        {
            values[index++] = value;
        }

        return new DenseTensor<float>(values, shape);
    }

    private static (int SelectedIndex, string SelectionRule) SelectDetectionOutputIndex(
        IReadOnlyList<string> outputNames,
        IReadOnlyList<int[]> outputShapes,
        int knownLabelCount)
    {
        if (outputNames.Count == 0 || outputShapes.Count == 0 || outputNames.Count != outputShapes.Count)
        {
            throw new ArgumentException("Output tensor names and shapes must be non-empty and aligned.");
        }

        if (knownLabelCount > 0)
        {
            var bestIndex = -1;
            var bestAnchor = -1;
            var bestRule = string.Empty;

            for (var i = 0; i < outputShapes.Count; i++)
            {
                if (!TryMatchKnownLabelShape(outputShapes[i], knownLabelCount, out var anchorDim, out var rule))
                {
                    continue;
                }

                if (anchorDim > bestAnchor)
                {
                    bestAnchor = anchorDim;
                    bestIndex = i;
                    bestRule = rule;
                }
            }

            if (bestIndex >= 0)
            {
                return (bestIndex, bestRule);
            }

            throw new InvalidOperationException(
                $"No output tensor matched the configured label count ({knownLabelCount}).");
        }

        var heuristicIndex = -1;
        var heuristicScore = int.MinValue;
        for (var i = 0; i < outputShapes.Count; i++)
        {
            if (!TryGetRank3DetectionScore(outputShapes[i], out var score))
            {
                continue;
            }

            if (score > heuristicScore)
            {
                heuristicScore = score;
                heuristicIndex = i;
            }
        }

        if (heuristicIndex >= 0)
        {
            return (heuristicIndex, "Rank3Heuristic");
        }

        throw new InvalidOperationException("Could not identify a rank-3 detection output tensor.");
    }

    private static bool TryMatchKnownLabelShape(int[] shape, int knownLabelCount, out int anchorDim, out string rule)
    {
        anchorDim = 0;
        rule = string.Empty;

        if (shape.Length != 3)
        {
            return false;
        }

        if (TryMatchFeatureDimension(shape[1], shape[2], knownLabelCount, out rule))
        {
            anchorDim = shape[2];
            return true;
        }

        if (TryMatchFeatureDimension(shape[2], shape[1], knownLabelCount, out rule))
        {
            anchorDim = shape[1];
            return true;
        }

        return false;
    }

    private static bool TryMatchFeatureDimension(int featureDim, int anchorDim, int knownLabelCount, out string rule)
    {
        rule = string.Empty;
        if (anchorDim <= featureDim)
        {
            return false;
        }

        if (featureDim == knownLabelCount + 4)
        {
            rule = "KnownLabelFeature+4";
            return true;
        }

        if (featureDim == knownLabelCount + 5)
        {
            rule = "KnownLabelFeature+5";
            return true;
        }

        return false;
    }

    private static bool TryGetRank3DetectionScore(int[] shape, out int score)
    {
        score = int.MinValue;
        if (shape.Length != 3)
        {
            return false;
        }

        var dimA = shape[1];
        var dimB = shape[2];
        var anchorDim = Math.Max(dimA, dimB);
        var featureDim = Math.Min(dimA, dimB);
        if (anchorDim < 16 || featureDim < 4 || featureDim > 512)
        {
            return false;
        }

        score = (anchorDim * 1024) - featureDim;
        return true;
    }

    /// <summary>
    /// 验证参数
    /// </summary>
    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var modelPath = GetStringParam(@operator, "ModelPath", string.Empty);
        var modelId = GetStringParam(@operator, "ModelId", string.Empty);

        if (string.IsNullOrWhiteSpace(modelPath) && string.IsNullOrWhiteSpace(modelId))
        {
            return ValidationResult.Invalid("必须指定模型路径");
        }

        ResolvedModelTarget modelTarget;
        try
        {
            modelTarget = ResolveModelTarget(@operator);
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid(ex.Message);
        }

        if (!DeepLearningTaskResolver.TryParse(
                GetStringParam(@operator, "TaskType", "ObjectDetection"),
                out var taskType))
        {
            return ValidationResult.Invalid(
                "TaskType must be ObjectDetection, ImageClassification, SemanticSegmentation or Auto.");
        }

        if (taskType == DeepLearningTaskType.Auto &&
            DeepLearningTaskResolver.TryResolveCatalogType(modelTarget.Entry?.Type, out var catalogTask))
        {
            taskType = catalogTask;
        }

        if (!TryResolveSessionGpuMode(
                @operator,
                GetStringParam(@operator, "ExecutionProvider", "Auto"),
                out _,
                out var providerError))
        {
            return ValidationResult.Invalid(providerError);
        }

        if (taskType is DeepLearningTaskType.ObjectDetection or DeepLearningTaskType.Auto)
        {
            var confidence = GetFloatParam(@operator, "Confidence", 0.5f);
            var nmsIouThreshold = GetFloatParam(@operator, "NmsIouThreshold", 0.45f);
            if (confidence < 0 || confidence > 1)
            {
                return ValidationResult.Invalid("置信度阈值必须在 0-1 之间");
            }

            if (nmsIouThreshold < 0 || nmsIouThreshold > 1)
            {
                return ValidationResult.Invalid("NMS IoU threshold must be between 0 and 1.");
            }

            try
            {
                _ = ParseDetectionOutputFormat(GetStringParam(@operator, "OutputFormat", "Auto"));
            }
            catch (Exception ex)
            {
                return ValidationResult.Invalid(ex.Message);
            }
        }

        if (taskType is DeepLearningTaskType.ImageClassification or DeepLearningTaskType.SemanticSegmentation)
        {
            var channelOrder = GetStringParam(@operator, "ChannelOrder", "RGB");
            if (!channelOrder.Equals("RGB", StringComparison.OrdinalIgnoreCase) &&
                !channelOrder.Equals("BGR", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Invalid("ChannelOrder must be RGB or BGR.");
            }

            if (!TryParseFloatTriplet(GetStringParam(@operator, "Mean", "0,0,0"), out _))
            {
                return ValidationResult.Invalid("Mean must contain 3 numeric values.");
            }

            if (!TryParseFloatTriplet(GetStringParam(@operator, "Std", "1,1,1"), out var std) ||
                std.Any(value => value <= 0f))
            {
                return ValidationResult.Invalid("Std must contain 3 positive numeric values.");
            }
        }

        if (taskType == DeepLearningTaskType.ImageClassification)
        {
            var inputSize = GetStringParam(@operator, "ClassificationInputSize", "Auto");
            if (!inputSize.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
                !TryParseImageSize(inputSize, out _, out _))
            {
                return ValidationResult.Invalid(
                    "ClassificationInputSize must be Auto, N or Width,Height.");
            }

            var scoreMode = GetStringParam(@operator, "ClassificationScoreMode", "Auto");
            if (!new[] { "Auto", "Logits", "Probabilities" }.Contains(scoreMode, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.Invalid(
                    "ClassificationScoreMode must be Auto, Logits or Probabilities.");
            }

            _ = GetIntParam(@operator, "TopK", 5, min: 1, max: 100);
        }

        if (taskType == DeepLearningTaskType.SemanticSegmentation)
        {
            var inputSize = GetStringParam(@operator, "SegmentationInputSize", "Auto");
            if (!inputSize.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
                !TryParseImageSize(inputSize, out _, out _))
            {
                return ValidationResult.Invalid(
                    "SegmentationInputSize must be Auto, N or Width,Height.");
            }

            _ = GetIntParam(@operator, "NumClasses", 21, min: 2, max: 4096);
            _ = GetIntParam(@operator, "MaxClassMasks", 32, min: 0, max: 4096);
        }

        return ValidationResult.Valid();
    }

    private ResolvedModelTarget ResolveModelTarget(Operator @operator)
    {
        var modelPath = GetStringParam(@operator, "ModelPath", string.Empty);
        var modelId = GetStringParam(@operator, "ModelId", string.Empty);
        var modelCatalogPath = GetStringParam(@operator, "ModelCatalogPath", string.Empty);

        return ModelCatalog.ResolveExplicitOrCatalog(
            modelPath,
            modelId,
            modelCatalogPath,
            SupportedCatalogTypes);
    }

    /// <summary>
    /// 后处理结果 - 支持多种 YOLO 版本
    /// </summary>
    private List<DetectionResult> PostprocessResults(
        DenseTensor<float> outputTensor,
        float confidenceThreshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        YoloVersion yoloVersion,
        HashSet<int>? targetClasses,
        bool enableInternalNms,
        float nmsIouThreshold)
    {
        // 根据 YOLO 版本选择处理方式
        var detections = yoloVersion switch
        {
            YoloVersion.YOLOv5 => PostprocessYoloV5V6(outputTensor, confidenceThreshold, originalWidth, originalHeight, inputSize, enableInternalNms, nmsIouThreshold),
            YoloVersion.YOLOv6 => PostprocessYoloV5V6(outputTensor, confidenceThreshold, originalWidth, originalHeight, inputSize, enableInternalNms, nmsIouThreshold),
            YoloVersion.YOLOv8 => PostprocessYoloV8V11(outputTensor, confidenceThreshold, originalWidth, originalHeight, inputSize, enableInternalNms, nmsIouThreshold),
            YoloVersion.YOLOv11 => PostprocessYoloV8V11(outputTensor, confidenceThreshold, originalWidth, originalHeight, inputSize, enableInternalNms, nmsIouThreshold),
            _ => PostprocessYoloV8V11(outputTensor, confidenceThreshold, originalWidth, originalHeight, inputSize, enableInternalNms, nmsIouThreshold)
        };

        // 如果指定了目标类别，进行过滤
        if (targetClasses != null && targetClasses.Count > 0)
        {
            var beforeFilter = detections.Count;
            var filteredDetections = new List<DetectionResult>(detections.Count);
            foreach (var detection in detections)
            {
                if (targetClasses.Contains(detection.ClassId))
                {
                    filteredDetections.Add(detection);
                }
            }

            detections = filteredDetections;
            Logger.LogDebug("[DeepLearning] 类别过滤: {BeforeFilter} -> {AfterFilter} (目标类别: {TargetClasses})",
                beforeFilter, detections.Count, string.Join(",", targetClasses));
        }

        return detections;
    }

    private PostprocessResult PostprocessResultsWithDiagnostics(
        DenseTensor<float> outputTensor,
        float confidenceThreshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        YoloVersion yoloVersion,
        DetectionOutputFormat outputFormat,
        int knownLabelCount,
        HashSet<int>? targetClasses,
        bool enableInternalNms,
        float nmsIouThreshold)
    {
        var resolvedOutputFormat = DetectionOutputFormat.RawYolo;
        List<DetectionResult> detections;

        if (ShouldUseEndToEndNmsOutput(outputTensor, outputFormat, knownLabelCount))
        {
            detections = PostprocessEndToEndNmsOutput(
                outputTensor,
                confidenceThreshold,
                originalWidth,
                originalHeight,
                inputSize);
            detections = ApplyTargetClassFilter(detections, targetClasses);
            resolvedOutputFormat = DetectionOutputFormat.EndToEndNms;
        }
        else
        {
            detections = PostprocessResults(
                outputTensor,
                confidenceThreshold,
                originalWidth,
                originalHeight,
                inputSize,
                yoloVersion,
                targetClasses,
                enableInternalNms,
                nmsIouThreshold);
        }

        return new PostprocessResult(
            detections,
            PostprocessDiagnostics.FromRawCandidates(
                detections,
                DefaultNmsCandidateLimit,
                droppedBeforeNms: 0,
                nmsApplied: resolvedOutputFormat == DetectionOutputFormat.RawYolo && enableInternalNms,
                iouComparisons: 0,
                outputFormat: resolvedOutputFormat.ToString()),
            resolvedOutputFormat);
    }

    private List<DetectionResult> ApplyTargetClassFilter(
        List<DetectionResult> detections,
        HashSet<int>? targetClasses)
    {
        if (targetClasses == null || targetClasses.Count == 0)
        {
            return detections;
        }

        var beforeFilter = detections.Count;
        var filteredDetections = new List<DetectionResult>(detections.Count);
        foreach (var detection in detections)
        {
            if (targetClasses.Contains(detection.ClassId))
            {
                filteredDetections.Add(detection);
            }
        }

        Logger.LogDebug("[DeepLearning] 类别过滤: {BeforeFilter} -> {AfterFilter} (目标类别: {TargetClasses})",
            beforeFilter, filteredDetections.Count, string.Join(",", targetClasses));
        return filteredDetections;
    }

    private bool ShouldUseEndToEndNmsOutput(
        DenseTensor<float> outputTensor,
        DetectionOutputFormat outputFormat,
        int knownLabelCount)
    {
        if (outputFormat == DetectionOutputFormat.RawYolo)
        {
            return false;
        }

        var shape = outputTensor.Dimensions.ToArray();
        if (outputFormat == DetectionOutputFormat.EndToEndNms)
        {
            if (!TryGetEndToEndNmsLayout(shape, out var forcedLayout))
            {
                throw new InvalidOperationException(
                    $"DeepLearning OutputFormat=EndToEndNms requires a 2D/3D tensor with 6 or 7 features per detection. Actual shape: {FormatTensorShape(shape)}.");
            }

            if (forcedLayout.DetectionCount > 1000)
            {
                throw new InvalidOperationException(
                    $"DeepLearning OutputFormat=EndToEndNms received {FormatTensorShape(shape)}, which looks like raw YOLO anchor output. Set OutputFormat=RawYolo/Auto or export an ONNX model with NMS.");
            }

            if (!HasPlausibleEndToEndRows(outputTensor, forcedLayout, knownLabelCount, allowEmpty: true))
            {
                throw new InvalidOperationException(
                    $"DeepLearning OutputFormat=EndToEndNms received {FormatTensorShape(shape)}, but sampled rows do not match [x1,y1,x2,y2,score,class]. Verify the model export or set OutputFormat=RawYolo/Auto.");
            }

            return true;
        }

        return IsLikelyEndToEndNmsOutput(outputTensor, knownLabelCount);
    }

    private static string FormatTensorShape(IReadOnlyList<int> shape)
    {
        return shape.Count == 0
            ? "[]"
            : "[" + string.Join(",", shape) + "]";
    }

    private bool IsLikelyEndToEndNmsOutput(DenseTensor<float> outputTensor, int knownLabelCount)
    {
        if (!TryGetEndToEndNmsLayout(outputTensor.Dimensions.ToArray(), out var layout))
        {
            return false;
        }

        if (layout.DetectionCount > 1000)
        {
            return false;
        }

        return HasPlausibleEndToEndRows(outputTensor, layout, knownLabelCount, allowEmpty: false);
    }

    private bool HasPlausibleEndToEndRows(
        DenseTensor<float> outputTensor,
        EndToEndNmsLayout layout,
        int knownLabelCount,
        bool allowEmpty)
    {
        var sampleCount = Math.Min(layout.DetectionCount, 24);
        var positiveScoreRows = 0;
        var validRows = 0;
        for (var i = 0; i < sampleCount; i++)
        {
            var score = ReadEndToEndFeature(outputTensor, layout, i, layout.ScoreIndex);
            if (score <= 0f)
            {
                continue;
            }

            positiveScoreRows++;
            if (score > 1.0f)
            {
                continue;
            }

            var classValue = ReadEndToEndFeature(outputTensor, layout, i, layout.ClassIndex);
            if (!IsPlausibleClassValue(classValue, knownLabelCount))
            {
                continue;
            }

            var x1 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset);
            var y1 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset + 1);
            var x2 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset + 2);
            var y2 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset + 3);
            if (x2 > x1 && y2 > y1)
            {
                validRows++;
            }
        }

        return validRows > 0 || (allowEmpty && positiveScoreRows == 0);
    }

    private List<DetectionResult> PostprocessEndToEndNmsOutput(
        DenseTensor<float> outputTensor,
        float confidenceThreshold,
        int originalWidth,
        int originalHeight,
        int inputSize)
    {
        var detections = new List<DetectionResult>();
        if (!TryGetEndToEndNmsLayout(outputTensor.Dimensions.ToArray(), out var layout))
        {
            return detections;
        }

        var scale = Math.Min((float)inputSize / originalWidth, (float)inputSize / originalHeight);
        var xPad = (inputSize - originalWidth * scale) / 2;
        var yPad = (inputSize - originalHeight * scale) / 2;

        for (var i = 0; i < layout.DetectionCount; i++)
        {
            var score = ReadEndToEndFeature(outputTensor, layout, i, layout.ScoreIndex);
            if (score < confidenceThreshold || score <= 0f)
            {
                continue;
            }

            var classValue = ReadEndToEndFeature(outputTensor, layout, i, layout.ClassIndex);
            var classId = (int)MathF.Round(classValue);
            if (classId < 0)
            {
                continue;
            }

            var x1 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset);
            var y1 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset + 1);
            var x2 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset + 2);
            var y2 = ReadEndToEndFeature(outputTensor, layout, i, layout.BoxOffset + 3);

            if (x2 <= x1 || y2 <= y1)
            {
                continue;
            }

            (x1, y1, x2, y2) = ProjectEndToEndBox(
                x1,
                y1,
                x2,
                y2,
                originalWidth,
                originalHeight,
                scale,
                xPad,
                yPad);

            var width = x2 - x1;
            var height = y2 - y1;
            if (width <= 0f || height <= 0f)
            {
                continue;
            }

            detections.Add(new DetectionResult
            {
                X = x1,
                Y = y1,
                Width = width,
                Height = height,
                Confidence = score,
                ClassId = classId
            });
        }

        Logger.LogDebug("[DeepLearning] EndToEndNms后处理: 输出检测数={DetectionCount}", detections.Count);
        return detections;
    }

    private static (float X1, float Y1, float X2, float Y2) ProjectEndToEndBox(
        float x1,
        float y1,
        float x2,
        float y2,
        int originalWidth,
        int originalHeight,
        float scale,
        float xPad,
        float yPad)
    {
        var maxCoordinate = MathF.Max(MathF.Max(MathF.Abs(x1), MathF.Abs(y1)), MathF.Max(MathF.Abs(x2), MathF.Abs(y2)));
        if (maxCoordinate <= 1.5f)
        {
            x1 *= originalWidth;
            x2 *= originalWidth;
            y1 *= originalHeight;
            y2 *= originalHeight;
        }
        else
        {
            x1 = (x1 - xPad) / scale;
            x2 = (x2 - xPad) / scale;
            y1 = (y1 - yPad) / scale;
            y2 = (y2 - yPad) / scale;
        }

        x1 = Math.Clamp(x1, 0, originalWidth);
        x2 = Math.Clamp(x2, 0, originalWidth);
        y1 = Math.Clamp(y1, 0, originalHeight);
        y2 = Math.Clamp(y2, 0, originalHeight);
        return (x1, y1, x2, y2);
    }

    private static bool IsPlausibleClassValue(float classValue, int knownLabelCount)
    {
        if (float.IsNaN(classValue) || float.IsInfinity(classValue))
        {
            return false;
        }

        var rounded = MathF.Round(classValue);
        if (MathF.Abs(classValue - rounded) > 0.001f)
        {
            return false;
        }

        return knownLabelCount <= 0
            ? rounded >= 0f && rounded <= 1000f
            : rounded >= 0f && rounded < knownLabelCount;
    }

    private static bool TryGetEndToEndNmsLayout(int[] shape, out EndToEndNmsLayout layout)
    {
        layout = default;

        if (shape.Length == 3 && shape[0] == 1)
        {
            if (IsEndToEndFeatureCount(shape[2]) && shape[1] > 0)
            {
                layout = new EndToEndNmsLayout(3, shape[1], shape[2], false);
                return true;
            }

            if (IsEndToEndFeatureCount(shape[1]) && shape[2] > 0)
            {
                layout = new EndToEndNmsLayout(3, shape[2], shape[1], true);
                return true;
            }
        }

        if (shape.Length == 2)
        {
            if (IsEndToEndFeatureCount(shape[1]) && shape[0] > 0)
            {
                layout = new EndToEndNmsLayout(2, shape[0], shape[1], false);
                return true;
            }

            if (IsEndToEndFeatureCount(shape[0]) && shape[1] > 0)
            {
                layout = new EndToEndNmsLayout(2, shape[1], shape[0], true);
                return true;
            }
        }

        return false;
    }

    private static bool IsEndToEndFeatureCount(int value)
    {
        return value == 6 || value == 7;
    }

    private static float ReadEndToEndFeature(DenseTensor<float> tensor, EndToEndNmsLayout layout, int detectionIndex, int featureIndex)
    {
        return layout.Rank switch
        {
            2 when layout.Transposed => tensor[featureIndex, detectionIndex],
            2 => tensor[detectionIndex, featureIndex],
            3 when layout.Transposed => tensor[0, featureIndex, detectionIndex],
            _ => tensor[0, detectionIndex, featureIndex]
        };
    }


    /// <summary>
    /// 后处理 YOLOv8/v11 格式：[1, 84, 8400] 或 [1, 8400, 84] (Transposed)
    /// </summary>
    private List<DetectionResult> PostprocessYoloV8V11(
        DenseTensor<float> outputTensor,
        float confidenceThreshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        bool enableInternalNms,
        float nmsIouThreshold)
    {
        var detections = new List<DetectionResult>();
        var shape = outputTensor.Dimensions.ToArray();

        if (shape.Length != 3)
            return detections;

        // Determine orientation
        // Standard: [1, Features, Anchors] e.g. [1, 84, 8400]
        // Transposed: [1, Anchors, Features] e.g. [1, 8400, 84]

        int numAnchors, numFeatures;
        bool isTransposed = false;

        if (shape[1] > shape[2])
        {
            // Likely transposed: [1, 8400, 84]
            numAnchors = shape[1];
            numFeatures = shape[2];
            isTransposed = true;
            Logger.LogDebug("[DeepLearning] YOLOv8/v11 处理模式: Transposed [1, {NumAnchors}, {NumFeatures}]", numAnchors, numFeatures);
        }
        else
        {
            // Likely standard: [1, 84, 8400]
            numFeatures = shape[1];
            numAnchors = shape[2];
            Logger.LogDebug("[DeepLearning] YOLOv8/v11 处理模式: Standard [1, {NumFeatures}, {NumAnchors}]", numFeatures, numAnchors);
        }

        int numClasses = numFeatures - 4; // 84 - 4 = 80
        float globalMaxConf = 0f;

        var scale = Math.Min((float)inputSize / originalWidth, (float)inputSize / originalHeight);
        var xPad = (inputSize - originalWidth * scale) / 2;
        var yPad = (inputSize - originalHeight * scale) / 2;

        for (int i = 0; i < numAnchors; i++)
        {
            float x, y, w, h;

            if (isTransposed)
            {
                // [1, 8400, 84] -> [0, i, 0..3]
                x = outputTensor[0, i, 0];
                y = outputTensor[0, i, 1];
                w = outputTensor[0, i, 2];
                h = outputTensor[0, i, 3];
            }
            else
            {
                // [1, 84, 8400] -> [0, 0..3, i]
                x = outputTensor[0, 0, i];
                y = outputTensor[0, 1, i];
                w = outputTensor[0, 2, i];
                h = outputTensor[0, 3, i];
            }

            float maxClassProb = 0;
            int maxClassId = 0;

            for (int c = 0; c < numClasses; c++)
            {
                float prob;
                if (isTransposed)
                {
                    prob = outputTensor[0, i, 4 + c];
                }
                else
                {
                    prob = outputTensor[0, 4 + c, i];
                }

                if (prob > maxClassProb)
                {
                    maxClassProb = prob;
                    maxClassId = c;
                }
            }

            if (maxClassProb > globalMaxConf)
            {
                globalMaxConf = maxClassProb;
            }

            if (maxClassProb < confidenceThreshold)
            {
                continue;
            }

            float x1 = (x - w / 2 - xPad) / scale;
            float y1 = (y - h / 2 - yPad) / scale;
            float x2 = (x + w / 2 - xPad) / scale;
            float y2 = (y + h / 2 - yPad) / scale;

            x1 = Math.Max(0, Math.Min(x1, originalWidth));
            y1 = Math.Max(0, Math.Min(y1, originalHeight));
            x2 = Math.Max(0, Math.Min(x2, originalWidth));
            y2 = Math.Max(0, Math.Min(y2, originalHeight));

            detections.Add(new DetectionResult
            {
                X = x1,
                Y = y1,
                Width = x2 - x1,
                Height = y2 - y1,
                Confidence = maxClassProb,
                ClassId = maxClassId
            });
        }

        Logger.LogDebug("[DeepLearning] V8/V11后处理: 最大置信度={GlobalMaxConf:F4}, 阈值={ConfidenceThreshold}, 阈值前检测数={DetectionCount}",
            globalMaxConf, confidenceThreshold, detections.Count);
        if (!enableInternalNms)
        {
            Logger.LogDebug("[DeepLearning] 已禁用内部NMS，输出候选框数: {CandidateCount}", detections.Count);
            return detections;
        }

        var nmsResult = ApplyNMS(detections, nmsIouThreshold);
        Logger.LogDebug("[DeepLearning] NMS后检测数: {NmsCount}", nmsResult.Count);
        return nmsResult;
    }

    /// <summary>
    /// 后处理 YOLOv5/v6 格式：[1, 25200, 85]
    /// </summary>
    private List<DetectionResult> PostprocessYoloV5V6(
        DenseTensor<float> outputTensor,
        float confidenceThreshold,
        int originalWidth,
        int originalHeight,
        int inputSize,
        bool enableInternalNms,
        float nmsIouThreshold)
    {
        var detections = new List<DetectionResult>();
        var shape = outputTensor.Dimensions.ToArray();

        if (shape.Length != 3)
            return detections;
        var isTransposed = shape[1] < shape[2];
        int numAnchors = isTransposed ? shape[2] : shape[1];
        int numFeatures = isTransposed ? shape[1] : shape[2];
        int numClasses = numFeatures - 5;
        float globalMaxConf = 0f;
        var scale = Math.Min((float)inputSize / originalWidth, (float)inputSize / originalHeight);
        var xPad = (inputSize - originalWidth * scale) / 2;
        var yPad = (inputSize - originalHeight * scale) / 2;

        for (int i = 0; i < numAnchors; i++)
        {
            float objConf = isTransposed
                ? outputTensor[0, 4, i]
                : outputTensor[0, i, 4];
            if (objConf < confidenceThreshold)
                continue;

            float x = isTransposed ? outputTensor[0, 0, i] : outputTensor[0, i, 0];
            float y = isTransposed ? outputTensor[0, 1, i] : outputTensor[0, i, 1];
            float w = isTransposed ? outputTensor[0, 2, i] : outputTensor[0, i, 2];
            float h = isTransposed ? outputTensor[0, 3, i] : outputTensor[0, i, 3];

            float maxClassProb = 0;
            int maxClassId = 0;

            for (int c = 0; c < numClasses; c++)
            {
                float prob = isTransposed
                    ? outputTensor[0, 5 + c, i]
                    : outputTensor[0, i, 5 + c];
                if (prob > maxClassProb)
                { maxClassProb = prob; maxClassId = c; }
            }

            float finalConf = objConf * maxClassProb;
            if (finalConf > globalMaxConf)
                globalMaxConf = finalConf;
            if (finalConf < confidenceThreshold)
                continue;

            float x1 = (x - w / 2 - xPad) / scale;
            float y1 = (y - h / 2 - yPad) / scale;
            float x2 = (x + w / 2 - xPad) / scale;
            float y2 = (y + h / 2 - yPad) / scale;

            x1 = Math.Max(0, Math.Min(x1, originalWidth));
            y1 = Math.Max(0, Math.Min(y1, originalHeight));
            x2 = Math.Max(0, Math.Min(x2, originalWidth));
            y2 = Math.Max(0, Math.Min(y2, originalHeight));

            detections.Add(new DetectionResult { X = x1, Y = y1, Width = x2 - x1, Height = y2 - y1, Confidence = finalConf, ClassId = maxClassId });
        }

        Logger.LogDebug("[DeepLearning] V5/V6后处理: 最大置信度={GlobalMaxConf:F4}, 阈值前检测数={DetectionCount}",
            globalMaxConf, detections.Count);
        if (!enableInternalNms)
        {
            Logger.LogDebug("[DeepLearning] 已禁用内部NMS，输出候选框数: {CandidateCount}", detections.Count);
            return detections;
        }

        var nmsResult = ApplyNMS(detections, nmsIouThreshold);
        Logger.LogDebug("[DeepLearning] NMS后检测数: {NmsCount}", nmsResult.Count);
        return nmsResult;
    }

    /// <summary>
    /// 自动检测 YOLO 版本
    /// </summary>
    private YoloVersion DetectYoloVersion(DenseTensor<float> outputTensor, int knownLabelCount = 0)
    {
        var shape = outputTensor.Dimensions.ToArray();

        if (shape.Length != 3)
        {
            Logger.LogDebug("[DeepLearning] 非标准3维张量 (维度数={ShapeLength})，默认使用YOLOv8", shape.Length);
            return YoloVersion.YOLOv8;
        }

        int dim1 = shape[1];
        int dim2 = shape[2];

        // dim1=8400, dim2=84 -> Transposed V8/V11
        // dim1=84, dim2=8400 -> Standard V8/V11
        // dim1=25200, dim2=85 -> Transposed V5/V6 (standard output)

        if (knownLabelCount > 0)
        {
            if (dim1 > dim2)
            {
                if (dim2 == knownLabelCount + 5)
                {
                    Logger.LogDebug("[DeepLearning] 自动检测: YOLOv5/v6自定义类别格式 (anchors={Dim1}, features={Dim2}, labels={KnownLabelCount})", dim1, dim2, knownLabelCount);
                    return YoloVersion.YOLOv5;
                }

                if (dim2 == knownLabelCount + 4)
                {
                    Logger.LogDebug("[DeepLearning] 自动检测: YOLOv8/v11自定义类别格式 (anchors={Dim1}, features={Dim2}, labels={KnownLabelCount})", dim1, dim2, knownLabelCount);
                    return YoloVersion.YOLOv8;
                }
            }
            else
            {
                if (dim1 == knownLabelCount + 5)
                {
                    Logger.LogDebug("[DeepLearning] 自动检测: YOLOv5/v6转置格式 (features={Dim1}, anchors={Dim2}, labels={KnownLabelCount})", dim1, dim2, knownLabelCount);
                    return YoloVersion.YOLOv5;
                }

                if (dim1 == knownLabelCount + 4)
                {
                    Logger.LogDebug("[DeepLearning] 自动检测: YOLOv8/v11标准格式 (features={Dim1}, anchors={Dim2}, labels={KnownLabelCount})", dim1, dim2, knownLabelCount);
                    return YoloVersion.YOLOv8;
                }
            }
        }

        if (dim1 == 85 && dim2 > dim1)
        {
            Logger.LogDebug("[DeepLearning] 自动检测: YOLOv5/v6转置格式 (features={Dim1}, anchors={Dim2})", dim1, dim2);
            return YoloVersion.YOLOv5;
        }

        if (dim1 > dim2)
        {
            // [1, Many, Few]
            // Check feature count (dim2)
            if (dim2 == 85) // 4 box + 1 obj + 80 cls
            {
                Logger.LogDebug("[DeepLearning] 自动检测: YOLOv5/v6格式 (anchors={Dim1}, features={Dim2})", dim1, dim2);
                return YoloVersion.YOLOv5;
            }
            else // e.g. 84 (4 box + 80 cls)
            {
                Logger.LogDebug("[DeepLearning] 自动检测: YOLOv8/v11格式 (Transposed) (anchors={Dim1}, features={Dim2})", dim1, dim2);
                return YoloVersion.YOLOv8; // V8 logic handles V11 too
            }
        }
        else
        {
            // [1, Few, Many]
            // Typically V8/V11: [1, 84, 8400]
            Logger.LogDebug("[DeepLearning] 自动检测: YOLOv8/v11格式 (features={Dim1}, anchors={Dim2})", dim1, dim2);
            return YoloVersion.YOLOv8;
        }
    }

    private YoloVersion ParseYoloVersion(string version)
    {
        return version?.ToLower() switch
        {
            "auto" => YoloVersion.Auto,
            "v5" or "yolov5" or "5" => YoloVersion.YOLOv5,
            "v6" or "yolov6" or "6" => YoloVersion.YOLOv6,
            "v8" or "yolov8" or "8" => YoloVersion.YOLOv8,
            "v11" or "yolov11" or "11" => YoloVersion.YOLOv11,
            _ => YoloVersion.Auto
        };
    }

    private DetectionOutputFormat ParseDetectionOutputFormat(string format)
    {
        return format?.Trim().ToLowerInvariant() switch
        {
            "raw" or "rawyolo" or "yolo" => DetectionOutputFormat.RawYolo,
            "endtoend" or "endtoendnms" or "nms" or "onnxnms" => DetectionOutputFormat.EndToEndNms,
            _ => DetectionOutputFormat.Auto
        };
    }

    /// <summary>
    /// 非极大值抑制 (NMS)
    /// </summary>
    private List<DetectionResult> ApplyNMS(List<DetectionResult> detections, float iouThreshold)
    {
        var (keep, _) = ApplyNmsWithStats(detections, iouThreshold);
        return keep;
    }

    private (List<DetectionResult> Kept, long IoUComparisons) ApplyNmsWithStats(
        List<DetectionResult> detections,
        float iouThreshold)
    {
        if (detections.Count == 0)
        {
            return (detections, 0);
        }

        var keep = new List<DetectionResult>(detections.Count);
        var iouComparisons = 0L;
        var indicesByClass = new Dictionary<int, List<int>>();
        var nmsBoxes = new NmsBox[detections.Count];
        for (var i = 0; i < detections.Count; i++)
        {
            nmsBoxes[i] = ToNmsBox(detections[i]);
            var classId = detections[i].ClassId;
            if (!indicesByClass.TryGetValue(classId, out var indices))
            {
                indices = new List<int>();
                indicesByClass[classId] = indices;
            }

            indices.Add(i);
        }

        var cellSize = GetNmsCellSize(detections);
        foreach (var indices in indicesByClass.Values)
        {
            indices.Sort((left, right) => detections[right].Confidence.CompareTo(detections[left].Confidence));
            var keptBySpatialCell = new Dictionary<long, List<int>>();
            var candidateNeighbors = new HashSet<int>();

            for (var i = 0; i < indices.Count; i++)
            {
                var candidateIndex = indices[i];
                var candidateBox = nmsBoxes[candidateIndex];
                if (!candidateBox.IsValid)
                {
                    continue;
                }

                candidateNeighbors.Clear();
                var minCellX = QuantizeToCell(candidateBox.X1, cellSize);
                var maxCellX = QuantizeToCell(candidateBox.X2, cellSize);
                var minCellY = QuantizeToCell(candidateBox.Y1, cellSize);
                var maxCellY = QuantizeToCell(candidateBox.Y2, cellSize);

                for (var cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    for (var cellY = minCellY; cellY <= maxCellY; cellY++)
                    {
                        var key = BuildSpatialKey(cellX, cellY);
                        if (!keptBySpatialCell.TryGetValue(key, out var neighborIndexes))
                        {
                            continue;
                        }

                        for (var idx = 0; idx < neighborIndexes.Count; idx++)
                        {
                            candidateNeighbors.Add(neighborIndexes[idx]);
                        }
                    }
                }

                var suppressed = false;
                foreach (var keptIndex in candidateNeighbors)
                {
                    iouComparisons++;
                    if (CalculateIoU(candidateBox, nmsBoxes[keptIndex]) > iouThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (suppressed)
                {
                    continue;
                }

                keep.Add(detections[candidateIndex]);
                for (var cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    for (var cellY = minCellY; cellY <= maxCellY; cellY++)
                    {
                        var key = BuildSpatialKey(cellX, cellY);
                        if (!keptBySpatialCell.TryGetValue(key, out var cellEntries))
                        {
                            cellEntries = new List<int>();
                            keptBySpatialCell[key] = cellEntries;
                        }

                        cellEntries.Add(candidateIndex);
                    }
                }
            }
        }

        return (keep, iouComparisons);
    }

    private sealed record PostprocessResult(
        List<DetectionResult> Detections,
        PostprocessDiagnostics Diagnostics,
        DetectionOutputFormat ResolvedOutputFormat);

    private sealed record PostprocessDiagnostics(
        int RawCandidateCount,
        int NmsCandidateLimit,
        int NmsPrefilteredCount,
        int DroppedBeforeNms,
        bool NmsApplied,
        long NmsIoUComparisons,
        string OutputFormat,
        Dictionary<int, int> CandidatesByClass)
    {
        public static PostprocessDiagnostics FromRawCandidates(
            IReadOnlyList<DetectionResult> detections,
            int candidateLimit,
            int droppedBeforeNms,
            bool nmsApplied,
            long iouComparisons,
            string outputFormat)
        {
            var byClass = new Dictionary<int, int>();
            for (var i = 0; i < detections.Count; i++)
            {
                var classId = detections[i].ClassId;
                byClass[classId] = byClass.TryGetValue(classId, out var count) ? count + 1 : 1;
            }

            return new PostprocessDiagnostics(
                detections.Count,
                candidateLimit,
                Math.Max(0, detections.Count - droppedBeforeNms),
                droppedBeforeNms,
                nmsApplied,
                iouComparisons,
                outputFormat,
                byClass);
        }

        public Dictionary<string, object> ToPayload() => new()
        {
            ["RawCandidateCount"] = RawCandidateCount,
            ["NmsCandidateLimit"] = NmsCandidateLimit,
            ["NmsPrefilteredCount"] = NmsPrefilteredCount,
            ["DroppedBeforeNms"] = DroppedBeforeNms,
            ["NmsApplied"] = NmsApplied,
            ["NmsIoUComparisons"] = NmsIoUComparisons,
            ["OutputFormat"] = OutputFormat,
            ["CandidatesByClass"] = CandidatesByClass
        };
    }

    private readonly record struct EndToEndNmsLayout(int Rank, int DetectionCount, int FeatureCount, bool Transposed)
    {
        public int BoxOffset => FeatureCount == 7 ? 1 : 0;
        public int ScoreIndex => BoxOffset + 4;
        public int ClassIndex => BoxOffset + 5;
    }

    private readonly struct NmsBox
    {
        public NmsBox(float x1, float y1, float x2, float y2, float area)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Area = area;
        }

        public float X1 { get; }
        public float Y1 { get; }
        public float X2 { get; }
        public float Y2 { get; }
        public float Area { get; }
        public bool IsValid => Area > 0f;
    }

    private static NmsBox ToNmsBox(DetectionResult detection)
    {
        var x1 = detection.X;
        var y1 = detection.Y;
        var x2 = x1 + Math.Max(0f, detection.Width);
        var y2 = y1 + Math.Max(0f, detection.Height);
        var width = Math.Max(0f, x2 - x1);
        var height = Math.Max(0f, y2 - y1);
        return new NmsBox(x1, y1, x2, y2, width * height);
    }

    private static int GetNmsCellSize(IReadOnlyList<DetectionResult> detections)
    {
        var totalArea = 0f;
        var validCount = 0;
        for (var i = 0; i < detections.Count; i++)
        {
            var width = Math.Max(0f, detections[i].Width);
            var height = Math.Max(0f, detections[i].Height);
            var area = width * height;
            if (area <= 0f)
            {
                continue;
            }

            totalArea += area;
            validCount++;
        }

        if (validCount == 0)
        {
            return 32;
        }

        var meanSideLength = MathF.Sqrt(totalArea / validCount);
        return Math.Clamp((int)MathF.Round(meanSideLength), 16, 256);
    }

    private static int QuantizeToCell(float coordinate, int cellSize)
    {
        return (int)MathF.Floor(coordinate / cellSize);
    }

    private static long BuildSpatialKey(int cellX, int cellY)
    {
        return ((long)cellX << 32) | (uint)cellY;
    }

    /// <summary>
    /// 计算 IoU
    /// </summary>
    private float CalculateIoU(DetectionResult a, DetectionResult b)
    {
        return CalculateIoU(ToNmsBox(a), ToNmsBox(b));
    }

    private static float CalculateIoU(in NmsBox a, in NmsBox b)
    {
        var intersectionWidth = MathF.Min(a.X2, b.X2) - MathF.Max(a.X1, b.X1);
        var intersectionHeight = MathF.Min(a.Y2, b.Y2) - MathF.Max(a.Y1, b.Y1);
        if (intersectionWidth <= 0f || intersectionHeight <= 0f)
        {
            return 0f;
        }

        var intersection = intersectionWidth * intersectionHeight;
        var union = a.Area + b.Area - intersection;
        return union > 0f ? intersection / union : 0f;
    }

    /// <summary>
    /// 解析目标类别字符串
    /// </summary>
    private HashSet<int>? ParseTargetClasses(string targetClassesStr, IReadOnlyList<string>? labels)
    {
        if (string.IsNullOrWhiteSpace(targetClassesStr))
            return null;

        var result = new HashSet<int>();
        var parts = targetClassesStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            // 尝试作为类别ID解析
            if (int.TryParse(trimmed, out var classId))
            {
                result.Add(classId);
            }
            else
            {
                // 尝试作为类别名称解析，查找对应的classId
                var index = FindClassIndex(labels, trimmed);
                if (index >= 0)
                {
                    result.Add(index);
                }
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 加载标签 - 支持自定义标签文件或自动查找
    /// </summary>
    /// <summary>
    /// Validates that named target classes exist in the active label set.
    /// </summary>
    private List<string> FindUnresolvedTargetClasses(string targetClassesStr, IReadOnlyList<string>? labels)
    {
        if (string.IsNullOrWhiteSpace(targetClassesStr))
        {
            return new List<string>();
        }

        var unresolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in targetClassesStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || int.TryParse(trimmed, out _))
            {
                continue;
            }

            if (FindClassIndex(labels, trimmed) >= 0 || !seen.Add(trimmed))
            {
                continue;
            }

            unresolved.Add(trimmed);
        }

        return unresolved;
    }

    private string? TryResolveBundledLabelsPath(string targetClassesStr)
    {
        return DeepLearningLabelResolver.TryResolveBundledLabelsPath(targetClassesStr);
    }

    private LabelContract ResolveLabelContract(
        InferenceSession session,
        string configuredLabelsPath,
        string modelPath,
        string targetClassesStr)
    {
        var metadataLabels = DeepLearningLabelResolver.GetMetadataLabels(session);
        if (metadataLabels.Length > 0)
        {
            return BuildLabelContract(
                modelPath,
                metadataLabels,
                new LabelSourceInfo
                {
                    Labels = Array.Empty<string>(),
                    Source = "IgnoredBecauseModelMetadataExists",
                    Path = string.Empty,
                    IsFileBacked = false
                });
        }

        var externalLabels = LoadExternalLabels(configuredLabelsPath, modelPath, targetClassesStr);
        return BuildLabelContract(modelPath, metadataLabels, externalLabels);
    }

    private LabelContract BuildLabelContract(
        string modelPath,
        string[] metadataLabels,
        LabelSourceInfo externalLabels)
    {
        if (metadataLabels.Length > 0)
        {
            Logger.LogInformation("[DeepLearning] Loaded {Count} labels from ONNX metadata.", metadataLabels.Length);
            if (externalLabels.IsFileBacked)
            {
                Logger.LogInformation(
                    "[DeepLearning] ONNX metadata labels are authoritative; external labels file will be ignored. LabelsPath={LabelsPath}",
                    externalLabels.Path);
            }

            return new LabelContract
            {
                ResolvedLabels = metadataLabels,
                MetadataLabels = metadataLabels,
                ExternalLabels = Array.Empty<string>(),
                ResolvedLabelSource = "ModelMetadata",
                ResolvedLabelPath = string.Empty,
                ValidationStatus = "MetadataOnly"
            };
        }

        if (externalLabels.Labels.Length == 0)
        {
            return new LabelContract
            {
                ResolvedLabels = Array.Empty<string>(),
                MetadataLabels = Array.Empty<string>(),
                ExternalLabels = Array.Empty<string>(),
                ResolvedLabelSource = externalLabels.Source,
                ResolvedLabelPath = externalLabels.Path,
                ValidationStatus = "MissingLabelContract",
                ValidationMessage = BuildMissingLabelContractMessage(modelPath)
            };
        }

        return new LabelContract
        {
            ResolvedLabels = externalLabels.Labels,
            MetadataLabels = Array.Empty<string>(),
            ExternalLabels = externalLabels.Labels,
            ResolvedLabelSource = externalLabels.Source,
            ResolvedLabelPath = externalLabels.Path,
            ValidationStatus = "ExternalLabelsOnly"
        };
    }

    private LabelSourceInfo LoadExternalLabels(string labelFile, string modelPath, string targetClassesStr)
    {
        if (!string.IsNullOrWhiteSpace(labelFile) && File.Exists(labelFile))
        {
            var labels = DeepLearningLabelResolver.ReadLabelsFromFile(labelFile);
            Logger.LogInformation("[DeepLearning] Loaded labels from explicit LabelsPath: {File}", labelFile);
            return new LabelSourceInfo
            {
                Labels = labels,
                Source = "ExplicitFile",
                Path = labelFile,
                IsFileBacked = true
            };
        }

        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            var modelDir = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrWhiteSpace(modelDir))
            {
                var autoLabelFile = Path.Combine(modelDir, "labels.txt");
                if (File.Exists(autoLabelFile))
                {
                    var labels = DeepLearningLabelResolver.ReadLabelsFromFile(autoLabelFile);
                    Logger.LogInformation("[DeepLearning] Loaded labels from model directory: {File}", autoLabelFile);
                    return new LabelSourceInfo
                    {
                        Labels = labels,
                        Source = "ModelDirectoryFile",
                        Path = autoLabelFile,
                        IsFileBacked = true
                    };
                }
            }
        }

        var bundledLabelFile = TryResolveBundledLabelsPath(targetClassesStr);
        if (!string.IsNullOrEmpty(bundledLabelFile))
        {
            var labels = DeepLearningLabelResolver.ReadLabelsFromFile(bundledLabelFile);
            Logger.LogInformation("[DeepLearning] Loaded bundled labels: {File}", bundledLabelFile);
            return new LabelSourceInfo
            {
                Labels = labels,
                Source = "BundledFile",
                Path = bundledLabelFile,
                IsFileBacked = true
            };
        }

        return new LabelSourceInfo
        {
            Labels = Array.Empty<string>(),
            Source = "Unavailable",
            Path = string.Empty,
            IsFileBacked = false
        };
    }

    private static string BuildMissingLabelContractMessage(string modelPath)
    {
        return string.Join(
            Environment.NewLine,
            "Label contract missing: the model does not expose ONNX metadata names and no valid labels file was found.",
            $"ModelPath: {modelPath}",
            "Provide LabelsPath, place labels.txt next to the model, or export the ONNX model with metadata names.");
    }

    private static string ResolveLabelsPath(Operator @operator)
    {
        var labelsPath = @operator.Parameters
            .FirstOrDefault(parameter => parameter.Name.Equals("LabelsPath", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.ToString();

        if (!string.IsNullOrWhiteSpace(labelsPath))
        {
            return labelsPath;
        }

        // Backward compatibility for older flows that still persisted LabelFile.
        return @operator.Parameters
            .FirstOrDefault(parameter => parameter.Name.Equals("LabelFile", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.ToString()
            ?? string.Empty;
    }

    // 存储当前使用的标签数组
    /// <summary>
    /// 获取类别名称
    /// </summary>
    private string GetClassName(int classId, IReadOnlyList<string>? labels)
    {
        if (labels != null && classId >= 0 && classId < labels.Count)
            return labels[classId];
        return $"class_{classId}";
    }

    /// <summary>
    /// 绘制检测结果 - 返回Mat实现零拷贝 (P0优先级)
    /// </summary>
    private List<DetectionResult> BuildVisualizationDetections(
        List<DetectionResult> detections,
        float confidenceThreshold,
        bool enableInternalNms,
        float nmsIouThreshold)
    {
        if (detections.Count == 0)
        {
            return detections;
        }

        if (enableInternalNms)
        {
            return detections;
        }

        // For preview readability we apply a visual-only NMS pass when the node is
        // configured to emit raw candidates to downstream BoxNms.
        var scoreFloor = Math.Max(confidenceThreshold, 0.25f);
        var filtered = new List<DetectionResult>(detections.Count);
        foreach (var detection in detections)
        {
            if (detection.Confidence >= scoreFloor)
            {
                filtered.Add(detection);
            }
        }
        if (filtered.Count == 0)
        {
            filtered = detections;
        }

        return ApplyNMS(filtered, nmsIouThreshold);
    }

    private static string BuildStatisticsLabel(int count, string detectionMode)
    {
        var isObjectMode = detectionMode.Equals("Object", StringComparison.OrdinalIgnoreCase);
        return isObjectMode
            ? $"Objects: {count}"
            : $"Defects: {count}";
    }

    private Mat DrawResults(Mat src, List<DetectionResult> detections, IReadOnlyList<string>? labels, string detectionMode)
    {
        var result = src.Clone();

        for (int i = 0; i < detections.Count; i++)
        {
            var det = detections[i];
            var color = ClassColors[det.ClassId % ClassColors.Length];

            // 绘制矩形框
            var rect = new Rect((int)det.X, (int)det.Y, (int)det.Width, (int)det.Height);
            Cv2.Rectangle(result, rect, color, 2);

            // 准备标签 - 使用真实类别名称
            var className = GetClassName(det.ClassId, labels);
            var label = $"{className}: {det.Confidence:P0}";

            // 计算标签大小
            var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.5, 1, out var baseline);

            // 绘制标签背景
            var labelRect = new Rect(
                (int)det.X,
                (int)Math.Max(det.Y - textSize.Height - 5, 0),
                textSize.Width + 10,
                textSize.Height + 10
            );
            Cv2.Rectangle(result, labelRect, color, -1);

            // 绘制标签文字
            Cv2.PutText(
                result,
                label,
                new Point(det.X + 5, Math.Max(det.Y - 5, textSize.Height)),
                HersheyFonts.HersheySimplex,
                0.5,
                new Scalar(255, 255, 255),
                1,
                LineTypes.AntiAlias
            );
        }

        // 添加统计信息
        var stats = BuildStatisticsLabel(detections.Count, detectionMode);
        Cv2.PutText(
            result,
            stats,
            new Point(10, 30),
            HersheyFonts.HersheySimplex,
            0.7,
            new Scalar(0, 255, 0),
            2,
            LineTypes.AntiAlias
        );

        return result;
    }

    private static int FindClassIndex(IReadOnlyList<string>? labels, string className)
    {
        if (labels != null)
        {
            for (var i = 0; i < labels.Count; i++)
            {
                if (string.Equals(labels[i], className, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }



    /// <summary>
    /// 检测结果结构
    /// </summary>
    private class DetectionResult
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Confidence { get; set; }
        public int ClassId { get; set; }
    }
}
