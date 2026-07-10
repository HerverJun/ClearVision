using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ClearVision.OperatorLibrary.ReadOnlyAudit;

public static class AuditCandidateProbe
{
    public static IReadOnlyList<AuditFinding> AnalyzeOutputFindings(
        string source,
        string operatorName,
        IReadOnlyList<string> declaredOutputs)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11));
        var compilation = CSharpCompilation.Create(
            "AuditCandidateProbe",
            [tree],
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var declaration = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(item => item.Identifier.Text.EndsWith("Operator", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Probe source must contain an operator class.");
        var observation = new SourceOperatorObservation
        {
            Operator = operatorName,
            EvidencePath = "synthetic/operator.cs",
            ClassLine = 1
        };
        RoslynOperatorSourceAnalyzer.AnalyzeSuccessOutputPaths(declaration, model, observation);

        var declared = declaredOutputs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var findings = observation.SuccessOutputKeys
            .Where(key => !declared.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => new AuditFinding(
                "RUNTIME_OUTPUT_UNDOCUMENTED",
                "warning",
                "low",
                "candidate",
                operatorName,
                key,
                observation.Evidence,
                "A successful output dictionary key has no matching output declaration.",
                "Review the successful return path before updating the formal contract."))
            .ToList();
        if (observation.HasDynamicSuccessOutputDictionary)
        {
            findings.Add(new AuditFinding(
                "RUNTIME_OUTPUT_DYNAMIC_UNPROVEN",
                "info",
                "low",
                "candidate",
                operatorName,
                "dynamic-output",
                observation.Evidence,
                "A successful output path contains a dynamic key that cannot be proven statically.",
                "Review the successful return path without treating unrelated dictionaries as public outputs."));
        }

        return findings
            .GroupBy(AuditBaselineStore.FindingIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trusted))
        {
            return [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];
        }

        return trusted
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
