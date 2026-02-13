# ClearVision Phase 5 — 前端 UI 增强与端到端测试

> **适用于**: opencode / AI 编码助手  
> **前置**: Phase 1-4 已完成（46 个算子、连接池、超时保护）  
> **目标**: 提升前端体验、补充集成测试、确保全量构建 0 errors

---

## 一、修复全量编译（如仍有问题）

```powershell
cd c:\Users\11234\Desktop\ClearVision\Acme.Product
Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force
dotnet build
```

如仍有 `ProjectService.cs` 错误，搜索重复定义：

```powershell
Get-ChildItem -Recurse -Include *.cs | Select-String "class CreateProjectRequest"
```

删除不含 `Flow` 属性的重复定义。

---

## 二、前端属性面板 — file 类型参数支持

### 2.1 检查当前实现

文件: `src\Acme.Product.Desktop\wwwroot\src\features\flow-editor\` 或 `src\shared\`

搜索 `renderParameter` 或 `createParameterInput` 方法，找到根据 `dataType` 分支渲染参数的逻辑。

### 2.2 添加 file 类型支持

如果找不到 `case 'file'` 分支，添加如下：

```javascript
// 在参数渲染的 switch/if 分支中
if (param.dataType === 'file' || param.dataType === 'folder') {
    const container = document.createElement('div');
    container.className = 'param-file-group';
    container.style.display = 'flex';
    container.style.gap = '4px';
    
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'param-input';
    input.value = param.value || param.defaultValue || '';
    input.placeholder = param.dataType === 'folder' ? '选择文件夹...' : '选择文件...';
    input.readOnly = true;
    input.style.flex = '1';
    
    const btn = document.createElement('button');
    btn.className = 'btn-outline btn-sm';
    btn.textContent = '浏览';
    btn.onclick = async () => {
        // 调用后端文件选择器
        const command = param.dataType === 'folder' 
            ? 'PickFolderCommand' 
            : 'PickFileCommand';
        
        try {
            const result = await window.chrome.webview.hostObjects.bridge.SendCommand(
                JSON.stringify({ Command: command, Parameters: {} })
            );
            const parsed = JSON.parse(result);
            if (parsed && parsed.FilePath) {
                input.value = parsed.FilePath;
                // 触发参数更新
                onParameterChanged(param.name, parsed.FilePath);
            }
        } catch (e) {
            console.error('文件选择失败:', e);
        }
    };
    
    container.appendChild(input);
    container.appendChild(btn);
    return container;
}
```

### 2.3 样式

```css
/* 在主 CSS 文件中追加 */
.param-file-group {
    display: flex;
    gap: 4px;
    align-items: center;
}
.param-file-group .param-input {
    flex: 1;
    cursor: pointer;
}
.param-file-group .btn-outline {
    white-space: nowrap;
    padding: 4px 8px;
    font-size: 12px;
}
```

---

## 三、算子图标扩展

### 3.1 为新增算子配置图标

在前端算子库渲染逻辑中，确认每个算子类别都有图标映射。

检查 `operatorLibrary.js` 中的图标映射对象，为新增类别补充：

```javascript
const iconMap = {
    // ... 已有映射 ...
    'color': '🎨',        // ColorDetection
    'serial': '🔌',       // SerialCommunication
    'fitting': '📐',      // GeometricFitting
    'roi': '⬜',          // RoiManager
    'shape': '🔍',        // ShapeMatching
    'subpixel': '🎯',    // SubpixelEdgeDetection
};
```

---

## 四、端到端集成测试

### 4.1 基础流程测试

文件: `tests\Acme.Product.Tests\Integration\BasicFlowIntegrationTests.cs`

```csharp
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Infrastructure.Operators;
using Acme.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Acme.Product.Tests.Integration;

/// <summary>
/// 端到端流程集成测试 — 验证多算子串联执行
/// </summary>
public class BasicFlowIntegrationTests
{
    [Fact]
    public async Task GaussianBlur_Then_Threshold_ShouldProduceOutput()
    {
        // Arrange: 创建两个算子
        var blurOp = new Operator("高斯模糊", OperatorType.GaussianBlur, 0, 0);
        var threshOp = new Operator("阈值", OperatorType.Threshold, 200, 0);
        
        var blurExecutor = new GaussianBlurOperator(new Mock<ILogger<GaussianBlurOperator>>().Object);
        var threshExecutor = new ThresholdOperator(new Mock<ILogger<ThresholdOperator>>().Object);
        
        // 创建测试输入图像
        using var testImage = TestHelpers.CreateGradientTestImage();
        var inputs = new Dictionary<string, object> { { "Image", testImage } };
        
        // Act: 串联执行
        var blurResult = await blurExecutor.ExecuteAsync(blurOp, inputs);
        blurResult.IsSuccess.Should().BeTrue("高斯模糊应成功");
        
        var threshResult = await threshExecutor.ExecuteAsync(threshOp, blurResult.OutputData);
        threshResult.IsSuccess.Should().BeTrue("阈值处理应成功");
        
        // Assert: 输出包含图像
        threshResult.OutputData.Should().ContainKey("Image");
    }

