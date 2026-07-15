# F02 API 与权限合同冻结

## 1. 冻结状态

```text
F02_API_CONTRACT_FREEZE=PASS
F02_PERMISSION_CONTRACT_FREEZE=PASS
PRODUCT_REQUEST_METHODS=GET_ONLY
AUTH_ENTRY_DECISION=PRESEEDED_SESSION_PREVIEW_ONLY
```

Goal 1 冻结以 `F02_INITIAL_SHA=f6d4d98a53914bac088cd62cda261b2c08a11670` 当前代码为准。
Goal 2 增量冻结以 `F02_GOAL2_ENTRY_SHA=a23022be48c1e580198a41912c72ad0bbed753fd` 当前代码为准，
并受本 Goal 批准的 Operator identity-only scoped gate 约束。未在表中批准的业务 endpoint 不进入产品 bundle。

## 2. Goal 1 endpoint

| Capability | Method/path | 当前安全边界 | F02 投影 |
| --- | --- | --- | --- |
| System status | `GET /health` | `AuthMiddleware` 白名单，匿名 | `{ status, port }`；未知字段忽略，缺失/非法字段进入 decode error |
| Session | `GET /api/auth/me` | 必须有现有 Bearer/X-Auth-Token session | `{ userId, username, role }`；role 只用于 UI 投影 |
| Projects list | `GET /api/projects` | 现有认证中间件；无额外 edit permission | 仅稳定摘要字段 |
| Recent projects | `GET /api/projects/recent?count=` | 同上 | 仅稳定摘要字段；count 由 capability 限定 |
| Project search | `GET /api/projects/search?keyword=` | 同上 | 仅稳定摘要字段；keyword 使用 URLSearchParams 编码 |
| Project detail | `GET /api/projects/{id}` | 同上；不存在返回 404 | 独立详情 decoder；不得由列表 payload 代替 |

Goal 1 不调用 `/api/auth/setup-status`，也不调用任何 login、logout、setup-admin、change-password、
Project POST/PUT/DELETE、Inspection、Station、Runtime、AI 或其他写 endpoint。

## 3. Project decoder 边界

当前列表、recent、search 与 detail 都返回后端 `ProjectDto`，但前端必须区分语义：

### 列表可读取字段

```text
id
name
description
version
persistenceRevision
createdAt
modifiedAt
lastOpenedAt
```

列表 decoder 不读取 `flow`、`globalSettings`、`globalVariables`、`assets`，也不派生算子数、连接数、
Flow 状态或保存权威。列表 payload 中即使存在这些字段，也只视为未消费的后端扩展。

### 详情可读取字段

详情仍来自 `GET /api/projects/{id}`。Goal 1 页面可以从详情 payload 展示工程身份、描述、版本、
正式持久化 revision、时间、Flow 名称、算子数、连接数、决策配置摘要与 assets 数量；这些派生值
必须明确来自详情 decoder。Flow、GlobalVariables 与 assets 不建立前端权威模型，不编辑、不保存。

`recent` 的 count 固定使用正数常量，不接受任意用户输入；search keyword 必须 trim，空字符串不发请求。
当前列表/search/recent 对需要 save recovery 的工程会跳过且不返回 partial metadata，因此 UI 不能把空列表
解释为“后端确定不存在任何工程”。

若未来需要完整 Flow 或 assets 语义，必须由相应阶段重新冻结 decoder 与 owner；不得扩展本冻结表。

## 4. 状态与错误映射

| Transport/decoder 结果 | 统一产品状态 | 行为 |
| --- | --- | --- |
| pending 且无 previous data | Loading | 显示局部 loading，区域 `aria-busy=true` |
| 200 + 空集合 | Empty | 保持页面结构，提供只读说明 |
| 401 | Unauthorized | 清空受保护缓存、推进 session generation、取消旧请求 |
| 403 | Forbidden | 正常产品状态；不得误报为网络故障 |
| 404 detail | Not Found | 显示工程未找到；不重定向列表 payload |
| network/5xx | Error | 可手动重试；不自动重试所有请求 |
| malformed/unknown required field | Decode Error | 不猜测字段，不降级成伪数据 |
| refresh 失败且有 previous data | Stale / Partial Failure | 保留 previous data 并显示明确提示 |
| abort/superseded generation | Silent cancelled | latest-request-wins，旧请求不得覆盖新结果 |

