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
    DecisionInterpretationRule Rule,
    bool? DefaultTrueMeansOk,
    string? DefaultOkValue,
    string? DefaultNgValue,
    string? RequiredOkValue,
    string? RequiredNgValue);

public sealed record FinalDecisionSourceCapability(
    OperatorType OperatorType,
    string OutputName,
    PortDataType PortType,
    DecisionInterpretationRule Rule,
    bool? DefaultTrueMeansOk,
    string? DefaultOkValue,
    string? DefaultNgValue,
    string? RequiredOkValue,
    string? RequiredNgValue);

internal sealed record FinalDecisionSourceContract(
    OperatorType OperatorType,
    string OutputName,
    PortDataType PortType,
    DecisionInterpretationRule Rule,
    bool? DefaultTrueMeansOk = null,
    string? DefaultOkValue = null,
    string? DefaultNgValue = null,
    string? RequiredOkValue = null,
    string? RequiredNgValue = null);

public static class FinalDecisionConfigurationCatalog
{
    private static readonly IReadOnlyDictionary<string, FinalDecisionSourceContract> EligibleOutputContracts = CreateEligibleOutputContracts();

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
        if (!EligibleOutputContracts.TryGetValue(ContractKey(source.Type, portName), out var contract) ||
            contract.PortType != portType)
        {
            return null;
        }

        var dataType = portType switch
        {
            PortDataType.Boolean => DecisionValueType.Boolean,
            PortDataType.String => DecisionValueType.String,
            PortDataType.Integer => DecisionValueType.Integer,
            PortDataType.Float => DecisionValueType.Float,
            _ => (DecisionValueType?)null
        };

        return dataType.HasValue
            ? new FinalDecisionOutputCandidate(
                source.Id,
                source.Name,
                portId,
                portName,
                dataType.Value,
                contract.Rule,
                contract.DefaultTrueMeansOk,
                contract.DefaultOkValue,
                contract.DefaultNgValue,
                contract.RequiredOkValue,
                contract.RequiredNgValue)
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
        EligibleOutputContracts.Values.Select(contract => new FinalDecisionSourceCapability(
            contract.OperatorType,
            contract.OutputName,
            contract.PortType,
            contract.Rule,
            contract.DefaultTrueMeansOk,
            contract.DefaultOkValue,
            contract.DefaultNgValue,
            contract.RequiredOkValue,
            contract.RequiredNgValue)).ToList();

