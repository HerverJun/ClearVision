// RegionClosingOperator.cs
// 区域闭运算算子 - 先膨胀后腐蚀，用于填充小孔洞
// 对标 Halcon: closing_region

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "Region Closing",
    Description = "Closing operation (dilation followed by erosion) for filling small holes and connecting nearby regions.",
    Category = "Morphology",
    IconName = "region-closing",
    Keywords = new[] { "Region", "Closing", "Morphology", "HoleFilling", "Connect" },
    Version = "1.0.2"
)]
[AlgorithmInfo(
    Name = "Region morphology closing",
    CoreApi = "MorphologyKernel.GetOffsets -> Dilate -> Erode -> Region",
    ImplementationStrategy = "Runs one dilation pass followed by one erosion pass with the same discrete structuring element to bridge small gaps and fill small holes.",
    TimeComplexity = "O(P*K + P' * K * log Rrow)",
    TypicalLatency = "Avg 0.963 ms, max 32.974 ms over 100 synthetic golden cases",
    SpaceComplexity = "O(P+P'+K)",
    SuitableUseCases = new[] { "Filling small holes, connecting nearby components, and stabilizing fragmented foreground before measurement." },
    UnsuitableUseCases = new[] { "Maintaining strict separation between adjacent components closer than the selected kernel." },
    KnownLimitations = new[] { "Closing can bridge nearby components when the gap is within kernel reach.", "The operation uses a single dilation+erosion pair; repeated closing requires explicit workflow repetition." }
)]
[InputPort("Region", "输入区域", PortDataType.Region, IsRequired = true, Description = "区域闭运算的主输入，必须是 Region/像素区域；Image 或 Contour 不能直接替代。")]
[InputPort("Image", "参考图像（可选）", PortDataType.Image, IsRequired = false, Description = "仅用于参考图和结果可视化，不参与区域闭运算计算，也不是主输入。")]
[OutputPort("Region", "闭运算后区域", PortDataType.Region, Description = "闭运算得到的 Region/像素区域。")]
[OutputPort("Image", "可视化图像", PortDataType.Image, Description = "在参考图或区域底图上绘制的预览结果。")]
[OutputPort("Area", "Closed Area", PortDataType.Integer)]
[OperatorParam("KernelShape", "Structuring Element Shape", "enum", DefaultValue = "Rectangle", Options = new[] { "Rectangle|Rectangle", "Ellipse|Ellipse", "Cross|Cross" })]
[OperatorParam("KernelWidth", "Kernel Width", "int", DefaultValue = 3, Min = 1, Max = 99)]
[OperatorParam("KernelHeight", "Kernel Height", "int", DefaultValue = 3, Min = 1, Max = 99)]
public class RegionClosingOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.RegionClosing;

    public RegionClosingOperator(ILogger<RegionClosingOperator> logger) : base(logger) { }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(Operator @operator, Dictionary<string, object>? inputs, CancellationToken cancellationToken)
    {
        var kernelShape = GetStringParam(@operator, "KernelShape", "Rectangle");
        var kernelWidth = GetIntParam(@operator, "KernelWidth", 3, 1, 99);
        var kernelHeight = GetIntParam(@operator, "KernelHeight", 3, 1, 99);

        if (!TryGetInputRegion(inputs, "Region", out var region) || region == null)
            return Task.FromResult(OperatorExecutionOutput.Failure("当前缺少 Region；Image/Contour 不能直接替代；请使用 BinaryImageToRegion 或区域生成算子。"));

        if (region.IsEmpty)
            return Task.FromResult(CreateEmptyOutput());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var shape = kernelShape.ToLowerInvariant() switch { "ellipse" => MorphologyKernelShape.Ellipse, "cross" => MorphologyKernelShape.Cross, _ => MorphologyKernelShape.Rectangle };
        var kernel = new MorphologyKernel(shape, kernelWidth, kernelHeight);

        // 闭运算 = 先膨胀后腐蚀
        var dilated = Dilate(region, kernel);
        var closed = Erode(dilated, kernel);

        stopwatch.Stop();

        Mat visualization = TryGetInputImage(inputs, "Image", out var img) && img != null
            ? CreateVisualization(img.GetMat(), region, closed)
            : CreateRegionVisualization(region, closed);

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(visualization, new Dictionary<string, object>
        {
            { "Region", closed },
            { "OriginalArea", region.Area },
            { "Area", closed.Area },
            { "AreaChange", closed.Area - region.Area },
            { "Kernel", new { Shape = kernelShape, Width = kernelWidth, Height = kernelHeight } },
            { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds }
        })));
    }

    private Region Dilate(Region region, MorphologyKernel kernel)
    {
        var offsets = kernel.GetOffsets().ToList();
        if (offsets.Count == 0)
            return region;

        var expanded = new HashSet<(int x, int y)>();
        foreach (var run in region.RunLengths)
            for (int x = run.StartX; x <= run.EndX; x++)
                foreach (var (dx, dy) in offsets)
                    expanded.Add((x + dx, run.Y + dy));

        return PointsToRuns(expanded);
    }

    private Region Erode(Region region, MorphologyKernel kernel)
    {
        var offsets = kernel.GetOffsets().ToList();
        if (offsets.Count == 0)
            return region;

        var resultRuns = new List<RunLength>();
        foreach (var run in region.RunLengths)
        {
            int y = run.Y;
            for (int x = run.StartX; x <= run.EndX; x++)
            {
                bool allInside = offsets.All(off => region.ContainsPoint(x + off.dx, y + off.dy));
                if (allInside)
                {
                    int startX = x;
                    while (x <= run.EndX && offsets.All(off => region.ContainsPoint(x + 1 + off.dx, y + off.dy)))
                        x++;
                    resultRuns.Add(new RunLength(y, startX, x));
                }
            }
        }
        return new Region(resultRuns).MergeAdjacentRuns();
    }

    private Region PointsToRuns(HashSet<(int x, int y)> points)
    {
        if (points.Count == 0)
            return new Region();
        var runs = new List<RunLength>();
        foreach (var group in points.GroupBy(p => p.Item2).OrderBy(g => g.Key))
        {
            var xs = group.Select(p => p.Item1).OrderBy(x => x).ToList();
            int start = xs[0], prev = start;
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] > prev + 1)
                { runs.Add(new RunLength(group.Key, start, prev)); start = xs[i]; }
                prev = xs[i];
            }
            runs.Add(new RunLength(group.Key, start, prev));
        }
        return new Region(runs);
    }

    private bool TryGetInputRegion(Dictionary<string, object>? inputs, string key, out Region? region)
    {
        region = null;
        if (inputs?.TryGetValue(key, out var val) == true && val is Region r)
        { region = r; return true; }
        return false;
    }

    private Mat CreateVisualization(Mat bg, Region orig, Region closed)
    {
        var res = bg.Clone();
        using var mat = closed.ToMat();
        var bbox = closed.BoundingBox;
        var roi = new Rect(bbox.X, bbox.Y, mat.Width, mat.Height);
        if (roi.X >= 0 && roi.Y >= 0 && roi.Right <= res.Width && roi.Bottom <= res.Height)
        {
            using var c = new Mat(mat.Size(), MatType.CV_8UC3, new Scalar(0, 255, 0));
            Cv2.BitwiseAnd(c, c, c, mat);
            Cv2.AddWeighted(res[roi], 0.7, c, 0.5, 0, res[roi]);
        }
        Cv2.PutText(res, $"Closing: {orig.Area} -> {closed.Area}", new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
        return res;
    }

    private Mat CreateRegionVisualization(Region orig, Region closed)
    {
        var bbox = orig.BoundingBox;
        int pad = 20, w = Math.Max(400, bbox.Width + pad * 2), h = Math.Max(300, bbox.Height + pad * 2);
        var mat = new Mat(h, w, MatType.CV_8UC3, Scalar.Black);
        using var cmat = closed.ToMat();
        var cbbox = closed.BoundingBox;
        var croi = new Rect(cbbox.X - bbox.X + pad, cbbox.Y - bbox.Y + pad, cmat.Width, cmat.Height);
        if (croi.X >= 0 && croi.Y >= 0 && croi.Right <= w && croi.Bottom <= h)
        {
            using var c = new Mat(cmat.Size(), MatType.CV_8UC3, new Scalar(0, 255, 0));
            Cv2.BitwiseAnd(c, c, c, cmat);
            c.CopyTo(mat[croi], cmat);
        }
        Cv2.PutText(mat, $"Original: {orig.Area}", new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
        Cv2.PutText(mat, $"Closed: {closed.Area}", new Point(10, 60), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
        return mat;
    }

    private OperatorExecutionOutput CreateEmptyOutput()
    {
        var m = new Mat(300, 400, MatType.CV_8UC3, Scalar.Black);
        Cv2.PutText(m, "Empty Region", new Point(10, 30), HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);
        return OperatorExecutionOutput.Success(CreateImageOutput(m, new Dictionary<string, object>
        {
            { "Region", new Region() },
            { "Area", 0 },
            { "Message", "Input region is empty" }
        }));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kw = GetIntParam(@operator, "KernelWidth", 3);
        var kh = GetIntParam(@operator, "KernelHeight", 3);
        var kernelShape = GetStringParam(@operator, "KernelShape", "Rectangle");
        if (kw < 1 || kw > 99)
            return ValidationResult.Invalid("KernelWidth 1-99.");
        if (kh < 1 || kh > 99)
            return ValidationResult.Invalid("KernelHeight 1-99.");
        var validShapes = new[] { "Rectangle", "Ellipse", "Cross" };
        if (!validShapes.Contains(kernelShape, StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Invalid($"KernelShape must be one of: {string.Join(", ", validShapes)}");
        return ValidationResult.Valid();
    }
}
