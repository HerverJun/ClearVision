using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Infrastructure.AI;

public interface IOperatorKnowledgeGraphService
{
    Task<OperatorKnowledgeGraph> BuildAsync(CancellationToken cancellationToken = default);
}

public sealed class OperatorKnowledgeGraphService : IOperatorKnowledgeGraphService
{
    private const string QualityManifestPath = "quality/evals/reports/operator_quality_evidence_manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, string[]> AliasHints =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["DeepLearning"] =
            [
                "YOLO", "目标检测", "缺陷检测", "AI检测", "图像分类", "分类推理", "语义分割", "像素级分割",
                "image classification", "semantic segmentation"
            ],
            ["TemplateMatching"] = ["传统视觉", "模板匹配", "标准模板", "参考图", "基准图", "找图", "TemplateMatch"],
            ["SemanticSegmentation"] = ["语义分割", "像素级分割"],
            ["AnomalyDetection"] = ["异常检测", "无监督缺陷检测"],
            ["SurfaceDefectDetection"] = ["表面缺陷", "外观缺陷", "瑕疵检测"],
            ["BoxFilter"] = ["ROI候选框筛选", "框过滤"],
            ["BoxNms"] = ["NMS", "候选框抑制"],
            ["ResultJudgment"] = ["OKNG判定", "阈值判定"],
            ["DetectionSequenceJudge"] = ["线序判定", "顺序判定"],
            ["GapMeasurement"] = ["间距测量", "孔距测量"],
            ["EdgeDetection"] = ["边缘提取", "Canny"]
        };

    private readonly IOperatorFactory _operatorFactory;
    private readonly IFlowTemplateService _templateService;

    public OperatorKnowledgeGraphService(
        IOperatorFactory operatorFactory,
        IFlowTemplateService templateService)
    {
        _operatorFactory = operatorFactory;
        _templateService = templateService;
    }

    public async Task<OperatorKnowledgeGraph> BuildAsync(CancellationToken cancellationToken = default)
    {
        var metadata = _operatorFactory
            .GetAllMetadata()
            .OrderBy(item => item.Type.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var templates = await _templateService.GetTemplatesAsync(cancellationToken: cancellationToken);
        var evidenceByOperator = LoadEvidenceByOperatorType();
        var cards = BuildCards(metadata, evidenceByOperator);
        var edges = BuildEdges(cards, templates);

        return new OperatorKnowledgeGraph
        {
            Cards = cards,
            Edges = edges
        };
    }

    private static List<OperatorKnowledgeCard> BuildCards(
        IReadOnlyList<OperatorMetadata> metadata,
        IReadOnlyDictionary<string, QualityOperatorEvidence> evidenceByOperator)
    {
        var cards = new List<OperatorKnowledgeCard>(metadata.Count);

        foreach (var item in metadata)
        {
            var operatorType = item.Type.ToString();
            var aliases = BuildAliases(item, operatorType);
            var resourceRequirements = item.ParameterConstraints
                .Where(constraint => !string.IsNullOrWhiteSpace(constraint.ResourceKind))
                .Select(constraint => new OperatorKnowledgeResourceRequirement
                {
                    Parameter = constraint.Parameter,
                    ResourceKind = constraint.ResourceKind!,
                    ReasonCode = constraint.ReasonCode,
                    AtLeastOneGroup = constraint.AtLeastOneGroup,
                    RequiredWhen = constraint.RequiredWhen
                })
                .GroupBy(
                    requirement => $"{requirement.Parameter}|{requirement.ResourceKind}|{requirement.ReasonCode}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var requiredResources = resourceRequirements
                .Select(requirement => $"{operatorType}.{requirement.Parameter}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var evidence = evidenceByOperator.TryGetValue(operatorType, out var found)
                ? found
                : QualityOperatorEvidence.CreateDefault(operatorType, item.DisplayName);

            cards.Add(new OperatorKnowledgeCard
            {
                OperatorType = operatorType,
                DisplayName = item.DisplayName,
                CategoryId = item.CategoryId.ToString(),
                CategoryOrder = OperatorCategoryCatalog.GetOrder(item.CategoryId),
                Category = item.Category,
                Lifecycle = item.Lifecycle.ToString(),
                LifecycleNote = item.LifecycleNote,
                DefaultHidden = item.DefaultHidden,
                DefaultAiRecommendation = ImageContractPresentationBuilder.IsDefaultAiRecommendation(
                    item.Lifecycle,
                    item.ImageInputContracts),
                RequiresLifecycleDisclosure = ImageContractPresentationBuilder.RequiresAiDisclosure(
                    item.Lifecycle,
                    item.ImageInputContracts),
                QualityState = item.QualityState,
                Aliases = aliases,
                IntentTags = BuildIntentTags(item),
                ScenarioTags = BuildScenarioTags(item),
                Inputs = item.InputPorts.Select(input => new OperatorKnowledgePort
                {
                    Name = input.Name,
                    DisplayName = input.DisplayName,
                    DataType = input.DataType.ToString(),
                    IsRequired = input.IsRequired,
                    Description = input.Description
                }).ToList(),
                Outputs = item.OutputPorts.Select(output => new OperatorKnowledgePort
                {
                    Name = output.Name,
                    DisplayName = output.DisplayName,
                    DataType = output.DataType.ToString(),
                    IsRequired = output.IsRequired,
                    Description = output.Description
                }).ToList(),
                Parameters = item.Parameters.Select(parameter => new OperatorKnowledgeParameter
                {
                    Name = parameter.Name,
                    DisplayName = parameter.DisplayName,
                    DataType = parameter.DataType,
                    Description = parameter.Description,
                    DefaultValue = parameter.DefaultValue?.ToString(),
                    MinValue = parameter.MinValue?.ToString(),
                    MaxValue = parameter.MaxValue?.ToString(),
                    IsRequired = parameter.IsRequired,
                    AllowedValues = parameter.Options?.Select(option => option.Label).Where(label => !string.IsNullOrWhiteSpace(label)).ToList()
                                    ?? new List<string>()
                }).ToList(),
                ParameterConditions = item.ParameterConstraints.ToList(),
                OutputConditions = item.OutputAvailabilityRules.ToList(),
                ImageInputContracts = item.ImageInputContracts.ToList(),
                ImageInputContractPresentations = item.ImageInputContractPresentations.ToList(),
                ResourceRequirements = resourceRequirements,
                GenerationDependencies = item.GenerationDependencies.ToList(),
                RequiredResources = requiredResources,
                KnownLimitations = BuildKnownLimitations(item, evidence),
                Evidence = new OperatorKnowledgeEvidence
                {
                    Contract = evidence.Contract,
                    Golden = evidence.Golden,
                    Dataset = evidence.Dataset,
                    FieldReplay = evidence.FieldReplay,
                    PrecisionClaim = evidence.PrecisionClaim,
                    IndustrialStatus = evidence.IndustrialStatus,
                    QScore = evidence.Priority
                }
            });
        }

        return cards;
    }

    private static List<OperatorKnowledgeEdge> BuildEdges(
        IReadOnlyList<OperatorKnowledgeCard> cards,
        IReadOnlyList<ClearVision.Product.Core.Entities.FlowTemplate> templates)
    {
        var edges = new List<OperatorKnowledgeEdge>();
        var cardsByType = cards.ToDictionary(card => card.OperatorType, StringComparer.OrdinalIgnoreCase);

        AddPortCompatibilityEdges(cards, edges);
        AddTemplateEdges(templates, edges, cardsByType);

        foreach (var card in cards)
        {
            foreach (var alias in card.Aliases.Where(item =>
                         !string.IsNullOrWhiteSpace(item) &&
                         !item.Equals(card.OperatorType, StringComparison.OrdinalIgnoreCase)))
            {
                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "ALIAS_OF",
                    Source = alias,
                    Target = card.OperatorType
                });
            }

            foreach (var requiredResource in card.RequiredResources)
            {
                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "REQUIRES_RESOURCE",
                    Source = card.OperatorType,
                    Target = requiredResource
                });
            }

            edges.Add(new OperatorKnowledgeEdge
            {
                RelationType = "HAS_EVIDENCE",
                Source = card.OperatorType,
                Target = $"{card.Evidence.Contract}|{card.Evidence.Golden}|{card.Evidence.Dataset}|{card.Evidence.FieldReplay}"
            });
        }

        return edges
            .GroupBy(edge => $"{edge.RelationType}|{edge.Source}|{edge.Target}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(edge => edge.RelationType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddPortCompatibilityEdges(
        IReadOnlyList<OperatorKnowledgeCard> cards,
        List<OperatorKnowledgeEdge> edges)
    {
        foreach (var source in cards)
        {
            foreach (var output in source.Outputs)
            {
                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "PRODUCES",
                    Source = source.OperatorType,
                    Target = output.DataType,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["port"] = output.Name
                    }
                });
            }
        }

        foreach (var target in cards)
        {
            foreach (var input in target.Inputs)
            {
                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "CONSUMES",
                    Source = target.OperatorType,
                    Target = input.DataType,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["port"] = input.Name
                    }
                });
            }
        }
    }

    private static void AddTemplateEdges(
        IReadOnlyList<ClearVision.Product.Core.Entities.FlowTemplate> templates,
        List<OperatorKnowledgeEdge> edges,
        IReadOnlyDictionary<string, OperatorKnowledgeCard> cardsByType)
    {
        foreach (var template in templates)
        {
            if (string.IsNullOrWhiteSpace(template.FlowJson))
                continue;

            TemplateGraphSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<TemplateGraphSnapshot>(template.FlowJson, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (snapshot?.Operators == null || snapshot.Connections == null)
                continue;

            var operatorByTempId = snapshot.Operators
                .Where(operatorNode => !string.IsNullOrWhiteSpace(operatorNode.TempId) &&
                                       !string.IsNullOrWhiteSpace(operatorNode.OperatorType))
                .ToDictionary(node => node.TempId, node => node.OperatorType, StringComparer.OrdinalIgnoreCase);

            var scenarioKey = !string.IsNullOrWhiteSpace(template.ScenarioKey)
                ? template.ScenarioKey!
                : template.Name;

            foreach (var type in operatorByTempId.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "USED_IN_TEMPLATE",
                    Source = type,
                    Target = scenarioKey,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["templateName"] = template.Name,
                        ["templateVersion"] = template.TemplateVersion
                    }
                });
            }

            if (template.ScenarioPackage?.RequiredResources != null &&
                template.ScenarioPackage.RequiredResources.Count > 0)
            {
                var resources = template.ScenarioPackage.RequiredResources
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (resources.Count > 0)
                {
                    foreach (var type in operatorByTempId.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        foreach (var resource in resources)
                        {
                            edges.Add(new OperatorKnowledgeEdge
                            {
                                RelationType = "REQUIRES_RESOURCE",
                                Source = type,
                                Target = resource,
                                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["templateName"] = template.Name,
                                    ["scenarioKey"] = scenarioKey
                                }
                            });
                        }
                    }
                }
            }

            foreach (var connection in snapshot.Connections)
            {
                if (!operatorByTempId.TryGetValue(connection.SourceTempId, out var sourceType) ||
                    !operatorByTempId.TryGetValue(connection.TargetTempId, out var targetType))
                {
                    continue;
                }

                if (!cardsByType.TryGetValue(sourceType, out var sourceCard) ||
                    !cardsByType.TryGetValue(targetType, out var targetCard))
                {
                    continue;
                }

                if (!sourceCard.TypicalDownstream.Contains(targetType, StringComparer.OrdinalIgnoreCase))
                    sourceCard.TypicalDownstream.Add(targetType);

                if (!targetCard.TypicalUpstream.Contains(sourceType, StringComparer.OrdinalIgnoreCase))
                    targetCard.TypicalUpstream.Add(sourceType);

                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "COMMONLY_PRECEDES",
                    Source = sourceType,
                    Target = targetType,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["template"] = template.Name,
                        ["sourcePort"] = connection.SourcePortName,
                        ["targetPort"] = connection.TargetPortName
                    }
                });

                edges.Add(new OperatorKnowledgeEdge
                {
                    RelationType = "COMMONLY_FOLLOWS",
                    Source = targetType,
                    Target = sourceType,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["template"] = template.Name,
                        ["sourcePort"] = connection.SourcePortName,
                        ["targetPort"] = connection.TargetPortName
                    }
                });
            }
        }
    }

    private static List<string> BuildAliases(OperatorMetadata metadata, string operatorType)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            metadata.DisplayName,
            operatorType
        };

        if (metadata.Keywords != null)
        {
            foreach (var keyword in metadata.Keywords.Where(item => !string.IsNullOrWhiteSpace(item)))
                aliases.Add(keyword.Trim());
        }

        if (AliasHints.TryGetValue(operatorType, out var hints))
        {
            foreach (var hint in hints)
                aliases.Add(hint);
        }

        return aliases.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
    }

    private static List<string> BuildIntentTags(OperatorMetadata metadata)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (metadata.Tags != null)
        {
            foreach (var tag in metadata.Tags.Where(item => !string.IsNullOrWhiteSpace(item)))
                tags.Add(NormalizeTag(tag));
        }

        foreach (var intent in CategoryIntents(metadata.CategoryId))
        {
            tags.Add(intent);
        }

        if (metadata.Type == OperatorType.ResultOutput || metadata.Type == OperatorType.ResultJudgment)
            tags.Add("decision_output");

        if (metadata.Type == OperatorType.DeepLearning)
        {
            tags.Add("object_detection");
            tags.Add("image_classification");
            tags.Add("semantic_segmentation");
        }

        if (tags.Count == 0)
            tags.Add("general");

        return tags.ToList();
    }

    private static IReadOnlyList<string> CategoryIntents(OperatorCategoryId categoryId) => categoryId switch
    {
        OperatorCategoryId.Acquisition => ["acquisition", "image_source"],
        OperatorCategoryId.ImagePreprocessing => ["preprocess"],
        OperatorCategoryId.SegmentationAndRegion => ["segmentation", "region_processing"],
        OperatorCategoryId.FeatureExtraction => ["feature_extraction"],
        OperatorCategoryId.MatchingAndLocalization => ["matching", "localization"],
        OperatorCategoryId.DefectDetection => ["defect_detection", "inspection"],
        OperatorCategoryId.Measurement => ["measurement", "metrology"],
        OperatorCategoryId.CalibrationAndCoordinates => ["calibration", "coordinate_transform"],
        OperatorCategoryId.AiInference => ["ai_inference"],
        OperatorCategoryId.PointCloud3D => ["point_cloud", "3d"],
        OperatorCategoryId.DataProcessing => ["data_processing"],
        OperatorCategoryId.FlowControl => ["flow_control"],
        OperatorCategoryId.Communication => ["integration", "communication"],
        OperatorCategoryId.OutputAndAuxiliary => ["output", "auxiliary"],
        _ => ["general"]
    };

    private static List<string> BuildScenarioTags(OperatorMetadata metadata)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullText = $"{metadata.DisplayName} {metadata.Description} {metadata.Category}";

        if (ContainsAny(fullText, ["线序", "端子", "connector", "wire"]))
            result.Add("wire_sequence");
        if (ContainsAny(fullText, ["缺陷", "外观", "defect", "appearance"]))
            result.Add("appearance_inspection");
        if (ContainsAny(fullText, ["间距", "距离", "measurement", "gap"]))
            result.Add("measurement");
        if (ContainsAny(fullText, ["遥控器", "presence", "missing", "有无"]))
            result.Add("presence_check");

        if (result.Count == 0)
            result.Add("general");

        return result.ToList();
    }

    private static List<string> BuildKnownLimitations(
        OperatorMetadata metadata,
        QualityOperatorEvidence evidence)
    {
        var limitations = new List<string>();

        if (OperatorLifecyclePolicy.RequiresDisclosure(metadata.Lifecycle))
        {
            limitations.Add(string.IsNullOrWhiteSpace(metadata.LifecycleNote)
                ? $"生命周期状态：{metadata.Lifecycle}"
                : $"生命周期状态：{metadata.Lifecycle}；{metadata.LifecycleNote}");
        }

        var imageContract = ImageContractPresentationBuilder.Summarize(metadata.ImageInputContracts);
        if (imageContract.CompatibilityOnly)
        {
            limitations.Add(ImageContractPresentationBuilder.LegacyCompatibilityNotice);
        }
        else if (imageContract.LegacyCompatibilityVariantCount > 0)
        {
            limitations.Add(
                $"{ImageContractPresentationBuilder.LegacyCompatibilityNotice}; " +
                "only variants explicitly marked VerifiedSupport or VerifiedConversion count as production support.");
        }

        if (imageContract.ContractCount > 0 &&
            !imageContract.HasProductionSupport &&
            imageContract.LegacyCompatibilityVariantCount == 0)
        {
            limitations.Add("Image input contract is Unknown; no verified executable support is registered.");
        }

        if (evidence.IndustrialStatus.Contains("未完成现场工业验证", StringComparison.OrdinalIgnoreCase))
        {
            limitations.Add("功能可用但未完成现场工业验证");
        }

        if (evidence.Dataset.Contains("Not yet evidenced", StringComparison.OrdinalIgnoreCase))
        {
            limitations.Add("缺少数据集层证据");
        }

        if (evidence.FieldReplay.Contains("Not yet evidenced", StringComparison.OrdinalIgnoreCase))
        {
            limitations.Add("缺少现场回放证据");
        }

        if (metadata.QualityState.ProductionReadiness == OperatorProductionReadiness.Unknown)
        {
            limitations.Add("ProductionReadiness=Unknown，不得据此宣称 Release Ready");
        }

        if (metadata.QualityState.FieldValidation == OperatorFieldValidation.NotValidated)
        {
            limitations.Add("FieldValidation=NotValidated，不得据此宣称 Field Verified");
        }

        return limitations;
    }

    private static bool ContainsAny(string source, IEnumerable<string> terms)
    {
        return terms.Any(term => source.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTag(string tag)
    {
        return tag.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static IReadOnlyDictionary<string, QualityOperatorEvidence> LoadEvidenceByOperatorType()
    {
        var manifestPath = ResolveManifestPath();
        if (!File.Exists(manifestPath))
            return new Dictionary<string, QualityOperatorEvidence>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<QualityManifest>(json, JsonOptions);
            if (manifest?.Operators == null)
                return new Dictionary<string, QualityOperatorEvidence>(StringComparer.OrdinalIgnoreCase);

            return manifest.Operators
                .Where(item => !string.IsNullOrWhiteSpace(item.OperatorType))
                .GroupBy(item => NormalizeOperatorType(item.OperatorType), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new QualityOperatorEvidence
                    {
                        OperatorType = group.Key,
                        DisplayName = group.First().DisplayName ?? string.Empty,
                        Contract = group.First().Contract ?? "Not yet evidenced",
                        Golden = group.First().Golden ?? "Not yet evidenced",
                        Dataset = group.First().Dataset ?? "Not yet evidenced",
                        FieldReplay = group.First().FieldReplay ?? "Not yet evidenced",
                        PrecisionClaim = group.First().PrecisionClaim ?? "Not yet evidenced",
                        IndustrialStatus = group.First().IndustrialStatus ?? "功能可用但未完成现场工业验证",
                        Priority = group.First().Priority
                    },
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, QualityOperatorEvidence>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ResolveManifestPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && current != null; depth++)
        {
            var candidate = Path.Combine(current.FullName, QualityManifestPath);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", QualityManifestPath));
    }

    private static string NormalizeOperatorType(string raw)
    {
        var value = raw.Trim();
        const string prefix = "OperatorType.";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return value[prefix.Length..];
        return value;
    }

    private sealed class TemplateGraphSnapshot
    {
        public List<TemplateOperatorNode> Operators { get; set; } = new();
        public List<TemplateConnectionNode> Connections { get; set; } = new();
    }

    private sealed class TemplateOperatorNode
    {
        public string TempId { get; set; } = string.Empty;
        public string OperatorType { get; set; } = string.Empty;
    }

    private sealed class TemplateConnectionNode
    {
        public string SourceTempId { get; set; } = string.Empty;
        public string SourcePortName { get; set; } = string.Empty;
        public string TargetTempId { get; set; } = string.Empty;
        public string TargetPortName { get; set; } = string.Empty;
    }

    private sealed class QualityManifest
    {
        public List<QualityManifestOperator> Operators { get; set; } = new();
    }

    private sealed class QualityManifestOperator
    {
        public string OperatorType { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Priority { get; set; }
        public string? Contract { get; set; }
        public string? Golden { get; set; }
        public string? Dataset { get; set; }
        public string? FieldReplay { get; set; }
        public string? PrecisionClaim { get; set; }
        public string? IndustrialStatus { get; set; }
    }

    private sealed class QualityOperatorEvidence
    {
        public string OperatorType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Contract { get; set; } = "Not yet evidenced";
        public string Golden { get; set; } = "Not yet evidenced";
        public string Dataset { get; set; } = "Not yet evidenced";
        public string FieldReplay { get; set; } = "Not yet evidenced";
        public string PrecisionClaim { get; set; } = "Not yet evidenced";
        public string IndustrialStatus { get; set; } = "功能可用但未完成现场工业验证";
        public string? Priority { get; set; }

        public static QualityOperatorEvidence CreateDefault(string operatorType, string displayName)
        {
            return new QualityOperatorEvidence
            {
                OperatorType = operatorType,
                DisplayName = displayName
            };
        }
    }
}
