# ClearVision 特色与竞争力 PPT 大纲（20页）

更新日期：2026-05-20

## 0. 使用口径

这份大纲用于制作一份介绍 ClearVision 产品特色、竞争力和当前成熟度边界的 20 页 PPT。建议整体基调定位为：

> ClearVision 是面向工业视觉检测的本地化 Studio + Runtime + Station 平台原型，它的核心竞争力不是单个算法，而是把“需求到流程、流程到运行、运行到现场、现场到证据”的链路工程化。

对外表达时建议坚持三条边界：

- 可以强调：平台骨架完整、算子覆盖广、AI 流程生成链路有工程约束、Station 现场链路成型、质量证据体系清晰。
- 谨慎表达：当前适合 Demo、内部试点、Beta 或 PoC，不宜直接宣称大规模生产级商用闭环。
- 避免表达：不要把 public dataset、semi-synthetic、field-substitute replay、smoke test 包装成真实产线签核。

## 1. 整体叙事线

PPT 的主线建议分成四章：

| 章节 | 页码 | 叙事任务 |
|---|---:|---|
| 为什么需要 ClearVision | 1-5 | 讲清工业视觉项目的真实痛点、竞品格局和 ClearVision 的切入点 |
| ClearVision 是什么 | 6-10 | 展示产品架构、Studio、算子、Runtime、AI 生成链路 |
| ClearVision 强在哪里 | 11-17 | 用线序场景包、Station、质量证据、OperatorLibrary 证明差异化 |
| 如何竞争与落地 | 18-20 | 对比竞品、明确成熟度边界、收束到产品价值和路线 |

## 2. 核心证明点

| 证明点 | 建议使用的证据 |
|---|---|
| 不是单点 OpenCV demo | `README.md`、`docs/项目总览.md`、产品审计报告中的端到端平台描述 |
| 算子体系有规模 | 质量矩阵：156 个正式算子，A=152，B=4 |
| 质量治理不是口号 | 质量矩阵：contract/golden/dataset/field replay/benchmark 分层证据 |
| AI 不是只拼 prompt | Prompt composer、parser、validator、DryRun、manual retry、模型配置与测试覆盖 |
| 现场链路成型 | Runtime package、Station sync、spool、health、deploy、audit |
| 有可讲的样板场景 | 端子线序检测场景包：模板、规则、标签、版本、样例、FAQ |
| 可复用交付资产 | `Acme.OperatorLibrary` NuGet 包、SBOM、第三方声明、锁文件和 smoke test |

## 3. 逐页规划

### 第 1 页：封面

**页面标题**

ClearVision：面向工业视觉检测的可验证流程平台

**这一页要证明什么**

先把 ClearVision 从“视觉算法 demo”定位到“工业视觉平台工程”，为后续讲架构、AI、Station 和证据体系铺垫。

**页面上写什么**

- 副标题：从视觉流程编排到现场运行与质量证据治理
- 标签：Studio / Runtime / Station / Operators / AI-assisted Flow / Quality Evidence
- 版本口径：2026-05，内部试点 / Beta 口径

**建议视觉**

一张横向产品链路图：`需求 -> 流程 -> 算子执行 -> 现场运行 -> 结果追溯 -> 质量证据`。背景可以用暗色工业工作站或检测线图片，但不要做成营销海报。

**讲稿要点**

> ClearVision 不是只解决“有没有算法”，而是解决工业视觉项目里更难复用、更难交付、更难证明的链路问题：流程怎么搭、怎么运行、怎么部署到现场、怎么留下质量证据。

**证据来源**

- `README.md`
- `docs/项目总览.md`
- `docs/产品审计报告-2026-05-16.md`

---

### 第 2 页：工业视觉项目真正的难点

**页面标题**

难点不只是识别，而是从需求到现场的断层

**这一页要证明什么**

说明 ClearVision 的价值来自工程化链路，而不是单个模型或算子。

**页面上写什么**

用四个痛点模块呈现：

