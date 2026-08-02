# ClearVision Studio UI Next F07 完整开发计划（PROPOSED / AUDITED / G0 DECISIONS FROZEN）

> 文档状态：`PROPOSED_AUDITED`；本文前半部分记录 F07 G0 的历史审计计划、产品决策与边界冻结。G1-G9 的实际实现与当前证据见本文第 12 节和链接的阶段报告；本计划本身不改变产品配置默认值、默认入口、CI 或 Legacy 工作树。
>
> 审计日期：2026-07-31。审计基线是执行时当前 HEAD，不绑定历史 SHA。
>
> G0 决策记录：[F07 G0 进入治理与产品决策冻结](./F07_G0_进入治理与产品决策冻结.md)

## 0. 审计基线与证据声明

| 项目 | 本轮事实 |
| --- | --- |
| 工作树 | `C:\Users\HerverJun\Desktop\ClearVision-UI-Next` |
| 分支 | `studio-ui-next` |
| G0 执行基线 SHA | `b7f54ffbe4e1efaed0ee599158cb1748cded0cd2` |
| G0 执行时工作树 | clean；没有发现已有 dirty 文件 |
| `origin/studio-ui-next` | G0 执行时与 G0 执行基线 SHA 相同 |
| Remote | `https://github.com/HerverJun/ClearVision.git` |
| 代码基线 | 当前仓库代码与当前配置优先；历史 README、旧 Goal 和历史 PASS 仅作线索 |
| 本轮测试/build | `NOT RUN`；本轮不以未执行的 build、test、Browser、WebView2、DPI、publish 或 CI 结论填充计划 |

审计读取了 Legacy `wwwroot/src/features/settings/`、Desktop 端点与配置服务、AI 与 Station 存储、Station WinForms 设置入口、Next `StudioUI` composition/router/platform/owner/test 结构，以及现有 UI/WebView2/publish/CI 脚本。计划中的“已存在”是代码事实；“建议”是下一轮实现前需要批准的方案；两者不混写。

## 1. 核心结论

F07 不应被定义为“把旧 Settings 页逐项复制到 Vue”。当前产品的配置不是单一 JSON，也不是一个可以由前端长期持有的对象：

- `AppConfig` 由 `JsonConfigurationService` 作为单一 authority 管理，落盘到运行目录的 `config.json`。
- Camera binding 由 `CameraManager` 与专用 `/api/cameras/bindings` API 负责内存应用和持久化。
- TCP 连接、服务器、收发帧和状态属于 `ITcpDeviceManager` 运行时状态；PLC connection test 也只是一次测试，不等同于保存或运行连接。
- Station 通信写入 Studio 与 Local Station 两份 LocalAppData 文档，并以 restart result 表达生效边界。
- AI model metadata 与 API key secret 分开存储；API key 由 Windows DPAPI secret store 保护。
- 用户、密码、数据库维护、Project/Flow/Runtime Package、Runtime 和 Station 又分别有自己的 authority。

因此建议 F07 采用一个 Next `/settings` 管理工作台、一个 Settings capability owner、按 authority 分区的 typed projection/draft，以及严格串行 Goal。建议总数为 **10 个串行 Goal：G0-G9**。

F07 的完成不自动改变默认入口，不自动退役 Legacy，也不把 Settings 迁移误判为 Runtime、Station、连续检测、AgentRun 或正式结果迁移完成。G0 已冻结的权限、authority、section 范围和延期项以 [G0 决策记录](./F07_G0_进入治理与产品决策冻结.md) 为准；后续偏离必须另行形成产品决策或 ADR。

## 2. 当前事实

### 2.1 Legacy Settings 的完整能力面

正式 Legacy Settings 入口是 `#settings-view`，由 `viewManager.js` 调用 `ensureSettingsView()` 懒加载。当前 `settingsView.js` 组合以下 tab 和 API：

| 能力域 | 当前 Legacy 能力 | 真实写入/操作入口 |
| --- | --- | --- |
| General | 软件标题、主题；历史 auto-start 字段显示为禁用/未启用语义 | `/api/settings` 的 `general` scope；主题也有专用 `/api/settings/theme` |
| Storage | image save path、保存策略、retention、disk usage；路径选择和立即清理通过 feature capability 标记，代码明确保留不可用语义 | `/api/settings` 的 `storage` scope；`GET /api/settings/disk-usage` |
| Runtime protection | auto-run、连续 NG 停止、缺料超时、protection rules | `/api/settings` 的 `runtime` scope |
| Security/account | 密码修改、密码策略、session/lockout 展示、Admin 用户列表、创建/编辑/删除/重置密码 | `/api/auth/change-password`、`/api/users/**`、`/api/settings` 的安全策略字段 |
| Database | status、repair、backup、restore、cleanup | `/api/settings/database/**`；Admin-only，具有破坏性或恢复语义 |
| PLC | S7、MC、FINS；各协议连接 profile；mapping；表单校验；连接测试；保存/重置；协议切换时保留各自 draft | `/api/plc/settings`、`/api/plc/mappings`、`/api/plc/test-connection` |
| TCP | Client/Server profile；connect/disconnect；start/stop server；文本/HEX send；runtime status；有界 frame log 和清空 | `/api/tcp/profiles/**` |
| Station communication | Disabled/LocalLoopback/LanController、port、LAN host、local sync、masked token、reveal/regenerate、restart diagnostics | `/api/station-communication/settings`、`/api/station-communication/token` |
| Cameras | discovery、Huaray/Hikvision discovery、binding、曝光、gain、pixel format、trigger mode/source、enter/serial photoelectric、soft capture、continuous preview | `/api/cameras/**`、`/api/trigger-input/**` |
| AI models | model CRUD、activate、planner/shadow-eval role、reasoning support、API key keep/replace/clear、connection test | `/api/ai/models/**`、`/api/ai/reasoning-support` |
| RuntimePreview Pilot | developer-only、metadata-only 配置、catalog、readiness、session/replay/report/export、governance | `/api/settings/runtime-preview-pilot/**`；不是正式执行或结果 authority |

`settingsView.js` 的 top-level `save()` 已经不是一个原子“保存全部设置”操作：Station、PLC、TCP、Camera、AI、Database 分别走专用处理函数，General/Storage/Runtime/Security 等才走 scoped `/api/settings`。F07 必须保留这个 authority 分界，而不是把所有 panel 重新拼成一份 JSON 再提交。

### 2.2 Legacy 内部已有两套 Settings owner

`app.js` 同时导入：

- `createLegacySettingsView('settings-view')` 对应完整的 `SettingsView`；
- `new SettingsCapabilityOwner(...)` 对应实验性的 JSON editor owner。

实验 owner 只有同时满足下列条件才会挂载：

1. WebView2 startup feature flag `Studio2.Settings` 为 `true`；
2. `window.__CLEARVISION_ENABLE_EXPERIMENTAL_SETTINGS_CAPABILITY === true`。

默认 `appsettings.json` 中 `Studio:SettingsCapabilityEnabled=false`，且普通 Legacy browser fixture 不注入第二个 window switch，因此默认路径仍是完整 Legacy Settings。已有 Legacy 回归测试还明确断言 Settings 使用完整 Legacy tab，而不是 `.settings-capability-owner` JSON editor。

实验 owner 的 `SETTINGS_TABS` key 是 `system`、`plc`、`station`、`ai`、`cameras`，保存请求形如 `saveScope: tab.configKey` 加同名属性。后端 `MergeSettingsUpdate` 实际认识的是 `general`、`storage`、`runtime`、`security`/`users`、`communication`/`plc`、`tcpCommunication`/`tcp`、`features`；相机又明确由专用 `/api/cameras/bindings` 所有。由此可见：

- `plc` editor 没有提交后端需要的 `communication` 属性；
- `system`、`station`、`ai` 不是 generic AppConfig merge section；
- `cameras` generic PUT 被后端保护为不直接合并；
- owner 的 `saveSettings()` 仍调用旧 `settingsApi`，不是 Next `ApiTransport`。

