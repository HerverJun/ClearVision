# ClearVision.OperatorLibrary 快速开始与质量验证

> 面向 ClearVision 算子库消费方、维护者和 CI/benchmark 接入者。本文只记录当前仓库已经存在的入口，不承诺尚未实现的 API。

## 1. 适用范围

`ClearVision.OperatorLibrary` 是 ClearVision 算子实现层的 NuGet 包装工程。源码仍主要来自 `ClearVision.Product/src/ClearVision.Product.Infrastructure`，包工程通过 MSBuild linked compile items 复用现有实现。

当前包关注：

- 图像处理、测量、标定、通信、流程控制、AI 等主要算子族。
- 面向消费端的模块索引：`ClearVision.OperatorLibrary.ImageProcessing`、`Measurement`、`Calibration`、`Communication`、`FlowControl`、`AI`。
- 包验收测试：`ClearVision.OperatorLibrary/tests/ClearVision.OperatorLibrary.SmokeTests`。
- 质量飞轮证据：`quality/evals/reports/*_baseline.*` 与 `operator_quality_matrix.md`。

## 2. 本地打包与引用

在 `ClearVision.OperatorLibrary` 目录运行：

```powershell
./pack.ps1
```

默认输出：

```text
ClearVision.OperatorLibrary/nupkg/ClearVision.OperatorLibrary.1.0.2.nupkg
```

消费项目可添加本地包源并引用当前版本：

```xml
<packageSources>
  <add key="local-operator-library" value="path/to/ClearVision.OperatorLibrary/nupkg" />
</packageSources>
```

```xml
<PackageReference Include="ClearVision.OperatorLibrary" Version="1.0.2" />
```

如需验证包可安装、可 restore、且代表性算子可执行，使用：

```powershell
./pack.ps1 -RunSmokeTest
```

CI 或发布流水线需要注入版本和源码追踪信息时，可使用：

```powershell
./pack.ps1 `
  -PackageVersion "1.0.2-ci.20260429.1" `
  -SourceRevisionId "a1b2c3d4" `
  -RepositoryBranch "main" `
  -RepositoryCommit "a1b2c3d4" `
  -RunSmokeTest
```

`pack.ps1` 也会读取 `CLEARVISION_OPERATORLIB_PACKAGE_VERSION`、`GITHUB_SHA`、`GITHUB_REF_NAME`、`BUILD_SOURCEVERSION`、`BUILD_SOURCEBRANCHNAME` 等常见 CI 环境变量。

## 3. 消费端最小示例

### 3.1 读取模块索引

模块索引用于让消费端按大类展示或筛选 `OperatorType`，不负责创建完整流程。

```csharp
using ClearVision.OperatorLibrary.Modules;
using ClearVision.Product.Core.Enums;

var imageOperators = ClearVision.OperatorLibrary.ImageProcessing.Operators.Types;
var measurementOperators = ClearVision.OperatorLibrary.Measurement.Operators.Types;

var meanFilterModule = OperatorModuleCatalog.GetModule(OperatorType.MeanFilter);
var caliperModule = OperatorModuleCatalog.GetModule(OperatorType.CaliperTool);

Console.WriteLine($"ImageProcessing count: {imageOperators.Count}");
Console.WriteLine($"Measurement count: {measurementOperators.Count}");
Console.WriteLine($"MeanFilter => {meanFilterModule}");
Console.WriteLine($"CaliperTool => {caliperModule}");
```

### 3.2 直接执行一个代表性算子

下面示例沿用包验收测试中的调用方式：构造 `Operator`、补齐参数、传入 `ImageWrapper`，再调用具体 executor。

```csharp
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

using var source = new Mat(64, 64, MatType.CV_8UC1, Scalar.Black);
Cv2.Rectangle(source, new Rect(8, 8, 20, 20), Scalar.White, -1);

using var inputImage = new ImageWrapper(source.Clone());

var op = new Operator("mean-filter-demo", OperatorType.MeanFilter, 0, 0);
op.AddParameter(new Parameter(Guid.NewGuid(), "KernelSize", "KernelSize", "", "int", 5));
op.AddParameter(new Parameter(Guid.NewGuid(), "BorderType", "BorderType", "", "int", 4));

var executor = new MeanFilterOperator(NullLogger<MeanFilterOperator>.Instance);
var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
{
    ["Image"] = inputImage
});

if (!result.IsSuccess)
{
    throw new InvalidOperationException(result.ErrorMessage);
}