| 痛点 | 典型表现 | 后果 |
|---|---|---|
| 需求翻译难 | 工艺语言很难直接变成视觉流程 | 高度依赖资深工程师 |
| 流程复用难 | 每个项目都从零搭流程、调参数 | 项目交付周期长 |
| 现场部署难 | 相机、PLC、运行包、权限、日志分散 | 调试风险集中到上线阶段 |
| 质量证明难 | 测试、样本、性能、现场记录口径混乱 | 对外承诺不稳 |

**建议视觉**

左侧画“传统交付断点”：需求、算法、流程、现场、证据之间有断裂；右侧留一个空位，为下一页引出平台化解决方案。

**讲稿要点**

> 工业视觉最容易被低估的是交付复杂度。算法只是其中一段，真正决定能不能落地的是流程、资源、现场通信、运行稳定性和证据闭环。

**证据来源**

- `docs/产品审计报告-2026-05-16.md`
- `docs/面试/面试资产库/业务正确性三层验证说明.md`

---

### 第 3 页：竞品格局

**页面标题**

视觉软件市场已经成熟，但切入点各不相同

**这一页要证明什么**

承认竞品强度，避免把 ClearVision 讲成没有对手；同时为后面“差异化切入”做铺垫。

**页面上写什么**

| 类型 | 代表产品 | 典型优势 | ClearVision 应避开的正面战场 |
|---|---|---|---|
| 国际 PC 视觉软件 | Cognex VisionPro | 成熟 PC 视觉软件、工业客户基础强 | 不拼品牌与生态沉淀 |
| 算法 SDK / 通用视觉库 | MVTec HALCON | 算法深度、3D、深度学习、开发者生态强 | 不拼底层算法大全 |
| 无代码/低代码视觉平台 | MVTec MERLIC、Zebra Aurora Vision Studio、NI Vision Builder AI | 图形化配置、部署路径成熟 | 不只拼“能拖拽流程” |
| 硬件一体机 | Keyence CV-X | 相机、控制器、软件、服务一体化 | 不拼硬件闭环和现场销售网络 |
| 国产机器视觉平台 | Hikrobot VisionMaster | 本土生态、图形化开发、算子和硬件配套 | 不拼全量商用生态 |

**建议视觉**

用二维坐标图：横轴“硬件绑定强 -> 开放软件强”，纵轴“算法 SDK -> 场景工作站”。把 ClearVision 放在“开放软件 + 场景工作站 + 工程证据”区域。

**讲稿要点**

> 竞品并不弱，ClearVision 的机会不在于说别人没有，而在于选择一个更适合当前项目的切入点：开放、可控、可二次开发、可证据化。

**证据来源**

- Cognex VisionPro 官方资料
- MVTec HALCON / MERLIC 官方资料
- Keyence CV-X 官方资料
- Hikrobot VisionMaster 官方资料
- Zebra Aurora Vision Studio 官方资料
- NI Vision Builder AI 官方资料

---

### 第 4 页：竞品强项与空白

**页面标题**

成熟竞品解决“能用”，ClearVision 聚焦“可控地交付”

**这一页要证明什么**

把竞品优点讲清楚，再指出 ClearVision 的差异化空间：不是替代全部竞品，而是补足开放工程链路和证据治理。

**页面上写什么**

| 维度 | 竞品常见强项 | ClearVision 的可竞争角度 |
|---|---|---|
| 算法能力 | 国际厂商和成熟 SDK 积累深 | 用开放算子库 + 质量矩阵呈现可验证边界 |
| 图形化配置 | 多数竞品已有拖拽或无代码能力 | 增加 LLM 模板化流程生成和 DryRun |
| 现场部署 | 硬件一体机优势明显 | Runtime Package + Station opt-in 同步 |
| 工程开放性 | 商业软件黑盒较多 | .NET 工程、NuGet 算子包、可审计代码链路 |
| 质量证据 | 竞品依赖品牌和成熟案例 | 用 contract/golden/dataset/field replay 明确证据等级 |

**建议视觉**

做一张“优势雷达图”或“竞争空白图”，不要做成简单红绿表。ClearVision 的区块标注为“开放工程平台 + AI辅助流程 + 证据化交付”。

**讲稿要点**

> ClearVision 不应该讲成“全面超过 Cognex 或 MVTec”。更稳的讲法是：成熟产品证明了市场需求，而 ClearVision 在开放工程骨架、AI流程生成和证据化交付上建立自己的差异化。

