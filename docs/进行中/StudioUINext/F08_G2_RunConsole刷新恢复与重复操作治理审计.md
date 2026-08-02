# ClearVision Studio UI Next F08 G2：Run Console、刷新恢复与重复操作治理审计

## 1. 状态与结论

```text
F08_G2_STATE=DONE
F08_G2_AUDIT=PASS
F08_G2_STOP_CONDITION=NONE
F08_G3_ENTRY=READY_AFTER_G2_COMMIT
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

G2 在既有 Workspace Formal Run owner、Continuous Inspection owner、共享 `ApiTransport`、realtime API 与 SSE adapter 上完成统一 Run Console 和恢复治理。没有新增第二 realtime endpoint、HTTP 基础设施、EventBus、RuntimeHost、Canvas、保存链或前端运行 authority。

## 2. 实现事实

### 2.1 统一 Run Console

- Formal Run 与 Continuous Inspection 共用 `RunConsole.vue` 展示控制、双轴统计、有效判定良率、判定覆盖率、平均节拍、近期结果、权威准入和执行身份；两个 route 仍各自只有一个 capability owner 和写入口。
- 结果投影消费 canonical execution/decision outcome；诊断通过通用嵌套数据展开，不依赖 operator type、输出名或文本关键字白名单。
- 准入区展示保存修订、snapshot、flow/decision identity、必要参数、工程资源、最终判定、采集设备和运行包，并保留后端 violation code、原因与目标参数。
- 共享组件使用语义标题、`Intl.NumberFormat`、不可翻译身份值、可见 focus 和长文本换行；紧凑/舒适密度均复用现有 token。

### 2.2 Formal Run 恢复与锁定

- Workspace mount 在可交互前读取既有 realtime state。完整 `WorkspaceFormalRun` 身份可恢复 stop/reconcile/SSE；Continuous/Legacy 占用只做只读投影，不挂 Formal SSE 或停止权限。
- coordinator 已清理但 realtime state 仍携带完整 Formal identity 时，使用 G1 的 Project + execution snapshot exact reconciliation，不按时间窗口或“最新结果”猜测。
- execute/stop 响应丢失均先读取既有 authority；终态只接受 project、snapshot、revision、flow hash、decision hash 全字段一致的 reconcile/result。
- 运行中 hydrate、SSE retry exhaustion 和 result/reconcile response 都执行相同的五字段 exact identity 比较。任一字段不一致进入 `unknown-outcome`，保持 Workspace 锁定。
- Formal route leave 在 admission 阶段只取消尚未创建 session 的本地请求；执行中禁止离开且不隐式 stop；终态/unknown 只通过既有 reconcile 解锁。

### 2.3 Continuous Inspection 恢复与资源治理

- owner mount、start admission 与手动核对都读取 authority；运行中刷新恢复同一 Continuous session 和 SSE cursor，不重新 start。
- start/stop 双击通过 single-flight promise 去重；409/5xx/network/abort 进入明确错误或权威重读，不自动重启 session。
- SSE 只接受递增安全整数 sequence，拒绝重复和乱序事件；generation/session/project guard 阻止旧响应覆盖当前状态。
- 重连采用有限退避；耗尽后只做一次 authority reread，不调用 start。route leave/dispose 释放 stream、timer、AbortController 和订阅，但不向后端发送 stop。
- 晚到的 start recovery 在 owner dispose 后不再回写状态。20 次 mount/unmount 资源归零测试通过。

## 3. 独立审计修复

审计不是只读绿灯确认；本轮发现并修复两项门禁缺口：

1. Formal SSE 重试耗尽后的 authority reread 原先只比较 snapshot id。现已集中为 project、snapshot、revision、flow hash、decision hash 五字段 exact match，并覆盖主动 hydrate。
2. Continuous start/stop 异常恢复在 `await hydrate()` 后缺少 dispose 复核。现已禁止已释放 owner 被晚到 authority response 改写。

两项均有失败导向的定向单测，修复后相关 owner 测试 39/39 通过，并纳入 739 项全量回归。

## 4. 门禁证据

```text
STUDIOUI_TYPECHECK=PASS
STUDIOUI_LINT=PASS (0 warnings)
STUDIOUI_UNIT=739/739 PASS (121 files)
STUDIOUI_PRODUCTION_BUILD=PASS
FORMAL_CONTINUOUS_OWNER_TARGETED=39/39 PASS
DESKTOP_INSPECTION_ENDPOINTS=16/16 PASS
BROWSER_PLAYWRIGHT_F05=3/3 PASS (Chromium fixture, 1 worker)
BROWSER_1920X1080_COMPACT=PASS
BROWSER_1536X864_COMFORTABLE=PASS (125% layout-pressure approximation)
WEB_INTERFACE_GUIDELINES_AUDIT=PASS
VISUAL_SCREENSHOT_AUDIT=PASS
STATIC_AUTHORITY_AUDIT=PASS
GIT_DIFF_CHECK=PASS
```

Browser 场景覆盖：Continuous 运行后 route leave 不 stop、返回后 authority 恢复且 start 只有一次；Formal 会话占用 Continuous route 时无 admission、无 Continuous SSE、无 start；准入阻断、长中文和具体参数在 1536x864 舒适密度下无全局横向溢出。截图保存在忽略的 `.tmp/studio-ui-next/f08-g2/screenshots/`，不进入提交。

`InspectionEventEndpointsTests` 通过仓库串行 wrapper 单次执行，证明 realtime state endpoint 返回完整 Formal session identity。该静态 Chromium 与 TestServer 证据不等同于真实 WebView2、真实 DPI 或生产硬件。

## 5. 门禁逐条结论

| 门禁 | 结论 | 证据 |
| --- | --- | --- |
| owner 单实例、dispose 后资源归零 | PASS | diagnostics/resource tests；20 次 mount/unmount；晚到响应测试 |
| start 前刷新、响应丢失、运行中刷新、重复点击、stop unknown、终态刷新 | PASS | Formal/Continuous owner unit + Desktop endpoint + F05 Browser |
| SSE cursor、stale guard、有限重连、权威重读、不 restart | PASS | sequence、generation、retry exhaustion tests |
| Formal/Continuous 相互占用不产生第二 session | PASS | 双向 owner tests；Formal-on-Continuous Browser fixture |
| identity mismatch 永久 fail-closed，旧 response/event 不覆盖 | PASS | 五字段 hydrate/retry/reconcile/result mismatch tests |
| admission 缺失字段与 violation 在运行前可见 | PASS | typed decoder/unit + blocked admission Browser fixture |
| 结果展示不依赖 operator/output-key 白名单 | PASS | generic diagnostics/statistics tests + static audit |

## 6. 停止条件审计

- Formal 恢复只消费 realtime state、SSE 和 exact result repository；没有用 localStorage、Pinia 或 DOM 声明运行终态。
- route leave/dispose 不再作为 Continuous stop；Formal 执行中离开被阻止，但不会伪装成 stop。
- 没有新增 realtime endpoint、状态机、HTTP client、EventBus 或 SSE owner。
- 结果卡使用 canonical outcome 与通用 diagnostics，不依赖算子类型或任意 output key 白名单。

因此 G2 停止条件均未触发，独立审计结论为 `PASS`。

## 7. 未执行证据边界

```text
REAL_WEBVIEW2=NOT RUN
WINDOWS_DPI_MATRIX=NOT RUN
VIRTUAL_STATION=NOT RUN
RELEASE_PUBLISH=NOT RUN
NO_NODE_TARGET=NOT RUN
REMOTE_CI=NOT PERFORMED
REAL_STATION_CAMERA_PLC_TCP=NOT PERFORMED
```

1536x864 只是浏览器对 1920x1080 Windows 125% 工作区压力的近似检查，不宣称真实 Windows DPI 证据。真实 WebView2、Station、相机、PLC、TCP、release publish 与完整 CI 留待对应后续 Goal/final evidence 门禁。
