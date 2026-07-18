# Studio UI Next F04 完整开发计划

> 文档状态：**G1_CONTRACTS_FROZEN**
> 本计划已经结合 F03 最终实现、当前 `studio-ui-next` 分支代码事实、原 F04 初稿及独立复审意见修订。
> 产品负责人已批准 G0B，并在 G0 通过后批准进入 G1 合同冻结；G2–G6 产品实现仍未批准。

---

## 0. 权威状态与治理口径

```text
PLAN_NAME=Studio UI Next F04
PLAN_THEME=产品化入口、认证闭环、工程生命周期与受控切换
PLAN_STATUS=G1_CONTRACTS_FROZEN

CODE_AUDIT_BASE_SHA=b24d20b3531bdea66f0b9b73ba5e18827489eedf
PLAN_DOCUMENT_COMMIT_SHA=42a2c8811d97af2212fa2a3ec40ba7b86aab649e
STABLE_LINE_AUDIT_SHA=4386d8f3537e80084802567b41d96414b0ddacd0
F04_IMPLEMENTATION_BASE_SHA=64311b41d76f1d736fd879e6c28372ca7b8e9e3f
G1_GUARD_SHA=f2ff1f858c63f40b14993a5e9d4cdbb5e59eb394

F03_G6_STATUS=DONE
F03_STATUS=PARTIAL
F03_IMPLEMENTED=YES
F03_OPEN_EVIDENCE_GAPS=1

CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
BLOCKS_F04=NO
BLOCKS_G1=NO
BLOCKS_G2_TO_G6=NO
BLOCKS_NEXT_PILOT=NO
BLOCKS_DEFAULT_ENTRY_RECOMMENDATION=NO

F04_ENTRY_EXCEPTION=APPROVED
F04_ENTRY_APPROVED_BY=PRODUCT_OWNER
F04_ENTRY_APPROVED_AT=2026-07-18
F04_ENTRY_RATIONALE=F03功能、代码、WebView2、Release、DPI、远端CI及本机published进程树证据已闭合；独立无Node目标机验证暂缓且不是当前主要矛盾

F04_AUDIT_ENTRY=APPROVED
G0B_APPROVED=YES
G1_ENTRY_APPROVED_AFTER_G0B_PASS=YES
G2_TO_G6_IMPLEMENTATION=NO
G0_STATUS=DONE
G1_STATUS=DONE
F04_STARTED=YES

OFFICIAL_STUDIO_UI_DEFAULT=false
OFFICIAL_WORKSPACE_DEFAULT=false
USER_MACHINE_VALUES_COMMITTED_AS_DEFAULT=NO

LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO
```

### 0.1 不改写 F03 历史事实

F03 权威计划当前仍记录：

```text
F03_STATUS=PARTIAL
OPEN_EVIDENCE_GAPS=1
F04_ENTRY=REJECTED
```

F04 不得把未执行的独立无 Node 目标机验证改写为“已完成”，也不得在开发任务中自行把 F03 改为 `COMPLETE`。

本阶段采用明确治理例外：

```text
F04_ENTRY_EXCEPTION=APPROVED
```

其含义是：

- F03 的核心工程实现与 G6 已闭环；
- 独立无 Node 目标机验证继续保留为真实未执行证据；
- 产品负责人批准该证据暂缓且不阻塞 F04；
- F04 可以进入 G0 审计；
- F03 历史记录不被伪造或重写。

### 0.2 工作区治理结果

G0B 已按产品负责人授权治理进入时的既有修改：

| 文件 | 原始状态 | 处置 |
| --- | --- | --- |
| `CLAUDE.md` | 分支级 Studio UI Next 红线与仓库文档入口修正 | 审计无密钥、账号、内部地址或机器路径后，以独立 housekeeping 提交纳入仓库 |
| `.codex/config.toml` | 0 字节未跟踪空文件 | 无仓库配置价值，删除；未提交机器值、provider 或密钥 |
| `appsettings.json` | 本机 `StudioUiEnabled=true`、`WorkspaceCapabilityEnabled=false` | 仅为本地入口覆盖；正式文件恢复 `false/false`，未将本机值提交为默认值 |
| F04 计划 | 未跟踪 | 由 G0 文档权威提交正式纳入 Git |

后续本地启用 Next 只允许使用进程环境变量、隔离配置或 runner 参数。正式默认值保持：

```text
Studio:StudioUiEnabled=false
Studio:WorkspaceCapabilityEnabled=false
```

---

# 1. 阶段定位

F03 已经完成 flag-gated Workspace 的核心视觉工程链：

```text
打开工程
→ 编辑流程
→ 参数与 ROI
→ Preview
→ Save
→ Formal Run
→ Stop / Reconcile
→ Results
```

F04 的主要矛盾不再是继续扩建 Canvas、Preview 或 Formal Run，而是：

> Studio UI Next 已有核心工程能力，但仍缺少真实认证入口、可靠的工程创建与删除合同、完整产品导航治理，以及可审计、可回滚的默认入口候选机制。

F04 的目标用户旅程为：

```text
启动 ClearVision
→ 首次管理员初始化或登录
→ 浏览并创建工程
→ 打开工程工作区
→ 编辑、预览、保存与正式运行
→ 查看检测结果
→ 安全返回、退出与重启
→ 再次登录并恢复同一工程
```

