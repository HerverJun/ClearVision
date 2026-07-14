# F01 Canonical FlowCanvas 验证报告

## 1. 结论

```text
CANVAS_DECISION=REUSE_EXISTING_FLOW_CANVAS
CANVAS_CORRECTNESS=PASS
CANVAS_IDENTITY=PASS
CANVAS_LIFECYCLE=PASS
CANVAS_PERFORMANCE=WARNING
BLOCKED_CANVAS_FOUNDATION=NO
BLOCKED_CANVAS_PERFORMANCE=NO
```

StudioUI 没有建立第二 Canvas 内核。`StudioUI/src/labs/canvas/canonicalFlowCanvas.ts` 只提供窄宿主接口，Vite alias 直接指向现有 canonical：

- `wwwroot/src/core/canvas/flowCanvasAdapter.js`
- `wwwroot/src/features/flow-editor/flowEditorInteraction.js`
- `createHostedFlowCanvasAdapter`

`FlowCanvas`、adapter registry、连接校验、序列化和 pointer 语义仍由 legacy canonical 实现负责。Vue 组件不持有 Canvas、RAF、ResizeObserver 或 interaction 对象；`canvasLabOwner.ts` 是唯一 mounted owner，并按 unsubscribe → interaction → adapter 顺序幂等释放。

## 2. Fixture 与正式 DTO

`operatorFlowFixtures.ts` 从当前 `OperatorFlowDto`、`OperatorDto`、`PortDto`、`OperatorConnectionDto` 和 backend operator metadata 语义构造并冻结投影，不新增生产类型。PascalCase DTO 兼容只集中在 decoder。

固定 fixture：

| Fixture | 节点 | 连接 | 用途 | Identity |
| --- | ---: | ---: | --- | --- |
| canonical | 5 | 3 | ImageAcquisition → Thresholding → BlobAnalysis 及正式字段/拒绝矩阵 | `ddf7a806` round-trip PASS |
| benchmark-100 | 100 | 150 | drag、pan、zoom 标准化 A/B | `318f9a5a` round-trip PASS |
| stress-300 | 300 | 450 | load、serialize、dispose 压力 | `47988a7a` round-trip PASS |

正式身份字段包含 flow/operator/port/connection ID、operator type、position、parameters、enabled、decision configuration 和连接端点。100/300 节点 WebView2 探针的 before/after fingerprint 相同。

## 3. 正确性与交互

中央 Playwright 的 Canvas 五个场景全部通过：

1. canonical rejection matrix 与 serialize/reload identity；
2. pointer 创建合法连接，并拒绝不兼容端口；
3. drag、selection、pan、zoom、browser resize 保持 canonical logical coordinates；
4. 20 次 route mount/unmount 保持单 owner 并释放全部资源；
5. DPR 1/1.25/1.5/2 与 viewport matrix 保持 backing store 和 hit-test 对齐。

连接拒绝码在 browser 与真实 WebView2 中一致：

| 场景 | 预期/实际 |
| --- | --- |
| duplicate | `duplicate-connection` |
| occupied input | `input-port-occupied` |
| self connection | `self-connection` |
| incompatible type | `incompatible-port-type` |
| cycle | `cycle` |

Legacy canonical 回归同时通过：

- `canvas-core.test.mjs`：32/32；
- `template-selector.test.mjs`：14/14；
- node drag 只推进一次 flow revision；
- restore 清除 transient drag/connection state；
- hosted adapter 每个 canvas id 只有一个实例，stale dispose 不会销毁新 generation；
- destroy 释放 deferred menu listener、global pointer release 与 adapter registry。

## 4. 生命周期证据

StudioUI unit 与 Playwright 覆盖：

- mounted `ownerCount=1`，disposed `ownerCount=0`；
- 20 次 mount/unmount 无重叠 owner；
- subscribe 返回的 structure/view/selection 订阅全部释放；
- ResizeObserver、theme observer、draw RAF、resize RAF、interaction RAF、context-menu timer 全部归零；
- `FlowEditorInteraction.destroy()` 与 adapter `dispose()` 均幂等；
- route 切换后无双触发、无持续 listener 增长；
- `window.__STUDIO_UI_DIAGNOSTICS__` 只投影 `canvasOwnerCount`，不是写入口。

真实 WebView2 `debug-canvas-dpi-1` 在 mounted 状态报告：

```text
studio.mountCount=1
studio.activeRoot=studio-ui
studio.canvasOwnerCount=1
canvas.ownerCount=1
canvas.lastError=null
```

同一探针主动 dispose 后报告：

```text
canvas.status=disposed
canvas.ownerCount=0
canvas.totalDisposals=1
studio.canvasOwnerCount=0
```

## 5. DPR 与 hit testing

真实 WebView2 force-scale 分层探针均命中同一节点 `00000010-0000-4000-8000-000000000001`：

| Requested scale | JS DPR | Canvas logical | Canvas backing | Hit test |
| ---: | ---: | --- | --- | --- |
| 1.00 | 1.00 | 1174×686 | 1174×686 | PASS |
| 1.25 | 1.25 | 871×500 | 1089×625 | PASS |
| 1.50 | 1.50 | 699×420 | 1049×630 | PASS |
| 2.00 | 2.00 | 743×420 | 1486×840 | PASS |

这些是 WebView2 simulated scale、JS DPR、backing store 与 hit testing 证据，不冒充真实 Windows 125/150/200 系统缩放或跨显示器移动。

## 6. 标准化性能 A/B

正式样本：3 个完整 Legacy/Studio 比较组；每个场景 2 次 warmup + 5 次 formal sample；6 份原始 evidence；相同机器、Release、fixture、窗口、scale 与动作。

| Group | Aggregate median regression | >20% 场景 | 结论 |
| --- | ---: | --- | --- |
| g01 | 16.10% | `benchmark-100-drag` 54.87% | WARNING |
| g02 | 0.97% | 无 | PASS |
| g03 | 0.00% | 无 | PASS |

三个组的所有场景 long-task max 均为 0；`hardFailures=[]`；没有崩溃、身份破坏、重复事件、持续泄漏、不可完成或长时间卡死。只有第一组单场景超过 20%，后两组未复现，因此：

```text
threeConsecutiveRegressionGroups=false
CANVAS_PERFORMANCE=WARNING
```

不满足 `BLOCKED_CANVAS_PERFORMANCE` 的三组连续退化条件。

原始样本与汇总：

- `.tmp/studio-ui-next/f01/matrix/f01m3/performance/`
- `.tmp/studio-ui-next/f01/matrix/f01m3/performance/studio-ui-canvas-performance-summary.json`

## 7. 验证汇总

```text
StudioUI unit = 17 files / 107 tests PASS
Central Playwright studio-ui-next = 10/10 PASS（Canvas 5）
Legacy Canvas core = 32/32 PASS
Legacy template/interaction = 14/14 PASS
真实 WebView2 Canvas Debug scale matrix = 4/4 PASS
真实 WebView2 Canvas Release publish = PASS
Performance comparison groups = 3/3 complete; decision WARNING
```

正式 WebView2 evidence：`.tmp/studio-ui-next/f01/matrix/f01m3/`。`f01m1`、`f01m2` 是失败调试样本，不属于验收结论。
