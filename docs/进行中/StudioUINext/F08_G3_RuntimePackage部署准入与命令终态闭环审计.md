# ClearVision Studio UI Next F08 G3：Runtime Package 部署准入与命令终态闭环审计

## 1. 状态与结论

```text
F08_G3_STATE=DONE
F08_G3_AUDIT=PASS
F08_G3_STOP_CONDITION=NONE
F08_G4_ENTRY=READY_AFTER_G3_COMMIT
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
```

G3 在既有 Runtime Package exporter/store、`StationCentralStore`、Station command journal/result spool、`StationPackageDeploymentService`、`RuntimeHost` 与 `stations-read` capability 上闭合正式部署流程。没有新增第二 package store、command store、RuntimeHost、HTTP client、EventBus、HostBridge 执行通道或前端激活 authority。

## 2. 实现事实

### 2.1 命令创建幂等与未知结果恢复

- `StationCommandRecords` additive 保存 `ClientRequestId` 与 canonical payload SHA-256，并以 `StationId + CommandType + ClientRequestId` 建立唯一约束；旧命令保持 nullable，不伪造 request id。
- payload fingerprint 先解析 JSON、递归按属性名排序再计算 SHA-256。同 key、同语义 payload 返回原 command；同 key、不同 payload 返回 `409 StationCommandIdempotencyConflict`。
- 并发创建通过数据库唯一约束收敛为一个 command/audit；进程重启后仍按持久化 key 返回原 command。
- 新增受 `StationAdmin` policy 保护的 exact lookup endpoint。Next owner 在 network/abort/5xx/contract unknown 后保留同一 request id，先按 station、command type、client request id 读取后端权威，不按时间窗或 payload 猜测命令。
- 普通 command 与 DeployPackage POST 成功只进入“命令已创建”；Created/Delivered/Accepted/Running/terminal 保持既有中央状态机与 Station journal/spool 语义。
- authority lookup、commands list 与 Station poll 都会结算过期的 Created/Delivered 命令，因此离线 Station 不再使过期命令永久停留在非终态。

### 2.2 正式部署准入与不可变目标身份

- 部署 endpoint 在创建命令前验证 Station 存在、启用、在线、`Inspection` 角色、Runtime 空闲、Station 版本可比较且满足包最小版本。
- 只接受 `Production` package；package version、SHA-256、source project id/revision、flow hash 与 decision hash 必须完整。规则位于既有后端 endpoint/store，UI 不复制准入 authority。
- DeployPackage command payload 固化 packageId、version、kind、SHA、source project id/revision、flow hash、decision hash 与同源 download URL，形成部署创建时的不可变预期身份。
- Next 部署投影从 command payload 读取上述 7 字段，不再以之后可能变化的 package registry 记录作为成功比较基线。
- UI 只有在 command terminal 为 `Succeeded`、Station 在线且 active package 的 7 字段全部匹配时显示“部署完成”；否则分别显示命令已创建、执行中、非成功终态、身份待确认或回滚/身份不匹配。

### 2.3 Station 校验、激活与恢复

- Station 下载仍限制到已配置 Studio 同源，并校验下载制品 SHA-256。
- 解压后校验外层 Station manifest 的 packageId、kind、version、source project id/revision、flow hash、decision hash、SHA 与最小 Station 版本。
- `RuntimeHost.LoadPackageAsync` 后再次核对实际加载 Runtime manifest 的 packageId、version、source project id/revision、flow hash 与 decision hash，防止外层 manifest 与真正运行内容分叉。
- 激活失败继续恢复磁盘 `last-known-good`，并重新加载恢复后的 active package；成功后才更新本地 active package version/SHA。
- `RuntimeHostSnapshot`、Station settings、registration、heartbeat、snapshot、registry、central store 和 Vue decoder 贯通 active package version/SHA/source identity，不使用 UI 默认值补齐旧数据。
- virtual Station 测试从真实 exporter、包注册与下载开始，覆盖 command Created/Delivered/Accepted/Running/Succeeded、Station 部署/load 和中央 active identity 7 字段匹配；该证据明确为 virtual，不代表真实设备。

## 3. 独立审计修复

本轮独立审计发现并关闭两项会导致错误完成判断的缺口：

1. 部署 UI 最初使用当前 package registry 记录核对 active identity。若相同 packageId 的仓库记录在命令创建后变化，页面可能用新记录解释旧命令。现已改为使用 command payload 中冻结的 7 字段身份，并增加“包列表之后变化仍按命令身份核对”的回归测试。
2. 过期结算原先只发生在 Station polling。离线 Station 永不 poll，Created/Delivered 命令会无限保持非终态。现已在 exact lookup 和 commands list 权威读取中结算过期命令，并增加离线过期 endpoint 测试。