F04 完成不自动等于默认入口切换，更不等于 Legacy 退役。

---

# 2. 总体架构红线

## 2.1 单一业务权威

继续复用现有权威：

```text
AuthService
ProjectService
ProjectSaveCoordinator
InspectionService
InspectionRuntimeCoordinator
InspectionResultRepository
```

前端只允许保存：

- 会话投影；
- 页面状态；
- UI draft；
- 可丢弃查询缓存；
- 明确不构成业务权威的临时状态。

禁止新增第二套：

- 用户或 token authority；
- Project / Flow store；
- Runtime tree；
- Result repository；
- HTTP client；
- EventBus；
- ServiceRegistry；
- Legacy / Next 双写同步器。

## 2.2 Auth owner 不得双轨并存

当前已经存在 `sessionProjectionOwner`，负责读取预置 token 对应的 `/api/auth/me` 投影。

F04 不得在旁边长期并存第二个认证 owner。目标拓扑必须是：

```text
authLifecycleOwner
  ├─ 吸收或替换 sessionProjectionOwner
  ├─ 唯一 token read / set / remove writer
  ├─ 唯一 setup / login / logout / change-password / session-expired 状态机
  └─ 通过直接回调驱动 ProductRuntime 挂载或卸载
```

必须增加 source/import guard，证明运行时不存在两个 session/auth owner。

## 2.3 Project 生命周期必须先闭合合同

当前 Project 生命周期存在真实缺口：

- 创建没有 idempotency/client operation identity；
- 创建先写数据库，再单独保存 Flow，存在部分成功语义；
- 删除没有 expected revision；
- 删除先标记，再清 Flow/assets，清理失败结果可能不确定；
- `/api/projects/recent` 已存在，但没有确认 `Project.RecordOpen()` 的真实调用链；
- 没有专用“从模板创建”合同；
- Update/Delete 的不存在语义没有稳定统一为 404。

因此禁止：

```text
发现合同不足
→ 前端先实现
→ 临时新增 endpoint
```

必须采用：

```text
G3A 报告并批准合同决策
→ G3B 实施批准的后端 hardening
→ G3C 建立前端 command owner 与 UX
```

## 2.4 启动入口能力以回归保护为主

现有 `StudioStartupPageResolver` 已具备：

- Next assets 完整性检查；
- assets 缺失时进入诊断页；
- 禁止静默回退 Legacy。

该能力标记为：

```text
EXISTING_AND_MUST_PRESERVE
```

F04 不重做第二套 resolver，不把已有能力包装成新增交付。

## 2.5 Flag 名称必须精确区分

```text
配置项：Studio:WorkspaceCapabilityEnabled
启动注入：featureFlags["Studio2.Workspace"]
计划简称：Workspace flag
```

禁止把三个名称混写为多个不同开关。

---

# 3. 非目标

F04 不包含：

- Legacy 正式删除或退役；
- Station SSE、部署、命令或现场控制；
- 连续实时检测页面；
- PLC、机器人、相机现场联调；
- Settings 全量迁移；
- AI Agent 页面迁移；
- GlobalVariables 完整管理页；
- 标定资产工作台扩展；
- Results 图像、ROI、Evidence 深度复核；
- OperatorLibrary、算法或 Runtime 重构；
- 独立无 Node 目标机验证。

独立无 Node 目标机验证固定标记为：

```text
ACCEPTED_DEFERRED_EVIDENCE
BLOCKS_F04=NO
BLOCKS_DEFAULT_ENTRY=NO
```

是否阻塞未来现场发布，由后续 Release / Field Readiness 阶段重新评审。

---

# 4. 执行顺序

严格串行：

```text
G0 进入批准、脏工作树、稳定线与文档权威
→ G1 parity / route / permission / HTTP / owner / contract matrix
→ G2 Auth lifecycle
→ G3 Project contract + lifecycle
→ G4 Shell / navigation / leave guard / visual
→ G5 profiles / entry / rollback
→ G6 isolated end-to-end evidence and decision
```

G0 完成前：

```text
G1_TO_G6_IMPLEMENTATION=FORBIDDEN
```

每个 Goal 未通过自己的合同、测试和证据门禁，不得进入下一个 Goal。

---

# 5. G0：进入审计与实施基线冻结

## 5.1 目标

在不改写历史事实、不污染用户工作树、不遗漏稳定线新修复的前提下，建立唯一可执行 F04 基线。

## 5.2 G0A：只读进入审计

必须执行：

1. `git fetch origin --prune`。
2. 确认：
   - 当前工作树；
   - 当前分支；
   - HEAD；
   - upstream；
   - local / tracking / remote；
   - ahead / behind；
   - 是否存在分叉。
3. 记录保护文件状态，但不得把保护文件内容作为实施输入。
4. 分别记录：
   - HEAD 正式 flags；
   - 用户本地 override；
   - 隔离测试 flags。
5. 获取 `origin/codex初稿` 当前真实 SHA。
6. 不得继续沿用 F03 的旧稳定线审计 SHA `dfa5ea1e`。
7. 审计从：