这不是可直接复用的 F07 地基，而是一个已接入 flag、但合同未闭合的实验 owner。不能把它报告为“Settings 已迁移”。

### 2.3 Next 当前事实

当前 `StudioUI/src/app/router.ts` 没有 `/settings` route，`ProductLayout.vue` 的产品导航也没有 Settings 入口。当前 composition root 是：

- `createStudioApp.ts` 创建 Vue app、Pinia、router、auth lifecycle root，并注入 `StudioPlatform`；
- `ProductRuntime` 提供一个共享 `ApiTransport`、read query client、session、system status、UI preferences、project lifecycle、workspace runtime；
- `WebView2HostAdapter` 与 `BrowserHostFake` 是现有宿主适配，不应新建 bridge；
- `ApiTransport` 校验同源 loopback `/api/`，统一处理 Authorization、abort、401/403/404/409/5xx；
- `UiPreferencesOwner` 只维护 theme、density、reduced motion，并使用 `clearvision.studio-ui.preferences.v1` localStorage key。

UI preferences 是 UI projection，不是 `AppConfig.General` 的产品配置 authority。不能因为 Next 已有外观菜单，就宣称产品 General/Theme 已迁移。

Next workspace 已有 `CameraBindingEditorOwner`：它在 Project workspace 内读取 `/api/cameras/bindings`，通过 existing camera capture/continuous preview API 获取单帧，并在 dispose 时取消请求、停止 preview session。它负责 Flow 中 ImageAcquisition 的绑定和预览输入，不负责系统级 camera discovery/binding administration。F07 的 Camera 管理必须与它协调 owner 生命周期，不能再建一个隐式 camera runtime 或第二 preview loop。

Next 已有 Project/Flow/GlobalVariables/Runtime Package/AI workbench 等 capability，但这些不等同于 Settings 迁移：

- Project/Flow/GlobalVariables 写入必须继续走现有 Application Service 与 `ProjectSaveCoordinator`；
- Runtime Package export 已有服务端 endpoint 和 Next owner，不能由 Settings 导出器替代；
- F06 AI workbench 的 AgentRun、SSE、handoff、replay/recovery 不属于 AI model management；
- `/stations` 是 Station read/operation capability，不是 Studio Station communication settings page。

### 2.4 AppConfig authority 与保存实现

`ClearVision.Product.Core/Entities/AppConfig.cs` 当前包含：

`Revision`、`General`、`Communication`、`TcpCommunication`、`Storage`、`Runtime`（含 `RuntimePreviewPilot`）、`Features`、`Cameras`、`Security`、`ActiveCameraId`。

`IConfigurationService` 的当前实现是 `JsonConfigurationService`：

1. 默认路径是 `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json")`；
2. `LoadAsync()` 读取并 `Normalize()`；文件不存在或读取/反序列化失败时使用 normalized defaults；
3. `SaveAsync()` clone/normalize 后，将 revision 设为 `max(snapshot.Revision, cached.Revision) + 1`；
4. 文件先写到 `config.json.tmp`，再 `File.Move(..., overwrite: true)`；
5. `_cachedConfig` 更新后，`GetCurrent()` 返回 clone；
6. 没有 expected revision、ETag、条件 PUT 或跨进程冲突检测。

`AppConfig.Revision` 是该配置文件的观察版本，不是 Project 的 `PersistenceRevision`。前端不能把它当成 Project save revision，也不能在没有后端合同的情况下自造 optimistic concurrency。

Desktop 启动时会加载 AppConfig，并把 camera bindings 与 active camera 应用到 `CameraManager`，同时配置 serial photoelectric trigger service。其他 domain 的生效时机由各自 service/endpoint 决定，不能由 Settings UI 猜测为“保存即已连接”或“保存即已重启”。

### 2.5 现有 endpoint 与生效语义

| 域 | 读 | 写/测试 | authority 与生效事实 |
| --- | --- | --- | --- |
| General/Storage/Runtime/Security policy | `GET /api/settings`；Admin full，非 Admin 只返回 safe subset（revision、软件标题、主题） | `PUT /api/settings` scoped；`PUT /api/settings/theme` | 写回 AppConfig；当前没有条件 revision；主题专用 endpoint 仍要求 Admin |
| Reset | 无单独 preview | `POST /api/settings/reset` | 同时重置 AppConfig 与 AI models；不重置 Station communication token，也不重置 user database；必须按破坏性 operation 处理 |
| Disk/database | `GET /api/settings/disk-usage`、database status | repair/backup/restore/cleanup | Admin-only；数据库 restore/cleanup 不是普通配置保存 |
| PLC | `GET/PUT /api/plc/settings`、`GET/PUT /api/plc/mappings` | `POST /api/plc/test-connection` | configService 持久化；test 创建临时 client、Connect/Ping/Disconnect，不保存、不建立长期 PLC session |
| TCP | `GET/PUT /api/tcp/profiles` | connect/disconnect/send/server start/stop/status/frames/clear | profile config 通过 `ITcpDeviceManager` 保存；socket、server、status、frame log 为运行时内存；保存不自动 connect/start |
| Camera | `GET /api/cameras/bindings`、discovery | `PUT /api/cameras/bindings`、soft trigger、trigger input、continuous preview | binding normalize/validate 后更新 CameraManager 并持久化；active stream 发生影响性变更时返回 `409`；preview 是可 dispose 的调试输入，不是正式结果 |
| Station communication | `GET /api/station-communication/settings` | PUT settings；token reveal/regenerate | 写两份文档；返回 `RequiresRestart.Studio` / `RequiresRestart.LocalStation`；当前 Studio ingress option 不 live reconfigure |
| AI models | `GET /api/ai/models`；Admin full projection，其他角色 safe projection | CRUD、activate、default planner/shadow-eval、test | `AiConfigStore` 写 `ai_models.json`；key 不写明文 model JSON；test 调配置的真实 LLM endpoint，不触碰 RuntimePreview/Camera/PLC/Station |
| RuntimePreview Pilot | config/catalog/session/replay/report/governance endpoint | 多个 developer/admin endpoint | metadata-only 与安全 deny rules；不应自动成为产品 Settings 主导航 |

Generic `/api/settings` 仍然可以接受 `communication`、`tcpCommunication`，而 dedicated PLC/TCP endpoints 也拥有这些配置。这是后端潜在写入口重叠。F07 默认只把 generic endpoint 用于它明确负责的 General/Storage/Runtime/Security sections；PLC/TCP/Camera/Station/AI 使用专用 endpoint。若产品要消除重叠，必须另有 contract/ADR，不能由前端偷偷改变后端语义。

### 2.6 权限事实

`EndpointPermissionGuards.cs` 当前策略为：`RequireAdmin`、`RequireEngineerOrAdmin`、`RequireStationAdmin`（当前等价 Admin）、`CanEditProject`、`CanOperateHardware`、`CanReadSensitiveConfig`。

| 操作 | 当前权限事实 |
| --- | --- |
| Generic settings write、theme、reset、disk/database | Admin |
| Generic settings GET | 经过现有 AuthMiddleware；Admin full，非 Admin safe subset |
| PLC read/test | Engineer/Admin（`CanOperateHardware`） |
| PLC settings/mapping write | Admin |
| TCP read/runtime connect/send/server/frame operations | Engineer/Admin |
| TCP profile write | Admin |
| Camera discovery/read/preview/trigger | Engineer/Admin |
| Camera binding write | Engineer/Admin |
| Station communication settings/token | Admin |
| AI model CRUD/activate/default/test | Admin |
| User management | Admin；密码修改是 authenticated session operation |

前端可以改善导航和禁用态，但不能用隐藏按钮替代服务端授权。所有 401/403 都必须保留为可解释的权限状态，不能自动重试写操作。

### 2.7 敏感字段、恢复与异常语义