**注意措辞**

- 不说“全面替代国际一线视觉软件”
- 改说“面向内部试点和可控场景，提供更开放、更可审计的工程化实现”

---

### 第 5 页：ClearVision 的战略切入点

**页面标题**

从“搭流程”切入，把经验变成可复用资产

**这一页要证明什么**

明确产品最重要的竞争路径：把视觉工程师经验沉淀到模板、规则、算子和质量证据中。

**页面上写什么**

一句主张：

> ClearVision 的核心不是堆算子，而是把工业视觉项目的经验沉淀成可复用、可校验、可运行、可追溯的流程资产。

四个关键词：

- 模板化：高频场景先固化骨架
- 可校验：Validator 检查结构、连线、参数和资源
- 可预演：DryRun 在上线前暴露链路问题
- 可追溯：质量矩阵、运行包、Station 日志和结果摘要形成证据

**建议视觉**

中间放“流程资产”核心圆，四周放模板、算子、规则、证据。避免用泛泛的“AI 大脑”图。

**讲稿要点**

> 传统经验通常存在工程师脑子里。ClearVision 的方向是把经验变成模板、规则、参数边界和证据包，让下一次类似项目不再从零开始。

---

### 第 6 页：产品全景架构

**页面标题**

一套从 Studio 到 Station 的本地化视觉平台骨架

**这一页要证明什么**

展示 ClearVision 的端到端结构，证明它不是孤立功能集合。

**页面上写什么**

模块链路：

1. Studio：项目、流程、算子配置、图像预览、检测运行
2. Local API：项目、模板、算子、检测、Station、设置接口
3. Runtime：流程执行、运行包导出、结果归一化
4. Operators：图像、测量、标定、AI、通信、流程控制
5. Station：现场运行、健康、结果摘要、部署命令、审计
6. Quality：测试、benchmark、质量矩阵、field replay

**建议视觉**

使用一张平台架构图，建议分三层：

- 上层：Studio / Station 用户入口
- 中层：Local API / Runtime / FlowExecutionService
- 下层：Operators / Models / PLC / SQLite / Quality Evidence

**讲稿要点**

> 这页要让观众一眼看到 ClearVision 的“平台感”：桌面工作台不是孤岛，后面有本地 API、运行时、算子层、现场端和质量证据体系。

**证据来源**

- `README.md`
- `docs/runtime/ClearVision-Runtime-Design.md`
- `docs/runtime/station-studio-sync.md`

---

### 第 7 页：Studio 工作台

**页面标题**

视觉工程师的主工作台：配置、预览、运行、追溯

**这一页要证明什么**

说明 ClearVision 已经具备完整的用户工作流入口，而不是只有后端算法。

**页面上写什么**

Studio 当前覆盖：

- 项目管理：创建、打开、导入导出、Demo 工程
- 流程编辑：画布、节点、连线、属性面板、ROI 编辑
- 算子配置：参数、输入输出、预览结果
- 检测执行：单次检测、连续检测、SSE 实时结果
- 结果看板：历史记录、统计、趋势、缺陷分布
- 设置中心：相机、PLC、AI 模型、用户管理
- Station 监控：在线状态、健康、命令、部署

**建议视觉**

用一张 Studio UI 截图做主视觉，旁边用 5 个小标签标注关键区域。没有截图时，用低保真界面线框替代。

**讲稿要点**

> 这部分是视觉工程师最直接接触的入口。它把算法能力包装成可操作的项目、流程、参数和结果，而不是要求用户直接写代码。

**证据来源**

- `Acme.Product/src/Acme.Product.Desktop/wwwroot/src/`
- 产品审计报告 4.1 Studio 主工作台

---

### 第 8 页：本地 API 与 Runtime

**页面标题**

桌面不是壳，背后有本地运行时服务

**这一页要证明什么**

展示 ClearVision 的架构深度：WinForms + WebView2 前端后面是本地 ASP.NET Core endpoints 和共享运行时。

**页面上写什么**

关键能力：

