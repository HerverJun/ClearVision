using System.Collections;
using System.Text.Json;
using ClearVision.Product.Core.ResultPaths;
using FluentAssertions;

namespace ClearVision.Product.Tests.ResultPaths;

public sealed class ResultPathV1Tests
{
    [Fact]
    public void Parser_ShouldConsumeVersionedConformanceVectors()
    {
        var vectors = LoadVectors();

        foreach (var path in vectors.Valid)
        {
            var parsed = ResultPathParser.Parse(vectors.Version, path);

            parsed.Succeeded.Should().BeTrue($"'{path}' should parse: {parsed.Diagnostic}");
            parsed.Path!.CanonicalPath.Should().Be(path);
            ResultPathFormatter.Format(parsed.Path).Should().Be(path);
        }

        foreach (var path in vectors.Invalid)
        {
            var parsed = ResultPathParser.Parse(vectors.Version, path);

            parsed.Succeeded.Should().BeFalse($"'{path}' should be rejected");
            parsed.Diagnostic!.Code.Should().StartWith("RP1");
        }
    }

    [Fact]
    public void Formatter_ShouldRoundTripQuotesBackslashesUnicodeAndControls()
    {
        var keys = new[] { "A\\B", "Quote\"Key", "Line\nBreak", "中文" };
        var path = ResultPathFormatter.FormatObjectPath(keys);
        var parsed = ResultPathParser.Parse(ResultPathV1.Version, path);

        parsed.Succeeded.Should().BeTrue(parsed.Diagnostic?.ToString());
        parsed.Path!.Segments.Select(segment => segment.Value).Should().Equal(keys);
        ResultPathFormatter.Format(parsed.Path).Should().Be(path);
    }

    [Theory]
    [InlineData("$.Score", "RP104")]
    [InlineData("$[0]", "RP104")]
    [InlineData("$[*]", "RP104")]
    [InlineData("$[?(@.Score>0)]", "RP104")]
    [InlineData("$[\"\\u0053core\"]", "RP107")]
    public void Parser_ShouldRejectUnsupportedOrNonCanonicalSyntax(string path, string code)
    {
        var parsed = ResultPathParser.Parse(ResultPathV1.Version, path);

        parsed.Succeeded.Should().BeFalse();
        parsed.Diagnostic!.Code.Should().Be(code);
    }

    [Fact]
    public void Parser_ShouldRejectLimitsWithoutThrowing()
    {
        var longKey = new string('K', ResultPathV1.MaxKeyChars + 1);
        var tooManySegments = ResultPathFormatter.Format(Enumerable
            .Range(0, ResultPathV1.MaxSegments + 1)
            .Select(index => ResultPathSegment.ObjectKey($"K{index}"))
            .ToList());
        var malicious = "$[\"unterminated\\";

        ResultPathParser.Parse(ResultPathV1.Version, ResultPathFormatter.FormatObjectPath([longKey]))
            .Diagnostic!.Code.Should().Be("RP109");
        ResultPathParser.Parse(ResultPathV1.Version, tooManySegments)
            .Diagnostic!.Code.Should().Be("RP108");
        var act = () => ResultPathParser.Parse(ResultPathV1.Version, malicious);
        act.Should().NotThrow();
        act().Diagnostic!.Code.Should().StartWith("RP1");
    }

