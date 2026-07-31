# F07 G2：Settings Shell、Route 与生命周期地基

## 状态

```text
REPORT_STATE=DONE
INITIAL_SHA=084165300d87b2abf09d5a879da53920531672a5
G1_R_SHA=084165300d87b2abf09d5a879da53920531672a5
G2_FINAL_SHA=HANDOFF_COMMIT
REMOTE_SHA=HANDOFF_COMMIT_AFTER_PUSH
F07_SETTINGS_ROUTE=DONE
F07_SETTINGS_READ_ONLY_SHELL=DONE
F07_SECTION_SAVES=NOT_IMPLEMENTED
F07_G3=NOT_STARTED
F07_G3_IMPLEMENTATION=FORBIDDEN
```

本报告随 G2 交付提交发布；最终提交 SHA 与远端 SHA 以交付消息为准。G2 没有进入任何正式 Settings 保存、draft 持久化或 G3 section implementation。

## 实际改动

- 新增受保护 `/settings` route，复用现有 `ProductRuntimeBoundary`；`allowedRoles` 固定为 `Admin`、`Engineer`，Operator 由既有 route guard 导向 authenticated forbidden 页面。
- 在产品主导航和共享 navigation contract 增加“设置”；`/settings` 加入安全 `returnTo` allowlist。没有修改 Next 默认入口、Legacy route、feature flag 或配置默认值。
- 新增 `SettingsPage`、`SettingsGroupNavigation`、`SettingsOverview` 和 capability-local view model。
- 页面只调用共享 `ProductRuntime.api.get('settings')`，由既有 Settings decoder 投影服务端 safe/full response；不调用任何保存 endpoint。
- Overview 展示服务端观察 revision、产品标题/主题、safe subset/full 范围、generic section 读取状态和隔离的专用 authority。
- General、Storage、Runtime、Security 可查看已返回的 generic projection；PLC、TCP、Camera、Station、AI、Database 只显示后续专用 endpoint 接入边界，不用 generic fallback 猜测数据。
- 角色或共享 session phase 变化时，旧 owner 真实 dispose；route leave、logout/session end 会 abort pending read 并释放 owner。单 owner mount token 保持有效。
- 新增 Browser Settings fixture 场景，覆盖 safe/full、loading、Operator forbidden、GET-only 和 1920/390 宽度无水平溢出。

## 权限与 Owner 结论

```text
Admin    -> /settings 可达，读取 full projection
Engineer -> /settings 可达，读取 safe subset 或服务端实际返回的 projection
Operator -> route guard forbidden；不挂载 Settings owner，不请求 /api/settings
```

页面不把导航可见性当作后端授权。401、403、error、decode failure 都保留为独立可解释状态；decode 失败时不展示未验证字段。UI preferences 仍与 AppConfig 产品主题独立。

## 验证结果

| 门禁 | 结果 |
| --- | --- |
| `npm run typecheck` | PASS |
| `npm run lint` | PASS |
| `npm run test:unit` | PASS，110 files，656/656 |
| `npm run build:production` | PASS |
| `npm run bundle:gate` | PASS |
| Browser Settings 场景 | PASS，4/4 Chromium tests |
| G1-R Desktop endpoint tests | PASS，58/58；同一 `.csproj` 串行 |
| WebView2、真实 Windows DPI/125%、Desktop publish、CI | NOT RUN / NOT PERFORMED |

Browser 验证使用现有 `studio-ui-next` static Browser fixture；它证明路由、投影状态、GET-only 请求和布局基本约束，不替代真实 WebView2、WinForms、Windows DPI 或后端现场证据。Playwright 命令通过 npm 转发 `--project` 时产生 npm 配置 warning，但 4 个目标测试均通过。

## 已知非阻塞问题

- 非 generic section 的专用读取、section 保存和 operation UI 延后到后续 Goal；当前用明确“后续接入”状态，不伪造后端数据。
- 生产 bundle budget 没有新增 Settings 专项预算项；现有 bundle gate 已通过，Settings 作为 lazy route chunk 输出。
- G2 未运行真实 WebView2、publish、CI 和 Windows 原生 DPI 证据。

## 真正 blocker 与 G3 准入

本轮没有发现需要新增后端 authority、第二 HTTP/Owner/HostBridge、配置默认值变更、Legacy 变更或持久化体系的 blocker。G2 具备提交 review 的证据，但不自动授权 G3；G3 仍需新的明确 review/准入后，才能实现任何 Settings section 保存功能。

```text
F07_G2_STATE=DONE
F07_G3_ENTRY=AWAITING_REVIEW
F07_G3_IMPLEMENTATION=FORBIDDEN
```