修复后重新执行 G3 定向、Desktop endpoint/services 回归、StudioUI 全量单测、生产构建与 Station Browser 功能场景，未发现新的 P0/P1 缺口。

## 4. 门禁证据

```text
DESKTOP_TESTS_BUILD=PASS (0 warnings, 0 errors)
STATION_BUILD=PASS (0 warnings, 0 errors)
DESKTOP_G3_FOCUSED=93/93 PASS
DESKTOP_ENDPOINTS_REGRESSION=422/422 PASS
SERVICES_REGRESSION=515/515 PASS (existing System.Collections.Immutable 8/9 resolution warnings)
STUDIOUI_TYPECHECK=PASS
STUDIOUI_LINT=PASS (0 warnings)
STUDIOUI_UNIT=752/752 PASS (122 files)
STUDIOUI_STATION_TARGETED=44/44 PASS
STUDIOUI_PRODUCTION_BUILD=PASS
BROWSER_PLAYWRIGHT_F02_FUNCTIONAL=4/4 PASS (STATIC CHROMIUM FIXTURE)
VIRTUAL_STATION_DEPLOYMENT=PASS
IMPECCABLE_STATIC_DETECTOR=PASS (no findings)
STATIC_AUTHORITY_AUDIT=PASS
GIT_DIFF_CHECK=PASS
```

Browser 功能场景覆盖部署命令创建与终态/active identity 区分、命令失败、身份不匹配和不可变命令身份。提交前 6 个截图证据用例因 `CV_F02_SOURCE_SHA` 必须是最终 40 位提交 SHA 而按 fixture 规则跳过；G3 提交后使用真实 SHA 单独捕获并记录，不把占位 SHA 作为证据。

### 4.1 UI 技术审计

| 维度 | 评分 | 结论 |
| --- | ---: | --- |
| 可访问性 | 4/4 | 原生标题/描述列表、可读状态文案与 `aria-live`；状态不只依赖颜色 |
| 性能 | 4/4 | 部署状态为纯 computed projection；无新增 timer、SSE、布局动画或重复请求 owner |
| 响应式 | 3/4 | 复用既有 1180px 单列收缩和可换行身份值；真实 WebView2/DPI 留待 G7 |
| 主题 | 4/4 | 全部使用现有 surface/text/border/spacing/status tokens，light/dark 合同不分叉 |
| 反模式 | 4/4 | detector 无 findings；无卡片套卡片、渐变、玻璃、装饰动效或第二套组件词汇 |
| **总计** | **19/20** | **Excellent；剩余 1 分是未执行真实 WebView2/DPI 证据，不是已知代码缺陷** |

## 5. 门禁逐条结论

| 门禁 | 结论 | 证据 |
| --- | --- | --- |
| 同 key 同 payload、异 payload、并发、重启、权限 | PASS | central store + endpoint tests；唯一索引与 canonical fingerprint |
| journal/spool 重启与重复投递不重复副作用 | PASS | G3 focused + services regression；既有 commandId journal/spool 保持唯一 owner |
| package source identity、SHA/manifest/version mismatch、rollback | PASS | package store/deployment tests + loaded Runtime identity 校验 |
| export/register/download/deploy/load/terminal/active identity | PASS | virtual Station end-to-end test |
| Admin-only 写入与 exact lookup | PASS | endpoint policy tests；Engineer/Operator 未扩权 |
| POST 与部署完成语义分离 | PASS | owner/projection unit + F02 Browser functional fixture |

## 6. 停止条件审计

- 同一 station、operation、client request key 不会创建两个语义不同命令；payload 不同 fail-closed 为 conflict。
- UI 不再使用 POST 200、timer 或可变 package registry 推断部署成功。
- active package 已投影 source revision、flow、decision、version 与 SHA；失败/rollback 不显示新包完成。
- 正式命令仍只通过 authenticated HTTP 和既有 Station 同步链；没有新增 WebMessage 执行通道。

因此 G3 停止条件均未触发，独立审计结论为 `PASS`。

## 7. 未执行证据边界

```text
REAL_WEBVIEW2=NOT RUN
WINDOWS_DPI_MATRIX=NOT RUN
RELEASE_PUBLISH=NOT RUN
NO_NODE_TARGET=NOT RUN
REMOTE_CI=NOT PERFORMED
REAL_STATION_CAMERA_PLC_TCP=NOT PERFORMED
```

静态 Chromium、TestServer 与 virtual Station 不能证明真实 WebView2、Windows DPI、现场网络、相机、PLC 或 TCP。Release publish、无 Node 启动、完整 CI 与真实现场继续留待 G7 分层证据准入。
