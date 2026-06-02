// FlowTemplateService.cs
// 流程模板服务
// 负责流程模板加载、查询与模板化生成支持
// 作者：蘅芜君
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Infrastructure.Services;

namespace Acme.Product.Infrastructure.AI;

public interface IFlowTemplateService
{
    Task<IReadOnlyList<FlowTemplate>> GetTemplatesAsync(
        string? industry = null,
        CancellationToken cancellationToken = default);

    Task<FlowTemplate?> GetTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<FlowTemplate> SaveTemplateAsync(
        FlowTemplate template,
        CancellationToken cancellationToken = default);

    Task<FlowTemplate> CreateTemplateAsync(
        FlowTemplate template,
        CancellationToken cancellationToken = default);

    Task<FlowTemplate?> UpdateTemplateAsync(
        Guid id,
        FlowTemplate template,
        CancellationToken cancellationToken = default);
}

public class FlowTemplateService : IFlowTemplateService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private const string AirConditioningIndustry = "空调制造";
    private const string TemplateDefaultRegionExtent = "999999";
    private const string WireSequenceScenarioKey = "wire-sequence-terminal";
    private const string WireSequenceVideoStreamScenarioKey = "wire-sequence-terminal-video-stream";
    private const string WireSequenceTemplateName = "端子线序检测";
    private const string WireSequenceVideoStreamTemplateName = "端子线序检测-视频流版";
    private const string WireSequenceIndustry = "线束装配";
    private static readonly IReadOnlyDictionary<string, string> _deprecatedBuiltInTemplates =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["传统缺陷检测"] = "3C电子",
            ["AI缺陷检测"] = "半导体",
            ["尺寸间距测量"] = "汽车零部件",
            ["条码读取写PLC"] = "食品包装",
            ["OCR文本追溯"] = "食品包装",
            ["环形件缺陷检测"] = "轴承行业",
            ["多工位循环检测"] = "通用制造",
            ["检测结果分拣"] = "通用制造"
        };

    private readonly string _templateFilePath;
    private readonly object _syncRoot = new();

    public FlowTemplateService(string? storageRootPath = null)
    {
        var rootPath = string.IsNullOrWhiteSpace(storageRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearVision")
            : storageRootPath;

        var templateDirectory = Path.Combine(rootPath, "templates");
        _templateFilePath = Path.Combine(templateDirectory, "flow_templates.json");
        EnsureTemplateStore();
    }

    public Task<IReadOnlyList<FlowTemplate>> GetTemplatesAsync(
        string? industry = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var templates = LoadTemplates();
        if (string.IsNullOrWhiteSpace(industry))
            return Task.FromResult<IReadOnlyList<FlowTemplate>>(templates);

        var filtered = templates
            .Where(template => template.Industry.Equals(industry, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<FlowTemplate>>(filtered);
    }

    public Task<FlowTemplate?> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = LoadTemplates().FirstOrDefault(item => item.Id == id);
        return Task.FromResult(template);
    }

    public Task<FlowTemplate> SaveTemplateAsync(FlowTemplate template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        lock (_syncRoot)
        {
            var templates = LoadTemplates();
            var existing = templates.FirstOrDefault(item => item.Id == template.Id);
            if (existing == null)
            {
                if (template.Id == Guid.Empty)
                    template.Id = Guid.NewGuid();

                template.CreatedAt = template.CreatedAt == default ? DateTime.UtcNow : template.CreatedAt;
                templates.Add(template);
            }
            else
            {
                existing.Name = template.Name;
                existing.Description = template.Description;
                existing.Industry = template.Industry;
                existing.Tags = template.Tags;
                existing.FlowJson = template.FlowJson;
            }

            SaveTemplates(templates);
        }

        return Task.FromResult(template);
    }

    public Task<FlowTemplate> CreateTemplateAsync(FlowTemplate template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        lock (_syncRoot)
        {
            var templates = LoadTemplates();
            template.Id = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id;
            template.CreatedAt = template.CreatedAt == default ? DateTime.UtcNow : template.CreatedAt;
            templates.Add(template);
            SaveTemplates(templates);
        }

        return Task.FromResult(template);
    }

    public Task<FlowTemplate?> UpdateTemplateAsync(Guid id, FlowTemplate template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        lock (_syncRoot)
        {
            var templates = LoadTemplates();
            var existing = templates.FirstOrDefault(item => item.Id == id);
            if (existing == null)
                return Task.FromResult<FlowTemplate?>(null);

            existing.Name = template.Name;
            existing.Description = template.Description;
            existing.Industry = template.Industry;
            existing.Tags = template.Tags;
            existing.FlowJson = template.FlowJson;

            SaveTemplates(templates);
            return Task.FromResult<FlowTemplate?>(existing);
        }
    }

    private void EnsureTemplateStore()
    {
        var directory = Path.GetDirectoryName(_templateFilePath)
                        ?? throw new InvalidOperationException("Template directory path is invalid.");
        Directory.CreateDirectory(directory);

        if (File.Exists(_templateFilePath))
            return;

        var defaults = CreateBuiltInTemplates();
        SaveTemplates(defaults);
    }

    private List<FlowTemplate> LoadTemplates()
    {
        lock (_syncRoot)
        {
            EnsureTemplateStore();

            try
            {
                var json = File.ReadAllText(_templateFilePath);
                var templates = JsonSerializer.Deserialize<List<FlowTemplate>>(json, _jsonOptions);

                if (templates == null || templates.Count == 0)
                {
                    BackupCorruptedTemplateFile();
                    templates = CreateBuiltInTemplates();
                    SaveTemplates(templates);
                }

                var changed = RemoveDeprecatedBuiltInTemplates(templates);
                if (MergeBuiltInTemplates(templates))
                {
                    changed = true;
                }

                if (changed)
                {
                    SaveTemplates(templates);
                }

                return templates;
            }
            catch
            {
                BackupCorruptedTemplateFile();
                var templates = CreateBuiltInTemplates();
                SaveTemplates(templates);
                return templates;
            }
        }
    }

    private static bool RemoveDeprecatedBuiltInTemplates(List<FlowTemplate> templates)
    {
        return templates.RemoveAll(IsDeprecatedBuiltInTemplate) > 0;
    }

    private static bool MergeBuiltInTemplates(List<FlowTemplate> templates)
    {
        var changed = false;
        foreach (var builtInTemplate in CreateBuiltInTemplates())
        {
            var existing = templates.FirstOrDefault(item => IsSameTemplateDefinition(item, builtInTemplate));
            if (existing == null)
            {
                templates.Add(builtInTemplate);
                changed = true;
                continue;
            }

            if (!ShouldUpgradeBuiltInTemplate(existing, builtInTemplate))
                continue;

            ApplyBuiltInTemplate(existing, builtInTemplate);
            changed = true;
        }

        return changed;
    }

    private static bool IsSameTemplateDefinition(FlowTemplate existing, FlowTemplate candidate)
    {
        if (!string.IsNullOrWhiteSpace(existing.ScenarioKey) &&
            !string.IsNullOrWhiteSpace(candidate.ScenarioKey) &&
            string.Equals(existing.ScenarioKey, candidate.ScenarioKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeprecatedBuiltInTemplate(FlowTemplate template)
    {
        if (IsWireSequenceTemplate(template))
        {
            return false;
        }

        return _deprecatedBuiltInTemplates.TryGetValue(template.Name, out var expectedIndustry) &&
            string.Equals(template.Industry, expectedIndustry, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWireSequenceTemplate(FlowTemplate template)
    {
        if (string.Equals(template.ScenarioKey, WireSequenceScenarioKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(template.Name, WireSequenceTemplateName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(template.Industry, WireSequenceIndustry, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUpgradeBuiltInTemplate(FlowTemplate existing, FlowTemplate candidate)
    {
        if (string.IsNullOrWhiteSpace(existing.TemplateVersion))
            return !string.IsNullOrWhiteSpace(candidate.TemplateVersion);

        var comparison = CompareTemplateVersions(candidate.TemplateVersion, existing.TemplateVersion);
        if (comparison > 0)
            return true;

        return comparison == 0 &&
            UsesLegacyPlatformNmsPolicy(existing) &&
            !UsesLegacyPlatformNmsPolicy(candidate);
    }

    private static bool UsesLegacyPlatformNmsPolicy(FlowTemplate template)
    {
        var flowJson = template.FlowJson ?? string.Empty;
        return flowJson.Contains("\"BoxNms\"", StringComparison.OrdinalIgnoreCase) ||
            flowJson.Contains("\"EnableInternalNms\": \"false\"", StringComparison.OrdinalIgnoreCase) ||
            flowJson.Contains("\"EnableInternalNms\":\"false\"", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareTemplateVersions(string? left, string? right)
    {
        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyBuiltInTemplate(FlowTemplate target, FlowTemplate source)
    {
        target.Name = source.Name;
        target.Description = source.Description;
        target.Industry = source.Industry;
        target.Tags = source.Tags;
        target.FlowJson = source.FlowJson;
        target.TemplateVersion = source.TemplateVersion;
        target.ScenarioKey = source.ScenarioKey;
        target.ScenarioPackage = source.ScenarioPackage == null
            ? null
            : new ScenarioPackageBinding
            {
                PackageKey = source.ScenarioPackage.PackageKey,
                PackageVersion = source.ScenarioPackage.PackageVersion,
                AssetVersionIds = source.ScenarioPackage.AssetVersionIds.ToList(),
                RequiredResources = source.ScenarioPackage.RequiredResources.ToList()
            };
    }

    private void SaveTemplates(List<FlowTemplate> templates)
    {
        var json = JsonSerializer.Serialize(templates, _jsonOptions);
        ValidateTemplatePayload(json);

        var directory = Path.GetDirectoryName(_templateFilePath)
                        ?? throw new InvalidOperationException("Template directory path is invalid.");
        Directory.CreateDirectory(directory);

        var tempFilePath = Path.Combine(directory, $"flow_templates.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $"flow_templates.swapbackup.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(tempFilePath, json);
            ValidateTemplatePayload(File.ReadAllText(tempFilePath));

            if (File.Exists(_templateFilePath))
            {
                File.Replace(tempFilePath, _templateFilePath, backupPath, true);
                TryDeleteFile(backupPath);
            }
            else
            {
                File.Move(tempFilePath, _templateFilePath);
            }
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    private static void ValidateTemplatePayload(string json)
    {
        var parsed = JsonSerializer.Deserialize<List<FlowTemplate>>(json, _jsonOptions);
        if (parsed == null)
            throw new InvalidDataException("Serialized template payload is invalid.");
    }

    private void BackupCorruptedTemplateFile()
    {
        if (!File.Exists(_templateFilePath))
            return;

        var directory = Path.GetDirectoryName(_templateFilePath)
                        ?? throw new InvalidOperationException("Template directory path is invalid.");
        Directory.CreateDirectory(directory);

        var backupPath = Path.Combine(directory, $"flow_templates.corrupted.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
        File.Copy(_templateFilePath, backupPath, overwrite: false);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // 忽略清理阶段异常，避免掩盖主流程写入结果
        }
    }

    private static List<FlowTemplate> CreateBuiltInTemplates()
    {
        return new List<FlowTemplate>
        {
            new FlowTemplate
            {
                Id = Guid.NewGuid(),
                Name = WireSequenceTemplateName,
                Description = "端子线序检测模板，ONNX 模型内置候选框抑制后直接输出线根检测框，再做顺序判定。",
                Industry = WireSequenceIndustry,
                Tags = ["线序", "YOLO", "端子", "ONNX-NMS"],
                TemplateVersion = "1.6.0",
                ScenarioKey = WireSequenceScenarioKey,
                ScenarioPackage = new ScenarioPackageBinding
                {
                    PackageKey = WireSequenceScenarioKey,
                    PackageVersion = "1.6.0",
                    AssetVersionIds =
                    [
                        "template:terminal-wire-sequence-template@1.6.0",
                        "model:wire-seq-yolo-nms@1.3.0",
                        "rule:wire-sequence-rule@1.6.0",
                        "label:wire-label-set@1.1.0"
                    ],
                    RequiredResources =
                    [
                        "DeepLearning.ModelPath"
                    ]
                },
                FlowJson = JsonSerializer.Serialize(new
                {
                    explanation = "适用于线束装配工位的端子线序判定。ONNX 模型已内置候选框抑制，平台侧只保留图像采集、深度学习检测、线序判定和结果输出。",
                    expectedSequence = new[] { "Wire_Black", "Wire_Blue" },
                    expectedDetectionCount = 2,
                    requiredResources = new[] { "DeepLearning.ModelPath" },
                    tunableParameters = new[]
                    {
                        "DeepLearning.Confidence"
                    },
                    operators = new object[]
                    {
                        Node("op_1", "ImageAcquisition", "图像采集", new Dictionary<string, string> { ["sourceType"] = "camera" }),
                        Node("op_2", "DeepLearning", "线根检测", new Dictionary<string, string>
                        {
                            ["ModelPath"] = "",
                            ["LabelsPath"] = "",
                            ["Confidence"] = "0.05",
                            ["InputSize"] = "640",
                            ["TargetClasses"] = "Wire_Black,Wire_Blue",
                            ["EnableInternalNms"] = "true",
                            ["OutputFormat"] = "EndToEndNms",
                            ["DetectionMode"] = "Object"
                        }),
                        Node("op_3", "DetectionSequenceJudge", "顺序判定", new Dictionary<string, string>
                        {
                            ["ExpectedLabels"] = "Wire_Black,Wire_Blue",
                            ["SortBy"] = "CenterY",
                            ["Direction"] = "TopToBottom",
                            ["ExpectedCount"] = "2",
                            ["MinConfidence"] = "0.0"
                        }),
                        Node("op_4", "ResultOutput", "结果输出", new Dictionary<string, string>
                        {
                            ["Format"] = "JSON",
                            ["SaveToFile"] = "false"
                        })
                    },
                    connections = new object[]
                    {
                        Link("op_1", "Image", "op_2", "Image"),
                        Link("op_2", "Objects", "op_3", "Detections"),
                        Link("op_2", "Image", "op_4", "Image"),
                        Link("op_2", "PostprocessDiagnostics", "op_4", "Data"),
                        Link("op_3", "Diagnostics", "op_4", "Result"),
                        Link("op_3", "Message", "op_4", "Text")
                    },
                    parametersNeedingReview = new Dictionary<string, List<string>>
                    {
                        ["op_2"] = ["ModelPath", "OutputFormat", "Confidence"],
                        ["op_3"] = ["ExpectedLabels", "ExpectedCount"]
                    }
                }, _jsonOptions),
                CreatedAt = DateTime.UtcNow
            },
            new FlowTemplate
            {
                Id = Guid.NewGuid(),
                Name = WireSequenceVideoStreamTemplateName,
                Description = "端子线序检测视频流模板，连续采集后先用帧变化触发判断到料，触发后进入内置 NMS 的 ONNX 检测和顺序判定。",
                Industry = WireSequenceIndustry,
                Tags = ["线序", "视频流", "连续采集", "帧变化触发", "端子", "ONNX-NMS"],
                TemplateVersion = "1.6.0",
                ScenarioKey = WireSequenceVideoStreamScenarioKey,
                ScenarioPackage = new ScenarioPackageBinding
                {
                    PackageKey = WireSequenceScenarioKey,
                    PackageVersion = "1.6.0",
                    AssetVersionIds =
                    [
                        "template:terminal-wire-sequence-video-stream-template@1.6.0",
                        "model:wire-seq-yolo-nms@1.3.0",
                        "rule:wire-sequence-rule@1.6.0",
                        "label:wire-label-set@1.1.0"
                    ],
                    RequiredResources =
                    [
                        "DeepLearning.ModelPath"
                    ]
                },
                FlowJson = JsonSerializer.Serialize(new
                {
                    explanation = "适用于没有光电或 PLC 触发信号的皮带线端子线序检测。相机连续采集，帧变化触发算子只负责到料判断；触发后进入内置 NMS 的 ONNX 检测和线序判定。",
                    expectedSequence = new[] { "Wire_Black", "Wire_Blue" },
                    expectedDetectionCount = 2,
                    requiredResources = new[] { "DeepLearning.ModelPath" },
                    tunableParameters = new[]
                    {
                        "FrameChangeTrigger.PixelThreshold",
                        "FrameChangeTrigger.MinChangeRatio",
                        "FrameChangeTrigger.MinChangePixels",
                        "FrameChangeTrigger.CooldownMs",
                        "FrameChangeTrigger.RoiX",
                        "FrameChangeTrigger.RoiY",
                        "FrameChangeTrigger.RoiW",
                        "FrameChangeTrigger.RoiH",
                        "DeepLearning.Confidence"
                    },
                    operators = new object[]
                    {
                        Node("op_1", "ImageAcquisition", "连续采集", new Dictionary<string, string>
                        {
                            ["SourceType"] = "Camera",
                            ["TriggerMode"] = "Continuous"
                        }),
                        Node("op_2", "FrameChangeTrigger", "到料触发", new Dictionary<string, string>
                        {
                            ["Enabled"] = "true",
                            ["ShortCircuitWhenNotTriggered"] = "true",
                            ["Profile"] = "line_fast_default",
                            ["PixelThreshold"] = "30",
                            ["MinChangeRatio"] = "0.02",
                            ["MinChangePixels"] = "500",
                            ["CooldownMs"] = "1200",
                            ["RoiX"] = "0",
                            ["RoiY"] = "0",
                            ["RoiW"] = "0",
                            ["RoiH"] = "0"
                        }),
                        Node("op_3", "DeepLearning", "线根检测", new Dictionary<string, string>
                        {
                            ["ModelPath"] = "",
                            ["LabelsPath"] = "",
                            ["Confidence"] = "0.05",
                            ["InputSize"] = "640",
                            ["TargetClasses"] = "Wire_Black,Wire_Blue",
                            ["EnableInternalNms"] = "true",
                            ["OutputFormat"] = "EndToEndNms",
                            ["DetectionMode"] = "Object"
                        }),
                        Node("op_4", "DetectionSequenceJudge", "顺序判定", new Dictionary<string, string>
                        {
                            ["ExpectedLabels"] = "Wire_Black,Wire_Blue",
                            ["SortBy"] = "CenterY",
                            ["Direction"] = "TopToBottom",
                            ["ExpectedCount"] = "2",
                            ["MinConfidence"] = "0.0"
                        }),
                        Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                        {
                            ["Format"] = "JSON",
                            ["SaveToFile"] = "false"
                        })
                    },
                    connections = new object[]
                    {
                        Link("op_1", "Image", "op_2", "Image"),
                        Link("op_2", "Image", "op_3", "Image"),
                        Link("op_3", "Objects", "op_4", "Detections"),
                        Link("op_3", "Image", "op_5", "Image"),
                        Link("op_3", "PostprocessDiagnostics", "op_5", "Data"),
                        Link("op_4", "Diagnostics", "op_5", "Result"),
                        Link("op_4", "Message", "op_5", "Text")
                    },
                    parametersNeedingReview = new Dictionary<string, List<string>>
                    {
                        ["op_1"] = ["CameraId", "TriggerMode"],
                        ["op_2"] = ["RoiX", "RoiY", "RoiW", "RoiH", "PixelThreshold", "MinChangeRatio", "MinChangePixels", "CooldownMs"],
                        ["op_3"] = ["ModelPath", "OutputFormat", "Confidence"],
                        ["op_4"] = ["ExpectedLabels", "ExpectedCount"]
                    }
                }, _jsonOptions),
                CreatedAt = DateTime.UtcNow
            },
            CreateClassicTemplateMatchingTemplate(),
            CreateGradientShapeMatchPositioningTemplate(),
            CreatePlanarFeatureMatchingTemplate(),
            CreateBlobDefectRegionAnalysisTemplate(),
            CreateCaliperWidthMeasurementTemplate(),
            CreateCircularHoleMeasurementTemplate(),
            CreateColorDeltaEInspectionTemplate(),
            CreateCodeTraceabilityTemplate(),
            CreateSurfaceReferenceDefectTemplate(),
            CreateSharpnessFocusGateTemplate(),
            CreateAiInspectionTemplate(
                name: "包装箱外观检测",
                scenarioKey: "carton-appearance-inspection",
                industry: "包装终检",
                description: "适合包装终检工位的包装箱外观缺陷检测，默认骨架为缩放 + 内置 NMS 的 ONNX AI 检测 + Region ROI 过滤 + OK/NG 判定；需人工确认模型、类别、ROI 与阈值。",
                explanation: "适用于包装终检工位的包装箱外观检测，默认检测箱体破损、压痕、脏污、封箱异常、标签异常等缺陷。",
                tags: ["包装箱", "外观", "AI", "YOLO"],
                targetClasses: "CartonDamage,CartonDent,CartonStain,SealAnomaly,LabelAnomaly",
                detectionMode: "Defect",
                detectionPort: "Defects",
                judgmentCondition: "Equal",
                judgmentExpectValue: "0",
                includeTargetClassesInReview: true),
            CreateAiInspectionTemplate(
                name: "空调内机外观检测",
                scenarioKey: "aircon-indoor-appearance-inspection",
                industry: AirConditioningIndustry,
                description: "适合总装/终检工位的空调内机外观检测，默认骨架为缩放 + 内置 NMS 的 ONNX AI 检测 + Region ROI 过滤 + OK/NG 判定；需人工确认模型、类别、ROI 与阈值。",
                explanation: "适用于总装或终检工位的空调内机外观检测，默认检测面板划伤、面板缝隙、污渍、磕碰等缺陷。",
                tags: ["内机", "外观", "AI", "YOLO"],
                targetClasses: "PanelScratch,PanelGap,Stain,Damage",
                detectionMode: "Defect",
                detectionPort: "Defects",
                judgmentCondition: "Equal",
                judgmentExpectValue: "0",
                includeTargetClassesInReview: true),
            CreateAiInspectionTemplate(
                name: "空调外机外观检测",
                scenarioKey: "aircon-outdoor-appearance-inspection",
                industry: AirConditioningIndustry,
                description: "适合总装/终检工位的空调外机外观检测，默认骨架为缩放 + 内置 NMS 的 ONNX AI 检测 + Region ROI 过滤 + OK/NG 判定；需人工确认模型、类别、ROI 与阈值。",
                explanation: "适用于总装或终检工位的空调外机外观检测，默认检测翅片变形、护网破损、凹陷、缺件等缺陷。",
                tags: ["外机", "外观", "AI", "YOLO"],
                targetClasses: "FinDeform,NetDamage,Dent,MissingPart",
                detectionMode: "Defect",
                detectionPort: "Defects",
                judgmentCondition: "Equal",
                judgmentExpectValue: "0",
                includeTargetClassesInReview: true),
            CreateAiInspectionTemplate(
                name: "遥控器漏装检测",
                scenarioKey: "remote-controller-missing-inspection",
                industry: AirConditioningIndustry,
                description: "适合包装位/附件位的遥控器漏装检测，默认骨架为缩放 + 内置 NMS 的 ONNX AI 目标检测 + Region ROI 过滤 + 有无判定；需人工确认模型、ROI 与阈值。",
                explanation: "适用于包装位或附件位的遥控器漏装检测，默认判断附件区域中是否存在遥控器目标。",
                tags: ["遥控器", "漏装", "附件", "AI"],
                targetClasses: "RemoteController",
                detectionMode: "Object",
                detectionPort: "Objects",
                judgmentCondition: "GreaterOrEqual",
                judgmentExpectValue: "1",
                includeTargetClassesInReview: false),
            CreateCopperHoleSpacingTemplate()
        };
    }

    private static ScenarioPackageBinding CreateRuleOnlyPackage(
        string scenarioKey,
        string templateVersion)
    {
        return new ScenarioPackageBinding
        {
            PackageKey = scenarioKey,
            PackageVersion = "1.0.0",
            AssetVersionIds =
            [
                $"template:{scenarioKey}@{templateVersion}",
                $"rule:{scenarioKey}-rule@1.0.0"
            ],
            RequiredResources = []
        };
    }

    private static FlowTemplate CreateClassicTemplateMatchingTemplate()
    {
        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "传统模板匹配检测",
            Description = "适合用标准模板图与待检图像做传统视觉模板匹配的 OK/NG 检测；不依赖深度学习模型，需人工确认模板图、待检图来源与匹配阈值。",
            Industry = "通用制造",
            Tags = ["传统视觉", "模板匹配", "标准模板", "OK/NG"],
            TemplateVersion = "1.0.0",
            ScenarioKey = "classic-template-matching-inspection",
            ScenarioPackage = new ScenarioPackageBinding
            {
                PackageKey = "classic-template-matching-inspection",
                PackageVersion = "1.0.0",
                AssetVersionIds =
                [
                    "template:classic-template-matching-inspection@1.0.0",
                    "rule:template-match-okng@1.0.0"
                ],
                RequiredResources = []
            },
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "使用传统模板匹配：读取待检图像和标准模板图，TemplateMatching 输出匹配结果，再按 IsMatch 做 OK/NG 判定。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "TemplateMatching.Threshold",
                    "TemplateMatching.Domain",
                    "TemplateMatching.UseRoi",
                    "TemplateMatching.EnablePoseSearch"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "待检图像", new Dictionary<string, string>
                    {
                        ["SourceType"] = "File",
                        ["FilePath"] = ""
                    }),
                    Node("op_2", "ImageAcquisition", "标准模板图", new Dictionary<string, string>
                    {
                        ["SourceType"] = "File",
                        ["FilePath"] = ""
                    }),
                    Node("op_3", "TemplateMatching", "传统模板匹配", new Dictionary<string, string>
                    {
                        ["Method"] = "CCoeffNormed",
                        ["Domain"] = "Gray",
                        ["Threshold"] = "0.8",
                        ["MaxMatches"] = "1",
                        ["UseRoi"] = "false",
                        ["RoiX"] = "0",
                        ["RoiY"] = "0",
                        ["RoiWidth"] = "0",
                        ["RoiHeight"] = "0",
                        ["EnablePoseSearch"] = "false"
                    }),
                    Node("op_4", "ResultJudgment", "合格判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Equal",
                        ["ExpectValue"] = "True",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_3", "Image"),
                    Link("op_2", "Image", "op_3", "Template"),
                    Link("op_3", "IsMatch", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "Matches", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["SourceType", "FilePath", "CameraId"],
                    ["op_2"] = ["FilePath"],
                    ["op_3"] = ["Threshold", "Domain", "UseRoi", "RoiX", "RoiY", "RoiWidth", "RoiHeight", "EnablePoseSearch"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateGradientShapeMatchPositioningTemplate()
    {
        const string scenarioKey = "gradient-shape-match-positioning";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "梯度形状匹配定位检测",
            Description = "适合轮廓清晰、允许小角度旋转的工件定位与有无检测；使用梯度方向形状匹配，建议现场用 ROI 限定搜索区并开启模板缓存。",
            Industry = "通用制造",
            Tags = ["传统视觉", "形状匹配", "定位", "旋转鲁棒"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于边缘轮廓稳定、纹理不明显但姿态会轻微变化的工件定位。梯度形状匹配比灰度相关匹配更抗光照波动；ROI 和 TopK 保持较小可降低节拍压力。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "GradientShapeMatch.MinScore",
                    "GradientShapeMatch.AngleRange",
                    "GradientShapeMatch.AngleStep",
                    "GradientShapeMatch.UseRoi",
                    "GradientShapeMatch.RoiX",
                    "GradientShapeMatch.RoiY",
                    "GradientShapeMatch.RoiWidth",
                    "GradientShapeMatch.RoiHeight"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "待检图像", CameraAcquisitionParameters()),
                    Node("op_2", "ImageAcquisition", "标准轮廓模板", FileAcquisitionParameters()),
                    Node("op_3", "GradientShapeMatch", "梯度形状匹配", new Dictionary<string, string>
                    {
                        ["TemplatePath"] = "",
                        ["MinScore"] = "80",
                        ["TopK"] = "1",
                        ["AngleRange"] = "30",
                        ["AngleStep"] = "2",
                        ["MagnitudeThreshold"] = "30",
                        ["EnableCache"] = "true",
                        ["UseRoi"] = "true",
                        ["RoiX"] = "0",
                        ["RoiY"] = "0",
                        ["RoiWidth"] = TemplateDefaultRegionExtent,
                        ["RoiHeight"] = TemplateDefaultRegionExtent
                    }),
                    Node("op_4", "ResultJudgment", "匹配判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Equal",
                        ["ExpectValue"] = "True",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_3", "Image"),
                    Link("op_2", "Image", "op_3", "Template"),
                    Link("op_3", "IsMatch", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "Matches", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["FilePath"],
                    ["op_3"] = ["MinScore", "AngleRange", "AngleStep", "UseRoi", "RoiX", "RoiY", "RoiWidth", "RoiHeight"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreatePlanarFeatureMatchingTemplate()
    {
        const string scenarioKey = "planar-feature-label-positioning";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "平面特征匹配定位检测",
            Description = "适合铭牌、标签、印刷件等有纹理平面目标的定位和贴附验证；用 ORB/AKAZE 特征匹配加单应性校验，能处理透视变化。",
            Industry = "包装追溯",
            Tags = ["传统视觉", "特征匹配", "平面定位", "透视校验"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于带文字、图案、二维码背景或铭牌纹理的平面目标。PlanarMatching 使用特征点匹配和 RANSAC 单应性验证，比普通模板匹配更适合透视、尺度和轻微旋转变化。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "PlanarMatching.DetectorType",
                    "PlanarMatching.MatchRatio",
                    "PlanarMatching.MinInliers",
                    "PlanarMatching.ScoreThreshold",
                    "PlanarMatching.UseRoi"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "待检图像", CameraAcquisitionParameters()),
                    Node("op_2", "ImageAcquisition", "标准平面模板", FileAcquisitionParameters()),
                    Node("op_3", "PlanarMatching", "平面特征匹配", new Dictionary<string, string>
                    {
                        ["TemplatePath"] = "",
                        ["DetectorType"] = "ORB",
                        ["MaxFeatures"] = "1200",
                        ["ScaleFactor"] = "1.2",
                        ["NLevels"] = "8",
                        ["MatchRatio"] = "0.75",
                        ["RansacThreshold"] = "3",
                        ["MinMatchCount"] = "12",
                        ["MinInliers"] = "8",
                        ["MinInlierRatio"] = "0.3",
                        ["ScoreThreshold"] = "0.55",
                        ["AllowCenterOnlyProjection"] = "false",
                        ["UseRoi"] = "false",
                        ["RoiX"] = "0",
                        ["RoiY"] = "0",
                        ["RoiWidth"] = "0",
                        ["RoiHeight"] = "0",
                        ["EnableMultiScale"] = "true",
                        ["ScaleRange"] = "0.2",
                        ["EnableEarlyExit"] = "true"
                    }),
                    Node("op_4", "ResultJudgment", "单应性判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Equal",
                        ["ExpectValue"] = "True",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_3", "Image"),
                    Link("op_2", "Image", "op_3", "Template"),
                    Link("op_3", "VerificationPassed", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "MatchResult", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["FilePath"],
                    ["op_3"] = ["DetectorType", "MatchRatio", "MinInliers", "MinInlierRatio", "ScoreThreshold", "UseRoi", "RoiX", "RoiY", "RoiWidth", "RoiHeight"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateBlobDefectRegionAnalysisTemplate()
    {
        const string scenarioKey = "blob-defect-region-analysis";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Blob缺陷区域分析",
            Description = "适合黑点、白点、脏污、缺料、毛刺等高对比缺陷；采用光照校正、阈值分割、形态学清理和连通域面积/形状过滤。",
            Industry = "通用制造",
            Tags = ["传统视觉", "Blob", "连通域", "缺陷区域"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于高对比、可二值化的区域缺陷。先做光照校正，再用 Otsu 阈值和形态学开运算清理噪点，最后按面积、圆度、凸度等 Blob 特征输出缺陷数量。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "Thresholding.UseOtsu",
                    "MorphologicalOperation.KernelWidth",
                    "MorphologicalOperation.KernelHeight",
                    "BlobAnalysis.MinArea",
                    "BlobAnalysis.MaxArea",
                    "BlobAnalysis.MinCircularity"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", CameraAcquisitionParameters()),
                    Node("op_2", "ShadingCorrection", "光照校正", new Dictionary<string, string>
                    {
                        ["Method"] = "GaussianModel",
                        ["KernelSize"] = "51",
                        ["ColorMode"] = "LumaOnly"
                    }),
                    Node("op_3", "Thresholding", "自适应阈值", new Dictionary<string, string>
                    {
                        ["Threshold"] = "0",
                        ["MaxValue"] = "255",
                        ["Type"] = "8",
                        ["UseOtsu"] = "true"
                    }),
                    Node("op_4", "MorphologicalOperation", "形态学去噪", new Dictionary<string, string>
                    {
                        ["Operation"] = "Open",
                        ["KernelShape"] = "Ellipse",
                        ["KernelWidth"] = "3",
                        ["KernelHeight"] = "3",
                        ["Iterations"] = "1",
                        ["AnchorX"] = "-1",
                        ["AnchorY"] = "-1"
                    }),
                    Node("op_5", "BlobAnalysis", "连通域缺陷分析", new Dictionary<string, string>
                    {
                        ["MinArea"] = "20",
                        ["MaxArea"] = "20000",
                        ["Color"] = "White",
                        ["MinCircularity"] = "0.0",
                        ["MinConvexity"] = "0.0",
                        ["MinInertiaRatio"] = "0.0",
                        ["MinRectangularity"] = "0.0",
                        ["MinEccentricity"] = "0.0",
                        ["OutputDetailedFeatures"] = "true",
                        ["FeatureFilter"] = "",
                        ["EnableColorFilter"] = "false",
                        ["HueLow"] = "0",
                        ["HueHigh"] = "180",
                        ["SatLow"] = "50",
                        ["SatHigh"] = "255",
                        ["ValLow"] = "50",
                        ["ValHigh"] = "255"
                    }),
                    Node("op_6", "ResultJudgment", "缺陷数量判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Equal",
                        ["ExpectValue"] = "0",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_7", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", "Image", "op_4", "Image"),
                    Link("op_4", "Image", "op_5", "Image"),
                    Link("op_2", "Image", "op_5", "SourceImage"),
                    Link("op_5", "BlobCount", "op_6", "Value"),
                    Link("op_5", "Image", "op_7", "Image"),
                    Link("op_5", "BlobFeatures", "op_7", "Data"),
                    Link("op_6", "JudgmentResult", "op_7", "Result"),
                    Link("op_6", "Details", "op_7", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["Method", "KernelSize"],
                    ["op_3"] = ["UseOtsu", "Threshold", "Type"],
                    ["op_4"] = ["Operation", "KernelWidth", "KernelHeight", "Iterations"],
                    ["op_5"] = ["MinArea", "MaxArea", "Color", "MinCircularity", "OutputDetailedFeatures"],
                    ["op_6"] = ["Condition", "ExpectValue"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateCaliperWidthMeasurementTemplate()
    {
        const string scenarioKey = "caliper-width-measurement";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "卡尺宽度测量",
            Description = "适合胶条、槽宽、焊缝、间隙等稳定边缘对的在线尺寸检测；采用轻量滤波和卡尺边缘对测量，可输出均值与离散度。",
            Industry = "精密装配",
            Tags = ["传统视觉", "卡尺", "宽度", "尺寸测量"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于两条稳定边缘之间的宽度/间隙测量。卡尺算子沿扫描方向寻找边缘对，开启亚像素模式后适合做工程尺寸判定；测量 ROI 应在现场固化。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "CaliperTool.Direction",
                    "CaliperTool.EdgeThreshold",
                    "CaliperTool.ExpectedCount",
                    "ResultJudgment.ExpectValueMin",
                    "ResultJudgment.ExpectValueMax"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", CameraAcquisitionParameters()),
                    Node("op_2", "Filtering", "轻量滤波", new Dictionary<string, string>
                    {
                        ["KernelSize"] = "3",
                        ["SigmaX"] = "0.8",
                        ["SigmaY"] = "0.0",
                        ["BorderType"] = "4"
                    }),
                    Node("op_3", "CaliperTool", "边缘对卡尺测量", new Dictionary<string, string>
                    {
                        ["Direction"] = "Horizontal",
                        ["Angle"] = "0",
                        ["Polarity"] = "Both",
                        ["EdgeThreshold"] = "18",
                        ["ExpectedCount"] = "1",
                        ["MeasureMode"] = "edge_pairs",
                        ["PairDirection"] = "any",
                        ["SubpixelAccuracy"] = "true",
                        ["SubPixelMode"] = "gradient_centroid"
                    }),
                    Node("op_4", "ResultJudgment", "宽度范围判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Range",
                        ["ExpectValueMin"] = "0",
                        ["ExpectValueMax"] = "999999",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", "AverageDistance", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "PairDistances", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["KernelSize", "SigmaX"],
                    ["op_3"] = ["Direction", "Angle", "Polarity", "EdgeThreshold", "ExpectedCount", "SubpixelAccuracy"],
                    ["op_4"] = ["ExpectValueMin", "ExpectValueMax"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateCircularHoleMeasurementTemplate()
    {
        const string scenarioKey = "circular-hole-radius-measurement";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "圆孔孔径与圆度检测",
            Description = "适合螺丝孔、冲孔、铜孔等圆形特征的半径、圆心、圆度和数量检测；优先使用固定 ROI 和半径范围约束。",
            Industry = "精密装配",
            Tags = ["传统视觉", "圆测量", "孔径", "圆度"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于孔径、圆心和圆度检测。先做轻量滤波降低噪声，再用圆测量输出半径、圆度和圆数量；现场应按产品规格收紧 MinRadius/MaxRadius 和判定范围。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "CircleMeasurement.Method",
                    "CircleMeasurement.MinRadius",
                    "CircleMeasurement.MaxRadius",
                    "CircleMeasurement.Param1",
                    "CircleMeasurement.Param2",
                    "ResultJudgment.ExpectValueMin",
                    "ResultJudgment.ExpectValueMax"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", CameraAcquisitionParameters()),
                    Node("op_2", "Filtering", "孔边缘降噪", new Dictionary<string, string>
                    {
                        ["KernelSize"] = "5",
                        ["SigmaX"] = "1.0",
                        ["SigmaY"] = "0.0",
                        ["BorderType"] = "4"
                    }),
                    Node("op_3", "CircleMeasurement", "圆孔测量", new Dictionary<string, string>
                    {
                        ["Method"] = "HoughCircle",
                        ["MinRadius"] = "10",
                        ["MaxRadius"] = "200",
                        ["Dp"] = "1",
                        ["MinDist"] = "50",
                        ["Param1"] = "100",
                        ["Param2"] = "30"
                    }),
                    Node("op_4", "ResultJudgment", "孔径范围判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Range",
                        ["ExpectValueMin"] = "0",
                        ["ExpectValueMax"] = "999999",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", "Radius", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "CircleDataList", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["KernelSize", "SigmaX"],
                    ["op_3"] = ["Method", "MinRadius", "MaxRadius", "MinDist", "Param1", "Param2"],
                    ["op_4"] = ["ExpectValueMin", "ExpectValueMax"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateColorDeltaEInspectionTemplate()
    {
        const string scenarioKey = "color-deltae-inspection";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Lab色差检测",
            Description = "适合喷涂、注塑、标签和指示件的颜色偏差检测；使用光照校正和 CIEDE2000 DeltaE，避免只看 RGB 均值。",
            Industry = "通用制造",
            Tags = ["传统视觉", "颜色", "Lab", "DeltaE"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于颜色一致性检测。先做光照校正，再在稳定 ROI 上计算 Lab DeltaE；CIEDE2000 更贴近人眼色差感知，适合喷涂和标签色偏判定。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "ColorMeasurement.RoiX",
                    "ColorMeasurement.RoiY",
                    "ColorMeasurement.RoiW",
                    "ColorMeasurement.RoiH",
                    "ColorMeasurement.RefL",
                    "ColorMeasurement.RefA",
                    "ColorMeasurement.RefB",
                    "ResultJudgment.ExpectValue"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", CameraAcquisitionParameters()),
                    Node("op_2", "ShadingCorrection", "光照校正", new Dictionary<string, string>
                    {
                        ["Method"] = "GaussianModel",
                        ["KernelSize"] = "51",
                        ["ColorMode"] = "PerChannel"
                    }),
                    Node("op_3", "ColorMeasurement", "Lab色差测量", new Dictionary<string, string>
                    {
                        ["MeasurementMode"] = "LabDeltaE",
                        ["DeltaEMethod"] = "CIEDE2000",
                        ["RoiX"] = "0",
                        ["RoiY"] = "0",
                        ["RoiW"] = "0",
                        ["RoiH"] = "0",
                        ["RefL"] = "0",
                        ["RefA"] = "0",
                        ["RefB"] = "0"
                    }),
                    Node("op_4", "ResultJudgment", "色差阈值判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "LessOrEqual",
                        ["ExpectValue"] = "3.0",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", "DeltaE", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "LabMean", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["Method", "KernelSize", "ColorMode"],
                    ["op_3"] = ["RoiX", "RoiY", "RoiW", "RoiH", "RefL", "RefA", "RefB"],
                    ["op_4"] = ["ExpectValue"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateCodeTraceabilityTemplate()
    {
        const string scenarioKey = "code-traceability-inspection";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "条码二维码追溯检测",
            Description = "适合包装、铭牌和零部件追溯码读取；先做清晰度质量门，再识别 QR、DataMatrix、Code128 等码制并按识别数量判定。",
            Industry = "包装追溯",
            Tags = ["传统视觉", "条码", "二维码", "追溯"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于在线追溯码读取。SharpnessEvaluation 先拦截失焦图像，CodeRecognition 负责读码，ResultJudgment 以识别数量作为 OK/NG 基础条件。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "SharpnessEvaluation.Method",
                    "SharpnessEvaluation.Threshold",
                    "SharpnessEvaluation.RoiX",
                    "SharpnessEvaluation.RoiY",
                    "SharpnessEvaluation.RoiW",
                    "SharpnessEvaluation.RoiH",
                    "CodeRecognition.CodeType",
                    "CodeRecognition.MaxResults"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", CameraAcquisitionParameters()),
                    Node("op_2", "SharpnessEvaluation", "清晰度质量门", new Dictionary<string, string>
                    {
                        ["Method"] = "Tenengrad",
                        ["ThresholdMode"] = "Manual",
                        ["Threshold"] = "100",
                        ["RoiX"] = "0",
                        ["RoiY"] = "0",
                        ["RoiW"] = "0",
                        ["RoiH"] = "0",
                        ["OutputImagePolicy"] = "Passthrough"
                    }),
                    Node("op_3", "CodeRecognition", "追溯码识别", new Dictionary<string, string>
                    {
                        ["CodeType"] = "All",
                        ["MaxResults"] = "4"
                    }),
                    Node("op_4", "ResultJudgment", "读码数量判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "GreaterOrEqual",
                        ["ExpectValue"] = "1",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", "CodeCount", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "Text", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["Method", "Threshold", "RoiX", "RoiY", "RoiW", "RoiH"],
                    ["op_3"] = ["CodeType", "MaxResults"],
                    ["op_4"] = ["ExpectValue"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateSurfaceReferenceDefectTemplate()
    {
        const string scenarioKey = "surface-reference-defect-inspection";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "参考图表面缺陷检测",
            Description = "适合金属、膜面、喷涂件等低对比划伤/脏污；使用参考图差分、相位相关对齐、局部均值归一化和组件形状过滤。",
            Industry = "表面检测",
            Tags = ["传统视觉", "表面缺陷", "参考图差分", "划伤"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于有稳定良品参考图的表面缺陷检测。ReferenceDiff + PhaseCorrelation 先做对齐，再用局部归一化和组件过滤抑制光照起伏与小噪声。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "SurfaceDefectDetection.Method",
                    "SurfaceDefectDetection.Threshold",
                    "SurfaceDefectDetection.MinArea",
                    "SurfaceDefectDetection.MorphCleanSize",
                    "SurfaceDefectDetection.ComponentFilterMode"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "待检图像", CameraAcquisitionParameters()),
                    Node("op_2", "ImageAcquisition", "良品参考图", FileAcquisitionParameters()),
                    Node("op_3", "SurfaceDefectDetection", "参考图差分缺陷检测", new Dictionary<string, string>
                    {
                        ["Method"] = "ReferenceDiff",
                        ["Threshold"] = "35",
                        ["MinArea"] = "20",
                        ["MaxArea"] = "1000000",
                        ["MorphCleanSize"] = "3",
                        ["MorphMode"] = "OpenClose",
                        ["AlignmentMode"] = "PhaseCorrelation",
                        ["NormalizationMode"] = "ClaheLocalMean",
                        ["ThresholdMode"] = "ReferenceStats",
                        ["BackgroundKernelSize"] = "31",
                        ["ClaheClipLimit"] = "2",
                        ["ClaheTileGridSize"] = "8",
                        ["ReferenceStatsSigma"] = "2.5",
                        ["RobustReferenceStats"] = "true",
                        ["ResponseNormalizeMode"] = "PercentileClip",
                        ["ComponentFilterMode"] = "ShapeAndResponseStats",
                        ["SmallNoiseAreaMax"] = "8",
                        ["MinElongationForSmallComponent"] = "1.5",
                        ["CompactNoiseAreaMax"] = "8",
                        ["CompactNoiseCircularityMin"] = "0.75",
                        ["CompactNoiseFillRatioMin"] = "0.65",
                        ["MinLocalResponseProminence"] = "6",
                        ["EnableCandidateProfile"] = "true",
                        ["CandidateProfile"] = "taxonomy_v2"
                    }),
                    Node("op_4", "ResultJudgment", "缺陷数量判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Equal",
                        ["ExpectValue"] = "0",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_5", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_3", "Image"),
                    Link("op_2", "Image", "op_3", "Reference"),
                    Link("op_3", "DefectCount", "op_4", "Value"),
                    Link("op_3", "Image", "op_5", "Image"),
                    Link("op_3", "Diagnostics", "op_5", "Data"),
                    Link("op_4", "JudgmentResult", "op_5", "Result"),
                    Link("op_4", "Details", "op_5", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["FilePath"],
                    ["op_3"] = ["Threshold", "MinArea", "MorphCleanSize", "ReferenceStatsSigma", "ComponentFilterMode", "MinLocalResponseProminence"],
                    ["op_4"] = ["ExpectValue"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateSharpnessFocusGateTemplate()
    {
        const string scenarioKey = "sharpness-focus-gate";
        const string templateVersion = "1.0.0";

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "清晰度对焦质量门",
            Description = "适合所有视觉工位前置的失焦、运动模糊和脏污镜头拦截；用 Tenengrad/Laplacian 清晰度分数做质量门。",
            Industry = "通用制造",
            Tags = ["传统视觉", "清晰度", "对焦", "质量门"],
            TemplateVersion = templateVersion,
            ScenarioKey = scenarioKey,
            ScenarioPackage = CreateRuleOnlyPackage(scenarioKey, templateVersion),
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "用于检测流程前置图像质量门。清晰度不足时直接输出 NG，可避免后续定位、测量或识别算法在失焦图上产生不稳定结论。",
                requiredResources = Array.Empty<string>(),
                tunableParameters = new[]
                {
                    "SharpnessEvaluation.Method",
                    "SharpnessEvaluation.Threshold",
                    "SharpnessEvaluation.RoiX",
                    "SharpnessEvaluation.RoiY",
                    "SharpnessEvaluation.RoiW",
                    "SharpnessEvaluation.RoiH"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", CameraAcquisitionParameters()),
                    Node("op_2", "SharpnessEvaluation", "清晰度评估", new Dictionary<string, string>
                    {
                        ["Method"] = "Tenengrad",
                        ["ThresholdMode"] = "Manual",
                        ["Threshold"] = "100",
                        ["RoiX"] = "0",
                        ["RoiY"] = "0",
                        ["RoiW"] = "0",
                        ["RoiH"] = "0",
                        ["OutputImagePolicy"] = "FullOverlay"
                    }),
                    Node("op_3", "ResultJudgment", "清晰度判定", new Dictionary<string, string>
                    {
                        ["FieldName"] = "Value",
                        ["Condition"] = "Equal",
                        ["ExpectValue"] = "True",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_4", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "IsSharp", "op_3", "Value"),
                    Link("op_2", "Image", "op_4", "Image"),
                    Link("op_2", "Score", "op_4", "Data"),
                    Link("op_3", "JudgmentResult", "op_4", "Result"),
                    Link("op_3", "Details", "op_4", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_1"] = ["CameraId", "ExposureTime", "Gain"],
                    ["op_2"] = ["Method", "Threshold", "RoiX", "RoiY", "RoiW", "RoiH"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateAiInspectionTemplate(
        string name,
        string scenarioKey,
        string industry,
        string description,
        string explanation,
        IReadOnlyList<string> tags,
        string targetClasses,
        string detectionMode,
        string detectionPort,
        string judgmentCondition,
        string judgmentExpectValue,
        bool includeTargetClassesInReview)
    {
        var reviewParameters = new Dictionary<string, List<string>>
        {
            ["op_3"] = includeTargetClassesInReview
                ? ["ModelPath", "TargetClasses", "OutputFormat", "Confidence"]
                : ["ModelPath", "OutputFormat", "Confidence"],
            ["op_4"] = ["RegionX", "RegionY", "RegionW", "RegionH"]
        };

        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Industry = industry,
            Tags = tags.ToList(),
            TemplateVersion = "1.2.0",
            ScenarioKey = scenarioKey,
            ScenarioPackage = new ScenarioPackageBinding
            {
                PackageKey = scenarioKey,
                PackageVersion = "1.0.0",
                AssetVersionIds =
                [
                    $"template:{scenarioKey}@1.2.0",
                    $"model:{scenarioKey}-detector@1.0.0",
                    $"label:{scenarioKey}-labels@1.0.0"
                ],
                RequiredResources =
                [
                    "DeepLearning.ModelPath"
                ]
            },
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation,
                requiredResources = new[] { "DeepLearning.ModelPath" },
                tunableParameters = new[]
                {
                    "DeepLearning.Confidence"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", new Dictionary<string, string> { ["sourceType"] = "camera" }),
                    Node("op_2", "ImageResize", "尺寸适配", new Dictionary<string, string>
                    {
                        ["Width"] = "640",
                        ["Height"] = "640"
                    }),
                    Node("op_3", "DeepLearning", detectionMode == "Object" ? "遥控器检测" : "外观缺陷检测", new Dictionary<string, string>
                    {
                        ["ModelPath"] = "",
                        ["LabelsPath"] = "",
                        ["Confidence"] = "0.5",
                        ["InputSize"] = "640",
                        ["TargetClasses"] = targetClasses,
                        ["EnableInternalNms"] = "true",
                        ["OutputFormat"] = "EndToEndNms",
                        ["DetectionMode"] = detectionMode
                    }),
                    Node("op_4", "BoxFilter", "ROI区域过滤", new Dictionary<string, string>
                    {
                        ["FilterMode"] = "Region",
                        ["RegionX"] = "0",
                        ["RegionY"] = "0",
                        ["RegionW"] = TemplateDefaultRegionExtent,
                        ["RegionH"] = TemplateDefaultRegionExtent,
                        ["MinScore"] = "0.0"
                    }),
                    Node("op_5", "ResultJudgment", detectionMode == "Object" ? "漏装判定" : "外观判定", new Dictionary<string, string>
                    {
                        ["Condition"] = judgmentCondition,
                        ["ExpectValue"] = judgmentExpectValue,
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_6", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", detectionPort, "op_4", "Detections"),
                    Link("op_2", "Image", "op_4", "Image"),
                    Link("op_4", "Count", "op_5", "Value"),
                    Link("op_4", "Image", "op_6", "Image"),
                    Link("op_3", "PostprocessDiagnostics", "op_6", "Data"),
                    Link("op_5", "JudgmentResult", "op_6", "Result"),
                    Link("op_5", "Details", "op_6", "Text")
                },
                parametersNeedingReview = reviewParameters
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FlowTemplate CreateCopperHoleSpacingTemplate()
    {
        return new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "两器铜孔间距检测",
            Description = "适合两器装配工位的铜孔间距检测，默认骨架为滤波 + 边缘检测 + 间距测量 + 范围判定；需人工确认预处理、阈值与像素间距范围。",
            Industry = AirConditioningIndustry,
            Tags = ["两器", "铜孔", "间距", "测量"],
            TemplateVersion = "1.1.0",
            ScenarioKey = "copper-hole-spacing-measurement",
            ScenarioPackage = new ScenarioPackageBinding
            {
                PackageKey = "copper-hole-spacing-measurement",
                PackageVersion = "1.0.0",
                AssetVersionIds =
                [
                    "template:copper-hole-spacing-measurement@1.1.0",
                    "rule:copper-hole-spacing-range@1.0.0"
                ],
                RequiredResources = []
            },
            FlowJson = JsonSerializer.Serialize(new
            {
                explanation = "适用于两器装配工位的铜孔间距检测，先做滤波和边缘增强，再按投影与多扫描方式测量像素间距。",
                tunableParameters = new[]
                {
                    "Filtering.KernelSize",
                    "EdgeDetection.Threshold1",
                    "EdgeDetection.Threshold2",
                    "GapMeasurement.MinGap",
                    "GapMeasurement.MaxGap"
                },
                operators = new object[]
                {
                    Node("op_1", "ImageAcquisition", "图像采集", new Dictionary<string, string> { ["sourceType"] = "camera" }),
                    Node("op_2", "Filtering", "滤波预处理", new Dictionary<string, string>
                    {
                        ["KernelSize"] = "5",
                        ["SigmaX"] = "1.0",
                        ["SigmaY"] = "0.0",
                        ["BorderType"] = "4"
                    }),
                    Node("op_3", "EdgeDetection", "边缘检测", new Dictionary<string, string>
                    {
                        ["Threshold1"] = "50",
                        ["Threshold2"] = "150",
                        ["AutoThreshold"] = "false",
                        ["AutoThresholdSigma"] = "0.33",
                        ["EnableGaussianBlur"] = "false",
                        ["GaussianKernelSize"] = "5",
                        ["ApertureSize"] = "3"
                    }),
                    Node("op_4", "GapMeasurement", "间距测量", new Dictionary<string, string>
                    {
                        ["Direction"] = "Auto",
                        ["MinGap"] = "0",
                        ["MaxGap"] = "0",
                        ["ExpectedCount"] = "0",
                        ["RobustMode"] = "true",
                        ["OutlierSigmaK"] = "3.0",
                        ["MinValidSamples"] = "4",
                        ["MultiScanCount"] = "8"
                    }),
                    Node("op_5", "ResultJudgment", "范围判定", new Dictionary<string, string>
                    {
                        ["Condition"] = "Range",
                        ["ExpectValueMin"] = "0",
                        ["ExpectValueMax"] = "999999",
                        ["MinConfidence"] = "0.0"
                    }),
                    Node("op_6", "ResultOutput", "结果输出", new Dictionary<string, string>
                    {
                        ["Format"] = "JSON",
                        ["SaveToFile"] = "false"
                    })
                },
                connections = new object[]
                {
                    Link("op_1", "Image", "op_2", "Image"),
                    Link("op_2", "Image", "op_3", "Image"),
                    Link("op_3", "Image", "op_4", "Image"),
                    Link("op_4", "MeanGap", "op_5", "Value"),
                    Link("op_4", "Image", "op_6", "Image"),
                    Link("op_4", "MeanGap", "op_6", "Data"),
                    Link("op_5", "JudgmentResult", "op_6", "Result"),
                    Link("op_5", "Details", "op_6", "Text")
                },
                parametersNeedingReview = new Dictionary<string, List<string>>
                {
                    ["op_2"] = ["KernelSize", "SigmaX", "SigmaY"],
                    ["op_3"] = ["Threshold1", "Threshold2", "AutoThresholdSigma"],
                    ["op_4"] = ["Direction", "MinGap", "MaxGap", "MultiScanCount", "MinValidSamples"],
                    ["op_5"] = ["ExpectValueMin", "ExpectValueMax"]
                }
            }, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static object Node(
        string tempId,
        string operatorType,
        string displayName,
        Dictionary<string, string>? parameters = null)
    {
        return new
        {
            tempId,
            operatorType,
            displayName,
            parameters = parameters ?? new Dictionary<string, string>()
        };
    }

    private static object Link(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new
        {
            sourceTempId,
            sourcePortName,
            targetTempId,
            targetPortName
        };
    }

    private static Dictionary<string, string> CameraAcquisitionParameters(
        string triggerMode = "Software")
    {
        return new Dictionary<string, string>
        {
            ["SourceType"] = "Camera",
            ["FilePath"] = "",
            ["CameraId"] = "",
            ["ExposureTime"] = "5000",
            ["Gain"] = "1",
            ["TriggerMode"] = triggerMode
        };
    }

    private static Dictionary<string, string> FileAcquisitionParameters()
    {
        return new Dictionary<string, string>
        {
            ["SourceType"] = "File",
            ["FilePath"] = "",
            ["CameraId"] = "",
            ["ExposureTime"] = "5000",
            ["Gain"] = "1",
            ["TriggerMode"] = "Software"
        };
    }
}