- Desktop 内嵌本地 ASP.NET Core minimal APIs
- API 覆盖项目、检测、算子、模板、AI、设置、相机、PLC、Station
- Runtime 负责流程执行、运行包导出、运行包校验
- Station 复用 FlowExecutionService、operator factory、变量上下文和 executor 注册
- Runtime Package V1 包含 `package.json`、`flow.json`、runtime profile、quality validation、field profile

**建议视觉**

做一张“同一流程在 Studio 编辑、Runtime 执行、Station 加载”的三段图。箭头要表达真实数据流，不要泛化成“前端 -> 后端 -> 数据库”。

**讲稿要点**

> 这套结构的意义是：Studio 负责编辑和调试，Runtime 负责可运行包，Station 负责现场执行。它们共享执行引擎，降低了编辑态和运行态不一致的风险。

**证据来源**

- `docs/runtime/ClearVision-Runtime-Design.md`
- 产品审计报告 4.2 本地 API 层

---

### 第 9 页：算子能力地图

**页面标题**

156 个算子构成工业视觉流程的积木层

**这一页要证明什么**

展示算子覆盖广度，同时避免把数量当作唯一卖点。

**页面上写什么**

质量矩阵当前口径：

- 正式算子总数：156
- A 级：152
- B 级：4
- 有任意证据信号：156
- Contract evidence：117
- Golden test：46 + 1 partial
- Dataset evidence：17 + 6 partial
- Field replay：22

能力分组：

- 图像预处理：滤波、阈值、形态学、几何变换
- 定位与匹配：模板匹配、形状匹配、特征匹配、ROI 跟踪
- 测量与标定：卡尺、宽度、几何测量、相机/手眼/鱼眼标定
- AI 检测：DeepLearning、SemanticSegmentation、AnomalyDetection
- 3D：点云滤波、RANSAC、PPF、聚类
- 通信与输出：PLC、TCP、Serial、HTTP、DatabaseWrite、ResultOutput
- 流程控制：条件、循环、变量、异常处理、统计

**建议视觉**

用“能力地图”而不是长列表。中心是 Flow，周围 7 个模块，每个模块列 3-5 个代表算子。

**讲稿要点**

> 算子数量能说明平台覆盖面，但更重要的是每个算子背后是否有证据。这里要把“功能成熟度”和“证据成熟度”分开讲。

**注意措辞**

- 不说“156 个算子都已完成真实产线验证”
- 改说“156 个正式算子均进入质量矩阵，其中证据类型和覆盖深度分层管理”

**证据来源**

- `quality/evals/reports/operator_quality_matrix.md`
- `算子资料/算子目录.md`

---

### 第 10 页：AI 辅助流程生成

**页面标题**

AI 不直接接管现场，而是辅助生成可审计流程草案

**这一页要证明什么**

讲清 ClearVision 的 AI 差异化：不是“让 LLM 自由发挥”，而是模板优先、约束生成、校验和预演。

**页面上写什么**

核心链路：

`自然语言需求 -> Requirement Brief -> 模板匹配 -> Prompt Composer -> Flow JSON -> Parser -> Validator -> DryRun -> 人工确认 -> 运行`

关键机制：

- 模板优先：高频场景先命中稳定骨架
- 输出约束：要求结构化流程对象，而不是自由文本
- Validator：检查算子、端口、连线、参数、环路、资源缺口
- DryRun：上线前预演链路是否可运行
- 人工确认：明确 pending parameters 和 missing resources

**建议视觉**

做一条“AI 生成流水线”，每个节点下面标注它消除哪类风险：结构风险、运行风险、资源风险、业务确认风险。

**讲稿要点**

> ClearVision 的 AI 不是魔法按钮。它真正有价值的地方，是把 LLM 的不确定性收进模板、校验器、DryRun 和人工确认边界里。

**证据来源**

- `docs/面试/面试资产库/深聊主线-LLM编排与模板化收缩.md`
- `docs/面试/面试资产库/业务正确性三层验证说明.md`

---

### 第 11 页：三层验证模型

**页面标题**

格式正确，不等于业务正确

**这一页要证明什么**

这是 PPT 的认知抓手：用三层验证模型体现 ClearVision 的工程克制和可信度。

**页面上写什么**