- AI `ApiKey` 通过 `ai_model_secrets` 下的 DPAPI 文件保存；model JSON 中不应当有可用明文 key。响应只给 `hasApiKey`、masked key 和 redacted URL/header/query/body。UI 应支持 keep/replace/clear，不把现有 key 放进可持久化 draft。
- Station shared token normal view 为 masked；reveal/regenerate 是单独 Admin operation。当前 store 生成六位数字 token，并把 token 写入 Studio/Station sync 文档。F07 不得把 token 放进 localStorage、普通日志或一般 export。
- 用户密码不属于 AppConfig；密码策略属于 AppConfig，但用户 authority 是 UserManagement service/database。
- PLC/TCP 地址、camera device identity、Station host 等是敏感的运营配置，应至少在证据、截图、导出和错误日志中脱敏；它们不是 AI API key，但也不能当作无风险 demo 数据。
- `JsonConfigurationService` malformed config 会 fallback to defaults；保存失败会抛异常。前端必须区分“读取到默认值”与“原配置被成功恢复”，不能在 UI 上无条件写成“已恢复”。
- database restore、settings reset、AI key clear、Station token regenerate、Camera active stream mutation 都是需要明确确认和失败恢复说明的操作。

### 2.8 Import/Export 当前事实

当前代码中存在多个互不相同的 Import/Export：

| 能力 | authority | F07 处理 |
| --- | --- | --- |
| Legacy Project JSON import/export | Legacy project manager；创建 Project、更新 Flow 并保存 | 不作为 Settings import/export；Project 迁移继续走 Project authority |
| Next Runtime Package export | `POST /api/projects/{id}/runtime-package/export`，服务端从 persisted Project snapshot 生成并校验 package/hash | 保留现有 Next owner；不放进 Settings |
| Inspection evidence export | inspection evidence service endpoint | 保留结果/evidence authority；不混入配置备份 |
| Station site profile import/export | Station `StationSiteProfileStore`，绑定 runtime package/site profile | 不由 Studio Settings 替代 |
| Station runtime parameter import/export | Station WinForms runtime parameter panel | 不由 Studio Settings 替代 |
| Settings bundle import/export | 当前没有 `/api/settings/import` 或 `/api/settings/export` 合同 | **缺失事实**；除非另行批准 schema、脱敏、校验、preview、原子 apply 与 rollback，不在 F07 虚构实现 |

因此，F07 不能把 `GET /api/settings` 的完整响应直接下载成“配置备份”，也不能把 JSON editor 变成通用导入器。Settings Import/Export 已排除出 F07；若未来需要，必须作为独立 feature 重新批准 schema、脱敏、校验、原子 apply 与 rollback 合同。

## 3. 问题、缺失、重复与不应保留项

### 3.1 问题清单

| 编号 | 结论 | 影响 |
| --- | --- | --- |
| F07-P01 | Next 没有 Settings route、nav item、Settings owner 或 Settings contract | 无法在 Next 中逐步验证配置工作流 |
| F07-P02 | Legacy 标准 Settings 与实验 JSON Settings owner 并存，且由不同开关控制 | 容易误报迁移完成；切换时必须真实 dispose/unmount |
| F07-P03 | 实验 owner 的 tab key 与后端 generic merge sections 不一致 | 开启实验开关可能显示可编辑 JSON，但写入不覆盖目标 authority |
| F07-P04 | AppConfig 有 Revision 但无条件保存/冲突合同 | 前端不能声称 stale protection；跨窗口/跨进程 last-write-wins 风险未解决 |
| F07-P05 | generic settings PUT 与 PLC/TCP 专用写 endpoint 有潜在重叠 | 两个写入口可能产生语义漂移；F07 必须选择专用 owner |
| F07-P06 | Station、AI、User、Database 不是 AppConfig 的同一存储或同一恢复域 | “保存全部设置”“恢复全部默认”会误导用户 |
| F07-P07 | Settings import/export endpoint 缺失 | 不能用前端序列化替代缺失的版本化/脱敏/原子 apply 合同 |
| F07-P08 | Camera binding 与 preview 有 active stream `409` 保护，Next workspace 已有 camera capture owner | Settings camera editor 必须处理 owner 协调、取消和冲突，不能私自保持流 |
| F07-P09 | Legacy AI panel 有硬编码性能指标展示 | latency/token/RPM 等 fake metrics 不是 backend authority，不能迁移为事实 |
| F07-P10 | RuntimePreview Pilot 是 developer/internal metadata-only surface | 不应因 Legacy tab 存在而进入面向生产用户的 F07 Settings 主路径 |
| F07-P11 | 默认 `StudioUiEnabled=false`、`SettingsCapabilityEnabled=false`；CI feature branch push 不触发常规 CI | 当前不能用 F07 文档或 push 宣称默认入口已经切换或完整 CI 已通过 |
| F07-P12 | 现有 WebView2 runner 没有 `f07` evidence phase，Next Settings 场景也不存在 | 需要新增隔离 evidence 场景/配置后才能形成真实宿主证据 |

### 3.2 已迁移、未迁移、重复和不应继续保留

- **已经存在但不是 Settings 迁移**：Next UI preferences、Project/Flow workspace、Project runtime package export、Stations read capability、F06 AI workbench。这些能力继续由现有 owner 和 authority 管理。
- **尚未迁移**：Legacy General/Storage/Runtime/Security/User/Database、PLC、TCP、系统 Camera administration、Station communication、AI model management，以及所有对应的 Settings information architecture。
- **需要区分而不能合并**：Workspace CameraBindingEditor 与系统 Camera administration；AI model configuration 与 AI AgentRun workbench；Station communication 与 Station local hardware/site profile；database maintenance 与 ordinary configuration save。
- **应淘汰的产品交互**：JSON editor 作为生产 Settings UI、假性能指标、把所有 domain 显示为同一“保存全部”事务、把 reset 叫作全量恢复默认、把 preview frame 当正式 inspection result。
- **不应在 F07 继续扩大**：RuntimePreview Pilot governance console、AgentRun/EventStore/recovery、RuntimeHost、Station local authority、正式结果持久化、第二 HTTP/Host/EventBus/Canvas/save infrastructure。

## 4. F07 产品范围与架构边界

### 4.1 F07 内

F07 的实现目标是建立一个可逐步启用的 Next Settings 管理工作台：

1. 新增 `/settings` product route 与受控 Settings 导航入口；
2. 建立唯一 `SettingsOwner`，其 projection/draft 只存在于该 owner 生命周期；
3. 复用 `ProductRuntime.api` 的单一 `ApiTransport`，用 capability-local typed adapter/decoder 消费现有 endpoint；
4. 按 authority 分区显示 General/Storage/Runtime/Security、PLC、TCP、Camera/Trigger、Station communication、AI model、Database status/backup；Database restore、cleanup、global reset 不在首轮交付；
5. 展示保存后状态、权限、校验错误、运行态与 restart required，而不是只显示“保存成功”；
6. 为每一个 mutation 定义 scope、权限、敏感字段、apply/restart/reload 语义；
7. `/settings` route 仅允许 Admin / Engineer；Operator 不进入该 route，且不扩大现有后端权限；
8. 保持 Legacy 是可回退的独立 owner，并为每个 Goal 提供真实 unmount/dispose、Browser、WebView2、DPI、publish 和 CI 证据。

### 4.2 F07 外

以下不属于 F07，除非产品负责人另行批准并建立独立 ADR：

