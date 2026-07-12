using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Core.Decisions;

public sealed record FinalDecisionOutputCandidate(
    Guid OperatorId,
    string OperatorName,
    Guid OutputPortId,
    string OutputName,
    DecisionValueType DataType,
    DecisionInterpretationRule Rule);

public sealed record FinalDecisionSourceCapability(
    OperatorType OperatorType,
    string OutputName,
    PortDataType PortType);

public static class FinalDecisionConfigurationCatalog
{
    private static readonly IReadOnlyDictionary<string, PortDataType> EligibleOutputContracts = CreateEligibleOutputContracts();

    public static IReadOnlyList<FinalDecisionOutputCandidate> GetEligibleOutputs(OperatorFlow? flow)
    {
        if (flow == null)
        {
            return Array.Empty<FinalDecisionOutputCandidate>();
        }

        return flow.Operators
            .Where(op => op.IsEnabled)
            .SelectMany(op => op.OutputPorts.Select(port => CreateCandidate(op, port.DataType, port.Id, port.Name)))
            .Where(candidate => candidate != null)
            .Cast<FinalDecisionOutputCandidate>()
            .OrderBy(candidate => candidate.OperatorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.OutputName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FinalDecisionOutputCandidate? CreateCandidate(
        Operator source,
        PortDataType portType,
        Guid portId,
        string portName)
    {
        if (!EligibleOutputContracts.TryGetValue(ContractKey(source.Type, portName), out var contractType) ||
            contractType != portType)
        {
            return null;
        }

        var mapping = portType switch
        {
            PortDataType.Boolean => (DecisionValueType.Boolean, DecisionInterpretationRule.Boolean),
            PortDataType.String => (DecisionValueType.String, DecisionInterpretationRule.StringMap),
            PortDataType.Integer => (DecisionValueType.Integer, DecisionInterpretationRule.NumericComparison),
            PortDataType.Float => (DecisionValueType.Float, DecisionInterpretationRule.NumericComparison),
            _ => ((DecisionValueType DataType, DecisionInterpretationRule Rule)?)null
        };

        return mapping.HasValue
            ? new FinalDecisionOutputCandidate(
                source.Id,
                source.Name,
                portId,
                portName,
                mapping.Value.DataType,
                mapping.Value.Rule)
            : null;
    }

    public static bool TryGetEligibleOutput(
        Operator source,
        Port port,
        out FinalDecisionOutputCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(port);

        candidate = CreateCandidate(source, port.DataType, port.Id, port.Name);
        return candidate != null;
    }

    public static bool IsEligibleContract(OperatorType operatorType, string outputName) =>
        !string.IsNullOrWhiteSpace(outputName) &&
        EligibleOutputContracts.ContainsKey(ContractKey(operatorType, outputName));

    public static IReadOnlyList<FinalDecisionSourceCapability> GetDeclaredCapabilities() =>
        EligibleOutputContracts.Select(pair =>
        {
            var separator = pair.Key.IndexOf(':');
            return new FinalDecisionSourceCapability(
                Enum.Parse<OperatorType>(pair.Key[..separator]),
                pair.Key[(separator + 1)..],
                pair.Value);
        }).ToList();

    private static IReadOnlyDictionary<string, PortDataType> CreateEligibleOutputContracts()
    {
        var contracts = new Dictionary<string, PortDataType>(StringComparer.OrdinalIgnoreCase);

        Add(contracts, OperatorType.ResultJudgment, PortDataType.String, "JudgmentResult", "JudgmentValue");
        Add(contracts, OperatorType.ResultJudgment, PortDataType.Boolean, "IsOk", "ConditionResult");
        Add(contracts, OperatorType.Comparator, PortDataType.Boolean, "Result");
        Add(contracts, OperatorType.DetectionSequenceJudge, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.DetectionSequenceJudge, PortDataType.Integer, "Count", "RowCount");
        Add(contracts, OperatorType.DualModalVoting, PortDataType.Boolean, "IsOk");
        Add(contracts, OperatorType.DualModalVoting, PortDataType.Float, "Confidence");
        Add(contracts, OperatorType.DualModalVoting, PortDataType.String, "JudgmentValue");
        Add(contracts, OperatorType.AnomalyDetection, PortDataType.Boolean, "IsAnomaly");
        Add(contracts, OperatorType.AnomalyDetection, PortDataType.Float, "AnomalyScore");
        Add(contracts, OperatorType.AnomalyDetection, PortDataType.Integer, "PatchCount");
        Add(contracts, OperatorType.GeometricTolerance, PortDataType.Boolean, "Accepted");
        Add(contracts, OperatorType.GeometricTolerance, PortDataType.Float, "Tolerance", "ZoneDeviation", "AngularDeviationDeg", "LinearBand");
        Add(contracts, OperatorType.SharpnessEvaluation, PortDataType.Boolean, "IsSharp");
        Add(contracts, OperatorType.SharpnessEvaluation, PortDataType.Float, "Score");
        Add(contracts, OperatorType.Statistics, PortDataType.Boolean, "IsCapable");
        Add(contracts, OperatorType.Statistics, PortDataType.Integer, "Count");
        Add(contracts, OperatorType.Statistics, PortDataType.Float, "Mean", "StdDev", "Min", "Max", "Cpk");

        Add(contracts, OperatorType.BlobAnalysis, PortDataType.Integer, "BlobCount");
        Add(contracts, OperatorType.BlobLabeling, PortDataType.Integer, "Count");
        Add(contracts, OperatorType.DeepLearning, PortDataType.Integer, "DefectCount", "ObjectCount");
        Add(contracts, OperatorType.EdgePairDefect, PortDataType.Integer, "DefectCount");
        Add(contracts, OperatorType.EdgePairDefect, PortDataType.Float, "MaxDeviation");
        Add(contracts, OperatorType.SurfaceDefectDetection, PortDataType.Integer, "DefectCount");
        Add(contracts, OperatorType.SurfaceDefectDetection, PortDataType.Float, "DefectArea", "AlignmentScore");
        Add(contracts, OperatorType.SemanticSegmentation, PortDataType.Integer, "ClassCount", "ClassMaskCount");

        Add(contracts, OperatorType.AngleMeasurement, PortDataType.Float, "Angle");
        Add(contracts, OperatorType.CaliperTool, PortDataType.Integer, "PairCount");
        Add(contracts, OperatorType.CaliperTool, PortDataType.Float, "Width", "AverageDistance", "DistanceStdDev");
        Add(contracts, OperatorType.CircleMeasurement, PortDataType.Float, "Radius", "Circularity");
        Add(contracts, OperatorType.ColorMeasurement, PortDataType.Boolean, "HueValid");
        Add(contracts, OperatorType.ColorMeasurement, PortDataType.Float, "DeltaE", "HueMean", "SaturationMean", "ValueMean");
        Add(contracts, OperatorType.ContourMeasurement, PortDataType.Integer, "ContourCount");
        Add(contracts, OperatorType.ContourMeasurement, PortDataType.Float, "Area", "Perimeter");
        Add(contracts, OperatorType.GapMeasurement, PortDataType.Integer, "Count");
        Add(contracts, OperatorType.GapMeasurement, PortDataType.Float, "MeanGap", "MinGap", "MaxGap", "P95Gap", "StdDev", "ValidSampleRate");
        Add(contracts, OperatorType.GeoMeasurement, PortDataType.Float, "Distance", "Angle");
        Add(contracts, OperatorType.LineMeasurement, PortDataType.Integer, "LineCount");
        Add(contracts, OperatorType.LineMeasurement, PortDataType.Float, "Angle", "Length");
        Add(contracts, OperatorType.Measurement, PortDataType.Float, "Distance");
        Add(contracts, OperatorType.PixelStatistics, PortDataType.Integer, "Min", "Max", "Median", "NonZeroCount");
        Add(contracts, OperatorType.PixelStatistics, PortDataType.Float, "Mean", "StdDev");
        Add(contracts, OperatorType.WidthMeasurement, PortDataType.Float, "Width", "MeanWidth", "MinWidth", "MaxWidth", "P95Width", "StdDev", "ValidSampleRate");

        Add(contracts, OperatorType.TemplateMatching, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.TemplateMatching, PortDataType.Integer, "MatchCount");
        Add(contracts, OperatorType.TemplateMatching, PortDataType.Float, "Score");
        Add(contracts, OperatorType.PlanarMatching, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.PlanarMatching, PortDataType.Integer, "MatchCount", "InlierCount");
        Add(contracts, OperatorType.PlanarMatching, PortDataType.Float, "Score", "InlierRatio", "MeanReprojectionError", "MaxReprojectionError");
        Add(contracts, OperatorType.PPFMatch, PortDataType.Boolean, "IsMatch", "IsMatched");
        Add(contracts, OperatorType.PPFMatch, PortDataType.Integer, "MatchCount", "InlierCount");
        Add(contracts, OperatorType.PPFMatch, PortDataType.Float, "Score", "InlierRatio", "RmsError");
        Add(contracts, OperatorType.AkazeFeatureMatch, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.AkazeFeatureMatch, PortDataType.Float, "Score", "InlierRatio", "MeanReprojectionError", "MaxReprojectionError");
        Add(contracts, OperatorType.OrbFeatureMatch, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.OrbFeatureMatch, PortDataType.Float, "Score", "InlierRatio", "MeanReprojectionError", "MaxReprojectionError");
        Add(contracts, OperatorType.GradientShapeMatch, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.GradientShapeMatch, PortDataType.Float, "Score", "Angle");
        Add(contracts, OperatorType.PyramidShapeMatch, PortDataType.Boolean, "IsMatch");
        Add(contracts, OperatorType.PyramidShapeMatch, PortDataType.Float, "Score", "Angle");

        Add(contracts, OperatorType.CodeRecognition, PortDataType.String, "Text");
        Add(contracts, OperatorType.CodeRecognition, PortDataType.Integer, "CodeCount");
        Add(contracts, OperatorType.OcrRecognition, PortDataType.String, "Text");

        return contracts;
    }

    private static void Add(
        Dictionary<string, PortDataType> contracts,
        OperatorType operatorType,
        PortDataType dataType,
        params string[] outputNames)
    {
        foreach (var outputName in outputNames)
        {
            contracts.Add(ContractKey(operatorType, outputName), dataType);
        }
    }

    private static string ContractKey(OperatorType operatorType, string outputName) =>
        $"{OperatorTypeAliasResolver.Resolve(operatorType)}:{outputName.Trim()}";
}