```text
dfa5ea1ef3d100e700a19cffea5ae64006648881
```

到稳定线当前 SHA 的全部提交，重点检查：

- `InspectionService`；
- Flow execution；
- execution snapshot identity；
- Result image / history；
- Project persistence；
- Auth / session；
- API error mapping；
- Desktop endpoint；
- CI 与证据脚本。

8. 每个稳定线提交分类：

```text
MUST_SEMANTICALLY_SYNC_BEFORE_F04
ALREADY_EQUIVALENT
DEFER_WITH_EXPLICIT_RATIONALE
CONFLICT_REQUIRES_DECISION
OUT_OF_SCOPE
```

## 5.3 G0B：批准后的语义同步

G0A 输出差异矩阵后，由主协调者批准：

- 哪些提交语义合入；
- 哪些已被 Next 等价覆盖；
- 哪些明确延期；
- 哪些冲突需要独立处理。

禁止：

- 未审计整分支 merge；
- 手工复制文件代替 Git 合并；
- 回滚 Next 既有修复；
- 在保护工作树中强行清理用户修改。

同步完成后冻结：

```text
CODE_AUDIT_BASE_SHA=b24d20b3531bdea66f0b9b73ba5e18827489eedf
PLAN_DOCUMENT_COMMIT_SHA=<计划首次受控提交 SHA>
STABLE_LINE_AUDIT_SHA=4386d8f3537e80084802567b41d96414b0ddacd0
F04_IMPLEMENTATION_BASE_SHA=64311b41d76f1d736fd879e6c28372ca7b8e9e3f
```

任何后续修复产生新 SHA，都必须更新 evidence source。

## 5.4 文档权威修正

G0 必须修复文档漂移，但不得虚构证据：

- F03 保持 `PARTIAL`；
- 写入 F04 entry exception；
- `StudioUINext/README.md` 不得继续声称 F03 未实施；
- F02 的 `VISUAL_CONFIRMATION=AWAITING_USER` 标记为历史 F02 状态；
- F04 建立独立产品视觉确认门禁；
- 本文成为 F04 唯一计划权威。

## 5.5 G0 交付

- G0 审计报告；
- stable-line commit matrix；
- protected-worktree manifest；
- documentation drift list；
- 初始 F04 blocker registry；
- 冻结后的 `F04_IMPLEMENTATION_BASE_SHA`；
- 修订为 `READY_FOR_APPROVAL` 的 F04 计划。

## 5.6 G0 门禁

```text
G0_STATUS=DONE
WORKTREE_PROTECTED=PASS
WORKTREE_CLEAN=PASS
STABLE_LINE_AUDIT=PASS
STABLE_LINE_SYNC=PASS
DOCUMENT_AUTHORITY=PASS
NO_NODE_DISPOSITION=RESOLVED_NON_BLOCKING
F04_IMPLEMENTATION_BASE_SHA=64311b41d76f1d736fd879e6c28372ca7b8e9e3f
G1_ENTRY=APPROVED_FOR_CONTRACT_FREEZE
```

本轮产品负责人已条件批准 G1；G0 通过后无需再次等待。该批准只覆盖合同、边界、产品策略与架构守卫，不覆盖 G2–G6 产品实现。

## 5.7 Stable-line disposition ledger

| 稳定线提交 | 处置 | G0 结论 |
| --- | --- | --- |
| `4386d8f3` | `MUST_SEMANTICALLY_SYNC_BEFORE_F04` | 已人工重放 Flow acquisition 源图快照、InspectionService/InspectionWorker 结果图回退及等价测试；未带入 Legacy 登录 CSS/E2E |
| `07ff5ede` | `DEFER_WITH_EXPLICIT_RATIONALE` | Legacy operator palette 1080P 布局修复；G4 统一产品 Shell/Workspace 视觉阶段再审计，避免把 Legacy CSS 直接复制到 Next |
| `c17a30ff` | `DEFER_WITH_EXPLICIT_RATIONALE` | 相机 provider 探测属于设备集成语义，不是 G0/G1 合同冻结 blocker；后续相机/现场 readiness 单独同步与验证 |
| `2eacc62d` | `DEFER_WITH_EXPLICIT_RATIONALE` | Legacy 单帧预览/ROI owner 改动与 F03 Next Workspace owner 边界高度耦合；留到 G4/后续 capability 专项语义审计，不复制 Legacy 实现 |
| `4403fee0` | `DEFER_WITH_EXPLICIT_RATIONALE` | Legacy 1080P E2E 只验证旧界面；Next 视觉与真实 WebView2/DPI 证据在 G4/G6 独立建立 |
| `b7667b01` | `OUT_OF_SCOPE` | 旧平面标定向导与 Legacy 布局，不属于 F04 产品入口/Auth/Project 合同范围 |

```text
MUST_SYNC_COUNT=1
MUST_SYNC_CLOSED=YES
DEFERRED_COUNT=4
OUT_OF_SCOPE_COUNT=1
```

## 5.8 独立无 Node 目标机 waiver / disposition

```text
APPROVED_BY=PRODUCT_OWNER
APPROVED_AT=2026-07-18
RATIONALE=F03核心代码、WebView2、Release、DPI、远端CI及本机published进程树证据已闭合；独立无Node目标机验证不是F04当前主要矛盾

CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```

