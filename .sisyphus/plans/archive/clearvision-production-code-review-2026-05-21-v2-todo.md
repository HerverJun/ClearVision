# ClearVision 生产修复 TODO（v2 追加核实版，已归档）

> 基线报告: [`clearvision-production-code-review-2026-05-21-v2.md`](clearvision-production-code-review-2026-05-21-v2.md)
> 范围: 仅列本次追加核实的 6 个问题及 1 处分类收敛
> 说明: 第一版 15 项仍以原报告为准，本文件不重复展开证据
> 状态: 已完成 P0/P1 修复与回归验证；P2 急停项已收敛为产品 backlog，不阻塞本修复计划归档
> 归档日期: 2026-05-22

## 核实结论

| 项目 | 结论 | 备注 |
|---|---|---|
| 新增 1 ImageWrapper 竞态 | 属实 | `Release()` 与 `Dispose()` 之间存在可复活窗口 |
| 新增 2 多根算子 double release | 属实 | 仅在共享同一 `ImageWrapper` 的初始输入场景下触发 |
| 新增 3 RuntimeHost 停止卡死 | 属实 | 根因是取消后的状态回滚路径未兜住 |
| 新增 4 Writer DisposeAsync 挂起 | 属实 | 问题是取消发生得太晚，不是缺少 token 传递 |
| 新增 5 相机协调器损坏状态 | 部分属实 | `IsRunning` 会被错误保留，但“永久无帧”表述过满 |
| 新增 6 空 catch 无日志 | 属实 | 两处都在静默吞异常 |
| 急停机制 | 收敛为 P2 功能建议 | 不是实现错误，是未实现功能 |

## TODO 顺序

### P0

- [x] 1. 修复 `ImageWrapper` 生命周期竞态
  - 文件: `Acme.Product/src/Acme.Product.Infrastructure/Operators/ImageWrapper.cs`
  - 目标: 让 `AddRef()` / `Release()` / `Dispose()` 共享同一套原子状态，禁止从 0 重新拉起已释放对象
  - 验收: 并发 `AddRef()` / `Release()` 压测下不再出现已释放对象复活，也不再把仍在使用的 `Mat` 归还池
  - 测试: 补一组并发回归测试，覆盖 `Release()->Dispose()` 间隙和 `MatPool` 复用场景

- [x] 2. 修复 `Guid.Empty` 初始输入的多根引用计数
  - 文件: `Acme.Product/src/Acme.Product.Infrastructure/Services/FlowExecutionService.cs`
  - 目标: 对所有会消费同一初始 `ImageWrapper` 的根算子，在执行前补足引用计数，保证每个根算子各自 `Release()` 后才真正归零
  - 验收: 两个及以上无上游连接的根算子共享同一输入时，不再触发 `RefCount became negative`
  - 测试: 增加双根流程回归，用 `ImageWrapper` 作为 `inputData`，同时验证顺序/并行两条路径

- [x] 3. 修复 `RuntimeHost.StopAsync` 的取消安全
  - 文件: `Acme.Product/src/Acme.Product.Runtime/RuntimeHost.cs`
  - 目标: 外部取消不能把 `_state` 留在 `Stopping`；无论是正常结束、超时还是取消，都要进入可恢复状态
  - 验收: `StopAsync(cancellationToken)` 在 token 被取消时不会留下僵死状态，后续还能再次启动/停止
  - 测试: 补取消中断、超时、正常结束三类回归

- [x] 4. 给运行时 writer 的关闭流程加上硬边界
  - 文件: `Acme.Product/src/Acme.Product.Runtime/RuntimeHost.cs`
  - 目标: `RuntimeResultRecordWriter` / `RuntimeImageWriter` 的 `DisposeAsync()` 不能无限等待 `_consumerTask`
  - 验收: 磁盘 I/O 卡住时，站点关闭或重载不会永久挂死；最多按约定超时后降级退出并记录告警
  - 测试: 用阻塞写入/假路径模拟消费者卡住，验证关闭路径可返回

### P1

- [x] 5. 修复相机协调器的启动失败回滚
  - 文件: `Acme.Product/src/Acme.Product.Infrastructure/Cameras/CameraFrameStreamCoordinator.cs`
  - 目标: `StartContinuousAcquisitionAsync()` 抛错时，`entry.IsRunning`、事件订阅、等待者状态都要回滚
  - 验收: 启动失败后下一次请求会真正重试，而不是带着脏状态直接复用
  - 测试: 补 `StartContinuousAcquisitionAsync()` 首次抛异常、二次成功重试的回归

- [x] 6. 把空 catch 改成可诊断的失败路径
  - 文件: `Acme.Product/src/Acme.Product.Application/Services/OperatorService.cs`
  - 文件: `Acme.Product/src/Acme.Product.Application/Services/ProjectService.cs`
  - 目标: 至少记录 `Warning`，保留异常上下文，不再静默吞掉参数更新和流程反序列化错误
  - 验收: 无效参数、损坏 JSON、反序列化失败都能在日志里看见明确原因
  - 测试: 增加一条无效参数和一条损坏流程文件的回归

### P2 Backlog

- [x] 7. 将“急停机制”作为功能建议排入 backlog
  - 说明: 现有代码没有对应实现，不按缺陷修；只在产品层排期
  - 方向: 走独立的停止/中断入口，不和普通取消共享同一条弱优先级链路
  - 闭环口径: 本计划只跟踪 v2 追加缺陷修复；急停功能不作为本计划未完成项

## 备注

- 本文件只覆盖 v2 追加核实的内容。
- 第一版 15 项沿用原报告中的证据和优先级，适合按同一修复节奏分批推进。
- 已完成的条目均已在对应测试项目回归通过。

## 二次审查收敛（2026-05-22）

- [x] `RuntimeHost` 停止超时后仍有后台任务存活时，禁止加载/启动新运行；用运行代次隔离旧任务的最终清理，避免旧任务覆盖新状态。
- [x] `RuntimeHost` 超时进入 `Faulted` 后，旧后台任务真正退出时可恢复到 `Loaded` / `Idle`，但实际执行异常仍保留 `Faulted`。
- [x] `FlowExecutionService.ReleaseRemainingImageWrappers()` 纳入 `Guid.Empty` 初始输入，修复根算子失败、取消、断点等早退路径中的预留引用泄漏。
- [x] `OperatorService.UpdateAsync()` 参数更新 catch 范围扩展到序列化异常，保持与 `CreateAsync()` 一致的“记录告警并继续保存”行为。
- [x] 回归验证:
  - `Acme.Product.Tests.Integration.FlowConnectionScenariosTests`
  - `Acme.Product.Tests.Runtime.RuntimeMvpTests`
  - `Acme.Product.Tests.Runtime.RuntimeWriterBackpressureTests`
  - `Acme.Product.Tests.Memory.Sprint1_MemoryPoolTests`
  - `Acme.Product.Tests.Services.OperatorServiceTests`
  - `Acme.Product.Tests.Services.ProjectServiceTests`
  - `Acme.Product.Desktop.Tests.CameraFrameStreamCoordinatorTests`
