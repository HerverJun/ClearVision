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
}

public static class VisionAgentRequirementMaturityGate
{
    private static readonly string[] BusinessSignals =
    [
        "视觉", "检测", "检验", "测量", "识别", "流程", "工程", "方案", "算法", "模型", "相机", "图像",
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
            "定位", "对位", "找正", "模板", "匹配", "位姿", "locate", "position", "align", "template", "matching", "pose"
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
        "圆孔", "圆形孔位", "孔位", "铜孔", "产品", "零件", "遥控器", "按键", "面板", "瓶盖", "螺丝", "pin", "hole",
        "terminal", "connector", "label", "package", "carton", "part", "product", "button", "wire", "harness", "metal", "surface"
    ];

    private static readonly string[] ImageSourceSignals =
    [
        "相机", "图片", "图像", "照片", "视频", "文件", "采集", "camera", "image", "photo", "video", "file"
    ];

    public static AiRequirementMaturityResult Evaluate(VisionAgentRequirementMaturityRequest request)
    {
        var text = NormalizeText(request.Description, request.AdditionalContext);
        var businessHits = HitTerms(text, BusinessSignals);
        var taskHits = HitTaskTerms(text, out var taskType);
        var objectHits = HitTerms(text, ObjectSignals);
        var missingFields = new List<string>();
        var blockingReasons = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result(
                AiRequirementMaturity.Ambiguous,
                AiVisionTaskTypes.Unknown,
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

        if (hasAbstractGoal && (!hasTaskType || !hasObject))
        {
            missingFields.AddRange(["inspection_object", "task_type", "image_source", "acceptance_criteria", "output_target"]);
            blockingReasons.AddRange(["abstract_goal_needs_decomposition", "task_type_missing", "inspection_object_missing"]);
            return Result(
                AiRequirementMaturity.AbstractGoal,
                AiVisionTaskTypes.AbstractGoal,
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
                canBuild: false,
                objectHits,
                taskHits,
                [],
                [],
                "输入未形成视觉工程需求。");
        }

        if (!hasTaskType || !hasObject)
        {
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

            missingFields.Add("acceptance_criteria");
            return Result(
                AiRequirementMaturity.Ambiguous,
                hasTaskType ? taskType : AiVisionTaskTypes.Unknown,
                canBuild: false,
                objectHits,
                taskHits,
                missingFields,
                blockingReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                "需求仍缺少检测对象或任务类型，暂不能构建。");
        }

        return Result(
            AiRequirementMaturity.Actionable,
            taskType,
            canBuild: true,
            objectHits,
            taskHits,
            BuildNonBlockingMissingFields(text),
            [],
            "需求已明确到可规划视觉流程。");
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
            AiVisionTaskTypes.BarcodeQr => "code_recognition",
            AiVisionTaskTypes.GeometryMeasurement => "measurement",
            AiVisionTaskTypes.TemplateLocation => "template_location",
            AiVisionTaskTypes.PlcOutput => "plc_output",
            AiVisionTaskTypes.PresenceAbsence => "presence_absence",
            AiVisionTaskTypes.Classification => "classification",
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
            _ => VisionAgentIntentRouterService.IntentAmbiguousVisionRequirement
        };
    }

    private static AiRequirementMaturityResult Result(
        string maturity,
        string taskType,
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

        if (!ContainsAny(text, ["OK", "NG", "判定", "标准", "阈值", "公差", "输出", "report", "tolerance", "criteria"]))
        {
            missing.Add("acceptance_criteria");
        }

        return missing;
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
}
