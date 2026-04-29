using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
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

var baseline = BsdsEdgeContourDatasetRunner.Run(options);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(baseline, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(baseline));
}

Console.WriteLine(
    $"BSDS500 EdgeDetection gate complete: {baseline.Summary.Passed}/{baseline.Summary.CaseCount} parsed/executed, " +
    $"boundaryF1={baseline.Summary.BoundaryF1:0.####}, consensusBoundaryF1={baseline.Summary.ConsensusBoundaryF1:0.####}, output={options.OutputPath}");

return baseline.Summary.Failed == 0 ? 0 : 1;

internal static class BsdsEdgeContourDatasetRunner
{
    private const string DatasetName = "BSDS500 human boundary annotations";
    private const int BoundaryTolerancePixels = 2;
    private static readonly CannyEdgeOperator EdgeOperator = new(NullLogger<CannyEdgeOperator>.Instance);

    public static BaselineResult Run(RunnerOptions options)
    {
        var index = BsdsIndex.Load(options.IndexPath);
        var records = index.Records
            .Where(item => item.HasGroundTruth)
            .Where(item => options.Split.Equals("all", StringComparison.OrdinalIgnoreCase) || item.Split.Equals(options.Split, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Split, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        if (options.MaxCases > 0)
        {
            records = records.Take(options.MaxCases).ToList();
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException($"No BSDS500 records selected from {options.IndexPath} with split '{options.Split}'.");
        }

        var cases = new List<CaseResult>(records.Count);
        foreach (var record in records)
        {
            cases.Add(RunCase(record, options));
        }

        var failed = cases.Count(item => !item.Passed);
        var precision = SafeDivide(cases.Sum(item => item.BoundaryPrecisionNumerator), cases.Sum(item => item.BoundaryPrecisionDenominator));
        var recall = SafeDivide(cases.Sum(item => item.BoundaryRecallNumerator), cases.Sum(item => item.BoundaryRecallDenominator));
        var f1 = Harmonic(precision, recall);
        var consensusPrecision = SafeDivide(cases.Sum(item => item.ConsensusPrecisionNumerator), cases.Sum(item => item.ConsensusPrecisionDenominator));
        var consensusRecall = SafeDivide(cases.Sum(item => item.ConsensusRecallNumerator), cases.Sum(item => item.ConsensusRecallDenominator));
        var consensusF1 = Harmonic(consensusPrecision, consensusRecall);

        return new BaselineResult(
            "dataset",
            new DatasetSummary(
                DateTimeOffset.UtcNow,
                DatasetName,
                "public BSDS500 real-image contour/boundary gate using MATLAB v5 human annotations",
                options.IndexPath,
                options.Split,
                cases.Count,
                cases.Count - failed,
                failed,
                cases.Sum(item => item.AnnotationCount),
                cases.Sum(item => item.TotalPixels),
                cases.Sum(item => item.PredictedEdgePixels),
                cases.Sum(item => item.UnionBoundaryPixels),
                cases.Sum(item => item.ConsensusBoundaryPixels),
                Math.Round(precision, 6),
                Math.Round(recall, 6),
                Math.Round(f1, 6),
                Math.Round(consensusPrecision, 6),
                Math.Round(consensusRecall, 6),
                Math.Round(consensusF1, 6),
                BoundaryTolerancePixels,
                Math.Round(cases.Sum(item => item.RuntimeMs), 3),
                cases.Count == 0 ? 0 : Math.Round(cases.Average(item => item.RuntimeMs), 3),
                Percentile(cases.Select(item => item.RuntimeMs), 0.95),
                cases.Sum(item => item.MemoryAllocationBytes)),
            [
                new OperatorSummary(
                    "EdgeDetection",
                    "CannyEdgeOperator",
                    cases.Count,
                    cases.Count - failed,
                    failed,
                    Math.Round(cases.Average(item => item.RuntimeMs), 3),
                    true,
                    "dataset",
                    DatasetName)
            ],
            cases
                .GroupBy(item => item.Split)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new SplitSummary(
                    group.Key,
                    group.Count(),
                    group.Count(item => item.Passed),
                    group.Count(item => !item.Passed),
                    Math.Round(group.Average(item => item.BoundaryF1), 6),
                    Math.Round(group.Average(item => item.ConsensusBoundaryF1), 6),
                    Math.Round(group.Average(item => item.RuntimeMs), 3)))
                .ToArray(),
            cases);
    }

    private static CaseResult RunCase(BsdsRecord record, RunnerOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
        Dictionary<string, object>? outputData = null;
        try
        {
            using var source = Cv2.ImRead(record.ImagePath, ImreadModes.Color);
            Require(!source.Empty(), $"Image could not be read: {record.ImagePath}");

            var annotations = MatlabV5BoundaryReader.ReadBoundaryMaps(record.GroundTruthPath);
            Require(annotations.Count > 0, $"No Boundaries sparse matrices parsed from {record.GroundTruthPath}");

            using var unionBoundary = MergeBoundaries(annotations, 1);
            using var consensusBoundary = MergeBoundaries(annotations, Math.Max(2, (annotations.Count + 1) / 2));
            EnsureSameSize(source, unionBoundary, record.Id);
            EnsureSameSize(source, consensusBoundary, record.Id);

            using var input = new ImageWrapper(source.Clone());
            var result = EdgeOperator.ExecuteAsync(CreateOperator(options), new Dictionary<string, object> { ["Image"] = input })
                .GetAwaiter()
                .GetResult();
            outputData = result.OutputData;
            Require(result.IsSuccess, $"EdgeDetection failed: {result.ErrorMessage}");
            Require(outputData is not null, "EdgeDetection returned no output data.");

            using var predicted = GetOutputImage(outputData!).Clone();
            var unionEval = Evaluate(unionBoundary, predicted, BoundaryTolerancePixels);
            var consensusEval = Evaluate(consensusBoundary, predicted, BoundaryTolerancePixels);

            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);

            return new CaseResult(
                record.Id,
                record.Split,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                source.Cols,
                source.Rows,
                annotations.Count,
                Math.Round(RequireDouble(outputData!, "Threshold1Used"), 6),
                Math.Round(RequireDouble(outputData!, "Threshold2Used"), 6),
                unionEval.TotalPixels,
                unionEval.ExpectedPixels,
                consensusEval.ExpectedPixels,
                unionEval.PredictedPixels,
                unionEval.PrecisionNumerator,
                unionEval.PrecisionDenominator,
                unionEval.RecallNumerator,
                unionEval.RecallDenominator,
                Math.Round(unionEval.Precision, 6),
                Math.Round(unionEval.Recall, 6),
                Math.Round(unionEval.F1, 6),
                consensusEval.PrecisionNumerator,
                consensusEval.PrecisionDenominator,
                consensusEval.RecallNumerator,
                consensusEval.RecallDenominator,
                Math.Round(consensusEval.Precision, 6),
                Math.Round(consensusEval.Recall, 6),
                Math.Round(consensusEval.F1, 6),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                record.Id,
                record.Split,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                0,
                0,
                0,
                options.Threshold1,
                options.Threshold2,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                ex.GetBaseException().Message);
        }
        finally
        {
            DisposeOutputImages(outputData);
        }
    }

