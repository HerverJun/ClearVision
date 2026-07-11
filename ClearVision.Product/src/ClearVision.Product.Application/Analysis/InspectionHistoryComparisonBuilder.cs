using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Core.ResultPaths;

namespace ClearVision.Product.Application.Analysis;

public static class InspectionHistoryComparisonBuilder
{
    private const int MaxPreviewDiffFields = 128;
    private const int MaxValuePreviewChars = 256;
    private const string MissingLeftMessage = "旧数据未记录";
    private const string MissingRightMessage = "本次结果未记录";
    private const string SafePreviewOnlyWarning = "仅比较安全预览字段";
    private const string FlowMismatchWarning = "流程版本不一致，对比仅供参考";
    private const string BundleMismatchWarning = "标定资产不一致，空间坐标对比可能无效";
    private const string NoSceneEvidenceMessage = "暂无 Scene evidence，已降级为摘要回放";

    public static InspectionHistoryComparison Build(InspectionHistoryDetail left, InspectionHistoryDetail right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftOutputPreview = SafeJsonPreviewBuilder.Build(left.OutputDataJson);
        var rightOutputPreview = SafeJsonPreviewBuilder.Build(right.OutputDataJson);
        var leftAnalysisPreview = SafeJsonPreviewBuilder.Build(left.AnalysisDataJson);
        var rightAnalysisPreview = SafeJsonPreviewBuilder.Build(right.AnalysisDataJson);

        var warnings = new List<string>();
        var fieldDiffs = new List<InspectionHistoryFieldDiff>();
        var traceabilityDiffs = new List<InspectionHistoryFieldDiff>();

        var leftOutcome = ResolveOutcome(left);
        var rightOutcome = ResolveOutcome(right);
        fieldDiffs.Add(CompareField(Path("outcome", "execution"), "executionOutcome", leftOutcome.Execution, rightOutcome.Execution));
        fieldDiffs.Add(CompareField(Path("outcome", "decision"), "decisionOutcome", leftOutcome.Decision, rightOutcome.Decision));
        fieldDiffs.Add(CompareField(Path("defectCount"), "defectCount", left.Defects.Count, right.Defects.Count));
        fieldDiffs.Add(CompareField(Path("processingTimeMs"), "processingTimeMs", left.ProcessingTimeMs, right.ProcessingTimeMs));
        fieldDiffs.Add(CompareField(Path("confidenceScore"), "confidenceScore", left.ConfidenceScore, right.ConfidenceScore));
        fieldDiffs.Add(CompareField(
            Path("diagnostics", "code"),
            "diagnostics code",
            GetDiagnosticCode(left),
            GetDiagnosticCode(right)));
        fieldDiffs.Add(CompareField(
            Path("diagnostics", "message"),
            "diagnostics message",
            left.ErrorMessage,
            right.ErrorMessage));

        var flowDiff = CompareField(
            Path("traceability", "flowVersionHash"),
            "FlowVersionHash",
            left.FlowVersionHash,
            right.FlowVersionHash,
            severity: "warning",
            incompatibleWhenChanged: true,
            incompatibleMessage: FlowMismatchWarning);
        traceabilityDiffs.Add(flowDiff);
        if (flowDiff.DiffType == "Incompatible")
        {
            warnings.Add(FlowMismatchWarning);
        }

        var bundleDiff = CompareField(
            Path("traceability", "calibrationBundleId"),
            "CalibrationBundleId",
            left.CalibrationBundleId,
            right.CalibrationBundleId,
            severity: "warning",
            incompatibleWhenChanged: true,
            incompatibleMessage: BundleMismatchWarning);
        traceabilityDiffs.Add(bundleDiff);
        if (bundleDiff.DiffType == "Incompatible")
        {
            warnings.Add(BundleMismatchWarning);
        }

        traceabilityDiffs.Add(CompareField(
            Path("traceability", "sessionId"),
            "SessionId / RunId",
            left.SessionId,
            right.SessionId));

        AddDefectSummaryDiffs(left, right, fieldDiffs);
        AddPreviewDiffs("outputDataPreview", "输出数据", leftOutputPreview, rightOutputPreview, fieldDiffs, warnings);
        AddPreviewDiffs("analysisDataPreview", "分析数据", leftAnalysisPreview, rightAnalysisPreview, fieldDiffs, warnings);

        var allDiffs = traceabilityDiffs.Concat(fieldDiffs).ToArray();
        return new InspectionHistoryComparison
        {
            LeftSummary = BuildSummary(left),
            RightSummary = BuildSummary(right),
            Compatibility = new InspectionHistoryCompatibility
            {
                FlowVersionCompatible = flowDiff.DiffType != "Incompatible",
                CalibrationBundleCompatible = bundleDiff.DiffType != "Incompatible",
                OnlySafePreviewComparison = UsesSafePreviewOnly(
                    leftOutputPreview,
                    rightOutputPreview,
                    leftAnalysisPreview,
                    rightAnalysisPreview),
                HasUnknownFields = allDiffs.Any(diff => diff.DiffType == "Unknown")
            },
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            FieldDiffs = fieldDiffs.ToArray(),
            TraceabilityDiff = traceabilityDiffs.ToArray(),
            SceneReplayAvailability = BuildSceneReplayAvailability(left, right),
            ImageReplayAvailability = BuildImageReplayAvailability(left, right)
        };
    }

