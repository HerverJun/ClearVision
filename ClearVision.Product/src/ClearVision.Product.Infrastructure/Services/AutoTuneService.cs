// AutoTuneService.cs
// 自动调参服务实现
// 【Phase 4】LLM 闭环验证 - 自动调参服务
// 作者：架构修复方案 v2

using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// 自动调参服务实现
/// 仅保留有正式消费者的线序场景白名单调参。
/// </summary>
public class AutoTuneService : IAutoTuneService
{
    private const string WireSequenceScenarioKey = "wire-sequence-terminal";

    private readonly ILogger<AutoTuneService> _logger;
    private readonly IFlowNodePreviewService _flowNodePreviewService;

    public AutoTuneService(
        ILogger<AutoTuneService> logger,
        IFlowNodePreviewService flowNodePreviewService)
    {
        _logger = logger;
        _flowNodePreviewService = flowNodePreviewService;
    }

    /// <inheritdoc />
    public async Task<ScenarioAutoTuneResult> AutoTuneScenarioAsync(
        string scenarioKey,
        OperatorFlow flow,
        byte[] inputImage,
        AutoTuneGoal goal,
        Guid projectId,
        long persistenceRevision,
        ExecutionRequestAuthority authority,
        int maxIterations = 5,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var sw = Stopwatch.StartNew();
        var iterations = new List<AutoTuneIteration>();
        var normalizedScenarioKey = scenarioKey?.Trim() ?? string.Empty;

        if (!string.Equals(normalizedScenarioKey, WireSequenceScenarioKey, StringComparison.OrdinalIgnoreCase))
        {
            return new ScenarioAutoTuneResult
            {
                Success = false,
                ScenarioKey = normalizedScenarioKey,
                ErrorMessage = $"暂不支持场景: {scenarioKey}"
            };
        }

        var judgeOperator = flow.Operators.FirstOrDefault(item => item.Type == OperatorType.DetectionSequenceJudge);
        if (judgeOperator == null)
        {
            return new ScenarioAutoTuneResult
            {
                Success = false,
                ScenarioKey = normalizedScenarioKey,
                ErrorMessage = "线序场景缺少 DetectionSequenceJudge 节点。"
            };
        }

        var boxNmsOperator = FindClosestUpstreamOperator(flow, judgeOperator.Id, OperatorType.BoxNms);
        var modelOperator = boxNmsOperator == null
            ? FindClosestUpstreamOperator(flow, judgeOperator.Id, OperatorType.DeepLearning)
            : null;
        var tuningOperator = boxNmsOperator ?? modelOperator;
        if (tuningOperator == null)
        {
            return new ScenarioAutoTuneResult
            {
                Success = false,
                ScenarioKey = normalizedScenarioKey,
                ErrorMessage = "线序场景缺少可调的 DeepLearning 或 BoxNms 节点。"
            };
        }

        maxIterations = Math.Clamp(maxIterations, 1, 5);
        var useModelConfidenceTuning = boxNmsOperator == null;
        var currentParameters = useModelConfidenceTuning
            ? CaptureWireSequenceModelParameters(tuningOperator)
            : CaptureWireSequenceParameters(tuningOperator);
        var bestParameters = new Dictionary<string, object>(currentParameters);
        FlowNodePreviewWithMetricsResult? bestPreview = null;
        double bestScore = double.MinValue;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var iterationStopwatch = Stopwatch.StartNew();

            if (useModelConfidenceTuning)
            {
                ApplyWireSequenceModelParameters(tuningOperator, currentParameters);
            }
            else
            {
                ApplyWireSequenceParameters(tuningOperator, currentParameters);
            }

            var preview = await _flowNodePreviewService.PreviewWithMetricsAsync(
                flow,
                judgeOperator.Id,
                inputImage,
                projectId,
                persistenceRevision,
                authority,
                projectVariables: null,
                ct: ct);

            var metrics = preview.Metrics ?? new PreviewMetrics();
            var score = metrics.OverallScore;
            iterations.Add(new AutoTuneIteration
            {
                Iteration = iteration,
                Parameters = new Dictionary<string, object>(currentParameters),
                Metrics = metrics,
                Score = score,
                ExecutionTimeMs = iterationStopwatch.ElapsedMilliseconds
            });

            if (preview.MissingResources.Count > 0)
            {
                return new ScenarioAutoTuneResult
                {
                    Success = false,
                    ScenarioKey = normalizedScenarioKey,
                    Iterations = iterations,
                    TotalIterations = iterations.Count,
                    TotalExecutionTimeMs = sw.ElapsedMilliseconds,
                    ErrorMessage = "线序场景缺少必要资源，自动调参已终止。",
                    DiagnosticCodes = preview.DiagnosticCodes,
                    MissingResources = preview.MissingResources,
                    FinalPreview = preview,
                    FinalParameters = new Dictionary<string, object>(currentParameters)
                };
            }

            if (score > bestScore || bestPreview == null)
            {
                bestScore = score;
                bestParameters = new Dictionary<string, object>(currentParameters);
                bestPreview = preview;
            }

            if (IsWireSequenceGoalAchieved(preview))
            {
                return new ScenarioAutoTuneResult
                {
                    Success = true,
                    ScenarioKey = normalizedScenarioKey,
                    FinalParameters = new Dictionary<string, object>(currentParameters),
                    Iterations = iterations,
                    TotalIterations = iterations.Count,
                    TotalExecutionTimeMs = sw.ElapsedMilliseconds,
                    IsGoalAchieved = true,
                    DiagnosticCodes = preview.DiagnosticCodes,
                    FinalPreview = preview
                };
            }

            var adjustedParameters = AdjustWireSequenceParameters(currentParameters, preview);
            if (adjustedParameters == null)
            {
                break;
            }

            currentParameters = adjustedParameters;
        }

