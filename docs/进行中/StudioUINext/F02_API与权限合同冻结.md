# F02 API 与权限合同冻结

## 1. 冻结状态

```text
F02_API_CONTRACT_FREEZE=PASS
F02_PERMISSION_CONTRACT_FREEZE=PASS
PRODUCT_REQUEST_METHODS=GET_ONLY
AUTH_ENTRY_DECISION=PRESEEDED_SESSION_PREVIEW_ONLY
```

本冻结以 `F02_INITIAL_SHA=f6d4d98a53914bac088cd62cda261b2c08a11670` 当前代码为准。
未在表中批准的业务 endpoint 不进入 Goal 1 产品 bundle。

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