    [Fact]
    public void Resolver_ShouldResolveJsonAndStringDictionaryObjectKeys()
    {
        using var document = JsonDocument.Parse("{\"Payload\":{\"Score\":0.95,\"Seen\":7}}");
        var dictionary = new Dictionary<string, object?>
        {
            ["Payload"] = new Dictionary<string, object?>
            {
                ["Score"] = 9L
            }
        };

        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Payload\"][\"Seen\"]", document.RootElement)
            .Value.Should().BeOfType<JsonElement>()
            .Which.GetInt32().Should().Be(7);
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Payload\"][\"Score\"]", dictionary)
            .Value.Should().Be(9L);
    }

    [Fact]
    public void Resolver_ShouldFailClosedForMissingKeysScalarBeforeEndAndTerminalComplex()
    {
        var root = new Dictionary<string, object?>
        {
            ["Scalar"] = 1L,
            ["Object"] = new Dictionary<string, object?>()
        };

        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Missing\"]", root)
            .Diagnostic!.Code.Should().Be("RP111");
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Scalar\"][\"Child\"]", root)
            .Diagnostic!.Code.Should().Be("RP117");
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Object\"]", root)
            .Diagnostic!.Code.Should().Be("RP118");
    }

    [Fact]
    public void Resolver_ShouldFailClosedForForbiddenResourcesAndNonFiniteNumbers()
    {
        var root = new Dictionary<string, object?>
        {
            ["Bytes"] = new byte[] { 1, 2, 3 },
            ["Nan"] = double.NaN
        };

        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Bytes\"]", root)
            .Diagnostic!.Code.Should().Be("RP119");
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Nan\"]", root)
            .Diagnostic!.Code.Should().Be("RP120");
    }

    [Fact]
    public void Resolver_ShouldNotEnumerateUnknownEnumerable()
    {
        var enumerable = new CountingEnumerable();
        var root = new Dictionary<string, object?>
        {
            ["Items"] = enumerable
        };

        var result = ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"A\"]", root);

        result.Diagnostic!.Code.Should().Be("RP112");
        enumerable.GetEnumeratorCallCount.Should().Be(0);
    }

    [Fact]
    public void Resolver_ShouldUseStableIdAdapterWithExactUniqueIdsAndLimits()
    {
        var options = new ResultPathResolverOptions
        {
            StableIdAdapters = [new StableItemAdapter()]
        };
        var root = new Dictionary<string, object?>
        {
            ["Items"] = new List<StableItem>
            {
                new("A", new Dictionary<string, object?> { ["Score"] = 1L }),
                new("B", new Dictionary<string, object?> { ["Score"] = 2L })
            }
        };

        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"B\"][\"Score\"]", root, options)
            .Value.Should().Be(2L);
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"missing\"]", root, options)
            .Diagnostic!.Code.Should().Be("RP115");

        root["Items"] = new List<StableItem> { new("A", 1L), new("A", 2L) };
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"A\"]", root, options)
            .Diagnostic!.Code.Should().Be("RP114");

        root["Items"] = new List<StableItem> { new("A", 1L), new("A", 2L), new("B", 3L) };
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"B\"]", root, options)
            .Diagnostic!.Code.Should().Be("RP114");

        root["Items"] = new List<StableItem> { new(new string('I', ResultPathV1.MaxStableIdChars + 1), 1L) };
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"A\"]", root, options)
            .Diagnostic!.Code.Should().Be("RP109");

        root["Items"] = new List<StableItem> { new(null, 1L) };
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"A\"]", root, options)
            .Diagnostic!.Code.Should().Be("RP113");

        root["Items"] = Enumerable.Range(0, ResultPathV1.MaxStableCollectionScanCount + 1)
            .Select(index => new StableItem(index.ToString(), index))
            .ToList();
        ResultPathResolver.Resolve(ResultPathV1.Version, "$[\"Items\"][@id=\"not-found\"]", root, options)
            .Diagnostic!.Code.Should().Be("RP116");
    }

    private static ConformanceVectors LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ResultPathV1Conformance.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ConformanceVectors>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed record ConformanceVectors(int Version, string[] Valid, string[] Invalid);

    private sealed record StableItem(string? Id, object? Value);

    private sealed class StableItemAdapter : IResultPathStableIdCollectionAdapter
    {
        public bool CanRead(object? collection) => collection is IReadOnlyList<StableItem>;

        public IEnumerable<ResultPathStableIdItem> Enumerate(object collection)
        {
            foreach (var item in (IReadOnlyList<StableItem>)collection)
            {
                yield return new ResultPathStableIdItem(item.Id, item.Value);
            }
        }
    }

    private sealed class CountingEnumerable : IEnumerable
    {
        public int GetEnumeratorCallCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            GetEnumeratorCallCount++;
            yield break;
        }
    }
}