- 修改 `StudioUiEnabled`、`SettingsCapabilityEnabled`、Workspace flags 或任何生产默认值；
- 修改默认入口、删除 Legacy、移除 Legacy Settings modules、Legacy AI 退役；
- FrontendV2/`FrontendV2` 兼容层、旧 `/v2` build/publish 路径；
- 新建 Project save endpoint、第二 Project save client、第二 `ProjectSaveCoordinator`、前端 Project/Flow authority；
- Runtime Package format、RuntimeHost、Station 现场链路、Station local settings/site profile、正式 inspection result/evidence authority；
- AgentRun、`AgentRunEventStore`、terminal reservation、replay/recovery、Workspace Snapshot authority；
- 用 WebMessage bridge 执行 PLC、相机、TCP、Station 或 inspection 命令；
- 用 Pinia、localStorage、DOM、IndexedDB 或前端 cache 长期保存产品配置或 secret；
- 新建第二 HTTP client、ServiceRegistry、EventBus、HostBridge、Canvas kernel 或 project save chain；
- 未有后端合同的 Settings import/export；
- Database restore、cleanup、global reset；首轮只允许 database status 与 backup；
- Station token 后端安全升级；现有 token endpoint 语义只能按当前后端消费，并登记为延期债务；
- 将 RuntimePreview Pilot 从 developer-only surface 扩大为普通产品 Settings；
- 真实相机、PLC、Station、LLM provider 的生产/现场验收。它们需要独立硬件或站点证据，不能由 Browser mock 代替。

## 5. 建议的信息架构

F07 Settings 不照搬 Legacy 的单一长页。建议使用一个受控 route 下的分组工作台：

1. **Overview / change review**：只显示本次会话的未保存 draft、各 authority 的当前 revision/status、未生效原因和待重启清单；不显示可编辑的任意 raw JSON。
2. **Product**：General、Storage、Runtime protection。每个 section 单独保存并显示“已保存/仅重载生效/需重启/不可用”。
3. **Security**：password policy、change password、users。用户管理与密码操作分开确认，密码永不回填。
4. **Device connections**：PLC 与 TCP 分成两个 capability-local panel；保存 profile 和 runtime test/connect/send 明确分开。
5. **Cameras & triggers**：系统 Camera binding/discovery/trigger diagnostics/preview；与 Workspace camera binding editor 通过同一后端 API 协调，不能共享未受控的 mutable camera object。
6. **Station communication**：Studio ingress、Local Station sync、token masked/reveal/regenerate、restart diagnostics；不显示为“已实时应用”。
7. **AI models**：model list、role/default、reasoning support、secret operation、connection test、last backend test status；不显示 fake throughput metrics，也不把 AgentRun 入口塞进 model config。
8. **Maintenance**：首轮只显示 database status 与 backup；restore、cleanup、global reset 延期，不进入 F07 Settings owner。高风险按钮需要 operation scope、影响范围、确认和恢复说明。
9. **Import/Export**：Settings Import/Export 排除出 F07，不挂载入口，也不在 G9 内形成合同；现有 Project/runtime/evidence/Station import/export 继续留在各自 authority 的入口。

导航和 section visibility 服从 session/role/feature flag，但安全性始终由 API policy 决定。组件只读 owner projection，写操作进入 owner 的窄命令接口；表单 draft 不直接写 Pinia 或 localStorage。`/settings` 的产品 route 角色固定为 Admin / Engineer，Operator 直接禁止。Next UI preferences 与 AppConfig 产品主题保持独立，不能互相隐式写入。

## 6. 配置 authority、保存与生效模型

### 6.1 Authority map

| UI section | authority | F07 允许的写入口 | 前端不得做的事 |
| --- | --- | --- | --- |
| General/Storage/Runtime/Security policy | `IConfigurationService` / AppConfig | 现有 `PUT /api/settings` 的明确 `saveScope`，且 generic scope 仅限这四类 | 不把完整 AppConfig 当成前端 store；不修改默认值；不使用 AppConfig revision 充当 Project revision |
| Next UI preferences | `UiPreferencesOwner` 的 UI projection | 现有 UI preference owner 的本地投影；不进入产品 Settings save chain | 不把 Next theme/density/reduced-motion preference 写入 AppConfig General theme，也不把产品主题反向覆盖 UI preference |
| PLC | `IConfigurationService` + PLC endpoint | `/api/plc/settings`、`/api/plc/mappings` | 不通过 generic settings PUT 绕过专用校验；不把 test 当持久化 |
| TCP | `ITcpDeviceManager` + AppConfig | `/api/tcp/profiles`；运行时操作使用专用 `/api/tcp/profiles/{id}/...` | 不在浏览器保存 socket/frame state；不把 save 误报为 connected/listening |
| Camera | `CameraManager` + frame/trigger coordinators | `/api/cameras/bindings` 与专用 discovery/trigger/preview APIs | Settings 负责系统管理；Workspace 继续负责工程绑定与预览；不在 Vue 直接持有 camera/stream/AbortController；active stream 冲突必须 fail closed |
| Station communication | `StationCommunicationSettingsStore` + `StationIngressOptions` | `/api/station-communication/settings`、`/token` | 不写 Station local settings 文件；不假定 save 后 Studio/Station live reload；token 后端安全升级登记为延期债务 |
| AI model | `AiConfigStore` + DPAPI secret store | `/api/ai/models/**` | 不读写 `ai_model_secrets`；不在 localStorage/cache 备份 key；不由 AgentRun owner 写 model config |
| User/password | UserManagement service/database + auth endpoint | `/api/users/**`、`/api/auth/change-password` | 不把 user record 或 password 放入 AppConfig draft |
| Database maintenance | `VisionDatabaseMaintenanceService` | 首轮仅使用 status 与 backup endpoints | 不在前端实现 restore/cleanup/global reset；不把 backup 文件路径当普通 text field 无条件提交 |
| Project/Flow/Runtime/Station package | 现有 Project/Application/Runtime/Station authorities | 现有各自 endpoint | 不由 Settings owner代理或缓存这些 authority |

G0 已冻结 generic `/api/settings` 的产品使用面：F07 只允许 General、Storage、Runtime、Security；PLC、TCP、Camera、Station、AI 只走专用 endpoint，即使后端当前仍接受潜在重叠字段，也不得在 Next 形成 fallback 或双写入口。

Camera Settings 负责系统管理，Workspace 继续负责工程绑定与预览；Settings owner 不得抢占 Workspace 的 active stream。后端 `409`、stream stop 和 owner dispose 都按 fail-closed 处理。

### 6.2 Draft、revision、保存结果

F07 owner 的建议状态模型：

```text
authoritative projection -> section draft -> local validation
        ^                         |
        |                         v
        +---- response/reload <- exact existing endpoint
```

- `authoritative projection` 是最后一次服务端响应或重新 GET 的投影；不是 localStorage；
- section draft 只在 mounted Settings owner 内存在，route leave/unmount 后默认丢弃或由 leave guard 明确阻止离开；
- UI `draftRevision`/generation 只用于 stale response、abort 和 draft identity；
- 后端 `AppConfig.Revision` 只作为观测和展示；G0 决定 F07 不新增 conditional revision、ETag 或 409 保存合同；
- 沿用单 Settings owner、section 串行写和现有后端无条件 revision / last-write-wins 语义；
- Project `PersistenceRevision` 与 F07 config revision 完全分离；
- 同一 section 的写请求由 owner 串行化；旧请求返回后不能覆盖新 draft；
- 409、validation failure、403、network failure、unknown outcome 必须分别显示，并保留或丢弃 draft 的语义要由 owner 明确规定；不能把所有失败 catch 成“保存成功”。

保存步骤固定为：

1. 从对应 read endpoint 加载并 decode authoritative shape；
2. 在 owner 中建立 section draft，不复制无关字段；
3. 使用与后端一致的 normalize/validation contract 做本地预检，但不以本地预检取代后端校验；
4. 只调用该 section 的唯一既有写入口；
5. 以 endpoint response 或随后 GET 重新建立 authoritative projection；
6. 显示 `saved`、`effective`、`requiresRestart`、`runtimeStatus` 等真实字段；
7. route leave、logout、feature flag off 或 owner dispose 时取消请求、timer、SSE、preview session、blob URL，并确保旧 owner 不再写入。

### 6.3 保存后生效分类

