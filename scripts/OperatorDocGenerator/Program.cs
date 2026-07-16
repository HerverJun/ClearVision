using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;

var repoRoot = ResolveRepoRoot(args);
var overwrite = args.Any(arg => string.Equals(arg, "--overwrite", StringComparison.OrdinalIgnoreCase));
var enforceVersionBump = args.Any(arg => string.Equals(arg, "--enforce-version-bump", StringComparison.OrdinalIgnoreCase));
var onlyOperators = ParseOnlyOperators(args);
var operatorDocsRoot = Path.Combine(repoRoot, "docs", "算子资料");
var docsRoot = Path.Combine(operatorDocsRoot, "算子名片");
var legacyOperatorsRoot = Path.Combine(repoRoot, "docs", "operators");
var legacyMirrorRoot = Path.Combine(repoRoot, "算子资料");
var qualityContext = BuildQualityContext(repoRoot, docsRoot);

Directory.CreateDirectory(operatorDocsRoot);
Directory.CreateDirectory(docsRoot);

var runtimeMetadataByType = new OperatorFactory()
    .GetAllMetadata()
    .ToDictionary(metadata => metadata.Type);

var candidates = typeof(OperatorBase).Assembly
    .GetTypes()
    .Where(type => type.IsClass && !type.IsAbstract)
    .Where(type => typeof(OperatorBase).IsAssignableFrom(type))
    .Select(type => new
    {
        Type = type,
        Algo = type.GetCustomAttribute<AlgorithmInfoAttribute>(inherit: false),
        OperatorType = ResolveOperatorType(type)
    })
    .Where(x => x.OperatorType != null)
    .Select(x => new OperatorDocModel(
        x.Type,
        x.Algo,
        x.OperatorType!.Value,
        runtimeMetadataByType.TryGetValue(x.OperatorType.Value, out var metadata)
            ? metadata
            : throw new InvalidOperationException($"Runtime metadata missing for OperatorType.{x.OperatorType.Value}.")))
    .OrderBy(x => OperatorCategoryCatalog.GetOrder(x.Metadata.CategoryId))
    .ThenBy(x => x.OperatorType.ToString(), StringComparer.Ordinal)
    .ToList();

var operators = candidates
    .Select(item => ToCatalogOperator(item, qualityContext))
    .OrderBy(item => item.CategoryOrder)
    .ThenBy(item => item.Id, StringComparer.Ordinal)
    .ToList();
var catalogFingerprint = ComputeCatalogFingerprint(operators);
var generatedAt = ResolveGeneratedAt(Path.Combine(docsRoot, "catalog.json"), catalogFingerprint);
var operatorChangelogDates = ResolveOperatorChangelogDates(
    Path.Combine(docsRoot, "version-history.json"),
    operators,
    generatedAt);
var (generated, skipped) = GenerateOperatorDocuments(
    candidates,
    docsRoot,
    overwrite,
    onlyOperators,
    qualityContext,
    operatorChangelogDates);

GenerateCatalogJson(operators, docsRoot, generatedAt, catalogFingerprint);
GenerateCatalogMarkdown(operators, docsRoot, "./", generatedAt);
var versionTracking = GenerateVersionTrackingArtifacts(operators, docsRoot, generatedAt);
SyncRootCatalogArtifacts(operators, docsRoot, operatorDocsRoot, generatedAt);
SyncLegacyOperatorsArtifacts(operators, docsRoot, legacyOperatorsRoot, repoRoot, generatedAt);
SyncLegacyMirrorArtifacts(operatorDocsRoot, docsRoot, legacyMirrorRoot);
GenerateFourAxisQualityReport(operators, repoRoot, generatedAt);

Console.WriteLine($"repoRoot={repoRoot} operatorDocsRoot={operatorDocsRoot} cardsRoot={docsRoot} operators={candidates.Count} generated={generated} skipped={skipped} overwrite={overwrite}");
Console.WriteLine($"catalogJson={Path.Combine(operatorDocsRoot, "算子目录.json")} catalogMarkdown={Path.Combine(operatorDocsRoot, "算子目录.md")}");
Console.WriteLine($"changelog={Path.Combine(operatorDocsRoot, "算子变更记录.md")} versionHistory={Path.Combine(operatorDocsRoot, "算子版本记录.json")}");

if (versionTracking.Violations.Count > 0)
{
    Console.WriteLine($"[WARN] detected {versionTracking.Violations.Count} operator(s) with source changes but unchanged version:");
    foreach (var violation in versionTracking.Violations)
    {
        Console.WriteLine($"[WARN] {violation.OperatorId}: version {violation.CurrentVersion} unchanged while source hash changed.");
    }
}

if (enforceVersionBump && versionTracking.Violations.Count > 0)
{
    Console.Error.WriteLine("[ERROR] version bump enforcement failed. Re-run after updating operator versions.");
    return 2;
}

return 0;

static (int generated, int skipped) GenerateOperatorDocuments(
    IReadOnlyList<OperatorDocModel> candidates,
    string docsRoot,
    bool overwrite,
    IReadOnlySet<string> onlyOperators,
    QualityContext qualityContext,
    IReadOnlyDictionary<OperatorType, DateTimeOffset> operatorChangelogDates)
{
    var generated = 0;
    var skipped = 0;

    foreach (var item in candidates)
    {
        var fileName = $"{item.OperatorType}.md";
        var operatorId = item.OperatorType.ToString();
        if (onlyOperators.Count > 0 && !onlyOperators.Contains(operatorId))
        {
            skipped++;
            continue;
        }

        var filePath = Path.Combine(docsRoot, fileName);
        if (File.Exists(filePath) && !overwrite)
        {
            skipped++;
            continue;
        }

        var changelogDate = operatorChangelogDates[item.OperatorType];
        File.WriteAllText(filePath, BuildDocument(item, qualityContext, changelogDate), new UTF8Encoding(false));
        generated++;
    }

    return (generated, skipped);
}

static HashSet<string> ParseOnlyOperators(string[] args)
{
    var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var arg in args)
    {
        var value = arg.StartsWith("--only=", StringComparison.OrdinalIgnoreCase)
            ? arg["--only=".Length..]
            : arg.StartsWith("--operators=", StringComparison.OrdinalIgnoreCase)
                ? arg["--operators=".Length..]
                : null;

        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        foreach (var item in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            selected.Add(item.StartsWith("OperatorType.", StringComparison.Ordinal)
                ? item["OperatorType.".Length..]
                : item);
        }
    }

    return selected;
}

