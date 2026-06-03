using System.Text.Json;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;

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

var workspaceRoot = ResolveWorkspaceRoot();
var storageRoot = Path.Combine(
    Path.GetTempPath(),
    "clearvision-operator-knowledge-graph-runner",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storageRoot);

var operatorFactory = new OperatorFactory();
var templateService = new FlowTemplateService(storageRoot);
var graphService = new OperatorKnowledgeGraphService(operatorFactory, templateService);

var graph = await graphService.BuildAsync();

var graphOutputPath = Path.GetFullPath(Path.Combine(workspaceRoot, options.GraphOutputPath));
var cardsOutputPath = Path.GetFullPath(Path.Combine(workspaceRoot, options.CardsOutputPath));
var reportOutputPath = string.IsNullOrWhiteSpace(options.ReportOutputPath)
    ? null
    : Path.GetFullPath(Path.Combine(workspaceRoot, options.ReportOutputPath));

Directory.CreateDirectory(Path.GetDirectoryName(graphOutputPath)!);
Directory.CreateDirectory(Path.GetDirectoryName(cardsOutputPath)!);

await File.WriteAllTextAsync(
    graphOutputPath,
    JsonSerializer.Serialize(graph, JsonSettings.Indented));

await File.WriteAllTextAsync(
    cardsOutputPath,
    JsonSerializer.Serialize(graph.Cards, JsonSettings.Indented));

if (reportOutputPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(reportOutputPath)!);
    await File.WriteAllTextAsync(reportOutputPath, MarkdownReport.Create(graph));
}

Console.WriteLine(
    $"Operator knowledge graph generated: cards={graph.Cards.Count}, edges={graph.Edges.Count}, " +
    $"graph={graphOutputPath}, cards={cardsOutputPath}");

try
{
    Directory.Delete(storageRoot, recursive: true);
}
catch
{
    // Best-effort cleanup only; generation outputs have already been written.
}

return 0;

static string ResolveWorkspaceRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        var candidate = Path.Combine(current.FullName, "ClearVision.Product");
        if (Directory.Exists(candidate))
            return current.FullName;

        current = current.Parent;
    }

    return Directory.GetCurrentDirectory();
}

internal sealed record RunnerOptions(
    string GraphOutputPath,
    string CardsOutputPath,
    string? ReportOutputPath,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var graphOutput = "docs/ai/operator-knowledge/operator_knowledge_graph.json";
        var cardsOutput = "docs/ai/operator-knowledge/operator_knowledge_cards.json";
        var reportOutput = "docs/ai/operator-knowledge/operator_knowledge_graph_report.md";
        var showHelp = false;
        string? parseError = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--graph-output":
                    graphOutput = ReadValue(args, ref index, arg, ref parseError) ?? graphOutput;
                    break;
                case "--cards-output":
                    cardsOutput = ReadValue(args, ref index, arg, ref parseError) ?? cardsOutput;
                    break;
                case "--report-output":
                    reportOutput = ReadValue(args, ref index, arg, ref parseError) ?? reportOutput;
                    break;
                case "--no-report":
                    reportOutput = null;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    parseError = $"Unknown argument: {arg}";
                    break;
            }
        }

        return new RunnerOptions(graphOutput, cardsOutput, reportOutput, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            Operator knowledge graph runner

            Options:
              --graph-output <path>   Graph JSON output path.
              --cards-output <path>   Knowledge cards JSON output path.
              --report-output <path>  Markdown report output path.
              --no-report             Skip markdown report generation.
            """);
    }

    private static string? ReadValue(string[] args, ref int index, string name, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"{name} requires a value.";
            return null;
        }

        index++;
        return args[index];
    }
}

internal static class MarkdownReport
{
    public static string Create(OperatorKnowledgeGraph graph)
    {
        var relationCounts = graph.Edges
            .GroupBy(edge => edge.RelationType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>
        {
            "# Operator Knowledge Graph Report",
            string.Empty,
            $"GeneratedAtUtc: `{graph.GeneratedAtUtc:O}`",
            $"SchemaVersion: `{graph.SchemaVersion}`",
            $"Source: `{graph.Source}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cards | {graph.Cards.Count} |",
            $"| Edges | {graph.Edges.Count} |",
            string.Empty,
            "## Edge Types",
            string.Empty,
            "| RelationType | Count |",
            "| --- | ---: |"
        };

        foreach (var relation in relationCounts)
            lines.Add($"| {relation.Key} | {relation.Count()} |");

        lines.AddRange(new[]
        {
            string.Empty,
            "## Top Operators By Downstream Degree",
            string.Empty,
            "| OperatorType | TypicalDownstreamCount | TypicalUpstreamCount |",
            "| --- | ---: | ---: |"
        });

        foreach (var card in graph.Cards
                     .OrderByDescending(item => item.TypicalDownstream.Count)
                     .ThenBy(item => item.OperatorType, StringComparer.OrdinalIgnoreCase)
                     .Take(20))
        {
            lines.Add($"| {card.OperatorType} | {card.TypicalDownstream.Count} | {card.TypicalUpstream.Count} |");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
    };
}
