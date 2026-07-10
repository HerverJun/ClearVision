using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ClearVision.OperatorLibrary.Modules;

namespace ClearVision.OperatorLibrary.ReadOnlyAudit;

public static class AuditSchema
{
    public const string Version = "2026-07-10.operator-audit.v2";
    public const string SummaryVersion = "2026-07-10.operator-audit-summary.v1";
    public const string ReadOnlyMode = "read-only";

    public static readonly IReadOnlySet<string> Classifications =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "confirmed",
            "candidate",
            "not-reproduced"
        };
}

public sealed record AuditOptions(
    string RepoRoot,
    string? SourceCommitSha,
    bool ReportOnly = false);

public sealed record AuditArtifacts(
    AuditReport Report,
    AuditSummary Summary,
    string Json,
    string Markdown,
    string SummaryJson);

public sealed record AuditAliasMapping(
    string Alias,
    string Canonical);

public sealed record AuditSurface(
    string Name,
    string Source,
    int ObservedCount,
    IReadOnlyList<string> Identities,
    IReadOnlyList<string> DuplicateIdentities);

public sealed record AuditIdentityDifference(
    string LeftSurface,
    string RightSurface,
    IReadOnlyList<string> MissingFromRight,
    IReadOnlyList<string> ExtraInRight);

public sealed record AuditFinding(
    string Code,
    string Severity,
    string Confidence,
    string Classification,
    string Operator,
    string Field,
    IReadOnlyList<string> Evidence,
    string Impact,
    string SuggestedAction);

public sealed record ManualReviewSample(
    string SampleId,
    string Operator,
    string Category,
    string AuditConclusion,
    IReadOnlyList<string> SourceEvidence,
    bool FalsePositive,
    string CandidateDegradationReason);

public sealed record ManualReviewSummary(
    int SampleCount,
    int FalsePositiveCount,
    IReadOnlyList<string> Categories);

public sealed record AuditSummary(
    string SchemaVersion,
    string SourceCommitSha,
    int CanonicalOperatorCount,
    int LegacyAliasCount,
    int ScannerCount,
    int FactoryCount,
    int CatalogCount,
    int OperatorLibraryCount,
    int AiContractVisibleCount,
    int ConfirmedCount,
    int CandidateCount,
    int NotReproducedCount,
    int DuplicateCount,
    int MissingCount,
    int SourceDifferenceCount,
    bool SchemaValid,
    bool Deterministic,
    IReadOnlyDictionary<string, int> SurfaceCounts,
    IReadOnlyDictionary<string, int> FindingCounts,
    ManualReviewSummary ManualReview);

public sealed record AuditReport(
    string SchemaVersion,
    string AuditMode,
    string SourceCommitSha,
    IReadOnlyList<string> CanonicalOperators,
    IReadOnlyList<AuditAliasMapping> LegacyAliases,
    IReadOnlyList<AuditSurface> Surfaces,
    IReadOnlyList<AuditIdentityDifference> IdentityDifferences,
    IReadOnlyList<AuditFinding> Findings,
    IReadOnlyList<ManualReviewSample> ManualReviewSamples,
    AuditSummary Summary);

