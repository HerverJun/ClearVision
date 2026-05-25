# ClearVision 用法介绍 PPT 大纲（20 页）

> 目标：做一份介绍 ClearVision 使用方法的 PPT，从“系统是什么”讲到“用户如何从建项目、搭流程、验证检测、导出运行包到 Station 现场同步”。  
> 建议受众：视觉工程师、现场联调工程师、项目维护者、面试/答辩听众。  
> 建议时长：20-30 分钟。若压缩到 10 分钟，可保留第 1、3、6、8、10、11、12、13、17、18、20 页。

## 核实口径

本大纲基于当前仓库中的以下材料核实：

- `README.md`：项目定位、架构、技术栈、启动与验证命令。
- `docs/项目总览.md`：Studio、Runtime、Station、OperatorLibrary、Quality 的职责边界。
- `docs/功能使用说明-2026-05-16.md`：日常使用步骤与当前功能边界。
- `docs/产品审计报告-2026-05-16.md`：产品能力地图、成熟度判断、风险与路线。
- `docs/runtime/ClearVision-Runtime-Design.md`：Runtime package、RuntimeHost、Station MVP。
- `docs/runtime/station-studio-sync.md`：Station 同步、断网重放、命令和部署。
- `docs/frontend/realtime-communication.md`：Inspection SSE 与结果页实时语义。
- `算子资料/算子目录.md`：156 个正式算子分类与质量口径。
- `线序检测/scenario-package-wire-sequence/README.md`：端子线序检测场景包与模板流程。
- `quality/evals/reports/FrameChangeTrigger_*.md`：2026-05-20 生成的 `FrameChangeTrigger` contract、dataset、field-substitute 证据。

## 整体叙事

这份 PPT 不建议做成“功能堆叠清单”，而是按用户真实路径讲：

1. ClearVision 是什么，要解决什么问题。
2. 用户如何启动 Studio 并初始化环境。
3. 如何新建项目、拖拽算子、配置参数和 ROI。
4. 如何用预览、单次检测、连续检测验证流程。
5. 如何借助 AI 生成流程，但保留 Validator、DryRun 和人工确认。
6. 如何把流程导出成 Runtime Package 并交给 Station。
7. 如何用质量证据和功能边界支撑可信交付。

建议主线案例：端子线序检测。它能串起图像采集、DeepLearning、BoxFilter/BoxNms、DetectionSequenceJudge、ResultOutput、FrameChangeTrigger、Runtime Package、Station 和质量证据。

---

## 第 1 页：封面

**页面标题**  
ClearVision 用法介绍：从视觉流程到现场运行

**页面要表达的核心信息**  
ClearVision 是一套面向工业视觉检测的本地化 Studio + Runtime + Station 平台，核心价值是把视觉检测流程搭建、调试、部署和证据管理串起来。

**页面正文建议**  
放 3 行以内：

- 工业视觉流程编排与运行平台
- 覆盖项目、算子、检测、运行包、Station 与质量证据
- 示例主线：端子线序检测

**视觉设计建议**  
右侧放一条简化流程链：

```text
Studio 建项目 -> 流程画布 -> 节点预览 -> 检测执行 -> Runtime Package -> Station
```

背景可用深浅对比的工业工作台示意，不建议用抽象渐变。若后续做 PPT，可以放 Studio 主界面截图或架构图的半透明底图。

**讲解话术**  
“今天这份介绍不只讲 ClearVision 有哪些功能，而是按一个视觉工程师真正使用它的路径来讲：怎么启动、怎么搭流程、怎么调试、怎么检测、怎么导出运行包，最后怎么交给 Station 做现场同步。”

**注意边界**  
不要在封面写“成熟商用闭环平台”。更稳的说法是“工业视觉平台工程样机 / 内部试点版 / 可演示 Beta”。

---

## 第 2 页：为什么需要 ClearVision

**页面标题**  
工业视觉项目的难点

**页面要表达的核心信息**  
传统视觉项目的问题不只是算法，而是流程搭建、参数沉淀、现场部署和证据追溯经常分散。

**页面正文建议**  
分成 4 个痛点模块：

