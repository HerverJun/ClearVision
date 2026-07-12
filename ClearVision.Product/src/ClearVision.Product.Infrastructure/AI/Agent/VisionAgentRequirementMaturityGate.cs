using System.Text.RegularExpressions;
using ClearVision.Product.Core.DTOs;

namespace ClearVision.Product.Infrastructure.AI.Agent;

public sealed record VisionAgentRequirementMaturityRequest
{
    public string? Description { get; init; }
    public string? AdditionalContext { get; init; }
    public string? Mode { get; init; }
    public bool HasCurrentFlow { get; init; }
    public bool HasPendingPlan { get; init; }
    public bool DeveloperDirectBuildDebug { get; init; }
    public AiTemplateSelectionInfo? TemplateSelection { get; init; }
    public string RequirementMode { get; init; } = AiRequirementModes.Strict;
}

internal sealed record VisionAgentRequirementSemanticSlots(
    List<string> ObjectSignals,
    List<string> TaskSignals,
    string? TaskTypeHint);

public static class VisionAgentRequirementMaturityGate
{
    private static readonly string[] BusinessSignals =
    [
        "视觉", "检测", "检验", "测量", "识别", "流程", "工程", "方案", "算法", "模型", "相机", "图像", "引导", "定位",
        "缺陷", "外观", "线序", "端子", "OCR", "二维码", "条码", "DataMatrix", "PLC", "ROI",
        "inspection", "inspect", "detect", "detection", "measure", "measurement", "vision", "workflow", "flow",
        "defect", "ocr", "barcode", "qr", "datamatrix", "plc"
    ];

    private static readonly string[] NewFlowSignals =
    [
        "构建", "生成", "创建", "新建", "新增", "搭建", "做一个", "帮我做", "帮我搞", "设计", "方案",
        "build", "create", "generate", "new flow", "new workflow", "from scratch"
    ];

    private static readonly string[] ModifySignals =
    [
        "当前流程", "已有流程", "现有流程", "这个流程", "修改", "调整", "改成", "替换", "删除", "增加", "参数", "阈值",
        "current flow", "existing flow", "modify", "update", "adjust", "replace", "remove", "add"
    ];

    private static readonly string[] HelpSignals =
    [
        "你能做什么", "可以做什么", "帮助", "help", "how can you help"
    ];

    private static readonly string[] AbstractGoalSignals =
    [
        "终极", "有野心", "高级", "完整方案", "智能方案", "视觉检测方案", "检测方案", "真正", "最佳", "全套",
        "整体方案", "系统方案", "解决方案", "帮我搞一个方案", "ultimate", "ambitious", "advanced solution",
        "complete solution", "full solution"
    ];

