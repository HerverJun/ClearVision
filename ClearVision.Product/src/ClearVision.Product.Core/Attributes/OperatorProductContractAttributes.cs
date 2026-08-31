using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Attributes;

public enum OperatorParameterRequiredPolicy
{
    Metadata,
    Required,
    Optional
}

public enum OperatorResourceKind
{
    None,
    ImageFile,
    CameraBinding,
    TemplateResource,
    ModelResource,
    ModelCatalog,
    ModelLabels,
    FeatureBank,
    OutputFile,
    PlcEndpoint,
    PlcAddress,
    TcpProfile,
    NetworkEndpoint,
    PlcProfile
}

/// <summary>
/// Declares reusable parameter availability/requirement semantics on an operator class.
/// Conditions use the stable grammar: Name==Value, Name!=Value, Name:empty, Name:not-empty.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class OperatorParameterRuleAttribute : Attribute
{
    public OperatorParameterRuleAttribute(string parameter)
    {
        Parameter = parameter;
    }

    public string Parameter { get; }
    public OperatorParameterRequiredPolicy RequiredPolicy { get; set; } = OperatorParameterRequiredPolicy.Metadata;
    public string[]? RequiredWhenAll { get; set; }
    public string[]? RequiredWhenAny { get; set; }
    public string[]? EnabledWhenAll { get; set; }
    public string[]? EnabledWhenAny { get; set; }
    public string[]? DisabledWhenAll { get; set; }
    public string[]? DisabledWhenAny { get; set; }
    public string[]? VisibleWhenAll { get; set; }
    public string[]? VisibleWhenAny { get; set; }
    public string[]? HiddenWhenAll { get; set; }
    public string[]? HiddenWhenAny { get; set; }
    public string[]? IgnoredWhenAll { get; set; }
    public string[]? IgnoredWhenAny { get; set; }
    public string? AtLeastOneGroup { get; set; }
    public string? MutuallyExclusiveGroup { get; set; }
    public string? AliasFor { get; set; }
    public bool Deprecated { get; set; }
    public OperatorResourceKind ResourceKind { get; set; }
    public string[]? SatisfiedByInputPorts { get; set; }
    public string ReasonCode { get; set; } = "PARAMETER_CONSTRAINT";
}

/// <summary>
/// Declares when an output is guaranteed to be produced for the configured mode.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class OperatorOutputRuleAttribute : Attribute
{
    public OperatorOutputRuleAttribute(string output)
    {
        Output = output;
    }

    public string Output { get; }
    public string[]? AvailableWhenAll { get; set; }
    public string[]? AvailableWhenAny { get; set; }
    public string ReasonCode { get; set; } = "OUTPUT_AVAILABILITY";
}

/// <summary>
/// Adds an explicit shared implementation dependency to an operator generation fingerprint.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class OperatorGenerationDependencyAttribute : Attribute
{
    public OperatorGenerationDependencyAttribute(Type dependencyType)
    {
        DependencyType = dependencyType;
    }

    public OperatorGenerationDependencyAttribute(string sourcePath)
    {
        SourcePath = sourcePath;
    }

    public Type? DependencyType { get; }
    public string? SourcePath { get; }
}
