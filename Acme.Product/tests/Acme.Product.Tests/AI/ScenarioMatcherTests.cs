using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public sealed class ScenarioMatcherTests
{
    [Theory(DisplayName = "ScenarioMatcher should route golden prompts to the expected template")]
    [InlineData("检测包装箱破损、压痕、标签异常", "carton-appearance-inspection", "包装箱外观检测")]
    [InlineData("空调内机面板划伤和缝隙检测", "aircon-indoor-appearance-inspection", "空调内机外观检测")]
    [InlineData("空调外机冷凝器护网凹陷检测", "aircon-outdoor-appearance-inspection", "空调外机外观检测")]
    [InlineData("附件区域判断遥控器有没有漏装", "remote-controller-missing-inspection", "遥控器漏装检测")]
    [InlineData("测量两器铜孔之间的距离是否合格", "copper-hole-spacing-measurement", "两器铜孔间距检测")]
    [InlineData("端子线序黑蓝顺序检测", "wire-sequence-terminal", "端子线序检测")]
    [InlineData("测量两个圆形孔位的圆心距离", "copper-hole-spacing-measurement", "两器铜孔间距检测")]
    public async Task MatchAsync_GoldenPrompts_ShouldReturnExpectedTop1(
        string prompt,
        string expectedScenarioKey,
        string expectedTemplateName)
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var matcher = new ScenarioMatcher(new FlowTemplateService(tempRoot));

            var matches = await matcher.MatchAsync(prompt);

            matches.Should().NotBeEmpty();
            var top = matches[0];
            top.Scenario.ScenarioKey.Should().Be(expectedScenarioKey);
            top.Scenario.TemplateName.Should().Be(expectedTemplateName);
            top.Template.Should().NotBeNull();
            top.Template!.Name.Should().Be(expectedTemplateName);
            top.Confidence.Should().BeGreaterThan(0.75);
            top.MatchedFields.Should().NotBeEmpty();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "ScenarioMatcher should support English wire-sequence keywords")]
    public async Task MatchAsync_EnglishWirePrompt_ShouldReturnWireTemplate()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var matcher = new ScenarioMatcher(new FlowTemplateService(tempRoot));

            var matches = await matcher.MatchAsync("check terminal order and wire sequence for black blue pins");

            matches.Should().NotBeEmpty();
            matches[0].Scenario.ScenarioKey.Should().Be("wire-sequence-terminal");
            matches[0].Confidence.Should().BeGreaterThan(0.75);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "ScenarioMatcher should not force a template for low-signal prompts")]
    public async Task MatchAsync_LowSignalPrompt_ShouldReturnNoConfidentMatch()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var matcher = new ScenarioMatcher(new FlowTemplateService(tempRoot));

            var matches = await matcher.MatchAsync("帮我做一个视觉检测流程");

            matches.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "ScenarioMatcher should keep ambiguous candidates below strict template-fill confidence")]
    public async Task MatchAsync_AmbiguousPrompt_ShouldNotReachStrictTemplateFillConfidence()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var matcher = new ScenarioMatcher(new FlowTemplateService(tempRoot));

            var matches = await matcher.MatchAsync("空调内机外机外观都要检测一下");

            matches.Should().NotBeEmpty();
            matches[0].Confidence.Should().BeLessThan(0.75);
            matches.Select(match => match.Scenario.ScenarioKey)
                .Should().Contain(["aircon-indoor-appearance-inspection", "aircon-outdoor-appearance-inspection"]);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clearvision-scenario-matcher-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}