该验证历史上未执行，F03 因而继续为 `PARTIAL` 且保留唯一 evidence gap。不得伪造 clean-machine、field 或 no-Node PASS；在整个 F04 中该项固定为 `NOT_PERFORMED / DEFERRED_NON_BLOCKING`，不阻塞 F04、G1、后续实现、pilot 或默认入口建议。未来 Release/Field Readiness 是否重新要求，由未来阶段另行决策。

---

# 6. G1：产品任务、路由、权限、HTTP、Owner 与合同矩阵

## 6.1 目标

在实现 Auth 与 Project UI 前，冻结默认产品入口到底暴露什么、谁能操作、调用哪些 endpoint、由哪个 owner 负责。

## 6.2 产品任务 parity matrix

逐项审计 Legacy 与 Next：

- 首次管理员初始化；
- 登录；
- session 恢复；
- session 过期；
- 修改密码；
- 退出；
- Overview；
- 工程列表；
- 最近工程；
- 创建工程；
- 修改工程摘要；
- 打开 Workspace；
- 保存；
- Preview；
- Formal Run；
- Stop / Reconcile；
- Results；
- 删除工程；
- 应用关闭与恢复；
- Legacy 回滚。

每项必须标记：

```text
PARITY
NEXT_BETTER
READ_ONLY_ACCEPTED
HIDDEN_IN_DEFAULT_PROFILE
DEFERRED_WITH_REASON
BLOCKS_DEFAULT_ENTRY
```

## 6.3 可见路由审计

当前导航包含：

```text
Overview
Projects
Operators
Stations
Results
Diagnostics
About
```

G1 必须为每个页面决定：

```text
DEFAULT_VISIBLE
ROLE_RESTRICTED
READ_ONLY_VISIBLE
HIDDEN_BY_PRODUCT_PROFILE
INTERNAL_ONLY
DEFERRED
```

建议初始策略：

- Overview：默认可见；
- Projects：默认可见；
- Operators：只读可见，但必须通过合同与视觉审计；
- Results：若 scalar/history 合同稳定则可见，否则 profile 限制；
- Stations：不得因页面已存在而自动进入默认入口；
- Diagnostics：按角色限制；
- About：默认可见；
- Labs：内部专用，不进入产品导航。

## 6.4 Route guard 冻结

当前只有 `requiresSession` 元数据，尚无真实全局 route guard。

G1 必须冻结：

- public routes；
- setup route；
- login route；
- protected product routes；
- internal lab routes；
- forbidden；
- not-found；
- safe return route；
- role-restricted route。

禁止任意外部 redirect。登录恢复地址必须是经过验证的内部 route。

## 6.5 权限矩阵

按现有后端 permission policy 审计：

```text
Admin
Engineer
Operator
Unauthenticated
```

每个 GET / POST / PUT / DELETE 必须记录：

- 后端 policy；
- 前端可见性；
- disabled / hidden 语义；
- 401；
- 403；
- 404；
- 409；
- unknown outcome。

前端隐藏按钮不构成授权。

## 6.6 HTTP allowlist

按 capability 冻结：

- Auth；
- Project read；
- Project lifecycle command；
- Workspace Save；
- Preview；
- Formal Run；
- Results；
- Diagnostics。

未列入 allowlist 的新方法不得进入 F04。

## 6.7 Owner topology matrix

必须列出：

- owner 名称；
- composition root；
- mount 条件；
- authority；
- dispose；
- AbortController；
- timer / subscription；
- late-response guard；
- 与 Legacy 的关系。

重点冻结：

```text
authLifecycleOwner = 唯一 Auth / Session owner
projectLifecycleCommandOwner = 唯一 Project 创建 / 更新 / 删除 command owner
workspaceOwner = F03 唯一 Workspace authority
```

## 6.8 G1 门禁

- 路由、权限、HTTP、owner、合同矩阵无空项；
- 所有可见页面有明确产品策略；
- 没有默认暴露未验收页面；
- 没有第二 Auth、Project 或 HTTP 基础设施；
- G3 合同缺口进入正式 decision register。

G1 冻结结果与全部矩阵见 [F04 G1 产品合同与边界冻结](./F04_G1_产品合同与边界冻结.md)。该记录包含 parity、visible route、route guard、permission、HTTP allowlist、owner topology、Project decision register 与 architecture guard 清单。

```text
G1_STATUS=DONE
G1_MATRICES_COMPLETE=YES
G1_ARCHITECTURE_GUARDS=ADDED
G2_TO_G6_IMPLEMENTED=NO
```

---

# 7. G2：真实 Auth 与 Session 生命周期

## 7.1 目标

用一个 Auth 状态机替换“只能依赖预置 token”的产品限制。

后端现有合同：

```text
GET  /api/auth/setup-status
POST /api/auth/setup-admin
POST /api/auth/login
POST /api/auth/logout
GET  /api/auth/me
POST /api/auth/change-password
```

F04 复用这些 endpoint，不新建认证服务。

## 7.2 唯一 Auth 状态机

```text
checking-setup
setup-required
unauthenticated
authenticating
authenticated
stale
expired
changing-password
logging-out
protected-transition
error
disposed
```

