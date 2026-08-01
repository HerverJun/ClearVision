# ClearVision Studio UI Next F07 G6：Camera、Trigger 与 Preview 完成报告

## 状态

```text
REPORT_STATE=DONE
INITIAL_SHA=c36d19178a93c5c45336c50a8cda13f62951aad3
G5_SHA=2390e120
G6_FINAL_SHA=SEE_FINAL_DELIVERY_MESSAGE
REMOTE_SHA=SEE_FINAL_DELIVERY_MESSAGE
G6_IMPLEMENTATION=DONE
G7_IMPLEMENTATION=NOT_ENTERED
```

本报告以执行时 HEAD `c36d19178a93c5c45336c50a8cda13f62951aad3` 为基线，承接 G5 的 Settings Shell、唯一 `SettingsOwner`、shared `ApiTransport` 和 endpoint permission matrix。完成后停止在 G6，不修改默认入口、Legacy、Workspace 正式保存链、Runtime、Station 或 G7。

## 交付能力

- Camera discovery：通用 discovery、Huaray provider、Hikvision provider；discovery 结果作为诊断投影，不自动创建 binding 或切换 active camera。
- Camera binding：读取和保存系统级 binding，显示 active camera；保存 `exposureTimeUs`、`gainDb`、`pixelFormat`、厂商/设备身份和连接状态等真实后端字段。
- Trigger：支持 Software/External/Continuous mode，Manual/Enter photoelectric/Serial photoelectric source，Enter device learn、串口列表和串口光电测试。
- Preview：Soft capture 与 continuous preview 使用既有 camera endpoint；preview frame 通过 blob URL 投影，显示尺寸、序号和触发模式。
- active stream conflict：保存运行中相机的曝光等运行参数收到后端 `409` 时，owner 不更新 authoritative projection、不停止现有 stream、不静默覆盖配置，界面保留错误反馈。
- lifecycle：`SettingsOwner` 独占 preview session、AbortController、frame loop 和 blob URL；route leave、panel 切换、权限变化、invalidate 和 dispose 时停止 loop、abort 请求、释放 blob URL，并 best-effort 停止远端 session。
- Soft capture/Preview 明确标记为调试输入，不调用正式 Inspection、Project 或结果写入口。

## 权限与边界

| 操作 | Admin | Engineer | Operator |
| --- | --- | --- | --- |
| Camera discovery/binding read | 允许 | 允许 | Settings route 禁止 |
| Camera binding save | 允许 | 允许 | 禁止 |
| Trigger diagnostics/test/learn | 允许 | 允许 | 禁止 |
| Soft capture/continuous preview | 允许 | 允许 | 禁止 |

- Camera Settings 只管理系统级 camera administration；Workspace 继续拥有工程内 CameraBinding 与流程预览语义。
- 新 UI 不持有真实 camera client、长期 stream owner 或第二 CameraManager；所有运行状态来自既有 `/api/cameras/**`、`/api/trigger-input/**`。
- 显式 preview stop 通过 `camera.preview.stop` permission matrix；内部切换和 dispose 只做受 owner 管理的 best-effort cleanup。
- 后端 `CameraManager`、`ICameraFrameStreamCoordinator` 和相机配置持久化仍是 authority；前端 projection 不是正式结果或设备状态 authority。

## 验证结果

| 门禁 | 结果 |
| --- | --- |
| `npm run lint` | PASS |
| `npm run typecheck` | PASS |
| `npm run test:unit` | PASS，115 files / 674 tests |
| Settings capability-local unit | PASS，9 files / 49 tests |
| `npm run build:production` | PASS |
| `npm run bundle:gate` | PASS |
| Desktop test project build | PASS，0 warnings / 0 errors |
| Camera/PLC/TCP Desktop 定向套件 | PASS，71/71 |
| F07 Settings shell + device Browser fixture | PASS，11/11 Chromium tests |

G6 Browser deterministic fixture 明确覆盖：Huaray/Hikvision discovery、binding projection、exposure/trigger controls、soft capture、continuous frame loop、active stream `409` fail-closed、显式 stop、panel leave cleanup、Operator no-request 和无正式 Inspection 写入。真实厂商 SDK 和相机硬件没有被 fixture 结果冒充。

## 未运行证据

- 真实 Huaray/Hikvision 相机、曝光/增益/像素格式现场验证、真实 Enter/串口光电设备：`NOT_PERFORMED`。
- 真实 WebView2、Windows DPI/125%、Release publish、完整 CI：`NOT_PERFORMED`。
- TCP loopback 与 virtual PLC 已在 G5 相关定向测试中执行；不代表现场 PLC/相机验收。

## 受保护路径

`ClearVision.Product/src/capabilities/` 是执行开始前已存在的受保护未知未跟踪路径。本轮未修改、未删除、未加入 staging、未提交；G5/G6 commit 不包含该路径。

## G7 准入建议

G5/G6 的工程实现和当前本地证据已完成，但这不是 G7 授权。建议后续另行 review G7 Station communication 的 endpoint contract、token/deferred security debt、真实 WebView2/DPI/Release evidence 和权限矩阵；在新的明确授权前保持 G7 未实现。本轮到此停止。
