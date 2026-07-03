# G11A-FOLLOWUP：CaliperFitV2 失败证据、Huber 语义与复杂度门禁收口

> 阶段：Vertical Product
> 状态：`DONE`
> 目标：修复 G11A 已确认的 CaliperFitV2 内核与契约缺口，保持 G11B Tool UI/Canvas/Geometry/Scene 不执行。

## 本轮范围

[x] 失败结果保留已知边缘、内点、离群点、coverage、residual 和 diagnostics 证据。
[x] `OutlierMode=Huber` 实现 deterministic Huber IRLS，而不是 MAD 硬阈值别名。
[x] `ValidateParameters` 按 Method 隔离校验。
[x] 增加组合采样复杂度预算与诊断。
[x] 增加 cancellation-aware caliper sampling 路径，旧签名保持兼容。
[x] 为 G11B 准备有界、稳定、只读 failure contract；本轮未实现 Scene。

## 明确未做

- Tool UI
- Property Panel 特殊控件
- Canvas/Geometry 编辑
- Scene projector
- 改写 HoughCircle/FitEllipse 数学行为
- 修改 Project schema、Runtime Package、Station 或 AgentRun

## 执行清单

[x] 修复 failure mapping：质量门禁失败保留证据但不输出伪圆。
[x] 实现 weighted normalized least-squares circle fit 与 deterministic Huber diagnostics。
[x] 增加 `MaxSamplingWorkUnits = 8_000_000`，使用 checked/long 预算，超预算返回 `InvalidInput` + `sampling.work-budget`。
[x] 增加 `IndustrialCaliperKernel.SampleBandProfile(..., CancellationToken)` additive overload。
[x] 增加 failure evidence、Huber、method-scoped validation、budget、cancellation、invariant 测试。
[x] 升级 `CircleMeasurement` operator version 至 `1.1.1` 并重生成资料。

## 验证清单

[x] Focused Product tests：47/47 PASS。
[x] measurement accuracy：122/122 PASS。
[x] measurement regression：144/144 PASS。
[x] Product full serial：3229 passed, 4 skipped。
[x] Desktop full serial：466/466 PASS；一次同类外单测隔离重跑通过后 full rerun PASS。
[x] Desktop Debug build：PASS，0 warnings/errors。
[x] Station Debug/Release build：PASS，0 warnings/errors。
[x] Desktop Release publish 到 `.tmp/publish-check/g11a-followup`：PASS。
[x] publish audit：`ClearVision.Product.Desktop.exe` 与 `wwwroot` 存在，144 files，dev/source artifact count 0。
[x] operator docs/catalog generator：PASS，无 source-hash/version warning。
[x] operator knowledge graph/cards：PASS，156 cards / 1845 edges；保留既有 `System.Collections.Immutable` warning。
[x] `git diff --check`：PASS，仅 CRLF warning。
[x] secret scan：PASS，3219 tracked/unignored untracked files。
[x] large-file audit：PASS，45 changed/untracked paths，none over 50MB。
[x] untracked/scratch/process audit：仅目标新卡片 untracked before staging；`.tmp/publish-check` empty；build-server shutdown 后无 `dotnet`/`testhost`/`node` 残留输出。
[x] 清理 publish scratch：DONE。

## 完成条件

[x] G11A 保持 `DONE`。
[x] G11A-FOLLOWUP 为 `DONE`。
[x] G11B 为 `READY`。
[x] G11C+ 保持 `LOCKED`。
[x] TODO 当前 Goal 回到 `G11B`。

## 回填区

- 状态：`DONE`
- 开始时间：`2026-07-03 21:12:43 +08:00`
- 完成时间：`2026-07-03 22:01:37 +08:00`
- Initial SHA：`795e13129fab235717608ae486a4e01e9ee16b8b`
- Final SHA：提交后以最终报告为准
- 远端 SHA：提交并 push 后核对
- 修改文件：CaliperFitV2 kernel、CircleMeasurement operator、IndustrialCaliperKernel、Product/Operator tests、benchmark evidence、operator generated docs/catalog、AI operator knowledge graph、TODO 与 G11A-FOLLOWUP/G11B 卡片。
- 新增/变更契约：failure result 保留 scene-ready evidence partition；Huber 为真实 weighted IRLS；组合采样预算固定上限；Caliper sampling cancellation-aware overload；CircleMeasurement `1.1.1`。
- failure evidence contract：拟合后质量门禁失败保留 Edge/Inlier/Outlier、coverage、angular coverage、residual RMSE/max、median strength、polarity、bounded diagnostics，但 Center/Radius/Circle/CircleCount 不输出伪成功。
- Huber 实现说明：普通归一化拟合作初值；每轮基于 signed residual + MAD scale 计算 Huber weight；weighted normalized least-squares 重新拟合；固定迭代和 deterministic convergence；最终阈值仅用于展示/质量分区。
- work-budget 常量与依据：`MaxSamplingWorkUnits = 8_000_000`，按 `CaliperCount * ProfileSampleCount * ceil(AveragingThickness)` checked/long 预算，超限在采样前失败。
- cancellation 语义：V2 将 token 传入 profile sampling；单 profile 内固定间隔检查；继续抛出 `OperationCanceledException`，不转换为普通 failure result。
- method-scoped validation：HoughCircle/FitEllipse/CaliperFitV2 各自只校验实际依赖参数；默认 Method 仍为 HoughCircle。
- active owner / 唯一写入口：无 G11B Tool UI/Canvas/Geometry/Scene 写入。
- Legacy mounted / subscription / timer 状态：未涉及。
- dataset/benchmark：CaliperFitV2 dataset/focused benchmark PASS；Product full 生成的 tracked benchmark reports 已刷新且均保持 PASS/OK 语义。
- operator version：`CircleMeasurement` `1.1.1`；`caliper-circle-fit.v2` contract version 保持不变。
- 测试命令与结果：见验证清单。
- build/publish/audit：见验证清单。
- GitHub CI / WebView2：完整 GitHub CI：`NOT RUN`；真实 WebView2：`NOT PERFORMED`。
- API / Project format / Runtime / Station / AgentRun 影响：未修改 Project schema、Runtime Package、Station 或 AgentRun；Station build 验证通过。
- 技术债与非阻断事项：AI operator knowledge graph runner 保留既有 `System.Collections.Immutable` warning；完整 GitHub CI 与真实 WebView2 未执行。
- 阻断：`NONE`
- 下一 Goal：`G11B`
