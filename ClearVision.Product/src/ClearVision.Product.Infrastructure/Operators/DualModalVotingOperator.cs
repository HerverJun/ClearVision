using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "双模态投票",
    Description = "融合深度学习与传统视觉检测结果，输出最终判定。",
    CategoryId = OperatorCategoryId.DefectDetection,
    IconName = "voting",
    Version = "1.0.1"
)]
[InputPort("DLResult", "Deep learning result", PortDataType.Any, IsRequired = true)]
[InputPort("TraditionalResult", "Traditional result", PortDataType.Any, IsRequired = true)]
[OutputPort("IsOk", "Whether the final result is OK", PortDataType.Boolean)]
[OutputPort("Confidence", "Confidence of the final judgment", PortDataType.Float)]
[OutputPort("JudgmentValue", "Final judgment value", PortDataType.String)]
[OperatorParam("VotingStrategy", "Voting strategy", "enum", DefaultValue = "WeightedAverage", Options = new[] { "WeightedAverage|Weighted average", "Unanimous|Unanimous", "Majority|Majority", "PrioritizeDeepLearning|Prioritize deep learning", "PrioritizeTraditional|Prioritize traditional" })]
[OperatorParam("DLWeight", "Deep learning weight", "double", DefaultValue = 0.6, Min = 0.0, Max = 1.0)]
[OperatorParam("TraditionalWeight", "Traditional weight", "double", DefaultValue = 0.4, Min = 0.0, Max = 1.0)]
[OperatorParam("ConfidenceThreshold", "Confidence threshold", "double", DefaultValue = 0.5, Min = 0.0, Max = 1.0)]
[OperatorParam("OkOutputValue", "OK output value", "string", DefaultValue = "1")]
[OperatorParam("NgOutputValue", "NG output value", "string", DefaultValue = "0")]
public class DualModalVotingOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.DualModalVoting;

    public DualModalVotingOperator(ILogger<DualModalVotingOperator> logger)
        : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var strategy = GetStringParam(@operator, "VotingStrategy", "WeightedAverage");
        var normalizedStrategy = NormalizeStrategy(strategy);
        var dlWeight = GetDoubleParam(@operator, "DLWeight", 0.6);
        var traditionalWeight = GetDoubleParam(@operator, "TraditionalWeight", 0.4);
        var confidenceThreshold = GetDoubleParam(@operator, "ConfidenceThreshold", 0.5);
        var okValue = GetStringParam(@operator, "OkOutputValue", "1");
        var ngValue = GetStringParam(@operator, "NgOutputValue", "0");

        var dlResult = ExtractDetectionResult(inputs, "DLResult");
        var traditionalResult = ExtractDetectionResult(inputs, "TraditionalResult");

        if (dlResult == null && traditionalResult == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("No valid detection result input was received."));
        }

        dlResult ??= DetectionResult.Failed("Deep learning result was not received.");
        traditionalResult ??= DetectionResult.Failed("Traditional result was not received.");

        var dlOkProbability = ToOkProbability(dlResult);
        var traditionalOkProbability = ToOkProbability(traditionalResult);

        bool isOk;
        double confidence;
        string details;

        switch (normalizedStrategy)
        {
            case "WeightedAverage":
            {
                var totalWeight = dlWeight + traditionalWeight;
                if (totalWeight <= 1e-12)
                {
                    return Task.FromResult(OperatorExecutionOutput.Failure("WeightedAverage requires DLWeight + TraditionalWeight > 0."));
                }

                var weightedOkProbability =
                    (dlOkProbability * dlWeight + traditionalOkProbability * traditionalWeight) / totalWeight;
                isOk = weightedOkProbability >= confidenceThreshold;
                confidence = ToOutputConfidence(isOk, weightedOkProbability);
                details =
                    $"WeightedAverage: DLOkProb={dlOkProbability:F2}*{dlWeight} + TraditionalOkProb={traditionalOkProbability:F2}*{traditionalWeight} -> OkProb={weightedOkProbability:F2}, DecisionConf={confidence:F2}";
                break;
            }

            case "Unanimous":
            {
                isOk = dlResult.IsOk && traditionalResult.IsOk;
                var unanimousOkProbability = Math.Min(dlOkProbability, traditionalOkProbability);
                confidence = ToOutputConfidence(isOk, unanimousOkProbability);
                details =
                    $"Unanimous: DL={dlResult.IsOk}/{GetLabelConfidence(dlResult):F2}, Traditional={traditionalResult.IsOk}/{GetLabelConfidence(traditionalResult):F2}, DecisionConf={confidence:F2}";
                break;
            }

            case "Majority":
            {
                if (dlResult.IsOk == traditionalResult.IsOk)
                {
                    isOk = dlResult.IsOk;
                    var majorityOkProbability = (dlOkProbability + traditionalOkProbability) / 2.0;
                    confidence = ToOutputConfidence(isOk, majorityOkProbability);
                }
                else if (GetLabelConfidence(dlResult) >= GetLabelConfidence(traditionalResult))
                {
                    isOk = dlResult.IsOk;
                    confidence = GetLabelConfidence(dlResult);
                }
                else
                {
                    isOk = traditionalResult.IsOk;
                    confidence = GetLabelConfidence(traditionalResult);
                }

                details = $"Majority: IsOk={isOk}, DecisionConf={confidence:F2}";
                break;
            }

            case "PrioritizeDeepLearning":
                isOk = dlResult.IsOk;
                confidence = GetLabelConfidence(dlResult);
                details = $"PrioritizeDeepLearning: IsOk={isOk}, DecisionConf={confidence:F2}";
                break;

            case "PrioritizeTraditional":
                isOk = traditionalResult.IsOk;
                confidence = GetLabelConfidence(traditionalResult);
                details = $"PrioritizeTraditional: IsOk={isOk}, DecisionConf={confidence:F2}";
                break;

            default:
                return Task.FromResult(OperatorExecutionOutput.Failure($"Unsupported voting strategy: {strategy}"));
        }

        var outputData = new Dictionary<string, object>
        {
            { "IsOk", isOk },
            { "Confidence", confidence },
            { "JudgmentValue", isOk ? okValue : ngValue }
        };

        Logger.LogInformation(
            "[DualModalVoting] Voting completed. Strategy: {Strategy}, IsOk: {IsOk}, Confidence: {Confidence:F2}, Details: {Details}",
            normalizedStrategy,
            isOk,
            confidence,
            details);

        return Task.FromResult(OperatorExecutionOutput.Success(outputData));
    }

    private static DetectionResult? ExtractDetectionResult(Dictionary<string, object>? inputs, string key)
    {
        if (inputs == null || !inputs.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return DetectionResultAdapter.TryCreateDecision(value, out var decision)
            ? DetectionResult.Success(decision.IsOk, decision.Confidence)
            : null;
    }

    private static double ToOkProbability(DetectionResult result)
    {
        if (!result.IsSuccess)
        {
            return 0.5;
        }

        var labelConfidence = GetLabelConfidence(result);
        return result.IsOk ? labelConfidence : 1.0 - labelConfidence;
    }

    private static double GetLabelConfidence(DetectionResult result)
    {
        return result.IsSuccess
            ? Math.Clamp(result.Confidence, 0.0, 1.0)
            : 0.0;
    }

    private static double ToOutputConfidence(bool isOk, double okProbability)
    {
        var normalizedOkProbability = Math.Clamp(okProbability, 0.0, 1.0);
        return isOk ? normalizedOkProbability : 1.0 - normalizedOkProbability;
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var strategy = GetStringParam(@operator, "VotingStrategy", "WeightedAverage");
        var normalizedStrategy = NormalizeStrategy(strategy);

        if (normalizedStrategy == null)
        {
            return ValidationResult.Invalid($"VotingStrategy must be one of: {string.Join(", ", ValidStrategies)}");
        }

        var dlWeight = GetDoubleParam(@operator, "DLWeight", 0.6, 0.0, 1.0);
        var traditionalWeight = GetDoubleParam(@operator, "TraditionalWeight", 0.4, 0.0, 1.0);
        var weightSum = dlWeight + traditionalWeight;

        if (normalizedStrategy == "WeightedAverage" && weightSum <= 1e-12)
        {
            return ValidationResult.Invalid("WeightedAverage requires DLWeight + TraditionalWeight > 0.");
        }

        if (normalizedStrategy == "WeightedAverage" && Math.Abs(weightSum - 1.0) > 0.01)
        {
            return ValidationResult.Invalid(
                $"In WeightedAverage mode, DLWeight ({dlWeight}) + TraditionalWeight ({traditionalWeight}) must be approximately 1.0 (current={weightSum:F2}).");
        }

        return ValidationResult.Valid();
    }

    private static readonly string[] ValidStrategies =
    [
        "Unanimous",
        "Majority",
        "WeightedAverage",
        "PrioritizeDeepLearning",
        "PrioritizeTraditional"
    ];

    private static string? NormalizeStrategy(string? strategy)
    {
        return ValidStrategies.FirstOrDefault(
            valid => valid.Equals(strategy?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
