---
title: "ClearVision 现场使用者体验审视与改进计划"
doc_type: "plan"
status: "active"
topic: "field usability review"
created: "2026-05-20"
updated: "2026-05-20"
owner: "Product UX Review"
---

# ClearVision 现场使用者体验审视与改进计划

## 审视口径

本计划从现场软件使用者角度审视，包括一线操作员、设备工程师、班组长、现场维护人员和交付实施人员。重点不是代码架构优雅性，而是现场是否敢用、好用、能追责、能排障、能避免误操作。

本次证据来自仓库静态审阅和已有文档，不包含真实相机、PLC、工站硬件、产线节拍下的联调验证。因此，涉及现场影响的内容属于基于证据的产品判断；涉及文件和行号的内容是可复核证据。

## 总体判断

ClearVision 已经有比较完整的流程编辑、检测运行、结果展示、工站部署、设置中心和算子库框架。但从现场使用者角度看，当前体验存在几个高风险方向：

- 关键生产操作的误触防护不足，尤其是远程停止、部署、测试包下发一类操作。
- 检测结果的追溯、全量导出和看板可信度不够直接，容易让现场人员怀疑“看到的是不是实际数据”。
- 失败提示偏开发人员语言，现场人员遇到断连、服务失败、权限不可用时缺少可执行步骤。
- 设置、算子、AI、存储等高级能力暴露得比较工程化，术语混杂，缺少面向班组和设备人员的解释边界。
- 部分模板、默认值、乱码和静态展示会降低交付可信度，尤其是在验收、培训、售后排障时。

## 优先级定义

| 优先级 | 定义 | 处理建议 |
| --- | --- | --- |
| P0 | 可能导致误操作、误判生产结果、无法追溯或明显损害现场信任 | 进入近期计划，优先修正 |
| P1 | 明显影响现场效率、交付解释成本、故障排查成本 | 分批进入体验改进 |
| P2 | 影响专业感、学习成本或长期维护体验 | 跟随相关模块迭代修正 |

## 优先级总表

| ID | 优先级 | 问题 | 现场影响 | 主要证据 |
| --- | --- | --- | --- | --- |
| FUX-P0-01 | P0 | 工站远程操作缺少二次确认和影响范围提示 | 误点停止、部署、测试下发会影响产线 | `stationMonitorView.js:673-733`, `stationMonitorView.js:936-942` |
| FUX-P0-02 | P0 | 检测结果追溯入口和全量导出不够直接 | 班组复盘、客户追责、异常回查困难 | `index.html:83-85`, `inspectionPanel.js:722-728`, `resultPanel.js:1085-1106` |
| FUX-P0-03 | P0 | 看板存在静态/未接入指标，数据可信度不足 | 现场人员无法判断哪些是真实数据 | `index.html:383-455`, `index.html:570-590`, `resultPanel.js:874-923`, `resultPanel.js:1759-1777` |
| FUX-P0-04 | P0 | 网络/服务错误提示偏开发人员 | 现场无法按提示独立恢复 | `httpClient.js:394-422` |
| FUX-P0-05 | P0 | 自动保存实际只写本地缓存，容易被理解为已保存工程 | 断电、换机、刷新后可能误以为工程已可靠落盘 | `app.js:1694-1725`, `app.js:1742-1757` |
| FUX-P1-01 | P1 | 算子库服务失败时回退默认算子 | 现场可能误以为默认/样例算子可用于真实配置 | `operatorLibrary.js:316-330`, `operatorLibrary.js:402-460` |
| FUX-P1-02 | P1 | 设置中心术语和入口偏工程化 | 班组和设备人员学习成本高，误配风险增加 | `settingsView.js:501-538`, `settingsView.js:3231-3299`, `settingsView.js:1443-1445` |
| FUX-P1-03 | P1 | 存储路径、清理等能力显示为可配置但功能不可用 | 现场可能配置了路径却无法通过软件完成选择/治理 | `featureRegistry.js:7-15`, `settingsView.js:3205-3213`, `settingsView.js:3274-3275` |
| FUX-P1-04 | P1 | 线序视频模板带未配置默认值，需要更强阻断 | 未配置 ROI、模型、标签时误运行会造成假失败或无意义结果 | `terminal-wire-sequence-video-stream.flow.template.json:47-59`, `terminal-wire-sequence-video-stream.flow.template.json:134-157` |
| FUX-P1-05 | P1 | 当前源代码/脚本存在乱码和编码风险 | 日志、脚本、验收输出不专业，排障关键词不可读 | `InspectionService.cs:472-476`, `package-portable-deployment.ps1:232-276` |
| FUX-P2-01 | P2 | AI 建议应用动作措辞不清 | 使用者不知道会改配置、改流程还是只改当前环境 | `aiPanel.js:372-378` |
| FUX-P2-02 | P2 | 欢迎和空状态偏演示口吻 | 现场软件第一屏应更任务导向 | `projectView.js:159-165`, `app.js:2298-2311` |
| FUX-P2-03 | P2 | UI 混用英文和工程词 | 培训、SOP 截图和跨班组交接成本增加 | `index.html:249`, `index.html:259`, `inspectionPanel.js:516-580`, `app.js:1898-1935` |