    public static InspectionPreviousSuccessReference BuildPreviousSuccessReference(
        InspectionHistoryDetail current,
        InspectionHistoryDetail? reference,
        int queryLimit,
        bool isFlowVersionFallback)
    {
        ArgumentNullException.ThrowIfNull(current);

        var warnings = new List<string>();
        if (isFlowVersionFallback)
        {
            warnings.Add(FlowMismatchWarning);
        }

        return new InspectionPreviousSuccessReference
        {
            CurrentSummary = BuildSummary(current),
            ReferenceSummary = reference == null ? null : BuildSummary(reference),
            Found = reference != null,
            IsFlowVersionFallback = isFlowVersionFallback,
            QueryLimit = Math.Clamp(queryLimit, 1, 200),
            Warnings = warnings,
            Message = reference == null
                ? "未找到失败前成功参考"
                : (isFlowVersionFallback ? FlowMismatchWarning : "已找到失败前成功参考")
        };
    }

    public static InspectionHistoryComparisonSummary BuildSummary(InspectionHistoryDetail result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var outcome = ResolveOutcome(result);
        return new InspectionHistoryComparisonSummary
        {
            ResultId = result.Id,
            ProjectId = result.ProjectId,
            Status = result.Status,
            ExecutionOutcome = outcome.Execution,
            DecisionOutcome = outcome.Decision,
            InspectionTime = result.InspectionTime,
            DefectCount = result.Defects.Count,
            ProcessingTimeMs = result.ProcessingTimeMs,
            ConfidenceScore = result.ConfidenceScore,
            FlowVersionHash = result.FlowVersionHash,
            CalibrationBundleId = result.CalibrationBundleId,
            SessionId = result.SessionId,
            ImageId = result.ImageId,
            ImageReference = BuildImageReference(result.ImageId),
            HasImage = result.HasImage,
            HasOutputData = result.HasOutputData,
            HasAnalysisData = result.HasAnalysisData
        };
    }

