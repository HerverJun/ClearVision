using Xunit.Abstractions;
using Xunit.Sdk;

namespace ClearVision.Testing;

public enum TestDomain
{
    General,
    Core,
    Desktop,
    Ui,
    OperatorLibrary,
    Measurement,
    Detection,
    Matching,
    Preprocessing,
    Calibration,
    Plc,
    PointCloud,
    Ai,
    Runtime,
    Data,
    Quality
}

public enum TestPurpose
{
    Regression,
    Accuracy,
    Determinism,
    Stability,
    Robustness,
    Performance,
    Integration,
    Smoke
}

public enum TestLane
{
    Pr,
    Nightly,
    ReleaseManual
}

public enum TestEvidenceType
{
    Contract,
    IndependentOracle,
    GoldenDataset,
    StatisticalDistribution,
    PerformanceProfile,
    IntegrationEvidence,
    PackageSmoke
}

public enum TestOracleType
{
    Contract,
    Mathematical,
    AnnotatedGroundTruth,
    GoldenFile,
    Metamorphic,
    Statistical,
    PerformanceBudget,
    ExternalSystem,
    None
}

public enum TestResourceRequirement
{
    None,
    RepositoryAsset,
    CpuProfile,
    Gpu,
    ModelAsset,
    Database,
    VirtualPlc,
    PhysicalPlc,
    Camera,
    Network,
    Browser,
    PackageFeed,
    HumanApproval
}

public enum TestExpectedDuration
{
    Fast,
    Medium,
    Long,
    Extended
}

public enum TestFlakyPolicy
{
    Blocking,
    RetryThenBlock,
    QuarantineWithOwner,
    ManualEvidence
}

[TraitDiscoverer(TestClassificationDiscoverer.TypeName, TestClassificationDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class TestClassificationAttribute : Attribute, ITraitAttribute
{
    public TestClassificationAttribute(
        TestDomain domain,
        TestPurpose purpose,
        TestLane lane,
        TestEvidenceType evidenceType,
        TestOracleType oracleType,
        TestResourceRequirement resourceRequirement,
        TestExpectedDuration expectedDuration,
        TestFlakyPolicy flakyPolicy,
        string owner)
    {
        Domain = domain;
        Purpose = purpose;
        Lane = lane;
        EvidenceType = evidenceType;
        OracleType = oracleType;
        ResourceRequirement = resourceRequirement;
        ExpectedDuration = expectedDuration;
        FlakyPolicy = flakyPolicy;
        Owner = owner;
    }

    public TestDomain Domain { get; }
    public TestPurpose Purpose { get; }
    public TestLane Lane { get; }
    public TestEvidenceType EvidenceType { get; }
    public TestOracleType OracleType { get; }
    public TestResourceRequirement ResourceRequirement { get; }
    public TestExpectedDuration ExpectedDuration { get; }
    public TestFlakyPolicy FlakyPolicy { get; }
    public string Owner { get; }
    public string Suites { get; set; } = string.Empty;
    public string SeedControl { get; set; } = string.Empty;
    public string PerformanceProfile { get; set; } = string.Empty;
}

public sealed class TestClassificationDiscoverer : ITraitDiscoverer
{
    public const string AssemblyName = "ClearVision.Testing";
    public const string TypeName = "ClearVision.Testing.TestClassificationDiscoverer";

    private static readonly string[] RequiredTraitNames =
    {
        "Domain",
        "Purpose",
        "Lane",
        "EvidenceType",
        "OracleType",
        "ResourceRequirement",
        "ExpectedDuration",
        "FlakyPolicy",
        "Owner"
    };

    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var arguments = traitAttribute.GetConstructorArguments().ToArray();
        if (arguments.Length != RequiredTraitNames.Length)
        {
            yield break;
        }

        for (var index = 0; index < RequiredTraitNames.Length; index++)
        {
            yield return new KeyValuePair<string, string>(RequiredTraitNames[index], arguments[index]?.ToString() ?? string.Empty);
        }

        foreach (var suite in SplitValues(traitAttribute.GetNamedArgument<string>(nameof(TestClassificationAttribute.Suites))))
        {
            yield return new KeyValuePair<string, string>("Suite", suite);
        }

        var seedControl = traitAttribute.GetNamedArgument<string>(nameof(TestClassificationAttribute.SeedControl));
        if (!string.IsNullOrWhiteSpace(seedControl))
        {
            yield return new KeyValuePair<string, string>("SeedControl", seedControl.Trim());
        }

        var performanceProfile = traitAttribute.GetNamedArgument<string>(nameof(TestClassificationAttribute.PerformanceProfile));
        if (!string.IsNullOrWhiteSpace(performanceProfile))
        {
            yield return new KeyValuePair<string, string>("PerformanceProfile", performanceProfile.Trim());
        }
    }

    private static IEnumerable<string> SplitValues(string? values) =>
        (values ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal);
}