1. 流程搭建依赖经验：采集、预处理、检测、测量、判定、输出需要人工串接。
2. 场景复用困难：同类项目的 ROI、阈值、模型、规则和调参记录容易散落。
3. 现场部署链路长：Studio 调好后，还要考虑运行包、Station、PLC、相机和日志。
4. 质量证据不集中：contract、golden、dataset、field replay、性能报告和现场签核口径容易混在一起。

**视觉设计建议**  
用“问题流”而不是四张大卡片。可以画一条从“需求”到“现场运行”的链路，在每个环节标出阻塞点。

**讲解话术**  
“工业视觉不是写一个 OpenCV demo 就结束。真正难的是把图像来源、算法、业务规则、输出接口、现场运行和质量证据连接成稳定链路。”

**注意边界**  
不要说 ClearVision 已经解决所有工业视觉问题。这里的表达应是“把这些问题收进同一个工程骨架里”。

---

## 第 3 页：ClearVision 的使用全链路

**页面标题**  
从需求到现场运行的 7 步

**页面要表达的核心信息**  
用户使用 ClearVision 的标准路径是一条闭环：准备环境、建项目、搭流程、调参数、跑检测、导出运行包、Station 同步。

**页面正文建议**  
用 7 个步骤横向排列：

1. 启动 Studio，登录系统
2. 创建或打开项目
3. 在流程画布拖拽算子
4. 配置参数、ROI、模型、标签和输出
5. 用节点预览、单次检测、连续检测验证
6. 导出 Runtime Package
7. 在 Station 查看健康、结果、日志、部署和审计

**视觉设计建议**  
做一条横向时间轴。每个步骤配一个简短图标：登录、文件夹、流程节点、调参滑杆、播放、打包、站点。

**讲解话术**  
“这张图是后面 17 页的导航。ClearVision 的用法不是从某个孤立按钮开始，而是从一个检测任务一路推进到现场运行。”

**注意边界**  
这一页不要展开技术细节，只建立观众的地图感。

---

## 第 4 页：系统架构与主工作区

**页面标题**  
Studio + Local API + Runtime + Station

**页面要表达的核心信息**  
ClearVision 的桌面端不是单纯 UI，内部包含 WebView2 前端、本地 ASP.NET Core API、运行时服务和 SQLite 数据。

**页面正文建议**  
左侧列系统组件：

- Desktop Studio：WinForms + WebView2，负责项目、流程、预览、检测、设置和 Station 监控。
- Local API：内嵌 ASP.NET Core endpoints，提供 `/api/projects`、`/api/operators`、`/api/inspection`、`/api/settings`、`/api/stations` 等接口。
- Runtime：流程执行、运行包导出、RuntimeHost 状态机。
- Station：现场端运行、结果摘要、健康状态、spool、命令和包部署。
- Operator Library：可独立打包的算子 NuGet 项目。

**视觉设计建议**  
使用架构图：

```text
用户
  -> Studio UI
  -> 本地 API
  -> Application Services
  -> Runtime / Operators
  -> SQLite / Runtime Package / Station
```

**讲解话术**  
“Studio 是用户入口，但真正重要的是它后面有本地 API 和 Runtime。这样流程编辑、检测执行、Station 同步和运行包导出都能被同一套服务承接。”

**注意边界**  
Station 不托管 WebView2，也不直接引入 Kestrel；Station 保持本地运行自治。

---

## 第 5 页：启动与首次登录

**页面标题**  
使用前准备：环境、启动、初始化

**页面要表达的核心信息**  
ClearVision 当前主要面向 Windows 桌面环境，启动后需要初始化管理员并登录。

**页面正文建议**  
分 3 块：

**环境要求**

- Windows 10/11 x64
- `.NET SDK 9.0.300`
- Microsoft Edge WebView2 Runtime
- PowerShell
- SQLite 由 Studio 初始化
- Node.js 20 仅 UI/Playwright 测试需要

**启动命令**

```powershell
dotnet restore .\Acme.Product\Acme.Product.sln --locked-mode
dotnet build .\Acme.Product\Acme.Product.sln --configuration Debug --no-restore
dotnet run --project .\Acme.Product\src\Acme.Product.Desktop\Acme.Product.Desktop.csproj --configuration Debug --no-build
```

