using System.Text.Json.Serialization;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Decisions;

public sealed class DecisionConfiguration
{
    public FinalDecisionBinding? FinalDecisionBinding { get; set; }

    public MissingDecisionPolicy MissingDecisionPolicy { get; set; } = MissingDecisionPolicy.Undetermined;
}

public sealed class FinalDecisionBinding
{
    public Guid SourceOperatorId { get; set; }

    public Guid? SourceOutputPortId { get; set; }

    public string? SourceOutputName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DecisionValueType DataType { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DecisionInterpretationRule Rule { get; set; }

    public bool TrueMeansOk { get; set; } = true;

    public string? OkValue { get; set; }

    public string? NgValue { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DecisionComparator? Comparator { get; set; }

    public double? Threshold { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionValueType
{
    Boolean = 0,
    String = 1,
    Integer = 2,
    Float = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionInterpretationRule
{
    Boolean = 0,
    StringMap = 1,
    NumericComparison = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionComparator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissingDecisionPolicy
{
    Undetermined = 0,
    NotApplicable = 1,
    Invalid = 2
}

public static class DecisionValueTypeExtensions
{
    public static bool MatchesPortType(this DecisionValueType valueType, PortDataType portType) =>
        valueType switch
        {
            DecisionValueType.Boolean => portType == PortDataType.Boolean,
            DecisionValueType.String => portType == PortDataType.String,
            DecisionValueType.Integer => portType == PortDataType.Integer,
            DecisionValueType.Float => portType is PortDataType.Float or PortDataType.Integer,
            _ => false
        };
}
