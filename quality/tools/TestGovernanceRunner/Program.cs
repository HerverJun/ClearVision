using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var options = Options.Parse(args);
var repoRoot = options.RepoRoot ?? FindRepoRoot();
var gateConfigPath = Path.Combine(repoRoot, "quality", "test-gates.json");
var reportDirectory = options.ReportDirectory ?? Path.Combine(repoRoot, ".tmp", "test-governance");

if (!File.Exists(gateConfigPath))
{
    Console.Error.WriteLine($"Test gate configuration is missing: {gateConfigPath}");
    return 2;
}

var gateConfig = JsonSerializer.Deserialize<GateConfiguration>(
    File.ReadAllText(gateConfigPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException($"Unable to deserialize {gateConfigPath}.");

var audit = Audit(repoRoot, gateConfig);
Directory.CreateDirectory(reportDirectory);
var jsonPath = Path.Combine(reportDirectory, "test-governance.json");
var markdownPath = Path.Combine(reportDirectory, "test-governance.md");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(audit.Report, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(markdownPath, BuildMarkdown(audit.Report));

Console.WriteLine($"[test-governance] Tests: {audit.Report.SourceTestCount}; unclassified: {audit.Report.Unclassified}; errors: {audit.Report.Errors.Count}; warnings: {audit.Report.Warnings.Count}");
Console.WriteLine($"[test-governance] JSON: {jsonPath}");
Console.WriteLine($"[test-governance] Markdown: {markdownPath}");

foreach (var error in audit.Report.Errors)
{
    Console.Error.WriteLine($"[test-governance] ERROR {error.Code}: {error.Location}: {error.Message}");
}

foreach (var warning in audit.Report.Warnings)
{
    Console.WriteLine($"[test-governance] WARNING {warning.Code}: {warning.Location}: {warning.Message}");
}

return audit.Report.Errors.Count == 0 && (!options.FailOnWarning || audit.Report.Warnings.Count == 0) ? 0 : 1;

static AuditResult Audit(string repoRoot, GateConfiguration gateConfig)
{
    var errors = new List<Finding>();
    var warnings = new List<Finding>();
    var tests = new List<ClassifiedTest>();
    var roots = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Product"] = Path.Combine(repoRoot, "ClearVision.Product", "tests", "ClearVision.Product.Tests"),
        ["Desktop"] = Path.Combine(repoRoot, "ClearVision.Product", "tests", "ClearVision.Product.Desktop.Tests"),
        ["OperatorLibrary"] = Path.Combine(repoRoot, "ClearVision.OperatorLibrary", "tests", "ClearVision.OperatorLibrary.SmokeTests")
    };

    foreach (var (project, root) in roots)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(IsSourceFile))
        {
            var source = File.ReadAllText(path);
            var tree = CSharpSyntaxTree.ParseText(source, path: path);
            var syntaxRoot = tree.GetRoot();
            var relativePath = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

            if (Regex.IsMatch(source, @"new\s+Random\s*\(\s*\)", RegexOptions.CultureInvariant) ||
                source.Contains("Random.Shared", StringComparison.Ordinal))
            {
                errors.Add(new Finding("RANDOM_SEED_UNCONTROLLED", relativePath, "Random tests must use a fixed or explicit seed; parameterless Random and Random.Shared are forbidden in formal tests."));
            }

            foreach (var method in syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(IsTestMethod))
            {
                var type = method.Ancestors().OfType<TypeDeclarationSyntax>().First();
                var methodAttributes = GetClassificationAttributes(method.AttributeLists).ToArray();
                var typeAttributes = GetClassificationAttributes(type.AttributeLists).ToArray();
                var location = $"{relativePath}:{tree.GetLineSpan(method.Identifier.Span).StartLinePosition.Line + 1}";
                var testName = $"{ResolveNamespace(type)}.{type.Identifier.ValueText}.{method.Identifier.ValueText}".TrimStart('.');

                if (methodAttributes.Length > 1 || typeAttributes.Length > 1)
                {
                    errors.Add(new Finding("CLASSIFICATION_DUPLICATE", location, $"{testName} has multiple TestClassification attributes."));
                    continue;
                }

                if (methodAttributes.Length > 0 && typeAttributes.Length > 0)
                {
                    errors.Add(new Finding("CLASSIFICATION_CONFLICT", location, $"{testName} has both method and class classification; method overrides are forbidden because xUnit would merge contradictory traits."));
                    continue;
                }

                var attribute = methodAttributes.SingleOrDefault() ?? typeAttributes.SingleOrDefault();
                if (attribute is null)
                {
                    errors.Add(new Finding("CLASSIFICATION_MISSING", location, $"{testName} has no TestClassification attribute."));
                    continue;
                }

                var classification = ParseClassification(attribute, location, errors);
                if (classification is null)
                {
                    continue;
                }

                ValidateClassification(classification, method, location, errors, warnings);
                tests.Add(new ClassifiedTest(project, testName, relativePath, classification));
            }
        }
    }

    var gateReports = new List<GateAudit>();
    var duplicateGateNames = gateConfig.Gates.GroupBy(gate => gate.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray();
    foreach (var duplicate in duplicateGateNames)
    {
        errors.Add(new Finding("GATE_DUPLICATE", "quality/test-gates.json", $"Gate name '{duplicate.Key}' is duplicated."));
    }

    foreach (var gate in gateConfig.Gates)
    {
        if (!gateConfig.Projects.ContainsKey(gate.Project))
        {
            errors.Add(new Finding("GATE_PROJECT_UNKNOWN", "quality/test-gates.json", $"Gate '{gate.Name}' references unknown project '{gate.Project}'."));
            continue;
        }

        IReadOnlyList<ClassifiedTest> matches;
        try
        {
            matches = tests.Where(test => test.Project == gate.Project && MatchesFilter(test.Classification.Traits, gate.Filter)).ToArray();
        }
        catch (Exception exception)
        {
            errors.Add(new Finding("GATE_FILTER_INVALID", "quality/test-gates.json", $"Gate '{gate.Name}' filter '{gate.Filter}' is invalid: {exception.Message}"));
            continue;
        }

        if (matches.Count < Math.Max(1, gate.MinimumTotalTests))
        {
            errors.Add(new Finding("GATE_EMPTY", "quality/test-gates.json", $"Gate '{gate.Name}' resolves to {matches.Count} source test(s); required at least {Math.Max(1, gate.MinimumTotalTests)}."));
        }

        var contradictory = matches.Where(test => !test.Classification.Lane.Equals(gate.Lane, StringComparison.Ordinal)).ToArray();
        if (contradictory.Length > 0)
        {
            errors.Add(new Finding("GATE_LANE_CONFLICT", "quality/test-gates.json", $"Gate '{gate.Name}' declares lane '{gate.Lane}' but selects {contradictory.Length} test(s) from another lane."));
        }

        gateReports.Add(new GateAudit(gate.Name, gate.Project, gate.Lane, gate.Filter, matches.Count));
    }

    var knownGates = gateConfig.Gates.Select(gate => gate.Name).ToHashSet(StringComparer.Ordinal);
    foreach (var (lane, gateNames) in gateConfig.Lanes)
    {
        foreach (var gateName in gateNames)
        {
            if (!knownGates.Contains(gateName))
            {
                errors.Add(new Finding("LANE_GATE_MISSING", "quality/test-gates.json", $"Lane '{lane}' references missing gate '{gateName}'."));
            }
        }
    }

    var purposeCounts = CountBy(tests, test => test.Classification.Purpose);
    var laneCounts = CountBy(tests, test => test.Classification.Lane);
    var domainCounts = CountBy(tests, test => test.Classification.Domain);
    var report = new GovernanceReport(
        gateConfig.SchemaVersion,
        DateTimeOffset.UtcNow,
        tests.Count,
        errors.Count(finding => finding.Code == "CLASSIFICATION_MISSING"),
        purposeCounts,
        laneCounts,
        domainCounts,
        gateReports,
        errors,
        warnings);
    return new AuditResult(report, tests);
}

static void ValidateClassification(
    Classification classification,
    MethodDeclarationSyntax method,
    string location,
    ICollection<Finding> errors,
    ICollection<Finding> warnings)
{
    if (string.IsNullOrWhiteSpace(classification.Owner))
    {
        errors.Add(new Finding("OWNER_MISSING", location, "Owner must be non-empty."));
    }

    if (classification.Lane == "Pr" && classification.Purpose == "Performance")
    {
        errors.Add(new Finding("PERFORMANCE_IN_PR", location, "Performance tests cannot enter the PR lane."));
    }

    if (classification.Lane == "Pr" && classification.ResourceRequirement is not ("None" or "PackageFeed"))
    {
        errors.Add(new Finding("RESOURCE_IN_PR", location, $"PR tests cannot require '{classification.ResourceRequirement}'."));
    }

    if (classification.Lane == "Pr" && classification.FlakyPolicy != "Blocking")
    {
        errors.Add(new Finding("PR_FLAKY_POLICY", location, "PR tests must be blocking and cannot rely on retry or quarantine."));
    }

    if (classification.Purpose == "Accuracy")
    {
        var validEvidence = classification.EvidenceType is "IndependentOracle" or "GoldenDataset";
        var validOracle = classification.OracleType is "Mathematical" or "AnnotatedGroundTruth" or "GoldenFile";
        if (!validEvidence || !validOracle)
        {
            errors.Add(new Finding("ACCURACY_ORACLE_INVALID", location, "Accuracy requires independent evidence and a mathematical, annotated-ground-truth, or golden-file oracle."));
        }

        if (!HasIndependentAccuracyAssertion(method))
        {
            errors.Add(new Finding("ACCURACY_ASSERTION_WEAK", location, "Accuracy cannot be established by IsSuccess alone; assert an independent numeric or annotated oracle."));
        }
    }

    if (classification.Purpose == "Determinism" && string.IsNullOrWhiteSpace(classification.SeedControl))
    {
        errors.Add(new Finding("DETERMINISM_SEED_MISSING", location, "Determinism tests must declare SeedControl, including an explicit NotApplicable reason when no randomized component exists."));
    }

    if (classification.Purpose == "Stability" && classification.OracleType is not ("Statistical" or "Metamorphic"))
    {
        errors.Add(new Finding("STABILITY_ORACLE_INVALID", location, "Stability requires a statistical or metamorphic oracle over perturbed, boundary, scale, bit-depth, or multi-seed inputs."));
    }

    if (classification.Purpose == "Performance")
    {
        if (classification.OracleType != "PerformanceBudget" || classification.EvidenceType != "PerformanceProfile")
        {
            errors.Add(new Finding("PERFORMANCE_ORACLE_INVALID", location, "Performance tests require PerformanceProfile evidence and a PerformanceBudget oracle."));
        }

        if (string.IsNullOrWhiteSpace(classification.PerformanceProfile))
        {
            errors.Add(new Finding("PERFORMANCE_PROFILE_MISSING", location, "Performance tests must declare environment, data scale, warmup, measurement, and statistic policy through PerformanceProfile."));
        }
    }

    if (classification.Purpose == "Regression" && classification.OracleType == "None")
    {
        warnings.Add(new Finding("REGRESSION_ORACLE_NONE", location, "Regression tests should identify their contract or historical oracle."));
    }
}

static bool HasIndependentAccuracyAssertion(MethodDeclarationSyntax method)
{
    var acceptedAssertionNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Equal",
        "InRange",
        "Be",
        "BeApproximately",
        "BeInRange",
        "BeEquivalentTo",
        "BeEmpty",
        "BeLessThan",
        "BeLessThanOrEqualTo",
        "BeGreaterThan",
        "BeGreaterThanOrEqualTo",
        "OnlyContain",
        "AllSatisfy",
        "ContainSingle"
    };

    foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        var assertionName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => string.Empty
        };
        if (!acceptedAssertionNames.Contains(assertionName))
        {
            continue;
        }

        if (!Regex.IsMatch(invocation.ToString(), @"\bIsSuccess\b", RegexOptions.CultureInvariant))
        {
            return true;
        }
    }

    return false;
}