`authLifecycleOwner` 必须：

- 吸收或替换现有 `sessionProjectionOwner`；
- 成为 token 唯一 set/remove writer；
- 统一 `/auth/me` projection；
- 管理 session generation；
- 直接接收共享 `ApiTransport` 的 401 callback；
- 不通过 EventBus 广播 401；
- 不允许多个并发 401 重复创建登录 owner。

## 7.3 Token port

在 `StudioPlatform` 建立唯一窄接口：

```text
readToken()
setToken()
removeToken()
```

约束：

- 不写 localStorage；
- 默认延续 sessionStorage；
- 所有 token 写入只能来自 `authLifecycleOwner`；
- `ApiTransport` 只读取，不写入；
- 测试 fake 复用同一接口；
- import guard 阻止其他模块直接调用 auth token key。

## 7.4 Composition root

冷启动未认证时：

```text
Auth Shell mounted
ProductRuntime not mounted
Workspace owner count = 0
```

认证成功后才创建 ProductRuntime。

## 7.5 首次管理员初始化

采用现有后端语义：

```text
setup-admin 成功
→ 服务端返回 token
→ 自动登录
→ GET auth/me 复核
→ 挂载 ProductRuntime
```

不得要求用户再重复登录一次。

## 7.6 修改密码

修改密码成功后必须：

```text
clear local token
→ dispose ProductRuntime
→ route to login
→ 提示使用新密码重新登录
```

不得继续显示旧 authenticated projection。

## 7.7 Logout、密码修改与 session invalidation

主动 session mutation 前必须：

1. 调用现有 Save / Run leave / reconcile 保护；
2. 未能权威 settle 时阻止主动 logout/change-password；
3. settle 后执行后端命令；
4. 清 token；
5. dispose ProductRuntime；
6. 进入登录页。

已认证会话运行中收到 401 时：

- 共享 transport 只触发一次直接 callback；
- 冻结新 mutation；
- 不创建第二 owner；
- 若存在 Formal Run unknown outcome，不得猜测已停止；
- 重新认证后使用原 identity reconcile；
- reconcile 后再恢复或重建 ProductRuntime。

该路径必须有独立 ADR 和测试。

## 7.8 真实 route guard

实现：

- setup-required 只允许 setup；
- unauthenticated 只允许 login/setup；
- authenticated 禁止回到 setup；
- protected route 未认证跳 login；
- 登录成功恢复安全内部地址；
- role policy 不满足进入 forbidden；
- logout 后 browser back 不得恢复受保护页面。

## 7.9 G2 证据

```text
fresh database
→ setup-status
→ setup-admin auto-login
→ auth/me
→ refresh session recovery
→ change password
→ token cleared
→ old password rejected
→ new password login
→ active save/run leave protection
→ logout
→ protected route blocked
```

必须覆盖：

- Unit；
- backend focused；
- Browser；
- 真实 WebView2；
- 401 burst；
- late response；
- owner ledger。

---

# 8. G3：Project 合同与生命周期

## 8.1 G3A：Project lifecycle contract decisions

G3A 只提交：

- 合同审计；
- ADR；
- 推荐方案；
- 测试草案；
- 主协调者审批记录。

不得直接实现 UI。

### D-PROJECT-01：Create idempotency

推荐：

```text
clientOperationId
+ server-side operation/result record
+ deterministic retry/reconcile
```

必须证明：

- POST 响应丢失后可查询权威结果；
- 相同 operation id 不创建两个工程；
- 不同用户不能读取或复用他人的 operation；
- operation record 与 ProjectId 绑定。

禁止通过名称搜索猜测创建结果。

### D-PROJECT-02：Create 部分成功

当前顺序是：

```text
DB Add Project
→ optional Flow file save
```

必须批准一种明确语义：

- staging + finalize；
- operation journal + resumable completion；
- compensation；
- 或将 F04 创建限定为空白工程，并保证空白 Flow 是服务端可恢复默认。

禁止继续保留“数据库成功、Flow 写失败、客户端只收到 400”的未知结果。

### D-PROJECT-03：Delete revision 与结果语义

必须决定：

- expected persistence revision；
- 运行中/保存中冲突；
- tombstone 权威时点；
- Flow/assets cleanup 是同步、异步还是 retryable；
- cleanup 失败后 GET/list/detail 语义；
- 网络响应丢失后的 reconcile；
- 二次 DELETE 的幂等结果。

### D-PROJECT-04：Not Found mapping

统一：

```text
ProjectNotFoundException → 404
Revision conflict → 409
Validation → 400 / 422（按现有规范冻结）
Unknown server outcome → 可 reconcile
```

不得让 UI 依赖解析异常字符串。

### D-PROJECT-05：Recently opened

确认 `RecordOpen()` 的真实 authority。

推荐新增显式、可审计 open command，例如：

```text
POST /api/projects/{id}/open
```

该命令不得修改 PersistenceRevision。

### D-PROJECT-06：Template create

默认建议：

```text
DEFERRED_WITH_REASON
```

除非 G3A 证明现有模板 authority、版本、Flow schema 和创建事务均可安全复用。

“从模板创建”不得作为 F04 默认入口 blocker。

## 8.2 G3A 批准门禁