## 详细问题

### FUX-P0-01 工站远程操作缺少二次确认和影响范围提示

#### 现象

工站监控界面暴露了 `Ping`、`重载`、`停止`、`部署`、`生成测试包并下发` 等动作。按钮本身是必要能力，但从现场角度看，这些动作差异很大：`Ping` 是低风险诊断，`停止`、`部署`、`测试包下发` 则可能影响正在生产的工站。当前前端动作处理函数直接发起命令或部署请求，未看到按风险分级的二次确认、影响范围说明、当前运行状态校验、工站锁定状态提示。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js:936-942`：卡片中直接渲染 `Ping`、`重载`、`停止`、`部署`、`生成测试包并下发` 按钮。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js:673-701`：`handleStationAction` 针对 `ping/reload/stop/deploy/testDeploy` 直接分发动作。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js:716-733`：`createCommand` 和 `deployLatestPackage` 直接调用后端接口。
- 后端已有管理员校验，本项不是权限缺失结论；问题聚焦在现场误触防护和操作可理解性。

#### 现场影响

现场软件往往运行在触摸屏、远程桌面或多窗口环境里，误触概率高。一旦误点 `停止` 或 `部署`，对操作员而言最可怕的不是按钮做了什么，而是他不知道影响哪台站、哪条线、是否会中断当前节拍、能不能撤回、谁会被记录。

#### 建议动作

- 将操作分为低风险、生产影响、配置变更三类。
- 对 `停止`、`部署`、`测试包下发` 增加二次确认弹窗，弹窗必须包含站点名、IP、当前状态、将执行的动作、不可撤销或可能中断生产的说明。
- 对正在运行检测、正在部署、离线、未知状态的工站显示不同文案，避免使用同一套按钮语义。
- 增加操作审计反馈：操作人、时间、命令 ID、结果状态、失败原因。

#### 验收标准

- 低风险动作可一键执行，高风险动作必须二次确认。
- 二次确认中不能只写“确定执行吗”，必须说明影响范围。
- 操作后能在界面上看到命令 ID 或审计记录入口。

### FUX-P0-02 检测结果追溯入口和全量导出不够直接

#### 现象

结果中心入口在导航中默认隐藏，检测页只展示最近 8 条结果。结果导出存在“当前页导出”的提醒，服务端报表未准备好时会退回前端导出。对现场使用者而言，检测结果不是“报表功能”，而是质量追溯的核心证据链。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html:83-85`：`results` 导航按钮带 `hidden`、`aria-hidden="true"` 和 `tabindex="-1"`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/inspection/inspectionPanel.js:722-728`：最近结果使用 `slice(0, 8)`，只展示前 8 条。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/results/resultPanel.js:1085-1106`：导出时提示“当前筛选结果包含 ... 但当前仅加载了 ... 条。将导出当前页面数据。”并在服务端报表不可用时回退到前端报表。

#### 现场影响