**首次登录**

1. 打开 Studio
2. 初始化管理员账号
3. 使用管理员登录
4. 在设置中创建普通用户

**视觉设计建议**  
左侧放命令块，右侧放“初始化管理员 -> 登录 -> 进入工作台”的步骤图。

**讲解话术**  
“第一次使用时先把运行环境和账号体系打通。管理员身份后面会影响 AI 模型、PLC、Station 命令和运行包部署这些高风险操作。”

**注意边界**  
如果前端提示 API 未连接，应检查 Desktop 进程、本地端口和 WebView2 Runtime。

---

## 第 6 页：项目管理

**页面标题**  
第一步：创建和管理检测项目

**页面要表达的核心信息**  
项目是流程、参数、样本和结果追溯的容器。用户应从项目开始，而不是直接零散运行算子。

**页面正文建议**  
写成“使用路径”：

1. 进入“项目”视图。
2. 新建空白项目或创建示例工程。
3. 输入项目名称和描述。
4. 打开项目进入流程编辑。
5. 后续可搜索、打开、删除、导入或导出项目。

**命名建议**  
使用产线、工位、产品和检测类型组合，例如：

```text
LineA-Terminal-WireSequence
```

**示例工程说明**  
第一次熟悉产品时优先使用 Demo 工程。Demo 工程适合演示工作流和 UI，不应直接作为现场交付项目。

**视觉设计建议**  
放项目列表或新建项目弹窗截图。旁边用小流程标出“Demo 入门”和“现场项目”两条路径。

**讲解话术**  
“项目管理的重点不是建一个名字，而是让流程、检测结果、后续运行包和 Station 部署都能追溯到同一个业务上下文。”

**注意边界**  
普通项目导出适合迁移项目结构；要给现场 Station 使用，应导出 Runtime Package。

---

## 第 7 页：流程编辑画布

**页面标题**  
第二步：用算子和连线搭检测流程

**页面要表达的核心信息**  
ClearVision 的检测流程由算子节点、端口、参数和连线组成。用户通过画布把视觉处理链路可视化。

**页面正文建议**  
解释 5 个基本概念：

- 算子：一个可执行处理单元，例如图像采集、阈值、模板匹配、深度学习、PLC 通信。
- 输入端口：算子接收数据的位置。
- 输出端口：算子产生数据的位置。
- 参数：阈值、ROI、模型路径、置信度、业务规则等。
- 连线：把上游输出连接到下游输入。

**最小流程**

```text
ImageAcquisition -> 预处理/检测/测量 -> ResultJudgment -> ResultOutput
```

**连线规则**

- 输出端口类型应与输入端口类型匹配。
- 一个输入端口通常只接收一个上游来源。
- 图像输出接图像处理或检测；布尔输出接判定或分支；数值输出接测量、统计或阈值判断。

**视觉设计建议**  
一张流程画布截图最有价值。没有截图时用节点框图表达。

**讲解话术**  
“画布让视觉流程从代码里的调用顺序变成用户能看、能拖、能改、能保存的工程资产。”

**注意边界**  
流程保存后才是后续检测、运行包导出和 Station 部署的依据。

---

## 第 8 页：算子库

**页面标题**  
第三步：从 156 个正式算子中选择能力

**页面要表达的核心信息**  
算子库是 ClearVision 的能力池，覆盖从图像采集到 AI、测量、通信和流程控制的主要工业视觉环节。

**页面正文建议**  
展示算子分类概览：

- 图像采集与预处理：采集、滤波、阈值、形态学、边缘。
- 定位与匹配：模板匹配、特征匹配、形状匹配、位置修正。
- 测量与标定：宽度、间隙、圆、线、点线距离、像素到世界坐标。
- AI 检测：DeepLearning、语义分割、异常检测、表面缺陷。
- 通信与输出：Modbus、S7、MC、FINS、TCP、串口、HTTP、结果输出。
- 流程控制：条件分支、变量、循环、异常捕获、计时统计。
- 3D：点云滤波、RANSAC 平面、PPF 匹配等基础算子。