    [Fact]
    public async Task ColorConversion_Then_AdaptiveThreshold_ShouldWork()
    {
        var colorOp = new Operator("颜色转换", OperatorType.ColorConversion, 0, 0);
        var atOp = new Operator("自适应阈值", OperatorType.AdaptiveThreshold, 200, 0);
        
        var colorExec = new ColorConversionOperator(new Mock<ILogger<ColorConversionOperator>>().Object);
        var atExec = new AdaptiveThresholdOperator(new Mock<ILogger<AdaptiveThresholdOperator>>().Object);
        
        using var testImage = TestHelpers.CreateShapeTestImage();
        var inputs = new Dictionary<string, object> { { "Image", testImage } };
        
        var r1 = await colorExec.ExecuteAsync(colorOp, inputs);
        r1.IsSuccess.Should().BeTrue();
        
        var r2 = await atExec.ExecuteAsync(atOp, r1.OutputData);
        r2.IsSuccess.Should().BeTrue();
    }
    
    [Fact]
    public async Task FindContours_Then_ContourMeasurement_Pipeline()
    {
        var findOp = new Operator("轮廓检测", OperatorType.FindContours, 0, 0);
        var measureOp = new Operator("轮廓测量", OperatorType.ContourMeasurement, 200, 0);
        
        var findExec = new FindContoursOperator(new Mock<ILogger<FindContoursOperator>>().Object);
        var measureExec = new ContourMeasurementOperator(new Mock<ILogger<ContourMeasurementOperator>>().Object);
        
        using var testImage = TestHelpers.CreateShapeTestImage();
        var inputs = new Dictionary<string, object> { { "Image", testImage } };
        
        var r1 = await findExec.ExecuteAsync(findOp, inputs);
        r1.IsSuccess.Should().BeTrue();
        
        var r2 = await measureExec.ExecuteAsync(measureOp, r1.OutputData);
        r2.IsSuccess.Should().BeTrue();
    }
}
```

### 4.2 颜色检测流程测试

```csharp
public class ColorDetectionIntegrationTests
{
    [Fact]
    public async Task ColorDetection_AverageMode_ShouldReturnColorValues()
    {
        var op = new Operator("颜色检测", OperatorType.ColorDetection, 0, 0);
        var executor = new ColorDetectionOperator(new Mock<ILogger<ColorDetectionOperator>>().Object);
        
        // 纯红色图像
        using var redImage = TestHelpers.CreateTestImage(color: new OpenCvSharp.Scalar(0, 0, 255));
        var inputs = new Dictionary<string, object> { { "Image", redImage } };
        
        var result = await executor.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
    }
}
```

---

## 五、性能基准（可选）

如果时间允许，创建 `tests\Acme.Product.Tests\Benchmarks\OperatorBenchmarks.cs`：

```csharp
// 仅做简单计时，不需要 BenchmarkDotNet
public class OperatorPerformanceTests
{
    [Fact]
    public async Task GaussianBlur_ShouldComplete_Within100ms()
    {
        var op = new Operator("高斯模糊", OperatorType.GaussianBlur, 0, 0);
        var executor = new GaussianBlurOperator(new Mock<ILogger<GaussianBlurOperator>>().Object);
        using var testImage = TestHelpers.CreateTestImage(1920, 1080); // 1080p
        var inputs = new Dictionary<string, object> { { "Image", testImage } };
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(op, inputs);
        sw.Stop();
        
        result.IsSuccess.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "1080p 高斯模糊应在 100ms 内完成");
    }
}
```

---

## 六、构建验证

```powershell
cd c:\Users\11234\Desktop\ClearVision\Acme.Product

# 全量构建
dotnet build

# 全量测试（含集成测试）
dotnet test --verbosity normal
```

---

## 七、执行顺序

| 顺序 | 任务 | 文件 |
|:----:|------|------|
| 1 | 修复全量编译 | 清理缓存 / 删除重复定义 |
| 2 | 前端 file 参数 | propertyPanel.js / CSS |
| 3 | 算子图标映射 | operatorLibrary.js |
| 4 | 端到端集成测试 | BasicFlowIntegrationTests.cs |
| 5 | 颜色检测测试 | ColorDetectionIntegrationTests.cs |
| 6 | 性能测试（可选） | OperatorPerformanceTests.cs |
| 7 | 全量验证 | `dotnet build && dotnet test` |

---

## 八、完成标准

- [ ] `dotnet build` 全量 0 errors
- [ ] 前端支持 file/folder 参数选择
- [ ] 新算子图标正确显示
- [ ] 3+ 个端到端集成测试通过
- [ ] `dotnet test` 全量通过