    private static InspectionHistoryFieldDiff CompareField(
        string path,
        string label,
        object? leftValue,
        object? rightValue,
        string severity = "info",
        bool incompatibleWhenChanged = false,
        string? incompatibleMessage = null)
    {
        var leftMissing = IsMissing(leftValue);
        var rightMissing = IsMissing(rightValue);

        if (leftMissing && rightMissing)
        {
            return new InspectionHistoryFieldDiff
            {
                Path = path,
                Label = label,
                LeftValuePreview = MissingLeftMessage,
                RightValuePreview = MissingRightMessage,
                DiffType = "Same",
                Severity = "info"
            };
        }

        if (leftMissing)
        {
            return new InspectionHistoryFieldDiff
            {
                Path = path,
                Label = label,
                LeftValuePreview = MissingLeftMessage,
                RightValuePreview = ToPreviewString(rightValue),
                DiffType = "Added",
                Severity = severity,
                Message = MissingLeftMessage
            };
        }

        if (rightMissing)
        {
            return new InspectionHistoryFieldDiff
            {
                Path = path,
                Label = label,
                LeftValuePreview = ToPreviewString(leftValue),
                RightValuePreview = MissingRightMessage,
                DiffType = "Removed",
                Severity = severity,
                Message = MissingRightMessage
            };
        }

        var left = ToPreviewString(leftValue);
        var right = ToPreviewString(rightValue);
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return new InspectionHistoryFieldDiff
            {
                Path = path,
                Label = label,
                LeftValuePreview = left,
                RightValuePreview = right,
                DiffType = "Same",
                Severity = "info"
            };
        }