| 分类 | UI 语义 | 当前已知例子 |
| --- | --- | --- |
| immediate projection | 后端写入并在返回中可重新读取 | AppConfig section 保存、AI model active/default、Camera binding memory update |
| runtime operation only | 操作即时影响运行时，但不是配置保存 | PLC test、TCP connect/send/server start、camera preview/capture、AI connection test |
| reload/startup dependent | 需要 service reload 或下次启动才使用 | Desktop startup config、部分 AppConfig consumers；具体 consumer 必须由代码/endpoint 证据确认 |
| restart required | backend 明确返回 restart result | Station Studio/LocalStation communication settings |
| destructive/maintenance | 不是普通 save，需要确认和独立结果 | database restore/cleanup、AppConfig+AI reset、token regenerate、AI key clear |

UI 文案必须让用户知道“已持久化”与“已生效”不是同一件事。没有 endpoint 返回或真实运行时证据时，不得写“已应用到设备”。

## 7. 串行 Goal 计划

以下 Goal 必须串行推进。每个 Goal 只有在自己的门禁通过后才能进入下一个；任一停止条件出现时保持上一个已通过 Goal 的可回滚状态，不自动扩大范围。

### G0：F07 入口、基线、权限与产品决策冻结

**目标**：确认实现 worktree、执行时源代码基线、共享文件 owner，并冻结本计划中的权限、authority、section 范围、延期项和独立 release 决策。

**允许范围**：

- 只读审计、authority/endpoint/permission/secret/restart matrix、ADR 草案、测试矩阵；
- 维护 F07 文档入口、Goal 状态和 evidence ledger；
- 记录 G0 执行基线 SHA、远端分支状态和当前未执行证据；决策详情见 [F07 G0 决策记录](./F07_G0_进入治理与产品决策冻结.md)。

**禁止范围**：

- 修改产品代码、配置默认值、路由、CI、Legacy 文件或共享 contracts；
- 开始 Settings route、owner 或 API client 实现；
- 擅自同步 `origin/codex初稿`、切换分支、stash、reset 或处理其他 worktree。

**门禁**：产品负责人已冻结 F07 不是默认 cutover；所有纳入 section 有 authority、permission、write endpoint、effective/restart 说明；本计划第 10 节不再保留未决产品问题；G1 尚未授权启动。

**停止条件**：发现 source/contract 与本计划无法对应、远端分支发生不兼容前进/分叉、或需要未批准的后端 authority 扩权。

### G1：Settings 合同、信息架构与唯一 Owner 冻结

**目标**：为 Next Settings 建立 typed contracts、section ownership、error/revision/restart semantics 和唯一 mounted owner 方案。

**允许范围**：

- Next capability-local DTO/decoder、API path constants、role projection、Browser fixtures、ADR 与 contract tests；
- 复用 `ProductRuntime.api`，只允许一个 Settings owner 和一个 settings write coordinator；
- 对已有后端 contract 做证据化差异记录；仅在独立 ADR 和产品批准后做最小兼容修正。

**禁止范围**：

- 第二 HTTP transport、第二 save client、第二 service registry/event bus/host bridge；
- 以 generic `/api/settings` 取代 PLC/TCP/Camera/Station/AI 专用 owner；
- 将 `AppConfig.Revision` 改名或伪装成 Project `PersistenceRevision`；
- 先建 Settings JSON editor 再补合同；
- 新增 Settings import/export endpoint 或把 secret 放进 contract。

**门禁**：每个字段都有 source/decoder/write owner/permission/sensitive/apply classification；所有 403/404/409/validation response 有测试 fixture；AppConfig concurrency 固定采用单 Settings owner、section 串行写和后端现有无条件 revision，不新增 conditional revision 合同；Operator 对 `/settings` 直接禁止。

**停止条件**：发现关键字段没有权威 endpoint、敏感字段响应无法安全脱敏、需要新 authority、或 revision/concurrency 决策未获批准。

### G2：Next Settings Shell、Route 与生命周期地基

**目标**：加入受控 `/settings` route、导航入口、Admin/Engineer role guard、Operator forbidden、Settings owner skeleton 与 read-only projection，建立可验证的 mount/unmount 边界。

**允许范围**：

- `StudioUI` route/layout/owner、typed adapter、loading/empty/error/forbidden/unknown states；
- 使用共享 `ApiTransport` 和现有 startup/session lifecycle；
- Browser fake host、unit mount/dispose tests、中央 Playwright fixture 的静态 Settings shell；
- 只读加载现有 safe/full endpoint，不提交产品配置。

**禁止范围**：

- 修改 `StudioUiEnabled`、`SettingsCapabilityEnabled` 默认值或 Legacy `app.js` default path；
- 在 Legacy `#settings-view` 与 Next `/settings` 同时挂载同一 capability；
- 用 CSS hidden 代替 unmount；
- Vue 组件直接持有 EventSource、camera stream、AbortController、WebView2 channel 或 imperative canvas；
- 使用 localStorage/Pinia 作为产品 Settings authority。

**门禁**：router/role/feature tests；Admin/Engineer 可达、Operator forbidden；owner 单实例、离开 route 后无请求/定时器/subscription/write；Browser 中可验证 loading/error/403；Legacy regression 仍确认默认路径。

**停止条件**：出现两个 mounted owner、dispose 后仍有请求/写入、Settings 访问依赖前端隐藏而非 API policy，或 route 引入第二 platform/client。

### G3：General、Storage 与 Runtime protection

**目标**：迁移低耦合的产品参数，准确区分 saved/effective/reload，保留后端 normalize 和 disabled/no-op 语义。

**允许范围**：

- General title/theme、Storage save policy/path/retention/disk usage、Runtime protection 的 scoped edit；
- 调用现有 `/api/settings` `saveScope` 与 `/api/settings/disk-usage`；
- 展示不可用的 path picker/immediate cleanup，不虚构 host dialog 或 cleanup endpoint；
- draft discard/reload、后端 validation、save failure 和 malformed/default projection 状态。

**禁止范围**：

- 修改默认保存路径、retention、runtime timeout、theme default 或配置 normalize；
- 把 Next UI theme preference 自动写成产品 General theme；两者保持独立；
- 通过 `/api/settings` 写 Camera、PLC、TCP、Station、AI；
- 把 auto-start 等已禁用历史字段变成可用功能。

**门禁**：AppConfig/config service 定向测试；scoped PUT payload contract；reload/save/error Browser；Admin/Engineer/Operator permission matrix；至少一次真实 Desktop endpoint smoke（不连接硬件）。

**停止条件**：保存会覆盖未编辑 section、默认值发生漂移、UI 将 disk usage/path check 误标为成功写入，或 backend revision/normalization 造成不可解释的回写。

### G4：Security、User 与 Database maintenance

**目标**：迁移账户和有限维护投影，同时把 password、database restore、cleanup、global reset 等高风险能力从本轮普通 Settings 范围中排除。

**允许范围**：

- 密码修改、Admin user CRUD/reset、password policy projection；
- database status 与 backup 的 operation-specific dialog/result；
- 记录 database repair/restore/cleanup、global reset 为延期项，不在 F07 首轮提供入口或前端合同；
- Admin-only gating、confirm/cancel、timeout/unknown outcome、成功后 reload。

**禁止范围**：

- 前端保存/哈希/缓存密码；
- 把延期的 database/reset 能力展示为“全部设置恢复默认”；
- 前端直接操作 SQLite/数据库文件或自造 restore chain；
- 允许非 Admin 通过前端参数绕过服务端权限；
- 在截图、日志、telemetry 或 test artifact 中保留密码、token、backup path 等敏感值。

**门禁**：现有 `UserEndpointsTests`、database status/backup 对应测试与 auth/permission tests 定向通过；Browser 验证 Admin/Engineer/Operator matrix；真实 WebView2 只使用隔离测试数据库；restore、cleanup、global reset 不形成 F07 证据。

**停止条件**：status/backup scope 与后端实现不一致、敏感字段泄漏、数据库测试无法可靠隔离，或有人试图把 restore/cleanup/global reset 带回 F07 首轮。

