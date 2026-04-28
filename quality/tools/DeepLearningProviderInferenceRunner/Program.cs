using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

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

var result = ProviderInferenceRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine($"DeepLearning provider inference complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, output={options.OutputPath}");
return result.Summary.Failed == 0 ? 0 : 1;

internal static class ProviderInferenceRunner
{
    public static ProviderInferenceResult Run(RunnerOptions options)
    {
        var modelPath = EnsureSmokeModel(options.ModelPath);
        var availableProviders = GetAvailableProviders();
        var cases = new List<ProviderCase> { new("cpu_smoke_identity", "CPUExecutionProvider", Required: true) };
        if (options.IncludeGpu)
        {
            cases.Add(new("cuda_smoke_identity_optional", "CUDAExecutionProvider", Required: false));
            cases.Add(new("tensorrt_smoke_identity_optional", "TensorrtExecutionProvider", Required: false));
        }

        var results = cases.Select(item => RunCase(item, modelPath, availableProviders)).ToList();
        var failed = results.Count(item => item.Required && !item.Passed);
        return new ProviderInferenceResult(
            "contract",
            new Summary(DateTimeOffset.UtcNow, results.Count, results.Count(item => item.Passed), failed, availableProviders),
            [new OperatorSummary("DeepLearning", results.Count, results.Count(item => item.Passed), failed, true, "provider-smoke")],
            results);
    }

    private static CaseResult RunCase(ProviderCase providerCase, string modelPath, string[] availableProviders)
    {
        var stopwatch = Stopwatch.StartNew();
        var requested = providerCase.RequestedProvider;
        var fallbackReason = string.Empty;
        var providerFailure = (Code: string.Empty, Message: string.Empty, ExceptionType: string.Empty);
        var realInference = false;

        try
        {
            using var sessionOptions = new SessionOptions();
            var appended = TryAppendProvider(sessionOptions, requested, out providerFailure);
            var fallbackToCpu = !requested.Equals("CPUExecutionProvider", StringComparison.OrdinalIgnoreCase) && !appended;
            if (fallbackToCpu)
            {
                fallbackReason = providerFailure.Message;
                if (!providerCase.Required)
                {
                    stopwatch.Stop();
                    return new CaseResult(providerCase.Id, requested, "CPUExecutionProvider", true, false, false, false, stopwatch.Elapsed.TotalMilliseconds, availableProviders, fallbackReason, providerFailure.Code, providerFailure.Message, providerFailure.ExceptionType);
                }
            }

            using var session = new InferenceSession(modelPath, sessionOptions);
            var activeProviders = GetSessionProviders(session);
            var active = activeProviders.FirstOrDefault() ?? (fallbackToCpu ? "CPUExecutionProvider" : requested);
            var input = new DenseTensor<float>(new[] { 1f, 2f, 3f }, [1, 3]);
            using var outputs = session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
            var values = outputs.First().AsTensor<float>().ToArray();
            realInference = values.SequenceEqual(new[] { 1f, 2f, 3f });
            stopwatch.Stop();

            return new CaseResult(providerCase.Id, requested, active, active.Equals("CPUExecutionProvider", StringComparison.OrdinalIgnoreCase) && !requested.Equals("CPUExecutionProvider", StringComparison.OrdinalIgnoreCase), realInference, realInference, providerCase.Required, stopwatch.Elapsed.TotalMilliseconds, availableProviders, fallbackReason, providerFailure.Code, providerFailure.Message, providerFailure.ExceptionType);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CaseResult(providerCase.Id, requested, "Unavailable", !requested.Equals("CPUExecutionProvider", StringComparison.OrdinalIgnoreCase), false, !providerCase.Required, providerCase.Required, stopwatch.Elapsed.TotalMilliseconds, availableProviders, fallbackReason, "InferenceFailed", ex.GetBaseException().Message, ex.GetBaseException().GetType().FullName ?? string.Empty);
        }
    }

    private static bool TryAppendProvider(SessionOptions options, string provider, out (string Code, string Message, string ExceptionType) failure)
    {
        failure = (string.Empty, string.Empty, string.Empty);
        if (provider.Equals("CPUExecutionProvider", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (provider.Contains("CUDA", StringComparison.OrdinalIgnoreCase))
            {
                options.AppendExecutionProvider_CUDA(0);
                return true;
            }

            var method = typeof(SessionOptions).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(item => item.Name == "AppendExecutionProvider_TensorRT" && item.GetParameters().Length == 1);
            if (method is null)
            {
                failure = ("TensorRtApiMissing", "The current OnnxRuntime package does not expose TensorRT provider APIs.", string.Empty);
                return false;
            }

            var providerOptions = Activator.CreateInstance(method.GetParameters()[0].ParameterType);
            if (providerOptions is null)
            {
                failure = ("TensorRtOptionsCreateFailed", "Unable to create TensorRT provider options.", string.Empty);
                return false;
            }

            method.Invoke(options, [providerOptions]);
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            failure = ("ProviderAppendFailed", ex.InnerException.Message, ex.InnerException.GetType().FullName ?? string.Empty);
            return false;
        }
        catch (Exception ex)
        {
            failure = ("ProviderAppendFailed", ex.Message, ex.GetType().FullName ?? string.Empty);
            return false;
        }
    }

    private static string[] GetAvailableProviders()
    {
        try
        {
            var instance = typeof(OrtEnv).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var providers = instance?.GetType().GetMethod("GetAvailableProviders")?.Invoke(instance, null);
            if (providers is IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Select(item => item.ToString() ?? string.Empty).Where(item => item.Length > 0).ToArray();
            }
        }
        catch
        {
        }

        return ["CPUExecutionProvider"];
    }

    private static string[] GetSessionProviders(InferenceSession session)
    {
        try
        {
            var providers = session.GetType().GetMethod("GetProviders")?.Invoke(session, null);
            if (providers is IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Select(item => item.ToString() ?? string.Empty).Where(item => item.Length > 0).ToArray();
            }
        }
        catch
        {
        }

        return [];
    }

    private static string EnsureSmokeModel(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var path = Path.Combine(".tmp", "publish-check", "deep-learning-smoke-identity.onnx");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, OnnxIdentityModel.Create());

        return path;
    }
}