    private static IReadOnlyDictionary<string, FinalDecisionSourceContract> CreateEligibleOutputContracts()
    {
        var contracts = new Dictionary<string, FinalDecisionSourceContract>(StringComparer.OrdinalIgnoreCase);

        AddFixedStringMap(contracts, OperatorType.ResultJudgment, "JudgmentResult", "OK", "NG");
        AddFixedStringMap(contracts, OperatorType.ResultJudgment, "JudgmentValue", "1", "0");
        AddBoolean(contracts, OperatorType.ResultJudgment, true, "IsOk", "ConditionResult");
        AddBoolean(contracts, OperatorType.Comparator, true, "Result");
        AddBoolean(contracts, OperatorType.DetectionSequenceJudge, true, "IsMatch");
        Add(contracts, OperatorType.DetectionSequenceJudge, PortDataType.Integer, "Count", "RowCount");
        AddBoolean(contracts, OperatorType.DualModalVoting, true, "IsOk");
        Add(contracts, OperatorType.DualModalVoting, PortDataType.Float, "Confidence");
        AddFixedStringMap(contracts, OperatorType.DualModalVoting, "JudgmentValue", "1", "0");
        AddBoolean(contracts, OperatorType.AnomalyDetection, false, "IsAnomaly");
        Add(contracts, OperatorType.AnomalyDetection, PortDataType.Float, "AnomalyScore");
        Add(contracts, OperatorType.AnomalyDetection, PortDataType.Integer, "PatchCount");
        AddBoolean(contracts, OperatorType.GeometricTolerance, true, "Accepted");
        Add(contracts, OperatorType.GeometricTolerance, PortDataType.Float, "Tolerance", "ZoneDeviation", "AngularDeviationDeg", "LinearBand");
        AddBoolean(contracts, OperatorType.SharpnessEvaluation, true, "IsSharp");
        Add(contracts, OperatorType.SharpnessEvaluation, PortDataType.Float, "Score");
        AddBoolean(contracts, OperatorType.Statistics, true, "IsCapable");
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
        AddBoolean(contracts, OperatorType.ColorMeasurement, true, "HueValid");
        Add(contracts, OperatorType.ColorMeasurement, PortDataType.Float, "DeltaE", "HueMean", "SaturationMean", "ValueMean");
        Add(contracts, OperatorType.ContourMeasurement, PortDataType.Integer, "ContourCount");
        Add(contracts, OperatorType.ContourMeasurement, PortDataType.Float, "Area", "Perimeter");
        Add(contracts, OperatorType.GapMeasurement, PortDataType.Integer, "Count");
        Add(contracts, OperatorType.GapMeasurement, PortDataType.Float, "MeanGap", "MinGap", "MaxGap", "P95Gap", "StdDev", "ValidSampleRate");
        Add(contracts, OperatorType.GeoMeasurement, PortDataType.Float, "Distance", "Angle");
        Add(contracts, OperatorType.LineMeasurement, PortDataType.Integer, "LineCount");
        Add(contracts, OperatorType.LineMeasurement, PortDataType.Float, "Angle", "Length");
        Add(contracts, OperatorType.Measurement, PortDataType.Float, "Distance");
        Add(contracts, OperatorType.PixelStatistics, PortDataType.Integer, "NonZeroCount", "SampleCount");
        Add(contracts, OperatorType.PixelStatistics, PortDataType.Float,
            "Mean", "StdDev", "Min", "Max", "Median",
            "Range", "MedianAbsoluteDeviation", "StdError");
        Add(contracts, OperatorType.WidthMeasurement, PortDataType.Float, "Width", "MeanWidth", "MinWidth", "MaxWidth", "P95Width", "StdDev", "ValidSampleRate");

        AddBoolean(contracts, OperatorType.TemplateMatching, true, "IsMatch");
        Add(contracts, OperatorType.TemplateMatching, PortDataType.Integer, "MatchCount");
        Add(contracts, OperatorType.TemplateMatching, PortDataType.Float, "Score");
        AddBoolean(contracts, OperatorType.PlanarMatching, true, "IsMatch");
        Add(contracts, OperatorType.PlanarMatching, PortDataType.Integer, "MatchCount", "InlierCount");
        Add(contracts, OperatorType.PlanarMatching, PortDataType.Float, "Score", "InlierRatio", "MeanReprojectionError", "MaxReprojectionError");
        AddBoolean(contracts, OperatorType.PPFMatch, true, "IsMatch", "IsMatched");
        Add(contracts, OperatorType.PPFMatch, PortDataType.Integer, "MatchCount", "InlierCount");
        Add(contracts, OperatorType.PPFMatch, PortDataType.Float, "Score", "InlierRatio", "RmsError");
        AddBoolean(contracts, OperatorType.AkazeFeatureMatch, true, "IsMatch");
        Add(contracts, OperatorType.AkazeFeatureMatch, PortDataType.Float, "Score", "InlierRatio", "MeanReprojectionError", "MaxReprojectionError");
        AddBoolean(contracts, OperatorType.OrbFeatureMatch, true, "IsMatch");
        Add(contracts, OperatorType.OrbFeatureMatch, PortDataType.Float, "Score", "InlierRatio", "MeanReprojectionError", "MaxReprojectionError");
        AddBoolean(contracts, OperatorType.GradientShapeMatch, true, "IsMatch");
        Add(contracts, OperatorType.GradientShapeMatch, PortDataType.Float, "Score", "Angle");
        AddBoolean(contracts, OperatorType.PyramidShapeMatch, true, "IsMatch");
        Add(contracts, OperatorType.PyramidShapeMatch, PortDataType.Float, "Score", "Angle");

        Add(contracts, OperatorType.CodeRecognition, PortDataType.String, "Text");
        Add(contracts, OperatorType.CodeRecognition, PortDataType.Integer, "CodeCount");
        Add(contracts, OperatorType.OcrRecognition, PortDataType.String, "Text");

        return contracts;
    }

    private static void Add(
        Dictionary<string, FinalDecisionSourceContract> contracts,
        OperatorType operatorType,
        PortDataType dataType,
        params string[] outputNames)
    {
        var rule = dataType switch
        {
            PortDataType.String => DecisionInterpretationRule.StringMap,
            PortDataType.Integer or PortDataType.Float => DecisionInterpretationRule.NumericComparison,
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Use AddBoolean for Boolean decision sources.")
        };

        foreach (var outputName in outputNames)
        {
            AddContract(contracts, new FinalDecisionSourceContract(operatorType, outputName, dataType, rule));
        }
    }

    private static void AddBoolean(
        Dictionary<string, FinalDecisionSourceContract> contracts,
        OperatorType operatorType,
        bool defaultTrueMeansOk,
        params string[] outputNames)
    {
        foreach (var outputName in outputNames)
        {
            AddContract(contracts, new FinalDecisionSourceContract(
                operatorType,
                outputName,
                PortDataType.Boolean,
                DecisionInterpretationRule.Boolean,
                DefaultTrueMeansOk: defaultTrueMeansOk));
        }
    }

    private static void AddFixedStringMap(
        Dictionary<string, FinalDecisionSourceContract> contracts,
        OperatorType operatorType,
        string outputName,
        string okValue,
        string ngValue) =>
        AddContract(contracts, new FinalDecisionSourceContract(
            operatorType,
            outputName,
            PortDataType.String,
            DecisionInterpretationRule.StringMap,
            DefaultOkValue: okValue,
            DefaultNgValue: ngValue,
            RequiredOkValue: okValue,
            RequiredNgValue: ngValue));

    private static void AddContract(
        Dictionary<string, FinalDecisionSourceContract> contracts,
        FinalDecisionSourceContract contract) =>
        contracts.Add(ContractKey(contract.OperatorType, contract.OutputName), contract);

    private static string ContractKey(OperatorType operatorType, string outputName) =>
        $"{OperatorTypeAliasResolver.Resolve(operatorType)}:{outputName.Trim()}";
}