        return new ScenarioAutoTuneResult
        {
            Success = false,
            ScenarioKey = normalizedScenarioKey,
            FinalParameters = bestParameters,
            Iterations = iterations,
            TotalIterations = iterations.Count,
            TotalExecutionTimeMs = sw.ElapsedMilliseconds,
            IsGoalAchieved = false,
            ErrorMessage = "未能在限定轮次内收敛到线序目标。",
            DiagnosticCodes = bestPreview?.DiagnosticCodes ?? new List<string>(),
            MissingResources = bestPreview?.MissingResources ?? new List<PreviewMissingResource>(),
            FinalPreview = bestPreview
        };
    }

    #region 线序场景辅助方法

    private static Operator? FindClosestUpstreamOperator(OperatorFlow flow, Guid targetNodeId, OperatorType type)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(targetNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var connection in flow.Connections.Where(item => item.TargetOperatorId == current))
            {
                var sourceOperator = flow.Operators.FirstOrDefault(item => item.Id == connection.SourceOperatorId);
                if (sourceOperator == null)
                {
                    continue;
                }

                if (sourceOperator.Type == type)
                {
                    return sourceOperator;
                }

                queue.Enqueue(sourceOperator.Id);
            }
        }

        return null;
    }

    private static Dictionary<string, object> CaptureWireSequenceParameters(Operator boxNmsOperator)
    {
        return new Dictionary<string, object>
        {
            ["BoxNms.ScoreThreshold"] = GetOperatorParameterValue(boxNmsOperator, "ScoreThreshold", 0.25d),
            ["BoxNms.IouThreshold"] = GetOperatorParameterValue(boxNmsOperator, "IouThreshold", 0.45d)
        };
    }

    private static Dictionary<string, object> CaptureWireSequenceModelParameters(Operator deepLearningOperator)
    {
        return new Dictionary<string, object>
        {
            ["DeepLearning.Confidence"] = GetOperatorParameterValue(deepLearningOperator, "Confidence", 0.05d)
        };
    }

    private static void ApplyWireSequenceParameters(Operator boxNmsOperator, Dictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("BoxNms.ScoreThreshold", out var scoreThreshold))
        {
            SetOperatorParameterValue(boxNmsOperator, "ScoreThreshold", scoreThreshold);
        }

        if (parameters.TryGetValue("BoxNms.IouThreshold", out var iouThreshold))
        {
            SetOperatorParameterValue(boxNmsOperator, "IouThreshold", iouThreshold);
        }
    }

    private static void ApplyWireSequenceModelParameters(Operator deepLearningOperator, Dictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("DeepLearning.Confidence", out var confidence))
        {
            SetOperatorParameterValue(deepLearningOperator, "Confidence", confidence);
        }
    }

    private static Dictionary<string, object>? AdjustWireSequenceParameters(
        Dictionary<string, object> currentParameters,
        FlowNodePreviewWithMetricsResult preview)
    {
        if (currentParameters.ContainsKey("DeepLearning.Confidence"))
        {
            return AdjustWireSequenceModelParameters(currentParameters, preview);
        }

        var diagnostics = new HashSet<string>(preview.DiagnosticCodes, StringComparer.OrdinalIgnoreCase);
        var nextParameters = new Dictionary<string, object>(currentParameters);
        var changed = false;

        if (diagnostics.Contains("duplicate_detected_class"))
        {
            var currentIou = ReadDoubleValue(currentParameters, "BoxNms.IouThreshold", 0.45d);
            var tightenedIou = Math.Max(0.10d, Math.Round(currentIou - 0.05d, 3));
            if (Math.Abs(tightenedIou - currentIou) > 0.0001d)
            {
                nextParameters["BoxNms.IouThreshold"] = tightenedIou;
                changed = true;
            }
        }

        if (diagnostics.Contains("missing_expected_class") ||
            diagnostics.Contains("detection_count_mismatch") ||
            diagnostics.Contains("low_detection_confidence"))
        {
            var summary = DetectionOutputInspector.Inspect(preview.Outputs);
            var currentScore = ReadDoubleValue(currentParameters, "BoxNms.ScoreThreshold", 0.25d);
            var expectedCount = summary.ExpectedCount ?? summary.ExpectedLabels.Count;
            var actualCount = summary.DetectionCount > 0 ? summary.DetectionCount : (summary.DeclaredCount ?? 0);
            var lowerThreshold = actualCount < expectedCount ||
                (summary.MinConfidence.HasValue && summary.MinConfidence.Value < summary.RequiredMinConfidence);
            var newScore = lowerThreshold
                ? Math.Max(0.05d, Math.Round(currentScore - 0.05d, 3))
                : Math.Min(0.80d, Math.Round(currentScore + 0.05d, 3));

            if (Math.Abs(newScore - currentScore) > 0.0001d)
            {
                nextParameters["BoxNms.ScoreThreshold"] = newScore;
                changed = true;
            }
        }

        return changed ? nextParameters : null;
    }

    private static Dictionary<string, object>? AdjustWireSequenceModelParameters(
        Dictionary<string, object> currentParameters,
        FlowNodePreviewWithMetricsResult preview)
    {
        var diagnostics = new HashSet<string>(preview.DiagnosticCodes, StringComparer.OrdinalIgnoreCase);
        var summary = DetectionOutputInspector.Inspect(preview.Outputs);
        var currentConfidence = ReadDoubleValue(currentParameters, "DeepLearning.Confidence", 0.05d);
        var expectedCount = summary.ExpectedCount ?? summary.ExpectedLabels.Count;
        var actualCount = summary.DetectionCount > 0 ? summary.DetectionCount : (summary.DeclaredCount ?? 0);

        double? nextConfidence = null;
        if (diagnostics.Contains("missing_expected_class") ||
            diagnostics.Contains("low_detection_confidence") ||
            (expectedCount > 0 && actualCount < expectedCount))
        {
            nextConfidence = Math.Max(0.0d, Math.Round(currentConfidence - 0.05d, 3));
        }
        else if (diagnostics.Contains("duplicate_detected_class") ||
                 (expectedCount > 0 && actualCount > expectedCount))
        {
            nextConfidence = Math.Min(0.80d, Math.Round(currentConfidence + 0.05d, 3));
        }

        if (!nextConfidence.HasValue || Math.Abs(nextConfidence.Value - currentConfidence) <= 0.0001d)
        {
            return null;
        }

        var nextParameters = new Dictionary<string, object>(currentParameters)
        {
            ["DeepLearning.Confidence"] = nextConfidence.Value
        };
        return nextParameters;
    }

    private static bool IsWireSequenceGoalAchieved(FlowNodePreviewWithMetricsResult preview)
    {
        if (preview.MissingResources.Count > 0)
        {
            return false;
        }

        var summary = DetectionOutputInspector.Inspect(preview.Outputs);
        var isMatch = false;
        if (preview.Outputs.TryGetValue("IsMatch", out var rawIsMatch))
        {
            isMatch = rawIsMatch switch
            {
                bool booleanValue => booleanValue,
                string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.True => true,
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.False => false,
                _ => false
            };
        }

        if (!isMatch && summary.ExpectedLabels.Count > 0)
        {
            isMatch = summary.ExpectedLabels.SequenceEqual(summary.ActualOrder, StringComparer.OrdinalIgnoreCase) &&
                summary.MissingLabels.Count == 0 &&
                summary.DuplicateLabels.Count == 0;
        }

        var blockingDiagnostics = preview.DiagnosticCodes.Any(code => code is
            "missing_expected_class" or
            "duplicate_detected_class" or
            "detection_count_mismatch" or
            "low_detection_confidence" or
            "order_mismatch" or
            "missing_model" or
            "missing_labels");

        return isMatch && !blockingDiagnostics;
    }

    private static double GetOperatorParameterValue(Operator @operator, string parameterName, double defaultValue)
    {
        var parameter = @operator.Parameters.FirstOrDefault(item => item.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        return parameter?.GetValue() switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetDouble(out var number) => number,
            string stringValue when double.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static void SetOperatorParameterValue(Operator @operator, string parameterName, object value)
    {
        var parameter = @operator.Parameters.FirstOrDefault(item => item.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));
        if (parameter == null)
        {
            parameter = new Parameter(
                Guid.NewGuid(),
                parameterName,
                parameterName,
                string.Empty,
                "double",
                value,
                null,
                null,
                false,
                null);
            @operator.Parameters.Add(parameter);
        }

        parameter.SetValue(value);
    }

    private static double ReadDoubleValue(Dictionary<string, object> parameters, string key, double defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetDouble(out var parsed) => parsed,
            string stringValue when double.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    #endregion
}