当客户要求查看某个时间段、某个批次、某个 SN、某台设备的历史结果时，现场人员需要快速回答：结果在哪里、是否完整、是否可导出、导出文件是否来自服务器真实记录。如果入口隐藏或导出范围不明确，会让现场人员无法形成可靠追溯闭环。

#### 建议动作

- 将结果追溯作为一等入口，不应长期隐藏在导航中。
- 检测页最近结果保持轻量，但必须提供“查看全部结果”入口。
- 导出前明确显示导出范围：当前页、当前筛选全部、指定时间段、指定批次。
- 服务端报表不可用时不要静默降级为“看似完整”的前端报表，必须在文件名、提示和报表页脚标明数据来源和范围。

#### 验收标准

- 操作员可在 2 次点击内进入完整结果查询。
- 导出的报表能看出时间范围、筛选条件、记录总数、导出者和数据来源。
- 当前页导出与全量导出在文案和按钮上明确区分。

### FUX-P0-03 看板存在静态/未接入指标，数据可信度不足

#### 现象

结果看板和 KPI 区域存在多处静态文本、固定变化值或未接入数据时保留默认图形。静态样式对演示有帮助，但现场会把看板当作生产状态依据。只要有一个指标是“看起来像实时但实际不是”，整个系统的可信度都会下降。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html:383-387`：状态显示包含“监控运行中”“更新于 10秒前”等静态初始内容。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html:397-455`：良率、通过数、不良数、误报率、漏检率、平均耗时等卡片存在固定变化值，例如 `+5.2%`、`+6.1%`、`-1.5%`、`-0.8%`、`+0.8%`、`-0.1s`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html:570-590`：CPK、MTBF 区域存在固定变化值，例如 `+0.05`、`+12h`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/results/resultPanel.js:1759-1777`：高级分析接口未接入时使用 `noData` 或 `unavailable` 状态。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/results/resultPanel.js:920-923`：趋势数据不足时保留默认曲线路径。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/results/resultPanel.js:874-877`：雷达图数据不足时保留默认形状。

#### 现场影响

现场看板的核心价值是让人快速判断“现在是否正常”。如果图表在无数据时仍然像有数据，操作员会困惑，班组长会不信，客户验收时也会追问指标来源。尤其是良率、误报率、漏检率、CPK、MTBF 这些质量指标，必须比普通 UI 数字更严谨。

#### 建议动作

- 所有 KPI 增加数据来源状态：真实、估算、样例、暂无数据、接口不可用。
- 无数据时显示空状态，不保留默认趋势线或默认雷达形状。
- 固定变化值改为绑定真实历史窗口；没有历史窗口时显示 `--`。
- 看板标题附近显示最后更新时间、数据窗口、筛选条件和采样数量。

#### 验收标准

- 任意指标都能回答“来自哪个接口、统计哪个时间段、样本数是多少”。
- 无真实数据时不会出现看似真实的趋势线、雷达形状或同比变化。
- 现场截图可以用于质量会议，不需要额外解释“这些只是占位数据”。

### FUX-P0-04 网络/服务错误提示偏开发人员

#### 现象