static Classification? ParseClassification(AttributeSyntax attribute, string location, ICollection<Finding> errors)
{
    var arguments = attribute.ArgumentList?.Arguments ?? default;
    var positional = arguments.Where(argument => argument.NameEquals is null).Take(9).ToArray();
    if (positional.Length != 9)
    {
        errors.Add(new Finding("CLASSIFICATION_SCHEMA_INVALID", location, "TestClassification must provide all nine positional fields."));
        return null;
    }

    var values = positional.Select(argument => ParseValue(argument.Expression)).ToArray();
    var named = arguments
        .Where(argument => argument.NameEquals is not null)
        .ToDictionary(argument => argument.NameEquals!.Name.Identifier.ValueText, argument => ParseValue(argument.Expression), StringComparer.Ordinal);
    var suites = named.GetValueOrDefault("Suites", string.Empty)
        .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    return new Classification(
        values[0],
        values[1],
        values[2],
        values[3],
        values[4],
        values[5],
        values[6],
        values[7],
        values[8],
        suites,
        named.GetValueOrDefault("SeedControl", string.Empty),
        named.GetValueOrDefault("PerformanceProfile", string.Empty));
}

static string ParseValue(ExpressionSyntax expression) => expression switch
{
    LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
    _ => expression.ToString().Trim('"')
};