    private static Mat MergeBoundaries(IReadOnlyList<Mat> annotations, int requiredVotes)
    {
        using var votes = new Mat(annotations[0].Rows, annotations[0].Cols, MatType.CV_16UC1, Scalar.All(0));
        foreach (var boundary in annotations)
        {
            using var binary = new Mat();
            Cv2.Threshold(boundary, binary, 0, 1, ThresholdTypes.Binary);
            Cv2.Add(votes, binary, votes, mask: null, MatType.CV_16UC1);
        }

        var merged = new Mat();
        Cv2.Compare(votes, requiredVotes - 1, merged, CmpType.GT);
        return merged;
    }

    private static BoundaryEvaluation Evaluate(Mat expected, Mat predicted, int tolerancePixels)
    {
        Require(expected.Rows == predicted.Rows && expected.Cols == predicted.Cols, "Predicted edge map size mismatch.");
        using var predictedBinary = new Mat();
        Cv2.Threshold(predicted, predictedBinary, 0, 255, ThresholdTypes.Binary);
        using var expectedDilated = Dilate(expected, tolerancePixels);
        using var predictedDilated = Dilate(predictedBinary, tolerancePixels);
        using var precisionHits = new Mat();
        using var recallHits = new Mat();
        Cv2.BitwiseAnd(predictedBinary, expectedDilated, precisionHits);
        Cv2.BitwiseAnd(expected, predictedDilated, recallHits);

        var predictedPixels = Cv2.CountNonZero(predictedBinary);
        var expectedPixels = Cv2.CountNonZero(expected);
        var precisionNumerator = Cv2.CountNonZero(precisionHits);
        var recallNumerator = Cv2.CountNonZero(recallHits);
        var precision = predictedPixels == 0 ? (expectedPixels == 0 ? 1d : 0d) : precisionNumerator / (double)predictedPixels;
        var recall = expectedPixels == 0 ? (predictedPixels == 0 ? 1d : 0d) : recallNumerator / (double)expectedPixels;

        return new BoundaryEvaluation(
            expected.Rows * expected.Cols,
            expectedPixels,
            predictedPixels,
            precisionNumerator,
            predictedPixels,
            recallNumerator,
            expectedPixels,
            precision,
            recall,
            Harmonic(precision, recall));
    }

