using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

var options = RunnerOptions.Parse(args);
if (options.ShowHelp)
{
    RunnerOptions.PrintHelp();
    return options.ParseError is null ? 0 : 2;
}

if (options.ParseError is not null)
{
    Console.Error.WriteLine(options.ParseError);
    RunnerOptions.PrintHelp();
    return 2;
}

var result = ContractBatch1Runner.Run();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"G1 P3 contract batch1 complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"operators={result.Operators.Count}, failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractBatch1Runner
{
    private const string EvidenceKind = "contract";
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string MatrixPath = Path.Combine(RepoRoot, "quality", "evals", "reports", "operator_quality_matrix.md");
    private static readonly string CardDir = Path.Combine(RepoRoot, "docs", "算子资料", "算子名片");

    private static readonly OperatorSpec[] Specs =
    [
        new(OperatorType.BilateralFilter, typeof(BilateralFilterOperator), "image-preprocessing"),
        new(OperatorType.BoxFilter, typeof(BoundingBoxFilterOperator), "image-preprocessing"),
        new(OperatorType.ClaheEnhancement, typeof(ClaheEnhancementOperator), "image-preprocessing"),
        new(OperatorType.ColorConversion, typeof(ColorConversionOperator), "image-preprocessing"),
        new(OperatorType.ContourMeasurement, typeof(ContourMeasurementOperator), "image-preprocessing"),
        new(OperatorType.CopyMakeBorder, typeof(CopyMakeBorderOperator), "image-preprocessing"),
        new(OperatorType.CornerDetection, typeof(CornerDetectionOperator), "image-preprocessing"),
        new(OperatorType.Filtering, typeof(GaussianBlurOperator), "image-preprocessing"),
        new(OperatorType.FrameAveraging, typeof(FrameAveragingOperator), "image-preprocessing"),
        new(OperatorType.HistogramAnalysis, typeof(HistogramAnalysisOperator), "image-preprocessing"),
        new(OperatorType.HistogramEqualization, typeof(HistogramEqualizationOperator), "image-preprocessing"),
        new(OperatorType.ImageBlend, typeof(ImageBlendOperator), "image-preprocessing"),
        new(OperatorType.ImageCompose, typeof(ImageComposeOperator), "image-preprocessing"),
        new(OperatorType.ImageCrop, typeof(ImageCropOperator), "image-preprocessing"),
        new(OperatorType.ImageNormalize, typeof(ImageNormalizeOperator), "image-preprocessing"),
        new(OperatorType.ImageResize, typeof(ImageResizeOperator), "image-preprocessing"),
        new(OperatorType.ImageRotate, typeof(ImageRotateOperator), "image-preprocessing"),
        new(OperatorType.ImageStitching, typeof(ImageStitchingOperator), "image-preprocessing"),
        new(OperatorType.ImageTiling, typeof(ImageTilingOperator), "image-preprocessing"),
        new(OperatorType.LaplacianSharpen, typeof(LaplacianSharpenOperator), "image-preprocessing"),
        new(OperatorType.MeanFilter, typeof(MeanFilterOperator), "image-preprocessing"),
        new(OperatorType.MedianBlur, typeof(MedianBlurOperator), "image-preprocessing"),
        new(OperatorType.MorphologicalOperation, typeof(MorphologicalOperationOperator), "image-preprocessing"),
        new(OperatorType.Morphology, typeof(MorphologyOperator), "image-preprocessing"),
        new(OperatorType.ParallelLineFind, typeof(ParallelLineFindOperator), "image-preprocessing"),
        new(OperatorType.PositionCorrection, typeof(PositionCorrectionOperator), "image-preprocessing"),
        new(OperatorType.QuadrilateralFind, typeof(QuadrilateralFindOperator), "image-preprocessing"),
        new(OperatorType.RectangleDetection, typeof(RectangleDetectionOperator), "image-preprocessing"),
        new(OperatorType.SubpixelEdgeDetection, typeof(SubpixelEdgeDetectionOperator), "image-preprocessing"),
        new(OperatorType.Thresholding, typeof(ThresholdOperator), "image-preprocessing"),
        new(OperatorType.AngleMeasurement, typeof(AngleMeasurementOperator), "measurement-geometry"),
        new(OperatorType.ColorDetection, typeof(ColorDetectionOperator), "measurement-geometry"),
        new(OperatorType.ColorMeasurement, typeof(ColorMeasurementOperator), "measurement-geometry"),
        new(OperatorType.EdgeIntersection, typeof(EdgeIntersectionOperator), "measurement-geometry"),
        new(OperatorType.GapMeasurement, typeof(GapMeasurementOperator), "measurement-geometry"),
        new(OperatorType.GeoMeasurement, typeof(GeoMeasurementOperator), "measurement-geometry"),
        new(OperatorType.GeometricTolerance, typeof(GeometricToleranceOperator), "measurement-geometry"),
        new(OperatorType.LineLineDistance, typeof(LineLineDistanceOperator), "measurement-geometry"),
        new(OperatorType.Measurement, typeof(MeasureDistanceOperator), "measurement-geometry"),
        new(OperatorType.PixelStatistics, typeof(PixelStatisticsOperator), "measurement-geometry")
    ];

    public static BaselineResult Run()
    {
        var matrixRows = LoadMatrixRows(MatrixPath);
        var cases = new List<ContractCase>(Specs.Length * 20);
        foreach (var spec in Specs)
        {
            AddSpecCases(cases, spec, matrixRows);
        }

        var results = new List<CaseResult>(cases.Count);
        foreach (var contractCase in cases)
        {
            results.Add(RunCase(contractCase));
        }

        var operators = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperatorEvidence(
                group.Key,
                EvidenceKind,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(group.Average(item => item.MemoryAllocationBytes)))))
            .ToArray();

        var scenarios = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToArray();

        return new BaselineResult(
            EvidenceKind,
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                Math.Round(results.Sum(item => item.RuntimeMs), 3)),
            operators,
            scenarios,
            results);
    }

    private static void AddSpecCases(List<ContractCase> cases, OperatorSpec spec, IReadOnlyDictionary<string, MatrixRow> matrixRows)
    {
        var operatorName = spec.OperatorType.ToString();
        var cardPath = Path.Combine(CardDir, $"{operatorName}.md");

        Add(cases, spec, "enum_name_roundtrip", "OperatorType contract", () =>
        {
            Require(Enum.TryParse<OperatorType>(operatorName, out var parsed), "OperatorType should parse by name.");
            Require(parsed == spec.OperatorType, "OperatorType parse should round-trip.");
            return Observed(("Enum", operatorName));
        });
        Add(cases, spec, "enum_numeric_stable", "OperatorType contract", () =>
        {
            Require(Convert.ToInt32(spec.OperatorType) >= 0, "OperatorType numeric value should be non-negative.");
            return Observed(("EnumValue", Convert.ToInt32(spec.OperatorType)));
        });
        Add(cases, spec, "implementation_type_name", "Implementation contract", () =>
        {
            Require(spec.ImplementationType.Name.EndsWith("Operator", StringComparison.Ordinal), "Implementation type should use Operator suffix.");
            return Observed(("Implementation", spec.ImplementationType.Name));
        });
        Add(cases, spec, "implementation_namespace", "Implementation contract", () =>
        {
            Require(spec.ImplementationType.Namespace?.Contains("Infrastructure.Operators", StringComparison.Ordinal) == true,
                "Implementation type should live under Infrastructure.Operators.");
            return Observed(("Namespace", spec.ImplementationType.Namespace));
        });
        Add(cases, spec, "implementation_public_concrete", "Implementation contract", () =>
        {
            Require(spec.ImplementationType.IsClass && !spec.ImplementationType.IsAbstract && spec.ImplementationType.IsPublic,
                "Implementation type should be a public concrete class.");
            return Observed(("IsPublic", spec.ImplementationType.IsPublic));
        });
        Add(cases, spec, "derives_operator_base", "Implementation contract", () =>
        {
            Require(typeof(OperatorBase).IsAssignableFrom(spec.ImplementationType), "Implementation should derive OperatorBase.");
            return Observed(("BaseType", spec.ImplementationType.BaseType?.Name));
        });
        Add(cases, spec, "implements_executor", "Implementation contract", () =>
        {
            Require(typeof(IOperatorExecutor).IsAssignableFrom(spec.ImplementationType), "Implementation should implement IOperatorExecutor.");
            return Observed(("Executor", true));
        });
        Add(cases, spec, "has_execute_async", "Implementation contract", () =>
        {
            Require(spec.ImplementationType.GetMethod(nameof(IOperatorExecutor.ExecuteAsync)) is not null, "Implementation should expose ExecuteAsync.");
            return Observed(("Method", nameof(IOperatorExecutor.ExecuteAsync)));
        });
        Add(cases, spec, "has_validate_parameters", "Implementation contract", () =>
        {
            Require(spec.ImplementationType.GetMethod(nameof(IOperatorExecutor.ValidateParameters)) is not null, "Implementation should expose ValidateParameters.");
            return Observed(("Method", nameof(IOperatorExecutor.ValidateParameters)));
        });
        Add(cases, spec, "supported_constructor", "DI construction contract", () =>
        {
            var ctor = FindSupportedConstructor(spec.ImplementationType);
            Require(ctor is not null, "Implementation should have a supported constructor surface.");
            return Observed(("ConstructorParameters", ctor!.GetParameters().Length));
        });
        Add(cases, spec, "can_create_executor", "DI construction contract", () =>
        {
            using var disposable = CreateExecutor(spec.ImplementationType) as IDisposable;
            Require(disposable is not null || typeof(IOperatorExecutor).IsAssignableFrom(spec.ImplementationType), "Executor creation should return an executor.");
            return Observed(("Created", true));
        });
        Add(cases, spec, "default_validation_no_throw", "Parameter contract", () =>
        {
            var executor = CreateExecutor(spec.ImplementationType);
            var validation = executor.ValidateParameters(CreateOperator(spec.OperatorType));
            Require(validation is not null, "Default validation should return a result.");
            DisposeIfNeeded(executor);
            return Observed(("DefaultIsValid", validation!.IsValid), ("Errors", validation.Errors.Count));
        });
        Add(cases, spec, "missing_input_execute_contract", "Execution failure contract", () =>
        {
            var executor = CreateExecutor(spec.ImplementationType);
            var result = executor.ExecuteAsync(CreateOperator(spec.OperatorType), null).GetAwaiter().GetResult();
            DisposeIfNeeded(executor);
            Require(result is not null, "ExecuteAsync should return an output object.");
            Require(result!.OutputData is not null || !string.IsNullOrWhiteSpace(result.ErrorMessage),
                "Execution output should include output data or a structured error message.");
            return Observed(("IsSuccess", result.IsSuccess), ("HasErrorMessage", !string.IsNullOrWhiteSpace(result.ErrorMessage)));
        });
        Add(cases, spec, "entity_contract", "Operator entity contract", () =>
        {
            var op = CreateOperator(spec.OperatorType);
            Require(op.Id != Guid.Empty, "Operator entity id should be set.");
            Require(op.Type == spec.OperatorType, "Operator entity type should match the spec.");
            return Observed(("OperatorId", op.Id), ("OperatorType", op.Type.ToString()));
        });
        Add(cases, spec, "parameter_roundtrip", "Operator entity contract", () =>
        {
            var op = CreateOperator(spec.OperatorType, ("ContractProbe", 42));
            var parameter = op.Parameters.Single(item => item.Name == "ContractProbe");
            var actualValue = parameter.Value is JsonElement element && element.TryGetInt32(out var jsonValue)
                ? jsonValue
                : Convert.ToInt32(parameter.Value);
            Require(actualValue == 42, "Operator parameter should round-trip.");
            return Observed(("Parameter", parameter.Name), ("Value", parameter.Value));
        });
        Add(cases, spec, "matrix_row_exists", "Matrix registry contract", () =>
        {
            Require(matrixRows.TryGetValue(operatorName, out var row), "Operator should exist in operator_quality_matrix.");
            return Observed(("DisplayName", row!.DisplayName), ("Priority", row.Priority));
        });
        Add(cases, spec, "matrix_level_a", "Matrix registry contract", () =>
        {
            var row = RequireMatrixRow(matrixRows, operatorName);
            Require(row.Level == "A", "Batch1 operators should stay in level A scope.");
            Require(row.QScore > 0, "Matrix QScore should be present.");
            return Observed(("QScore", row.QScore), ("Level", row.Level));
        });
        Add(cases, spec, "matrix_ports_non_negative", "Matrix registry contract", () =>
        {
            var row = RequireMatrixRow(matrixRows, operatorName);
            Require(row.Inputs >= 0 && row.Outputs >= 0 && row.Parameters >= 0, "Matrix port counts should be non-negative.");
            return Observed(("Inputs", row.Inputs), ("Outputs", row.Outputs), ("Parameters", row.Parameters));
        });
        Add(cases, spec, "card_file_exists", "Card contract", () =>
        {
            Require(File.Exists(cardPath), $"Operator card should exist: {cardPath}");
            return Observed(("Card", Path.GetRelativePath(RepoRoot, cardPath).Replace('\\', '/')));
        });
        Add(cases, spec, "card_sections_present", "Card contract", () =>
        {
            var text = File.ReadAllText(cardPath);
            var hasBasicOrContract = text.Contains("基本信息", StringComparison.Ordinal)
                || text.Contains("Basic Info", StringComparison.Ordinal)
                || text.Contains("当前契约", StringComparison.Ordinal);
            var hasAlgorithmOrSemantics = text.Contains("算法原理", StringComparison.Ordinal)
                || text.Contains("Algorithm Principle", StringComparison.Ordinal)
                || text.Contains("算法说明", StringComparison.Ordinal)
                || text.Contains("关键语义", StringComparison.Ordinal);
            var hasPorts = text.Contains("输入/输出", StringComparison.Ordinal)
                || text.Contains("Input/Output", StringComparison.Ordinal)
                || (text.Contains("## 输入", StringComparison.Ordinal) && text.Contains("## 输出", StringComparison.Ordinal));
            Require(hasBasicOrContract, "Card should include basic info or a current contract section.");
            Require(hasAlgorithmOrSemantics, "Card should include algorithm or semantic contract notes.");
            Require(hasPorts,
                "Card should include input/output contract.");
            return Observed(("CardSections", "basic,algorithm,ports"));
        });
        Add(cases, spec, "batch1_group_frozen", "Batch selection contract", () =>
        {
            Require(spec.Group is "image-preprocessing" or "measurement-geometry", "Batch1 group should be frozen to the approved priority lanes.");
            return Observed(("Group", spec.Group));
        });
    }

    private static CaseResult RunCase(ContractCase contractCase)
    {
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var observed = contractCase.Body();
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                contractCase.CaseId,
                contractCase.Operator,
                contractCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                null,
                observed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                contractCase.CaseId,
                contractCase.Operator,
                contractCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                ex.GetBaseException().Message,
                new Dictionary<string, object?>());
        }
    }

    private static IReadOnlyDictionary<string, MatrixRow> LoadMatrixRows(string path)
    {
        var rows = new Dictionary<string, MatrixRow>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("| OperatorType.", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitMarkdownRow(line);
            Require(cells.Count >= 25, $"Unexpected matrix row shape for line: {line}");
            var operatorName = cells[0].Split('.').Last();
            rows[operatorName] = new MatrixRow(
                cells[1],
                ParseInt(cells[3]),
                cells[4],
                ParseInt(cells[7]),
                ParseInt(cells[8]),
                ParseInt(cells[9]),
                cells[22]);
        }

        Require(rows.Count > 0, $"No matrix rows found in {path}.");
        return rows;
    }

    private static List<string> SplitMarkdownRow(string line)
    {
        var value = line.Trim().Trim('|');
        var cells = new List<string>();
        var current = new List<char>();
        var escaped = false;

        foreach (var ch in value)
        {
            if (ch == '\\' && !escaped)
            {
                escaped = true;
                current.Add(ch);
                continue;
            }

            if (ch == '|' && !escaped)
            {
                cells.Add(new string(current.ToArray()).Trim().Replace("\\|", "|", StringComparison.Ordinal));
                current.Clear();
                continue;
            }

            current.Add(ch);
            escaped = false;
        }

        cells.Add(new string(current.ToArray()).Trim().Replace("\\|", "|", StringComparison.Ordinal));
        return cells;
    }

    private static MatrixRow RequireMatrixRow(IReadOnlyDictionary<string, MatrixRow> rows, string operatorName)
    {
        Require(rows.TryGetValue(operatorName, out var row), $"Missing matrix row for {operatorName}.");
        return row!;
    }

    private static ConstructorInfo? FindSupportedConstructor(Type type)
    {
        return type.GetConstructors()
            .OrderBy(item => item.GetParameters().Length)
            .FirstOrDefault(item => item.GetParameters().All(parameter => CanResolve(parameter.ParameterType)));
    }

    private static IOperatorExecutor CreateExecutor(Type type)
    {
        var ctor = FindSupportedConstructor(type);
        Require(ctor is not null, $"No supported constructor found for {type.Name}.");
        var args = ctor!.GetParameters().Select(parameter => Resolve(parameter.ParameterType)).ToArray();
        var instance = ctor.Invoke(args);
        Require(instance is IOperatorExecutor, $"{type.Name} should create IOperatorExecutor.");
        return (IOperatorExecutor)instance;
    }

    private static bool CanResolve(Type parameterType)
    {
        return parameterType == typeof(IServiceProvider)
            || parameterType == typeof(ILogger)
            || (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>));
    }

    private static object Resolve(Type parameterType)
    {
        if (parameterType == typeof(IServiceProvider))
        {
            return EmptyServiceProvider.Instance;
        }

        if (parameterType == typeof(ILogger))
        {
            return NullLogger.Instance;
        }

        if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var loggerType = typeof(NullLogger<>).MakeGenericType(parameterType.GetGenericArguments()[0]);
            return loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? loggerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException($"Unable to resolve logger instance for {parameterType.FullName}.");
        }

        throw new InvalidOperationException($"Unsupported constructor parameter type: {parameterType.FullName}");
    }

    private static void DisposeIfNeeded(object value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(Guid.NewGuid(), $"{type}G1P3ContractBatch1", type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferParameterType(value), value, isRequired: false));
        }

        return op;
    }

    private static string InferParameterType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            float or double or decimal => "double",
            _ => "string"
        };
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value.Trim().Trim('`'), out var parsed) ? parsed : 0;
    }

    private static Dictionary<string, object?> Observed(params (string Name, object? Value)[] values)
    {
        return values.ToDictionary(item => item.Name, item => item.Value);
    }

    private static void Add(List<ContractCase> cases, OperatorSpec spec, string id, string scenario, Func<Dictionary<string, object?>> body)
    {
        var operatorName = spec.OperatorType.ToString();
        cases.Add(new ContractCase($"{operatorName}_{id}", operatorName, scenario, body));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "quality", "evals", "reports", "operator_quality_matrix.md");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from AppContext.BaseDirectory.");
    }
}