static IEnumerable<AttributeSyntax> GetClassificationAttributes(SyntaxList<AttributeListSyntax> attributeLists) =>
    attributeLists.SelectMany(list => list.Attributes).Where(attribute =>
        attribute.Name.ToString() is "TestClassification" or "TestClassificationAttribute" or
        "ClearVision.Testing.TestClassification" or "ClearVision.Testing.TestClassificationAttribute");

static bool IsTestMethod(MethodDeclarationSyntax method) =>
    method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
        attribute.Name.ToString() is "Fact" or "FactAttribute" or "Theory" or "TheoryAttribute" or
        "Xunit.Fact" or "Xunit.FactAttribute" or "Xunit.Theory" or "Xunit.TheoryAttribute");

static string ResolveNamespace(TypeDeclarationSyntax type)
{
    var segments = type.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(parent => parent.Identifier.ValueText).ToList();
    segments.Insert(0, type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty);
    return string.Join('.', segments.Where(segment => segment.Length > 0));
}

static bool IsSourceFile(string path) =>
    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

static bool MatchesFilter(IReadOnlyDictionary<string, IReadOnlySet<string>> traits, string filter)
{
    if (string.IsNullOrWhiteSpace(filter))
    {
        return true;
    }

    foreach (var orClause in filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var matches = true;
        foreach (var condition in orClause.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var notEquals = condition.Contains("!=", StringComparison.Ordinal);
            var parts = condition.Split(notEquals ? "!=" : "=", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw new FormatException($"Invalid condition '{condition}'.");
            }

            var contains = traits.TryGetValue(parts[0], out var values) && values.Contains(parts[1]);
            if (notEquals ? contains : !contains)
            {
                matches = false;
                break;
            }
        }

        if (matches)
        {
            return true;
        }
    }

    return false;
}

