# F07 G1-G6-R2 Authority Reconcile 与生命周期修补报告

## 状态

```text
F07_G1_G6_R2_STATE=DONE
F07_G7_ENTRY=AWAITING_REVIEW
F07_G7_IMPLEMENTATION=FORBIDDEN
```

本轮执行分支为 `studio-ui-next`，以执行开始时的当前 `HEAD` 为基线。受保护的未跟踪路径 `ClearVision.Product/src/capabilities/` 未修改、未删除、未暂存、未提交。本轮只处理 G1-G6，不进入 G7。

## 修补内容

### G1 authority-specific unknown reconcile

Settings owner 不再用同一 section 内任意成功操作清除 unknown outcome。每个 mutation family 只由对应 authority reread 或已有 auth lifecycle 结果清除：

| Mutation family | 允许清除 unknown 的结果 | 不允许替代清除的结果 |
| --- | --- | --- |
| Generic write | 成功解码 `GET /api/settings` 并应用完整 projection | 其他 section read、diagnostic、discovery、connection test |
| PLC settings | PLC settings reread | 无关 PLC test 或其他 section read |
| PLC mappings | mappings reread，或完整 PLC settings reread | PLC connection test |
| TCP profile write | profiles reread | TCP runtime operation 或 connection test |
| TCP runtime operation | 对应 profile status/frame reconcile | 其他 profile 或 profiles write |
| Camera bindings | bindings reread | discovery、trigger、preview 或 connection test |
| Preview operation | session stop/status 和 controller、session、frame loop、blob resource 归零确认 | 普通 camera read 或 binding reread |
| User mutation | 用户列表或目标用户 authority reread | Database status 或普通 Settings read |
| Change password | auth lifecycle 确认 session 已失效 | Settings section read |
| Database backup | 当前没有可确认备份 operation 的合同，因此保持 unknown | 普通 database status read |

无匹配 authority 的 reconcile 明确返回 unsupported，不会伪造成功或修改权威投影。取消、中断、stale、dispose 造成的 mutation unknown 继续显示在 owner state 中，直到匹配 reconcile 或既有生命周期结果完成。

### G2/G3 刷新、草稿与 route leave

- 页面顶部刷新在 dirty、pending 或 unknown 时拒绝覆盖当前 projection，并留下可见状态说明。
- projection watcher 只有在当前面板不 dirty 时才回填 draft；脏草稿不会被静默覆盖。
- 普通分组切换继续使用 KeepAlive 保留非敏感编辑草稿。
- route leave 继续复用现有共享 Leave Guard，没有建立第二套导航或确认体系。
- mutation 期间 route leave 被阻止；中断后的 unknown 仍可观察和 reconcile。
- 密码变更启动顺序修正为先让现有 auth lifecycle 通过 protected transition，再将 Settings panel 标记为 pending，避免 Settings owner 通过共享 Leave Guard 自阻塞；真正 mutation 运行期间仍然阻止 route leave。

### G4 Users、Database 与敏感字段

- Users 的 create、update、delete、reset-password 都登记到 `security` panel state。
- delete 使用独立 busy 状态和 disabled 防重复提交；create、update、reset 也共享 mutation busy 约束。
- Database backup 登记到 `database` panel state，普通 database status 不会清除 backup unknown。
- Security 或 Users 面板 deactivated 时清空 current/old password、new password、创建用户密码和 reset password，并关闭 reset modal。
- 普通非敏感 Settings 草稿仍由 KeepAlive 保留；secret 不进入普通草稿生命周期。

### G5 Camera authority projection

- binding 保存完成后重新读取服务端 bindings，并用服务端返回值重建 baseline 与 draft。
- 服务端 normalize 后与提交值不同，UI 仍以服务端值为准，不以本地提交值覆盖 authority projection。
- 409 只保留本地 draft 和冲突反馈，不修改 authoritative projection。
- 已补充 normalize 回填测试。

### G6 Generic 保存与文案

- `SaveAsync` 完成后返回 `configService.GetCurrent()` 等价的持久化 authority projection。
- 保存响应中的 Revision 使用实际落盘 Revision；前端 projection 与随后 `GET /api/settings` 一致。
- 非 object 的 scoped `PUT /api/settings` 请求直接返回 400，不进入 no-op 保存成功路径。
- Database 在导航和总览中标记为“已接入”。
- `SessionTimeoutMinutes` 明确标为历史只读，不暗示它控制当前 session expiry。

## 验证证据

以下结果均为本轮在当前工作树实际运行的结果：

| 验证 | 结果 |
| --- | --- |
| `npm run test:unit -- tests/unit/capabilities/settings` | PASS，12 files / 74 tests |
| `npm run test:unit -- tests/unit/capabilities/settings/settingsSecurityLifecycle.spec.ts` | PASS，7 tests |
| `npm run test:unit` | PASS，118 files / 699 tests |
| `npm run typecheck` | PASS |
| `npm run lint` | PASS |
| `npm run build:production` | PASS |
| `npm run bundle:gate` | PASS |
| Settings/Database/Reset/User endpoint 定向合并测试 | PASS，36 tests |
| `& './scripts/run-tests-desktop-endpoints.ps1' -NoBuild -NoRestore -Verbosity minimal` | PASS，26 classes / 394 tests |
| `& './scripts/run-tests-plc-regression.ps1' -Virtual -NoBuild -NoRestore -Verbosity minimal` | PASS，Virtual smoke + 83 tests |
| F07 Browser：`f07-settings-shell.spec.ts` + `f07-device-workbench.spec.ts` | PASS，14/14 Chromium tests |

Browser F07 首次并行运行时，密码变更用例发现上述 Settings pending 自阻塞，随后修复并单独重跑 1/1，再完整重跑 14/14。该证据是仓库 Playwright 静态 Chromium 测试，不等同于真实 WebView2、Windows 125% DPI 或真实端点联调。

## 未运行边界

- 真实相机、PLC、TCP 硬件及现场 Station：`NOT PERFORMED`。
- 真实 WebView2 宿主、Windows 125% DPI/分辨率矩阵：`NOT PERFORMED`。本轮内置 Browser 初始化曾因 kernel assets 写入 `os error 3` 失败，因此不把该路径冒充为真实 WebView2 证据。
- Release publish、无 Node 目标机启动、完整 CI：`NOT RUN`。
- G7 Station、AI model、Import/Export、Database restore/repair/cleanup/reset：本轮禁止进入，未实现。

上述未运行项是证据边界，不构成对未验证环境的通过声明。G7 仍需独立 review 和授权。