**页面重点数字**

- 正式算子：156 个
- 质量等级：A 级 152 个，B 级 4 个
- 算子平均质量评分：95.1

**视觉设计建议**  
用一张“算子能力地图”。不要把 156 个名字全部铺满，按类别展示即可。

**讲解话术**  
“156 个算子不是为了炫数量，而是为了覆盖视觉项目常见链路。真正使用时，用户通常只需要围绕一个场景选出 5 到 8 个关键节点。”

**注意边界**  
质量等级是功能成熟度口径，不等同真实产线签核。`MqttPublish` 当前是 placeholder-disabled，不作为正式 MQTT 发布能力。

---

## 第 9 页：参数、ROI 与节点预览

**页面标题**  
第四步：把流程调到能解释、能验证

**页面要表达的核心信息**  
流程搭出来之后，真正决定能否落地的是参数、ROI、模型路径、标签和样本验证。

**页面正文建议**  
分成 4 个操作区：

**参数配置**

- 数值：阈值、置信度、核大小。
- 布尔：是否启用、是否保存结果。
- 枚举：模式、算法类型、触发方式。
- 文件路径：模型、标签、标定文件。
- ROI：检测区域、测量区域、到料区域。

**ROI 设置步骤**

1. 加载真实图像或样本图。
2. 确认产品区域。
3. 设置 ROI。
4. 单节点预览。
5. 用 OK/NG 样本验证。

**节点预览步骤**

1. 选中算子。
2. 上传或选择测试图。
3. 点击预览。
4. 查看前后图、输出字段、诊断信息和错误提示。

**常见失败原因**

- 必填输入未连接。
- 图像为空或格式不支持。
- 模型路径不存在。
- 标签顺序与模型输出不匹配。
- ROI 超出图像范围。
- 参数越界或类型错误。

**视觉设计建议**  
放“图像 + ROI 框 + 参数面板 + 预览结果”的四分区截图。

**讲解话术**  
“节点预览的作用是把问题尽量缩小到单个节点。预览通过后再接完整流程，调试效率会高很多。”

**注意边界**  
自动参数推荐只能作为起点，不等同现场验收参数。

---

## 第 10 页：典型基础流程

**页面标题**  
常用流程模板：从图像到结果

**页面要表达的核心信息**  
不同检测任务可以复用相似流程骨架，用户只替换中间的检测/测量模块。

**页面正文建议**  
列 5 条常用链路：

**1. Blob 缺陷检测**

```text
ImageAcquisition -> Filtering -> Thresholding -> BlobAnalysis -> ResultJudgment -> ResultOutput
```

**2. AI 目标检测**

```text
ImageAcquisition -> ImageResize -> DeepLearning(OutputFormat=EndToEndNms) -> optional BoxFilter -> ResultJudgment -> ResultOutput
```

**3. 几何测量**

```text
ImageAcquisition -> Filtering -> EdgeDetection -> CircleMeasurement/LineMeasurement -> CoordinateTransform -> ResultOutput
```

**4. OCR/条码识别**

```text
ImageAcquisition -> CodeRecognition/OcrRecognition -> ConditionalBranch -> DatabaseWrite/PLC output -> ResultOutput
```

**5. 宽度测量**

```text
ImageAcquisition -> Filtering -> CaliperTool -> WidthMeasurement -> UnitConvert -> ResultJudgment -> ResultOutput
```

**视觉设计建议**  
用“模板卡片”排列，每张卡片只放链路和适用场景，不放长解释。

**讲解话术**  
“ClearVision 的一个核心思想是模板优先。面对新需求，先判断它像哪类已知视觉任务，再从稳定骨架开始调整。”

**注意边界**  
模板减少搭建成本，但现场仍必须确认图像、ROI、阈值、模型、标签和输出规则。

---

## 第 11 页：AI 流程生成

**页面标题**  
用自然语言生成流程，但不让 AI 直接上线

**页面要表达的核心信息**  
AI 生成是辅助搭建流程，不是替代业务验收。ClearVision 的关键是模板优先、结构校验、DryRun 和人工确认。

**页面正文建议**  
讲 5 个环节：