Console.WriteLine($"Output keys: {string.Join(", ", result.OutputData!.Keys)}");
```

注意：不同算子的参数名、输入端口和输出键由现有算子实现决定。新增文档或示例前应优先参考对应算子源码、算子名片和现有测试，不要凭惯例猜 API。

## 4. 测试入口

### 4.1 包验收测试

包验收测试覆盖代表性模块，而不是只做空实例化：

- `MeanFilter`：kernel 边界与图像输出契约。
- `CaliperTool`：成功路径与缺失输入失败路径。
- `CameraCalibration`：参数校验与缺失文件夹失败路径。
- `ModbusCommunication`：端口/协议校验失败路径。
- `TryCatch`：try 分支透传契约。
- `DeepLearning`：缺失模型路径校验与运行时失败路径。
- 模块索引：`OperatorModuleCatalog` 与各模块 `Operators.Types`。

从仓库根目录运行：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.OperatorLibrary/tests/ClearVision.OperatorLibrary.SmokeTests/ClearVision.OperatorLibrary.SmokeTests.csproj"
```

若同一会话中该项目已经成功 build，后续窄范围复跑可加：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.OperatorLibrary/tests/ClearVision.OperatorLibrary.SmokeTests/ClearVision.OperatorLibrary.SmokeTests.csproj" `
  -FullyQualifiedName "ClearVision.OperatorLibrary.SmokeTests.RepresentativeOperatorAcceptanceTests" `
  -NoBuild `
  -NoRestore
```

### 4.2 产品侧回归测试

算子包复用产品侧实现，因此涉及核心行为时也应关注 `ClearVision.Product/tests/ClearVision.Product.Tests`。已有固定脚本包括：

```powershell
./scripts/run-tests-services-regression.ps1
./scripts/run-tests-phase42-regression.ps1
./scripts/run-tests-plc-regression.ps1
./scripts/run-tests-desktop-endpoints.ps1
```

若只验证多个测试类，仍应通过 `run-dotnet-test-serial.ps1` 合并到一次 `dotnet test`，避免同一 `.csproj` 并行。

## 5. 质量飞轮与 CI 分层

质量飞轮把证据分为三条常用执行 lane：

- `quick_contract_suite`：本地/CI 友好的契约证据，目标是快速 gate。
- `golden_core50_suite`：核心 50 的 synthetic 或 protocol-oracle baseline，通常比 quick lane 更重。
- `dataset_heavy_suite`：dataset、public-proxy、重 benchmark 证据，适合手动或定时任务。

查看、校验或演练 suite：

```powershell
python quality/tools/run_quality_suite.py --suite quick_contract_suite --list
python quality/tools/run_quality_suite.py --suite quick_contract_suite --validate-only
python quality/tools/run_quality_suite.py --suite quick_contract_suite --dry-run
```

真正执行时使用：

```powershell
python quality/tools/run_quality_suite.py --suite quick_contract_suite --run
```

执行器会串行运行 manifest 中的命令。当前质量矩阵入口：

```text
quality/evals/reports/operator_quality_matrix.md
```

它汇总 `Contract`、`Golden`、`Dataset`、`Field`、`Benchmark` 等证据信号。阅读矩阵时要区分证据等级：mock/contract evidence 不能当作现场 field evidence，synthetic golden 也不能自动等同公开数据集验证。

## 6. Benchmark 入口

当前 benchmark 证据有两类：

1. 质量 runner 产物：`quality/evals/reports/*_baseline.json` 与 `*_baseline.md`。
2. 产品测试产物：`ClearVision.Product/test_results/*benchmark_report.md`。

通用 baseline benchmark 入口：

```powershell
dotnet run --project scripts/BaselineBenchmark/BaselineBenchmark.csproj -- `
  --iterations 8 `
  --warmup 1 `
  --output docs/审计资料/报告/baseline_performance.json
```

产品侧已有 benchmark 测试会生成报告：

- `ClearVision.Product/test_results/operator_benchmark_report.md`
- `ClearVision.Product/test_results/calibration_operator_benchmark_report.md`
- `ClearVision.Product/test_results/measurement_operator_benchmark_report.md`
- `ClearVision.Product/test_results/preprocessing_benchmark_report.md`

单次本机 benchmark 只能说明当前机器、当前输入、当前迭代次数下的趋势。用于 CI 或发布判断时，应同时看：

- baseline JSON/Markdown 是否更新。
- `operator_quality_matrix.md` 中 `HasBenchmark` 和证据类型是否匹配。
- 是否存在 1080p、4K、batch pressure、CPU/GPU fallback 等目标场景要求。

## 7. 文档维护约定

新增或更新算子库文档时：

- 先找源码、测试、suite manifest、baseline report，再写说明。
- 示例代码优先来自已有测试或最小可验证路径。
- 不在文档里虚构参数名、输出键、服务注册方式或 CI 工作流名称。
- 只把稳定入口写到 `docs/参考资料/指南/`；仍在推进中的计划放到 `docs/进行中/当前计划/`。
- 如果更改了算子行为，至少同步检查对应算子名片、质量矩阵、baseline report 和 README 入口。