## 5. 权限决定

- 后端认证与 endpoint permission 是唯一安全边界；
- 前端不得复制完整 permission policy；
- `role` 只决定导航提示与可见性优化，不能替代 401/403；
- Projects GET 当前只有认证要求，不要求 `CanEditProject`；Project 写 endpoint 继续由现有
  `CanEditProject` policy 保护，但 Goal 1 不调用；
- Unauthorized 不提供未经批准的登录 handoff，只说明当前阶段需要预置会话；
- token、Authorization header 与 session identity 不写入 cache key、日志、DOM 或持久化偏好。

## 6. Query authority

所有以上请求必须通过现有 `apiTransport.ts`。业务 query 传入 transport-relative path：

```text
auth/me
projects
projects/recent?count=...
projects/search?keyword=...
projects/{id}
```

公共 health 使用 root-relative `/health`。禁止组件直接 `fetch`、Axios、第二 request core、端口发现、
第二 token provider、第二 session owner 或第二 health timer。

## 7. Goal 2 Operator endpoint 与权限冻结

```text
F02_OPERATOR_GATE_AUDIT_SHA=4958ecab5873160d96b8c34efcf5f488257ea4df
OPERATOR_IDENTITY_METADATA_SYNC=PASS
OPERATOR_CONTRACT_GATE=PASS
F02_OPERATOR_CONTRACT_SCOPE=CATALOG_IDENTITY_AND_CURRENT_BRANCH_RUNTIME_METADATA
STABLE_LINE_FULL_OPERATOR_CONTRACT_SYNC=DEFERRED
```

`F02_OPERATOR_GATE_AUDIT_SHA` 只表示审计结论，不是最终通过后的 `F02_OPERATOR_CONTRACT_SHA`。
`F02_OPERATOR_CONTRACT_SHA` 由通过本节全部 Gate 验证后的独立提交 SHA 记录，不能回填为审计提交。
Operator identity 仅允许同步并验证：

```text
DisplayName / Description / CategoryId / Lifecycle /
LifecycleNote / DefaultHidden / IconName / Keywords / Tags
```

| 用途 | Method/path | 当前安全边界 | Goal 2 投影 |
| --- | --- | --- | --- |
| 完整 Catalog 读取 | `GET /api/operators/library?includeCompatibility=true` | 现有认证中间件 | identity 字段、当前分支 `version`、`inputPorts`、`outputPorts`、`parameters`；未知扩展忽略 |
| 默认 Catalog 读取 | `GET /api/operators/library` | 同上 | 后端排除 `DefaultHidden=true`，按 category order、displayName 稳定排序 |
| 类型索引验证 | `GET /api/operators/types?includeCompatibility=true` | 同上 | 只用于类型数量、唯一性和 library 对齐验证，不作为显示语义 authority |
| 算子详情 | `GET /api/operators/{type}/metadata` | 同上；未知 enum/不存在按当前 endpoint 行为 | 与 library 同一 decoder；不生成默认参数，不调用 preview/recommend endpoint |

当前 HTTP JSON 形状中 `OperatorType`、`OperatorCategoryId`、`OperatorLifecycle`、`PortDataType` 为数字 enum；
decoder 必须按当前分支数值映射读取，不能用稳定线文档中的 enum 名字符串替代真实 payload。

稳定线新增但当前分支尚未同步的 conditional parameter/output rules 与 image-depth contracts 延期；
不得从稳定线文档、类型名或算法名推断。side-effect/readiness 不展示、不筛选、不推断。

Operator 列表允许搜索、分类、端口、参数、hidden/deprecated 过滤；全部过滤条件进入 URL query，
但产品请求仍只调用上述 GET。Catalog 不提供拖拽、预览、执行或默认参数生成。

## 8. Goal 2 Station endpoint 与权限冻结

