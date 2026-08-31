using ClearVision.Product.Application.Exports;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class CsvSanitizerTests
{
    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData(" +SUM(1,2)")]
    [InlineData("\t-1+1")]
    [InlineData("\r\n@HYPERLINK(\"https://example.test\")")]
    public void FormatField_ShouldNeutralizeFormulaMarkersAfterLeadingWhitespace(string value)
    {
        var field = CsvSanitizer.FormatField(value);

        CsvSanitizer.SanitizeText(value).Should().StartWith("'");
        field.Should().Contain(value.Replace("\"", "\"\""));
    }

    [Fact]
    public void FormatField_ShouldPreserveNormalUnicodeText()
    {
        CsvSanitizer.FormatField("正常中文 · 检测通过 ✅").Should().Be("正常中文 · 检测通过 ✅");
    }
}