| 层级 | 检查问题 | 通过后说明 | 不能说明 |
|---|---|---|---|
| Schema 合法 | JSON、算子、端口、参数格式是否正确 | 系统能识别流程草案 | 不代表能跑 |
| 链路可运行 | 端口类型、资源、DryRun、运行时是否通过 | 流程可进入执行链路 | 不代表业务正确 |
| 业务结果正确 | 样本、规则、ROI、阈值、NG 原因是否符合验收 | 该场景下做对了事 | 不代表全部场景通用 |

**建议视觉**

用三层阶梯或漏斗图，最上面是 Schema，最下面是业务验收。每层旁边放一个“仍需确认”的小提示。

**讲稿要点**

> 这页能体现 ClearVision 的边界意识。很多 AI 生成工具停留在“返回合法 JSON”，但工业视觉必须继续追问：能不能跑？跑出来是不是业务上真的对？

**证据来源**

- `docs/面试/面试资产库/业务正确性三层验证说明.md`

---

### 第 12 页：线序检测样板场景

**页面标题**

用端子线序检测证明流程资产如何落地

**这一页要证明什么**

把前面的抽象架构落到一个具体可讲的样板场景上。

**页面上写什么**

场景目标：

- 检测端子线束中不同颜色线缆的顺序是否符合规则
- 输出 OK / NG 以及可解释的 NG 原因

基线流程：

`ImageAcquisition -> DeepLearning(OutputFormat=EndToEndNms) -> DetectionSequenceJudge -> ResultOutput`

视频流扩展：

`ImageAcquisition(Continuous) -> FrameChangeTrigger -> DeepLearning -> DetectionSequenceJudge -> ResultOutput`

关键规则：

- 模型标签顺序和业务期望顺序分开管理
- DeepLearning 的置信度可自动调参
- ExpectedLabels、ExpectedCount、ModelPath 需要人工确认
- NMS 阈值由导出的 ONNX 模型负责，平台侧不重复接管

**建议视觉**

左侧放流程图，右侧放“业务正确性检查清单”：标签、数量、排序方向、ROI、阈值归属、NG 解释。

**讲稿要点**

> 线序检测是最适合展示 ClearVision 的场景，因为它同时需要 AI 检测、规则判断、模板复用、参数边界和结果解释。

**证据来源**

- `线序检测/scenario-package-wire-sequence/README.md`
- `线序检测/scenario-package-wire-sequence/template/terminal-wire-sequence.flow.template.json`
- `线序检测/scenario-package-wire-sequence/rules/sequence-rule.v1.json`

---

### 第 13 页：场景包机制

**页面标题**

场景包把一次项目经验沉淀成下一次可复用资产

**这一页要证明什么**

说明 ClearVision 的复用不是简单复制流程文件，而是有 manifest、模板、模型、规则、标签、样例、FAQ 和版本策略。

**页面上写什么**

线序检测场景包目录契约：

- `manifest.json`：当前激活包清单
- `versions/`：不可变版本描述
- `template/`：流程模板
- `models/`：模型与模型版本说明
- `rules/`：期望顺序、容差、NG 原因
- `labels/`：模型 classId 到 label 的对齐
- `samples/`：样本引用和调参验证
- `faq/`：场景知识与故障排查

版本策略：

- 模型、模板、规则任一变化都应登记新版本
- `manifest.json` 指向当前激活版本
- 每个发布快照记录在 `versions/<packageVersion>/release.json`

**建议视觉**

做成“场景包文件夹展开图”，每个文件夹旁边标注它服务的生命周期：设计、调参、验证、交付、复盘。

**讲稿要点**

> 场景包的价值是把项目经验从口头经验变成结构化资产。它不仅保存流程，还保存规则、模型、标签、版本和排障知识。

**证据来源**

- `线序检测/scenario-package-wire-sequence/README.md`

---

### 第 14 页：Station 现场链路

**页面标题**

从调试态走向现场态：Station 保持本地自治

**这一页要证明什么**

说明 ClearVision 已经考虑现场运行、断网、结果摘要、命令和部署，不是只在开发机上跑 demo。

**页面上写什么**

Station 同步核心原则：