static SortedDictionary<string, int> CountBy(IEnumerable<ClassifiedTest> tests, Func<ClassifiedTest, string> selector) =>
    new(tests.GroupBy(selector, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), StringComparer.Ordinal);

static string BuildMarkdown(GovernanceReport report)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Test Governance Report");
    builder.AppendLine();
    builder.AppendLine($"- Schema: `{report.SchemaVersion}`");
    builder.AppendLine($"- Generated UTC: `{report.GeneratedAtUtc:O}`");
    builder.AppendLine($"- Source tests: {report.SourceTestCount}");
    builder.AppendLine($"- Unclassified: {report.Unclassified}");
    builder.AppendLine($"- Errors: {report.Errors.Count}");
    builder.AppendLine($"- Warnings: {report.Warnings.Count}");
    AppendCounts(builder, "Purpose", report.PurposeCounts);
    AppendCounts(builder, "Lane", report.LaneCounts);
    AppendCounts(builder, "Domain", report.DomainCounts);
    builder.AppendLine();
    builder.AppendLine("## Gates");
    builder.AppendLine();
    builder.AppendLine("| Gate | Project | Lane | Source tests | Filter |");
    builder.AppendLine("| --- | --- | --- | ---: | --- |");
    foreach (var gate in report.Gates)
    {
        builder.AppendLine($"| {gate.Name} | {gate.Project} | {gate.Lane} | {gate.SourceTestCount} | `{gate.Filter}` |");
    }
    AppendFindings(builder, "Errors", report.Errors);
    AppendFindings(builder, "Warnings", report.Warnings);
    return builder.ToString();
}