### G5：PLC 与 TCP 连接工作台

**目标**：迁移 PLC/TCP 配置与连接诊断，严格把 persisted profile、connection test、runtime session 和 frame log 分开。

**允许范围**：

- PLC S7/MC/FINS profile、mapping、active protocol、validation、test connection；
- TCP client/server profile、validation、connect/disconnect、server start/stop、send、status、bounded frames、clear frames；
- 复用 `/api/plc/**`、`/api/tcp/**` 和现有 validators；每类操作有独立 pending/abort/result；
- 保留 Legacy 的 PLC per-protocol draft isolation 和“保存当前/全部 profile”语义，但以当前 backend contract 为准。

**禁止范围**：

- generic `/api/settings` 与 dedicated PLC/TCP write 双写；
- 把 test/connect/send 通过 WebMessage bridge 或浏览器私有 socket 执行；
- 保存后自动 connect/start server；
- 在前端长期缓存 frame log、socket handle、PLC client 或制造虚假 connected 状态；
- 未获批准的 PLC/TCP contract 扩展。

**门禁**：现有 PLC unit/E2E、`PlcSettingsEndpointTests`、`TcpEndpointsTests`、`TcpDeviceManagerTests` 与 validation tests；Admin/Engineer/Operator 403 matrix；virtual PLC/loopback only smoke；真实设备证据另行标记。

**停止条件**：协议 drafts 互相污染、test 修改持久化配置、runtime operation 造成 owner leak、profile save 覆盖其他 profile，或硬件测试无法隔离端口/数据库/进程。

### G6：Camera、Trigger 与 Preview

**目标**：迁移系统级 Camera administration、discovery、trigger diagnostics/preview，同时明确 Workspace 继续负责工程绑定与预览，并保护既有 CameraBindingEditor 和 camera stream lifecycle。

**允许范围**：

- all/Huaray/Hikvision discovery、binding edit、active camera、exposure/gain/pixel/trigger、enter/serial photoelectric、soft capture、continuous preview；
- 使用 `/api/cameras/**` 与 `/api/trigger-input/**`；preview/soft capture 仅展示 debug input，不写正式 inspection result；
- Settings owner 通过专门 adapter 持有 AbortController、preview session、blob URL，并在 dispose/route leave 时停止；
- 遇 active stream settings mutation 的 `409` 时 fail closed，显示冲突资源并要求先停止 preview/inspection；不得静默覆盖或抢占流。

**禁止范围**：

- Vue 直接创建或长期持有 CameraManager/FlowCanvas/ImageCanvas/stream 对象；
- 与 Workspace CameraBindingEditor 复制一个永久 camera/preview authority；
- 把 discovery 成功当作 camera 已连接、把 preview 单帧当作正式检测结果；
- 修改 Station `HardwareSettingsDialog` 或 Station local camera authority；
- 在没有 real camera evidence 时宣称现场连接通过。

**门禁**：`CameraBindingsEndpointTests`、`ContinuousPreviewEndpointTests`、`SoftTriggerCaptureEndpointTests`、camera provider/frame stream tests；Next owner mount/dispose/abort/stop tests；Browser 只用 deterministic image fixture；WebView2 Debug/Release 至少验证真实 preview endpoint cleanup 和 `409`；硬件厂商证据单独记录。

**停止条件**：离开 Settings 后 preview session、pending frame waiter 或 blob URL 残留；workspace/settings 两个 owner 同时写/持有 stream；active stream conflict 被静默覆盖；或相机 API 需要未批准的新 authority。

### G7：Station communication 与 restart workflow

**目标**：迁移 Studio 对 Station communication 的管理与诊断，不越过 Station 自身的 settings/site/package authority；现有 token endpoint 可按当前语义消费，但后端安全升级延期并登记为 `F07-D01`。

**允许范围**：

- mode、port、LAN host、local sync、masked token、reveal/regenerate、running vs persisted comparison、Studio/LocalStation restart indicators；
- 使用现有 `/api/station-communication/settings` 与 `/token`；保存后展示两份文档路径和 restart result（按后端 response）；
- 为 disable、invalid port/host、missing token、regenerate failure 和 restart pending 提供明确状态。

**禁止范围**：

- 前端直接改 `%LocalAppData%` 文件、`StationLocalSettingsStore`、`StationSiteProfileStore` 或 Station hardware dialog；
- 把保存报告成 Studio/Station 已实时重载；
- 把 token 放入 localStorage、项目 JSON、普通日志、截图或通用 export；
- 在 F07 内修改 token generation、storage、reveal 或 backend security contract；该升级进入延期债务，不由前端掩盖；
- 通过 Settings 代替 Station runtime package deployment、commands、results 或 sync journal。

**门禁**：`StationCommunicationSettingsStoreTests`、`StationCommunicationEndpointsTests`、Station sync tests；Admin/Engineer/Operator route matrix（Station mutation 仍 Admin-only）；token reveal/regenerate artifact 必须脱敏且证明不落盘到前端 storage；restart result 必须可回读。

**停止条件**：两份文档写入不一致、restart marker 语义无法呈现、token 泄漏、或需要在 Studio 进程内新增 live reconfiguration authority。

### G8：AI model management 与 secret lifecycle

**目标**：将 AI model profile/role/default/test/reasoning 管理纳入 F07，严格区别 model config、secret store 和 F06 AI workbench；RuntimePreview Pilot 继续保持 developer-only。

**允许范围**：

- model CRUD、enable/active、planner/shadow-eval role、reasoning support、masked key keep/replace/clear、connection test、last test status；
- 使用 `/api/ai/models/**` 和 `/api/ai/reasoning-support`；保留 backend masking/redaction；
- API key 输入只在操作期间存在于内存，离开 panel/owner 即清除；明确 test 使用真实存储 key 但不保存其它 UI draft。

**禁止范围**：

- 读取/复制/迁移 `ai_model_secrets`；
- 把 API key 写入 model JSON、localStorage、Pinia persistence、query cache 或 test artifact；
- 将 fake latency/token/RPM 指标迁移为事实；
- 修改 `AiGenerationOptions`/appsettings defaults、AgentRun/EventStore、RuntimePreview execution 或 Flow Apply；
- 将 RuntimePreview Pilot developer console 当作普通 AI model panel。

**门禁**：`AiConfigStoreTests`、`AiModelEndpointsTests`、redaction/reasoning/permission tests；Admin/non-Admin safe projection matrix；Browser key lifecycle assertions；WebView2 test connection 只使用隔离模型/provider fixture，报告不能含 key 或完整 URL query/header。

**停止条件**：key 在响应/日志/DOM persistence 中泄漏、AI model save 与 activate 语义被误合成一个不可回滚事务、真实 provider test 结果无法区分 mock/real，或 F06 owner 与 Settings owner 形成第二 model authority。

### G9：集成验收与 F07 准入（不含 Settings Import/Export）

**目标**：完成所有已批准 Settings sections 的组合验证并形成完整证据包；Settings Import/Export 已排除出 F07，不在 G9 内决定或实现；不执行默认 cutover。

**允许范围**：

- 端到端验证跨 section route leave、auth expiration、403、draft discard、restart pending、unknown outcome、reload/recovery；
- 复核现有 Project import/export、Runtime Package export、evidence export、Station profile import/export 的边界；
- 记录 Settings Import/Export 为独立后续合同候选，不在 F07 创建 endpoint、schema、入口或 bundle；
- 生成 F07 evidence ledger、风险签字、回滚演练与 legacy regression 结果。

**禁止范围**：

- 没有合同就新增 Settings import/export；
- 将 Settings Import/Export 的后续合同决策伪装成 F07 准入项；
- 把 `GET /api/settings` dump、AI key、Station token、user/password、database backup path 放入通用 bundle；
- 修改默认入口、删除 Legacy、默认关闭回退；
- 用一次 Browser mock 或一次 WebView2 smoke 代替硬件、DPI、publish、CI 和生产验收各自证据。