当前 HTTP 客户端在连接失败时，会提示通过 Visual Studio F5、`dotnet run` 或浏览器控制台设置 `localStorage` 来处理。这对开发环境有帮助，但现场用户通常没有源码、IDE、命令行权限，也不应该被要求打开浏览器控制台。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/core/httpClient.js:394-422`：连接失败信息包含“确认 Acme.Product.Desktop 已通过 Visual Studio F5 或 dotnet run 启动”，以及 `localStorage.setItem('cv_api_base_url', 'http://host:port')`。

#### 现场影响

现场断连时，操作员需要知道的是：服务是否未启动、网络是否断开、IP 是否配置错误、是否需要联系维护、是否会影响当前检测。开发者提示会让现场人员觉得软件“没交付完成”，也会给售后带来大量重复沟通。

#### 建议动作

- 将错误提示分为开发模式和现场模式。
- 现场模式提示应包含：当前连接地址、最近一次成功连接时间、建议检查项、联系维护入口或日志包导出入口。
- 控制台命令、Visual Studio、`dotnet run` 等提示只在开发模式显示。
- 增加“一键复制诊断信息”按钮，复制服务地址、版本、错误码、请求 ID、时间戳。

#### 验收标准

- 现场用户不会看到 Visual Studio、`dotnet run`、浏览器控制台操作。
- 错误提示能指导用户完成至少 3 个非开发检查步骤。
- 售后人员可通过复制的诊断信息定位环境问题。

### FUX-P0-05 自动保存实际只写本地缓存，容易被理解为已保存工程

#### 现象

应用存在自动保存机制，但实现是定期把当前流程写入浏览器 `localStorage` 的 `cv_autosave_backup`。这不等价于工程文件保存、服务端保存或版本化保存。手动触发时文案虽然提到“本地缓存”，但入口语义仍容易让用户误以为工程已安全保存。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/app.js:1694-1725`：自动保存每 30 秒将流程保存到 `localStorage`，key 为 `cv_autosave_backup`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/app.js:1710-1715`：检测运行中跳过自动保存。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/app.js:1742-1757`：手动触发后提示“流程草稿已保存到本地缓存”。
- 静态搜索中目前只看到 `cv_autosave_backup` 写入和提示，未看到清晰的恢复入口或冲突处理流程。

#### 现场影响

现场最怕“以为保存了，其实没有”。断电、浏览器清缓存、换工控机、多人改同一流程时，本地缓存都不能承担工程资产保存责任。若没有恢复入口和差异提示，自动保存反而会制造错误安全感。

#### 建议动作

- 将“自动保存”改名为“本机草稿备份”，避免与工程保存混淆。
- 增加启动时恢复提示：检测到本机草稿，显示时间、流程名、节点数、与当前工程差异。
- 对正式工程保存提供明确按钮、保存位置、版本号和保存结果。
- 检测运行中跳过自动保存时，界面上显示原因，避免用户以为一直在保存。

#### 验收标准

- 用户能区分“本机草稿备份”和“正式工程保存”。
- 断电后重启能看到可恢复草稿，并能选择恢复或丢弃。
- 保存成功提示必须包含保存目标或工程版本。

### FUX-P1-01 算子库服务失败时回退默认算子

#### 现象

算子库加载失败后，会回退到默认算子数据并提示“使用默认算子数据”。默认算子包含示例化图标和参数。对开发和演示友好，但对现场配置可能危险，因为用户很容易把默认算子当成系统真实可用能力。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/operators/operatorLibrary.js:316-330`：`loadOperators` 失败后调用 `getDefaultOperators()` 并提示 `使用默认算子数据`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/operators/operatorLibrary.js:402-460`：默认算子数据包含图像采集、预处理、缺陷检测、尺寸测量、PLC 控制、AI 分类等样例算子。

#### 现场影响

算子是流程配置的生产能力入口。服务失败时继续显示默认算子，现场人员可能拖入样例算子并尝试运行，最终在更后面的环节失败。更严重的是，客户会误判系统能力边界。

#### 建议动作

- 服务失败时默认显示“算子库不可用”空状态，而不是填充可拖拽默认算子。
- 如果必须保留演示模式，需显式标记“演示算子，不可用于生产流程”。
- 算子卡片增加来源字段：平台算子、项目算子、演示算子、本地调试算子。

#### 验收标准

- 生产模式下服务失败不会展示可误用的默认算子。
- 演示算子有不可忽略的视觉标识。
- 拖入不可生产算子时会被阻止或强提示。

### FUX-P1-02 设置中心术语和入口偏工程化

#### 现象

设置中心承载很多能力，包括常量、存储、运行控制、AI、设备、产品规格等。但部分文案和分组偏工程内部语言，且中英文混杂，例如 `Runtime`、`Production Guards`、`Active`、`All`、`None`。这会让现场用户不知道哪些配置是日常可改，哪些配置需要工程师授权。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js:501-538`：设置中心侧栏和标题包含常量、存储、执行、设备、AI 等大量入口，主标题为“常量预设”。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js:3231-3233`：图像保存选项包含 `保存所有图像 (All)`、`不保存 (None)`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js:3287-3299`：执行配置中出现 `执行与控制 (Runtime)`、`Production Guards`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js:1443-1445`：AI 表格状态显示 `Active`。