1. 配置 AI 模型：Provider、Model、Base URL、API Key、Timeout、Reasoning、RoleBindings。
2. 输入自然语言需求：检测对象、输入来源、OK/NG 规则、输出方式和现场限制。
3. 模板优先收缩：命中线序、缺陷、OCR、测量等场景时，优先使用稳定骨架。
4. Validator 校验：检查算子是否存在、端口连线、参数类型、缺失资源。
5. DryRun 与人工确认：预演覆盖信息，确认模型路径、标签、ROI、ExpectedLabels 等关键项。

**示例提示词**

```text
我要做端子线序检测，输入来自相机连续采集，红黄蓝绿顺序必须一致，
需要输出 OK/NG 和诊断信息，不使用未接入的 MQTT，模型路径由人工确认。
```

**视觉设计建议**  
做一张“AI 生成保险丝”图：

```text
自然语言 -> 模板匹配 -> 结构化流程 -> Validator -> DryRun -> 人工确认 -> 保存流程
```

**讲解话术**  
“这里最重要的不是让 AI 返回一段 JSON，而是让它的结果进入可检查、可预演、可人工确认的工程流程。”

**注意边界**  
API Key 不写入流程或运行包。导出运行包时疑似密钥字段会被阻止。

---

## 第 12 页：端子线序检测场景包

**页面标题**  
主线案例：端子线序检测

**页面要表达的核心信息**  
端子线序检测是当前最适合演示 ClearVision 用法的完整场景，因为它能串起模板、模型、规则、调参、检测、结果输出和质量证据。

**页面正文建议**  
介绍场景包结构：

- `manifest.json`：当前活动包 manifest。
- `template/`：流程模板。
- `rules/`：线序规则和 NG 原因。
- `labels/`：模型类别顺序。
- `models/`：模型说明，私有模型默认不入仓库。
- `samples/`：样本目录契约和 metadata 示例。
- `versions/`：不可变版本记录。
- `faq/`：场景知识和排障。

**基线流程**

```text
ImageAcquisition -> DeepLearning -> DetectionSequenceJudge -> ResultOutput
```

**文档中的完整调试链路也可讲**

```text
ImageAcquisition -> DeepLearning(OutputFormat=EndToEndNms) -> DetectionSequenceJudge -> ResultOutput
```

**现场交付前检查**

- ONNX 模型文件就位。
- labels 顺序与模型 metadata 对齐。
- expected sequence 与业务检测顺序一致。
- ROI 参数按现场图像调整。
- OK/NG 样本验证通过。
- 运行包 manifest 包含正确 hash 和版本。

**视觉设计建议**  
左侧放端子线序业务图，右侧放流程链路。底部放“模型、标签、规则、样本、manifest”的资产条。

**讲解话术**  
“这个场景最适合用来解释 ClearVision，因为它不是一个孤立算法，而是一个可复用场景包。”

**注意边界**  
私有模型二进制默认不入仓库；真实样本如涉及保密，应放在仓库外，仅保留 manifest 或脱敏说明。

---

## 第 13 页：无触发视频流检测

**页面标题**  
没有光电/PLC 触发时：用 FrameChangeTrigger 做到料门控

**页面要表达的核心信息**  
连续视频流场景下，可用帧变化判断是否到料，避免空帧继续触发深度学习和 OK/NG 输出。

**页面正文建议**  
推荐流程：

```text
ImageAcquisition(TriggerMode=Continuous)
-> FrameChangeTrigger
-> DeepLearning
-> BoxFilter(FilterMode=Region)
-> BoxNms
-> DetectionSequenceJudge
-> ResultOutput
```

**调参顺序**

1. 设置 `FrameChangeTrigger.RoiX/Y/W/H`，覆盖端子到料区域。
2. 调整 `PixelThreshold`，过滤反光和背景抖动。
3. 调整 `MinChangeRatio` 和 `MinChangePixels`，控制触发灵敏度。
4. 调整 `CooldownMs`，避免同一工件重复触发。
5. 再调 `DeepLearning`、`BoxFilter`、`BoxNms` 和线序判定。

**证据摘要**

