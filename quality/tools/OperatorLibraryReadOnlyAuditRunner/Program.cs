using ClearVision.OperatorLibrary.ReadOnlyAudit;

var options = AuditRunnerOptions.Parse(args);
if (options.ShowHelp)
{
    AuditRunnerOptions.PrintHelp();
    return options.ParseError is null ? 0 : 2;
}

if (options.ParseError is not null)
{
    Console.Error.WriteLine(options.ParseError);
    AuditRunnerOptions.PrintHelp();
    return 2;
}

try
{
    var artifacts = AuditEngine.Generate(new AuditOptions(
        options.RepoRoot,
        options.SourceCommitSha,
        options.ReportOnly));

    AuditArtifactWriter.Write(options.RepoRoot, artifacts);

    Console.WriteLine(
        $"Operator audit: canonical={artifacts.Summary.CanonicalOperatorCount}, " +
        $"aliases={artifacts.Summary.LegacyAliasCount}, " +
        $"static-confirmed={artifacts.Summary.ConfirmedCount}, " +
        $"accepted-intentional-differences={artifacts.Summary.Review.IntentionalDifferenceCount}, " +
        $"open-production-defects={artifacts.Summary.Review.OpenProductionDefectCount}, " +
        $"new-confirmed={artifacts.Summary.NewConfirmedCount}, " +
        $"new-confirmed-gate={(artifacts.Summary.NewConfirmedCount == 0 ? "pass" : "fail")}, " +
        $"candidate={artifacts.Summary.CandidateCount}, " +
        $"not-reproduced={artifacts.Summary.NotReproducedCount}");

    if (artifacts.Summary.NewConfirmedCount > 0)
    {
        return 1;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Operator audit failed: {ex.Message}");
    return 1;
}

internal sealed record AuditRunnerOptions(
    string RepoRoot,
    string? SourceCommitSha,
    bool ReportOnly,
    bool ShowHelp,
    string? ParseError)
{
    public static AuditRunnerOptions Parse(string[] args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        string? sourceCommitSha = null;
        var reportOnly = false;
        var showHelp = false;
        string? parseError = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo-root":
                    repoRoot = ReadValue(args, ref index, "--repo-root", ref parseError) ?? repoRoot;
                    break;
                case "--source-commit-sha":
                    sourceCommitSha = ReadValue(args, ref index, "--source-commit-sha", ref parseError);
                    break;
                case "--report-only":
                    reportOnly = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    parseError = $"Unknown argument: {args[index]}";
                    break;
            }
        }

        return new AuditRunnerOptions(
            Path.GetFullPath(repoRoot),
            sourceCommitSha,
            reportOnly,
            showHelp,
            parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            ClearVision operator-library read-only audit

            Options:
              --repo-root <path>          Repository root. Defaults to the current directory.
              --source-commit-sha <sha>   Stable source commit identity. Defaults to HEAD.
              --report-only               Findings do not fail the command.
            """);
    }

    private static string? ReadValue(
        string[] args,
        ref int index,
        string option,
        ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"{option} requires a value.";
            return null;
        }

        index++;
        return args[index];
    }
}
