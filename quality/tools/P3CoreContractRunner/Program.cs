using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

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

var result = await ContractRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"P3 core contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<ContractCase>();
        AddComparatorCases(cases);
        AddLogicGateCases(cases);
        AddStringFormatCases(cases);
        AddArrayIndexerCases(cases);
        AddJsonExtractorCases(cases);
        AddMathOperationCases(cases);
        AddTypeConvertCases(cases);
        AddResultJudgmentCases(cases);
        AddTimerStatisticsCases(cases);
        AddVariableReadCases(cases);
        AddVariableWriteCases(cases);
        AddVariableIncrementCases(cases);
        AddCycleCounterCases(cases);
        AddDelayCases(cases);
        AddForEachCases(cases);
        AddImageAcquisitionCases(cases);
        AddMitsubishiMcCommunicationCases(cases);
        AddOmronFinsCommunicationCases(cases);
        AddSiemensS7CommunicationCases(cases);

        var results = new List<CaseResult>(cases.Count);
        try
        {
            foreach (var contractCase in cases)
            {
                results.Add(await RunCaseAsync(contractCase));
            }
        }
        finally
        {
            PlcCommunicationOperatorBase.StopHeartbeat();
        }

        var operators = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperatorEvidence(
                group.Key,
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

    private static async Task<CaseResult> RunCaseAsync(ContractCase contractCase)
    {
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await contractCase.Body();
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                contractCase.Name,
                contractCase.Operator,
                contractCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                contractCase.Name,
                contractCase.Operator,
                contractCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                ex.Message);
        }
    }

    private static void AddComparatorCases(List<ContractCase> cases)
    {
        var op = new ComparatorOperator(NullLogger<ComparatorOperator>.Instance);
        for (var i = 0; i < 5; i++)
        {
            var value = i + 10.0;
            Add(cases, "Comparator", $"greater_than_{i}", "Comparison truth table", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.Comparator, ("Condition", "GreaterThan")), Inputs(("ValueA", value), ("ValueB", value - 1)));
                RequireSuccess(result);
                RequireBool(result, "Result", true);
            });
            Add(cases, "Comparator", $"less_equal_{i}", "Comparison truth table", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.Comparator, ("Condition", "LessThanOrEqual")), Inputs(("ValueA", value), ("ValueB", value)));
                RequireSuccess(result);
                RequireBool(result, "Result", true);
            });
            Add(cases, "Comparator", $"equal_tolerance_{i}", "Tolerance contract", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.Comparator, ("Condition", "Equal"), ("Tolerance", 0.01)), Inputs(("ValueA", value + 0.005), ("ValueB", value)));
                RequireSuccess(result);
                RequireBool(result, "Result", true);
            });
            Add(cases, "Comparator", $"in_range_{i}", "Range contract", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.Comparator, ("Condition", "InRange"), ("RangeMin", value - 1), ("RangeMax", value + 1)), Inputs(("ValueA", value)));
                RequireSuccess(result);
                RequireBool(result, "Result", true);
            });
        }

        Add(cases, "Comparator", "invalid_value_a_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.Comparator), Inputs(("ValueA", "abc")));
            RequireFailure(result, "ValueA");
        });
        Add(cases, "Comparator", "invalid_value_b_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.Comparator), Inputs(("ValueA", 1), ("ValueB", "abc")));
            RequireFailure(result, "ValueB");
        });
    }

    private static void AddLogicGateCases(List<ContractCase> cases)
    {
        var op = new LogicGateOperator(NullLogger<LogicGateOperator>.Instance);
        var rows = new (string Operation, bool A, bool B, bool Expected)[]
        {
            ("AND", true, true, true),
            ("AND", true, false, false),
            ("OR", false, true, true),
            ("OR", false, false, false),
            ("XOR", true, false, true),
            ("XOR", true, true, false),
            ("NAND", true, true, false),
            ("NAND", false, true, true),
            ("NOR", false, false, true),
            ("NOR", true, false, false)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            Add(cases, "LogicGate", $"{row.Operation.ToLowerInvariant()}_{i}", "Boolean truth table", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.LogicGate, ("Operation", row.Operation)), Inputs(("InputA", row.A), ("InputB", row.B)));
                RequireSuccess(result);
                RequireBool(result, "Result", row.Expected);
            });
        }

        for (var i = 0; i < 8; i++)
        {
            var input = i % 2 == 0 ? "yes" : "0";
            var expected = i % 2 != 0;
            Add(cases, "LogicGate", $"not_convertible_{i}", "Boolean conversion", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.LogicGate, ("Operation", "NOT")), Inputs(("InputA", input)));
                RequireSuccess(result);
                RequireBool(result, "Result", expected);
            });
        }

        Add(cases, "LogicGate", "missing_input_b_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.LogicGate, ("Operation", "AND")), Inputs(("InputA", true)));
            RequireFailure(result, "InputB");
        });
        Add(cases, "LogicGate", "invalid_operation_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.LogicGate, ("Operation", "BAD")));
            Require(!validation.IsValid, "Invalid logic operation should fail validation.");
            return Task.CompletedTask;
        });
    }

    private static void AddStringFormatCases(List<ContractCase> cases)
    {
        var op = new StringFormatOperator(NullLogger<StringFormatOperator>.Instance);
        for (var i = 0; i < 10; i++)
        {
            Add(cases, "StringFormat", $"template_named_{i}", "Template mode", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.StringFormat, ("Mode", "Template"), ("Template", "part-{Name}-{Index}")), Inputs(("Name", "A"), ("Index", i)));
                RequireSuccess(result);
                RequireValue(result, "Result", $"part-A-{i}");
            });
        }

        for (var i = 0; i < 8; i++)
        {
            Add(cases, "StringFormat", $"join_{i}", "Join mode", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.StringFormat, ("Mode", "Join"), ("Separator", "|")), Inputs(("A", "left"), ("B", i)));
                RequireSuccess(result);
                RequireValue(result, "Result", $"left|{i}");
            });
        }

        Add(cases, "StringFormat", "date_mode_outputs_text", "Date mode", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.StringFormat, ("Mode", "Date"), ("DateFormat", "yyyy")), Inputs(("Trigger", true)));
            RequireSuccess(result);
            Require(Convert.ToString(result.OutputData!["Result"])!.Length == 4, "Date mode should output a 4-digit year.");
        });
        Add(cases, "StringFormat", "invalid_mode_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.StringFormat, ("Mode", "BAD")));
            Require(!validation.IsValid, "Invalid StringFormat mode should fail validation.");
            return Task.CompletedTask;
        });
    }

    private static void AddArrayIndexerCases(List<ContractCase> cases)
    {
        var op = new ArrayIndexerOperator(NullLogger<ArrayIndexerOperator>.Instance);
        var values = Enumerable.Range(10, 12).ToArray();
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Add(cases, "ArrayIndexer", $"index_{index}", "Index mode", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.ArrayIndexer, ("Mode", "Index"), ("Index", index)), Inputs(("List", values)));
                RequireSuccess(result);
                RequireValue(result, "Item", values[index]);
                RequireValue(result, "Index", index);
            });
        }

        for (var i = 0; i < 5; i++)
        {
            Add(cases, "ArrayIndexer", $"first_last_{i}", "Boundary modes", async () =>
            {
                var mode = i % 2 == 0 ? "First" : "Last";
                var expected = mode == "First" ? values[0] : values[^1];
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.ArrayIndexer, ("Mode", mode)), Inputs(("List", values)));
                RequireSuccess(result);
                RequireValue(result, "Item", expected);
            });
        }

        Add(cases, "ArrayIndexer", "empty_list_returns_not_found", "Boundary modes", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ArrayIndexer), Inputs(("List", Array.Empty<int>())));
            RequireSuccess(result);
            RequireBool(result, "Found", false);
        });
        Add(cases, "ArrayIndexer", "out_of_range_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ArrayIndexer, ("Index", 99)), Inputs(("List", values)));
            RequireFailure(result, "索引越界");
        });
        Add(cases, "ArrayIndexer", "non_enumerable_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ArrayIndexer), Inputs(("List", 123)));
            RequireFailure(result, "可枚举");
        });
    }

    private static void AddJsonExtractorCases(List<ContractCase> cases)
    {
        var op = new JsonExtractorOperator(NullLogger<JsonExtractorOperator>.Instance);
        for (var i = 0; i < 8; i++)
        {
            Add(cases, "JsonExtractor", $"property_string_{i}", "Path extraction", async () =>
            {
                var json = $$"""{"name":"item-{{i}}","value":{{i}}}""";
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.JsonExtractor, ("JsonPath", "$.name"), ("OutputType", "String")), Inputs(("Json", json)));
                RequireSuccess(result);
                RequireValue(result, "Value", $"item-{i}");
                RequireBool(result, "IsSuccess", true);
            });
        }

        for (var i = 0; i < 8; i++)
        {
            Add(cases, "JsonExtractor", $"array_integer_{i}", "Array extraction", async () =>
            {
                var json = """{"items":[{"id":1},{"id":2},{"id":3}]}""";
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.JsonExtractor, ("JsonPath", $"$.items[{i % 3}].id"), ("OutputType", "Integer")), Inputs(("Json", json)));
                RequireSuccess(result);
                RequireValue(result, "Value", (i % 3) + 1);
            });
        }

        Add(cases, "JsonExtractor", "missing_optional_uses_default", "Default contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.JsonExtractor, ("JsonPath", "$.missing"), ("OutputType", "String"), ("DefaultValue", "fallback")), Inputs(("Json", """{"ok":true}""")));
            RequireSuccess(result);
            RequireBool(result, "IsSuccess", false);
            RequireValue(result, "Value", "fallback");
        });
        Add(cases, "JsonExtractor", "required_missing_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.JsonExtractor, ("JsonPath", "$.missing"), ("Required", true)), Inputs(("Json", """{"ok":true}""")));
            RequireFailure(result, "未找到路径");
        });
        Add(cases, "JsonExtractor", "invalid_json_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.JsonExtractor), Inputs(("Json", """{"ok":true,]""")));
            RequireFailure(result, "JSON");
        });
        Add(cases, "JsonExtractor", "invalid_output_type_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.JsonExtractor, ("OutputType", "BadType")));
            Require(!validation.IsValid, "Invalid JsonExtractor OutputType should fail validation.");
            return Task.CompletedTask;
        });
    }

    private static void AddMathOperationCases(List<ContractCase> cases)
    {
        var op = new MathOperationOperator(NullLogger<MathOperationOperator>.Instance);
        var binary = new (string Operation, double A, double B, double Expected)[]
        {
            ("Add", 10, 5, 15),
            ("Subtract", 10, 5, 5),
            ("Multiply", 10, 5, 50),
            ("Divide", 10, 5, 2),
            ("Min", 10, 5, 5),
            ("Max", 10, 5, 10),
            ("Power", 2, 3, 8),
            ("Modulo", 17, 5, 2)
        };
        foreach (var row in binary)
        {
            Add(cases, "MathOperation", row.Operation.ToLowerInvariant(), "Math operations", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.MathOperation, ("Operation", row.Operation)), Inputs(("ValueA", row.A), ("ValueB", row.B)));
                RequireSuccess(result);
                RequireDouble(result, "Result", row.Expected, 1e-9);
            });
        }

        var unary = new (string Operation, double A, double Expected)[]
        {
            ("Abs", -10, 10),
            ("Sqrt", 9, 3),
            ("Round", 3.7, 4)
        };
        foreach (var row in unary)
        {
            Add(cases, "MathOperation", row.Operation.ToLowerInvariant(), "Math operations", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.MathOperation, ("Operation", row.Operation)), Inputs(("ValueA", row.A)));
                RequireSuccess(result);
                RequireDouble(result, "Result", row.Expected, 1e-9);
            });
        }

        for (var i = 0; i < 5; i++)
        {
            Add(cases, "MathOperation", $"positive_flag_{i}", "Output contract", async () =>
            {
                var value = i - 2;
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.MathOperation, ("Operation", "Add")), Inputs(("ValueA", value), ("ValueB", 0)));
                RequireSuccess(result);
                RequireBool(result, "IsPositive", value > 0);
            });
        }

        Add(cases, "MathOperation", "divide_by_zero_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.MathOperation, ("Operation", "Divide")), Inputs(("ValueA", 1), ("ValueB", 0)));
            RequireFailure(result, "Divisor");
        });
        Add(cases, "MathOperation", "missing_value_b_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.MathOperation, ("Operation", "Multiply")), Inputs(("ValueA", 1)));
            RequireFailure(result, "ValueB");
        });
        Add(cases, "MathOperation", "non_finite_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.MathOperation, ("Operation", "Add")), Inputs(("ValueA", double.NaN), ("ValueB", 1)));
            RequireFailure(result, "finite");
        });
        Add(cases, "MathOperation", "invalid_operation_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.MathOperation, ("Operation", "BAD")));
            Require(!validation.IsValid, "Invalid MathOperation operation should fail validation.");
            return Task.CompletedTask;
        });
    }

    private static void AddTypeConvertCases(List<ContractCase> cases)
    {
        var op = new TypeConvertOperator(NullLogger<TypeConvertOperator>.Instance);
        var rows = new (object Input, string Target, object Expected)[]
        {
            ("42", "Integer", 42),
            ("3.5", "Float", 3.5f),
            (true, "String", "True"),
            ("false", "Boolean", false),
            (1, "Boolean", true),
            (0, "Boolean", false),
            (12.9, "Integer", 12),
            (5, "Float", 5f)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            Add(cases, "TypeConvert", $"target_{row.Target.ToLowerInvariant()}_{i}", "Target conversions", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.TypeConvert, ("TargetType", row.Target)), Inputs(("Input", row.Input)));
                RequireSuccess(result);
                RequireValue(result, "Output", row.Expected);
            });
        }

        for (var i = 0; i < 10; i++)
        {
            Add(cases, "TypeConvert", $"all_outputs_{i}", "Output contract", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.TypeConvert, ("Format", "F2")), Inputs(("Input", 3.14159 + i)));
                RequireSuccess(result);
                Require(Convert.ToString(result.OutputData!["AsString"])!.Contains('.'), "AsString should honor numeric format.");
                Require(result.OutputData.ContainsKey("AsFloat"), "AsFloat output missing.");
                Require(result.OutputData.ContainsKey("AsInteger"), "AsInteger output missing.");
                Require(result.OutputData.ContainsKey("AsBoolean"), "AsBoolean output missing.");
            });
        }

        Add(cases, "TypeConvert", "missing_input_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.TypeConvert), new Dictionary<string, object>());
            RequireFailure(result, "Input");
        });
        Add(cases, "TypeConvert", "invalid_target_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.TypeConvert, ("TargetType", "BadType")));
            Require(!validation.IsValid, "Invalid TypeConvert target should fail validation.");
            return Task.CompletedTask;
        });
    }

    private static void AddResultJudgmentCases(List<ContractCase> cases)
    {
        var op = new ResultJudgmentOperator(NullLogger<ResultJudgmentOperator>.Instance);
        var rows = new (string Condition, object Value, string Expect, string Min, string Max, bool Expected)[]
        {
            ("Equal", 5, "5", "", "", true),
            ("NotEqual", 5, "6", "", "", true),
            ("GreaterThan", 7, "6", "", "", true),
            ("LessThan", 5, "6", "", "", true),
            ("GreaterOrEqual", 6, "6", "", "", true),
            ("LessOrEqual", 6, "6", "", "", true),
            ("Range", 5, "", "4", "6", true),
            ("Equal", "NG", "OK", "", "", false),
            ("Range", 7, "", "4", "6", false),
            ("GreaterThan", 3, "6", "", "", false)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            Add(cases, "ResultJudgment", $"condition_{i}", "Condition contract", async () =>
            {
                var result = await op.ExecuteAsync(
                    CreateOperator(
                        OperatorType.ResultJudgment,
                        ("Condition", row.Condition),
                        ("ExpectValue", row.Expect),
                        ("ExpectValueMin", row.Min),
                        ("ExpectValueMax", row.Max)),
                    Inputs(("Value", row.Value)));
                RequireSuccess(result);
                RequireBool(result, "IsOk", row.Expected);
                RequireValue(result, "JudgmentResult", row.Expected ? "OK" : "NG");
            });
        }

        for (var i = 0; i < 8; i++)
        {
            Add(cases, "ResultJudgment", $"confidence_gate_{i}", "Confidence gate", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.ResultJudgment, ("MinConfidence", 0.9), ("ExpectValue", "1")), Inputs(("Value", 1), ("Confidence", 0.5)));
                RequireSuccess(result);
                RequireBool(result, "IsOk", false);
                RequireValue(result, "Condition", "MinConfidenceGate");
            });
        }

        Add(cases, "ResultJudgment", "invalid_min_confidence_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.ResultJudgment, ("MinConfidence", 1.5)));
            Require(!validation.IsValid, "Invalid MinConfidence should fail validation.");
            return Task.CompletedTask;
        });
        Add(cases, "ResultJudgment", "invalid_rel_tolerance_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.ResultJudgment, ("NumericRelTolerance", 2.0)));
            Require(!validation.IsValid, "Invalid NumericRelTolerance should fail validation.");
            return Task.CompletedTask;
        });
        Add(cases, "ResultJudgment", "invalid_condition_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.ResultJudgment, ("Condition", "Bad")));
            RequireValidationInvalid(validation, "Unsupported condition");
            return Task.CompletedTask;
        });
        Add(cases, "ResultJudgment", "invalid_condition_execution_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ResultJudgment, ("Condition", "Bad")), Inputs(("Value", 1)));
            RequireFailure(result, "Unsupported condition");
        });
    }

    private static void AddTimerStatisticsCases(List<ContractCase> cases)
    {
        var op = new TimerStatisticsOperator(NullLogger<TimerStatisticsOperator>.Instance);
        for (var i = 0; i < 10; i++)
        {
            Add(cases, "TimerStatistics", $"single_shot_{i}", "SingleShot timing", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.TimerStatistics, ("Mode", "SingleShot")), Inputs(("Trigger", i)));
                RequireSuccess(result);
                RequireValue(result, "Count", 1);
                Require(result.OutputData!.ContainsKey("ElapsedMs"), "ElapsedMs output missing.");
            });
        }

        for (var i = 0; i < 8; i++)
        {
            Add(cases, "TimerStatistics", $"cumulative_{i}", "Cumulative timing", async () =>
            {
                var timerOp = CreateOperator(OperatorType.TimerStatistics, ("Mode", "Cumulative"));
                var first = await op.ExecuteAsync(timerOp, Inputs(("Trigger", i)));
                var second = await op.ExecuteAsync(timerOp, Inputs(("Trigger", i + 1)));
                RequireSuccess(first);
                RequireSuccess(second);
                RequireValue(second, "Count", 2);
            });
        }

        Add(cases, "TimerStatistics", "invalid_mode_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.TimerStatistics, ("Mode", "BAD")));
            Require(!validation.IsValid, "Invalid TimerStatistics mode should fail validation.");
            return Task.CompletedTask;
        });
        Add(cases, "TimerStatistics", "negative_reset_validation", "Validation contract", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(OperatorType.TimerStatistics, ("ResetInterval", -1)));
            Require(!validation.IsValid, "Negative ResetInterval should fail validation.");
            return Task.CompletedTask;
        });
        Add(cases, "TimerStatistics", "invalid_mode_execution_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.TimerStatistics, ("Mode", "BAD")), Inputs(("Trigger", true)));
            RequireFailure(result, "Mode");
        });
        Add(cases, "TimerStatistics", "negative_reset_execution_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.TimerStatistics, ("ResetInterval", -1)), Inputs(("Trigger", true)));
            RequireFailure(result, "ResetInterval");
        });
    }

    private static void AddVariableReadCases(List<ContractCase> cases)
    {
        var context = new VariableContext();
        context.SetValue("station", "A01");
        context.SetValue("count", 42L);
        context.SetValue("temperature", 36.5);
        context.SetValue("enabled", true);
        context.IncrementCycleCount();
        context.IncrementCycleCount();
        var op = new VariableReadOperator(NullLogger<VariableReadOperator>.Instance, context);

        var reads = new (string Name, string DataType, object Expected)[]
        {
            ("station", "String", "A01"),
            ("count", "Int", 42L),
            ("temperature", "Double", 36.5),
            ("enabled", "Bool", true)
        };

        for (var round = 0; round < 2; round++)
        {
            var roundIndex = round;
            foreach (var row in reads)
            {
                Add(cases, "VariableRead", $"existing_{row.DataType.ToLowerInvariant()}_{roundIndex}", "Variable context read", async () =>
                {
                    var result = await op.ExecuteAsync(CreateOperator(OperatorType.VariableRead, ("VariableName", row.Name), ("DataType", row.DataType)));
                    RequireSuccess(result);
                    RequireValue(result, "Value", row.Expected);
                    RequireBool(result, "Exists", true);
                    RequireValue(result, "CycleCount", 2L);
                });
            }
        }

        var defaults = new (string DataType, string DefaultValue, object Expected)[]
        {
            ("String", "fallback", "fallback"),
            ("Int", "17", 17L),
            ("Double", "2.5", 2.5),
            ("Bool", "true", true)
        };

        for (var round = 0; round < 2; round++)
        {
            var roundIndex = round;
            foreach (var row in defaults)
            {
                Add(cases, "VariableRead", $"default_{row.DataType.ToLowerInvariant()}_{roundIndex}", "Default contract", async () =>
                {
                    var result = await op.ExecuteAsync(CreateOperator(
                        OperatorType.VariableRead,
                        ("VariableName", $"missing_{row.DataType}_{roundIndex}"),
                        ("DataType", row.DataType),
                        ("DefaultValue", row.DefaultValue)));
                    RequireSuccess(result);
                    RequireValue(result, "Value", row.Expected);
                    RequireBool(result, "Exists", false);
                });
            }
        }

        Add(cases, "VariableRead", "empty_variable_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.VariableRead, ("VariableName", ""))));
            return Task.CompletedTask;
        });
        Add(cases, "VariableRead", "invalid_type_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.VariableRead, ("VariableName", "station"), ("DataType", "Decimal"))));
            return Task.CompletedTask;
        });
        Add(cases, "VariableRead", "empty_variable_execution_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.VariableRead, ("VariableName", "")));
            RequireFailure(result);
        });
        Add(cases, "VariableRead", "invalid_type_execution_defaults_to_string", "Default contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.VariableRead, ("VariableName", "station"), ("DataType", "Decimal")));
            RequireSuccess(result);
            RequireValue(result, "Value", "A01");
        });
    }

    private static void AddVariableWriteCases(List<ContractCase> cases)
    {
        var inputRows = new (string DataType, object InputValue, object Expected)[]
        {
            ("String", "LAB-001", "LAB-001"),
            ("Int", 12, 12L),
            ("Double", 4.25, 4.25),
            ("Bool", true, true)
        };

        for (var round = 0; round < 2; round++)
        {
            var roundIndex = round;
            foreach (var row in inputRows)
            {
                Add(cases, "VariableWrite", $"input_{row.DataType.ToLowerInvariant()}_{roundIndex}", "Variable context write", async () =>
                {
                    var context = new VariableContext();
                    var op = new VariableWriteOperator(NullLogger<VariableWriteOperator>.Instance, context);
                    var name = $"write_{row.DataType}_{roundIndex}";
                    var result = await op.ExecuteAsync(
                        CreateOperator(OperatorType.VariableWrite, ("VariableName", name), ("DataType", row.DataType)),
                        Inputs(("Value", row.InputValue)));
                    RequireSuccess(result);
                    RequireValue(result, "VariableName", name);
                    Require(Equals(context.GetValue<object>(name), row.Expected) || Equals(Convert.ToString(context.GetValue<object>(name)), Convert.ToString(row.Expected)),
                        $"Expected context value {row.Expected} for {name}.");
                });
            }
        }

        var staticRows = new (string DataType, string StaticValue, object Expected)[]
        {
            ("String", "STATIC", "STATIC"),
            ("Int", "24", 24L),
            ("Double", "8.5", 8.5),
            ("Bool", "true", true)
        };

        for (var round = 0; round < 2; round++)
        {
            var roundIndex = round;
            foreach (var row in staticRows)
            {
                Add(cases, "VariableWrite", $"static_{row.DataType.ToLowerInvariant()}_{roundIndex}", "Static value fallback", async () =>
                {
                    var context = new VariableContext();
                    var op = new VariableWriteOperator(NullLogger<VariableWriteOperator>.Instance, context);
                    var name = $"static_{row.DataType}_{roundIndex}";
                    var result = await op.ExecuteAsync(CreateOperator(
                        OperatorType.VariableWrite,
                        ("VariableName", name),
                        ("DataType", row.DataType),
                        ("UseInputValue", false),
                        ("StaticValue", row.StaticValue)));
                    RequireSuccess(result);
                    Require(Equals(context.GetValue<object>(name), row.Expected) || Equals(Convert.ToString(context.GetValue<object>(name)), Convert.ToString(row.Expected)),
                        $"Expected static context value {row.Expected} for {name}.");
                });
            }
        }

        Add(cases, "VariableWrite", "named_input_fallback", "Input priority", async () =>
        {
            var context = new VariableContext();
            var op = new VariableWriteOperator(NullLogger<VariableWriteOperator>.Instance, context);
            var result = await op.ExecuteAsync(
                CreateOperator(OperatorType.VariableWrite, ("VariableName", "BatchId"), ("DataType", "String")),
                Inputs(("BatchId", "B-002")));
            RequireSuccess(result);
            RequireValue(result, "Value", "B-002");
            RequireValueObject(context.GetValue<string>("BatchId"), "B-002", "BatchId context value");
        });
        Add(cases, "VariableWrite", "static_fallback_when_input_missing", "Input priority", async () =>
        {
            var context = new VariableContext();
            var op = new VariableWriteOperator(NullLogger<VariableWriteOperator>.Instance, context);
            var result = await op.ExecuteAsync(
                CreateOperator(OperatorType.VariableWrite, ("VariableName", "Fallback"), ("DataType", "String"), ("StaticValue", "S-1")),
                Inputs(("Other", "ignored")));
            RequireSuccess(result);
            RequireValueObject(context.GetValue<string>("Fallback"), "S-1", "Fallback context value");
        });
        Add(cases, "VariableWrite", "empty_variable_validation", "Validation contract", () =>
        {
            var op = new VariableWriteOperator(NullLogger<VariableWriteOperator>.Instance, new VariableContext());
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.VariableWrite, ("VariableName", ""))));
            return Task.CompletedTask;
        });
        Add(cases, "VariableWrite", "invalid_type_validation", "Validation contract", () =>
        {
            var op = new VariableWriteOperator(NullLogger<VariableWriteOperator>.Instance, new VariableContext());
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.VariableWrite, ("VariableName", "x"), ("DataType", "Decimal"))));
            return Task.CompletedTask;
        });
    }

    private static void AddVariableIncrementCases(List<ContractCase> cases)
    {
        for (var i = 0; i < 8; i++)
        {
            var seed = i;
            var delta = i % 2 == 0 ? 2 : -1;
            Add(cases, "VariableIncrement", $"delta_{i}", "Increment contract", async () =>
            {
                var context = new VariableContext();
                context.SetValue("counter", (long)seed);
                var op = new VariableIncrementOperator(NullLogger<VariableIncrementOperator>.Instance, context);
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.VariableIncrement, ("VariableName", "counter"), ("Delta", delta)));
                RequireSuccess(result);
                RequireValue(result, "PreviousValue", (long)seed);
                RequireValue(result, "NewValue", seed + (long)delta);
                RequireBool(result, "WasReset", false);
            });
        }

        var resetRows = new (long Seed, string Condition, int Threshold, int ResetValue, int Delta, bool WasReset, long Expected)[]
        {
            (10, "GreaterThan", 5, 1, 2, true, 3),
            (1, "LessThan", 5, 7, 1, true, 8),
            (5, "Equal", 5, 0, 3, true, 3),
            (4, "GreaterThan", 5, 1, 2, false, 6),
            (8, "LessThan", 5, 1, -2, false, 6),
            (6, "Equal", 5, 1, 1, false, 7)
        };

        for (var i = 0; i < resetRows.Length; i++)
        {
            var row = resetRows[i];
            Add(cases, "VariableIncrement", $"reset_{i}", "Reset contract", async () =>
            {
                var context = new VariableContext();
                context.SetValue("counter", row.Seed);
                var op = new VariableIncrementOperator(NullLogger<VariableIncrementOperator>.Instance, context);
                var result = await op.ExecuteAsync(CreateOperator(
                    OperatorType.VariableIncrement,
                    ("VariableName", "counter"),
                    ("Delta", row.Delta),
                    ("ResetCondition", row.Condition),
                    ("ResetThreshold", row.Threshold),
                    ("ResetValue", row.ResetValue)));
                RequireSuccess(result);
                RequireBool(result, "WasReset", row.WasReset);
                RequireValue(result, "NewValue", row.Expected);
                RequireValueObject(context.GetValue<long>("counter"), row.Expected, "counter context value");
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "VariableIncrement", $"cycle_count_output_{index}", "Output contract", async () =>
            {
                var context = new VariableContext();
                context.IncrementCycleCount();
                var op = new VariableIncrementOperator(NullLogger<VariableIncrementOperator>.Instance, context);
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.VariableIncrement, ("VariableName", $"c{index}")));
                RequireSuccess(result);
                RequireValue(result, "CycleCount", 1L);
            });
        }

        Add(cases, "VariableIncrement", "empty_variable_validation", "Validation contract", () =>
        {
            var op = new VariableIncrementOperator(NullLogger<VariableIncrementOperator>.Instance, new VariableContext());
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.VariableIncrement, ("VariableName", ""))));
            return Task.CompletedTask;
        });
        Add(cases, "VariableIncrement", "invalid_reset_validation", "Validation contract", () =>
        {
            var op = new VariableIncrementOperator(NullLogger<VariableIncrementOperator>.Instance, new VariableContext());
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.VariableIncrement, ("VariableName", "counter"), ("ResetCondition", "Bad"))));
            return Task.CompletedTask;
        });
    }

    private static void AddCycleCounterCases(List<ContractCase> cases)
    {
        for (var i = 0; i < 8; i++)
        {
            Add(cases, "CycleCounter", $"increment_{i}", "Cycle increment", async () =>
            {
                var context = new VariableContext();
                var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, context);
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.CycleCounter, ("Action", "Increment"), ("MaxCycles", 10)));
                RequireSuccess(result);
                RequireValue(result, "CycleCount", 1L);
                RequireBool(result, "IsLimitReached", false);
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var maxCycles = i + 2;
            Add(cases, "CycleCounter", $"read_progress_{i}", "Cycle read", async () =>
            {
                var context = new VariableContext();
                context.IncrementCycleCount();
                var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, context);
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.CycleCounter, ("Action", "Read"), ("MaxCycles", maxCycles)));
                RequireSuccess(result);
                RequireValue(result, "CycleCount", 1L);
                RequireValue(result, "RemainingCycles", (long)(maxCycles - 1));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "CycleCounter", $"reset_{i}", "Cycle reset", async () =>
            {
                var context = new VariableContext();
                context.IncrementCycleCount();
                context.IncrementCycleCount();
                var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, context);
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.CycleCounter, ("Action", "Reset"), ("MaxCycles", 5)));
                RequireSuccess(result);
                RequireValue(result, "CycleCount", 0L);
                RequireValueObject(context.CycleCount, 0L, "cycle count after reset");
            });
        }

        Add(cases, "CycleCounter", "limit_prevents_increment", "Limit contract", async () =>
        {
            var context = new VariableContext();
            context.IncrementCycleCount();
            context.IncrementCycleCount();
            var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, context);
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.CycleCounter, ("Action", "Increment"), ("MaxCycles", 2)));
            RequireSuccess(result);
            RequireValue(result, "CycleCount", 2L);
            RequireBool(result, "IsLimitReached", true);
            RequireValue(result, "RemainingCycles", 0L);
        });
        Add(cases, "CycleCounter", "invalid_action_validation", "Validation contract", () =>
        {
            var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, new VariableContext());
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.CycleCounter, ("Action", "Skip"))), "Unsupported action");
            return Task.CompletedTask;
        });
        Add(cases, "CycleCounter", "negative_max_validation", "Validation contract", () =>
        {
            var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, new VariableContext());
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.CycleCounter, ("Action", "Read"), ("MaxCycles", -1))), "MaxCycles");
            return Task.CompletedTask;
        });
        Add(cases, "CycleCounter", "negative_max_execution_fails", "Error contract", async () =>
        {
            var op = new CycleCounterOperator(NullLogger<CycleCounterOperator>.Instance, new VariableContext());
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.CycleCounter, ("Action", "Read"), ("MaxCycles", -1)));
            RequireFailure(result, "MaxCycles");
        });
    }

    private static void AddDelayCases(List<ContractCase> cases)
    {
        var op = new DelayOperator(NullLogger<DelayOperator>.Instance);
        for (var i = 0; i < 10; i++)
        {
            var index = i;
            Add(cases, "Delay", $"zero_passthrough_{index}", "Passthrough contract", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.Delay, ("Milliseconds", 0)), Inputs(("Input", $"payload-{index}")));
                RequireSuccess(result);
                RequireValue(result, "Output", $"payload-{index}");
                Require(result.OutputData!.ContainsKey("ElapsedMs"), "ElapsedMs output missing.");
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "Delay", $"zero_without_input_{i}", "Default output contract", async () =>
            {
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.Delay, ("Milliseconds", 0)));
                RequireSuccess(result);
                RequireValue(result, "Output", string.Empty);
            });
        }

        Add(cases, "Delay", "negative_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.Delay, ("Milliseconds", -1))), "greater than or equal");
            return Task.CompletedTask;
        });
        Add(cases, "Delay", "too_large_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(OperatorType.Delay, ("Milliseconds", 60001))), "60000");
            return Task.CompletedTask;
        });
        Add(cases, "Delay", "negative_execution_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.Delay, ("Milliseconds", -1)));
            RequireFailure(result, "greater than or equal");
        });
        Add(cases, "Delay", "too_large_execution_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.Delay, ("Milliseconds", 60001)));
            RequireFailure(result, "60000");
        });
        Add(cases, "Delay", "pre_canceled_token_throws", "Cancellation contract", async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                _ = await op.ExecuteAsync(CreateOperator(OperatorType.Delay, ("Milliseconds", 10)), cancellationToken: cts.Token);
                throw new InvalidOperationException("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        });
        Add(cases, "Delay", "delay_canceled_token_throws", "Cancellation contract", async () =>
        {
            using var cts = new CancellationTokenSource(1);
            try
            {
                _ = await op.ExecuteAsync(CreateOperator(OperatorType.Delay, ("Milliseconds", 100)), cancellationToken: cts.Token);
                throw new InvalidOperationException("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private static void AddForEachCases(List<ContractCase> cases)
    {
        for (var i = 0; i < 6; i++)
        {
            var index = i;
            Add(cases, "ForEach", $"empty_items_{index}", "Empty input contract", async () =>
            {
                var op = CreateForEachOperator();
                object items = index % 2 == 0 ? Array.Empty<int>() : new List<string>();
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.ForEach), Inputs(("Items", items)));
                RequireSuccess(result);
                RequireValue(result, "Count", 0);
                RequireBool(result, "AllPass", true);
            });
        }

        Add(cases, "ForEach", "null_inputs_fail", "Error contract", async () =>
        {
            var result = await CreateForEachOperator().ExecuteAsync(CreateOperator(OperatorType.ForEach), null);
            RequireFailure(result);
        });
        Add(cases, "ForEach", "missing_items_fail", "Error contract", async () =>
        {
            var result = await CreateForEachOperator().ExecuteAsync(CreateOperator(OperatorType.ForEach), new Dictionary<string, object>());
            RequireFailure(result);
        });
        Add(cases, "ForEach", "non_enumerable_items_fail", "Error contract", async () =>
        {
            var result = await CreateForEachOperator().ExecuteAsync(CreateOperator(OperatorType.ForEach), Inputs(("Items", 123)));
            RequireFailure(result);
        });
        Add(cases, "ForEach", "missing_subgraph_fails", "Error contract", async () =>
        {
            var result = await CreateForEachOperator().ExecuteAsync(CreateOperator(OperatorType.ForEach), Inputs(("Items", new[] { 1, 2 })));
            RequireFailure(result);
        });

        Add(cases, "ForEach", "invalid_iomode_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(CreateForEachOperator().ValidateParameters(CreateOperator(OperatorType.ForEach, ("IoMode", "Bad"))));
            return Task.CompletedTask;
        });
        Add(cases, "ForEach", "min_parallelism_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(CreateForEachOperator().ValidateParameters(CreateOperator(OperatorType.ForEach, ("MaxParallelism", 0))));
            return Task.CompletedTask;
        });
        Add(cases, "ForEach", "max_parallelism_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(CreateForEachOperator().ValidateParameters(CreateOperator(OperatorType.ForEach, ("MaxParallelism", 65))));
            return Task.CompletedTask;
        });
        Add(cases, "ForEach", "timeout_validation", "Validation contract", () =>
        {
            RequireValidationInvalid(CreateForEachOperator().ValidateParameters(CreateOperator(OperatorType.ForEach, ("TimeoutMs", 500))));
            return Task.CompletedTask;
        });

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "ForEach", $"parallel_success_{i}", "Subgraph aggregation", async () =>
            {
                var flow = new StubFlowExecutionService();
                var op = CreateForEachOperator(flow);
                op.SubGraph = new OperatorFlow("SubGraph");
                var result = await op.ExecuteAsync(
                    CreateOperator(OperatorType.ForEach, ("IoMode", "Parallel"), ("MaxParallelism", 2), ("TimeoutMs", 1000)),
                    Inputs(("Items", new[] { 1, 2, 3 })));
                RequireSuccess(result);
                RequireValue(result, "Count", 3);
                RequireValue(result, "SuccessCount", 3);
                RequireBool(result, "AllSucceeded", true);
            });
        }

        for (var i = 0; i < 2; i++)
        {
            Add(cases, "ForEach", $"sequential_failfast_{i}", "FailFast contract", async () =>
            {
                var flow = new StubFlowExecutionService(input =>
                {
                    var index = Convert.ToInt32(input!["CurrentIndex"]);
                    return index == 1
                        ? new FlowExecutionResult { IsSuccess = false, ErrorMessage = "planned failure" }
                        : new FlowExecutionResult { IsSuccess = true, OutputData = new Dictionary<string, object> { ["Result"] = true } };
                });
                var op = CreateForEachOperator(flow);
                op.SubGraph = new OperatorFlow("SubGraph");
                var result = await op.ExecuteAsync(
                    CreateOperator(OperatorType.ForEach, ("IoMode", "Sequential"), ("FailFast", true), ("TimeoutMs", 1000)),
                    Inputs(("Items", new[] { 1, 2, 3, 4 })));
                RequireSuccess(result);
                RequireValue(result, "Count", 2);
                RequireValue(result, "FailureCount", 1);
                RequireBool(result, "AllSucceeded", false);
            });
        }
    }

    private static void AddImageAcquisitionCases(List<ContractCase> cases)
    {
        var op = new ImageAcquisitionOperator(NullLogger<ImageAcquisitionOperator>.Instance, new StubCameraManager());
        Add(cases, "ImageAcquisition", "null_inputs_fail", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition), null);
            RequireFailure(result);
        });
        Add(cases, "ImageAcquisition", "empty_inputs_fail", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition), new Dictionary<string, object>());
            RequireFailure(result);
        });
        Add(cases, "ImageAcquisition", "file_mode_without_path_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition, ("SourceType", "File")));
            RequireFailure(result);
        });
        Add(cases, "ImageAcquisition", "missing_file_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition, ("FilePath", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"))));
            RequireFailure(result);
        });

        for (var i = 0; i < 6; i++)
        {
            var width = 8 + i;
            var height = 6 + i;
            Add(cases, "ImageAcquisition", $"png_bytes_{i}", "Image passthrough", async () =>
            {
                var bytes = CreatePngBytes(width, height);
                var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition), Inputs(("Image", bytes)));
                RequireSuccess(result);
                RequireValue(result, "Width", width);
                RequireValue(result, "Height", height);
                RequireValue(result, "Channels", 3);
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var width = 10 + i;
            var height = 9 + i;
            Add(cases, "ImageAcquisition", $"file_path_{i}", "File acquisition", async () =>
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"p3-image-{Guid.NewGuid():N}.png");
                try
                {
                    WritePngFile(tempFile, width, height);
                    var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition, ("SourceType", "Camera"), ("FilePath", tempFile)));
                    RequireSuccess(result);
                    RequireValue(result, "Width", width);
                    RequireValue(result, "Height", height);
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            });
        }

        var cameraManager = new StubCameraManager(CreatePngBytes(12, 7));
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "ImageAcquisition", $"camera_mock_{index}", "Camera mock acquisition", async () =>
            {
                var cameraOp = new ImageAcquisitionOperator(NullLogger<ImageAcquisitionOperator>.Instance, cameraManager);
                var result = await cameraOp.ExecuteAsync(CreateOperator(
                    OperatorType.ImageAcquisition,
                    ("SourceType", "Camera"),
                    ("CameraId", $"cam-{index}"),
                    ("ExposureTime", 1200.0 + index),
                    ("Gain", 1.0 + index)));
                RequireSuccess(result);
                RequireValue(result, "Width", 12);
                RequireValue(result, "Height", 7);
                RequireValue(result, "Source", "camera");
            });
        }

        Add(cases, "ImageAcquisition", "camera_without_id_fails", "Error contract", async () =>
        {
            var result = await op.ExecuteAsync(CreateOperator(OperatorType.ImageAcquisition, ("SourceType", "Camera")));
            RequireFailure(result);
        });
        Add(cases, "ImageAcquisition", "validation_is_stable", "Validation contract", () =>
        {
            RequireValidationValid(op.ValidateParameters(CreateOperator(OperatorType.ImageAcquisition)));
            return Task.CompletedTask;
        });
    }

    private static void AddMitsubishiMcCommunicationCases(List<ContractCase> cases)
    {
        var op = new MitsubishiMcCommunicationOperator(NullLogger<MitsubishiMcCommunicationOperator>.Instance);
        var dataTypes = new[] { "Bit", "Word", "Int16", "DWord", "Int32", "Float", "Double", "Word" };
        for (var i = 0; i < dataTypes.Length; i++)
        {
            var dataType = dataTypes[i];
            Add(cases, "MitsubishiMcCommunication", $"valid_datatype_{i}", "PLC parameter validation", () =>
            {
                RequireValidationValid(op.ValidateParameters(CreateOperator(OperatorType.MitsubishiMcCommunication, McParams(("DataType", dataType)))));
                return Task.CompletedTask;
            });
        }

        AddPlcCommonValidationCases(cases, "MitsubishiMcCommunication", OperatorType.MitsubishiMcCommunication, op, McParams(), hasLength: true);
    }

    private static void AddOmronFinsCommunicationCases(List<ContractCase> cases)
    {
        var op = new OmronFinsCommunicationOperator(NullLogger<OmronFinsCommunicationOperator>.Instance);
        var dataTypes = new[] { "Bit", "Word", "Int16", "DWord", "Int32", "Float", "Double", "Word" };
        for (var i = 0; i < dataTypes.Length; i++)
        {
            var dataType = dataTypes[i];
            Add(cases, "OmronFinsCommunication", $"valid_datatype_{i}", "PLC parameter validation", () =>
            {
                RequireValidationValid(op.ValidateParameters(CreateOperator(OperatorType.OmronFinsCommunication, FinsParams(("DataType", dataType)))));
                return Task.CompletedTask;
            });
        }

        AddPlcCommonValidationCases(cases, "OmronFinsCommunication", OperatorType.OmronFinsCommunication, op, FinsParams(), hasLength: true);
    }

    private static void AddSiemensS7CommunicationCases(List<ContractCase> cases)
    {
        var op = new SiemensS7CommunicationOperator(NullLogger<SiemensS7CommunicationOperator>.Instance);
        var dataTypes = new[] { "Bit", "Byte", "Word", "Int16", "DWord", "Int32", "Float", "Double", "String" };
        for (var i = 0; i < dataTypes.Length; i++)
        {
            var dataType = dataTypes[i];
            Add(cases, "SiemensS7Communication", $"valid_datatype_{i}", "PLC parameter validation", () =>
            {
                RequireValidationValid(op.ValidateParameters(CreateOperator(OperatorType.SiemensS7Communication, S7Params(("DataType", dataType)))));
                return Task.CompletedTask;
            });
        }

        var cpuTypes = new[] { "S7200", "S7200Smart", "S7300", "S7400", "S71200", "S71500" };
        for (var i = 0; i < cpuTypes.Length; i++)
        {
            var cpuType = cpuTypes[i];
            Add(cases, "SiemensS7Communication", $"valid_cpu_{i}", "PLC parameter validation", () =>
            {
                RequireValidationValid(op.ValidateParameters(CreateOperator(OperatorType.SiemensS7Communication, S7Params(("CpuType", cpuType)))));
                return Task.CompletedTask;
            });
        }

        AddPlcCommonValidationCases(cases, "SiemensS7Communication", OperatorType.SiemensS7Communication, op, S7Params(), hasLength: false, desiredTotalCases: 20);
    }

    private static void AddPlcCommonValidationCases(
        List<ContractCase> cases,
        string operatorName,
        OperatorType operatorType,
        OperatorBase op,
        (string Name, object Value)[] validParams,
        bool hasLength,
        int? desiredTotalCases = null)
    {
        Add(cases, operatorName, "empty_address_invalid", "PLC parameter validation", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Address", "")))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "missing_ip_invalid", "PLC parameter validation", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("IpAddress", "")))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "missing_port_invalid", "PLC parameter validation", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Port", 0)))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "high_port_invalid", "PLC parameter validation", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Port", 70000)))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "invalid_polling_mode", "PLC parameter validation", () =>
        {
            var validation = op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("PollingMode", "Spin"))));
            if (operatorType == OperatorType.OmronFinsCommunication)
            {
                RequireValidationValid(validation);
            }
            else
            {
                RequireValidationInvalid(validation);
            }

            return Task.CompletedTask;
        });

        if (desiredTotalCases == 20)
        {
            return;
        }

        Add(cases, operatorName, "valid_write_operation", "PLC parameter validation", () =>
        {
            RequireValidationValid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Operation", "Write"), ("WriteValue", "1")))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "valid_polling_wait", "PLC parameter validation", () =>
        {
            RequireValidationValid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("PollingMode", "WaitForValue")))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "negative_port_invalid", "PLC parameter validation", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Port", -1)))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "global_fallback_missing_invalid", "PLC parameter validation", () =>
        {
            RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("IpAddress", ""), ("Port", 0), ("UseGlobalFallback", true)))));
            return Task.CompletedTask;
        });
        Add(cases, operatorName, "lower_case_polling_valid", "PLC parameter validation", () =>
        {
            RequireValidationValid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("PollingMode", "waitforvalue")))));
            return Task.CompletedTask;
        });

        if (hasLength)
        {
            Add(cases, operatorName, "length_zero_invalid", "PLC parameter validation", () =>
            {
                RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Length", 0)))));
                return Task.CompletedTask;
            });
            Add(cases, operatorName, "length_high_invalid", "PLC parameter validation", () =>
            {
                RequireValidationInvalid(op.ValidateParameters(CreateOperator(operatorType, WithParams(validParams, ("Length", 1000)))));
                return Task.CompletedTask;
            });
        }
    }

    private static (string Name, object Value)[] McParams(params (string Name, object Value)[] overrides)
    {
        return WithParams(
        [
            ("IpAddress", "127.0.0.1"),
            ("Port", 5002),
            ("UseGlobalFallback", false),
            ("Address", "D100"),
            ("Length", 1),
            ("DataType", "Word"),
            ("Operation", "Read"),
            ("PollingMode", "None")
        ], overrides);
    }

    private static (string Name, object Value)[] FinsParams(params (string Name, object Value)[] overrides)
    {
        return WithParams(
        [
            ("IpAddress", "127.0.0.1"),
            ("Port", 9600),
            ("UseGlobalFallback", false),
            ("Address", "DM100"),
            ("Length", 1),
            ("DataType", "Word"),
            ("Operation", "Read"),
            ("PollingMode", "None")
        ], overrides);
    }

    private static (string Name, object Value)[] S7Params(params (string Name, object Value)[] overrides)
    {
        return WithParams(
        [
            ("IpAddress", "127.0.0.1"),
            ("Port", 102),
            ("UseGlobalFallback", false),
            ("CpuType", "S71200"),
            ("Rack", 0),
            ("Slot", 1),
            ("Address", "DB1.DBW100"),
            ("DataType", "Word"),
            ("Operation", "Read"),
            ("PollingMode", "None")
        ], overrides);
    }

    private static (string Name, object Value)[] WithParams(
        (string Name, object Value)[] parameters,
        params (string Name, object Value)[] overrides)
    {
        var merged = parameters.ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in overrides)
        {
            merged[name] = value;
        }

        return merged.Select(item => (item.Key, item.Value)).ToArray();
    }

    private static ForEachOperator CreateForEachOperator(StubFlowExecutionService? flow = null)
    {
        flow ??= new StubFlowExecutionService();
        return new ForEachOperator(NullLogger<ForEachOperator>.Instance, new SingleServiceProvider(flow));
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(10, 80, 160));
        if (!Cv2.ImEncode(".png", mat, out var bytes))
        {
            throw new InvalidOperationException("Failed to encode PNG test image.");
        }

        return bytes;
    }

    private static void WritePngFile(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(30, 100, 200));
        if (!Cv2.ImWrite(path, mat))
        {
            throw new InvalidOperationException($"Failed to write PNG test image: {path}");
        }
    }

    private static void Add(List<ContractCase> cases, string operatorName, string name, string scenario, Func<Task> body)
    {
        cases.Add(new ContractCase($"{operatorName}_{name}", operatorName, scenario, body));
    }

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(Guid.NewGuid(), $"{type}Contract", type, 0, 0);
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
            int => "int",
            long => "int",
            float => "double",
            double => "double",
            _ => "string"
        };
    }

    private static Dictionary<string, object> Inputs(params (string Name, object Value)[] values)
    {
        var inputs = new Dictionary<string, object>();
        foreach (var (name, value) in values)
        {
            inputs[name] = value;
        }

        return inputs;
    }

    private static void RequireSuccess(OperatorExecutionOutput result)
    {
        Require(result.IsSuccess, $"Expected success, got failure: {result.ErrorMessage}");
        Require(result.OutputData is not null, "Expected output data.");
    }

    private static void RequireFailure(OperatorExecutionOutput result, string? messageFragment = null)
    {
        Require(!result.IsSuccess, "Expected failure.");
        if (!string.IsNullOrWhiteSpace(messageFragment))
        {
            RequireContains(result.ErrorMessage, messageFragment);
        }
    }

    private static void RequireBool(OperatorExecutionOutput result, string key, bool expected)
    {
        RequireSuccess(result);
        Require(result.OutputData!.TryGetValue(key, out var raw), $"Missing output key {key}.");
        Require(raw is bool actual && actual == expected, $"Expected {key}={expected}, got {raw ?? "null"}.");
    }

    private static void RequireValue(OperatorExecutionOutput result, string key, object? expected)
    {
        RequireSuccess(result);
        Require(result.OutputData!.TryGetValue(key, out var actual), $"Missing output key {key}.");
        if (expected is float expectedFloat)
        {
            Require(Math.Abs(Convert.ToSingle(actual) - expectedFloat) < 1e-5, $"Expected {key}={expectedFloat}, got {actual}.");
            return;
        }

        Require(Equals(actual, expected), $"Expected {key}={expected ?? "null"}, got {actual ?? "null"}.");
    }

    private static void RequireDouble(OperatorExecutionOutput result, string key, double expected, double tolerance)
    {
        RequireSuccess(result);
        Require(result.OutputData!.TryGetValue(key, out var actual), $"Missing output key {key}.");
        Require(Math.Abs(Convert.ToDouble(actual) - expected) <= tolerance, $"Expected {key}={expected}, got {actual}.");
    }

    private static void RequireValueObject(object? actual, object? expected, string label)
    {
        Require(Equals(actual, expected), $"Expected {label}={expected ?? "null"}, got {actual ?? "null"}.");
    }

    private static void RequireValidationValid(ValidationResult validation)
    {
        Require(validation.IsValid, $"Expected validation success, got: {string.Join("; ", validation.Errors)}");
    }

    private static void RequireValidationInvalid(ValidationResult validation, string? messageFragment = null)
    {
        Require(!validation.IsValid, "Expected validation failure.");
        if (!string.IsNullOrWhiteSpace(messageFragment))
        {
            Require(validation.Errors.Any(error => error.Contains(messageFragment, StringComparison.OrdinalIgnoreCase)),
                $"Expected validation errors to contain '{messageFragment}', got: {string.Join("; ", validation.Errors)}");
        }
    }

    private static void RequireContains(string? actual, string expectedFragment)
    {
        Require(actual?.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase) == true, $"Expected '{actual}' to contain '{expectedFragment}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record ContractCase(
    string Name,
    string Operator,
    string Scenario,
    Func<Task> Body);

internal sealed record BaselineResult(
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
    string? ErrorMessage);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# P3 Core Contract Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |",
            "| --- | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.MemoryAllocationBytesAvg} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Scenarios",
            string.Empty,
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
                string.Empty,
                "## Failures",
                string.Empty,
                "| Case | Operator | Scenario | Error |",
                "| --- | --- | --- | --- |"
            ]);
            lines.AddRange(failures.Select(item =>
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {item.ErrorMessage} |"));
        }

        lines.Add(string.Empty);
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
        var output = "quality/evals/reports/P3CoreContracts_baseline.json";
        string? report = "quality/evals/reports/P3CoreContracts_baseline.md";
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
            P3 core contract runner

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

internal sealed class SingleServiceProvider(IFlowExecutionService flowExecutionService) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return serviceType == typeof(IFlowExecutionService) ? flowExecutionService : null;
    }
}

internal sealed class StubFlowExecutionService(
    Func<Dictionary<string, object>?, FlowExecutionResult>? execute = null) : IFlowExecutionService
{
    private readonly Func<Dictionary<string, object>?, FlowExecutionResult> _execute =
        execute ?? (_ => new FlowExecutionResult
        {
            IsSuccess = true,
            OutputData = new Dictionary<string, object> { ["Result"] = true }
        });

    public Task<FlowExecutionResult> ExecuteFlowAsync(
        OperatorFlow flow,
        Dictionary<string, object>? inputData = null,
        bool enableParallel = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_execute(inputData));
    }

    public Task<OperatorExecutionResult> ExecuteOperatorAsync(Operator @operator, Dictionary<string, object>? inputs = null)
    {
        throw new NotSupportedException("P3 contract runner only exercises ForEach subgraph execution.");
    }

    public FlowValidationResult ValidateFlow(OperatorFlow flow)
    {
        return new FlowValidationResult { IsValid = true };
    }

    public FlowExecutionStatus? GetExecutionStatus(Guid flowId)
    {
        return null;
    }

    public Task CancelExecutionAsync(Guid flowId)
    {
        return Task.CompletedTask;
    }

    public Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(
        OperatorFlow flow,
        DebugOptions options,
        Dictionary<string, object>? inputData = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Debug execution is outside the P3 contract baseline.");
    }

    public Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId)
    {
        return null;
    }

    public Task ClearDebugCacheAsync(Guid debugSessionId)
    {
        return Task.CompletedTask;
    }
}

internal sealed class StubCameraManager(byte[]? frameBytes = null) : ICameraManager
{
    private readonly byte[] _frameBytes = frameBytes ?? Array.Empty<byte>();

    public Task<IEnumerable<CameraInfo>> EnumerateCamerasAsync()
    {
        return Task.FromResult<IEnumerable<CameraInfo>>(
        [
            new CameraInfo { CameraId = "stub-camera", Name = "Stub Camera", IsConnected = true }
        ]);
    }

    public Task<ICamera> GetOrCreateCameraAsync(string cameraId)
    {
        return Task.FromResult<ICamera>(new StubCamera(cameraId, _frameBytes));
    }

    public Task<ICamera> OpenCameraAsync(string cameraId)
    {
        return GetOrCreateCameraAsync(cameraId);
    }

    public Task CloseCameraAsync(string cameraId)
    {
        return Task.CompletedTask;
    }

    public ICamera? GetCamera(string cameraId)
    {
        return new StubCamera(cameraId, _frameBytes);
    }

    public Task DisconnectAllAsync()
    {
        return Task.CompletedTask;
    }

    public void LoadBindings(List<CameraBindingConfig> bindings, string activeCameraId)
    {
    }

    public List<CameraBindingConfig> GetBindings()
    {
        return new List<CameraBindingConfig>();
    }

    public void UpdateBindings(List<CameraBindingConfig> bindings, string activeCameraId)
    {
    }

    public Task<ICamera> GetOrCreateByBindingAsync(string bindingId)
    {
        return Task.FromResult<ICamera>(new StubCamera(bindingId, _frameBytes));
    }
}

internal sealed class StubCamera(string cameraId, byte[] frameBytes) : ICamera
{
    public string CameraId { get; } = cameraId;
    public string Name => $"Stub {CameraId}";
    public bool IsConnected => true;
    public bool IsAcquiring => false;

    public Task ConnectAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        return Task.CompletedTask;
    }

    public Task<byte[]> AcquireSingleFrameAsync()
    {
        return Task.FromResult(frameBytes);
    }

    public Task StartContinuousAcquisitionAsync(Func<byte[], Task> frameCallback)
    {
        return Task.CompletedTask;
    }

    public Task StopContinuousAcquisitionAsync()
    {
        return Task.CompletedTask;
    }

    public Task SetExposureTimeAsync(double exposureTime)
    {
        return Task.CompletedTask;
    }

    public Task SetGainAsync(double gain)
    {
        return Task.CompletedTask;
    }

    public CameraParameters GetParameters()
    {
        return new CameraParameters { Width = 12, Height = 7, PixelFormat = "RGB8" };
    }

    public void Dispose()
    {
    }
}
