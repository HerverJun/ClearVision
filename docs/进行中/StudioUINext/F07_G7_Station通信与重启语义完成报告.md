# ClearVision Studio UI Next F07 G7：Station 通信与重启语义完成报告

## 状态

本轮以 `studio-ui-next` 的初始 HEAD `26b04c75bbce2463ef657004697800714e0d3b7d` 为基线，完成 G7；未进入 G9。

```text
F07_G7_STATE=DONE
F07_G9_ENTRY=AWAITING_REVIEW
F07_G9_IMPLEMENTATION=FORBIDDEN
```

## 实际接入能力

- Settings 使用唯一 `SettingsOwner` 和现有 `ProductRuntime.api`，接入专用 `GET/PUT /api/station-communication/settings`。
- 配置页覆盖通信模式、Studio 端口、局域网地址、本机 Station 同步、当前运行值、诊断信息和服务端返回的 restart hints。
- token 默认 preserve；只有明确选择 replace 才发送新 token。token 真实值不进入 owner projection、普通草稿、日志、URL、持久化或页面反馈；服务端返回值也只保留 masked projection。
- 支持现有 token endpoint 的 regenerate 操作。保存或 token 操作完成后，必须重新读取 Station authority，UI 以服务端 projection 重建 baseline/draft。
- 不自动重启 Desktop、Station 或后台服务。页面分别显示已保存、当前是否生效、以及 Studio/本机 Station 是否需要重启；保存成功不被表述为立即生效。

## authority、权限与生命周期

- 实际 endpoint matrix 与后端一致：Station settings read、write 和 token operation 均为 Admin-only；Engineer/Operator 不执行 Station endpoint，Operator 仍不能进入 Settings route。
- Station mutation 的 unknown 只登记为 `station-communication`，只能由匹配的 Station settings authority reread 清除。AI reasoning、普通读取和其他诊断不会清除它。
- dirty、pending、unknown 状态继续由 Settings leave guard 统一管理；刷新不会覆盖这些状态，KeepAlive 分组切换时普通字段可保留，token 输入在 deactivated、route leave/dispose 后清空。
- 未新增 Station runtime owner、连接管理、HTTP client、HostBridge 或第二套写入口；Stations 管理页的运行包、部署、启停和冲突恢复不在本面板内。

## 验证证据

| 验证 | 结果 |
| --- | --- |
| Settings capability unit | PASS，13 files / 83 tests |
| StudioUI full unit | PASS，119 files / 708 tests |
| typecheck、lint、production build、bundle gate、bundle reproducibility | PASS |
| F07 Browser settings shell | PASS，13/13 |
| Desktop endpoint full suite | PASS，26 classes / 394 tests |
| Station/AI/User/Database/Settings 定向 Desktop tests | PASS，93/93 |
| Virtual PLC/TCP 回归 | PASS，Virtual PLC 83/83；TCP 14/14；virtual Modbus、MC/FINS smoke PASS |

## 未运行证据与边界

- 真实 Station、真实 LLM、真实 WebView2 宿主、Windows 125% DPI/分辨率矩阵、Release publish、无 Node 目标机启动和完整 CI：`NOT RUN` / `NOT PERFORMED`。
- 本轮不修改默认入口，不退役 Legacy Settings，不重构 Station token 安全模型。
- 受保护未跟踪路径 `ClearVision.Product/src/capabilities/` 仍存在，未修改、未删除、未暂存、未提交。