static void AppendCounts(StringBuilder builder, string title, IReadOnlyDictionary<string, int> counts)
{
    builder.AppendLine();
    builder.AppendLine($"## {title}");
    builder.AppendLine();
    foreach (var (key, value) in counts)
    {
        builder.AppendLine($"- {key}: {value}");
    }
}

static void AppendFindings(StringBuilder builder, string title, IReadOnlyList<Finding> findings)
{
    builder.AppendLine();
    builder.AppendLine($"## {title}");
    builder.AppendLine();
    if (findings.Count == 0)
    {
        builder.AppendLine("- None");
        return;
    }

    foreach (var finding in findings)
    {
        builder.AppendLine($"- `{finding.Code}` `{finding.Location}`: {finding.Message}");
    }
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(directory.FullName, "quality")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root.");
}

internal sealed record Options(string? RepoRoot, string? ReportDirectory, bool FailOnWarning)
{
    public static Options Parse(string[] args)
    {
        string? repoRoot = null;
        string? reportDirectory = null;
        var failOnWarning = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo-root":
                    repoRoot = Path.GetFullPath(args[++index]);
                    break;
                case "--report-directory":
                    reportDirectory = Path.GetFullPath(args[++index]);
                    break;
                case "--fail-on-warning":
                    failOnWarning = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        return new Options(repoRoot, reportDirectory, failOnWarning);
    }
}

internal sealed record Classification(
    string Domain,
    string Purpose,
    string Lane,
    string EvidenceType,
    string OracleType,
    string ResourceRequirement,
    string ExpectedDuration,
    string FlakyPolicy,
    string Owner,
    IReadOnlyList<string> Suites,
    string SeedControl,
    string PerformanceProfile)
{
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Traits { get; } = BuildTraits(
        Domain, Purpose, Lane, EvidenceType, OracleType, ResourceRequirement, ExpectedDuration, FlakyPolicy, Owner, Suites, SeedControl, PerformanceProfile);

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildTraits(
        string domain,
        string purpose,
        string lane,
        string evidenceType,
        string oracleType,
        string resourceRequirement,
        string expectedDuration,
        string flakyPolicy,
        string owner,
        IReadOnlyList<string> suites,
        string seedControl,
        string performanceProfile)
    {
        var traits = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["Domain"] = Set(domain),
            ["Purpose"] = Set(purpose),
            ["Lane"] = Set(lane),
            ["EvidenceType"] = Set(evidenceType),
            ["OracleType"] = Set(oracleType),
            ["ResourceRequirement"] = Set(resourceRequirement),
            ["ExpectedDuration"] = Set(expectedDuration),
            ["FlakyPolicy"] = Set(flakyPolicy),
            ["Owner"] = Set(owner),
            ["Suite"] = new HashSet<string>(suites, StringComparer.Ordinal)
        };
        if (seedControl.Length > 0) traits["SeedControl"] = Set(seedControl);
        if (performanceProfile.Length > 0) traits["PerformanceProfile"] = Set(performanceProfile);
        return traits;
    }

    private static IReadOnlySet<string> Set(string value) => new HashSet<string>(new[] { value }, StringComparer.Ordinal);
}

internal sealed record ClassifiedTest(string Project, string Name, string SourcePath, Classification Classification);
internal sealed record Finding(string Code, string Location, string Message);
internal sealed record GateAudit(string Name, string Project, string Lane, string Filter, int SourceTestCount);
internal sealed record GovernanceReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int SourceTestCount,
    int Unclassified,
    IReadOnlyDictionary<string, int> PurposeCounts,
    IReadOnlyDictionary<string, int> LaneCounts,
    IReadOnlyDictionary<string, int> DomainCounts,
    IReadOnlyList<GateAudit> Gates,
    IReadOnlyList<Finding> Errors,
    IReadOnlyList<Finding> Warnings);
internal sealed record AuditResult(GovernanceReport Report, IReadOnlyList<ClassifiedTest> Tests);

internal sealed class GateConfiguration
{
    public string SchemaVersion { get; set; } = string.Empty;
    public Dictionary<string, string> Projects { get; set; } = new(StringComparer.Ordinal);
    public List<GateDefinition> Gates { get; set; } = new();
    public Dictionary<string, List<string>> Lanes { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GateDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public int MinimumTotalTests { get; set; } = 1;
}