    private static Mat Dilate(Mat source, int radius)
    {
        if (radius <= 0)
        {
            return source.Clone();
        }

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(radius * 2 + 1, radius * 2 + 1));
        var result = new Mat();
        Cv2.Dilate(source, result, kernel);
        return result;
    }

    private static Operator CreateOperator(RunnerOptions options)
    {
        var op = new Operator(Guid.NewGuid(), "BsdsEdgeContourDataset", OperatorType.EdgeDetection, 0, 0);
        AddParameter(op, "Threshold1", options.Threshold1);
        AddParameter(op, "Threshold2", options.Threshold2);
        AddParameter(op, "AutoThreshold", options.AutoThreshold);
        AddParameter(op, "AutoThresholdSigma", options.AutoThresholdSigma);
        AddParameter(op, "AutoThresholdStrategy", options.AutoThresholdStrategy);
        AddParameter(op, "EnableGaussianBlur", options.EnableGaussianBlur);
        AddParameter(op, "GaussianKernelSize", options.GaussianKernelSize);
        AddParameter(op, "ApertureSize", options.ApertureSize);
        AddParameter(op, "L2Gradient", options.L2Gradient);
        return op;
    }

    private static void AddParameter(Operator op, string name, object value)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferParameterType(value), value, isRequired: false));
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

    private static Mat GetOutputImage(Dictionary<string, object> outputData)
    {
        if (!outputData.TryGetValue("Image", out var raw) || raw is not ImageWrapper wrapper)
        {
            throw new InvalidOperationException("Missing Image output.");
        }

        return wrapper.MatReadOnly;
    }

    private static double RequireDouble(Dictionary<string, object> outputData, string key)
    {
        Require(outputData.TryGetValue(key, out var raw), $"Missing {key} output.");
        return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
    }

    private static void DisposeOutputImages(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var value in outputData.Values)
        {
            if (value is ImageWrapper wrapper)
            {
                wrapper.Dispose();
            }
        }
    }

    private static void EnsureSameSize(Mat source, Mat boundary, string id)
    {
        Require(source.Rows == boundary.Rows && source.Cols == boundary.Cols, $"Boundary size mismatch for {id}: image={source.Cols}x{source.Rows}, boundary={boundary.Cols}x{boundary.Rows}");
    }

    private static double SafeDivide(long numerator, long denominator)
    {
        return denominator == 0 ? 0 : numerator / (double)denominator;
    }

    private static double Harmonic(double precision, double recall)
    {
        return precision + recall <= 0 ? 0 : 2d * precision * recall / (precision + recall);
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(item => item).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Length - 1)], 3);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal static class MatlabV5BoundaryReader
{
    private const int MiInt8 = 1;
    private const int MiUInt8 = 2;
    private const int MiInt16 = 3;
    private const int MiUInt16 = 4;
    private const int MiInt32 = 5;
    private const int MiUInt32 = 6;
    private const int MiSingle = 7;
    private const int MiDouble = 9;
    private const int MiInt64 = 12;
    private const int MiUInt64 = 13;
    private const int MiMatrix = 14;
    private const int MiCompressed = 15;
    private const int MxCellClass = 1;
    private const int MxStructClass = 2;
    private const int MxSparseClass = 5;

    public static IReadOnlyList<Mat> ReadBoundaryMaps(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128)
        {
            throw new InvalidOperationException("MATLAB v5 file is too short.");
        }

        var maps = new List<Mat>();
        var reader = new ElementReader(bytes, 128, bytes.Length - 128);
        while (reader.TryRead(out var element))
        {
            if (element.Type == MiCompressed)
            {
                var inflated = Inflate(element.Data);
                var compressedReader = new ElementReader(inflated, 0, inflated.Length);
                while (compressedReader.TryRead(out var compressedElement))
                {
                    ParseTopLevelElement(compressedElement, maps);
                }
            }
            else
            {
                ParseTopLevelElement(element, maps);
            }
        }

        return maps;
    }

    private static void ParseTopLevelElement(MatElement element, List<Mat> maps)
    {
        if (element.Type == MiMatrix)
        {
            ParseMatrix(element.Data, null, maps);
        }
    }

    private static void ParseMatrix(ReadOnlySpan<byte> data, string? parentName, List<Mat> maps)
    {
        var reader = new ElementReader(data);
        if (!reader.TryRead(out var flags) || !reader.TryRead(out var dimsElement) || !reader.TryRead(out var nameElement))
        {
            return;
        }

        var flagsData = flags.Data;
        if (flagsData.Length < 8)
        {
            return;
        }

        var arrayClass = BitConverter.ToUInt32(flagsData[..4]) & 0xff;
        var dimensions = ReadIntArray(dimsElement);
        var name = ReadName(nameElement);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = parentName ?? string.Empty;
        }

        if (arrayClass == MxSparseClass)
        {
            var sparse = ParseSparse(reader, dimensions);
            if (string.Equals(name, "Boundaries", StringComparison.OrdinalIgnoreCase))
            {
                maps.Add(sparse);
            }
            else
            {
                sparse.Dispose();
            }

            return;
        }

        if (string.Equals(name, "Boundaries", StringComparison.OrdinalIgnoreCase))
        {
            if (reader.TryRead(out var numericElement))
            {
                maps.Add(ParseDenseBoundary(numericElement, dimensions));
            }

            return;
        }

        if (arrayClass == MxCellClass)
        {
            var count = Product(dimensions);
            for (var i = 0; i < count && reader.TryRead(out var child); i++)
            {
                if (child.Type == MiMatrix)
                {
                    ParseMatrix(child.Data, name, maps);
                }
            }

            return;
        }

        if (arrayClass == MxStructClass)
        {
            if (!reader.TryRead(out var fieldLengthElement) || !reader.TryRead(out var fieldNamesElement))
            {
                return;
            }

            var fieldLength = ReadFirstInt(fieldLengthElement);
            if (fieldLength <= 0)
            {
                return;
            }

            var fieldNames = ReadFieldNames(fieldNamesElement.Data, fieldLength);
            var elementCount = Product(dimensions);
            for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                foreach (var fieldName in fieldNames)
                {
                    if (!reader.TryRead(out var child))
                    {
                        return;
                    }

                    if (child.Type == MiMatrix)
                    {
                        ParseMatrix(child.Data, fieldName, maps);
                    }
                }
            }
        }
    }

    private static Mat ParseSparse(ElementReader reader, IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count < 2)
        {
            throw new InvalidOperationException("Sparse matrix is missing dimensions.");
        }

        if (!reader.TryRead(out var irElement) || !reader.TryRead(out var jcElement) || !reader.TryRead(out var prElement))
        {
            throw new InvalidOperationException("Sparse matrix is missing ir/jc/pr arrays.");
        }

        var rows = dimensions[0];
        var cols = dimensions[1];
        var ir = ReadIntArray(irElement);
        var jc = ReadIntArray(jcElement);
        var values = ReadDoubleArray(prElement);
        if (jc.Count < cols + 1)
        {
            throw new InvalidOperationException("Sparse matrix jc array is shorter than expected.");
        }

        var mat = new Mat(rows, cols, MatType.CV_8UC1, Scalar.All(0));
        var indexer = mat.GetGenericIndexer<byte>();
        for (var col = 0; col < cols; col++)
        {
            var start = jc[col];
            var end = jc[col + 1];
            for (var k = start; k < end && k < ir.Count; k++)
            {
                if (k < values.Count && Math.Abs(values[k]) <= double.Epsilon)
                {
                    continue;
                }

                var row = ir[k];
                if ((uint)row < rows)
                {
                    indexer[row, col] = 255;
                }
            }
        }

        return mat;
    }

    private static Mat ParseDenseBoundary(MatElement element, IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count < 2)
        {
            throw new InvalidOperationException("Boundary matrix is missing dimensions.");
        }

        var rows = dimensions[0];
        var cols = dimensions[1];
        var values = ReadDoubleArray(element);
        if (values.Count < rows * cols)
        {
            throw new InvalidOperationException("Boundary matrix payload is shorter than expected.");
        }

        var mat = new Mat(rows, cols, MatType.CV_8UC1, Scalar.All(0));
        var indexer = mat.GetGenericIndexer<byte>();
        for (var col = 0; col < cols; col++)
        {
            for (var row = 0; row < rows; row++)
            {
                if (Math.Abs(values[col * rows + row]) > double.Epsilon)
                {
                    indexer[row, col] = 255;
                }
            }
        }

        return mat;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static IReadOnlyList<int> ReadIntArray(MatElement element)
    {
        var result = new List<int>();
        var data = element.Data;
        var size = TypeSize(element.Type);
        if (size <= 0)
        {
            return result;
        }

        for (var offset = 0; offset + size <= data.Length; offset += size)
        {
            result.Add(element.Type switch
            {
                MiInt8 => unchecked((sbyte)data[offset]),
                MiUInt8 => data[offset],
                MiInt16 => BitConverter.ToInt16(data.AsSpan().Slice(offset, size)),
                MiUInt16 => BitConverter.ToUInt16(data.AsSpan().Slice(offset, size)),
                MiInt32 => BitConverter.ToInt32(data.AsSpan().Slice(offset, size)),
                MiUInt32 => unchecked((int)BitConverter.ToUInt32(data.AsSpan().Slice(offset, size))),
                MiInt64 => unchecked((int)BitConverter.ToInt64(data.AsSpan().Slice(offset, size))),
                MiUInt64 => unchecked((int)BitConverter.ToUInt64(data.AsSpan().Slice(offset, size))),
                _ => 0
            });
        }

        return result;
    }

    private static IReadOnlyList<double> ReadDoubleArray(MatElement element)
    {
        var result = new List<double>();
        var data = element.Data;
        var size = TypeSize(element.Type);
        if (size <= 0)
        {
            return result;
        }

        for (var offset = 0; offset + size <= data.Length; offset += size)
        {
            result.Add(element.Type switch
            {
                MiDouble => BitConverter.ToDouble(data.AsSpan().Slice(offset, size)),
                MiSingle => BitConverter.ToSingle(data.AsSpan().Slice(offset, size)),
                MiInt8 => unchecked((sbyte)data[offset]),
                MiUInt8 => data[offset],
                MiInt16 => BitConverter.ToInt16(data.AsSpan().Slice(offset, size)),
                MiUInt16 => BitConverter.ToUInt16(data.AsSpan().Slice(offset, size)),
                MiInt32 => BitConverter.ToInt32(data.AsSpan().Slice(offset, size)),
                MiUInt32 => BitConverter.ToUInt32(data.AsSpan().Slice(offset, size)),
                _ => 1d
            });
        }

        return result;
    }

    private static int ReadFirstInt(MatElement element)
    {
        return ReadIntArray(element).FirstOrDefault();
    }

    private static string ReadName(MatElement element)
    {
        return Encoding.ASCII.GetString(element.Data).TrimEnd('\0', ' ');
    }

    private static IReadOnlyList<string> ReadFieldNames(ReadOnlySpan<byte> data, int fieldLength)
    {
        var count = data.Length / fieldLength;
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var name = Encoding.ASCII.GetString(data.Slice(i * fieldLength, fieldLength)).TrimEnd('\0', ' ');
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static int Product(IReadOnlyList<int> values)
    {
        return values.Count == 0 ? 0 : values.Aggregate(1, (current, value) => current * Math.Max(1, value));
    }

    private static int TypeSize(int type)
    {
        return type switch
        {
            MiInt8 or MiUInt8 => 1,
            MiInt16 or MiUInt16 => 2,
            MiInt32 or MiUInt32 or MiSingle => 4,
            MiDouble or MiInt64 or MiUInt64 => 8,
            _ => 0
        };
    }

    private ref struct ElementReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        public ElementReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _offset = 0;
        }

        public ElementReader(byte[] bytes, int offset, int length)
        {
            _bytes = bytes.AsSpan(offset, length);
            _offset = 0;
        }

        public bool TryRead(out MatElement element)
        {
            element = default;
            if (_offset + 4 > _bytes.Length)
            {
                return false;
            }

            var tag = BitConverter.ToUInt32(_bytes.Slice(_offset, 4));
            var smallBytes = (int)(tag >> 16);
            var smallType = (int)(tag & 0xffff);
            if (smallBytes > 0)
            {
                if (_offset + 8 > _bytes.Length)
                {
                    return false;
                }

                element = new MatElement(smallType, _bytes.Slice(_offset + 4, smallBytes).ToArray());
                _offset += 8;
                return true;
            }

            if (_offset + 8 > _bytes.Length)
            {
                return false;
            }

            var type = (int)tag;
            var bytes = checked((int)BitConverter.ToUInt32(_bytes.Slice(_offset + 4, 4)));
            var dataOffset = _offset + 8;
            if (bytes < 0 || dataOffset + bytes > _bytes.Length)
            {
                return false;
            }

            element = new MatElement(type, _bytes.Slice(dataOffset, bytes).ToArray());
            _offset = dataOffset + RoundUp8(bytes);
            return true;
        }

        private static int RoundUp8(int value)
        {
            return (value + 7) & ~7;
        }
    }

    private readonly record struct MatElement(int Type, byte[] Data);
}