**门禁**：见第 8 节全部 evidence matrix；Legacy default regression PASS；Next route/owner lifecycle PASS；所有已批准 Goal 的 backend/unit/browser/WebView2/publish/CI 结果可追溯到同一 source SHA；产品负责人明确 `DEFAULT_ENTRY_CHANGE` 与 `LEGACY_RETIREMENT` 仍为独立决策。

**停止条件**：任何 authority 未闭合、证据类别混用、secret/backup 泄漏、CI 未产生真实结果、真实 WebView2/publish 失败，或有人试图把 F07 完成解释为默认切换/Legacy 退役授权。

## 8. 测试与证据要求

### 8.1 Unit、contract 与 backend

每个 Goal 必须最小化但完整地覆盖：

- Next typed decoder 对 camelCase/PascalCase、缺字段、非法 enum、unknown error payload 的行为；
- owner 的单实例、draft isolation、generation/stale response、abort/dispose、route leave 和 forbidden 状态；
- backend 已有 service/endpoint tests 不得被 frontend 计划替代。重点现有测试文件包括：
  - `JsonConfigurationServiceTests.cs`、AppConfig normalization/config migration tests；
  - `SettingsThemeEndpointTests.cs`、`SettingsResetEndpointTests.cs`、`SettingsDiskUsageEndpointTests.cs`、`SettingsDatabaseEndpointTests.cs`；
  - `PlcSettingsEndpointTests.cs`、PLC protocol/virtual tests、`TcpEndpointsTests.cs`、`TcpDeviceManagerTests.cs`；
  - `CameraBindingsEndpointTests.cs`、`ContinuousPreviewEndpointTests.cs`、`SoftTriggerCaptureEndpointTests.cs`、camera frame/provider tests；
  - `StationCommunicationSettingsStoreTests.cs`、`StationCommunicationEndpointsTests.cs`、Station local/sync/hardware tests；
  - `AiConfigStoreTests.cs`、`AiModelEndpointsTests.cs`、redaction/permission tests；
  - Next `apiTransport.spec.ts`、router/auth/platform composition、owner lifecycle tests。

同一 `.csproj` 不得并发启动多个 `dotnet test`。定向测试使用仓库串行脚本或固定 regression entry；同一项目多个类合并到一次调用。测试数据、端口、数据库、WebView2 user-data、publish 与结果目录必须隔离。

### 8.2 Browser / Playwright

使用 `ClearVision.Product/tests/ClearVision.Product.UI.Tests/` 中央 Playwright 配置和 `CV_UI_SCENARIO=studio-ui-next`。Browser 证据可以证明：

- route、layout、role projection、draft interaction、validation/error/403/409 presentation；
- API request shape 与 owner dispose；
- 视觉密度、按钮/表单可用性、滚动/窄宽度与 1920x1080 工业工作站布局。

Browser fixture 不能证明真实 ASP.NET endpoint、WebView2、WinForms、Windows DPI 或硬件。未来 F07 至少需要：

- Settings overview、每个已实现 section 的 happy/error/forbidden/dirty/discard 状态；
- Admin、Engineer、Operator 角色矩阵；
- 1366x768、1920x1080、1024 宽和窄宽度防溢出检查；
- 拍摄与审计结果仅写 `.tmp/studio-ui-next/f07/...` 或测试目录，不在仓库根创建未忽略生成物。

现有 `plc-settings.spec.ts` 和 `studio-legacy-regression.spec.ts` 只能作为 Legacy/PLC 回归基线，不能当作 Next Settings PASS。

### 8.3 真实 WebView2

未来 F07 需要复用 `webview2-harness.cjs`、`Invoke-StudioUiWebView2Evidence.ps1` / `Invoke-StudioUiWebView2Matrix.ps1` 的隔离原则，并新增 F07 scenario 或经批准的 phase 支持。现有 runner 的 `EvidencePhase` 尚未包含 `f07`，本轮不修改它。

每次真实宿主证据必须记录：

- source SHA、Debug/Release、executable path、Web port、CDP port、runtime/user-data directory；
- WebView2 channel/host kind、实际 route、auth role、request/response 状态；
- console error、page error、request failure、dispose/port release；
- settings read/write、restart response、403/409、camera preview cleanup 等实际场景；
- sanitized screenshots/JSON；不能记录 API key、Station token、密码或完整敏感 header/query。

静态 Chromium、Browser fixture 或 WebView2 fake 不能替代真实 WebView2。

### 8.4 DPI、分辨率与 Windows 125% 缩放

F07 UI 需要在产品目标的 1920x1080、Windows 125% 缩放工作流下验证，至少覆盖：

- native Desktop DPI awareness 由 `Get-DesktopRuntimeProbe.ps1` 取证；
- WebView2 CDP `devicePixelRatio`、CSS layout viewport、visual viewport、screenshot pixel size；
- Windows native 125% 与 WebView2 `force-device-scale-factor` 模拟必须分别标记，不能把 simulated scale 当作真实系统缩放；
- Settings 长表单、错误消息、确认对话框、sticky action bar、token/restart 状态在 125% 下无重叠/裁剪；
- 如 150%/200% 或跨屏无法在当前 runner 复现，标记 `NOT_PERFORMED`，不能用 Browser DPR 补写。

### 8.5 Release publish、no-Node 与临时目录

未来 Release evidence 应：

- 使用当前 Desktop `.csproj` 的真实 Release self-contained win-x64 publish；
- 临时 publish 只写 `./.tmp/publish-check/` 或仓库外的专用目录，并在清理前验证路径位于允许根；
- 扫描 `wwwroot/studio` 资产、manifest、source/dev artifact 和 Node 依赖；
- 从 publish 目录启动真实 Desktop executable，验证 Settings route、API、WebView2 生命周期；
- 明确“publish 静态 scan 无 Node”与“独立从未安装 Node 的目标机启动”是不同证据；
- 记录 publish cleanup；不在仓库根保留 publish、截图、日志或 test-results。

### 8.6 CI 与远端证据

当前 `.github/workflows/ci.yml` 的 push 触发是 `main`、`develop` 和 `v*` tag，pull request 目标是 `main`/`develop`；普通 `studio-ui-next` push 不自动形成 CI。当前 `ui-browser` job 会分别运行 Legacy UI 与 `CV_UI_SCENARIO=studio-ui-next` Browser tests，`studio-ui` job 负责 StudioUI lint/typecheck/unit，Release build 只在 main/tag 条件下运行。

F07 完整准入必须通过真实 workflow_dispatch 或指向受支持 base branch 的 PR 产生：

- StudioUI lint/typecheck/unit；
- product/desktop 定向或完整测试；
- Legacy UI regression；
- Next Browser scenario；
- Desktop build/publish；
- 已批准的 F07 evidence artifact 与 report；
- 不把本地 PASS、普通 branch push 或历史 CI run 伪装成当前 Final Gate。

未实际运行的任何项继续标为 `NOT RUN` 或 `NOT PERFORMED`，并记录原因。

## 9. 风险、回滚与保护策略