#### 现场影响

现场配置通常有角色边界：操作员只改班次、批次、当前产品；设备工程师改相机、光源、PLC；算法工程师改模型和阈值；管理员改路径、权限和数据保留策略。如果所有入口都混在“设置中心”，并使用工程词，培训成本和误配风险都会增加。

#### 建议动作

- 按现场角色重新组织设置：生产参数、设备连接、数据与存储、算法与模型、系统维护。
- 高风险配置默认折叠，并显示“需要设备工程师/管理员权限”。
- 统一中文文案，保留英文只作为括号内协议名、字段名或技术缩写。
- 保存按钮分区化，不要让“保存所有更改”覆盖多个风险域。

#### 验收标准

- 操作员能在 30 秒内找到班次、批次、产品相关设置。
- 高风险设置有角色提示和变更影响说明。
- 中英文混杂项减少到确有必要的技术名词。

### FUX-P1-03 存储路径、清理等能力显示为可配置但功能不可用

#### 现象

功能注册表中标记 `storage.pathPicker` 不可用，但说明手动路径仍可持久化。设置页面中路径输入框存在，同时“选择目录”按钮不可用。立即清理也存在不可用状态。这类“看起来能配、但关键动作不可用”的体验在现场很容易造成卡顿。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/config/featureRegistry.js:7-15`：`storage.pathPicker` 标记为不可用，说明“Desktop directory picker is not wired yet; manual paths can still be persisted.”。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js:3205-3213`：默认保存路径输入框存在，“选择目录”按钮根据能力状态禁用。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js:3274-3275`：立即清理按钮也受功能可用性控制。

#### 现场影响

图像、结果、日志保存路径是现场交付必须确认的事项。不能选择目录，却允许手工输入路径，会把路径合法性、权限、磁盘空间、网络盘稳定性等问题推给现场人员。

#### 建议动作

- 若目录选择不可用，路径配置区域应明确标记“需由管理员在配置文件中设置”或“当前版本暂不支持选择目录”。
- 手工输入路径必须做路径存在性、权限、剩余空间校验。
- 清理不可用时说明原因和替代操作，不只禁用按钮。

#### 验收标准

- 用户知道为什么不能选目录，以及谁能处理。
- 保存路径前完成可写性校验。
- 清理不可用时有明确替代步骤。

### FUX-P1-04 线序视频模板带未配置默认值，需要更强阻断

#### 现象

线序视频流模板中 ROI 默认值为 `0,0,0,0`，模型路径和标签路径为空。模板本身包含 `parametersNeedingReview`，说明设计上知道这些参数必须复核。但从现场体验看，仅“需要复核”还不够，未配置状态应阻止运行、部署或发布。

#### 证据

- `线序检测/scenario-package-wire-sequence/template/terminal-wire-sequence-video-stream.flow.template.json:47-50`：`RoiX`、`RoiY`、`RoiWidth`、`RoiHeight` 默认均为 `0`。
- `线序检测/scenario-package-wire-sequence/template/terminal-wire-sequence-video-stream.flow.template.json:58-59`：`ModelPath`、`LabelsPath` 默认为空。
- `线序检测/scenario-package-wire-sequence/template/terminal-wire-sequence-video-stream.flow.template.json:134-157`：`parametersNeedingReview` 明确列出相机、ROI、模型路径、标签路径、期望标签、端子数量等需复核参数。

#### 现场影响

ROI、模型、标签是线序检测能否工作的重要前提。默认空值或零值如果进入运行，会产生“算法不好用”“相机没画面”“识别不到”的假象，排障成本高。

#### 建议动作

- 未配置 ROI、模型、标签时禁止运行、禁止部署、禁止发布。
- 在流程编辑器中给未配置节点加明显状态，例如“待配置，不能运行”。
- 运行前检查应输出面向现场的修复路径：打开哪个节点、配置哪个字段、示例值是什么。

#### 验收标准

- `0,0,0,0` ROI 不能进入生产运行。
- 空模型路径和空标签路径不能进入生产部署。
- 检查报告能直接定位到节点和参数名。

### FUX-P1-05 当前源代码/脚本存在乱码和编码风险

#### 现象

当前源代码中存在中文乱码日志；部署脚本生成 `.bat` 内容时包含中文提示，但写入编码使用 ASCII。日志和部署脚本都可能被现场人员、售后和客户看到，乱码会显著降低专业感，也会影响问题检索。

#### 证据

- `Acme.Product/src/Acme.Product.Application/Services/InspectionService.cs:472-476`：日志中出现 `娴佺▼鎵ц澶辫触`、`鍒ゅ畾缁撴灉` 等乱码。
- `scripts/package-portable-deployment.ps1:232-234`：生成的批处理内容包含中文提示。
- `scripts/package-portable-deployment.ps1:271-276`：对 `.bat` 文件使用 `Set-Content -Encoding ASCII` 写入，中文存在被替换或损坏风险。

#### 现场影响

售后排障常常依赖日志关键词。乱码意味着无法搜索“流程执行失败”“判定结果”等关键词；批处理提示乱码会让客户怀疑部署包质量。即使功能正常，交付观感也会被拉低。

#### 建议动作

- 修复当前源码中的乱码日志，确保源文件统一为 UTF-8。
- 生成 `.bat` 时避免写入中文，或改用合适编码并验证 Windows 控制台显示。
- 增加编码扫描检查，将明显 mojibake 词列入构建或发布前检查。

#### 验收标准

- 日志中的中文可读且可搜索。
- 便携部署包中的脚本在目标 Windows 环境显示正常。
- 发布前扫描不再出现已知乱码模式。

### FUX-P2-01 AI 建议应用动作措辞不清

#### 现象

AI 面板中存在“应用到环境”按钮。该文案对现场用户并不明确：是修改当前流程、写入项目配置、只影响本次运行环境，还是改变后续默认参数。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/ai/aiPanel.js:372-378`：建议卡片动作按钮显示 `应用到环境`。