internal sealed record BsdsIndex(string Name, string LocalRoot, IReadOnlyList<BsdsRecord> Records)
{
    public static BsdsIndex Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var records = root.GetProperty("records").EnumerateArray()
            .Select(item => new BsdsRecord(
                item.GetProperty("id").GetString() ?? string.Empty,
                item.GetProperty("split").GetString() ?? string.Empty,
                item.GetProperty("image_path").GetString() ?? string.Empty,
                item.GetProperty("ground_truth_path").GetString() ?? string.Empty,
                item.TryGetProperty("has_ground_truth", out var hasGroundTruth) && hasGroundTruth.GetBoolean()))
            .ToArray();

        return new BsdsIndex(
            root.GetProperty("name").GetString() ?? "BSDS500",
            root.GetProperty("local_root").GetString() ?? string.Empty,
            records);
    }
}

internal sealed record BsdsRecord(string Id, string Split, string ImagePath, string GroundTruthPath, bool HasGroundTruth);

internal sealed record BoundaryEvaluation(
    int TotalPixels,
    int ExpectedPixels,
    int PredictedPixels,
    int PrecisionNumerator,
    int PrecisionDenominator,
    int RecallNumerator,
    int RecallDenominator,
    double Precision,
    double Recall,
    double F1);

internal sealed record BaselineResult(
    string EvidenceKind,
    DatasetSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<SplitSummary> Splits,
    IReadOnlyList<CaseResult> Cases);