    private static readonly Dictionary<string, string[]> TaskSignalTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        [AiVisionTaskTypes.GeometryMeasurement] =
        [
            "测量", "尺寸", "孔距", "圆心距", "圆心距离", "直径", "宽度", "高度", "距离", "间距", "角度", "面积",
            "measure", "measurement", "distance", "diameter", "width", "height", "hole", "spacing", "angle"
        ],
        [AiVisionTaskTypes.WireSequence] =
        [
            "线序", "端子", "线束", "排线", "插线", "颜色顺序", "wire sequence", "terminal", "harness", "wire order"
        ],
        [AiVisionTaskTypes.BarcodeQr] =
        [
            "二维码", "条码", "读码", "扫码", "标签识别", "OCR", "字符", "文字", "DataMatrix", "barcode", "qr", "code", "ocr"
        ],
        [AiVisionTaskTypes.PresenceAbsence] =
        [
            "有无", "漏装", "缺件", "少装", "缺失", "装配完整", "装配是否完整", "是否存在", "presence", "absence", "missing part"
        ],
        [AiVisionTaskTypes.Classification] =
        [
            "分类", "类别", "型号", "类型识别", "classification", "classify", "type recognition"
        ],
        [AiVisionTaskTypes.TemplateLocation] =
        [
            "定位", "对位", "找正", "模板", "匹配", "位姿", "视觉引导", "机械臂引导", "机器人引导", "打螺钉", "拧螺丝",
            "锁螺丝", "焊缝引导", "焊缝跟踪", "涂胶轨迹", "胶路", "轨迹定位", "locate", "position", "align", "template",
            "matching", "pose", "robot guidance", "screw driving", "weld seam", "glue path"
        ],
        [AiVisionTaskTypes.PlcOutput] =
        [
            "PLC", "输出信号", "握手", "地址", "plc output", "station output"
        ],
        [AiVisionTaskTypes.SurfaceOrPoseDefect] =
        [
            "缺陷", "外观", "划痕", "刮伤", "裂纹", "破损", "凹坑", "压痕", "脏污", "污渍", "贴正", "贴歪", "贴附", "胶带", "偏斜",
            "surface", "defect", "scratch", "crack", "damage", "dent", "stain", "tape", "pose"
        ]
    };

    private static readonly string[] ObjectSignals =
    [
        "包装箱", "纸箱", "箱体", "胶带", "金属件", "金属表面", "端子", "线束", "连接器", "标签", "二维码", "条码",
        "圆孔", "圆形孔位", "孔位", "铜孔", "产品", "零件", "遥控器", "按键", "面板", "瓶盖", "螺丝", "螺钉",
        "机械臂", "机器人", "焊缝", "涂胶", "胶路", "工件", "pin", "hole",
        "terminal", "connector", "label", "package", "carton", "part", "product", "button", "wire", "harness", "metal", "surface"
    ];

    private static readonly string[] ImageSourceSignals =
    [
        "相机", "图片", "图像", "照片", "视频", "文件", "采集", "camera", "image", "photo", "video", "file"
    ];

    private static readonly string[] StrategySignals =
    [
        "规则", "模型", "深度学习", "传统算法", "模板", "阈值", "AI", "rule", "model", "deep learning", "template", "threshold"
    ];

    public static AiRequirementMaturityResult Evaluate(VisionAgentRequirementMaturityRequest request)
    {
        var text = NormalizeText(request.Description, request.AdditionalContext);
        var businessHits = HitTerms(text, BusinessSignals);
        var taskHits = HitTaskTerms(text, out var taskType);
        var objectHits = HitTerms(text, ObjectSignals);
        var semanticSlots = ExtractSemanticSlots(text);
        var knownObjectSignals = objectHits.ToList();
        objectHits = objectHits
            .Concat(semanticSlots.ObjectSignals)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        var knownTaskSignals = taskHits.ToList();
        taskHits = taskHits
            .Concat(semanticSlots.TaskSignals)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        if (taskType == AiVisionTaskTypes.Unknown &&
            !string.IsNullOrWhiteSpace(semanticSlots.TaskTypeHint))
        {
            taskType = semanticSlots.TaskTypeHint!;
        }
        var missingFields = new List<string>();
        var blockingReasons = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result(
                AiRequirementMaturity.Ambiguous,
                AiVisionTaskTypes.Unknown,
                canPlan: false,
                canBuild: false,
                objectHits,
                taskHits,
                ["inspection_object", "task_type", "image_source", "acceptance_criteria"],
                ["empty_requirement"],
                "请先说明要检测、测量或识别的对象。");
        }

        if (LooksLikeChatOrHelp(text) && businessHits.Count == 0)
        {
            return Result(
                AiRequirementMaturity.ChatOrHelp,
                AiVisionTaskTypes.Unknown,
                canPlan: false,
                canBuild: false,
                objectHits,
                taskHits,
                [],
                [],
                "这是普通对话或能力咨询，不进入构建。");
        }

        if (request.HasCurrentFlow && ContainsAny(text, ModifySignals))
        {
            return Result(
                AiRequirementMaturity.ModifyExistingFlow,
                taskType == AiVisionTaskTypes.Unknown ? AiVisionTaskTypes.AbstractGoal : taskType,
                canPlan: true,
                canBuild: true,
                objectHits,
                taskHits,
                [],
                [],
                "输入是在当前流程基础上修改。");
        }

        var hasVisionDomain = businessHits.Count > 0 ||
                              taskHits.Count > 0 ||
                              objectHits.Count > 0 ||
                              request.TemplateSelection != null;
        var hasAbstractGoal = ContainsAny(text, AbstractGoalSignals);
        var hasTaskType = taskType != AiVisionTaskTypes.Unknown;
        var hasObject = objectHits.Count > 0;
        var isReliable = IsReliablePlannable(request, null, hasObject, hasTaskType);
        var canPlan = isReliable && !hasAbstractGoal;

        if (hasAbstractGoal && !canPlan)
        {
            missingFields.AddRange(["inspection_object", "task_type", "image_source", "acceptance_criteria", "output_target"]);
            blockingReasons.AddRange(["abstract_goal_needs_decomposition", "task_type_missing", "inspection_object_missing"]);
            return Result(
                AiRequirementMaturity.AbstractGoal,
                AiVisionTaskTypes.AbstractGoal,
                canPlan: false,
                canBuild: false,
                objectHits,
                taskHits,
                missingFields,
                blockingReasons,
                "这是方案愿景，不是可直接构建的检测流程。");
        }

        if (!hasVisionDomain)
        {
            return Result(
                AiRequirementMaturity.ChatOrHelp,
                AiVisionTaskTypes.Unknown,
                canPlan: false,
                canBuild: false,
                objectHits,
                taskHits,
                [],
                [],
                "输入未形成视觉工程需求。");
        }

        if (!canPlan)
        {
            missingFields.AddRange(["inspection_object", "task_type", "image_source", "acceptance_criteria"]);
            blockingReasons.AddRange(["inspection_object_missing", "task_type_missing"]);
            return Result(
                AiRequirementMaturity.Ambiguous,
                AiVisionTaskTypes.Unknown,
                canPlan: false,
                canBuild: false,
                objectHits,
                taskHits,
                missingFields,
                blockingReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                "需求仍缺少检测对象或任务类型，暂不能构建。");
        }

        if (!hasObject)
        {
            missingFields.Add("inspection_object");
            blockingReasons.Add("inspection_object_missing");
        }

        if (!hasTaskType)
        {
            missingFields.Add("task_type");
            blockingReasons.Add("task_type_missing");
        }

        if (!ContainsAny(text, ImageSourceSignals))
        {
            missingFields.Add("image_source");
        }

        if (!HasAcceptanceCriteriaText(text))
        {
            missingFields.Add("acceptance_criteria");
        }

        missingFields = CanonicalMissingFields(missingFields);
        var canBuild = CanBuildFromMissingFields(missingFields, taskType);
        if (!canBuild && hasObject && hasTaskType && !ContainsAny(text, StrategySignals))
        {
            missingFields.Add("model_or_rule_strategy");
            blockingReasons.Add("model_or_rule_strategy_missing");
            missingFields = CanonicalMissingFields(missingFields);
            canBuild = CanBuildFromMissingFields(missingFields, taskType);
        }
        if (!canBuild)
        {
            return Result(
                AiRequirementMaturity.Ambiguous,
                hasTaskType ? taskType : AiVisionTaskTypes.Unknown,
                canPlan: true,
                canBuild: false,
                objectHits,
                taskHits,
                missingFields,
                blockingReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                "需求已足够进入规划，但构建前仍需补充图像来源、判定标准或实现策略。");
        }

        return Result(
            AiRequirementMaturity.Actionable,
            taskType,
            canPlan: true,
            canBuild: true,
            objectHits,
            taskHits,
            CanonicalMissingFields(BuildNonBlockingMissingFields(text)),
            [],
            "需求已明确到可规划视觉流程。");
    }

    public static AiRequirementMaturityResult Evaluate(
        VisionAgentRequirementMaturityRequest request,
        VisionAgentSemanticExtractionResult? semantic)
    {
        if (!ShouldUseStructuredSemantic(semantic))
        {
            return Evaluate(request);
        }

        var normalizedIntent = Clean(semantic!.Intent).ToLowerInvariant();
        var taskType = NormalizeSemanticTaskType(semantic.TaskType);
        var objectSignals = BuildSemanticObjectSignals(semantic);
        var taskSignals = BuildSemanticTaskSignals(semantic, taskType);
        var hasObject = objectSignals.Count > 0;
        var hasTaskType = taskType != AiVisionTaskTypes.Unknown &&
                          taskType != AiVisionTaskTypes.AbstractGoal;
        var hasImageSource = !string.IsNullOrWhiteSpace(semantic.ImageSource);
        var hasAcceptance = !string.IsNullOrWhiteSpace(semantic.OkCondition) ||
                            !string.IsNullOrWhiteSpace(semantic.NgCondition);

        if (normalizedIntent is "help" or "chat")
        {
            return Result(
                AiRequirementMaturity.ChatOrHelp,
                AiVisionTaskTypes.Unknown,
                canPlan: false,
                canBuild: false,
                objectSignals,
                taskSignals,
                [],
                [],
                "语义抽取判断这是普通对话或能力咨询，不进入构建。");
        }

        if (request.HasCurrentFlow && normalizedIntent == "modify_flow")
        {
            return Result(
                AiRequirementMaturity.ModifyExistingFlow,
                hasTaskType ? taskType : AiVisionTaskTypes.AbstractGoal,
                canPlan: true,
                canBuild: true,
                objectSignals,
                taskSignals,
                [],
                [],
                "语义抽取判断这是在当前流程基础上修改。");
        }

        var isVisionRequest = semantic.IsVisionRequest || request.TemplateSelection != null;

        if (taskType == AiVisionTaskTypes.AbstractGoal)
        {
            return Result(
                AiRequirementMaturity.AbstractGoal,
                AiVisionTaskTypes.AbstractGoal,
                canPlan: false,
                canBuild: false,
                objectSignals,
                taskSignals,
                ["inspection_object", "task_type", "image_source", "acceptance_criteria", "output_target"],
                ["abstract_goal_needs_decomposition", "task_type_missing", "inspection_object_missing"],
                "语义抽取判断这是方案愿景，不是可直接构建的检测流程。");
        }

        var hasSemanticObject = !string.IsNullOrWhiteSpace(semantic.InspectionObject);
        var hasSemanticTaskType = !string.IsNullOrWhiteSpace(semantic.TaskType) &&
                                  !string.Equals(semantic.TaskType, AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
                                  !string.Equals(semantic.TaskType, AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase);

        var isReliable = IsReliablePlannable(request, semantic, hasObject, hasTaskType) ||
                         semantic.CanPlanCandidate ||
                         isVisionRequest;
        var canPlan = isReliable;
        if (!canPlan)
        {
            var semanticMissingFields = BuildMissingFields(hasObject, hasTaskType, hasImageSource, hasAcceptance);
            return Result(
                AiRequirementMaturity.Ambiguous,
                AiVisionTaskTypes.Unknown,
                canPlan: false,
                canBuild: false,
                objectSignals,
                taskSignals,
                semanticMissingFields,
                ["inspection_object_missing", "task_type_missing"],
                "语义抽取未形成可规划的视觉工程需求。");
        }

        var missingFields = new List<string>();
        var blockingReasons = new List<string>();
        if (!hasObject)
        {
            missingFields.Add("inspection_object");
            blockingReasons.Add("inspection_object_missing");
        }

        if (!hasTaskType)
        {
            missingFields.Add("task_type");
            blockingReasons.Add("task_type_missing");
        }

        if (!hasImageSource)
        {
            missingFields.Add("image_source");
        }

        if (!hasAcceptance)
        {
            missingFields.Add("acceptance_criteria");
        }

        missingFields = CanonicalMissingFields(missingFields);
        var canBuild = CanBuildFromMissingFields(missingFields, taskType);
        return Result(
            canBuild ? AiRequirementMaturity.Actionable : AiRequirementMaturity.Ambiguous,
            hasTaskType ? taskType : AiVisionTaskTypes.Unknown,
            canPlan: true,
            canBuild,
            objectSignals,
            taskSignals,
            missingFields,
            blockingReasons,
            canBuild
                ? "语义抽取结果已明确到可规划视觉流程。"
                : "语义抽取结果已足够进入规划，但构建前仍需补充图像来源、判定标准或实现策略。");
    }

    public static AiDecisionTrace BuildDecisionTrace(
        VisionAgentRequirementMaturityRequest request,
        AiRequirementMaturityResult maturity,
        string turnIntent,
        string interactionState,
        string fallbackReason = "")
    {
        var text = NormalizeText(request.Description, request.AdditionalContext);
        return new AiDecisionTrace
        {
            RawUserText = Clean(request.Description),
            TurnIntent = turnIntent,
            InteractionState = interactionState,
            BusinessSignalsHit = HitTerms(text, BusinessSignals),
            NewFlowSignalsHit = HitTerms(text, NewFlowSignals),
            TaskTypeSignalsHit = maturity.TaskSignals,
            ObjectSignalsHit = maturity.ObjectSignals,
            MaturityLevel = maturity.Maturity,
            TaskType = maturity.TaskType,
            CanPlan = maturity.CanPlan,
            CanBuild = maturity.CanBuild,
            FallbackReason = fallbackReason,
            BlockingReasons = maturity.BlockingReasons
        };
    }

    public static string ToPlanIntent(AiRequirementMaturityResult maturity)
    {
        return maturity.TaskType switch
        {
            AiVisionTaskTypes.WireSequence => "wire_sequence",
            AiVisionTaskTypes.CodeRecognition => "code_recognition",
            AiVisionTaskTypes.BarcodeQr => "code_recognition",
            AiVisionTaskTypes.GeometryMeasurement => "measurement",
            AiVisionTaskTypes.TemplateLocation => "template_location",
            AiVisionTaskTypes.PlcOutput => "plc_output",
            AiVisionTaskTypes.PresenceAbsence => "presence_absence",
            AiVisionTaskTypes.AttributeClassification => "attribute_classification",
            AiVisionTaskTypes.Classification => "classification",
            AiVisionTaskTypes.SurfaceDefect => "surface_defect",
            AiVisionTaskTypes.SurfaceOrPoseDefect => "surface_defect",
            AiVisionTaskTypes.AbstractGoal => "abstract_goal",
            _ => "general_inspection"
        };
    }

    public static string ToRouterIntent(AiRequirementMaturityResult maturity)
    {
        return maturity.Maturity switch
        {
            AiRequirementMaturity.ChatOrHelp => VisionAgentIntentRouterService.IntentHelp,
            AiRequirementMaturity.ModifyExistingFlow => VisionAgentIntentRouterService.IntentModifyExistingFlow,
            AiRequirementMaturity.Actionable => VisionAgentIntentRouterService.IntentActionableVisionPlan,
            _ when maturity.CanPlan => VisionAgentIntentRouterService.IntentActionableVisionPlan,
            _ => VisionAgentIntentRouterService.IntentAmbiguousVisionRequirement
        };
    }

    public static List<VisionAgentPlanAnswer> ExtractExplicitPlanAnswers(
        VisionAgentRequirementMaturityRequest request,
        VisionAgentSemanticExtractionResult? semantic)
    {
        var text = NormalizeText(request.Description, request.AdditionalContext);
        var answers = new Dictionary<string, VisionAgentPlanAnswer>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var explicitObject = FirstRegexValue(text,
            @"(?:检测目标|检测对象)\s*(?:是|为|:|：)?\s*(?<value>[^，。；;,.!?！？]+)",
            @"读取\s*(?<value>[^，。；;,.!?！？]{1,32}?)\s*上的",
            @"(?:相机|图像|图片|视频|文件)?\s*输入\s*(?<value>[^，。；;,.!?！？\s]{1,32})(?:并|后|，|,)");
        if (string.IsNullOrWhiteSpace(explicitObject) &&
            ContainsLiteralEvidence(text, semantic?.InspectionObject))
        {
            explicitObject = semantic!.InspectionObject;
        }
        if (string.IsNullOrWhiteSpace(explicitObject))
        {
            explicitObject = ExtractSemanticSlots(text).ObjectSignals.FirstOrDefault() ??
                             HitTerms(text, ObjectSignals).FirstOrDefault();
        }
        AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.InspectionObject, explicitObject);

        var explicitTaskType = ResolveExplicitTaskType(text, semantic);
        AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.TaskType, explicitTaskType);

        if (ContainsAny(text, ["相机", "工业相机", "采集", "camera"]))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.ImageSource, "station_camera");
        }
        else if (ContainsAny(text, ["图片", "图像文件", "照片", "样张", "文件夹", "image", "photo", "file"]))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.ImageSource, "file_sample");
        }
        else if (ContainsAny(text, ["视频", "video"]))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.ImageSource, "video_stream");
        }

        if (HasAcceptanceCriteriaText(text))
        {
            var acceptance = VisionAgentPlanFieldPolicy.FormatAcceptanceCriteria(
                semantic?.OkCondition,
                semantic?.NgCondition);
            AddExplicitAnswer(
                answers,
                VisionAgentPlanAnswerFields.AcceptanceCriteria,
                string.IsNullOrWhiteSpace(acceptance) ? BoundedText(text) : acceptance);
        }

        var outputTarget = FirstRegexValue(text,
            @"(?:输出|返回)\s*(?<value>[^，。；;,.!?！？]+)",
            @"(?:写入|发送到|保存到)\s*(?<value>[^，。；;,.!?！？]+)");
        AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.OutputTarget, outputTarget);

        if (ContainsLiteralEvidence(text, semantic?.TargetAttribute))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.TargetAttribute, semantic!.TargetAttribute);
        }
        if (ContainsLiteralEvidence(text, semantic?.DefectType))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.DefectType, semantic!.DefectType);
        }
        if (ContainsLiteralEvidence(text, semantic?.MeasurementTarget))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.MeasurementTarget, semantic!.MeasurementTarget);
        }

        if (ContainsAny(text, ["深度学习", "分类模型", "检测模型", "AI 模型", "neural", "deep learning"]))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.AlgorithmStrategy, "model_strategy");
        }
        else if (ContainsAny(text, ["传统算法", "规则算法", "几何算法", "阈值规则", "traditional rule", "rule based"]))
        {
            AddExplicitAnswer(answers, VisionAgentPlanAnswerFields.AlgorithmStrategy, "traditional_rule");
        }

        return answers.Values.ToList();
    }

    private static string ResolveExplicitTaskType(
        string text,
        VisionAgentSemanticExtractionResult? semantic)
    {
        if (ContainsAny(text, ["DataMatrix", "二维码", "条码", "读码", "扫码", "barcode", "QR code"]))
        {
            return AiVisionTaskTypes.CodeRecognition;
        }
        if (Regex.IsMatch(text, @"(?:输出|识别|判别)[^，。；;,.!?！？]{0,24}(?:类型|类别|品类)", RegexOptions.IgnoreCase) ||
            ContainsAny(text, ["分类", "classification", "classify"]))
        {
            return AiVisionTaskTypes.Classification;
        }
        if (Regex.IsMatch(text, @"识别内容\s*(?:是|为|:|：)", RegexOptions.IgnoreCase))
        {
            return AiVisionTaskTypes.Classification;
        }
        if (Regex.IsMatch(text, @"检测\s*[^，。；;,.!?！？]+?上的[^，。；;,.!?！？]+", RegexOptions.IgnoreCase))
        {
            return AiVisionTaskTypes.PresenceAbsence;
        }
        if (semantic != null &&
            NormalizeSemanticTaskType(semantic.TaskType) == AiVisionTaskTypes.AttributeClassification &&
            ContainsAnyLiteralEvidence(text, semantic.TargetAttribute))
        {
            return AiVisionTaskTypes.AttributeClassification;
        }
        if (ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.GeometryMeasurement]))
        {
            return AiVisionTaskTypes.GeometryMeasurement;
        }
        if (ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.WireSequence]))
        {
            return AiVisionTaskTypes.WireSequence;
        }
        if (ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.PresenceAbsence]))
        {
            return AiVisionTaskTypes.PresenceAbsence;
        }
        if (ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.TemplateLocation]))
        {
            return AiVisionTaskTypes.TemplateLocation;
        }
        if (ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.SurfaceOrPoseDefect]))
        {
            return AiVisionTaskTypes.SurfaceOrPoseDefect;
        }

        var semanticTaskType = NormalizeSemanticTaskType(semantic?.TaskType);
        return HasExplicitTaskEvidence(text, semanticTaskType) ? semanticTaskType : string.Empty;
    }

    private static bool HasExplicitTaskEvidence(string text, string taskType)
    {
        return taskType switch
        {
            AiVisionTaskTypes.CodeRecognition => ContainsAny(text, ["DataMatrix", "二维码", "条码", "读码", "扫码", "code", "ocr"]),
            AiVisionTaskTypes.Classification or AiVisionTaskTypes.AttributeClassification =>
                ContainsAny(text, ["分类", "类型", "类别", "品类", "classification", "classify"]),
            AiVisionTaskTypes.GeometryMeasurement => ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.GeometryMeasurement]),
            AiVisionTaskTypes.WireSequence => ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.WireSequence]),
            AiVisionTaskTypes.PresenceAbsence => ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.PresenceAbsence]),
            AiVisionTaskTypes.TemplateLocation => ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.TemplateLocation]),
            AiVisionTaskTypes.SurfaceDefect or AiVisionTaskTypes.SurfaceOrPoseDefect =>
                ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.SurfaceOrPoseDefect]),
            AiVisionTaskTypes.PlcOutput => ContainsAny(text, TaskSignalTerms[AiVisionTaskTypes.PlcOutput]),
            _ => false
        };
    }

    private static bool RequiresAlgorithmStrategyDecision(string taskType)
    {
        return taskType is AiVisionTaskTypes.Classification or
            AiVisionTaskTypes.AttributeClassification or
            AiVisionTaskTypes.SurfaceDefect or
            AiVisionTaskTypes.SurfaceOrPoseDefect;
    }

    private static bool RequiresOutputContract(string taskType)
    {
        return taskType is AiVisionTaskTypes.CodeRecognition or
            AiVisionTaskTypes.Classification or
            AiVisionTaskTypes.AttributeClassification or
            AiVisionTaskTypes.PlcOutput;
    }

    private static void AddExplicitAnswer(
        IDictionary<string, VisionAgentPlanAnswer> answers,
        string field,
        string? value)
    {
        var normalizedField = VisionAgentPlanFieldPolicy.NormalizeField(field);
        var normalizedValue = BoundedText(value);
        if (string.IsNullOrWhiteSpace(normalizedField) ||
            string.IsNullOrWhiteSpace(normalizedValue) ||
            VisionAgentPlanFieldPolicy.IsPlaceholderValue(normalizedValue))
        {
            return;
        }

        answers[normalizedField] = new VisionAgentPlanAnswer
        {
            Field = normalizedField,
            Value = normalizedValue,
            Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
        };
    }

    private static string FirstRegexValue(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            var value = CleanSlot(match.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool ContainsLiteralEvidence(string text, string? value)
    {
        var evidence = Clean(value);
        return !string.IsNullOrWhiteSpace(evidence) &&
               text.Contains(evidence, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAnyLiteralEvidence(string text, string? value)
    {
        return Clean(value)
            .Split(['/', '／', '|', '、', ',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.Length >= 2 && text.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static string BoundedText(string? value)
    {
        var text = Clean(value)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return text.Length > 256 ? text[..256] : text;
    }

    private static AiRequirementMaturityResult Result(
        string maturity,
        string taskType,
        bool canPlan,
        bool canBuild,
        IReadOnlyList<string> objectSignals,
        IReadOnlyList<string> taskSignals,
        IReadOnlyList<string> missingFields,
        IReadOnlyList<string> blockingReasons,
        string publicReason)
    {
        return new AiRequirementMaturityResult
        {
            Maturity = maturity,
            TaskType = taskType,
            CanPlan = canPlan,
            CanBuild = canBuild,
            ObjectSignals = objectSignals.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            TaskSignals = taskSignals.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            MissingFields = missingFields.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            BlockingReasons = blockingReasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            PublicReason = publicReason
        };
    }

    private static List<string> BuildNonBlockingMissingFields(string text)
    {
        var missing = new List<string>();
        if (!ContainsAny(text, ImageSourceSignals))
        {
            missing.Add("image_source");
        }

        if (!HasAcceptanceCriteriaText(text))
        {
            missing.Add("acceptance_criteria");
        }

        return CanonicalMissingFields(missing);
    }

    private static List<string> BuildMissingFields(
        bool hasObject,
        bool hasTaskType,
        bool hasImageSource,
        bool hasAcceptance)
    {
        var missing = new List<string>();
        if (!hasObject)
        {
            missing.Add("inspection_object");
        }

        if (!hasTaskType)
        {
            missing.Add("task_type");
        }

        if (!hasImageSource)
        {
            missing.Add("image_source");
        }

        if (!hasAcceptance)
        {
            missing.Add("acceptance_criteria");
        }

        return CanonicalMissingFields(missing);
    }

    private static bool HasAcceptanceCriteriaText(string text)
    {
        return ContainsAny(text, ["OK", "NG", "判定", "标准", "阈值", "公差", "criteria", "tolerance"]);
    }

    private static List<string> CanonicalMissingFields(IEnumerable<string> fields)
    {
        return fields
            .Select(VisionAgentPlanFieldPolicy.NormalizeField)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool CanBuildFromMissingFields(
        IReadOnlyList<string> missingFields,
        string taskType)
    {
        return !missingFields.Any(field =>
            VisionAgentPlanFieldPolicy.IsStrictBlocking(field, taskType, null));
    }

    private static VisionAgentRequirementSemanticSlots ExtractSemanticSlots(string text)
    {
        var objects = new List<string>();
        var tasks = new List<string>();
        string? taskTypeHint = null;

        foreach (Match match in Regex.Matches(text, @"(?:检测目标|检测对象)\s*(?:是|为|:|：)?\s*(?<value>[^，。；;,.!?！？]+)"))
        {
            AddSlot(objects, match.Groups["value"].Value);
        }

        foreach (Match match in Regex.Matches(text, @"识别内容\s*(?:是|为|:|：)?\s*(?<value>[^，。；;,.!?！？]+)"))
        {
            AddSlot(tasks, match.Groups["value"].Value);
            taskTypeHint ??= AiVisionTaskTypes.Classification;
        }

        foreach (Match match in Regex.Matches(text, @"检测\s*(?<object>[^，。；;,.!?！？\s]{1,24}?)\s*(?<task>缺陷|异常|有无|漏装|缺失|少装|测量|尺寸|分类|OCR|字符|条码|二维码)"))
        {
            var obj = match.Groups["object"].Value.Trim();
            if (!IsGenericObjectCandidate(obj))
            {
                AddSlot(objects, obj);
            }
            var task = match.Groups["task"].Value;
            AddSlot(tasks, task);
            taskTypeHint ??= TaskTypeFromExplicitTask(task);
        }

        foreach (Match match in Regex.Matches(text, @"(?<object>[^检测识别视觉流程问题，。；;,.!?！？\s]{1,24}?)\s*检测"))
        {
            var obj = match.Groups["object"].Value.Trim();
            if (!IsGenericObjectCandidate(obj))
            {
                AddSlot(objects, obj);
            }
        }

        foreach (Match match in Regex.Matches(text, @"检测\s*(?<object>[^，。；;,.!?！？]+?)上的(?<target>[^，。；;,.!?！？]+)"))
        {
            AddSlot(objects, match.Groups["object"].Value);
            AddSlot(tasks, match.Groups["target"].Value);
            taskTypeHint ??= AiVisionTaskTypes.PresenceAbsence;
        }

        foreach (Match match in Regex.Matches(text, @"判断\s*(?<object>[^，。；;,.!?！？]+?)是否存在"))
        {
            AddSlot(objects, match.Groups["object"].Value);
            AddSlot(tasks, "是否存在");
            taskTypeHint ??= AiVisionTaskTypes.PresenceAbsence;
        }

        foreach (Match match in Regex.Matches(text, @"判断\s*(?<object>[^，。；;,.!?！？\s]+?)\s*是否\s*(?<target>[^，。；;,.!?！？\s]+)"))
        {
            AddSlot(objects, match.Groups["object"].Value);
            AddSlot(tasks, match.Groups["target"].Value);
            taskTypeHint ??= AiVisionTaskTypes.AttributeClassification;
        }

        return new VisionAgentRequirementSemanticSlots(
            objects.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
            tasks.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
            taskTypeHint);
    }

    private static void AddSlot(List<string> values, string value)
    {
        var cleaned = CleanSlot(value);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            values.Add(cleaned);
        }
    }

    private static string CleanSlot(string value)
    {
        var cleaned = Clean(value);
        cleaned = Regex.Replace(cleaned, @"^(一个|一条|一种|某个|这个|那个|的)+", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(这个|那个|方案|流程)$", string.Empty);
        return cleaned.Trim();
    }

    private static bool IsGenericObjectCandidate(string value)
    {
        var cleaned = Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ||
               ContainsAny(cleaned, ["进行", "做个", "做一个", "帮我", "问题", "流程", "视觉", "检测", "识别", "外观"]);
    }

    private static string TaskTypeFromExplicitTask(string value)
    {
        var task = Clean(value);
        if (ContainsAny(task, ["缺陷", "异常"]))
        {
            return AiVisionTaskTypes.SurfaceOrPoseDefect;
        }

        if (ContainsAny(task, ["有无", "漏装", "缺失", "少装"]))
        {
            return AiVisionTaskTypes.PresenceAbsence;
        }

        if (ContainsAny(task, ["测量", "尺寸"]))
        {
            return AiVisionTaskTypes.GeometryMeasurement;
        }

        if (ContainsAny(task, ["OCR", "字符", "条码", "二维码"]))
        {
            return AiVisionTaskTypes.CodeRecognition;
        }

        if (ContainsAny(task, ["分类"]))
        {
            return AiVisionTaskTypes.Classification;
        }

        return AiVisionTaskTypes.Unknown;
    }

    private static List<string> HitTaskTerms(string text, out string taskType)
    {
        taskType = AiVisionTaskTypes.Unknown;
        foreach (var pair in TaskSignalTerms)
        {
            var hits = HitTerms(text, pair.Value);
            if (hits.Count == 0)
            {
                continue;
            }

            taskType = pair.Key;
            return hits;
        }

        return [];
    }

    private static List<string> HitTerms(string text, IEnumerable<string> terms)
    {
        return terms
            .Where(term => !string.IsNullOrWhiteSpace(term) &&
                           text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
    }

    private static bool LooksLikeChatOrHelp(string text)
    {
        var normalized = Regex.Replace(text, @"[\s!?！？。。，,\.]+", string.Empty);
        return normalized is "hi" or "hello" or "hey" or "你好" or "您好" or "在吗" or "在不在" ||
               ContainsAny(text, HelpSignals);
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
    {
        return HitTerms(text, terms).Count > 0;
    }

    private static bool ShouldUseStructuredSemantic(VisionAgentSemanticExtractionResult? semantic)
    {
        return semantic != null &&
               semantic.IsVisionRequest &&
               string.IsNullOrWhiteSpace(semantic.FailureCode);
    }

    private static string NormalizeSemanticTaskType(string? taskType)
    {
        return Clean(taskType).ToLowerInvariant() switch
        {
            AiVisionTaskTypes.SurfaceDefect => AiVisionTaskTypes.SurfaceDefect,
            AiVisionTaskTypes.SurfaceOrPoseDefect => AiVisionTaskTypes.SurfaceDefect,
            AiVisionTaskTypes.GeometryMeasurement => AiVisionTaskTypes.GeometryMeasurement,
            "measurement" => AiVisionTaskTypes.GeometryMeasurement,
            AiVisionTaskTypes.WireSequence => AiVisionTaskTypes.WireSequence,
            AiVisionTaskTypes.CodeRecognition => AiVisionTaskTypes.CodeRecognition,
            AiVisionTaskTypes.BarcodeQr => AiVisionTaskTypes.CodeRecognition,
            "ocr" => AiVisionTaskTypes.CodeRecognition,
            AiVisionTaskTypes.PresenceAbsence => AiVisionTaskTypes.PresenceAbsence,
            AiVisionTaskTypes.Classification => AiVisionTaskTypes.Classification,
            AiVisionTaskTypes.AttributeClassification => AiVisionTaskTypes.AttributeClassification,
            AiVisionTaskTypes.TemplateLocation => AiVisionTaskTypes.TemplateLocation,
            AiVisionTaskTypes.PlcOutput => AiVisionTaskTypes.PlcOutput,
            AiVisionTaskTypes.AbstractGoal => AiVisionTaskTypes.AbstractGoal,
            _ => AiVisionTaskTypes.Unknown
        };
    }

    private static List<string> BuildSemanticObjectSignals(VisionAgentSemanticExtractionResult semantic)
    {
        return semantic.ObjectSignals
            .Concat(Single(semantic.InspectionObject))
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static List<string> BuildSemanticTaskSignals(
        VisionAgentSemanticExtractionResult semantic,
        string taskType)
    {
        return semantic.TaskSignals
            .Concat(Single(taskType))
            .Concat(Single(semantic.TargetAttribute))
            .Concat(Single(semantic.DefectType))
            .Concat(Single(semantic.MeasurementTarget))
            .Concat(Single(semantic.OkCondition))
            .Concat(Single(semantic.NgCondition))
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IEnumerable<string> Single(string? value)
    {
        var text = Clean(value);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }

    private static string NormalizeText(string? description, string? additionalContext)
    {
        return string.Join(' ', new[] { description, additionalContext }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()))
            .Trim();
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool IsReliablePlannable(
        VisionAgentRequirementMaturityRequest request,
        VisionAgentSemanticExtractionResult? semantic,
        bool hasObject,
        bool hasTaskType)
    {
        if (semantic != null && !string.IsNullOrWhiteSpace(semantic.InspectionObject))
        {
            return true;
        }

        if (semantic != null &&
            !string.IsNullOrWhiteSpace(semantic.TaskType) &&
            !string.Equals(semantic.TaskType, AiVisionTaskTypes.Unknown, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(semantic.TaskType, AiVisionTaskTypes.AbstractGoal, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (hasObject || hasTaskType)
        {
            return true;
        }

        if (request.TemplateSelection != null)
        {
            return true;
        }

        if (request.HasPendingPlan)
        {
            return true;
        }

        return false;
    }
}