- 2026-05-20 contract baseline：31/31 passed。
- 2026-05-20 synthetic dataset baseline：140/140 passed，Trigger Precision 1.0000，Trigger Recall 1.0000。
- 2026-05-20 field-substitute baseline：20/20 passed。

**视觉设计建议**  
画一张“空帧短路、到料帧放行”的两分支图。

**讲解话术**  
“FrameChangeTrigger 的意义是保护下游检测链路：没有工件时短路当前周期，有到料变化时再放行到 DeepLearning。”

**注意边界**  
这些报告是 contract、synthetic dataset 和 field-substitute evidence，不是客户产线签核。对外材料必须把这个边界讲清楚。

---

## 第 14 页：检测执行

**页面标题**  
第五步：单次检测与连续检测

**页面要表达的核心信息**  
检测执行分为调试导向的单次检测和现场模拟导向的连续检测。

**页面正文建议**  
分两栏：

**单次检测**

- 适合调试、演示和样本验证。
- 操作：打开项目 -> 确认流程已保存 -> 准备输入图像或相机 -> 执行单次检测 -> 查看 OK/NG、耗时、诊断和输出图。

**连续检测**

- 适合模拟现场运行。
- 操作：配置图像来源或相机 -> 确认触发方式 -> 开始连续检测 -> 观察实时结果、吞吐、丢帧、错误和历史 -> 停止。

**关键观察项**

- OK/NG/Error
- 处理耗时
- 置信度
- 缺陷数量
- 诊断信息
- dropped frames / backpressure / 内存

**视觉设计建议**  
放检测面板截图。没有截图时用仪表盘式布局：状态、耗时、结果、错误、历史。

**讲解话术**  
“单次检测帮我们确认流程是否正确，连续检测帮我们看它在现场节奏下是否稳定。”

**注意边界**  
连续检测时优先保护主检测路径，慢消费者不应阻塞 runtime。

---

## 第 15 页：结果查看与实时通信

**页面标题**  
第六步：看历史、统计和实时结果

**页面要表达的核心信息**  
结果页既能查看检测历史，也能通过 SSE 接收实时增量，但完整历史仍以 history endpoint 为准。

**页面正文建议**  
结果页展示字段：

- 检测时间
- 项目 ID
- OK/NG/Error
- 处理耗时
- 置信度
- 缺陷数量
- 诊断信息

**基础分析接口**

- `/api/analysis/statistics/{projectId}`
- `/api/analysis/defect-distribution/{projectId}`
- `/api/analysis/trend/{projectId}`
- `/api/analysis/report/{projectId}`

**实时语义**

- Inspection SSE endpoint：`GET /api/inspection/realtime/{projectId}/events`
- `resultProduced`：检测结果摘要
- `heartbeat`：连接保活
- `Last-Event-ID`：支持有限历史回放

**视觉设计建议**  
放一个结果看板截图，旁边用小箭头表示“history 全量 + SSE 增量”。

**讲解话术**  
“SSE 是实时增量窗口，不是完整历史数据库。需要完整分页历史时，仍然应走 history 接口。”

**注意边界**  
高级 CPK、MTBF、缺陷聚类、PDF 深度报告和图片打包当前未完全接入，不能作为正式质量分析承诺。

---

## 第 16 页：设置、相机与 PLC

**页面标题**  
现场联调前：配置相机、PLC 和触发输入

**页面要表达的核心信息**  
流程跑通之后，还需要把现场设备和系统配置对齐，包括相机、PLC、触发输入和 AI 模型配置。

**页面正文建议**  
分 3 个模块：

**相机管理**

- 搜索所有可用相机。
- 仅搜索华睿或海康相机。
- 查看已绑定相机。
- 软触发拍照。
- 连续预览用于对焦、取景和 ROI 调整。

**PLC 配置**

- 支持 Modbus、Siemens S7、Mitsubishi MC、Omron FINS。
- 配置协议、IP、端口、站号、变量名、地址、数据类型。
- 测试连接并保存变量映射。

**触发输入**

- 查看诊断状态。
- 学习回车/光电类输入设备。
- 将外部触发映射为流程触发信号。