static void GenerateCatalogJson(
    IReadOnlyList<CatalogOperator> operators,
    string docsRoot,
    DateTimeOffset generatedAt,
    string generationFingerprint)
{
    var categories = operators
        .GroupBy(item => new { item.CategoryId, item.CategoryOrder, item.Category })
        .OrderBy(group => group.Key.CategoryOrder)
        .ToDictionary(
            group => group.Key.Category,
            group => new CatalogCategorySummary
            {
                CategoryId = group.Key.CategoryId,
                DisplayName = group.Key.Category,
                Order = group.Key.CategoryOrder,
                Count = group.Count(),
                Operators = group
                    .Select(op => op.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList()
            },
            StringComparer.Ordinal);

    var model = new CatalogDocument
    {
        GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
        GenerationFingerprint = generationFingerprint,
        TotalCount = operators.Count,
        Categories = categories,
        Operators = operators.ToList()
    };

    var options = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    var json = JsonSerializer.Serialize(model, options);
    File.WriteAllText(Path.Combine(docsRoot, "catalog.json"), json + Environment.NewLine, new UTF8Encoding(false));
}

static string ComputeCatalogFingerprint(IReadOnlyList<CatalogOperator> operators)
{
    var canonical = JsonSerializer.Serialize(
        operators
            .OrderBy(item => item.CategoryOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList(),
        new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

    return ComputeSha256(canonical);
}

static DateTimeOffset ResolveGeneratedAt(string catalogPath, string generationFingerprint)
{
    if (File.Exists(catalogPath))
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var root = document.RootElement;
            var previousFingerprint = root.TryGetProperty("generationFingerprint", out var fingerprintElement)
                ? fingerprintElement.GetString()
                : null;
            var previousGeneratedAt = root.TryGetProperty("generatedAt", out var generatedAtElement)
                ? generatedAtElement.GetString()
                : null;

            if (string.Equals(previousFingerprint, generationFingerprint, StringComparison.Ordinal) &&
                DateTimeOffset.TryParse(
                    previousGeneratedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // Regenerate invalid or legacy catalogs using a fresh timestamp.
        }
    }

    return DateTimeOffset.Now;
}

static void GenerateFourAxisQualityReport(IReadOnlyList<CatalogOperator> operators, string repoRoot, DateTimeOffset generatedAt)
{
    var reportDirectory = Path.Combine(repoRoot, "quality", "evals", "reports");
    Directory.CreateDirectory(reportDirectory);
    var rows = operators
        .OrderBy(item => item.Id, StringComparer.Ordinal)
        .Select(item => new
        {
            operatorType = item.Id,
            item.DisplayName,
            item.Lifecycle,
            item.QualityState.Execution,
            item.QualityState.AlgorithmQuality,
            item.QualityState.ProductionReadiness,
            item.QualityState.FieldValidation,
            item.QualityState.EvidenceScope,
            item.QualityState.EvidenceIdentity,
            evidenceRefs = item.QualityState.EvidenceRefs,
            modeEvidence = item.QualityState.ModeEvidence
        })
        .ToArray();
    var document = new
    {
        schemaVersion = "2026-07-15.operator-quality-four-axis.v1",
        generatedAtUtc = generatedAt.ToString("o", CultureInfo.InvariantCulture),
        claimBoundary = "Four independent evidence axes. Unknown, compatibility-only and synthetic/public-dataset evidence never imply Release Ready or Field Verified.",
        summary = new
        {
            total = rows.Length,
            execution = rows.GroupBy(row => row.Execution).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            algorithmQuality = rows.GroupBy(row => row.AlgorithmQuality).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            productionReadiness = rows.GroupBy(row => row.ProductionReadiness).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            fieldValidation = rows.GroupBy(row => row.FieldValidation).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        },
        operators = rows
    };
    var jsonOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
    File.WriteAllText(
        Path.Combine(reportDirectory, "operator_quality_four_axis.json"),
        JsonSerializer.Serialize(document, jsonOptions) + Environment.NewLine,
        new UTF8Encoding(false));

    var markdown = new StringBuilder();
    markdown.AppendLine("# Operator Quality Four-Axis State");
    markdown.AppendLine();
    markdown.AppendLine($"GeneratedAtUtc: `{generatedAt:o}`");
    markdown.AppendLine();
    markdown.AppendLine("> Execution, AlgorithmQuality, ProductionReadiness and FieldValidation are independent. Synthetic/public-dataset evidence is not Release Ready or Field Verified evidence.");
    markdown.AppendLine();
    markdown.AppendLine("| Operator | Execution | AlgorithmQuality | ProductionReadiness | FieldValidation | Scope | Mode evidence | Evidence |");
    markdown.AppendLine("|---|---|---|---|---|---|---|---|");
    foreach (var row in rows)
    {
        var modes = string.Join("<br>", row.modeEvidence.Select(mode => $"{mode.ModeId}: {mode.AlgorithmQuality}; adopted={mode.Adopted}; default={mode.IsDefault}"));
        markdown.AppendLine($"| `{row.operatorType}` | `{row.Execution}` | `{row.AlgorithmQuality}` | `{row.ProductionReadiness}` | `{row.FieldValidation}` | `{row.EvidenceScope}` | {EscapeCell(modes)} | {EscapeCell(string.Join("<br>", row.evidenceRefs))} |");
    }
    File.WriteAllText(Path.Combine(reportDirectory, "operator_quality_four_axis.md"), markdown.ToString(), new UTF8Encoding(false));
}

static IReadOnlyDictionary<OperatorType, DateTimeOffset> ResolveOperatorChangelogDates(
    string historyPath,
    IReadOnlyList<CatalogOperator> operators,
    DateTimeOffset fallback)
{
    var historyById = LoadVersionHistory(historyPath).Operators
        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
        .ToDictionary(item => item.Id, item => item, StringComparer.Ordinal);
    var dates = new Dictionary<OperatorType, DateTimeOffset>();

    foreach (var op in operators)
    {
        var recordedAt = historyById.TryGetValue(op.Id, out var entry)
            ? entry.Records.FirstOrDefault(record =>
                string.Equals(record.Version, op.Version, StringComparison.Ordinal) &&
                string.Equals(record.SourceHash, op.GenerationFingerprint, StringComparison.Ordinal) &&
                string.Equals(
                    record.FingerprintVersion,
                    OperatorGenerationFingerprintBuilder.SchemeVersion,
                    StringComparison.Ordinal))?.RecordedAt
            : null;
        dates[Enum.Parse<OperatorType>(op.Id)] = DateTimeOffset.TryParse(
            recordedAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : fallback;
    }

    return dates;
}

static void GenerateCatalogMarkdown(IReadOnlyList<CatalogOperator> operators, string docsRoot, string docLinkPrefix, DateTimeOffset generatedAt)
{
    var markdown = BuildCatalogMarkdown(operators, generatedAt, docLinkPrefix);
    File.WriteAllText(Path.Combine(docsRoot, "CATALOG.md"), markdown, new UTF8Encoding(false));
}

static string BuildCatalogMarkdown(IReadOnlyList<CatalogOperator> operators, DateTimeOffset generatedAt, string docLinkPrefix)
{
    var grouped = operators
        .GroupBy(item => new { item.CategoryId, item.CategoryOrder, item.Category })
        .OrderBy(group => group.Key.CategoryOrder)
        .ToList();

    var normalizedPrefix = string.IsNullOrWhiteSpace(docLinkPrefix) ? "./" : docLinkPrefix.Trim();
    if (!normalizedPrefix.EndsWith('/'))
    {
        normalizedPrefix += "/";
    }

    var sb = new StringBuilder();
    sb.AppendLine("# 算子目录 / Operator Catalog");
    sb.AppendLine();
    sb.AppendLine($"> 生成时间 / Generated At: `{generatedAt:yyyy-MM-dd HH:mm:ss zzz}`");
    sb.AppendLine($"> 算子总数 / Total Operators: **{operators.Count}**");
    sb.AppendLine();
    sb.AppendLine("## 分类统计 / Category Summary");
    sb.AppendLine("| 分类 ID | 分类 (Category) | 数量 (Count) | 占比 (Ratio) |");
    sb.AppendLine("|------|------|------:|------:|");
    foreach (var categoryGroup in grouped)
    {
        var ratio = operators.Count == 0 ? 0 : categoryGroup.Count() * 100.0 / operators.Count;
        sb.AppendLine($"| `{categoryGroup.Key.CategoryId}` | {EscapeCell(categoryGroup.Key.Category)} | {categoryGroup.Count()} | {ratio.ToString("0.0", CultureInfo.InvariantCulture)}% |");
    }

    sb.AppendLine();
    sb.AppendLine("## 四轴质量状态 / Four-Axis Quality State");
    sb.AppendLine("> 四个轴必须独立解释；生命周期、测试数量或历史 A/B 分数均不能代表整体成熟度。合成或公开数据证据不会自动提升 ProductionReadiness 或 FieldValidation。");
    foreach (var axis in new[]
             {
                 (Name: "Execution", Select: (Func<CatalogOperator, string>)(op => op.QualityState.Execution)),
                 (Name: "AlgorithmQuality", Select: (Func<CatalogOperator, string>)(op => op.QualityState.AlgorithmQuality)),
                 (Name: "ProductionReadiness", Select: (Func<CatalogOperator, string>)(op => op.QualityState.ProductionReadiness)),
                 (Name: "FieldValidation", Select: (Func<CatalogOperator, string>)(op => op.QualityState.FieldValidation))
             })
    {
        var summary = string.Join(", ", operators.GroupBy(axis.Select).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}={group.Count()}"));
        sb.AppendLine($"- {axis.Name}: {summary}");
    }

    sb.AppendLine();
    sb.AppendLine("## 分类索引 / Grouped Index");
    foreach (var categoryGroup in grouped)
    {
        sb.AppendLine();
        sb.AppendLine($"### {categoryGroup.Key.Category} / `{categoryGroup.Key.CategoryId}` ({categoryGroup.Count()})");
        sb.AppendLine("| 枚举 (Enum) | 显示名 (DisplayName) | Execution | AlgorithmQuality | ProductionReadiness | FieldValidation | 输入 | 输出 | 参数 | 版本 (Version) | 算法 (Algorithm) | 文档 |");
        sb.AppendLine("|------|------|------|------|------|------|------:|------:|------:|------|------|------|");

        foreach (var op in categoryGroup.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var algorithm = string.IsNullOrWhiteSpace(op.Algorithm) ? "-" : EscapeCell(op.Algorithm!);
            var linkPath = $"{normalizedPrefix}{op.Id}.md";
            sb.AppendLine(
                $"| `OperatorType.{op.Id}` | {EscapeCell(op.DisplayName)} | `{op.QualityState.Execution}` | `{op.QualityState.AlgorithmQuality}` | `{op.QualityState.ProductionReadiness}` | `{op.QualityState.FieldValidation}` | {op.InputPorts.Count} | {op.OutputPorts.Count} | {op.Parameters.Count} | `{op.Version}` | {algorithm} | [{op.Id}]({linkPath}) |");
        }
    }

    return sb.ToString();
}

static void SyncRootCatalogArtifacts(IReadOnlyList<CatalogOperator> operators, string docsRoot, string docsParentRoot, DateTimeOffset generatedAt)
{
    Directory.CreateDirectory(docsParentRoot);

    var catalogJsonPath = Path.Combine(docsRoot, "catalog.json");
    if (File.Exists(catalogJsonPath))
    {
        var json = File.ReadAllText(catalogJsonPath);
        File.WriteAllText(Path.Combine(docsParentRoot, "算子目录.json"), json, new UTF8Encoding(false));
    }

    var rootMarkdown = BuildCatalogMarkdown(operators, generatedAt, "./算子名片/");
    File.WriteAllText(Path.Combine(docsParentRoot, "算子目录.md"), rootMarkdown, new UTF8Encoding(false));

    foreach (var artifact in new[] { "CHANGELOG.md", "version-history.json" })
    {
        var sourcePath = Path.Combine(docsRoot, artifact);
        if (File.Exists(sourcePath))
        {
            var content = File.ReadAllText(sourcePath);
            var destinationFileName = artifact == "CHANGELOG.md"
                ? "算子变更记录.md"
                : "算子版本记录.json";
            File.WriteAllText(Path.Combine(docsParentRoot, destinationFileName), content, new UTF8Encoding(false));
        }
    }
}

static void SyncLegacyMirrorArtifacts(string activeDocsRoot, string activeCardsRoot, string legacyMirrorRoot)
{
    Directory.CreateDirectory(legacyMirrorRoot);

    foreach (var fileName in new[]
    {
        "README.md",
        "算子目录.json",
        "算子目录.md",
        "算子变更记录.md",
        "算子版本记录.json",
        "算子手册.md",
        "算子文档现状对齐说明-2026-04.md",
        "测量类算子精度升级说明-2026-04.md",
        "导航.md"
    })
    {
        var sourcePath = Path.Combine(activeDocsRoot, fileName);
        if (!File.Exists(sourcePath))
        {
            continue;
        }

        var destinationPath = Path.Combine(legacyMirrorRoot, fileName);
        if (string.Equals(Path.GetExtension(fileName), ".md", StringComparison.OrdinalIgnoreCase))
        {
            var content = RewriteRootMirrorLinks(File.ReadAllText(sourcePath));
            File.WriteAllText(destinationPath, content, new UTF8Encoding(false));
        }
        else
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    var legacyCardsRoot = Path.Combine(legacyMirrorRoot, "算子名片");
    Directory.CreateDirectory(legacyCardsRoot);

    foreach (var cardPath in Directory.EnumerateFiles(activeCardsRoot, "*.md", SearchOption.TopDirectoryOnly))
    {
        File.Copy(cardPath, Path.Combine(legacyCardsRoot, Path.GetFileName(cardPath)), overwrite: true);
    }

    foreach (var artifact in new[] { "catalog.json", "CATALOG.md", "CHANGELOG.md", "version-history.json" })
    {
        var sourcePath = Path.Combine(activeCardsRoot, artifact);
        if (!File.Exists(sourcePath))
        {
            continue;
        }

        File.Copy(sourcePath, Path.Combine(legacyCardsRoot, artifact), overwrite: true);
    }
}

static string RewriteRootMirrorLinks(string content)
{
    return content
        .Replace("](../参考资料/", "](../docs/参考资料/", StringComparison.Ordinal)
        .Replace("](../进行中/", "](../docs/进行中/", StringComparison.Ordinal)
        .Replace("](../../quality/", "](../quality/", StringComparison.Ordinal)
        .Replace("`../进行中/", "`../docs/进行中/", StringComparison.Ordinal)
        .Replace("`../../quality/", "`../quality/", StringComparison.Ordinal);
}

static void SyncLegacyOperatorsArtifacts(
    IReadOnlyList<CatalogOperator> operators,
    string activeCardsRoot,
    string legacyOperatorsRoot,
    string repoRoot,
    DateTimeOffset generatedAt)
{
    Directory.CreateDirectory(legacyOperatorsRoot);

    foreach (var cardPath in Directory.EnumerateFiles(activeCardsRoot, "*.md", SearchOption.TopDirectoryOnly))
    {
        File.Copy(cardPath, Path.Combine(legacyOperatorsRoot, Path.GetFileName(cardPath)), overwrite: true);
    }

    foreach (var artifact in new[] { "catalog.json", "version-history.json" })
    {
        var sourcePath = Path.Combine(activeCardsRoot, artifact);
        if (!File.Exists(sourcePath))
        {
            continue;
        }

        File.Copy(sourcePath, Path.Combine(legacyOperatorsRoot, artifact), overwrite: true);
    }

    var rootCatalog = BuildCatalogMarkdown(operators, generatedAt, "./operators/");
    File.WriteAllText(Path.Combine(repoRoot, "docs", "CATALOG.md"), rootCatalog, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(repoRoot, "docs", "OPERATOR_CATALOG.md"), rootCatalog, new UTF8Encoding(false));

    var changelogPath = Path.Combine(activeCardsRoot, "CHANGELOG.md");
    if (File.Exists(changelogPath))
    {
        File.Copy(changelogPath, Path.Combine(repoRoot, "docs", "CHANGELOG.md"), overwrite: true);
    }
}

static string ResolveRepoRoot(string[] args)
{
    if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
    {
        return Path.GetFullPath(args[0]);
    }

    return Directory.GetCurrentDirectory();
}

static OperatorType? ResolveOperatorType(Type operatorType)
{
    var property = operatorType.GetProperty(nameof(OperatorBase.OperatorType), BindingFlags.Public | BindingFlags.Instance);
    if (property?.PropertyType == typeof(OperatorType) && property.GetMethod != null)
    {
        try
        {
            var uninitialized = RuntimeHelpers.GetUninitializedObject(operatorType);
            if (property.GetValue(uninitialized) is OperatorType resolved)
            {
                return resolved;
            }
        }
        catch
        {
            // fallback to class name parsing
        }
    }

    var className = operatorType.Name;
    if (className.EndsWith("Operator", StringComparison.Ordinal))
    {
        className = className[..^"Operator".Length];
    }

    return Enum.TryParse<OperatorType>(className, out var parsed) ? parsed : null;
}

static string BuildDocument(
    OperatorDocModel item,
    QualityContext qualityContext,
    DateTimeOffset changelogDate)
{
    var sb = new StringBuilder();
    var metadata = item.Metadata;
    var className = item.ClrType.Name;
    var englishName = className.EndsWith("Operator", StringComparison.Ordinal)
        ? className[..^"Operator".Length]
        : className;
    var category = metadata.Category;
    var facts = AnalyzeOperatorSource(item, qualityContext);
    var tags = BuildOperatorTags(item);
    var generationFingerprint = ComputeOperatorGenerationFingerprint(item, qualityContext);

    sb.AppendLine($"# {metadata.DisplayName} / {englishName}");
    sb.AppendLine();
    sb.AppendLine("## 基本信息 / Basic Info");
    sb.AppendLine("| 项目 (Field) | 值 (Value) |");
    sb.AppendLine("|------|------|");
    sb.AppendLine($"| 类名 (Class) | `{className}` |");
    sb.AppendLine($"| 枚举值 (Enum) | `OperatorType.{item.OperatorType}` |");
    sb.AppendLine($"| 分类 ID (CategoryId) | `{metadata.CategoryId}` |");
    sb.AppendLine($"| 分类 (Category) | {EscapeCell(category)} |");
    sb.AppendLine($"| 分类顺序 (CategoryOrder) | {OperatorCategoryCatalog.GetOrder(metadata.CategoryId)} |");
    sb.AppendLine($"| 版本 (Version) | `{NormalizeSemVersion(metadata.Version)}` |");
    sb.AppendLine($"| 生命周期 (Lifecycle) | {GetLifecycleDisplayName(metadata.Lifecycle)} `{metadata.Lifecycle}` |");
    sb.AppendLine($"| 生命周期说明 (Lifecycle Note) | {EscapeCell(Fallback(metadata.LifecycleNote, "-"))} |");
    sb.AppendLine($"| 默认隐藏 (Default Hidden) | {BoolToMark(metadata.DefaultHidden)} |");
    sb.AppendLine($"| AI 默认推荐 (Default AI Recommendation) | {BoolToMark(ImageContractPresentationBuilder.IsDefaultAiRecommendation(metadata.Lifecycle, metadata.ImageInputContracts))} |");
    sb.AppendLine($"| AI 必须披露状态 (Requires Disclosure) | {BoolToMark(ImageContractPresentationBuilder.RequiresAiDisclosure(metadata.Lifecycle, metadata.ImageInputContracts))} |");
    sb.AppendLine($"| Execution | `{metadata.QualityState.Execution}` |");
    sb.AppendLine($"| AlgorithmQuality | `{metadata.QualityState.AlgorithmQuality}` |");
    sb.AppendLine($"| ProductionReadiness | `{metadata.QualityState.ProductionReadiness}` |");
    sb.AppendLine($"| FieldValidation | `{metadata.QualityState.FieldValidation}` |");
    sb.AppendLine($"| Quality Evidence Refs | {EscapeCell(string.Join("<br>", metadata.QualityState.EvidenceRefs))} |");
    if (metadata.QualityState.ModeEvidence.Count > 0)
    {
        sb.AppendLine($"| Evidence Scope | `{metadata.QualityState.EvidenceScope}` |");
        sb.AppendLine($"| Evidence Identity | `{metadata.QualityState.EvidenceIdentity}` |");
        sb.AppendLine($"| Mode Evidence | {EscapeCell(string.Join("<br>", metadata.QualityState.ModeEvidence.Select(mode => $"{mode.ModeId}: {mode.AlgorithmQuality}; adopted={mode.Adopted}; default={mode.IsDefault}; {mode.Verdict}")))} |");
    }
    sb.AppendLine($"| 标签 (Tags) | {EscapeCell(string.Join(", ", tags.Select(tag => $"`{tag}`")))} |");

    sb.AppendLine();
    sb.AppendLine("## 算法原理 / Algorithm Principle");
    foreach (var line in BuildAlgorithmPrinciple(item, category, facts))
    {
        sb.AppendLine(line);
    }

    sb.AppendLine();
    sb.AppendLine("## 实现策略 / Implementation Strategy");
    foreach (var line in BuildImplementationStrategy(item, facts))
    {
        sb.AppendLine(line);
    }

    sb.AppendLine();
    sb.AppendLine("## 核心 API 调用链 / Core API Call Chain");
    var apiCalls = BuildCoreApiCalls(item, facts);
    foreach (var api in apiCalls)
    {
        sb.AppendLine($"- `{api}`");
    }

    sb.AppendLine();
    sb.AppendLine("## 参数说明 / Parameters");
    sb.AppendLine("| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |");
    sb.AppendLine("|--------|------|------|--------|------|------|------|");
    if (metadata.Parameters.Count == 0)
    {
        sb.AppendLine("| - | - | - | - | - | - | - |");
    }
    else
    {
        foreach (var parameter in metadata.Parameters)
        {
            var range = BuildParameterRangeAndOptions(parameter);
            sb.AppendLine(
                $"| `{parameter.Name}` | {EscapeCell(parameter.DisplayName)} | `{parameter.DataType}` | {EscapeCell(FormatValue(parameter.DefaultValue))} | {EscapeCell(range)} | {BoolToMark(parameter.IsRequired)} | {EscapeCell(Fallback(parameter.Description, "-"))} |");
        }
    }

    sb.AppendLine();
    sb.AppendLine("## 输入/输出端口 / Input/Output Ports");
    sb.AppendLine("### 输入 / Inputs");
    sb.AppendLine("| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |");
    sb.AppendLine("|------|------|------|------|------|");
    if (metadata.InputPorts.Count == 0)
    {
        sb.AppendLine("| - | - | - | - | - |");
    }
    else
    {
        foreach (var input in metadata.InputPorts)
        {
            sb.AppendLine(
                $"| `{input.Name}` | {EscapeCell(input.DisplayName)} | `{input.DataType}` | {BoolToMark(input.IsRequired)} | {EscapeCell(BuildInputDescription(input))} |");
        }
    }

    sb.AppendLine();
    sb.AppendLine("### 输出 / Outputs");
    sb.AppendLine("| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |");
    sb.AppendLine("|------|------|------|------|");
    if (metadata.OutputPorts.Count == 0)
    {
        sb.AppendLine("| - | - | - | - |");
    }
    else
    {
        foreach (var output in metadata.OutputPorts)
        {
            sb.AppendLine($"| `{output.Name}` | {EscapeCell(output.DisplayName)} | `{output.DataType}` | {EscapeCell(BuildOutputDescription(output))} |");
        }
    }

    sb.AppendLine();
    sb.AppendLine("## 模式与资源契约 / Mode & Resource Contracts");
    sb.AppendLine("### 参数条件 / Parameter Conditions");
    sb.AppendLine("| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |");
    sb.AppendLine("|------|------|------|------|------|------|------|------|");
    if (metadata.ParameterConstraints.Count == 0)
    {
        sb.AppendLine("| - | - | - | - | - | - | - | - |");
    }
    else
    {
        foreach (var constraint in metadata.ParameterConstraints
                     .OrderBy(item => item.Parameter, StringComparer.Ordinal)
                     .ThenBy(item => item.ReasonCode, StringComparer.Ordinal))
        {
            var enabled = $"enabled: {DescribeConditionSet(constraint.EnabledWhen)}; disabled: {DescribeConditionSet(constraint.DisabledWhen)}";
            sb.AppendLine(
                $"| `{constraint.Parameter}` | {EscapeCell($"{constraint.RequiredPolicy}; {DescribeConditionSet(constraint.RequiredWhen)}")} | {EscapeCell($"visible: {DescribeConditionSet(constraint.VisibleWhen)}; hidden: {DescribeConditionSet(constraint.HiddenWhen)}")} | {EscapeCell(enabled)} | {EscapeCell(DescribeConditionSet(constraint.IgnoredWhen))} | {EscapeCell(Fallback(constraint.ResourceKind, "-"))} | {EscapeCell(constraint.SatisfiedByInputPorts is { Count: > 0 } ? string.Join(", ", constraint.SatisfiedByInputPorts) : "-")} | `{constraint.ReasonCode}` |");
        }
    }

    if (metadata.ImageInputContracts.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("## 图像输入域合同 / Image Input Domain Contracts");
        sb.AppendLine("| 输入端口 | 准入摘要 | 验证摘要 | 支持位深（摘要） | 原生位深（摘要） | 支持通道（摘要） | 输入策略 | 隐式转换 | 输出位深 | 动态范围 | 非有限值 | 默认失败码 | 版本 |");
        sb.AppendLine("|------|------|------|------|------|------|------|------|------|------|------|------|------|");
        foreach (var contract in metadata.ImageInputContracts.OrderBy(item => item.InputPort, StringComparer.Ordinal))
        {
            var presentation = contract.Presentation;
            var admissionSummary = $"Allowed:{presentation.AllowedVariantCount}, " +
                                   $"Rejected:{presentation.VerifiedRejectionVariantCount}, " +
                                   $"Unknown:{presentation.UnknownVariantCount}";
            var verificationSummary = presentation.EvidenceSummary;
            sb.AppendLine(
                $"| `{contract.InputPort}` | {EscapeCell(admissionSummary)} | {EscapeCell(verificationSummary)} | {EscapeCell(string.Join(", ", contract.SupportedDepths))} | {EscapeCell(string.Join(", ", contract.NativeDepths))} | {EscapeCell(string.Join(", ", contract.SupportedChannels))} | {EscapeCell(contract.InputDepthPolicy)} | {EscapeCell(contract.ImplicitConversionPolicy)} | {EscapeCell(contract.OutputDepthPolicy)} | {EscapeCell(contract.DynamicRangePolicy)} | {EscapeCell(contract.NonFinitePolicy)} | `{contract.FailureCode}` | `{contract.ContractVersion}` |");
        }

        var variants = metadata.ImageInputContracts
            .SelectMany(contract => contract.Presentation.ExactVariantGroups
                .Select(variant => (contract.InputPort, Variant: variant)))
            .ToList();
        sb.AppendLine();
        sb.AppendLine("### 精确运行变体 / Exact Runtime Variants");
        sb.AppendLine("| 输入端口 | 实际模式 | 精确输入类型（非笛卡尔积） | 条件 | 准入 | 验证 | 转换 | 输出 | 动态范围 | 输入值策略 | 失败码 | 证据 |");
        sb.AppendLine("|------|------|------|------|------|------|------|------|------|------|------|------|");
        if (variants.Count == 0)
        {
            sb.AppendLine("| - | - | - | - | - | - | - | - | - | - | - | - |");
        }
        else
        {
            foreach (var (inputPort, variant) in variants
                         .OrderBy(item => item.InputPort, StringComparer.Ordinal)
                         .ThenBy(item => item.Variant.Mode, StringComparer.Ordinal)
                         .ThenBy(item => item.Variant.Condition, StringComparer.Ordinal))
            {
                sb.AppendLine(
                    $"| `{inputPort}` | {EscapeCell(variant.Mode)} | {EscapeCell(string.Join(", ", variant.ExactInputTypes))} | {EscapeCell(variant.Condition)} | `{variant.Admission}` | `{variant.Verification}` | {EscapeCell(variant.ConversionPolicy)} | {EscapeCell(variant.OutputDepthPolicy)} | {EscapeCell(variant.DynamicRangePolicy)} | `{variant.InputValuePolicy}` | `{variant.FailureCode}` | `{variant.EvidenceLevel}` |");
            }
        }
    }

    sb.AppendLine();
    sb.AppendLine("### 输出条件 / Output Conditions");
    sb.AppendLine("| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |");
    sb.AppendLine("|------|------|------|");
    if (metadata.OutputAvailabilityRules.Count == 0)
    {
        sb.AppendLine("| - | - | - |");
    }
    else
    {
        foreach (var rule in metadata.OutputAvailabilityRules
                     .OrderBy(item => item.Output, StringComparer.Ordinal)
                     .ThenBy(item => item.ReasonCode, StringComparer.Ordinal))
        {
            sb.AppendLine($"| `{rule.Output}` | {EscapeCell(DescribeConditionSet(rule.AvailableWhen))} | `{rule.ReasonCode}` |");
        }
    }

    sb.AppendLine();
    sb.AppendLine("## 生成依赖 / Generation Dependencies");
    sb.AppendLine($"- 组合指纹 (Generation Fingerprint)：`{generationFingerprint}`");
    if (metadata.GenerationDependencies.Count == 0)
    {
        sb.AppendLine("- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。");
    }
    else
    {
        foreach (var dependency in metadata.GenerationDependencies.OrderBy(item => item, StringComparer.Ordinal))
        {
            sb.AppendLine($"- `{dependency}`");
        }
    }

    sb.AppendLine();
    sb.AppendLine("### 运行时附加输出 / Runtime Additional Outputs");
    if (facts.RuntimeAdditionalOutputs.Count == 0)
    {
        sb.AppendLine("- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。");
    }
    else
    {
        sb.AppendLine("| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |");
        sb.AppendLine("|------|------|------|");
        foreach (var field in facts.RuntimeAdditionalOutputs)
        {
            sb.AppendLine($"| `{field.Name}` | `{field.DataType}` | {EscapeCell(field.Description)} |");
        }
    }

    sb.AppendLine();
    sb.AppendLine("## 性能特征 / Performance");
    sb.AppendLine("| 指标 (Metric) | 值 (Value) |");
    sb.AppendLine("|------|------|");
    sb.AppendLine($"| 时间复杂度 (Time Complexity) | {EscapeCell(Fallback(item.Algo?.TimeComplexity, InferTimeComplexity(item, facts)))} |");
    sb.AppendLine($"| 典型耗时 (Typical Latency) | {EscapeCell(Fallback(item.Algo?.TypicalLatency, InferTypicalLatency(item, facts)))} |");
    sb.AppendLine($"| 内存特征 (Memory Profile) | {EscapeCell(Fallback(item.Algo?.SpaceComplexity, InferMemoryProfile(item, facts)))} |");

    sb.AppendLine();
    sb.AppendLine("## 证据与失败契约 / Evidence & Failure Contracts");
    foreach (var line in BuildEvidenceAndFailureContracts(item, facts, qualityContext))
    {
        sb.AppendLine(line);
    }

    sb.AppendLine();
    sb.AppendLine("## 适用场景 / Use Cases");
    foreach (var useCase in BuildSuitableUseCases(item, category, facts))
    {
        sb.AppendLine($"- 适合 (Suitable)：{useCase}");
    }

    foreach (var useCase in BuildUnsuitableUseCases(item, category, facts))
    {
        sb.AppendLine($"- 不适合 (Not Suitable)：{useCase}");
    }

    sb.AppendLine();
    sb.AppendLine("## 已知限制 / Known Limitations");
    var limitations = BuildKnownLimitations(item, facts).ToList();
    for (var index = 0; index < limitations.Count; index++)
    {
        sb.AppendLine($"{index + 1}. {limitations[index]}");
    }

    sb.AppendLine();
    sb.AppendLine("## 变更记录 / Changelog");
    sb.AppendLine("| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |");
    sb.AppendLine("|------|------|----------|");
    sb.AppendLine($"| {NormalizeSemVersion(metadata.Version)} | {changelogDate:yyyy-MM-dd} | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |");

    return sb.ToString();
}

static OperatorSourceFacts AnalyzeOperatorSource(OperatorDocModel item, QualityContext qualityContext)
{
    qualityContext.SourceTextByTypeName.TryGetValue(item.ClrType.Name, out var sourceText);
    sourceText ??= string.Empty;

    return new OperatorSourceFacts(
        SourceText: sourceText,
        CoreApiCalls: ExtractCoreApiCalls(sourceText),
        RuntimeAdditionalOutputs: ExtractRuntimeAdditionalOutputs(item, sourceText),
        FailureCount: Regex.Matches(sourceText, @"OperatorExecutionOutput\.Failure\s*\(", RegexOptions.CultureInvariant).Count,
        HasValidation: sourceText.Contains("ValidateParameters(", StringComparison.Ordinal),
        HasTryCatch: sourceText.Contains("try", StringComparison.Ordinal) && sourceText.Contains("catch", StringComparison.Ordinal),
        HasShortCircuit: sourceText.Contains("OperatorExecutionOutput.ShortCircuit", StringComparison.Ordinal),
        UsesImageOutput: sourceText.Contains("CreateImageOutput", StringComparison.Ordinal) ||
            sourceText.Contains("CreateSharedImageOutput", StringComparison.Ordinal),
        UsesOpenCv: sourceText.Contains("Cv2.", StringComparison.Ordinal) ||
            sourceText.Contains("OpenCvSharp", StringComparison.Ordinal),
        UsesExternalIo: Regex.IsMatch(sourceText, @"\b(File|Directory|HttpClient|PlcClientFactory|Mqtt|SerialPort|TcpClient)\b", RegexOptions.CultureInvariant),
        UsesModel: sourceText.Contains("InferenceSession", StringComparison.Ordinal) ||
            sourceText.Contains("ModelPath", StringComparison.Ordinal) ||
            sourceText.Contains("ONNX", StringComparison.OrdinalIgnoreCase),
        UsesState: sourceText.Contains("ConcurrentDictionary", StringComparison.Ordinal) ||
            sourceText.Contains("GetOrAdd", StringComparison.Ordinal) ||
            sourceText.Contains("State", StringComparison.Ordinal));
}

static List<string> BuildAlgorithmPrinciple(OperatorDocModel item, string category, OperatorSourceFacts facts)
{
    var lines = new List<string>();
    var description = Fallback(item.Metadata.Description, $"{item.Metadata.DisplayName} 算子执行当前声明的视觉/流程处理逻辑");

    var descriptionSentence = ContainsCjk(description)
        ? $"该算子用于{TrimSentence(description)}。"
        : $"当前元数据描述为：{TrimSentence(description)}。";
    lines.Add($"{descriptionSentence}运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。");

    if (!string.IsNullOrWhiteSpace(item.Algo?.Name))
    {
        lines.Add($"算法类型以 `{item.Algo.Name}` 为主；元数据未声明更多细分时，以当前源码实现为准。");
    }

    if (facts.UsesModel)
    {
        lines.Add("源码中包含模型或推理资源解析逻辑，核心结果取决于模型文件、标签配置、阈值和运行时推理环境。");
    }
    else if (facts.UsesOpenCv)
    {
        lines.Add("源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。");
    }
    else if (facts.UsesExternalIo)
    {
        lines.Add("源码中包含外部资源访问逻辑，执行结果会受文件系统、网络、PLC、串口或外部服务状态影响。");
    }
    else if (category.Contains("流程", StringComparison.Ordinal) || category.Contains("逻辑", StringComparison.Ordinal))
    {
        lines.Add("该类算子主要对上游值、集合或流程状态做判断、转换、聚合或路由，不直接改写图像像素。");
    }
    else
    {
        lines.Add("处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。");
    }

    return lines;
}

static List<string> BuildImplementationStrategy(OperatorDocModel item, OperatorSourceFacts facts)
{
    var lines = new List<string>();
    var requiredInputs = item.Metadata.InputPorts.Where(port => port.IsRequired).Select(port => $"`{port.Name}`").ToList();
    var optionalInputs = item.Metadata.InputPorts.Where(port => !port.IsRequired).Select(port => $"`{port.Name}`").ToList();

    if (requiredInputs.Count > 0)
    {
        lines.Add($"- 先校验必填输入：{string.Join("、", requiredInputs)}；缺失时通常返回失败结果。");
    }
    else
    {
        lines.Add("- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。");
    }

    if (optionalInputs.Count > 0)
    {
        lines.Add($"- 可选输入用于覆盖或补充参数配置：{string.Join("、", optionalInputs)}。");
    }

    lines.Add(item.Metadata.Parameters.Count > 0
        ? $"- 参数解析覆盖 {item.Metadata.Parameters.Count} 个当前元数据字段，默认值、范围和枚举项以参数表为准。"
        : "- 当前元数据未声明参数，执行逻辑主要由输入数据和源码默认策略决定。");

    if (facts.HasValidation)
    {
        lines.Add("- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。");
    }

    if (facts.HasTryCatch)
    {
        lines.Add("- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。");
    }

    if (facts.HasShortCircuit)
    {
        lines.Add("- 源码包含短路输出路径，可在条件不满足时阻止后续节点继续执行。");
    }

    lines.Add(facts.UsesImageOutput
        ? "- 图像类输出通过 `ImageWrapper`/`CreateImageOutput` 封装，通常会合并图像尺寸和业务附加字段。"
        : "- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。");

    return lines;
}

static List<string> BuildCoreApiCalls(OperatorDocModel item, OperatorSourceFacts facts)
{
    var calls = new List<string>();
    if (!string.IsNullOrWhiteSpace(item.Algo?.CoreApi))
    {
        calls.Add(item.Algo.CoreApi.Trim());
    }

    calls.AddRange(facts.CoreApiCalls);

    if (calls.Count == 0)
    {
        calls.Add("OperatorBase.Get*Param(...)");
        calls.Add("OperatorExecutionOutput.Success/Failure(...)");
    }

    return calls
        .Where(call => !string.IsNullOrWhiteSpace(call))
        .Distinct(StringComparer.Ordinal)
        .Take(14)
        .ToList();
}

static List<string> ExtractCoreApiCalls(string sourceText)
{
    if (string.IsNullOrWhiteSpace(sourceText))
    {
        return new List<string>();
    }

    var calls = new List<string>();
    var patterns = new[]
    {
        @"\b(Cv2)\.([A-Za-z_][A-Za-z0-9_]*)",
        @"\b(File|Directory|Path)\.([A-Za-z_][A-Za-z0-9_]*)",
        @"\b(JsonSerializer|JsonDocument|JsonElement)\.([A-Za-z_][A-Za-z0-9_]*)",
        @"\b(PlcClientFactory|Math|Convert|Enumerable)\.([A-Za-z_][A-Za-z0-9_]*)",
        @"\b(HttpClient|HttpRequestMessage|InferenceSession|ImageWrapper)\b"
    };

    foreach (var pattern in patterns)
    {
        foreach (Match match in Regex.Matches(sourceText, pattern, RegexOptions.CultureInvariant))
        {
            var call = match.Groups.Count > 2 && !string.IsNullOrWhiteSpace(match.Groups[2].Value)
                ? $"{match.Groups[1].Value}.{match.Groups[2].Value}"
                : match.Groups[1].Value;

            if (!calls.Contains(call, StringComparer.Ordinal))
            {
                calls.Add(call);
            }
        }
    }

    if (sourceText.Contains("GetStringParam", StringComparison.Ordinal) ||
        sourceText.Contains("GetIntParam", StringComparison.Ordinal) ||
        sourceText.Contains("GetDoubleParam", StringComparison.Ordinal) ||
        sourceText.Contains("GetBoolParam", StringComparison.Ordinal))
    {
        calls.Insert(0, "OperatorBase.Get*Param(...)");
    }

    if (sourceText.Contains("OperatorExecutionOutput.Success", StringComparison.Ordinal))
    {
        calls.Add("OperatorExecutionOutput.Success(...)");
    }

    if (sourceText.Contains("OperatorExecutionOutput.Failure", StringComparison.Ordinal))
    {
        calls.Add("OperatorExecutionOutput.Failure(...)");
    }

    return calls
        .Distinct(StringComparer.Ordinal)
        .Take(18)
        .ToList();
}

static List<RuntimeOutputField> ExtractRuntimeAdditionalOutputs(OperatorDocModel item, string sourceText)
{
    var fields = new Dictionary<string, RuntimeOutputField>(StringComparer.Ordinal);
    var declared = item.Metadata.OutputPorts.Select(port => port.Name).ToHashSet(StringComparer.Ordinal);
    var inputs = item.Metadata.InputPorts.Select(port => port.Name).ToHashSet(StringComparer.Ordinal);
    var parameters = item.Metadata.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);

    void Add(string? key, string reason)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        key = key.Trim();
        if (!IsLikelyOutputKey(key) ||
            declared.Contains(key) ||
            inputs.Contains(key) ||
            parameters.Contains(key) ||
            fields.ContainsKey(key))
        {
            return;
        }

        fields[key] = new RuntimeOutputField(
            key,
            InferRuntimeOutputType(key),
            reason);
    }

    if (sourceText.Contains("CreateImageOutput", StringComparison.Ordinal) ||
        sourceText.Contains("CreateSharedImageOutput", StringComparison.Ordinal))
    {
        Add("Width", "由图像输出封装自动附加，表示输出图像宽度。");
        Add("Height", "由图像输出封装自动附加，表示输出图像高度。");
    }

    foreach (Match match in Regex.Matches(
        sourceText,
        @"CreateImageOutput\s*\([^;]*?,\s*""([^""]+)""\s*,\s*""([^""]+)""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant))
    {
        Add(match.Groups[1].Value, "由带自定义尺寸字段名的图像输出封装附加。");
        Add(match.Groups[2].Value, "由带自定义尺寸字段名的图像输出封装附加。");
    }

    foreach (Match match in Regex.Matches(
        sourceText,
        @"\[\s*""([A-Za-z_][A-Za-z0-9_]{1,63})""\s*\]\s*=",
        RegexOptions.CultureInvariant))
    {
        Add(match.Groups[1].Value, "源码通过输出字典索引赋值写入。");
    }

    foreach (var dictionaryBody in ExtractObjectDictionaryInitializerBodies(sourceText))
    {
        foreach (Match match in Regex.Matches(
            dictionaryBody,
            @"\{\s*""([A-Za-z_][A-Za-z0-9_]{1,63})""\s*,",
            RegexOptions.CultureInvariant))
        {
            Add(match.Groups[1].Value, "源码输出字典初始化中可见字段。");
        }
    }

    return fields.Values
        .OrderBy(field => field.Name, StringComparer.Ordinal)
        .Take(40)
        .ToList();
}

static IEnumerable<string> ExtractObjectDictionaryInitializerBodies(string sourceText)
{
    const string marker = "new Dictionary<string, object";
    var searchIndex = 0;

    while (searchIndex < sourceText.Length)
    {
        var markerIndex = sourceText.IndexOf(marker, searchIndex, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            yield break;
        }

        var braceIndex = sourceText.IndexOf('{', markerIndex);
        if (braceIndex < 0)
        {
            yield break;
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var index = braceIndex; index < sourceText.Length; index++)
        {
            var ch = sourceText[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    yield return sourceText[(braceIndex + 1)..index];
                    searchIndex = index + 1;
                    break;
                }
            }

            if (index == sourceText.Length - 1)
            {
                searchIndex = sourceText.Length;
            }
        }
    }
}

static bool IsLikelyOutputKey(string key)
{
    if (key.Length is < 2 or > 64)
    {
        return false;
    }

    if (!Regex.IsMatch(key, @"^[A-Z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
    {
        return false;
    }

    var excluded = new HashSet<string>(StringComparer.Ordinal)
    {
        "Name",
        "Description",
        "Default",
        "DefaultValue",
        "Min",
        "Max",
        "Options",
        "Value",
        "Key"
    };

    return !excluded.Contains(key);
}

static string InferRuntimeOutputType(string key)
{
    if (key.EndsWith("Width", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("Height", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("Count", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("Limit", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Pixels", StringComparison.OrdinalIgnoreCase) ||
        key is "Width" or "Height" or "Index")
    {
        return "Integer";
    }

    if (key.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "Mask", StringComparison.OrdinalIgnoreCase))
    {
        return "Image";
    }

    if (key.Contains("Detections", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Objects", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Defects", StringComparison.OrdinalIgnoreCase))
    {
        return "DetectionList";
    }

    if (key.StartsWith("Is", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("Has", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Enabled", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Passed", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Accepted", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Valid", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Triggered", StringComparison.OrdinalIgnoreCase))
    {
        return "Boolean";
    }

    if (key.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Ratio", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Angle", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Mean", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Min", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Max", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Std", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Delta", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Confidence", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Tolerance", StringComparison.OrdinalIgnoreCase))
    {
        return "Float";
    }

    if (key.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Reason", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Source", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Mode", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Type", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("Unit", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
        return "String";
    }

    return "Any";
}

static string BuildParameterRangeAndOptions(ParameterDefinition parameter)
{
    var parts = new List<string>();
    var range = BuildRange(parameter.MinValue, parameter.MaxValue);
    if (range != "-")
    {
        parts.Add(range);
    }

    if (parameter.Options is { Count: > 0 })
    {
        parts.Add(string.Join("；", parameter.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .Select(option => !string.IsNullOrWhiteSpace(option.Label) &&
                              !string.Equals(option.Value, option.Label, StringComparison.Ordinal)
                ? $"{option.Value}/{option.Label}"
                : option.Value)));
    }

    return parts.Count == 0 ? "-" : string.Join("；", parts);
}

static string BuildInputDescription(PortDefinition input)
{
    if (!string.IsNullOrWhiteSpace(input.Description))
    {
        return input.Description;
    }

    return input.IsRequired
        ? "必填输入，缺失时算子通常返回失败或无法产生有效结果。"
        : "可选输入；提供时会参与当前算子处理或覆盖部分参数配置。";
}

static string BuildOutputDescription(PortDefinition output)
{
    if (!string.IsNullOrWhiteSpace(output.Description))
    {
        return output.Description;
    }

    return output.DataType switch
    {
        PortDataType.Image => "图像输出，可供后续图像处理、显示或保存节点使用。",
        PortDataType.Boolean => "布尔判定结果，适合连接条件分支、结果判定或通信写入。",
        PortDataType.Integer or PortDataType.Float => "数值结果，可用于测量、阈值判定、统计或报表输出。",
        PortDataType.String => "文本结果，可用于显示、日志、保存或外部接口传输。",
        PortDataType.DetectionList => "检测列表结果，可连接筛选、NMS、顺序判定或结果输出节点。",
        PortDataType.PointList => "点集结果，可连接几何测量、定位或标定相关节点。",
        _ => "业务输出字段，具体结构以源码输出和运行时结果为准。"
    };
}

static string InferTimeComplexity(OperatorDocModel item, OperatorSourceFacts facts)
{
    if (facts.UsesModel)
    {
        return "推理路径主要受模型规模、输入尺寸和硬件后端影响；后处理通常随候选数量线性或近似线性增长。";
    }

    if (facts.UsesExternalIo)
    {
        return "主要受外部 I/O、网络或设备响应时间影响；本地处理通常随输入规模线性增长。";
    }

    if (facts.UsesOpenCv || facts.UsesImageOutput || item.Metadata.InputPorts.Any(input => input.DataType == PortDataType.Image))
    {
        return "多数图像路径近似 `O(W*H)`；涉及轮廓、匹配或排序时会叠加候选数量相关开销。";
    }

    return "通常随输入集合、字符串长度或字段数量线性增长。";
}

static string InferTypicalLatency(OperatorDocModel item, OperatorSourceFacts facts)
{
    if (facts.UsesModel)
    {
        return "未固定；取决于模型大小、输入尺寸、CPU/GPU/ONNX Runtime 后端和候选数量。";
    }

    if (facts.UsesExternalIo)
    {
        return "未固定；取决于文件系统、网络、PLC/串口设备或外部服务响应。";
    }

    if (facts.UsesOpenCv)
    {
        return "未固定；取决于图像分辨率、ROI 范围、OpenCV 算法分支和输出可视化成本。";
    }

    return "未固定；一般由输入数据规模和运行时调度开销决定。";
}

static string InferMemoryProfile(OperatorDocModel item, OperatorSourceFacts facts)
{
    if (facts.UsesModel)
    {
        return "需要模型会话、输入张量、输出张量和后处理集合内存；峰值随模型与输入尺寸增长。";
    }

    if (facts.UsesImageOutput || facts.UsesOpenCv)
    {
        return "通常需要输入图像、临时 Mat、结果图和输出封装内存；峰值随图像尺寸和中间副本数量增长。";
    }

    if (facts.UsesExternalIo)
    {
        return "主要由请求/响应缓冲、序列化数据和外部资源句柄占用决定。";
    }

    return "主要由输出字典、集合和少量中间对象决定。";
}

static IEnumerable<string> BuildEvidenceAndFailureContracts(
    OperatorDocModel item,
    OperatorSourceFacts facts,
    QualityContext qualityContext)
{
    var operatorId = item.OperatorType.ToString();
    var hasTests =
        qualityContext.TestIndex.Contains(item.ClrType.Name) ||
        qualityContext.TestIndex.Contains(operatorId);
    var hasGolden = qualityContext.GoldenEvidenceIndex.Contains(operatorId);

    yield return hasTests
        ? "- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。"
        : "- 单元/契约测试：未发现同名算子测试入口，建议补充关键路径和边界输入验证。";

    if (hasGolden)
    {
        yield return "- Golden/回放证据：质量报告中存在通过的 baseline 证据。";
    }

    yield return facts.HasValidation
        ? "- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。"
        : "- 参数失败契约：当前未发现独立 `ValidateParameters`，主要依赖执行期输入检查和参数读取默认值。";

    if (facts.FailureCount > 0)
    {
        yield return $"- 执行失败契约：源码中发现 {facts.FailureCount} 条 `OperatorExecutionOutput.Failure(...)` 路径。";
    }

    if (facts.HasShortCircuit)
    {
        yield return "- 短路契约：算子可返回 `ShortCircuit`，用于阻止后续节点在当前周期继续执行。";
    }
}

static IEnumerable<string> BuildSuitableUseCases(OperatorDocModel item, string category, OperatorSourceFacts facts)
{
    var declared = NonEmptyItems(item.Algo?.SuitableUseCases).ToList();
    if (declared.Count > 0)
    {
        return declared;
    }

    if (facts.UsesModel)
    {
        return new[] { "模型、标签和阈值已完成现场校准，需要把推理结果接入视觉流程的场景。" };
    }

    if (facts.UsesExternalIo)
    {
        return new[] { "需要把视觉流程与文件、HTTP、数据库、PLC、MQTT 或串口等外部系统连接的场景。" };
    }

    if (category.Contains("标定", StringComparison.Ordinal))
    {
        return new[] { "相机、坐标、像素到世界坐标或工装几何关系需要被显式建模和复用的场景。" };
    }

    if (category.Contains("通信", StringComparison.Ordinal))
    {
        return new[] { "检测结果需要与现场设备、上位系统或网络服务进行读写交互的场景。" };
    }

    if (category.Contains("流程", StringComparison.Ordinal) || category.Contains("逻辑", StringComparison.Ordinal))
    {
        return new[] { "需要对上游结果做判断、转换、聚合、计数、延时或流程路由的场景。" };
    }

    if (facts.UsesOpenCv || item.Metadata.InputPorts.Any(input => input.DataType == PortDataType.Image))
    {
        return new[] { "输入图像质量稳定、参数范围明确，需要在流程中完成图像处理、定位、测量或可视化输出的场景。" };
    }

    return new[] { "输入数据结构稳定、下游明确消费当前输出字段的常规流程节点。" };
}

static IEnumerable<string> BuildUnsuitableUseCases(OperatorDocModel item, string category, OperatorSourceFacts facts)
{
    var declared = NonEmptyItems(item.Algo?.UnsuitableUseCases).ToList();
    if (declared.Count > 0)
    {
        return declared;
    }

    var items = new List<string>();
    if (facts.UsesExternalIo)
    {
        items.Add("外部设备、路径、网络或权限不可控，且流程不能容忍 I/O 超时或失败的场景。");
    }

    if (facts.UsesModel)
    {
        items.Add("模型未完成验证、标签映射不稳定或现场数据分布明显偏离训练数据的场景。");
    }

    if (facts.UsesOpenCv)
    {
        items.Add("图像严重失焦、遮挡、反光、尺度变化过大，且没有前置校正或质量 gate 的场景。");
    }

    if (items.Count == 0)
    {
        items.Add("上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。");
    }

    return items;
}

static IEnumerable<string> BuildKnownLimitations(OperatorDocModel item, OperatorSourceFacts facts)
{
    var limitations = NonEmptyItems(item.Algo?.KnownLimitations).ToList();

    if (item.Metadata.InputPorts.Any(input => input.IsRequired))
    {
        limitations.Add("必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。");
    }

    if (item.Metadata.Parameters.Any(parameter => parameter.MinValue != null || parameter.MaxValue != null || parameter.Options is { Count: > 0 }))
    {
        limitations.Add("参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。");
    }

    if (facts.RuntimeAdditionalOutputs.Count > 0)
    {
        limitations.Add("运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。");
    }

    if (facts.UsesExternalIo)
    {
        limitations.Add("外部文件、网络、PLC、数据库或消息系统不可用时，算子结果会受环境状态影响。");
    }

    if (facts.UsesModel)
    {
        limitations.Add("模型推理类路径依赖模型文件、标签、运行时库和硬件后端，算法准确率不由算子元数据单独保证。");
    }

    if (facts.UsesState)
    {
        limitations.Add("源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。");
    }

    if (limitations.Count == 0)
    {
        limitations.Add("当前名片仅根据源码和元数据描述算子契约，不替代现场样本验收或专项性能基准。");
    }

    return limitations
        .Distinct(StringComparer.Ordinal)
        .Take(8);
}

static string TrimSentence(string text)
{
    var trimmed = text.Trim();
    return trimmed.TrimEnd('。', '.', ';', '；');
}

static bool ContainsCjk(string text) =>
    text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff');

static CatalogOperator ToCatalogOperator(OperatorDocModel item, QualityContext qualityContext)
{
    var metadata = item.Metadata;
    var id = item.OperatorType.ToString();

    var inputPorts = metadata.InputPorts
        .Select(port => new CatalogPort
        {
            Name = port.Name,
            DisplayName = port.DisplayName,
            DataType = port.DataType.ToString(),
            IsRequired = port.IsRequired
        })
        .ToList();

    var outputPorts = metadata.OutputPorts
        .Select(port => new CatalogPort
        {
            Name = port.Name,
            DisplayName = port.DisplayName,
            DataType = port.DataType.ToString(),
            IsRequired = port.IsRequired
        })
        .ToList();

    var parameters = metadata.Parameters
        .Select(parameter => new CatalogParameter
        {
            Name = parameter.Name,
            DisplayName = parameter.DisplayName,
            DataType = parameter.DataType,
            Description = parameter.Description,
            DefaultValue = NormalizeParameterValue(parameter.DefaultValue),
            Min = NormalizeParameterValue(parameter.MinValue),
            Max = NormalizeParameterValue(parameter.MaxValue),
            IsRequired = parameter.IsRequired,
            Options = parameter.Options?
                .Select(option => new CatalogParameterOption
                {
                    Value = option.Value,
                    Label = option.Label
                })
                .ToList()
        })
        .ToList();

    return new CatalogOperator
    {
        Id = id,
        Type = (int)item.OperatorType,
        DisplayName = metadata.DisplayName,
        Description = metadata.Description,
        CategoryId = metadata.CategoryId.ToString(),
        CategoryOrder = OperatorCategoryCatalog.GetOrder(metadata.CategoryId),
        Category = metadata.Category,
        Lifecycle = metadata.Lifecycle.ToString(),
        LifecycleNote = metadata.LifecycleNote,
        DefaultHidden = metadata.DefaultHidden,
        DefaultAiRecommendation = ImageContractPresentationBuilder.IsDefaultAiRecommendation(
            metadata.Lifecycle,
            metadata.ImageInputContracts),
        RequiresLifecycleDisclosure = ImageContractPresentationBuilder.RequiresAiDisclosure(
            metadata.Lifecycle,
            metadata.ImageInputContracts),
        QualityState = metadata.QualityState,
        Version = NormalizeSemVersion(metadata.Version),
        Keywords = (metadata.Keywords ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList(),
        Tags = BuildOperatorTags(item),
        Algorithm = ResolveCatalogAlgorithm(item, qualityContext),
        InputPorts = inputPorts,
        OutputPorts = outputPorts,
        Parameters = parameters,
        ParameterConditions = metadata.ParameterConstraints
            .OrderBy(constraint => constraint.Parameter, StringComparer.Ordinal)
            .ThenBy(constraint => constraint.ReasonCode, StringComparer.Ordinal)
            .ToList(),
        OutputConditions = metadata.OutputAvailabilityRules
            .OrderBy(rule => rule.Output, StringComparer.Ordinal)
            .ThenBy(rule => rule.ReasonCode, StringComparer.Ordinal)
            .ToList(),
        ImageInputContracts = metadata.ImageInputContracts
            .OrderBy(contract => contract.InputPort, StringComparer.Ordinal)
            .Select(ImageContractPresentationBuilder.Build)
            .ToList(),
        ResourceRequirements = metadata.ParameterConstraints
            .Where(constraint => !string.IsNullOrWhiteSpace(constraint.ResourceKind))
            .OrderBy(constraint => constraint.Parameter, StringComparer.Ordinal)
            .Select(constraint => new CatalogResourceRequirement
            {
                Parameter = constraint.Parameter,
                ResourceKind = constraint.ResourceKind!,
                ReasonCode = constraint.ReasonCode,
                AtLeastOneGroup = constraint.AtLeastOneGroup,
                RequiredWhen = constraint.RequiredWhen,
                SatisfiedByInputPorts = constraint.SatisfiedByInputPorts?.ToList() ?? []
            })
            .ToList(),
        GenerationDependencies = metadata.GenerationDependencies
            .OrderBy(dependency => dependency, StringComparer.Ordinal)
            .ToList(),
        GenerationFingerprint = ComputeOperatorGenerationFingerprint(item, qualityContext),
        DocPath = $"算子资料/算子名片/{id}.md"
    };
}

static string? ResolveCatalogAlgorithm(OperatorDocModel item, QualityContext qualityContext)
{
    if (!string.IsNullOrWhiteSpace(item.Algo?.Name))
    {
        return item.Algo!.Name.Trim();
    }

    var facts = AnalyzeOperatorSource(item, qualityContext);
    var summary = BuildAlgorithmPrinciple(item, item.Metadata.Category, facts)
        .Select(line => line.Trim())
        .FirstOrDefault(line =>
            !string.IsNullOrWhiteSpace(line) &&
            !line.StartsWith(">", StringComparison.Ordinal) &&
            !line.StartsWith("|", StringComparison.Ordinal));

    if (string.IsNullOrWhiteSpace(summary))
    {
        return null;
    }

    summary = summary!
        .Replace("`", string.Empty, StringComparison.Ordinal)
        .Replace("**", string.Empty, StringComparison.Ordinal)
        .Replace("__", string.Empty, StringComparison.Ordinal);

    return summary.Length <= 42
        ? summary
        : summary[..42] + "…";
}

static List<string> BuildOperatorTags(OperatorDocModel item)
{
    var tags = new HashSet<string>(StringComparer.Ordinal);
    var metadata = item.Metadata;

    if (metadata.Tags != null)
    {
        foreach (var rawTag in metadata.Tags)
        {
            if (!string.IsNullOrWhiteSpace(rawTag))
            {
                tags.Add(rawTag.Trim());
            }
        }
    }

    tags.Add($"分类:{metadata.CategoryId}");
    tags.Add($"分类显示:{metadata.Category}");
    tags.Add($"生命周期:{metadata.Lifecycle}");
    tags.Add($"Execution:{metadata.QualityState.Execution}");
    tags.Add($"AlgorithmQuality:{metadata.QualityState.AlgorithmQuality}");
    tags.Add($"ProductionReadiness:{metadata.QualityState.ProductionReadiness}");
    tags.Add($"FieldValidation:{metadata.QualityState.FieldValidation}");
    tags.Add($"算法类型:{ResolveAlgorithmTag(item)}");

    return tags
        .OrderBy(tag => tag, StringComparer.Ordinal)
        .ToList();
}

static string ResolveAlgorithmTag(OperatorDocModel item)
{
    if (item.Algo?.Dependencies != null && item.Algo.Dependencies.Length > 0)
    {
        if (item.Algo.Dependencies.Any(dep =>
                dep.Contains("OpenCv", StringComparison.OrdinalIgnoreCase)))
        {
            return "基于OpenCV";
        }

        return "第三方SDK";
    }

    if (item.Metadata.Keywords != null && item.Metadata.Keywords.Any(keyword =>
            keyword.Contains("OpenCV", StringComparison.OrdinalIgnoreCase)))
    {
        return "基于OpenCV";
    }

    return "自研";
}

static string NormalizeSemVersion(string? version)
{
    if (string.IsNullOrWhiteSpace(version))
    {
        return "1.0.0";
    }

    var normalized = version.Trim();
    var isValid = Regex.IsMatch(
        normalized,
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    return isValid ? normalized : "1.0.0";
}

static VersionTrackingResult GenerateVersionTrackingArtifacts(
    IReadOnlyList<CatalogOperator> operators,
    string docsRoot,
    DateTimeOffset generatedAt)
{
    var historyPath = Path.Combine(docsRoot, "version-history.json");
    var changelogPath = Path.Combine(docsRoot, "CHANGELOG.md");

    var historyDocument = LoadVersionHistory(historyPath);
    var historyById = historyDocument.Operators
        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
        .ToDictionary(item => item.Id, item => item, StringComparer.Ordinal);

    var violations = new List<VersionBumpViolation>();
    var recordedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture);

    foreach (var op in operators)
    {
        var sourceHash = op.GenerationFingerprint;
        var fingerprintVersion = OperatorGenerationFingerprintBuilder.GetHistorySchemeVersion(
            op.ImageInputContracts.Count > 0);
        if (!historyById.TryGetValue(op.Id, out var historyEntry))
        {
            historyEntry = new OperatorVersionHistoryEntry
            {
                Id = op.Id,
                DisplayName = op.DisplayName,
                CategoryId = op.CategoryId,
                Category = op.Category,
                Lifecycle = op.Lifecycle
            };
            historyById[op.Id] = historyEntry;
        }
        else
        {
            if (!string.Equals(historyEntry.DisplayName, op.DisplayName, StringComparison.Ordinal))
            {
                historyEntry.DisplayName = op.DisplayName;
            }

            if (!string.Equals(historyEntry.Category, op.Category, StringComparison.Ordinal))
            {
                historyEntry.Category = op.Category;
            }

            historyEntry.CategoryId = op.CategoryId;
            historyEntry.Lifecycle = op.Lifecycle;
        }

        var latest = historyEntry.Records.LastOrDefault();
        if (latest == null)
        {
            historyEntry.Records.Add(new OperatorVersionRecord
            {
                Version = op.Version,
                SourceHash = sourceHash,
                FingerprintVersion = fingerprintVersion,
                RecordedAt = recordedAt
            });
            continue;
        }

        var fingerprintSchemeChanged = !string.Equals(
            latest.FingerprintVersion,
            fingerprintVersion,
            StringComparison.Ordinal);
        var sourceChanged = !string.Equals(latest.SourceHash, sourceHash, StringComparison.Ordinal);
        var versionChanged = !string.Equals(latest.Version, op.Version, StringComparison.Ordinal);

        if (fingerprintSchemeChanged && !versionChanged)
        {
            latest.SourceHash = sourceHash;
            latest.FingerprintVersion = fingerprintVersion;
            latest.RecordedAt = recordedAt;
        }
        else
        {
            if (sourceChanged && !versionChanged)
            {
                violations.Add(new VersionBumpViolation
                {
                    OperatorId = op.Id,
                    CurrentVersion = op.Version
                });
            }

            if (sourceChanged || versionChanged || fingerprintSchemeChanged)
            {
                historyEntry.Records.Add(new OperatorVersionRecord
                {
                    Version = op.Version,
                    SourceHash = sourceHash,
                    FingerprintVersion = fingerprintVersion,
                    RecordedAt = recordedAt
                });
            }
        }
    }

    var mergedHistory = new OperatorVersionHistoryDocument
    {
        GeneratedAt = recordedAt,
        Operators = historyById.Values
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList()
    };

    SaveVersionHistory(historyPath, mergedHistory);
    File.WriteAllText(changelogPath, BuildVersionChangelogMarkdown(operators, mergedHistory, generatedAt), new UTF8Encoding(false));

    return new VersionTrackingResult(violations);
}

static OperatorVersionHistoryDocument LoadVersionHistory(string historyPath)
{
    if (!File.Exists(historyPath))
    {
        return new OperatorVersionHistoryDocument();
    }

    try
    {
        var json = File.ReadAllText(historyPath);
        var model = JsonSerializer.Deserialize<OperatorVersionHistoryDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return model ?? new OperatorVersionHistoryDocument();
    }
    catch
    {
        return new OperatorVersionHistoryDocument();
    }
}

static void SaveVersionHistory(string historyPath, OperatorVersionHistoryDocument history)
{
    var json = JsonSerializer.Serialize(
        history,
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

    File.WriteAllText(historyPath, json + Environment.NewLine, new UTF8Encoding(false));
}

static string BuildVersionChangelogMarkdown(
    IReadOnlyList<CatalogOperator> operators,
    OperatorVersionHistoryDocument history,
    DateTimeOffset generatedAt)
{
    var sb = new StringBuilder();
    sb.AppendLine("# 算子版本变更记录 / Operator Version Changelog");
    sb.AppendLine();
    sb.AppendLine($"> 生成时间 / Generated At: `{generatedAt:yyyy-MM-dd HH:mm:ss zzz}`");
    sb.AppendLine($"> 算子总数 / Total Operators: **{operators.Count}**");
    sb.AppendLine();

    sb.AppendLine("## 当前版本快照 / Current Snapshot");
    sb.AppendLine("| 枚举 (Enum) | 显示名 (DisplayName) | 分类 ID | 分类 (Category) | 生命周期 | 版本 (Version) |");
    sb.AppendLine("|------|------|------|------|------|------|");
    foreach (var op in operators.OrderBy(item => item.CategoryOrder).ThenBy(item => item.Id, StringComparer.Ordinal))
    {
        sb.AppendLine($"| `OperatorType.{op.Id}` | {EscapeCell(op.DisplayName)} | `{op.CategoryId}` | {EscapeCell(op.Category)} | `{op.Lifecycle}` | `{op.Version}` |");
    }

    var historyById = history.Operators
        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
        .ToDictionary(item => item.Id, item => item, StringComparer.Ordinal);

    var changedOperators = operators
        .Where(op => historyById.TryGetValue(op.Id, out var entry) && entry.Records.Count > 1)
        .OrderBy(op => op.Id, StringComparer.Ordinal)
        .ToList();

    sb.AppendLine();
    sb.AppendLine("## 历史变更 / Historical Changes");
    if (changedOperators.Count == 0)
    {
        sb.AppendLine("- 当前暂无历史版本变更（均为基线版本记录）。");
        return sb.ToString();
    }

    foreach (var op in changedOperators)
    {
        var historyEntry = historyById[op.Id];
        sb.AppendLine();
        sb.AppendLine($"### OperatorType.{op.Id} / {EscapeCell(historyEntry.DisplayName)}");
        sb.AppendLine("| 版本 (Version) | 记录时间 (Recorded At) | 组合指纹 (Generation Fingerprint) | 指纹方案 |");
        sb.AppendLine("|------|------|------|------|");
        foreach (var record in historyEntry.Records.AsEnumerable().Reverse())
        {
            var hash = record.SourceHash.Length > 12 ? record.SourceHash[..12] : record.SourceHash;
            sb.AppendLine($"| `{record.Version}` | `{record.RecordedAt}` | `{hash}` | `{Fallback(record.FingerprintVersion, "legacy-source-only")}` |");
        }
    }

    return sb.ToString();
}

static string ComputeSha256(string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
}

static string ComputeOperatorGenerationFingerprint(OperatorDocModel item, QualityContext qualityContext)
{
    if (!qualityContext.SourceTextByTypeName.TryGetValue(item.ClrType.Name, out var operatorSource))
    {
        throw new InvalidOperationException($"Source file was not resolved for {item.ClrType.FullName}.");
    }

    var dependencySources = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var dependency in item.Metadata.GenerationDependencies
                 .Where(value => !string.IsNullOrWhiteSpace(value))
                 .Distinct(StringComparer.Ordinal))
    {
        if (dependency.StartsWith("type:", StringComparison.Ordinal))
        {
            var qualifiedType = dependency["type:".Length..];
            var simpleType = qualifiedType
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Split('+', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(simpleType) ||
                !qualityContext.SourceTextByTypeName.TryGetValue(simpleType, out var sourceText))
            {
                throw new InvalidOperationException(
                    $"Generation dependency '{dependency}' for OperatorType.{item.OperatorType} has no source file.");
            }

            dependencySources[dependency] = sourceText;
            continue;
        }

        if (dependency.StartsWith("source:", StringComparison.Ordinal))
        {
            var relativePath = dependency["source:".Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(qualityContext.RepoRoot, relativePath));
            var repoPrefix = Path.GetFullPath(qualityContext.RepoRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"Generation dependency '{dependency}' for OperatorType.{item.OperatorType} is outside the repository or missing.");
            }

            dependencySources[dependency] = File.ReadAllText(fullPath);
            continue;
        }

        throw new InvalidOperationException(
            $"Unsupported generation dependency '{dependency}' for OperatorType.{item.OperatorType}.");
    }

    return OperatorGenerationFingerprintBuilder.Compute(item.Metadata, operatorSource, dependencySources);
}

static QualityContext BuildQualityContext(string repoRoot, string docsRoot)
{
    var sourceIndex = BuildOperatorSourceIndex(repoRoot);
    var testIndex = BuildOperatorTestIndex(repoRoot);

    return new QualityContext(
        repoRoot,
        docsRoot,
        sourceIndex.SourcePathByTypeName,
        sourceIndex.SourceTextByTypeName,
        testIndex,
        BuildGoldenEvidenceIndex(repoRoot));
}

static (Dictionary<string, string> SourcePathByTypeName, Dictionary<string, string> SourceTextByTypeName) BuildOperatorSourceIndex(string repoRoot)
{
    var infrastructureRoot = Path.Combine(repoRoot, "ClearVision.Product", "src", "ClearVision.Product.Infrastructure");
    var pathByType = new Dictionary<string, string>(StringComparer.Ordinal);
    var textByType = new Dictionary<string, string>(StringComparer.Ordinal);

    if (!Directory.Exists(infrastructureRoot))
    {
        return (pathByType, textByType);
    }

    var classPattern = new Regex(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

    foreach (var filePath in Directory.EnumerateFiles(infrastructureRoot, "*.cs", SearchOption.AllDirectories))
    {
        var sourceText = File.ReadAllText(filePath);
        var matches = classPattern.Matches(sourceText);

        foreach (Match match in matches)
        {
            var className = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(className) || pathByType.ContainsKey(className))
            {
                continue;
            }

            pathByType[className] = filePath;
            textByType[className] = sourceText;
        }
    }

    return (pathByType, textByType);
}

static HashSet<string> BuildOperatorTestIndex(string repoRoot)
{
    var testsRoot = Path.Combine(repoRoot, "ClearVision.Product", "tests", "ClearVision.Product.Tests", "Operators");
    var index = new HashSet<string>(StringComparer.Ordinal);

    if (!Directory.Exists(testsRoot))
    {
        return index;
    }

    foreach (var filePath in Directory.EnumerateFiles(testsRoot, "*Tests.cs", SearchOption.AllDirectories))
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith("Tests", StringComparison.Ordinal))
        {
            var candidate = fileName[..^"Tests".Length];
            index.Add(candidate);

            if (candidate.EndsWith("Operator", StringComparison.Ordinal))
            {
                index.Add(candidate[..^"Operator".Length]);
            }
        }

    }

    return index;
}

static HashSet<string> BuildGoldenEvidenceIndex(string repoRoot)
{
    var index = new HashSet<string>(StringComparer.Ordinal);
    var reportsRoot = Path.Combine(repoRoot, "quality", "evals", "reports");
    if (!Directory.Exists(reportsRoot))
    {
        return index;
    }

    foreach (var baselinePath in Directory.EnumerateFiles(reportsRoot, "*_baseline.json", SearchOption.TopDirectoryOnly))
    {
        AddGoldenEvidenceFromBaseline(baselinePath, index);
    }

    return index;
}

static void AddGoldenEvidenceFromBaseline(string baselinePath, HashSet<string> index)
{
    try
    {
        using var stream = File.OpenRead(baselinePath);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("Operators", out var operators) || operators.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in operators.EnumerateArray())
        {
            var operatorId = item.TryGetProperty("Operator", out var operatorElement)
                ? operatorElement.GetString()
                : null;
            var caseCount = item.TryGetProperty("CaseCount", out var caseCountElement)
                ? caseCountElement.GetInt32()
                : 0;
            var failed = item.TryGetProperty("Failed", out var failedElement)
                ? failedElement.GetInt32()
                : 0;

            if (!string.IsNullOrWhiteSpace(operatorId) && caseCount >= 20 && failed == 0)
            {
                index.Add(operatorId);
            }
        }
    }
    catch (JsonException)
    {
        return;
    }
}

static string? NormalizeParameterValue(object? value)
{
    if (value == null)
    {
        return null;
    }

    if (value is string text)
    {
        return text;
    }

    if (value is bool b)
    {
        return b ? "true" : "false";
    }

    if (value is IFormattable formattable)
    {
        return formattable.ToString(null, CultureInfo.InvariantCulture);
    }

    return Convert.ToString(value, CultureInfo.InvariantCulture);
}

static string BuildRange(object? min, object? max)
{
    var minText = FormatValue(min);
    var maxText = FormatValue(max);
    return (min, max) switch
    {
        (null, null) => "-",
        (_, null) => $">= {minText}",
        (null, _) => $"<= {maxText}",
        _ => $"[{minText}, {maxText}]"
    };
}

static string FormatValue(object? value)
{
    if (value == null)
    {
        return "-";
    }

    if (value is string text)
    {
        return text.Length == 0 ? "\"\"" : text;
    }

    if (value is bool b)
    {
        return b ? "true" : "false";
    }

    if (value is IFormattable formattable)
    {
        return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "-";
    }

    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
}

static string BoolToMark(bool value) => value ? "Yes" : "No";

static string GetLifecycleDisplayName(OperatorLifecycle lifecycle) => lifecycle switch
{
    OperatorLifecycle.Stable => "稳定",
    OperatorLifecycle.Experimental => "实验",
    OperatorLifecycle.Reference => "参考",
    OperatorLifecycle.Legacy => "旧版",
    OperatorLifecycle.Deprecated => "已弃用",
    _ => lifecycle.ToString()
};

static string DescribeConditionSet(OperatorParameterConditionSet? conditionSet)
{
    if (conditionSet is null)
    {
        return "-";
    }

    var parts = new List<string>();
    if (conditionSet.All is { Count: > 0 })
    {
        parts.Add("ALL(" + string.Join(" && ", conditionSet.All.Select(DescribeCondition)) + ")");
    }

    if (conditionSet.Any is { Count: > 0 })
    {
        parts.Add("ANY(" + string.Join(" || ", conditionSet.Any.Select(DescribeCondition)) + ")");
    }

    return parts.Count == 0 ? "-" : string.Join("; ", parts);
}

static string DescribeCondition(OperatorParameterCondition condition)
{
    var comparison = condition.Comparison switch
    {
        OperatorParameterConditionComparisons.Equal => "==",
        OperatorParameterConditionComparisons.NotEquals => "!=",
        OperatorParameterConditionComparisons.Empty => "is empty",
        OperatorParameterConditionComparisons.NotEmpty => "is not empty",
        _ => condition.Comparison
    };

    return condition.Value is null
        ? $"{condition.Parameter} {comparison}"
        : $"{condition.Parameter} {comparison} {FormatValue(condition.Value)}";
}

static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

static IEnumerable<string> NonEmptyItems(string[]? values) =>
    values?
        .Select(value => value?.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!) ?? Enumerable.Empty<string>();

static string EscapeCell(string input) => input.Replace("|", "\\|", StringComparison.Ordinal);

internal sealed record OperatorDocModel(
    Type ClrType,
    AlgorithmInfoAttribute? Algo,
    OperatorType OperatorType,
    OperatorMetadata Metadata);

internal sealed record OperatorSourceFacts(
    string SourceText,
    List<string> CoreApiCalls,
    List<RuntimeOutputField> RuntimeAdditionalOutputs,
    int FailureCount,
    bool HasValidation,
    bool HasTryCatch,
    bool HasShortCircuit,
    bool UsesImageOutput,
    bool UsesOpenCv,
    bool UsesExternalIo,
    bool UsesModel,
    bool UsesState);

internal sealed record RuntimeOutputField(
    string Name,
    string DataType,
    string Description);

internal sealed class CatalogDocument
{
    public string GeneratedAt { get; set; } = string.Empty;

    public string GenerationFingerprint { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public Dictionary<string, CatalogCategorySummary> Categories { get; set; } = new(StringComparer.Ordinal);

    public List<CatalogOperator> Operators { get; set; } = new();
}

internal sealed class CatalogCategorySummary
{
    public string CategoryId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int Order { get; set; }

    public int Count { get; set; }

    public List<string> Operators { get; set; } = new();
}

internal sealed class CatalogOperator
{
    public string Id { get; set; } = string.Empty;

    public int Type { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    public int CategoryOrder { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Lifecycle { get; set; } = string.Empty;

    public string? LifecycleNote { get; set; }

    public bool DefaultHidden { get; set; }

    public bool DefaultAiRecommendation { get; set; }

    public bool RequiresLifecycleDisclosure { get; set; }

    public OperatorQualityState QualityState { get; set; } = OperatorQualityState.Unknown;

    public string Version { get; set; } = "1.0.0";

    public List<string> Keywords { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public string? Algorithm { get; set; }

    public List<CatalogPort> InputPorts { get; set; } = new();

    public List<CatalogPort> OutputPorts { get; set; } = new();

    public List<CatalogParameter> Parameters { get; set; } = new();

    public List<OperatorParameterConstraint> ParameterConditions { get; set; } = new();

    public List<OperatorOutputAvailabilityRule> OutputConditions { get; set; } = new();

    public List<ImageInputContractPresentation> ImageInputContracts { get; set; } = new();

    public List<CatalogResourceRequirement> ResourceRequirements { get; set; } = new();

    public List<string> GenerationDependencies { get; set; } = new();

    public string GenerationFingerprint { get; set; } = string.Empty;

    public string DocPath { get; set; } = string.Empty;
}

internal sealed class CatalogPort
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool? IsRequired { get; set; }
}

internal sealed class CatalogParameter
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? DefaultValue { get; set; }

    public string? Min { get; set; }

    public string? Max { get; set; }

    public bool IsRequired { get; set; }

    public List<CatalogParameterOption>? Options { get; set; }
}

internal sealed class CatalogParameterOption
{
    public string Value { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

internal sealed class CatalogResourceRequirement
{
    public string Parameter { get; set; } = string.Empty;

    public string ResourceKind { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public string? AtLeastOneGroup { get; set; }

    public OperatorParameterConditionSet? RequiredWhen { get; set; }

    public List<string> SatisfiedByInputPorts { get; set; } = new();
}

internal sealed class OperatorVersionHistoryDocument
{
    public string GeneratedAt { get; set; } = string.Empty;

    public List<OperatorVersionHistoryEntry> Operators { get; set; } = new();
}

internal sealed class OperatorVersionHistoryEntry
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Lifecycle { get; set; } = string.Empty;

    public List<OperatorVersionRecord> Records { get; set; } = new();
}

internal sealed class OperatorVersionRecord
{
    public string Version { get; set; } = "1.0.0";

    public string SourceHash { get; set; } = string.Empty;

    public string FingerprintVersion { get; set; } = string.Empty;

    public string RecordedAt { get; set; } = string.Empty;
}

internal sealed class VersionBumpViolation
{
    public string OperatorId { get; set; } = string.Empty;

    public string CurrentVersion { get; set; } = "1.0.0";
}

internal sealed record VersionTrackingResult(IReadOnlyList<VersionBumpViolation> Violations);

internal sealed record QualityContext(
    string RepoRoot,
    string DocsRoot,
    IReadOnlyDictionary<string, string> SourcePathByTypeName,
    IReadOnlyDictionary<string, string> SourceTextByTypeName,
    IReadOnlySet<string> TestIndex,
    IReadOnlySet<string> GoldenEvidenceIndex);