#### 现场影响

AI 建议本身就需要建立信任。动作文案不清会让用户不敢点，或者误以为只是试用，实际却改变配置。现场软件应避免“黑箱式应用”。

#### 建议动作

- 根据实际行为改为更明确的按钮，例如“应用到当前流程草稿”“写入当前工站配置”“仅本次试用”。
- 应用前展示差异：改哪个节点、哪个参数、原值和新值。
- 高风险 AI 修改需要确认和撤销入口。

#### 验收标准

- 用户在点击前知道变更对象和持久化范围。
- AI 修改可预览、可撤销。

### FUX-P2-02 欢迎和空状态偏演示口吻

#### 现象

工程空状态和欢迎界面使用“创建您的第一个工程开始视觉检测之旅”“零代码搭建检测流程”等偏产品演示口吻。对销售演示友好，但现场生产软件更需要任务导向：打开现有工程、选择产品、连接设备、开始检测、查看最近异常。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/project/projectView.js:159-165`：空状态文案为“创建您的第一个工程开始视觉检测之旅”。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/app.js:2298-2311`：欢迎界面包含“零代码搭建检测流程”“AI智能诊断与优化建议”等介绍性模块。

#### 现场影响

现场每天打开软件不是为了了解产品能力，而是为了继续生产。欢迎语太像演示系统，会降低“这是正式产线工具”的感受。

#### 建议动作

- 首屏优先显示最近工程、当前产品、设备连接状态、最近异常、继续检测入口。
- 演示介绍可以放到帮助或首次上手模式，不应长期占据生产入口。
- 空状态提供明确任务：导入工程、创建工程、连接设备、打开样例。

#### 验收标准

- 老用户打开软件后能直接继续上次工作。
- 新用户空状态能引导完成第一条真实任务。

### FUX-P2-03 UI 混用英文和工程词

#### 现象