public static class AuditEngine
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static AuditArtifacts Generate(AuditOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var repoRoot = Path.GetFullPath(options.RepoRoot);
        EnsureRepositoryShape(repoRoot);

        var sourceCommitSha = string.IsNullOrWhiteSpace(options.SourceCommitSha)
            ? ResolveHeadSha(repoRoot)
            : options.SourceCommitSha.Trim();
        if (string.IsNullOrWhiteSpace(sourceCommitSha))
        {
            throw new InvalidOperationException("A stable sourceCommitSha is required.");
        }

        var scanner = new OperatorMetadataScanner();
        var scannedMetadata = scanner.Scan()
            .OrderBy(item => item.Type.ToString(), Comparer)
            .ToList();
        var factory = new OperatorFactory();
        var factoryMetadata = factory.GetAllMetadata()
            .OrderBy(item => item.Type.ToString(), Comparer)
            .ToList();
        var factoryByName = factoryMetadata.ToDictionary(
            item => item.Type.ToString(),
            Comparer);

        var aliases = BuildAliasMappings();
        var canonicalNames = factoryMetadata
            .Select(item => item.Type.ToString())
            .OrderBy(item => item, Comparer)
            .ToArray();
        var scannerNames = scannedMetadata.Select(item => item.Type.ToString()).ToArray();
        var scannerCanonicalNames = scannerNames
            .Select(Canonicalize)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToArray();

        var sourceAnalysis = RoslynOperatorSourceAnalyzer.Analyze(repoRoot);
        var catalogNames = ReadFormalCatalogIdentities();
        var libraryNames = ReadOperatorLibraryIdentities(sourceAnalysis);
        var aiNames = ReadAiContractIdentities(factory);

        var surfaces = new[]
        {
            CreateSurface("scanner", "OperatorMetadataScanner.Scan", scannerNames),
            CreateSurface("scannerCanonical", "OperatorMetadataScanner.Scan normalized by OperatorTypeAliasResolver", scannerCanonicalNames),
            CreateSurface("factory", "OperatorFactory.GetAllMetadata", canonicalNames),
            CreateSurface("catalog", "ClearVision.OperatorLibrary.Modules.OperatorModuleCatalog", catalogNames),
            CreateSurface("operatorLibrary", "ClearVision.OperatorLibrary packaged operator assembly", libraryNames),
            CreateSurface("aiContract", "VisionAgentOperatorContractCatalog", aiNames)
        };

        var identityDifferences = BuildIdentityDifferences(surfaces);
        var findings = new List<AuditFinding>();
        AddIdentityFindings(findings, identityDifferences);
        AddAliasFindings(findings, aliases, canonicalNames);

        foreach (var metadata in factoryMetadata)
        {
            findings.AddRange(AuditRuleEngine.ValidateMetadata(
                metadata,
                RelativePath(repoRoot, FindMetadataSourcePath(repoRoot, metadata.Type.ToString()))));
        }

        findings.AddRange(sourceAnalysis.UnknownPortDataTypes.Select(item =>
            AuditRuleEngine.UnknownPortDataTypeFinding(item.Operator, item.Field, item.Evidence)));
        AddCandidateFindings(findings, factoryMetadata, sourceAnalysis);
        AddExplicitReferenceFindings(findings, repoRoot, factoryMetadata, aiNames, factoryByName);
        AddDocumentationFindings(findings, repoRoot, canonicalNames);

        var sortedFindings = findings
            .Where(IsValidFinding)
            .OrderBy(item => item.Code, Comparer)
            .ThenBy(item => item.Operator, Comparer)
            .ThenBy(item => item.Field, Comparer)
            .ThenBy(item => string.Join("\u001f", item.Evidence), Comparer)
            .ToList();

        var manualReview = BuildManualReviewSamples(
            factoryMetadata,
            sortedFindings,
            sourceAnalysis,
            repoRoot);
        var summary = BuildSummary(
            sourceCommitSha,
            canonicalNames,
            aliases,
            surfaces,
            identityDifferences,
            sortedFindings,
            manualReview,
            schemaValid: true);
        var report = new AuditReport(
            AuditSchema.Version,
            AuditSchema.ReadOnlyMode,
            sourceCommitSha,
            canonicalNames,
            aliases,
            surfaces,
            identityDifferences,
            sortedFindings,
            manualReview,
            summary);

        var schemaErrors = AuditSchemaValidator.Validate(report);
        if (schemaErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Audit report schema validation failed: " + string.Join("; ", schemaErrors));
        }

        var finalSummary = summary with { SchemaValid = true };
        report = report with { Summary = finalSummary };
        var json = AuditSerialization.SerializeReport(report);
        var summaryJson = AuditSerialization.SerializeSummary(finalSummary);
        var markdown = AuditSerialization.SerializeMarkdown(report);
        return new AuditArtifacts(report, finalSummary, json, markdown, summaryJson);
    }

    private static void EnsureRepositoryShape(string repoRoot)
    {
        var required = new[]
        {
            Path.Combine(repoRoot, "ClearVision.Product"),
            Path.Combine(repoRoot, "ClearVision.OperatorLibrary"),
            Path.Combine(repoRoot, "ClearVision.Product", "src", "ClearVision.Product.Infrastructure", "Operators")
        };
        if (required.Any(path => !Directory.Exists(path)))
        {
            throw new DirectoryNotFoundException("The repository root does not contain the ClearVision source layout.");
        }
    }

    private static string ResolveHeadSha(string repoRoot)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "rev-parse", "HEAD" }
        });
        if (process is null)
        {
            throw new InvalidOperationException("Could not start git to resolve sourceCommitSha.");
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"Could not resolve sourceCommitSha: {error}");
        }

        return output;
    }

    private static IReadOnlyList<AuditAliasMapping> BuildAliasMappings()
    {
        return Enum.GetValues<OperatorType>()
            .Where(OperatorTypeAliasResolver.IsLegacyAlias)
            .Select(alias => new AuditAliasMapping(
                alias.ToString(),
                OperatorTypeAliasResolver.Resolve(alias).ToString()))
            .OrderBy(item => item.Alias, Comparer)
            .ToArray();
    }

    private static string Canonicalize(string name)
    {
        if (Enum.TryParse<OperatorType>(name, true, out var parsed))
        {
            return OperatorTypeAliasResolver.Resolve(parsed).ToString();
        }

        return name.Trim();
    }

    private static AuditSurface CreateSurface(
        string name,
        string source,
        IEnumerable<string> observedIdentities)
    {
        var observed = observedIdentities
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToArray();
        var duplicates = observed
            .GroupBy(item => item, Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(item => item, Comparer)
            .ToArray();
        var identities = observed
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToArray();
        return new AuditSurface(name, source, observed.Length, identities, duplicates);
    }

    private static IReadOnlyList<AuditIdentityDifference> BuildIdentityDifferences(
        IReadOnlyList<AuditSurface> surfaces)
    {
        var byName = surfaces.ToDictionary(item => item.Name, Comparer);
        var pairs = new[]
        {
            ("scannerCanonical", "factory"),
            ("factory", "catalog"),
            ("catalog", "operatorLibrary"),
            ("factory", "aiContract")
        };
        return pairs
            .Select(pair =>
            {
                var left = byName[pair.Item1].Identities.ToHashSet(Comparer);
                var right = byName[pair.Item2].Identities.ToHashSet(Comparer);
                return new AuditIdentityDifference(
                    pair.Item1,
                    pair.Item2,
                    left.Except(right, Comparer).OrderBy(item => item, Comparer).ToArray(),
                    right.Except(left, Comparer).OrderBy(item => item, Comparer).ToArray());
            })
            .Where(item => item.MissingFromRight.Count > 0 || item.ExtraInRight.Count > 0)
            .OrderBy(item => item.LeftSurface, Comparer)
            .ThenBy(item => item.RightSurface, Comparer)
            .ToArray();
    }

    private static void AddIdentityFindings(
        ICollection<AuditFinding> findings,
        IEnumerable<AuditIdentityDifference> differences)
    {
        foreach (var difference in differences)
        {
            foreach (var identity in difference.MissingFromRight)
            {
                findings.Add(new AuditFinding(
                    "SURFACE_IDENTITY_MISMATCH",
                    "warning",
                    "high",
                    "confirmed",
                    identity,
                    $"{difference.LeftSurface}->{difference.RightSurface}",
                    [$"{difference.LeftSurface} contains {identity}; {difference.RightSurface} does not."],
                    "The named audit surfaces do not expose the same operator identity.",
                    "Resolve the source-boundary difference or explicitly document the intentional boundary."));
            }

            foreach (var identity in difference.ExtraInRight)
            {
                findings.Add(new AuditFinding(
                    "SURFACE_IDENTITY_MISMATCH",
                    "warning",
                    "high",
                    "confirmed",
                    identity,
                    $"{difference.RightSurface}->{difference.LeftSurface}",
                    [$"{difference.RightSurface} contains {identity}; {difference.LeftSurface} does not."],
                    "The named audit surfaces do not expose the same operator identity.",
                    "Resolve the source-boundary difference or explicitly document the intentional boundary."));
            }
        }
    }

    private static void AddAliasFindings(
        ICollection<AuditFinding> findings,
        IReadOnlyList<AuditAliasMapping> aliases,
        IReadOnlyList<string> canonicalNames)
    {
        var canonical = canonicalNames.ToHashSet(Comparer);
        foreach (var alias in aliases)
        {
            if (string.Equals(alias.Alias, alias.Canonical, StringComparison.OrdinalIgnoreCase) ||
                !canonical.Contains(alias.Canonical))
            {
                findings.Add(new AuditFinding(
                    "ALIAS_MAPPING_BROKEN",
                    "error",
                    "high",
                    "confirmed",
                    alias.Alias,
                    "alias->canonical",
                    [$"OperatorTypeAliasResolver.Resolve({alias.Alias}) = {alias.Canonical}"],
                    "Legacy flows cannot be resolved to a canonical factory operator.",
                    "Repair the formal resolver mapping or restore the canonical operator metadata."));
            }
        }
    }

    private static void AddCandidateFindings(
        ICollection<AuditFinding> findings,
        IReadOnlyList<OperatorMetadata> metadata,
        SourceAnalysisResult analysis)
    {
        foreach (var item in metadata)
        {
            var name = item.Type.ToString();
            if (!analysis.Observations.TryGetValue(name, out var observations) || observations.Count == 0)
            {
                findings.Add(new AuditFinding(
                    "PARAMETER_ACCESS_UNPROVEN",
                    "info",
                    "low",
                    "candidate",
                    name,
                    "parameters",
                    ["No operator source class could be associated with the scanned metadata."],
                    "Parameter usage cannot be established from the available source model.",
                    "Review the operator source association; do not treat absence of evidence as proof of an unused parameter."));
                continue;
            }

            var merged = SourceOperatorObservation.Merge(observations);
            var sourceEvidence = merged.Evidence;
            foreach (var parameter in item.Parameters.OrderBy(parameter => parameter.Name, Comparer))
            {
                if (!merged.ParameterReads.Contains(parameter.Name) && !merged.HasDynamicParameterAccess)
                {
                    findings.Add(new AuditFinding(
                        "PARAMETER_SUSPECTED_UNUSED",
                        "warning",
                        "medium",
                        "candidate",
                        name,
                        parameter.Name,
                        sourceEvidence,
                        "A metadata parameter has no proven read in the reachable source evidence.",
                        "Review helper, base-class, alias, and dynamic dictionary paths before changing the parameter contract."));
                }
            }

            foreach (var fallback in merged.ParameterDefaults)
            {
                var parameter = item.Parameters.FirstOrDefault(itemParameter =>
                    itemParameter.Name.Equals(fallback.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter is null || DefaultValuesEqual(parameter.DefaultValue, fallback.Value))
                {
                    continue;
                }

                findings.Add(new AuditFinding(
                    "GET_PARAM_DEFAULT_MISMATCH",
                    "warning",
                    "medium",
                    "candidate",
                    name,
                    fallback.Name,
                    [$"{fallback.Evidence}; metadata default={FormatValue(parameter.DefaultValue)}; source fallback={fallback.Value}"],
                    "Runtime fallback behavior may diverge from the formal metadata default.",
                    "Confirm the intended default across direct calls, helpers, aliases, and all execution paths."));
            }

            var outputNames = item.OutputPorts
                .Select(port => port.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(Comparer);
            foreach (var output in outputNames.OrderBy(value => value, Comparer))
            {
                if (merged.DictionaryKeys.Contains(output) || merged.HasDynamicOutputDictionary)
                {
                    continue;
                }

                findings.Add(new AuditFinding(
                    "OUTPUT_KEY_NO_SUCCESS_PATH",
                    "warning",
                    "low",
                    "candidate",
                    name,
                    output,
                    sourceEvidence,
                    "The output metadata key was not proven to be written on a successful path by the static model.",
                    "Trace success construction, helper returns, and dynamic dictionaries before changing the output contract."));
            }

            foreach (var key in merged.DictionaryKeys
                         .Where(key => !outputNames.Contains(key))
                         .OrderBy(key => key, Comparer))
            {
                findings.Add(new AuditFinding(
                    "RUNTIME_OUTPUT_UNDOCUMENTED",
                    "warning",
                    "low",
                    "candidate",
                    name,
                    key,
                    sourceEvidence,
                    "A runtime dictionary key may be returned without a matching output-port declaration.",
                    "Determine whether the key is an internal helper dictionary or a public output, then update the formal contract only when proven."));
            }

            if (merged.UsesPreviewTerms &&
                !item.Description.Contains("preview", StringComparison.OrdinalIgnoreCase) &&
                item.OutputPorts.All(port => !port.Description?.Contains("preview", StringComparison.OrdinalIgnoreCase) == true))
            {
                findings.Add(new AuditFinding(
                    "PREVIEW_SEMANTICS_AMBIGUOUS",
                    "info",
                    "low",
                    "candidate",
                    name,
                    "preview",
                    sourceEvidence,
                    "Source contains preview-related behavior without a corresponding metadata description.",
                    "Document the preview boundary or retain the behavior as an explicitly reviewable candidate."));
            }
        }
    }

    private static void AddExplicitReferenceFindings(
        ICollection<AuditFinding> findings,
        string repoRoot,
        IReadOnlyList<OperatorMetadata> metadata,
        IReadOnlyList<string> aiNames,
        IReadOnlyDictionary<string, OperatorMetadata> factoryByName)
    {
        var aiCatalog = new VisionAgentOperatorContractCatalog(new OperatorFactory());
        var knownParameterNames = metadata
            .SelectMany(item => item.Parameters.Select(parameter => parameter.Name))
            .ToHashSet(Comparer);

        foreach (var contract in aiCatalog.Operators.OrderBy(item => item.OperatorType, Comparer))
        {
            var canonical = Canonicalize(contract.OperatorType);
            if (!factoryByName.TryGetValue(canonical, out var operatorMetadata))
            {
                findings.Add(new AuditFinding(
                    "EXPLICIT_PARAMETER_REFERENCE_UNRESOLVED",
                    "warning",
                    "medium",
                    "candidate",
                    contract.OperatorType,
                    "operator",
                    ["VisionAgentOperatorContractCatalog contains an operator without a factory metadata identity."],
                    "Agent-visible contract coverage may refer to a supplemental or unavailable operator.",
                    "Confirm whether the contract is intentionally supplemental; otherwise bind it to the factory metadata."));
                continue;
            }

            foreach (var parameter in contract.Parameters.OrderBy(item => item.Name, Comparer))
            {
                if (operatorMetadata.Parameters.Any(item => item.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                findings.Add(new AuditFinding(
                    "EXPLICIT_PARAMETER_REFERENCE_UNRESOLVED",
                    "warning",
                    "high",
                    "confirmed",
                    canonical,
                    parameter.Name,
                    [$"VisionAgentOperatorContractCatalog.{contract.OperatorType}.Parameters references {parameter.Name}; factory metadata does not define it."],
                    "Agent-generated flows may emit a parameter that the runtime factory cannot resolve.",
                    "Align the formal AI contract with OperatorFactory metadata or remove the explicit reference."));
            }
        }

        var frontendRoot = Path.Combine(
            repoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Desktop",
            "wwwroot",
            "src");
        if (!Directory.Exists(frontendRoot))
        {
            return;
        }

        var explicitParameterPattern = new Regex(
            "(?:parameters?|parameterName|param(?:eter)?Name)\\s*(?:[:=]|\\[)\\s*[\\\"'](?<name>[A-Za-z][A-Za-z0-9_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(frontendRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in explicitParameterPattern.Matches(text))
            {
                var parameter = match.Groups["name"].Value;
                if (knownParameterNames.Contains(parameter))
                {
                    continue;
                }

                var line = 1 + text[..match.Index].Count(character => character == '\n');
                findings.Add(new AuditFinding(
                    "EXPLICIT_PARAMETER_REFERENCE_UNRESOLVED",
                    "warning",
                    "low",
                    "candidate",
                    "frontend",
                    parameter,
                    [$"{RelativePath(repoRoot, file)}:{line}: explicit parameter literal {parameter} is not present in any factory metadata."],
                    "A front-end parameter reference may be stale or may belong to a dynamic operator-specific contract.",
                    "Resolve the reference against the selected operator contract; keep dynamic or cross-operator cases as candidates."));
            }
        }

        _ = aiNames;
    }

    private static void AddDocumentationFindings(
        ICollection<AuditFinding> findings,
        string repoRoot,
        IReadOnlyList<string> canonicalNames)
    {
        var path = Path.Combine(repoRoot, "docs", "operators", "catalog.json");
        if (!File.Exists(path))
        {
            findings.Add(new AuditFinding(
                "DOC_DETERMINISM_DIFF",
                "warning",
                "high",
                "confirmed",
                "catalog",
                "docs/operators/catalog.json",
                ["The checked-in operator catalog is missing."],
                "Documentation cannot be verified against the formal operator boundary.",
                "Regenerate the documentation through the existing generator and review the diff."));
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("operators", out var operators) ||
                operators.ValueKind != JsonValueKind.Array)
            {
                findings.Add(new AuditFinding(
                    "DOC_DETERMINISM_DIFF",
                    "warning",
                    "high",
                    "confirmed",
                    "catalog",
                    "operators",
                    ["docs/operators/catalog.json does not contain an operators array."],
                    "The checked-in documentation catalog is not reproducible from its declared shape.",
                    "Restore the formal catalog schema before regenerating documentation."));
                return;
            }

            var ids = operators.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out _))
                .Select(item => item.GetProperty("id").GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            var sorted = ids.OrderBy(item => item, Comparer).ToArray();
            var duplicate = ids.GroupBy(item => item, Comparer).Any(group => group.Count() > 1);
            var identityMismatch = !ids.Select(Canonicalize).ToHashSet(Comparer)
                .SetEquals(canonicalNames);
            if (duplicate || !ids.SequenceEqual(sorted, Comparer) || identityMismatch)
            {
                var evidence = new List<string>
                {
                    $"documentation identities={ids.Length}; formal identities={canonicalNames.Count()}"
                };
                if (duplicate)
                {
                    evidence.Add("documentation catalog contains duplicate identities");
                }

                if (!ids.SequenceEqual(sorted, Comparer))
                {
                    evidence.Add("documentation identities are not in stable ordinal-insensitive order");
                }

                if (identityMismatch)
                {
                    evidence.Add("documentation identity set differs from OperatorFactory metadata");
                }

                findings.Add(new AuditFinding(
                    "DOC_DETERMINISM_DIFF",
                    "warning",
                    "high",
                    "confirmed",
                    "catalog",
                    "docs/operators/catalog.json",
                    evidence,
                    "Documentation regeneration would produce a stable identity or ordering diff.",
                    "Regenerate only the intended documentation surface and review the deterministic diff."));
            }
        }
        catch (JsonException ex)
        {
            findings.Add(new AuditFinding(
                "DOC_DETERMINISM_DIFF",
                "warning",
                "high",
                "confirmed",
                "catalog",
                "docs/operators/catalog.json",
                [$"Invalid JSON: {ex.Message}"],
                "Documentation cannot be compared deterministically.",
                "Repair the checked-in JSON before regenerating documentation."));
        }
    }

    private static IReadOnlyList<ManualReviewSample> BuildManualReviewSamples(
        IReadOnlyList<OperatorMetadata> metadata,
        IReadOnlyList<AuditFinding> findings,
        SourceAnalysisResult analysis,
        string repoRoot)
    {
        var selected = new List<OperatorMetadata>();
        foreach (var group in metadata
                     .OrderBy(item => item.Category, Comparer)
                     .ThenBy(item => item.Type.ToString(), Comparer)
                     .GroupBy(item => string.IsNullOrWhiteSpace(item.Category) ? "uncategorized" : item.Category, Comparer))
        {
            selected.Add(group.First());
        }

        foreach (var item in metadata.OrderBy(item => item.Type.ToString(), Comparer))
        {
            if (selected.Any(existing => existing.Type == item.Type))
            {
                continue;
            }

            selected.Add(item);
            if (selected.Count >= 15)
            {
                break;
            }
        }

        if (selected.Count < 15)
        {
            throw new InvalidOperationException("At least 15 cross-category manual review samples are required.");
        }

        return selected
            .Take(15)
            .Select((item, index) =>
            {
                var operatorName = item.Type.ToString();
                var operatorFindings = findings
                    .Where(finding => finding.Operator.Equals(operatorName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var confirmed = operatorFindings.Any(finding => finding.Classification == "confirmed");
                var candidate = operatorFindings.Any(finding => finding.Classification == "candidate");
                var conclusion = confirmed ? "confirmed" : candidate ? "candidate" : "not-reproduced";
                var evidence = analysis.Observations.TryGetValue(operatorName, out var observations)
                    ? SourceOperatorObservation.Merge(observations).Evidence
                    : [$"{RelativePath(repoRoot, FindMetadataSourcePath(repoRoot, operatorName))}: metadata only"];
                var reason = conclusion switch
                {
                    "candidate" => "Roslyn found a static signal, but helper, base-class, alias, indirect-call, or dynamic-dictionary reachability cannot be proven.",
                    "confirmed" => "The deterministic rule has a direct formal metadata or source-boundary violation.",
                    _ => "No deterministic violation was reproduced in the available source and metadata evidence."
                };
                return new ManualReviewSample(
                    $"MR-{index + 1:00}",
                    operatorName,
                    string.IsNullOrWhiteSpace(item.Category) ? "uncategorized" : item.Category,
                    conclusion,
                    evidence,
                    false,
                    reason);
            })
            .OrderBy(item => item.SampleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static AuditSummary BuildSummary(
        string sourceCommitSha,
        IReadOnlyList<string> canonicalNames,
        IReadOnlyList<AuditAliasMapping> aliases,
        IReadOnlyList<AuditSurface> surfaces,
        IReadOnlyList<AuditIdentityDifference> differences,
        IReadOnlyList<AuditFinding> findings,
        IReadOnlyList<ManualReviewSample> manualReview,
        bool schemaValid)
    {
        var findingCounts = findings
            .GroupBy(item => item.Classification, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var classification in AuditSchema.Classifications)
        {
            findingCounts.TryAdd(classification, 0);
        }

        var surfaceCounts = surfaces
            .OrderBy(item => item.Name, Comparer)
            .ToDictionary(item => item.Name, item => item.Identities.Count, StringComparer.Ordinal);
        var duplicateCount = surfaces.Sum(item => item.DuplicateIdentities.Count);
        var missingCount = differences.Sum(item => item.MissingFromRight.Count);
        var sourceDifferenceCount = differences.Sum(item => item.MissingFromRight.Count + item.ExtraInRight.Count);
        var categories = manualReview
            .Select(item => item.Category)
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToArray();
        return new AuditSummary(
            AuditSchema.SummaryVersion,
            sourceCommitSha,
            canonicalNames.Count,
            aliases.Count,
            surfaceCounts.GetValueOrDefault("scanner"),
            surfaceCounts.GetValueOrDefault("factory"),
            surfaceCounts.GetValueOrDefault("catalog"),
            surfaceCounts.GetValueOrDefault("operatorLibrary"),
            surfaceCounts.GetValueOrDefault("aiContract"),
            findingCounts["confirmed"],
            findingCounts["candidate"],
            findingCounts["not-reproduced"],
            duplicateCount,
            missingCount,
            sourceDifferenceCount,
            schemaValid,
            true,
            new ReadOnlyDictionary<string, int>(surfaceCounts),
            new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(findingCounts, StringComparer.Ordinal)),
            new ManualReviewSummary(
                manualReview.Count,
                manualReview.Count(item => item.FalsePositive),
                categories));
    }

    private static bool IsValidFinding(AuditFinding finding)
    {
        return !string.IsNullOrWhiteSpace(finding.Code) &&
               !string.IsNullOrWhiteSpace(finding.Severity) &&
               !string.IsNullOrWhiteSpace(finding.Confidence) &&
               AuditSchema.Classifications.Contains(finding.Classification) &&
               !string.IsNullOrWhiteSpace(finding.Operator) &&
               !string.IsNullOrWhiteSpace(finding.Field) &&
               finding.Evidence.Count > 0 &&
               !string.IsNullOrWhiteSpace(finding.Impact) &&
               !string.IsNullOrWhiteSpace(finding.SuggestedAction);
    }

    private static string FindMetadataSourcePath(string repoRoot, string operatorName)
    {
        var sourceRoot = Path.Combine(
            repoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Operators");
        var file = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path)
                .Equals(operatorName + "Operator", StringComparison.OrdinalIgnoreCase));
        return file ?? Path.Combine(sourceRoot, operatorName + "Operator.cs");
    }

    private static string RelativePath(string repoRoot, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(repoRoot, fullPath);
        return relative.Replace('\\', '/');
    }

    private static bool DefaultValuesEqual(object? metadataValue, string sourceValue)
    {
        var metadataText = FormatValue(metadataValue);
        if (double.TryParse(metadataText, NumberStyles.Float, CultureInfo.InvariantCulture, out var metadataNumber) &&
            double.TryParse(sourceValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var sourceNumber))
        {
            return Math.Abs(metadataNumber - sourceNumber) <= 1e-12;
        }

        return string.Equals(metadataText, sourceValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is JsonElement element)
        {
            return element.ToString();
        }

        return value switch
        {
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IReadOnlyList<string> ReadFormalCatalogIdentities()
    {
        var values = new[]
        {
            OperatorModuleCatalog.ImageProcessingTypes,
            OperatorModuleCatalog.MeasurementTypes,
            OperatorModuleCatalog.CalibrationTypes,
            OperatorModuleCatalog.CommunicationTypes,
            OperatorModuleCatalog.FlowControlTypes,
            OperatorModuleCatalog.AiTypes
        };
        return values
            .SelectMany(items => items)
            .Select(item => item.ToString())
            .Select(Canonicalize)
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadOperatorLibraryIdentities(SourceAnalysisResult sourceAnalysis)
    {
        return sourceAnalysis.Observations.Keys
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadAiContractIdentities(OperatorFactory factory)
    {
        return new VisionAgentOperatorContractCatalog(factory).Operators
            .Select(item => Canonicalize(item.OperatorType))
            .Distinct(Comparer)
            .OrderBy(item => item, Comparer)
            .ToArray();
    }
}

public static class AuditRuleEngine
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<AuditFinding> ValidateMetadata(
        OperatorMetadata metadata,
        string evidencePath)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var findings = new List<AuditFinding>();
        var operatorName = metadata.Type.ToString();
        IReadOnlyList<string> evidence = string.IsNullOrWhiteSpace(evidencePath)
            ? new[] { "runtime metadata" }
            : new[] { evidencePath };

        AddDuplicateNameFindings(
            findings,
            operatorName,
            "DUPLICATE_PARAMETER_NAME",
            metadata.Parameters.Select(item => item.Name),
            evidence);
        AddDuplicateNameFindings(
            findings,
            operatorName,
            "DUPLICATE_INPUT_PORT_NAME",
            metadata.InputPorts.Select(item => item.Name),
            evidence);
        AddDuplicateNameFindings(
            findings,
            operatorName,
            "DUPLICATE_OUTPUT_PORT_NAME",
            metadata.OutputPorts.Select(item => item.Name),
            evidence);

        foreach (var parameter in metadata.Parameters.OrderBy(item => item.Name, Comparer))
        {
            if (TryDouble(parameter.MinValue, out var min) && TryDouble(parameter.MaxValue, out var max) && min > max)
            {
                findings.Add(new AuditFinding(
                    "MIN_GREATER_THAN_MAX",
                    "error",
                    "high",
                    "confirmed",
                    operatorName,
                    parameter.Name,
                    [$"{string.Join("/", evidence)}; min={min.ToString(CultureInfo.InvariantCulture)}; max={max.ToString(CultureInfo.InvariantCulture)}"],
                    "The parameter range cannot be satisfied consistently.",
                    "Correct the formal minimum and maximum values."));
            }

            if (parameter.Options is { Count: > 0 } options &&
                parameter.DefaultValue is not null &&
                !options.Any(option => string.Equals(
                    option.Value,
                    Format(parameter.DefaultValue),
                    StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new AuditFinding(
                    "ENUM_DEFAULT_NOT_IN_OPTIONS",
                    "error",
                    "high",
                    "confirmed",
                    operatorName,
                    parameter.Name,
                    [$"{string.Join("/", evidence)}; default={Format(parameter.DefaultValue)}; options={string.Join(",", options.Select(option => option.Value).OrderBy(item => item, Comparer))}"],
                    "The UI or runtime cannot select the declared default from the declared option set.",
                    "Align the enum default with one of the formal option values."));
            }
        }

        foreach (var port in metadata.InputPorts.Concat(metadata.OutputPorts))
        {
            if (!Enum.IsDefined(port.DataType))
            {
                findings.Add(UnknownPortDataTypeFinding(
                    operatorName,
                    port.Name,
                    [$"{string.Join("/", evidence)}; runtime value={(int)port.DataType}"]));
            }
        }

        return findings;
    }

    public static AuditFinding UnknownPortDataTypeFinding(
        string operatorName,
        string field,
        IReadOnlyList<string> evidence)
    {
        return new AuditFinding(
            "UNKNOWN_PORT_DATA_TYPE",
            "error",
            "high",
            "confirmed",
            string.IsNullOrWhiteSpace(operatorName) ? "unknown" : operatorName,
            string.IsNullOrWhiteSpace(field) ? "port" : field,
            evidence.Count == 0 ? ["source semantic model"] : evidence,
            "The port type is outside the formal PortDataType enum and cannot be safely connected.",
            "Use a declared PortDataType value or add the type through the formal contract boundary.");
    }

    private static void AddDuplicateNameFindings(
        ICollection<AuditFinding> findings,
        string operatorName,
        string code,
        IEnumerable<string> names,
        IReadOnlyList<string> evidence)
    {
        foreach (var duplicate in names
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .GroupBy(item => item, Comparer)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .OrderBy(item => item, Comparer))
        {
            findings.Add(new AuditFinding(
                code,
                "error",
                "high",
                "confirmed",
                operatorName,
                duplicate,
                evidence,
                "Duplicate names make the formal operator contract ambiguous.",
                "Rename the duplicate fields and keep names unique within the formal collection."));
        }
    }

    private static bool TryDouble(object? value, out double result)
    {
        if (value is null)
        {
            result = default;
            return false;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = convertible.ToDouble(CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                // Fall through to false.
            }
            catch (InvalidCastException)
            {
                // Fall through to false.
            }
        }

        result = default;
        return false;
    }

    private static string Format(object value)
    {
        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }
}

public static class AuditSchemaValidator
{
    public static IReadOnlyList<string> Validate(AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var errors = new List<string>();
        if (report.SchemaVersion != AuditSchema.Version)
        {
            errors.Add("schemaVersion");
        }

        if (report.AuditMode != AuditSchema.ReadOnlyMode)
        {
            errors.Add("auditMode");
        }

        if (string.IsNullOrWhiteSpace(report.SourceCommitSha))
        {
            errors.Add("sourceCommitSha");
        }

        if (!report.CanonicalOperators.SequenceEqual(
                report.CanonicalOperators.OrderBy(item => item, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("canonicalOperators.sort");
        }

        foreach (var finding in report.Findings)
        {
            if (!AuditSchema.Classifications.Contains(finding.Classification))
            {
                errors.Add($"finding.classification:{finding.Code}");
            }

            if (string.IsNullOrWhiteSpace(finding.Code) ||
                string.IsNullOrWhiteSpace(finding.Severity) ||
                string.IsNullOrWhiteSpace(finding.Confidence) ||
                string.IsNullOrWhiteSpace(finding.Operator) ||
                string.IsNullOrWhiteSpace(finding.Field) ||
                finding.Evidence.Count == 0 ||
                string.IsNullOrWhiteSpace(finding.Impact) ||
                string.IsNullOrWhiteSpace(finding.SuggestedAction))
            {
                errors.Add($"finding.shape:{finding.Code}");
            }
        }

        if (report.ManualReviewSamples.Count < 15)
        {
            errors.Add("manualReviewSamples.count");
        }

        return errors;
    }
}

public static class AuditSerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string SerializeReport(AuditReport report)
    {
        return Serialize(report);
    }

    public static string SerializeSummary(AuditSummary summary)
    {
        return Serialize(summary);
    }

    public static string SerializeMarkdown(AuditReport report)
    {
        var lines = new List<string>
        {
            "# Operator Library Read-Only Audit",
            string.Empty,
            $"- Schema: {report.SchemaVersion}",
            $"- Mode: {report.AuditMode}",
            $"- Source commit: {report.SourceCommitSha}",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Canonical operators | {report.Summary.CanonicalOperatorCount} |",
            $"| Legacy aliases | {report.Summary.LegacyAliasCount} |",
            $"| Confirmed | {report.Summary.ConfirmedCount} |",
            $"| Candidate | {report.Summary.CandidateCount} |",
            $"| Not reproduced | {report.Summary.NotReproducedCount} |",
            $"| Manual review samples | {report.Summary.ManualReview.SampleCount} |",
            $"| Manual review false positives | {report.Summary.ManualReview.FalsePositiveCount} |",
            string.Empty,
            "## Audit surfaces",
            string.Empty,
            "| Surface | Count | Source |",
            "| --- | ---: | --- |"
        };

        foreach (var surface in report.Surfaces.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"| {Escape(surface.Name)} | {surface.Identities.Count} | {Escape(surface.Source)} |");
        }

        lines.AddRange(
        [
            string.Empty,
            "## Aliases",
            string.Empty,
            "| Alias | Canonical |",
            "| --- | --- |"
        ]);
        foreach (var alias in report.LegacyAliases)
        {
            lines.Add($"| {Escape(alias.Alias)} | {Escape(alias.Canonical)} |");
        }

        lines.AddRange(
        [
            string.Empty,
            "## Findings",
            string.Empty,
            "| Code | Severity | Confidence | Classification | Operator | Field | Evidence |",
            "| --- | --- | --- | --- | --- | --- | --- |"
        ]);
        if (report.Findings.Count == 0)
        {
            lines.Add("| none | - | - | not-reproduced | - | - | No findings. |");
        }
        else
        {
            foreach (var finding in report.Findings)
            {
                lines.Add($"| {Escape(finding.Code)} | {Escape(finding.Severity)} | {Escape(finding.Confidence)} | {Escape(finding.Classification)} | {Escape(finding.Operator)} | {Escape(finding.Field)} | {Escape(string.Join("; ", finding.Evidence))} |");
            }
        }

        lines.AddRange(
        [
            string.Empty,
            "## Manual review samples",
            string.Empty,
            "| Sample | Operator | Category | Conclusion | False positive | Evidence | Candidate downgrade reason |",
            "| --- | --- | --- | --- | :---: | --- | --- |"
        ]);
        foreach (var sample in report.ManualReviewSamples)
        {
            lines.Add($"| {Escape(sample.SampleId)} | {Escape(sample.Operator)} | {Escape(sample.Category)} | {Escape(sample.AuditConclusion)} | {(sample.FalsePositive ? "yes" : "no")} | {Escape(string.Join("; ", sample.SourceEvidence))} | {Escape(sample.CandidateDegradationReason)} |");
        }

        lines.AddRange(
        [
            string.Empty,
            "## Read-only boundary",
            string.Empty,
            "The audit reads formal runtime metadata, the package module catalog, the AI contract catalog, and source syntax through Roslyn semantic models. It does not execute operators, load images or models, access network or hardware, or mutate source inputs.",
            string.Empty
        ]);
        return string.Join("\n", lines);
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions) + "\n";
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}

public static class AuditArtifactWriter
{
    public const string RelativeOutputDirectory = "artifacts/operator-audit";
    public const string JsonFileName = "operator-audit.json";
    public const string MarkdownFileName = "operator-audit.md";
    public const string SummaryFileName = "operator-audit-summary.json";

    public static void Write(string repoRoot, AuditArtifacts artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var directory = Path.Combine(Path.GetFullPath(repoRoot), RelativeOutputDirectory);
        Directory.CreateDirectory(directory);
        WriteUtf8(Path.Combine(directory, JsonFileName), artifacts.Json);
        WriteUtf8(Path.Combine(directory, MarkdownFileName), artifacts.Markdown);
        WriteUtf8(Path.Combine(directory, SummaryFileName), artifacts.SummaryJson);
    }

    private static void WriteUtf8(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

internal sealed record SourceParameterFallback(
    string Name,
    string Value,
    string Evidence);

internal sealed class SourceOperatorObservation
{
    public string Operator { get; init; } = string.Empty;
    public string EvidencePath { get; init; } = string.Empty;
    public int ClassLine { get; init; }
    public HashSet<string> ParameterReads { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SourceParameterFallback> ParameterDefaults { get; } = [];
    public HashSet<string> DictionaryKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasDynamicParameterAccess { get; set; }
    public bool HasDynamicOutputDictionary { get; set; }
    public bool UsesPreviewTerms { get; set; }

    public IReadOnlyList<string> Evidence =>
        [$"{EvidencePath}:{ClassLine}: Roslyn semantic model for {Operator}"];

    public static SourceOperatorObservation Merge(
        IReadOnlyList<SourceOperatorObservation> observations)
    {
        var first = observations
            .OrderBy(item => item.EvidencePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ClassLine)
            .First();
        var merged = new SourceOperatorObservation
        {
            Operator = first.Operator,
            EvidencePath = first.EvidencePath,
            ClassLine = first.ClassLine
        };
        foreach (var observation in observations)
        {
            merged.ParameterReads.UnionWith(observation.ParameterReads);
            merged.ParameterDefaults.AddRange(observation.ParameterDefaults);
            merged.DictionaryKeys.UnionWith(observation.DictionaryKeys);
            merged.HasDynamicParameterAccess |= observation.HasDynamicParameterAccess;
            merged.HasDynamicOutputDictionary |= observation.HasDynamicOutputDictionary;
            merged.UsesPreviewTerms |= observation.UsesPreviewTerms;
        }

        return merged;
    }
}

internal sealed record UnknownPortDataTypeObservation(
    string Operator,
    string Field,
    IReadOnlyList<string> Evidence);

internal sealed class SourceAnalysisResult
{
    public Dictionary<string, List<SourceOperatorObservation>> Observations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<UnknownPortDataTypeObservation> UnknownPortDataTypes { get; } = [];
}

internal static class RoslynOperatorSourceAnalyzer
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static SourceAnalysisResult Analyze(string repoRoot)
    {
        var result = new SourceAnalysisResult();
        var sourceRoot = Path.Combine(
            repoRoot,
            "ClearVision.Product",
            "src",
            "ClearVision.Product.Infrastructure",
            "Operators");
        var files = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var trees = files
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                new CSharpParseOptions(LanguageVersion.CSharp11),
                path: path))
            .ToArray();
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            "ClearVision.OperatorLibrary.ReadOnlyAudit.SemanticModel",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetRoot();
            foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!LooksLikeOperatorClass(classDeclaration, model))
                {
                    continue;
                }

                var operatorName = ResolveOperatorName(classDeclaration, model);
                if (string.IsNullOrWhiteSpace(operatorName))
                {
                    continue;
                }

                var canonicalName = Canonicalize(operatorName);
                var line = 1 + classDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line;
                var observation = new SourceOperatorObservation
                {
                    Operator = canonicalName,
                    EvidencePath = RelativePath(repoRoot, tree.FilePath),
                    ClassLine = line
                };
                AnalyzeInvocations(classDeclaration, model, observation);
                AnalyzeDictionaryKeys(classDeclaration, observation);
                AnalyzePreviewTerms(classDeclaration, observation);
                AnalyzeInheritedSource(classDeclaration, model, observation);
                AnalyzePortTypeNames(classDeclaration, model, canonicalName, observation, result);

                if (!result.Observations.TryGetValue(canonicalName, out var list))
                {
                    list = [];
                    result.Observations[canonicalName] = list;
                }

                list.Add(observation);
            }
        }

        return result;
    }

    private static void AnalyzeInheritedSource(
        ClassDeclarationSyntax declaration,
        SemanticModel model,
        SourceOperatorObservation observation)
    {
        var type = model.GetDeclaredSymbol(declaration)?.BaseType;
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        while (type is not null && visited.Add(type))
        {
            foreach (var syntaxReference in type.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not ClassDeclarationSyntax baseDeclaration)
                {
                    continue;
                }

                var baseModel = model.Compilation.GetSemanticModel(
                    syntaxReference.SyntaxTree,
                    ignoreAccessibility: true);
                AnalyzeInvocations(baseDeclaration, baseModel, observation);
                AnalyzeDictionaryKeys(baseDeclaration, observation);
                AnalyzePreviewTerms(baseDeclaration, observation);
            }

            type = type.BaseType;
        }
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }
        }

        foreach (var assembly in new[]
                 {
                     typeof(OperatorMetadataScanner).Assembly,
                     typeof(OperatorMetadata).Assembly,
                     typeof(OperatorBase).Assembly
                 })
        {
            if (!string.IsNullOrWhiteSpace(assembly.Location) && File.Exists(assembly.Location))
            {
                paths.Add(assembly.Location);
            }
        }

        return paths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static bool LooksLikeOperatorClass(
        ClassDeclarationSyntax declaration,
        SemanticModel model)
    {
        var symbol = model.GetDeclaredSymbol(declaration);
        if (symbol?.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.Name.Equals("OperatorMetaAttribute", StringComparison.Ordinal) == true) == true)
        {
            return true;
        }

        return declaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString().EndsWith("OperatorMeta", StringComparison.Ordinal));
    }

    private static string ResolveOperatorName(
        ClassDeclarationSyntax declaration,
        SemanticModel model)
    {
        var property = declaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(item => item.Identifier.Text.Equals("OperatorType", StringComparison.Ordinal));
        var enumMember = property?
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Select(item => item.Name.Identifier.Text)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(enumMember) &&
            Enum.TryParse<OperatorType>(enumMember, true, out _))
        {
            return enumMember;
        }

        var symbol = model.GetDeclaredSymbol(declaration);
        var className = symbol?.Name ?? declaration.Identifier.Text;
        return className.EndsWith("Operator", StringComparison.Ordinal)
            ? className[..^"Operator".Length]
            : className;
    }

    private static void AnalyzeInvocations(
        ClassDeclarationSyntax declaration,
        SemanticModel model,
        SourceOperatorObservation observation)
    {
        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetMethodName(invocation);
            if (string.IsNullOrWhiteSpace(methodName) ||
                !methodName.Contains("Param", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var arguments = invocation.ArgumentList.Arguments;
            var nameArgumentIndex = -1;
            var parameterName = string.Empty;
            for (var index = 0; index < arguments.Count; index++)
            {
                if (TryGetStringConstant(model, arguments[index].Expression, out var value))
                {
                    nameArgumentIndex = index;
                    parameterName = value;
                    observation.ParameterReads.Add(value);
                    break;
                }
            }

            if (nameArgumentIndex < 0)
            {
                observation.HasDynamicParameterAccess = true;
                continue;
            }

            if (nameArgumentIndex + 1 < arguments.Count &&
                TryGetConstantText(model, arguments[nameArgumentIndex + 1].Expression, out var fallback))
            {
                var line = 1 + invocation.GetLocation().GetLineSpan().StartLinePosition.Line;
                observation.ParameterDefaults.Add(new SourceParameterFallback(
                    parameterName,
                    fallback,
                    $"{observation.EvidencePath}:{line}: {methodName} fallback"));
            }
        }

        foreach (var access in declaration.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            if (!access.Expression.ToString().Contains("Parameters", StringComparison.OrdinalIgnoreCase) ||
                access.ArgumentList.Arguments.Count == 0)
            {
                continue;
            }

            if (TryGetStringConstant(model, access.ArgumentList.Arguments[0].Expression, out var name))
            {
                observation.ParameterReads.Add(name);
            }
            else
            {
                observation.HasDynamicParameterAccess = true;
            }
        }
    }

    private static void AnalyzeDictionaryKeys(
        ClassDeclarationSyntax declaration,
        SourceOperatorObservation observation)
    {
        foreach (var initializer in declaration.DescendantNodes().OfType<InitializerExpressionSyntax>())
        {
            if (!initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
            {
                continue;
            }

            var objectCreation = initializer.Ancestors().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
            if (objectCreation is null ||
                !objectCreation.Type.ToString().Contains("Dictionary", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var element in initializer.Expressions)
            {
                var literals = element.DescendantNodesAndSelf()
                    .OfType<LiteralExpressionSyntax>()
                    .Where(item => item.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(item => item.Token.ValueText)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (literals.Length == 0)
                {
                    observation.HasDynamicOutputDictionary = true;
                    continue;
                }

                observation.DictionaryKeys.Add(literals[0]);
            }
        }
    }

    private static void AnalyzePreviewTerms(
        ClassDeclarationSyntax declaration,
        SourceOperatorObservation observation)
    {
        observation.UsesPreviewTerms = declaration.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.Text.Contains("preview", StringComparison.OrdinalIgnoreCase)) ||
            declaration.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(item => item.IsKind(SyntaxKind.StringLiteralExpression))
                .Any(literal => literal.Token.ValueText.Contains("preview", StringComparison.OrdinalIgnoreCase));
    }

    private static void AnalyzePortTypeNames(
        ClassDeclarationSyntax declaration,
        SemanticModel model,
        string operatorName,
        SourceOperatorObservation observation,
        SourceAnalysisResult result)
    {
        foreach (var attribute in declaration.DescendantNodes().OfType<AttributeSyntax>()
                     .Where(item => item.Name.ToString().Contains("Port", StringComparison.OrdinalIgnoreCase)))
        {
            var expression = attribute.ArgumentList?.Arguments
                .Select(item => item.Expression)
                .FirstOrDefault(item => item.ToString().Contains("PortDataType", StringComparison.Ordinal));
            if (expression is null)
            {
                continue;
            }

            var typeName = expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                _ => expression.ToString().Split('.').Last()
            };
            if (Enum.TryParse<PortDataType>(typeName, true, out _))
            {
                continue;
            }

            var line = 1 + attribute.GetLocation().GetLineSpan().StartLinePosition.Line;
            var field = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('"') ?? "port";
            var semantic = model.GetSymbolInfo(expression).Symbol?.ToDisplayString() ?? "unresolved";
            result.UnknownPortDataTypes.Add(new UnknownPortDataTypeObservation(
                operatorName,
                field,
                [$"{observation.EvidencePath}:{line}: PortDataType.{typeName}; semantic={semantic}"]));
        }
    }

    private static string GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => string.Empty
        };
    }

    private static bool TryGetStringConstant(
        SemanticModel model,
        ExpressionSyntax expression,
        out string value)
    {
        if (model.GetConstantValue(expression).Value is string constant)
        {
            value = constant;
            return true;
        }

        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetConstantText(
        SemanticModel model,
        ExpressionSyntax expression,
        out string value)
    {
        var constant = model.GetConstantValue(expression);
        if (constant.HasValue)
        {
            value = Format(constant.Value);
            return true;
        }

        if (expression is LiteralExpressionSyntax literal)
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string Format(object? value)
    {
        return value switch
        {
            null => "null",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string Canonicalize(string name)
    {
        if (Enum.TryParse<OperatorType>(name, true, out var parsed))
        {
            return OperatorTypeAliasResolver.Resolve(parsed).ToString();
        }

        return name.Trim();
    }

    private static string RelativePath(string repoRoot, string path)
    {
        return Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
    }
}