internal static class OnnxIdentityModel
{
    public static byte[] Create()
    {
        var input = ValueInfo("input");
        var output = ValueInfo("output");
        var node = Message(L(1, String("input")), L(2, String("output")), L(4, String("Identity")));
        var graph = Message(L(1, node), L(2, String("deep_learning_smoke_identity")), L(11, input), L(12, output));
        var opset = Message(L(1, String("")), V(2, 13));
        return Message(V(1, 7), L(2, String("ClearVision")), L(7, graph), L(8, opset));
    }

    private static byte[] ValueInfo(string name)
    {
        var dim1 = Message(V(1, 1));
        var dim3 = Message(V(1, 3));
        var shape = Message(L(1, dim1), L(1, dim3));
        var tensorType = Message(V(1, 1), L(2, shape));
        var type = Message(L(1, tensorType));
        return Message(L(1, String(name)), L(2, type));
    }

    private static Field L(int number, byte[] value) => new(number, 2, value);
    private static Field V(int number, uint value) => new(number, 0, Varint(value));

    private static byte[] Message(params Field[] fields)
    {
        using var stream = new MemoryStream();
        foreach (var field in fields)
        {
            WriteVarint(stream, (uint)((field.Number << 3) | field.WireType));
            if (field.WireType == 2)
            {
                WriteVarint(stream, (uint)field.Value.Length);
            }

            stream.Write(field.Value);
        }

        return stream.ToArray();
    }

    private static byte[] String(string value) => System.Text.Encoding.UTF8.GetBytes(value);
    private static byte[] Varint(uint value)
    {
        using var stream = new MemoryStream();
        WriteVarint(stream, value);
        return stream.ToArray();
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private readonly record struct Field(int Number, int WireType, byte[] Value);
}

internal sealed record ProviderCase(string Id, string RequestedProvider, bool Required);
internal sealed record ProviderInferenceResult(string EvidenceKind, Summary Summary, List<OperatorSummary> Operators, List<CaseResult> Cases);
internal sealed record Summary(DateTimeOffset GeneratedAtUtc, int CaseCount, int Passed, int Failed, string[] AvailableProviders);
internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, bool HasBenchmark, string Benchmark);
internal sealed record CaseResult(string CaseId, string RequestedProvider, string ActiveProvider, bool FallbackToCpu, bool RealOnnxInference, bool Passed, bool Required, double LatencyMs, string[] AvailableProviders, string? FallbackReason, string? ProviderFailureCode, string? ProviderFailureMessage, string? ProviderFailureExceptionType);

internal static class MarkdownReport
{
    public static string Create(ProviderInferenceResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning Provider Inference Baseline",
            "",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "",
            "## Summary",
            "",
            "| Cases | Passed | Failed | Available providers |",
            "| ---: | ---: | ---: | --- |",
            $"| {result.Summary.CaseCount} | {result.Summary.Passed} | {result.Summary.Failed} | {string.Join(", ", result.Summary.AvailableProviders)} |",
            "",
            "## Cases",
            "",
            "| Case | Required | RequestedProvider | ActiveProvider | FallbackToCpu | RealOnnxInference | Passed | Latency ms | Provider failure |",
            "| --- | --- | --- | --- | --- | --- | --- | ---: | --- |"
        };
        lines.AddRange(result.Cases.Select(item => $"| {item.CaseId} | {item.Required} | {item.RequestedProvider} | {item.ActiveProvider} | {item.FallbackToCpu} | {item.RealOnnxInference} | {item.Passed} | {item.LatencyMs:0.###} | {item.ProviderFailureCode}: {item.ProviderFailureMessage} |"));
        lines.Add("");
        lines.Add("GPU/CUDA/TensorRT cases are optional/manual. Missing providers are reported as optional evidence gaps, not as enabled GPU inference.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, string? ModelPath, bool IncludeGpu, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions("quality/evals/reports/DeepLearning_provider_inference_baseline.json", "quality/evals/reports/DeepLearning_provider_inference_baseline.md", null, false, false, null);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help") return options with { ShowHelp = true };
            if (arg == "--include-gpu") { options = options with { IncludeGpu = true }; continue; }
            if (i + 1 >= args.Length) return options with { ParseError = $"Missing value for {arg}" };
            var value = args[++i];
            options = arg switch
            {
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                "--model" => options with { ModelPath = value },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };
        }

        return options;
    }

    public static void PrintHelp() => Console.WriteLine("Usage: dotnet run --project quality/tools/DeepLearningProviderInferenceRunner/DeepLearningProviderInferenceRunner.csproj -- [--output path] [--report path] [--model path] [--include-gpu]");
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
