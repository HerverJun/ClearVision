# Studio UI Next F04 — G2 Auth 生命周期闭环

## 1. Closure

```text
G2_STATUS=DONE
AUTH_OWNER_COUNT=1
SESSION_PROJECTION_OWNER_RUNTIME_COUNT=0
TOKEN_SET_REMOVE_WRITER_COUNT=1
DIRECT_AUTH_TOKEN_KEY_ACCESS_OUTSIDE_PORT=0
HTTP_CLIENT_AUTHORITY_COUNT=1
PRODUCT_RUNTIME_MOUNTED_WHEN_UNAUTHENTICATED=NO
WORKSPACE_OWNER_MOUNTED_WHEN_UNAUTHENTICATED=NO
EVENTBUS_AUTH_401_USAGE=0

F04-B10-AUTH-DUAL-OWNER=CLOSED
F04-B11-TOKEN-MULTI-WRITER=CLOSED
F04-B12-ROUTE-GUARD-MISSING=CLOSED
F04-B13-401-LOOP=CLOSED
F04-B14-SESSION-TRANSITION-BYPASSES-RUN-SAVE-PROTECTION=CLOSED
```

G2 只实现认证、session、受保护 composition 与所需页面；没有实现 Project lifecycle command、G3B/G3C 或 G4–G6。

## 2. Owner topology

```text
createStudioApp
└─ AuthLifecycleRoot
   ├─ authLifecycleOwner                 唯一 auth/session 状态机
   ├─ StudioPlatform.tokenPort           唯一 token 存储端口
   ├─ shared ApiTransport                唯一 HTTP authority
   ├─ Router global guard                唯一产品路由认证门禁
   └─ ProductRuntime (authenticated only)
      ├─ ReadQueryClient
      ├─ SystemStatusOwner
      ├─ UiPreferencesOwner
      └─ WorkspaceRuntime
```

- `sessionProjectionOwner.ts` 只保留 projection 类型与 decoder，不创建 owner、timer 或请求；运行时实例数为 0。
- `authLifecycleOwner.session` 是同一个 Auth owner 内部的窄只读 adapter，供现有 Product capability 消费，不是第二 authority。
- 未认证、setup-required、expired 或 logout 后，`ProductRuntimeBoundary` 不挂载，Workspace owner count 为 0。

## 3. 状态机

实现状态：

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

所有异步 Auth 请求使用 operation generation、单一 AbortController、disposed guard 和 late-response guard。login/setup/recovery 各自去重；dispose 后晚到 token 响应不能写 token 或挂载 Runtime。

同一身份的 `/auth/me` 刷新不增加 session generation、不重建 ProductRuntime。网络失败保留上次已确认用户并进入 stale；权威 401 才清 token 并执行 expire/quarantine。

## 4. Token authority

`StudioPlatform.tokenPort` 提供：

```text
readToken()
setToken(token)
removeToken()
```

- production 使用 `sessionStorage`；
- 测试使用同合同 memory port；
- `cv_auth_token` 字面量只存在于 `platform/auth/tokenPort.ts`；
- 只有 `authLifecycleOwner` 调用 set/remove；
- `ApiTransport` 只读取；
- 不写 localStorage、Vue store、URL、日志或错误详情。

## 5. Auth 产品链

### 冷启动与初始化

```text
GET auth/setup-status
→ setup-required: 只挂载 setup page
→ setup complete + token: GET auth/me recovery
→ setup complete + no token: login page
```

setup-admin 成功链：

```text
POST auth/setup-admin
→ tokenPort.setToken
→ GET auth/me
→ ProductRuntime mount
→ /overview
```

setup POST 响应丢失时重新读取 setup-status：仍需初始化才允许安全重试；已经完成则转 login，不通过重复 POST 或用户名搜索猜测结果。

### 登录与恢复

- login token 不能直接等于 authenticated；必须经 `/auth/me`。
- invalid token 清除后进入 login。
- transient `/auth/me` failure 为 stale，不伪造 logout。
- 刷新应用从 sessionStorage token 恢复。

### 修改密码与 logout

- 进入 change-password route 前以及提交命令前均经过统一 leave protection。
- change-password 成功：remove token → dispose Runtime → login → 新密码提示。
- logout 成功：server response → remove token → dispose Runtime → login。
- logout 响应未知：保留 token/Runtime 并报告，不能宣称服务端已注销。

## 6. Route guard

全局 guard 实现：

- `/setup`、`/login`；
- `/change-password`；
- `/forbidden` 与 authenticated catch-all not-found；
- Overview/Projects/Operators/Results/About protected；
- Workspace 与 Diagnostics 为 Admin/Engineer；
- Stations 默认 profile 拒绝，只有 `Studio2.StationsRead=true` 才允许；
- Labs 只允许 browser-test/internal evidence host，不进入产品导航；
- safe return 只接受 allowlisted 内部 product route，拒绝 scheme、`//host`、反斜杠、encoded separator、`..`、Labs 与未知 route；
- logout 后 browser back 重新评估 session，不恢复 protected DOM。

前端 guard/visibility 不替代后端 permission。

## 7. 401 与 reauth reconcile

权威决策见 [ADR-F04-G2-401会话失效与运行重认证协调](./ADR-F04-G2-401会话失效与运行重认证协调.md)。

共享 transport 直接 callback 按 session generation 去重。首个 401 卸载 Product DOM；无活动 Workspace 的 Runtime dispose，有 draft/run 的 Runtime readonly quarantine。重新认证后先用原 Project、clientSnapshot、revision 与 hash identity reconcile，再恢复或重建 Runtime。

## 8. 页面与可访问性

G2 新增 setup、login、change-password 与 forbidden 页面；包含：

- label/autocomplete/required；
- keyboard submit；
- password visibility toggle with `aria-pressed`；
- status/error `aria-live` 与失败后 focus restoration；
- submit busy/disabled；
- 1366×768 关键链无全局横向溢出；
- skip link 在 hash router 下使用 preventDefault + programmatic focus，避免被解释为业务 route。

## 9. 验证状态

本文件的最终测试数量、Browser/WebView2、远端 CI 与最终 SHA 以 Prompt 3/4 交付报告为准。未执行证据不得由本地 unit 推断为 PASS。

```text
CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```