**视觉设计建议**  
用“设备接入三角形”：相机输入、PLC/触发、ClearVision 流程。

**讲解话术**  
“视觉流程不是孤立运行的，它必须和相机、触发信号、PLC 点位和输出语义对齐。”

**注意边界**  
目录选择器暂未完整接入执行链路；过期文件即时清理未开放；宽高参数部分入口暂未开放编辑。

---

## 第 17 页：Runtime Package 导出

**页面标题**  
第七步：把调好的流程冻结成运行包

**页面要表达的核心信息**  
Runtime Package 是从 Studio 调试走向 Station 运行的交付物，包含 flow、runtime profile、质量报告和现场配置草案。

**页面正文建议**  
说明什么时候导出：

- 准备部署到 Station。
- 需要冻结当前流程版本。
- 需要给现场交付可加载运行包。
- 需要保留 `flow hash`、`package ID` 和现场参数 schema。

**导出步骤**

1. 打开并保存目标项目。
2. 确认流程能在 Studio 内单次检测通过。
3. 检查模型、标签、标定文件等资源路径。
4. 点击 Runtime Package 导出。
5. 选择受控导出目录或默认目录。
6. 查看 package ID、flow hash、validation report 和 README。

**导出前校验**

- 项目是否包含可执行 flow。
- 是否至少有一个算子。
- 参数是否合法。
- 资源文件或目录是否存在。
- 是否包含疑似 secret、token、password、credential。
- 导出目录是否在受控范围内。

**视觉设计建议**  
展示包结构：

```text
runtime-package/
|- package.json
|- flow.json
|- runtime-profile.json
|- README.runtime.md
|- quality/validation-report.json
|- field/station-profile.json
```

**讲解话术**  
“运行包是现场交付边界。它把 Studio 里的工程状态冻结成 Station 能加载、能验证、能追溯的形态。”

**注意边界**  
不要把运行包导出到不受控系统目录或仓库根临时文件夹。默认允许 Studio export root、`.tmp/publish-check/`、系统 temp 或白名单目录。

---

## 第 18 页：Station 现场端与同步

**页面标题**  
Station：本地运行优先，Studio 负责监控和部署

**页面要表达的核心信息**  
Station sync 是 opt-in。Station 自主运行检测，Studio 作为监控、命令、包源和持久化对等方。

**页面正文建议**  
讲 5 个能力：

1. Station 注册与在线状态：Station ID、StationName、LineName、心跳。
2. 结果摘要：Station ID、Run ID、Package ID、Flow hash、OK/NG/Error、耗时、诊断码、时间戳。
3. 健康状态：Runtime state、内存、磁盘、spool pending、spool bytes、camera/plc summary、backpressure、dropped result summaries。
4. 命令和部署：Ping、DeployPackage、ReloadPackage、CollectLogs 等。
5. 断网与重放：Studio 离线时 Station 本地检测继续，结果摘要进入 spool，恢复后回放。

**部署包流程**

```text
Studio 登记 package -> 下发 deploy-package 命令
-> Station 下载包 -> 校验 sha256/manifest/version
-> staging -> promote -> 上报命令结果和 audit
```

**视觉设计建议**  
左侧画 Studio Monitor，右侧画多个 Station。中间箭头标注“结果摘要、健康、命令、包下载”，并注明“不传输大图像”。

**讲解话术**  
“Station 的第一职责是保证本地检测不中断。Studio 断线时不能拖垮检测主路径。”

**注意边界**  
当前 Alpha 不传输图像，不引入 Station HTTP server，不引入 MQTT/Kafka 等外部 broker。

---

## 第 19 页：质量证据与发布边界

**页面标题**  
能跑不等于能对外承诺：质量证据要分层

**页面要表达的核心信息**  
ClearVision 区分功能成熟度和证据成熟度。对外介绍时必须讲清哪些是功能存在，哪些是本地证据，哪些才是产线签核。

**页面正文建议**  
用 5 层证据塔：

1. Contract：API、端口、参数、错误语义。
2. Golden：固定 oracle 或回归基线。
3. Dataset：公开、授权、半合成或策划数据集。
4. Field replay：匿名回放或 field-substitute replay。
5. Industrial sign-off：真实现场样本、硬件 profile、报告 ID、审批记录。