- Station 检测保持本地自治
- Studio 作为监控、命令、包源和持久化对端
- Station 主动出站连接 Studio，不新增 Station HTTP Server
- 图片不通过同步链路传输，只传结果摘要、健康、日志、命令、游标、包元数据
- 同步默认关闭，必须 opt-in

现场能力：

- 心跳与健康状态
- 结果摘要队列与本地 spool
- backpressure / dropped / spool 指标
- 远程命令与部署包
- hash、manifest、版本检查与回滚
- 审计记录

**建议视觉**

画“Studio 监控中心”和“多个 Station”之间的 LAN 同步图。箭头从 Station 指向 Studio，突出出站连接和本地自治。

**讲稿要点**

> 现场系统最怕为了上传监控数据影响检测主路径。ClearVision 的设计是 Station 本地检测优先，Studio 同步是监控路径，队列拥塞时宁可丢摘要也不阻塞检测。

**证据来源**

- `docs/runtime/station-studio-sync.md`
- 产品审计报告 4.5 Runtime Package 与 Station

---

### 第 15 页：工业集成能力

**页面标题**

视觉流程必须接得进工厂，而不是只跑在算法环境里

**这一页要证明什么**

展示 ClearVision 对相机、PLC、通信、数据库和外部系统的接入意识。

**页面上写什么**

当前技术栈覆盖：

- 相机与图像采集：ImageAcquisition、相机设置入口
- PLC：Modbus、Siemens S7、Mitsubishi MC、Omron FINS
- 通信：TCP、Serial、HTTP Request、MQTT 边界说明
- 数据与结果：SQLite、DatabaseWrite、ResultOutput、JSON/CSV/Text
- 触发与流程：TriggerModule、FrameChangeTrigger、条件分支、变量、TryCatch
- 模型：ONNX Runtime、模型 manifest、hash / labels / dataset / hardware profile 字段

**建议视觉**

做工厂集成拓扑图：相机、PLC、Station、Studio、数据库、MES/API。每条线标注协议或数据类型。

**讲稿要点**

> 工业视觉项目不是算法闭环，而是现场系统闭环。ClearVision 在产品架构里把 PLC、触发、结果输出、Station 和质量证据都纳入了同一条链路。

**注意措辞**

- MQTT 当前应谨慎表述，避免讲成已成熟生产功能。
- 可以说“通信能力边界已纳入算子体系和质量矩阵，其中部分能力仍需按发布口径标注成熟度”。

**证据来源**

- `README.md`
- `quality/evals/reports/operator_quality_matrix.md`
- 产品审计报告风险清单

---

### 第 16 页：质量证据体系

**页面标题**

把“能跑”拆成可审计的证据等级

**这一页要证明什么**

ClearVision 的质量体系是重要竞争力，尤其适合对比黑盒软件或普通 demo。

**页面上写什么**

证据分层：

| 证据类型 | 解决的问题 | 当前用途 |
|---|---|---|
| Contract | 输入输出、参数和错误语义是否稳定 | 基础契约 |
| Golden | 典型样本结果是否一致 | 回归验证 |
| Dataset | 数据集样本上是否有效 | 算法有效性参考 |
| Field replay | 现场替代/回放路径是否覆盖 | 场景可信度增强 |
| Benchmark | 性能预算是否可接受 | 性能承诺边界 |
| CI Gate | 是否进入持续验证 | 发布流程约束 |

质量矩阵摘要：

- 156 个正式算子
- 117 个有 contract evidence
- 46 + 1 partial 有 golden test
- 17 + 6 partial 有 dataset evidence
- 22 有 field replay

**建议视觉**

用“证据金字塔”：底层 contract，中间 golden/dataset/benchmark，上层 field replay，顶层真实产线签核。把“真实产线签核”标为下一阶段目标。

**讲稿要点**

> 这页要强调 ClearVision 的诚实口径：有功能不等于有现场证据。我们把证据分层，是为了知道哪些能力可以演示，哪些可以试点，哪些还不能对外承诺。

**证据来源**

- `quality/evals/reports/operator_quality_matrix.md`
- `docs/engineering/ci-quality-gates.md`
- `docs/engineering/evidence-artifacts.md`

---