| 风险 | 保护 | 回滚 |
| --- | --- | --- |
| Next/Legacy 双 owner、旧请求继续写 | route/flag 级单一 owner、generation/Abort、dispose tests、request ledger | 关闭 Next capability/回到 Legacy；不删除 Legacy 文件 |
| AppConfig last-write-wins 或 generic overlap | 单 Settings owner、section-specific write coordinator、保存前 reload、明确显示观察 revision；F07 不新增 conditional revision | 禁止继续扩大写面；保留 dedicated endpoint，回退新增 UI |
| Camera active stream 被改写 | 后端 `409`、Settings/Workspace owner 协调、dispose stop preview | 保持原 binding/stream；要求用户先停止 preview/inspection |
| Station token/restart 误导 | masked/reveal operation、双文档回读、restart flags、脱敏 evidence；后端安全升级登记为 `F07-D01` | 停止 Station section release；继续使用 Legacy Station page |
| AI key 丢失或泄漏 | DPAPI authority、keep/replace/clear、内存清理、redaction tests | 关闭 AI Settings capability；不回滚/复制 secret 文件 |
| Database/reset 破坏数据 | F07 首轮只做 status/backup；restore、cleanup、global reset 延期；隔离测试库、unknown outcome | 通过 backend/数据库 recovery；前端不伪造恢复 |
| Settings bundle schema/secret policy 不成熟 | Import/Export 排除出 F07，不出现入口或伪造合同 | 拆为独立后续 feature，F07 不交付该入口 |
| WebView2/DPI/publish 证据不足 | 分层 evidence matrix，独立 ports/user-data/runtime dirs | F07 只能保持 `PARTIAL`，不准入默认切换 |
| 共享文件 owner 越界 | `AGENTS.md` 白名单、主协调代理串行改 shared files | 停止该 Goal，先完成语义合并和审计 |

回滚的第一选择是关闭 F07 Next capability 或保持 route 不可达，而不是删除数据、覆盖 AppConfig、回滚 Station token 或重置数据库。任何产品数据恢复都必须由既有 backend authority 完成。

## 10. G0 已冻结的产品决策

以下决策已由 F07 G0 冻结，不再作为 G1 capability 实现者可自行假设的开放问题。完整 authority、权限和债务登记见 [F07 G0 决策记录](./F07_G0_进入治理与产品决策冻结.md)。任何偏离都必须重新取得产品批准并建立独立 ADR。

| 编号 | 决策 | F07 约束 |
| --- | --- | --- |
| D01 | Settings route 仅 Admin / Engineer；Operator 禁止 | 前端 route guard 只能表达现有权限，不能扩大后端权限；Operator 不提供只读概览绕过 |
| D02 | Next UI preferences 与 AppConfig 产品主题独立 | 不互相隐式写入；UI preference 仍是 UI projection，产品主题继续由 AppConfig authority 管理 |
| D03 | F07 不新增 conditional revision | 沿用单 Settings owner、section 串行写和现有后端无条件 revision / last-write-wins 语义；UI revision 只做 draft/stale 防护 |
| D04 | generic `/api/settings` 只负责 General、Storage、Runtime、Security | PLC、TCP、Camera、Station、AI 只走专用 endpoint；不得 generic fallback 或双写 |
| D05 | Settings Import/Export 排除出 F07 | 不新增 endpoint、schema、入口、bundle 或伪造备份；未来另立 feature/ADR |
| D06 | Database 首轮只迁移 status 与 backup | restore、cleanup、repair、global reset 不进入 F07 首轮 Settings owner 或准入证据 |
| D07 | Camera Settings 负责系统管理；Workspace 继续负责工程绑定与预览 | active stream 冲突保留后端 `409` 并 fail closed；不得静默覆盖、抢占或复制 preview owner |
| D08 | Station token 后端安全升级延期 | 现有 endpoint 只能按当前语义消费；`F07-D01` 登记 token generation/storage/reveal 安全升级债务，前端不得掩盖或擅自改后端 |
| D09 | AI model management 纳入 F07；RuntimePreview Pilot 保持 developer-only | 可迁移 model profile/role/default/reasoning/test/secret lifecycle；不把 RuntimePreview 或 AgentRun 变成普通 Settings authority |
| D10 | 默认入口切换与 Legacy 退役独立决策 | F07 完成、G0/G1/G9 通过均不自动修改默认 flags、入口或 Legacy 文件 |

本节取代原“产品负责人需要裁决的问题”。G1 只能将这些决策翻译为 typed contract、permission matrix、owner lifecycle 和证据门禁，不得重新打开已冻结范围。

## 11. F07 最终完成定义

只有在以下条件全部满足时，才能把 F07 标记为 `COMPLETE`：

- 所有获批 section 都有明确 authority、唯一 write entry、permission、sensitive-field、save/apply/restart contract；
- `/settings` route 只对 Admin / Engineer 可达，Operator forbidden；不新增或扩大后端权限；
- Next `/settings` 由唯一 owner 挂载，route/feature 切换和 logout 后旧 owner 无 listener、timer、SSE、preview、AbortController、blob URL、请求或写入残留；
- 不存在第二 HTTP/Host/EventBus/Canvas/Project save authority；
- General/Storage/Runtime/Security、User、Database status/backup、PLC/TCP、Camera/Trigger、Station communication、AI model 的实际 endpoint 语义与 UI 文案一致；
- generic `/api/settings` 只服务 General/Storage/Runtime/Security，其他设备和 AI section 只使用专用 endpoint；
- Camera system administration 与 Workspace 工程绑定/预览保持唯一 owner 边界，active stream conflict fail closed；
- AppConfig `Revision`、Project `PersistenceRevision`、AI model storage、Station restart state 没有被前端混用；
- Legacy regression、Next unit/Browser、backend endpoint/service tests 均有当前 source SHA 证据；
- 真实 WebView2 Debug、Release publish、DPI/125%、cleanup、publish/no-Node 分层证据齐全；未运行项明确为 `NOT RUN`/`NOT PERFORMED`；
- CI 由真实 workflow/PR/dispatch 产生并上传可追溯 artifact；普通 feature branch push 不被当作 CI；
- Settings Import/Export 已明确排除出 F07，不产生 endpoint、schema、入口或伪造备份；
- Station token 后端安全升级以 `F07-D01` 登记为延期债务，不被前端工作伪装为已完成；
- 产品负责人签字确认破坏性操作、权限、secret、Station restart 和回滚；
- `DEFAULT_ENTRY_CHANGE=NOT_AUTOMATIC`，`LEGACY_RETIREMENT=NOT_APPROVED`，除非另有独立批准和证据；
- F07 完成只表示 Settings capability 达到批准的迁移门禁，不表示 Runtime、Station、真实硬件、真实模型质量、正式检测结果或生产现场验收完成。

本 G0 结束状态：

```text
G0_STATUS=DONE
F07_PRODUCT_DECISIONS=FROZEN
F07_IMPLEMENTATION=FORBIDDEN
G1_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=INDEPENDENT_DECISION
LEGACY_RETIREMENT=INDEPENDENT_DECISION
SETTINGS_IMPORT_EXPORT=EXCLUDED_FROM_F07
DATABASE_FIRST_ROUND=STATUS_AND_BACKUP_ONLY
STATION_TOKEN_BACKEND_HARDENING=DEFERRED_DEBT_F07-D01
```

## 12. G1-G9 执行闭环附录

本计划前文记录的是 G0 的审计计划和冻结决策，不能继续作为当前实现状态的唯一入口。G1-G8 实现、G7/G8-R 修补和 G9 集成证据已在当前分支完成，当前状态与测试事实以 [F07 G9 集成验收与 Final Evidence 闭环](./F07_G9_集成验收与FinalEvidence闭环.md) 为准。

```text
F07_SOURCE_EVIDENCE_SHA=a5f017d0d0ae6bf3ba20ec85488bb5afa96e21ce
F07_G9_STATE=DONE
F07_ENGINEERING_STATE=DONE
F07_SETTINGS_IMPORT_EXPORT=EXCLUDED
F07_REAL_HARDWARE_VALIDATION=NOT_PERFORMED
F07_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_SETTINGS_RETIREMENT=NOT_APPROVED
PRODUCTION_ACCEPTANCE=BLOCKED
```

已验证的本地证据包括 StudioUI `119 files / 721 tests`、typecheck、lint、production build、bundle gate/reproducibility、Desktop `744/744`、F07 Browser `18/18`、StudioUI Next Browser `159 total / 138 passed / 21 skipped / 0 failed` 和 Virtual PLC `83/83`。真实 Station、真实 LLM 产品质量、WebView2、Windows 125% DPI、Release publish、无 Node 目标机和完整 CI 仍为 `NOT PERFORMED`，不得将本地证据写成生产验收通过。