| 用途 | Method/path | 当前安全边界 | Goal 2 投影 |
| --- | --- | --- | --- |
| Station 列表 | `GET /api/stations` | 现有认证中间件，无 StationAdmin requirement | 普通列表与普通详情的 authority |
| 摘要 | `GET /api/stations/summary` | 同上 | 只读摘要与 canonical outcome counters |
| 统计 | `GET /api/stations/statistics?...` | 同上 | 时间范围、stationId、status、diagnosticCode 过滤；九类结果不折叠 |
| Station 结果页 | `GET /api/stations/results?...` | 同上 | Station 上报视图与分页 authority |
| 单 Station 结果 | `GET /api/stations/{stationId}/results?take=...` | 同上 | 详情结果区域 |
| 单 Station health | `GET /api/stations/{stationId}/health?take=...` | 同上 | 详情 health 区域 |
| Admin 增强详情 | `GET /api/stations/{stationId}` | `RequireStationAdmin` | 可选增强区；403 只降级该区域，不覆盖普通详情、结果或 health |

当前 HTTP JSON 形状中 `StationOnlineState`、`StationRuntimeState` 与 legacy `RuntimeRunOutcome` 为数字 enum；
`ExecutionOutcome`、`DecisionOutcome` 自带 string enum converter，仍为字符串。Browser fixture 必须覆盖这种混合形状。

禁止调用 Station SSE、logs、commands、audit、packages、download、identity PATCH、command/deploy POST。
Station capability 允许手动刷新，并仅在页面可见时以保守间隔轮询；hidden、route unmount 或 owner dispose
必须停止 timer 和进行中的请求。筛选条件进入 URL query。

## 9. Goal 2 Results endpoint 与权限冻结

| 来源 | Method/path | 当前安全边界 | Goal 2 投影 |
| --- | --- | --- | --- |
| 本机工程选择 | `GET /api/projects` | 现有认证中间件 | 只读取 Goal 1 冻结的工程摘要，不读取 Flow/asset authority |
| 本机结果列表 | `GET /api/inspection/history/{projectId}?startTime=&endTime=&status=&defectType=&pageIndex=&pageSize=&flowVersionHash=` | 现有认证中间件 | 分页摘要；必须读取 `executionOutcome` 与 `decisionOutcome` 双轴 |
| 本机结果详情 | `GET /api/inspection/history/{projectId}/{resultId}` | 同上；不存在返回 404 | 只读标量详情、diagnostic、defect 摘要与 traceability；图片/ROI/compare/export/replay 延期 |
| Station 上报结果 | `GET /api/stations/results?...` | 现有认证中间件 | 与 Station capability 共用后端 DTO，但使用 Results 私有 decoder；只共享 canonical outcome formatter |

Results 的 URL query 冻结为 `source`、`projectId`、`resultId`、`outcome`、`diagnosticCode`、
`from`、`to`、`page`、`pageSize`。`source=local|station`；本机无 projectId 时不发 inspection history 请求。
本机 history endpoint 当前没有 `diagnosticCode` 参数，因此该条件只能过滤当前已返回页，UI 必须明确标注
“当前页过滤”，不得伪装为后端全量过滤；Station results 的 `diagnosticCode` 仍由 endpoint authority 过滤。

Execution/Decision 双轴映射严格保留以下九类：

```text
Ok / Ng / Undetermined / NotApplicable / Invalid /
Failed / Cancelled / TimedOut / Skipped
```

只有 `Succeeded + Ng` 是 NG。Undetermined、NotApplicable、Invalid、Failed、Cancelled、TimedOut、Skipped
不得并入 NG。Station 旧 payload 缺少双轴时，只允许复制当前后端 `StationCanonicalOutcomeProjection` 的读取时兼容映射，
并明确标记为 legacy projection；不得从 diagnosticCode 或显示文案推断。

## 10. Goal 2 fixture 冻结

Browser fixture 顶层元数据必须包含：

```text
schemaVersion
endpoint
sourceSha
DATA_SOURCE=BROWSER_FIXTURE
AUTH_SOURCE=HARNESS_SEEDED_SESSION
```

Operator fixture 允许生成 200 条（158 条权威项加确定性重复分页样本）；Results fixture 允许生成 500 条，
用于分页与浏览器性能验证。扩展样本必须保持唯一 fixture id，并保留原始 operator type/result source 标记，
不得冒充真实后端数量。真实 WebView2 empty-authority 证据必须标记
`DATA_SOURCE=REAL_WEBVIEW2_EMPTY_AUTHORITY`，不得与 Browser fixture 混报。