### 第 17 页：OperatorLibrary 独立算子包

**页面标题**

把平台能力拆成可复用、可交付的 NuGet 算子包

**这一页要证明什么**

展示 ClearVision 不只是一个单体应用，也在沉淀可复用工程资产。

**页面上写什么**

OperatorLibrary 当前能力：

- 独立 NuGet 包：`Acme.OperatorLibrary`
- 版本基线：`1.0.2`
- 复用主工程算子源码，使用 MSBuild linked compile items
- 覆盖图像处理、测量、标定、通信、流程控制、AI
- 提供 package metadata、SBOM、third-party notices、symbols
- 支持 CI 版本注入、SourceRevisionId、RepositoryCommit、locked restore
- 有 smoke / acceptance 测试覆盖代表算子

**建议视觉**

用“主产品 -> 算子层 -> NuGet 包 -> 外部宿主”的拆分图，说明它降低二次集成成本。

**讲稿要点**

> OperatorLibrary 的意义是把算子从 ClearVision 主应用里解耦出来，形成可复用交付单元。对内部平台化、客户定制和第三方宿主集成都有价值。

**证据来源**

- `Acme.OperatorLibrary/README.md`
- `docs/operator-library/release-package-industrialization.md`

---

### 第 18 页：竞争力矩阵

**页面标题**

ClearVision 的竞争力来自组合，而不是单点领先

**这一页要证明什么**

把 ClearVision 与竞品放在同一张矩阵里，展示差异化但保持克制。

**页面上写什么**

建议使用“强 / 中 / 弱 / 待验证”四档，不用绝对打分。

| 维度 | 国际成熟视觉软件 | 硬件一体机 | 国产平台 | ClearVision 当前状态 |
|---|---|---|---|---|
| 算法深度 | 强 | 中-强 | 中-强 | 中，覆盖广但证据分层 |
| 图形化配置 | 强 | 强 | 强 | 中-强，Studio 已成型 |
| AI 流程生成 | 中，偏工具增强 | 弱-中 | 中 | 强差异化，但需继续现场验证 |
| 开放工程性 | 中 | 弱 | 中 | 强，.NET + 本地 API + NuGet |
| 现场运行链路 | 强 | 强 | 强 | 中，Station 成型但需签核 |
| 质量证据透明度 | 依赖厂商背书 | 依赖厂商背书 | 依赖厂商/项目 | 强差异化，矩阵可审计 |
| 商业成熟度 | 强 | 强 | 强 | 待验证 / Beta |

**建议视觉**

用颜色要克制：ClearVision 不要全绿。把“当前优势”和“待验证项”都明确显示，可信度会更高。

**讲稿要点**

> ClearVision 的竞争策略不是宣称每一项都领先，而是用开放工程性、AI流程生成、场景包和质量证据形成组合优势。商业成熟度则要诚实地放在待验证区。

**注意措辞**

- 不要写“完胜”
- 推荐写“差异化优势明显，但仍处于现场证据补齐阶段”

---

### 第 19 页：当前成熟度与路线图

**页面标题**

下一阶段目标：从可演示走向现场可信交付

**这一页要证明什么**

主动交代边界和路线，增强可信度。

**页面上写什么**

当前成熟度判断：

| 阶段 | 当前状态 | 可承诺 | 不应承诺 |
|---|---|---|---|
| Demo / 展示版 | 已具备 | 平台完整性、核心流程、样板场景 | 真实产线签核 |
| 内部试点版 | 接近/部分具备 | 运行包、Station、虚拟PLC、spool、日志 | 长周期无人值守 |
| 生产候选版 | 待补齐 | 可控产线 PoC | 泛行业全自动适配 |

30-60 天路线建议：

1. 以线序检测为主样板补真实样本、硬件 profile、报告 ID 和签核记录
2. 补齐模型 manifest 的 hash、datasetVersion、hardwareProfile、reportId
3. 做 Station 断网、重启、队列满、部署失败、回滚演练
4. 把质量矩阵拆成“功能成熟度”和“证据成熟度”两张对外表
5. 修正 placeholder / 实验功能在 UI、目录、文档中的口径

**建议视觉**