主协调者和产品负责人必须批准：

- endpoint；
- request/response；
- operation identity；
- error code；
- permission；
- revision；
- reconcile；
- cleanup；
- backward compatibility。

未批准时：

```text
G3B_ENTRY=REJECTED
```

## 8.3 G3B：Approved backend hardening

只实现 G3A 已批准合同。

要求：

- 复用 `ProjectService`、`ProjectSaveCoordinator` 和既有 repository；
- 不新增第二 Project service；
- operation journal 若新增，必须有清理、权限和生命周期；
- Create/Delete/Update 返回结构化 error code；
- 不存在统一 404；
- mutation lease 覆盖运行态；
- 并发与响应丢失测试；
- Legacy/API compatibility 回归。

## 8.4 G3C：Frontend command owner and UX

建立唯一：

```text
projectLifecycleCommandOwner
```

状态：

```text
idle
creating
updating
deleting
reconciling
conflict
unknown-outcome
succeeded
failed
disposed
```

规则：

- Create 使用 `clientOperationId`；
- 响应未知时 reconcile，不盲重试；
- 创建成功后只使用服务端返回 ProjectId；
- Delete 不做不可逆 optimistic success；
- Update/Delete 使用 server-issued revision；
- Project A 晚到响应不得污染 Project B；
- 删除当前工程前经过 Workspace leave protection；
- 不复制 read-query cache；
- recent-open 使用批准合同。

## 8.5 G3 用户旅程

```text
create
→ response-loss reconcile
→ list
→ detail
→ explicit open
→ workspace
→ rename
→ save
→ close/reopen
→ delete with expected revision
→ list/detail not-found
```

---

# 9. G4：Shell、导航、Leave Guard 与视觉闭环

## 9.1 目标

把现有页面集合整理成连贯产品任务流，并关闭旧文案、路由和视觉状态漂移。

## 9.2 Shell 语义

当前 Shell 仍存在“只读工作区”文案，与 F03 已支持 Save/Formal Run 的事实冲突。

必须改为：

- 前端是可编辑工程工作台；
- 正式保存与运行由后端权威链负责；
- 不暗示页面本身只读；
- 不暗示 Preview 成功等于 Formal Run 可运行。

## 9.3 导航策略

按 G1 决策：

- 隐藏未批准页面；
- role-restricted 页面按权限显示；
- read-only 页面明确标识；
- internal labs 不进入产品导航；
- 直接访问受限 URL 仍由 route guard 和后端权限保护。

## 9.4 工程任务入口

Projects：

- 新建工程；
- 直接打开 Workspace；
- 查看详情；
- 删除入口；
- 最近工程。

Project Detail：

- 打开 Workspace；
- 编辑名称/描述；
- 删除；
- 返回列表。

Workspace：

- 当前工程和 revision；
- Save / Run 状态；
- 返回详情/列表；
- Results handoff；
- session transition 提示。

## 9.5 Leave Guard

统一保护：

- dirty draft；
- saving；
- conflict；
- admitting；
- executing；
- cancel-requested；
- unknown-outcome；
- create/delete unknown outcome；
- logout；
- change-password；
- project switch；
- route leave；
- host close。

不得为 Auth 或 Project lifecycle 再建第二套离开保护。

## 9.6 视觉与可访问性

必须完成：

- 1366×768；
- 1600×1000；
- DPI 1.0 / 1.25 / 1.5 / 2.0；
- keyboard-only；
- focus restoration；
- destructive confirmation；
- error / empty / loading / stale / forbidden；
- login/setup/project/workspace/results 统一设计语言。

禁止使用 Computer Use 操作用户屏幕。证据使用现有 Browser、Playwright、WebView2 runner 和截图脚本。

## 9.7 F02 视觉状态接续

F02 的：

```text
VISUAL_CONFIRMATION=AWAITING_USER
```

保留为历史状态。

F04 新建：

```text
F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
```

只有 F04 最终真实产品页面获得用户确认，才能推荐正式默认入口 `APPROVE`。

视觉确认可以不阻塞受控 pilot，但阻塞默认入口推荐。

---

# 10. G5：Profiles、启动入口与回滚

## 10.1 目标

形成可控启用、可诊断、可回滚的 Next 产品 profile。

## 10.2 已有能力回归保护

以下能力标记为：

```text
EXISTING_AND_MUST_PRESERVE
```

包括：

- `StudioStartupPageResolver` asset completeness；
- missing asset diagnostic page；
- no silent fallback to Legacy。

F04 只补充回归、profile 与运维证据，不重做实现。

## 10.3 精确真值表

| `Studio:StudioUiEnabled` | `Studio:WorkspaceCapabilityEnabled` | 注入 `Studio2.Workspace` | 预期 |
|---|---:|---:|---|
| false | false | false | Legacy 启动；Next 不挂载 |
| false | true | true/注入值可存在 | Legacy 启动；Next owner=0，不得产生副作用 |
| true | false | false | Next Shell；Workspace 入口不可用或明确 flag-off |
| true | true | true | Next 完整候选产品链 |

真值表必须使用隔离配置，不修改用户 `appsettings.json`。

## 10.4 Profiles

至少建立：