        return new InspectionHistoryFieldDiff
        {
            Path = path,
            Label = label,
            LeftValuePreview = left,
            RightValuePreview = right,
            DiffType = incompatibleWhenChanged ? "Incompatible" : "Changed",
            Severity = incompatibleWhenChanged ? "warning" : severity,
            Message = incompatibleWhenChanged ? incompatibleMessage : null
        };
    }

    private static void AddDefectSummaryDiffs(
        InspectionHistoryDetail left,
        InspectionHistoryDetail right,
        List<InspectionHistoryFieldDiff> fieldDiffs)
    {
        var leftSummary = SummarizeDefects(left.Defects);
        var rightSummary = SummarizeDefects(right.Defects);
        foreach (var type in leftSummary.Keys.Union(rightSummary.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            leftSummary.TryGetValue(type, out var leftDefect);
            rightSummary.TryGetValue(type, out var rightDefect);
            var segment = SanitizePathKey(type);

            fieldDiffs.Add(CompareField(
                Path("defectSummary", segment, "count"),
                $"defect summary：{type} count",
                leftDefect?.Count,
                rightDefect?.Count));
            fieldDiffs.Add(CompareField(
                Path("defectSummary", segment, "confidence"),
                $"defect summary：{type} confidence",
                leftDefect?.AverageConfidence,
                rightDefect?.AverageConfidence));
            fieldDiffs.Add(CompareField(
                Path("defectSummary", segment, "bbox"),
                $"defect summary：{type} bbox summary",
                leftDefect?.BoundingBoxSummary,
                rightDefect?.BoundingBoxSummary));
        }
    }

    private static void AddPreviewDiffs(
        string rootKey,
        string label,
        SafeJsonPreview leftPreview,
        SafeJsonPreview rightPreview,
        List<InspectionHistoryFieldDiff> fieldDiffs,
        List<string> warnings)
    {
        if (leftPreview.WasTruncated || rightPreview.WasTruncated)
        {
            warnings.Add(SafePreviewOnlyWarning);
        }

        if ((leftPreview.IsPresent && !leftPreview.IsJson) || (rightPreview.IsPresent && !rightPreview.IsJson))
        {
            fieldDiffs.Add(new InspectionHistoryFieldDiff
            {
                Path = Path(rootKey),
                Label = label,
                LeftValuePreview = PreviewStateDescription(leftPreview),
                RightValuePreview = PreviewStateDescription(rightPreview),
                DiffType = "Unknown",
                Severity = "warning",
                Message = "Stored JSON payload could not be parsed; " + SafePreviewOnlyWarning
            });
            warnings.Add(SafePreviewOnlyWarning);
            return;
        }

        var leftFields = FlattenPreview(rootKey, leftPreview);
        var rightFields = FlattenPreview(rootKey, rightPreview);
        foreach (var path in leftFields.Keys.Union(rightFields.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            leftFields.TryGetValue(path, out var leftValue);
            rightFields.TryGetValue(path, out var rightValue);
            fieldDiffs.Add(CompareField(path, path, leftValue, rightValue));
        }
    }

    private static Dictionary<string, string> FlattenPreview(string rootKey, SafeJsonPreview preview)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!preview.IsPresent || !preview.IsJson || preview.Value == null)
        {
            return output;
        }

        FlattenValue(preview.Value, [ResultPathSegment.ObjectKey(rootKey)], output);
        return output;
    }

    private static void FlattenValue(object? value, List<ResultPathSegment> segments, Dictionary<string, string> output)
    {
        if (output.Count >= MaxPreviewDiffFields)
        {
            return;
        }

        switch (value)
        {
            case null:
                output[ResultPathFormatter.Format(segments)] = "null";
                return;
            case IDictionary<string, object?> dictionary:
                if (dictionary.Count == 0)
                {
                    output[ResultPathFormatter.Format(segments)] = "{}";
                    return;
                }

                foreach (var (key, child) in dictionary
                             .Where(pair => !pair.Key.StartsWith("__", StringComparison.Ordinal))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (output.Count >= MaxPreviewDiffFields)
                    {
                        return;
                    }

                    var nextSegments = new List<ResultPathSegment>(segments)
                    {
                        ResultPathSegment.ObjectKey(key)
                    };
                    FlattenValue(child, nextSegments, output);
                }

                return;
            case IReadOnlyDictionary<string, object?> dictionary:
                if (dictionary.Count == 0)
                {
                    output[ResultPathFormatter.Format(segments)] = "{}";
                    return;
                }

                foreach (var pair in dictionary
                             .Where(pair => !pair.Key.StartsWith("__", StringComparison.Ordinal))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (output.Count >= MaxPreviewDiffFields)
                    {
                        return;
                    }

                    var nextSegments = new List<ResultPathSegment>(segments)
                    {
                        ResultPathSegment.ObjectKey(pair.Key)
                    };
                    FlattenValue(pair.Value, nextSegments, output);
                }

                return;
            case IList<object?> list:
                if (list.Count == 0)
                {
                    output[ResultPathFormatter.Format(segments)] = "[]";
                    return;
                }

                for (var index = 0; index < list.Count && output.Count < MaxPreviewDiffFields; index++)
                {
                    var nextSegments = new List<ResultPathSegment>(segments)
                    {
                        ResultPathSegment.Index(index)
                    };
                    FlattenValue(list[index], nextSegments, output);
                }

                return;
            case IEnumerable<object?> enumerable:
                var items = enumerable.ToList();
                if (items.Count == 0)
                {
                    output[ResultPathFormatter.Format(segments)] = "[]";
                    return;
                }

                for (var index = 0; index < items.Count && output.Count < MaxPreviewDiffFields; index++)
                {
                    var nextSegments = new List<ResultPathSegment>(segments)
                    {
                        ResultPathSegment.Index(index)
                    };
                    FlattenValue(items[index], nextSegments, output);
                }

                return;
            default:
                output[ResultPathFormatter.Format(segments)] = ToPreviewString(value);
                return;
        }
    }

    private static InspectionHistoryReplayAvailability BuildImageReplayAvailability(
        InspectionHistoryDetail left,
        InspectionHistoryDetail right)
    {
        var leftReference = BuildImageReference(left.ImageId);
        var rightReference = BuildImageReference(right.ImageId);
        var leftAvailable = !string.IsNullOrWhiteSpace(leftReference);
        var rightAvailable = !string.IsNullOrWhiteSpace(rightReference);
        var anyAvailable = leftAvailable || rightAvailable;
        var hasMissingImage = (left.HasImage && !leftAvailable) || (right.HasImage && !rightAvailable);

        return new InspectionHistoryReplayAvailability
        {
            Kind = "image",
            Mode = anyAvailable ? "image-reference" : "summary-only",
            IsAvailable = anyAvailable,
            LeftAvailable = leftAvailable,
            RightAvailable = rightAvailable,
            LeftReference = leftReference,
            RightReference = rightReference,
            LeftSummary = leftAvailable ? "imageReference" : (left.HasImage ? "image missing" : "no image"),
            RightSummary = rightAvailable ? "imageReference" : (right.HasImage ? "image missing" : "no image"),
            Message = hasMissingImage
                ? "图像缺失，已降级为摘要回放"
                : (anyAvailable ? "图像引用可用" : "无图像引用，已降级为摘要回放")
        };
    }

    private static InspectionHistoryReplayAvailability BuildSceneReplayAvailability(
        InspectionHistoryDetail left,
        InspectionHistoryDetail right)
    {
        var leftScene = SceneEvidenceInfo.Merge(
            InspectSceneEvidence(left.OutputDataJson),
            InspectSceneEvidence(left.AnalysisDataJson));
        var rightScene = SceneEvidenceInfo.Merge(
            InspectSceneEvidence(right.OutputDataJson),
            InspectSceneEvidence(right.AnalysisDataJson));
        var anyAvailable = leftScene.HasEvidence || rightScene.HasEvidence;
        var allAvailable = leftScene.HasEvidence && rightScene.HasEvidence;

        return new InspectionHistoryReplayAvailability
        {
            Kind = "scene",
            Mode = anyAvailable ? "scene-summary" : "summary-only",
            IsAvailable = anyAvailable,
            LeftAvailable = leftScene.HasEvidence,
            RightAvailable = rightScene.HasEvidence,
            LeftSummary = leftScene.ToDisplaySummary(),
            RightSummary = rightScene.ToDisplaySummary(),
            Message = allAvailable
                ? "Scene evidence 摘要可用，来自正式结果已有 evidence"
                : NoSceneEvidenceMessage
        };
    }

    private static SceneEvidenceInfo InspectSceneEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SceneEvidenceInfo.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var state = new SceneEvidenceAccumulator();
            InspectSceneElement(document.RootElement, propertyName: null, depth: 0, state);
            return state.ToInfo();
        }
        catch (JsonException)
        {
            return SceneEvidenceInfo.Empty;
        }
    }

    private static void InspectSceneElement(
        JsonElement element,
        string? propertyName,
        int depth,
        SceneEvidenceAccumulator state)
    {
        if (depth > 8 || state.VisitedNodes >= 256)
        {
            state.Truncated = true;
            return;
        }

        state.VisitedNodes++;

        if (!string.IsNullOrWhiteSpace(propertyName) &&
            propertyName.Contains("scene", StringComparison.OrdinalIgnoreCase))
        {
            state.HasEvidence = true;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("schemaVersion") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var schema = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(schema) &&
                            schema.Contains("scene", StringComparison.OrdinalIgnoreCase))
                        {
                            state.HasEvidence = true;
                            state.SchemaVersion ??= schema;
                        }
                    }

                    if (property.NameEquals("primitives") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        state.HasEvidence = true;
                        state.PrimitiveCount ??= property.Value.GetArrayLength();
                    }

                    InspectSceneElement(property.Value, property.Name, depth + 1, state);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    InspectSceneElement(item, propertyName, depth + 1, state);
                }

                break;
        }
    }

    private static Dictionary<string, DefectSummary> SummarizeDefects(IReadOnlyList<InspectionHistoryDefectItem> defects)
    {
        return defects
            .GroupBy(defect => defect.Type.ToString(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var items = group.ToArray();
                    var minX = items.Min(item => item.X);
                    var minY = items.Min(item => item.Y);
                    var maxX = items.Max(item => item.X + item.Width);
                    var maxY = items.Max(item => item.Y + item.Height);
                    return new DefectSummary(
                        items.Length,
                        Math.Round(items.Average(item => item.ConfidenceScore), 4),
                        FormattableString.Invariant($"x={minX:0.###},y={minY:0.###},w={maxX - minX:0.###},h={maxY - minY:0.###}"));
                },
                StringComparer.Ordinal);
    }

    private static bool UsesSafePreviewOnly(params SafeJsonPreview[] previews) =>
        previews.Any(preview => preview.WasTruncated || preview.WasRedacted || (preview.IsPresent && !preview.IsJson));

    private static string PreviewStateDescription(SafeJsonPreview preview)
    {
        if (!preview.IsPresent)
        {
            return MissingLeftMessage;
        }

        if (!preview.IsJson)
        {
            return preview.Error ?? "MalformedJson";
        }

        return preview.WasTruncated ? SafePreviewOnlyWarning : "JSON";
    }

    private static string? GetDiagnosticCode(InspectionHistoryDetail result) =>
        ResolveOutcome(result).ReasonCode;

    private static InspectionOutcome ResolveOutcome(InspectionHistoryDetail result) =>
        result.ExecutionOutcome.HasValue && result.DecisionOutcome.HasValue
            ? new InspectionOutcome(
                result.ExecutionOutcome.Value,
                result.DecisionOutcome.Value,
                result.DecisionSource,
                result.ReasonCode,
                result.ErrorMessage,
                result.HasJudgmentSignal ?? false)
            : LegacyInspectionStatusProjection.FromLegacy(result.Status) with { Message = result.ErrorMessage };

    private static bool IsMissing(object? value) =>
        value == null ||
        (value is string text && string.IsNullOrWhiteSpace(text));

    private static string ToPreviewString(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            double number => number.ToString("0.####", CultureInfo.InvariantCulture),
            float number => number.ToString("0.####", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        return text.Length <= MaxValuePreviewChars
            ? text
            : text[..MaxValuePreviewChars] + "...";
    }

    private static string Path(params string[] keys) =>
        ResultPathFormatter.Format(keys.Select(ResultPathSegment.ObjectKey).ToArray());

    private static string SanitizePathKey(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static string? BuildImageReference(Guid? imageId) =>
        imageId.HasValue ? $"/api/images/{imageId.Value:D}" : null;

    private sealed record DefectSummary(int Count, double AverageConfidence, string BoundingBoxSummary);

    private sealed class SceneEvidenceAccumulator
    {
        public bool HasEvidence { get; set; }

        public string? SchemaVersion { get; set; }

        public int? PrimitiveCount { get; set; }

        public bool Truncated { get; set; }

        public int VisitedNodes { get; set; }

        public SceneEvidenceInfo ToInfo() => new(HasEvidence, SchemaVersion, PrimitiveCount, Truncated);
    }

    private sealed record SceneEvidenceInfo(
        bool HasEvidence,
        string? SchemaVersion,
        int? PrimitiveCount,
        bool Truncated)
    {
        public static SceneEvidenceInfo Empty { get; } = new(false, null, null, false);

        public static SceneEvidenceInfo Merge(SceneEvidenceInfo left, SceneEvidenceInfo right) =>
            new(
                left.HasEvidence || right.HasEvidence,
                left.SchemaVersion ?? right.SchemaVersion,
                left.PrimitiveCount ?? right.PrimitiveCount,
                left.Truncated || right.Truncated);

        public string ToDisplaySummary()
        {
            if (!HasEvidence)
            {
                return NoSceneEvidenceMessage;
            }

            var parts = new List<string> { "formal Scene evidence" };
            if (!string.IsNullOrWhiteSpace(SchemaVersion))
            {
                parts.Add(SchemaVersion);
            }

            if (PrimitiveCount.HasValue)
            {
                parts.Add($"primitives={PrimitiveCount.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            if (Truncated)
            {
                parts.Add("bounded summary");
            }

            return string.Join("; ", parts);
        }
    }
}
