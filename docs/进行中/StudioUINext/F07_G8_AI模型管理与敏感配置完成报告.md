# ClearVision Studio UI Next F07 G8：AI 模型管理与敏感配置完成报告

## 状态

本轮以 `studio-ui-next` 的初始 HEAD `26b04c75bbce2463ef657004697800714e0d3b7d` 为基线，完成 G8；未进入 G9。

```text
F07_G8_STATE=DONE
F07_G9_ENTRY=AWAITING_REVIEW
F07_G9_IMPLEMENTATION=FORBIDDEN
F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
```

## 实际接入能力

- Settings 使用唯一 `SettingsOwner` 接入现有 `/api/ai/models`、`/api/ai/reasoning-support` 和模型 connection-test 合同。
- Admin 可在现有后端能力范围内新增、编辑、启用/停用、激活、删除模型，并设置 planner、shadow-eval 默认模型；保存后 owner 重新 GET `/api/ai/models`，以服务端 projection 重建 baseline 和 draft。
- 编辑器覆盖 Provider、Protocol、Wire API、Auth mode、Auth header、Base URL、模型名、timeout、priority、role bindings、reasoning metadata 和能力元数据。reasoning support 使用后端解析结果，不在前端复制模型选择策略。
- Engineer 可读取 safe projection 和 reasoning-support；Admin 才能读取 full projection、执行模型 mutation 和 connection test。Operator 不进入 Settings。
- connection test 只报告通信合同的结果、状态码、延迟和 sanitized message，不宣称真实 LLM 产品质量；保留 `F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED`。

## secret authority 与生命周期

- API key 继续由现有后端 secret store/配置 authority 管理；前端只接收 `hasApiKey`/masked projection，不保存真实 key。
- API key 默认 `keep`，未填写时不发送 key；明确选择 `replace` 才发送新 key，明确选择 `clear` 才清除。masked key 被拦截，不能作为真实 key 回写。
- API key 在成功、失败、取消、分组切换、KeepAlive deactivated、route leave 和组件 dispose 后清空；不进入 localStorage、URL、日志、截图或测试快照。extra headers/query/body 只接受服务端 redacted projection。
- AI model metadata projection 与 secret 字段分离使用：服务端 safe/full projection 负责 metadata 读取，secret 真实值不回流到 UI。

## authority reconcile、权限与边界

- AI mutation 的 unknown 只登记为 `ai-models`，只有成功解码的 `/api/ai/models` authority reread 才能清除。reasoning-support、connection test、普通读取和其他 section read 不会清除已有模型 mutation unknown。
- mutation pending 时按钮明确 busy 并防重复提交；统一 Settings leave guard 阻止无提示 route leave。mutation 中断或结果未知时状态仍可观察，并可通过匹配 authority reread reconcile。
- 未新增 AI Workbench、Agent Planner、第二模型选择策略、第二 HTTP client、第二 owner 或第二 secret store；不复制 `/ai` 工作台的会话、Build、Apply 和资源确认能力。

## 验证证据

| 验证 | 结果 |
| --- | --- |
| Settings capability unit | PASS，13 files / 83 tests |
| StudioUI full unit | PASS，119 files / 708 tests |
| typecheck、lint、production build、bundle gate、bundle reproducibility | PASS |
| F07 Browser Admin/Engineer settings scenarios | PASS，13/13 |
| Desktop endpoint full suite | PASS，26 classes / 394 tests |
| Station/AI/User/Database/Settings 定向 Desktop tests | PASS，93/93 |
| Virtual PLC/TCP 回归 | PASS，Virtual PLC 83/83；TCP 14/14；virtual Modbus、MC/FINS smoke PASS |

## 未运行证据与交付结论

- 真实 Station、真实 LLM、真实 WebView2 宿主、Windows 125% DPI/分辨率矩阵、Release publish、无 Node 目标机启动和完整 CI：`NOT RUN` / `NOT PERFORMED`。
- 本轮不修改 F06 的真实模型质量结论，不修改默认入口，不退役 Legacy Settings，不进入 G9。
- 受保护未跟踪路径 `ClearVision.Product/src/capabilities/` 仍存在，未修改、未删除、未暂存、未提交。