```text
LEGACY_DEFAULT
NEXT_PILOT
NEXT_FULL_CANDIDATE
```

要求：

- profile 使用进程级环境变量或隔离配置；
- 不提交用户机器专用值；
- 启动时记录 profile、flags、source SHA、asset root；
- 配置切换必须重启；
- 同一进程不得同时挂载 Legacy 与 Next root。

## 10.5 回滚演练

```text
NEXT_PILOT
→ 登录
→ 创建工程
→ Workspace 保存与运行
→ 关闭
→ LEGACY_DEFAULT
→ 登录并读取同一工程/结果
→ 关闭
→ NEXT_PILOT
→ revision、Flow、Result identity 一致
```

门禁：

- 不做数据库迁移；
- 不丢 Project/Flow/Result；
- 不产生双用户 session；
- 不产生双 Project authority；
- 不静默切入口；
- Legacy 暂不删除。

---

# 11. G6：隔离 E2E、Final Closure 与决策包

## 11.1 Final-SHA 用户旅程

在隔离数据库和最终代码 SHA 上执行：

```text
fresh database
→ setup-admin auto-login
→ Overview
→ create project
→ list/detail/open
→ Workspace
→ add/configure operator
→ Preview
→ Save
→ Formal Run
→ Results
→ return Workspace
→ modify and save again
→ logout
→ restart
→ login
→ reopen same project
→ delete
→ verify not-found
```

## 11.2 验证矩阵

- lint；
- typecheck；
- StudioUI unit；
- Auth focused backend；
- Project lifecycle focused backend；
- Product / Runtime focused；
- Desktop endpoint regression；
- Browser fixture；
- 真实 WebView2；
- Release publish；
- sanitized path；
- startup truth table；
- DPI；
- keyboard/focus；
- 20-cycle auth/project/workspace/run/logout；
- Next→Legacy→Next rollback；
- remote CI；
- Final Gate；
- product visual confirmation。

独立无 Node 目标机继续：

```text
NOT_PERFORMED
DEFERRED_NON_BLOCKING
```

不得重新升级为 F04 blocker。

## 11.3 证据分类

分别记录：

```text
Unit
Backend integration
Browser fixture
Real WebView2
Release
DPI
Rollback
Remote CI
Visual confirmation
Field / clean-machine
```

不得用一种证据替代另一种。

## 11.4 三态收口模型

### A. 完成并建议默认入口

```text
F04_STATUS=COMPLETE
F04_IMPLEMENTED=YES
NEXT_DEFAULT_ENTRY_RECOMMENDATION=APPROVE
F04_PRODUCT_VISUAL_CONFIRMATION=PASS
LEGACY_RETIREMENT=NOT_APPROVED
```

该状态仍需产品负责人最终批准修改正式默认值。

### B. 实现完成但暂缓默认入口

```text
F04_STATUS=COMPLETE
F04_IMPLEMENTED=YES
NEXT_DEFAULT_ENTRY_RECOMMENDATION=DEFER
NEXT_PILOT_PROFILE_AVAILABLE=YES
LEGACY_RETIREMENT=NOT_APPROVED
```

适用于：

- 产品策略暂缓；
- 需要更长 pilot；
- 视觉确认尚未批准默认入口；
- 但代码与计划门禁均已闭合。

### C. 尚未准备好

```text
F04_STATUS=PARTIAL
F04_IMPLEMENTED=PARTIAL
NEXT_DEFAULT_ENTRY_RECOMMENDATION=NOT_READY
LEGACY_RETIREMENT=NOT_APPROVED
```

只要存在功能、Auth、Project 合同、入口回滚或 Final-SHA 证据 blocker，就必须使用该状态，不得包装为 COMPLETE。

---

# 12. F04 Blocker Registry

```text
F04-B00-G0-NOT-CLOSED
F04-B01-STABLE-LINE-UNAUDITED
F04-B02-PROTECTED-WORKTREE-TOUCHED
F04-B03-DOCUMENT-AUTHORITY-DRIFT

F04-B10-AUTH-DUAL-OWNER
F04-B11-TOKEN-MULTI-WRITER
F04-B12-ROUTE-GUARD-MISSING
F04-B13-401-LOOP
F04-B14-SESSION-TRANSITION-BYPASSES-RUN-SAVE-PROTECTION

F04-B20-PROJECT-CREATE-NON-IDEMPOTENT
F04-B21-PROJECT-CREATE-PARTIAL-SUCCESS
F04-B22-PROJECT-DELETE-NO-REVISION
F04-B23-PROJECT-DELETE-UNKNOWN-CLEANUP
F04-B24-PROJECT-NOT-FOUND-SEMANTICS
F04-B25-RECENT-PROJECT-NO-OPEN-AUTHORITY

F04-B30-VISIBLE-ROUTE-UNAUDITED
F04-B31-SHELL-SEMANTIC-DRIFT
F04-B32-VISUAL-CONFIRMATION-MISSING
F04-B33-LEAVE-GUARD-BYPASS

F04-B40-SILENT-LEGACY-FALLBACK
F04-B41-DOUBLE-ROOT
F04-B42-NEXT-LEGACY-DATA-DIVERGENCE
F04-B43-ROLLBACK-DATA-LOSS
F04-B44-FLAG-NAME-MIXED

F04-B50-FINAL-SHA-EVIDENCE-MISMATCH
F04-B51-REMOTE-CI-NOT-GREEN
F04-B52-SCOPE-CREEP
```