internal sealed record OperatorSpec(
    OperatorType OperatorType,
    Type ImplementationType,
    string Group);

internal sealed record MatrixRow(
    string DisplayName,
    int QScore,
    string Level,
    int Inputs,
    int Outputs,
    int Parameters,
    string Priority);

internal sealed record ContractCase(
    string CaseId,
    string Operator,
    string Scenario,
    Func<Dictionary<string, object?>> Body);

internal sealed record BaselineResult(
    string EvidenceKind,
    BaselineSummary Summary,
    IReadOnlyList<OperatorEvidence> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs);

internal sealed record OperatorEvidence(
    string Operator,
    string EvidenceKind,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    Dictionary<string, object?> Observed);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# G1 P3 Contract Batch 1 Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Operators | {result.Operators.Count} |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Operators",
            "",
            "| Operator | Evidence | Cases | Passed | Failed | Avg ms | Avg bytes |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.EvidenceKind} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.MemoryAllocationBytesAvg} |"));

        lines.AddRange(
        [
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        ]);
        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} |"));

        var failures = result.Cases.Where(item => !item.Passed).ToList();
        if (failures.Count > 0)
        {
            lines.AddRange(
            [
                "",
                "## Failures",
                "",
                "| Case | Operator | Scenario | Error |",
                "| --- | --- | --- | --- |"
            ]);
            lines.AddRange(failures.Select(item =>
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {item.ErrorMessage} |"));
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/G1P3ContractBatch1_baseline.json";
        string? report = "quality/evals/reports/G1P3ContractBatch1_baseline.md";
        string? parseError = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--output":
                    output = NextValue(args, ref i, "--output", ref parseError);
                    break;
                case "--report":
                    report = NextValue(args, ref i, "--report", ref parseError);
                    break;
                default:
                    parseError = $"Unknown argument: {args[i]}";
                    break;
            }
        }

        return new RunnerOptions(output, report, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            G1 P3 contract batch1 runner

            Options:
              --output <path>  Baseline JSON output path.
              --report <path>  Markdown report output path.
              --help           Show help.
            """);
    }

    private static string NextValue(string[] args, ref int index, string optionName, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"Missing value for {optionName}";
            return string.Empty;
        }

        index++;
        return args[index];
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static readonly EmptyServiceProvider Instance = new();

    private EmptyServiceProvider()
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