用路线图：Now / Next / Later 三段。Now 写“可演示平台骨架”，Next 写“场景证据包”，Later 写“生产候选闭环”。

**讲稿要点**

> ClearVision 下一步最重要的不是继续堆功能，而是把一个真实场景的闭环证据做扎实：样本、模型、规则、运行包、Station、性能、日志和签核。

**证据来源**

- `docs/产品审计报告-2026-05-16.md`

---

### 第 20 页：结论

**页面标题**

ClearVision 的价值：让工业视觉流程更快搭建、更可复用、更可验证

**这一页要证明什么**

用一句强结论收束整份 PPT。

**页面上写什么**

主结论：

> ClearVision 把工业视觉项目从“靠经验搭流程”推进到“模板化生成、可校验预演、可运行部署、可证据追溯”的工程化链路。

三句话总结：

- 对视觉工程师：减少从零搭流程，把经验沉淀成模板和场景包
- 对集成与现场：通过 Runtime Package 和 Station 打通运行、部署、健康和结果摘要
- 对管理与交付：用质量矩阵和证据等级讲清功能边界、风险边界和发布边界

最后一句：

> 它当前最适合从高价值可控场景切入，以线序检测为样板，逐步补齐真实现场证据，走向生产候选版本。

**建议视觉**

回到第 1 页的链路图，但这次把每个节点都点亮：需求、模板、流程、运行、现场、证据。形成首尾呼应。

**讲稿要点**

> 最后一页不要再堆功能，要强调 ClearVision 的本质价值：不是替代所有成熟视觉软件，而是在可控场景里提供更开放、更可审计、更容易持续迭代的工业视觉平台路径。

## 4. 可直接放入 PPT 的关键金句

- 工业视觉真正难的不是“有没有一个算法”，而是“能不能把需求稳定翻译成现场可运行、可验证、可追溯的流程”。
- ClearVision 的核心竞争力不是算子数量，而是把流程、算子、运行包、Station 和质量证据放进同一条工程链路。
- AI 在 ClearVision 中不是替代工程师，而是把需求转成可审计流程草案，再由模板、Validator、DryRun 和人工确认收敛风险。
- 格式正确不等于业务正确；能运行不等于能交付；有数据集结果不等于真实产线签核。
- ClearVision 当前最稳的商业化路径，是先做强一个真实场景证据包，再复制到更多高频工业视觉场景。

## 5. 竞品资料链接

以下链接用于制作第 3、4、18 页竞品部分。PPT 中建议只保留短链接或来源脚注，不要堆大段引用。

- Cognex VisionPro：<https://www.cognex.com/products/machine-vision/vision-software/visionpro-software>
- MVTec HALCON：<https://www.mvtec.com/products/halcon>
- MVTec MERLIC：<https://www.mvtec.com/products/merlic>
- Keyence CV-X：<https://www.keyence.com/products/vision/vision-sys/cv-x100/>
- Hikrobot VisionMaster：<https://www.hikrobotics.com/en/machinevision/visionmaster/>
- Zebra Aurora Vision Studio：<https://www.zebra.com/us/en/products/oem/software/aurora-vision-studio.html>
- NI Vision Builder for Automated Inspection：<https://www.ni.com/en-us/shop/product/vision-builder-for-automated-inspection.html>
- Basler pylon vTools：<https://www.baslerweb.com/en/software/pylon-vtools/>

## 6. 本地证据索引

- `README.md`
- `docs/项目总览.md`
- `docs/产品审计报告-2026-05-16.md`
- `docs/runtime/ClearVision-Runtime-Design.md`
- `docs/runtime/station-studio-sync.md`
- `docs/engineering/ci-quality-gates.md`
- `quality/evals/reports/operator_quality_matrix.md`
- `Acme.OperatorLibrary/README.md`
- `线序检测/scenario-package-wire-sequence/README.md`
- `线序检测/scenario-package-wire-sequence/template/terminal-wire-sequence.flow.template.json`
- `线序检测/scenario-package-wire-sequence/rules/sequence-rule.v1.json`
- `docs/面试/面试资产库/深聊主线-LLM编排与模板化收缩.md`
- `docs/面试/面试资产库/业务正确性三层验证说明.md`