以下不是 F04 blocker：

```text
CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```

---

# 13. 开发、提交与协作策略

## 13.1 基线

```text
WORKTREE=C:\Users\HerverJun\Desktop\ClearVision-UI-Next
BRANCH=studio-ui-next
CODE_AUDIT_BASE_SHA=b24d20b3531bdea66f0b9b73ba5e18827489eedf
IMPLEMENTATION_BASE=64311b41d76f1d736fd879e6c28372ca7b8e9e3f
```

## 13.2 串行提交

每个 Goal 至少拆分：

```text
contract / ADR / guard
backend capability
frontend capability
tests / evidence
docs
```

G3A 合同决策必须与 G3B 实现分离。

## 13.3 Shared file 单 owner

以下模块只能由主协调者集成：

- router；
- navigation；
- product runtime；
- studio platform；
- startup contracts；
- WebView2Host；
- Auth owner；
- Project lifecycle owner；
- API transport 401 callback；
- feature flags；
- CI；
- 权威计划和 README。

## 13.4 禁止事项

- force push；
- 回滚其他 Agent 的无关修改；
- 未审计整分支 merge；
- 同时运行会争用相同输出目录的测试；
- 提交 `.tmp`、publish、数据库、截图、日志、WebView2 user data 或 Node dependencies；
- 修改用户保护文件；
- 使用 Computer Use 操作用户屏幕；
- 为测试放宽 decoder、permission、identity 或错误处理；
- 复制第二套 Auth、Project、Runtime、Result 或 HTTP 基础设施。

最多使用 3 个子代理。

---

# 14. Goal 状态模板

```text
F04_G0_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G1_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G2_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G3A_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G3B_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G3C_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G4_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G5_STATUS=NOT_STARTED | DONE | BLOCKED
F04_G6_STATUS=NOT_STARTED | DONE | BLOCKED
```

每轮汇报必须包含：

- Initial / Final / tracking / remote SHA；
- 实际 diff；
- 本轮 owner；
- 合同变化；
- 本地与远端测试；
- Browser / WebView2 / Release / DPI 分类；
- protected files 状态；
- 未执行项；
- blocker 数量；
- 是否批准进入下一 Goal。

---

# 15. F04 最终状态模板

```text
PLAN_STATUS=IMPLEMENTED | PARTIAL | BLOCKED

F03_G6_STATUS=DONE
F03_STATUS=PARTIAL
F03_OPEN_EVIDENCE_GAPS=1

CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
F04_ENTRY_EXCEPTION=APPROVED

CODE_AUDIT_BASE_SHA=
PLAN_DOCUMENT_COMMIT_SHA=
STABLE_LINE_AUDIT_SHA=
F04_IMPLEMENTATION_BASE_SHA=
F04_FINAL_CODE_SHA=
F04_FINAL_DELIVERY_SHA=

F04_STATUS=COMPLETE | PARTIAL
F04_IMPLEMENTED=YES | PARTIAL

OPEN_FUNCTIONAL_BLOCKERS=
OPEN_AUTH_BLOCKERS=
OPEN_PROJECT_CONTRACT_BLOCKERS=
OPEN_PROJECT_LIFECYCLE_BLOCKERS=
OPEN_NAVIGATION_BLOCKERS=
OPEN_ENTRY_SWITCH_BLOCKERS=
OPEN_VISUAL_BLOCKERS=
OPEN_DELIVERY_GAPS=

F04_PRODUCT_VISUAL_CONFIRMATION=PASS | AWAITING_USER | REJECTED
NEXT_DEFAULT_ENTRY_RECOMMENDATION=APPROVE | DEFER | NOT_READY
NEXT_PILOT_PROFILE_AVAILABLE=YES | NO

Studio:StudioUiEnabled=false
Studio:WorkspaceCapabilityEnabled=false
LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO
```

正式默认值在产品负责人批准前继续保持 false。即使最终建议为 `APPROVE`，开发 Agent 也不得自行修改正式默认入口。

---

# 16. 当前批准边界

当前只批准：

```text
G0B_APPROVED=YES
G1_ENTRY_APPROVED_AFTER_G0B_PASS=YES
G2_TO_G6_IMPLEMENTATION=NO
```

当前流程为：

```text
完成并提交 G0 稳定线同步、测试与文档权威冻结
→ 进入 G1 产品合同与架构守卫冻结
→ 停止，不实现 G2–G6
```

---

# 17. 本次计划编制证据状态

本计划编制基于只读代码与文档审计。

```text
BUILD=NOT_RUN
UNIT_TEST=NOT_RUN
BACKEND_TEST=NOT_RUN
PLAYWRIGHT=NOT_RUN
WEBVIEW2=NOT_RUN
DPI=NOT_RUN
RELEASE=NOT_RUN
REMOTE_CI=NOT_RUN
```

上述 `NOT_RUN` 不代表功能失败，只表示计划编制阶段未执行运行性验证。实际验证从获批的 Goal 开始，并必须绑定对应 Final SHA。
