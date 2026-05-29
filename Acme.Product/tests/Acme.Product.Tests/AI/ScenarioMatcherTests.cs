using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public sealed class ScenarioMatcherTests
{
    [Theory(DisplayName = "ScenarioMatcher should route golden prompts to the expected template")]
    [InlineData("检测包装箱破损、压痕、标签异常", "carton-appearance-inspection", "包装箱外观检测")]
    [InlineData("我希望构建一个传统视觉的模板匹配工程，支持用户上传标准模板，然后之后的图片与该模板对比进行合格与否的判断。", "classic-template-matching-inspection", "传统模板匹配检测")]
    [InlineData("金属件旋转角度会变化，用梯度形状匹配做工件定位和有无检测", "gradient-shape-match-positioning", "梯度形状匹配定位检测")]
    [InlineData("铭牌有透视变化，需要用ORB特征匹配和平面单应性做标签定位", "planar-feature-label-positioning", "平面特征匹配定位检测")]
    [InlineData("产品表面有黑点脏污，想用二值化连通域Blob做缺陷区域分析", "blob-defect-region-analysis", "Blob缺陷区域分析")]
    [InlineData("用卡尺测量胶条宽度是否超差", "caliper-width-measurement", "卡尺宽度测量")]
    [InlineData("检测圆孔孔径和圆度是否合格", "circular-hole-radius-measurement", "圆孔孔径与圆度检测")]
    [InlineData("喷涂件颜色色差DeltaE检测", "color-deltae-inspection", "Lab色差检测")]
    [InlineData("包装二维码DataMatrix追溯码读取", "code-traceability-inspection", "条码二维码追溯检测")]
    [InlineData("金属表面划伤不用深度学习，用良品参考图差分检测", "surface-reference-defect-inspection", "参考图表面缺陷检测")]
    [InlineData("相机对焦清晰度质量门，失焦模糊要判NG", "sharpness-focus-gate", "清晰度对焦质量门")]
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

    [Fact(DisplayName = "ScenarioMatcher should prefer classic template matching over generic appearance templates")]
    public async Task MatchAsync_TraditionalTemplatePrompt_ShouldNotRouteToDeepLearningAppearanceTemplate()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var matcher = new ScenarioMatcher(new FlowTemplateService(tempRoot));

            var matches = await matcher.MatchAsync(
                "传统视觉模板匹配，上传标准模板图，后续产品图片与参考图对比判断OK/NG；场景类型：外观缺陷，检测对象：产品。");

            matches.Should().NotBeEmpty();
            var top = matches[0];
            top.Scenario.ScenarioKey.Should().Be("classic-template-matching-inspection");
            top.Scenario.TemplateName.Should().Be("传统模板匹配检测");
            top.Scenario.RequiredResources.Should().NotContain("DeepLearning.ModelPath");
            top.Template.Should().NotBeNull();
            top.Template!.ScenarioPackage!.RequiredResources.Should().BeEmpty();
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