**当前口径摘要**

- 156 个正式算子。
- 质量矩阵显示 A/B 等级，但 A 级不等于真实产线签核。
- OperatorLibrary 可独立打包为 NuGet，包级 smoke 不等于所有算子现场验收。
- 模型 release gate 需要 model hash、license、labels contract、dataset version、hardware profile、report ID。

**必须强调的边界**

- `MqttPublish` 当前是 placeholder-disabled。
- 高级分析和 PDF 深度报告等未完全接入。
- public dataset、semi-synthetic、field-substitute replay 不能写成真实产线签核。

**视觉设计建议**  
做“证据金字塔”或“功能成熟度 vs 证据成熟度”二维图。

**讲解话术**  
“这个项目最稳的表达不是夸所有功能都工业闭环，而是明确什么已经实现、什么已有本地证据、什么还需要真实现场补签核。”

**注意边界**  
这页是可信度核心，建议不要省略。

---

## 第 20 页：推荐演示脚本与总结

**页面标题**  
推荐演示路径：15 分钟讲完 ClearVision 用法

**页面要表达的核心信息**  
用一条可执行演示路线收束全篇，让观众知道看完 PPT 后应该怎么上手。

**页面正文建议**  
给出 10 步演示脚本：

1. 启动 Studio，登录管理员账号。
2. 创建 Demo 工程或打开端子线序检测项目。
3. 进入流程编辑画布，展示算子和连线。
4. 打开算子库，说明 156 个正式算子的类别。
5. 选中一个节点，展示属性面板和 ROI。
6. 上传或选择图像，执行节点预览。
7. 执行单次检测，查看 OK/NG、耗时和诊断。
8. 打开结果页，展示历史、统计和实时更新。
9. 导出 Runtime Package，展示 package ID、flow hash、validation report。
10. 打开 Station Monitor，展示站点、健康、结果摘要、命令和审计。

**收尾三句话**

- ClearVision 把视觉流程从“脚本和经验”变成可编辑、可验证、可交付的工程资产。
- 它的使用主线是：建项目、搭流程、调参数、跑检测、导出包、看 Station。
- 当前最稳的对外口径是：可演示、可试点、可持续补证据，不夸大真实产线签核。

**视觉设计建议**  
做一张“演示检查清单”。底部放一句主张：  
“从能跑，到能解释，再到能被证明可靠地跑。”

**讲解话术**  
“如果只记住一件事，那就是 ClearVision 的价值不在单个按钮，而在这条从视觉需求到现场运行包的完整链路。”

**注意边界**  
结尾建议主动提风险边界，这会比泛泛宣传更可信。

---

## 附录：制作 PPT 时的素材清单

建议后续制作 PPT 时补充以下素材：

1. Studio 主界面截图。
2. 项目列表 / 新建项目弹窗截图。
3. 流程画布截图。
4. 算子库与属性面板截图。
5. ROI 编辑与节点预览截图。
6. 单次检测结果截图。
7. 结果页统计 / 趋势截图。
8. 设置页中的 AI 模型、相机、PLC 配置截图。
9. Runtime Package 导出结果截图。
10. Station Monitor 的站点、健康、命令、审计截图。
11. 端子线序检测样例图或示意图。
12. 质量矩阵或 FrameChangeTrigger 报告摘要截图。

## 附录：推荐 PPT 视觉系统

- 风格：工业软件工作台，不做营销落地页风格。
- 色彩：深灰、白、蓝绿作为主信息色，少量橙/红表示告警和 NG。
- 字体层级：每页一个结论标题，正文不超过 5 条。
- 图表优先：流程链路、架构图、证据塔、演示路径比长段文字更重要。
- 页面节奏：功能页、流程页、案例页、边界页交替出现，避免 20 页都是列表。

## 附录：可复用的一句话定位

ClearVision 是一个面向工业视觉检测的本地化 Studio + Runtime + Station 平台，用流程编排和算子库把图像采集、预处理、检测、测量、标定、AI 推理、PLC 通信、结果持久化和现场同步串成可验证、可交付的工程链路。