界面中存在 `ANALYSIS`、`RECENT RESULTS`、`Project JSON`、`Runtime Package`、`CYCLE TIME` 等英文或工程词。部分词对于开发人员准确，但对现场班组、质检和设备维护人员不够友好。

#### 证据

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html:249`：导航或区域文本包含 `ANALYSIS`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html:259`：结果区域文本包含 `RECENT RESULTS`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/inspection/inspectionPanel.js:516-517`：驱动标签包含“相机驱动 (Camera)”“流程驱动 (PLC触发)”。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/inspection/inspectionPanel.js:580`：耗时统计包含 `CYCLE TIME`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/app.js:1898-1935`：导入导出中出现 `Project JSON`、`Runtime Package`。
- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js:137`：状态文案包含 `等待真实 Station 结果`。

#### 现场影响

现场 SOP、培训材料、客户验收截图通常需要统一中文表达。英文工程词过多会让软件显得像内部调试工具，也会增加跨班组沟通成本。

#### 建议动作

- 建立现场中文术语表，例如“工程文件”“运行包”“最近结果”“节拍耗时”“工站”。
- 英文保留在技术附注或高级模式，不放在主要按钮和标题中。
- 所有导入导出格式说明增加用途解释，而不只写文件类型。

#### 验收标准

- 主要导航、按钮、卡片标题使用统一中文。
- 培训材料截图无需额外解释英文术语。

## 建议执行顺序

### M0 近期必须处理

- [ ] 给工站 `停止`、`部署`、`测试包下发` 增加风险确认和操作审计。
- [ ] 恢复或明确结果追溯入口，区分当前页导出和全量导出。
- [ ] 清理看板静态指标，未接入数据必须显示无数据或接口不可用。
- [ ] 将开发者错误提示拆分为开发模式和现场模式。
- [ ] 重命名并完善自动保存机制，区分本机草稿和正式工程保存。

### M1 近期体验改进

- [ ] 算子库服务失败时不展示可误用默认算子。
- [ ] 设置中心按现场角色和风险等级重组。
- [ ] 存储路径、清理功能增加不可用原因和可执行替代步骤。
- [ ] 线序模板未配置参数禁止运行、部署、发布。
- [ ] 修复当前源码和部署脚本编码问题。

### M2 持续优化

- [ ] AI 建议应用前增加差异预览和撤销。
- [ ] 首屏和空状态改为生产任务导向。
- [ ] 建立统一现场中文术语表，并逐步替换混杂英文。

## 证据复核方式

可使用以下命令复核主要证据点：

```powershell
rg -n "handleStationAction|deployLatestPackage|生成测试包并下发|停止|重载" Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js
rg -n "nav-results|RECENT RESULTS|ANALYSIS" Acme.Product/src/Acme.Product.Desktop/wwwroot/index.html
rg -n "当前仅加载|服务端报表|drawTrendChart|drawRadarChart|advanced" Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/results/resultPanel.js
rg -n "Visual Studio|dotnet run|localStorage.setItem" Acme.Product/src/Acme.Product.Desktop/wwwroot/src/core/httpClient.js
rg -n "cv_autosave_backup|本地缓存" Acme.Product/src/Acme.Product.Desktop/wwwroot/src/app.js
rg -n "getDefaultOperators|使用默认算子数据" Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/operators/operatorLibrary.js
rg -n "Production Guards|Runtime|保存所有图像|Active" Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/settings/settingsView.js
rg -n "RoiX|RoiY|RoiWidth|RoiHeight|ModelPath|LabelsPath|parametersNeedingReview" 线序检测/scenario-package-wire-sequence/template/terminal-wire-sequence-video-stream.flow.template.json
rg -n "娴佺▼|鍒ゅ畾|Set-Content -Encoding ASCII" Acme.Product/src/Acme.Product.Application/Services/InspectionService.cs scripts/package-portable-deployment.ps1
```

## 不作为本计划结论的范围

- 本计划不判断算法准确率、模型质量或真实硬件稳定性。
- 本计划不替代产线验收、设备联调、安全审计或权限审计。
- 本计划不要求一次性重做 UI，而是建议优先处理会影响现场信任、追溯、安全和排障的体验断点。