internal sealed record DatasetSummary(
    DateTimeOffset GeneratedAtUtc,
    string DatasetName,
    string DatasetKind,
    string IndexPath,
    string Split,
    int CaseCount,
    int Passed,
    int Failed,
    int AnnotationCount,
    int TotalPixels,
    int PredictedEdgePixels,
    int UnionBoundaryPixels,
    int ConsensusBoundaryPixels,
    double BoundaryPrecision,
    double BoundaryRecall,
    double BoundaryF1,
    double ConsensusBoundaryPrecision,
    double ConsensusBoundaryRecall,
    double ConsensusBoundaryF1,
    int BoundaryTolerancePixels,
    double RuntimeMs,
    double RuntimeMsAvg,
    double RuntimeMsP95,
    long MemoryAllocationBytes);

internal sealed record OperatorSummary(
    string Operator,
    string Implementation,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    bool HasPublicDataset,
    string EvidenceKind,
    string DatasetName);

internal sealed record SplitSummary(
    string Split,
    int CaseCount,
    int Passed,
    int Failed,
    double BoundaryF1Avg,
    double ConsensusBoundaryF1Avg,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Split,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    int Width,
    int Height,
    int AnnotationCount,
    double Threshold1Used,
    double Threshold2Used,
    int TotalPixels,
    int UnionBoundaryPixels,
    int ConsensusBoundaryPixels,
    int PredictedEdgePixels,
    int BoundaryPrecisionNumerator,
    int BoundaryPrecisionDenominator,
    int BoundaryRecallNumerator,
    int BoundaryRecallDenominator,
    double BoundaryPrecision,
    double BoundaryRecall,
    double BoundaryF1,
    int ConsensusPrecisionNumerator,
    int ConsensusPrecisionDenominator,
    int ConsensusRecallNumerator,
    int ConsensusRecallDenominator,
    double ConsensusBoundaryPrecision,
    double ConsensusBoundaryRecall,
    double ConsensusBoundaryF1,
    string? Failure);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# EdgeDetection BSDS500 Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            $"Dataset: `{result.Summary.DatasetName}`",
            $"Index: `{result.Summary.IndexPath}`",
            $"Split: `{result.Summary.Split}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Human annotations | {result.Summary.AnnotationCount} |",
            $"| Total pixels | {result.Summary.TotalPixels} |",
            $"| Predicted edge pixels | {result.Summary.PredictedEdgePixels} |",
            $"| Union boundary pixels | {result.Summary.UnionBoundaryPixels} |",
            $"| Consensus boundary pixels | {result.Summary.ConsensusBoundaryPixels} |",
            $"| Boundary precision | {result.Summary.BoundaryPrecision:0.####} |",
            $"| Boundary recall | {result.Summary.BoundaryRecall:0.####} |",
            $"| Boundary F1 | {result.Summary.BoundaryF1:0.####} |",
            $"| Consensus boundary precision | {result.Summary.ConsensusBoundaryPrecision:0.####} |",
            $"| Consensus boundary recall | {result.Summary.ConsensusBoundaryRecall:0.####} |",
            $"| Consensus boundary F1 | {result.Summary.ConsensusBoundaryF1:0.####} |",
            $"| Boundary tolerance px | {result.Summary.BoundaryTolerancePixels} |",
            $"| Runtime ms avg | {result.Summary.RuntimeMsAvg:0.###} |",
            $"| Runtime ms p95 | {result.Summary.RuntimeMsP95:0.###} |",
            "",
            "## Failure Boundaries",
            "",
            "- `mat_annotation_parse_failure`: baseline fails if any selected MATLAB v5 file cannot expose at least one `Boundaries` dense or sparse matrix.",
            "- `operator_execution_failure`: baseline fails if product `CannyEdgeOperator` cannot process a selected BSDS500 image.",
            "- `low_contrast_boundary`: tracked by low recall against human boundary union/consensus.",
            "- `high_texture_false_positive`: tracked by low precision against dilated human boundaries.",
            "- `thin_boundary_miss`: tracked by consensus recall with a fixed 2 px tolerance.",
            "- Quality metrics are observational for this first real-data gate; pass/fail is reserved for parser and product execution integrity.",
            "",
            "## Splits",
            "",
            "| Split | Cases | Passed | Failed | Boundary F1 avg | Consensus F1 avg | Runtime ms avg |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Splits.Select(item =>
            $"| {item.Split} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.BoundaryF1Avg:0.####} | {item.ConsensusBoundaryF1Avg:0.####} | {item.RuntimeMsAvg:0.###} |"));

        lines.AddRange(
        [
            "",
            "## Cases",
            "",
            "| Case | Split | Passed | Size | Annotations | Thresholds | Union F1 | Consensus F1 | Predicted | Union | Consensus | Runtime ms | Failure |",
            "| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Split} | {item.Passed} | {item.Width}x{item.Height} | {item.AnnotationCount} | {item.Threshold1Used:0.###}/{item.Threshold2Used:0.###} | {item.BoundaryF1:0.####} | {item.ConsensusBoundaryF1:0.####} | {item.PredictedEdgePixels} | {item.UnionBoundaryPixels} | {item.ConsensusBoundaryPixels} | {item.RuntimeMs:0.###} | {item.Failure ?? "-"} |"));

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string IndexPath,
    string OutputPath,
    string ReportPath,
    string Split,
    int MaxCases,
    double Threshold1,
    double Threshold2,
    bool AutoThreshold,
    double AutoThresholdSigma,
    string AutoThresholdStrategy,
    bool EnableGaussianBlur,
    int GaussianKernelSize,
    int ApertureSize,
    bool L2Gradient,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/datasets/bsds500_index.json",
            "quality/evals/reports/EdgeDetection_bsds500_baseline.json",
            "quality/evals/reports/EdgeDetection_bsds500_baseline.md",
            "test",
            0,
            50,
            150,
            false,
            0.33,
            "MedianIntensity",
            true,
            5,
            3,
            false,
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (i + 1 >= args.Length)
            {
                return options with { ParseError = $"Missing value for {arg}" };
            }

            var value = args[++i];
            options = arg switch
            {
                "--index" => options with { IndexPath = value },
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                "--split" => options with { Split = value },
                "--max-cases" => options with { MaxCases = int.Parse(value, CultureInfo.InvariantCulture) },
                "--threshold1" => options with { Threshold1 = double.Parse(value, CultureInfo.InvariantCulture) },
                "--threshold2" => options with { Threshold2 = double.Parse(value, CultureInfo.InvariantCulture) },
                "--auto-threshold" => options with { AutoThreshold = bool.Parse(value) },
                "--auto-threshold-sigma" => options with { AutoThresholdSigma = double.Parse(value, CultureInfo.InvariantCulture) },
                "--auto-threshold-strategy" => options with { AutoThresholdStrategy = value },
                "--enable-gaussian-blur" => options with { EnableGaussianBlur = bool.Parse(value) },
                "--gaussian-kernel-size" => options with { GaussianKernelSize = int.Parse(value, CultureInfo.InvariantCulture) },
                "--aperture-size" => options with { ApertureSize = int.Parse(value, CultureInfo.InvariantCulture) },
                "--l2-gradient" => options with { L2Gradient = bool.Parse(value) },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };

            if (options.ParseError is not null)
            {
                return options;
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        Usage: dotnet run --project quality/tools/BsdsEdgeContourDatasetRunner/BsdsEdgeContourDatasetRunner.csproj -- [options]

        Options:
          --index <path>                  BSDS500 index JSON path.
          --output <path>                 Baseline JSON output path.
          --report <path>                 Baseline Markdown report path.
          --split <train|val|test|all>    Source split to run. Default: test.
          --max-cases <n>                 Optional smoke subset; 0 means all selected cases.
          --threshold1 <number>           Canny low threshold. Default: 50.
          --threshold2 <number>           Canny high threshold. Default: 150.
          --auto-threshold <bool>         Use product auto-thresholding. Default: false.
          --auto-threshold-strategy <s>   MedianIntensity or GradientPercentile. Default: MedianIntensity.
        """);
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
