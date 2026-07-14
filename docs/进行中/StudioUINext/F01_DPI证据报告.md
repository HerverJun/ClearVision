# F01 DPI authority 与分层证据报告

## 1. Authority 结论

```text
DPI_AUTHORITY=ClearVision.Product.Desktop.csproj:ApplicationHighDpiMode=PerMonitorV2
EFFECTIVE_RUNTIME_DPI_MODE=PerMonitorV2
DPI_AUTHORITY=PASS
```

唯一代码 authority 是 Desktop 项目属性。当前代码扫描结果：

- `ApplicationHighDpiMode` 只有一个值：`PerMonitorV2`；
- `Program.cs` 没有 `Application.SetHighDpiMode(...)`；
- `app.manifest` 没有 `dpiAware` / `dpiAwareness` 第二声明；
- focused architecture guard 与完整 Desktop.Tests 防止三重权威重新出现。

## 2. 实际进程 awareness

F01 通过 Win32 runtime probe 查询真实 Desktop native window，而不是从项目文件推断：

```text
awareness.label=PerMonitorV2
awareness.processAwareness=PerMonitorAware
awareness.isPerMonitorV2=true
nativeWindow.dpi=96
nativeWindow.scale=1
nativeWindow.clientSize=1584x961
```

本机这次 native window 位于 96 DPI/100% 显示环境。该事实证明实际进程 awareness 与项目 authority 一致，但不证明真实 125/150/200 系统缩放或跨屏行为。

## 3. WebView2 simulated scale、Browser DPR 与 Canvas

| Force scale | JS DPR | CDP layout viewport | Screenshot pixels | Canvas logical | Canvas backing | Pointer hit |
| ---: | ---: | --- | --- | --- | --- | --- |
| 1.00 | 1.00 | 1584×936 | 1584×936 | 1174×686 | 1174×686 | PASS |
| 1.25 | 1.25 | 1268×749 | 1585×938 | 871×500 | 1089×625 | PASS |
| 1.50 | 1.50 | 1040×624 | 1584×936 | 699×420 | 1049×630 | PASS |
| 2.00 | 2.00 | 777×468 | 1584×936 | 743×420 | 1486×840 | PASS |

每个 scale 同时验证：

1. WebView2 renderer 命令行包含请求的 `--force-device-scale-factor`；
2. 请求 scale 与 JS `devicePixelRatio` 一致；
3. screenshot pixel/CSS layout 比例与 DPR 一致；
4. Canvas backing store 按 DPR 扩展；
5. 相同逻辑坐标命中同一 canonical node ID。

Release publish Canvas 在 scale 1 也重复通过同一分层检查。

## 4. 证据含义边界

| 层 | 本轮结论 | 能证明 | 不能替代 |
| --- | --- | --- | --- |
| project property | PASS | 唯一声明为 PerMonitorV2 | 实际运行进程 |
| Win32 runtime awareness | PASS | 真实 Desktop 进程为 PerMonitorV2 | 非本机的系统缩放/屏幕 |
| WebView2 force scale | PASS | renderer 模拟 scale 参数生效 | Windows system scale |
| JS DPR/CDP layout | PASS | Web 内容坐标与 pixel ratio | native per-monitor move |
| Canvas backing/hit test | PASS | canonical 坐标与 DPR 对齐 | 真实工业触控/现场设备 |

## 5. NOT_PERFORMED

以下没有被模拟证据冒充：

- 真实 Windows 100/125/150/200 系统缩放矩阵：`NOT_PERFORMED`；
- PerMonitorV2 跨显示器移动：`NOT_PERFORMED`；
- 真实 200% 工业屏：`NOT_PERFORMED`。

它们属于后续 release evidence；不影响本轮“唯一 authority 与当前实际 mode 已闭环”的 F01 结论。

## 6. 证据

- 汇总：`.tmp/studio-ui-next/f01/matrix/f01m3/studio-ui-dpi-evidence.json`
- 运行层：`.tmp/studio-ui-next/f01/matrix/f01m3/runs/debug-canvas-dpi-*/evidence/`
- Release：`.tmp/studio-ui-next/f01/matrix/f01m3/runs/publish-canvas/evidence/`
- 实现：`scripts/studio-ui-next/Get-DesktopRuntimeProbe.ps1`
- 判定：`scripts/studio-ui-next/Test-StudioUiDpiEvidence.ps1`
