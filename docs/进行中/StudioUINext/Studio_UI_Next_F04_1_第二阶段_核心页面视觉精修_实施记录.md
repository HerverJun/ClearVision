# Studio UI Next F04.1 第二阶段：核心页面视觉精修实施记录

> 实施日期：2026-07-20（Asia/Shanghai）
> 分支：`studio-ui-next`
> Initial SHA：`053c562b392d81794ce31a85d0dee68b07fd121b`
> 页面实现候选 SHA：`92f9de60`
> 视觉参考：`C:\Users\HerverJun\Desktop\stitch_remix_of_pixel_perfect_reproduction`
> 稳定功能参考：`C:\Users\HerverJun\Desktop\ClearVision` / `codex初稿`
> 产品视觉确认：`PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER`

## 1. 范围与边界

本阶段只精修以下核心页面：

- 工程页；
- 结果与追溯页；
- 工作站监控页及其只读详情工作台。

未修改流程工作台、FlowCanvas、Inspector、Preview、API、DTO、权限、状态机、持久化、运行包、Station 现场控制或 capability owner。查询、轮询、工程生命周期命令与 URL 筛选继续复用现有 owner 和 transport。

工作区既有 `ClearVision.Product/src/ClearVision.Product.Desktop/appsettings.json` 修改保持原样，未纳入本阶段提交。

## 2. 信息层级判断

### 2.1 工程页

核心任务：工程师浏览工程库、返回最近工程、新建空白工程并进入流程工作区。

- 将“全部工程”确立为主工作区，“最近工程”收敛为窄侧栏；
- 搜索与排序进入连续工具带，表格直接承接数据主体；
- 页面标题只保留任务说明和真实工程计数，不再使用眉题与合同说明；
- 空状态提供“创建第一个工程”或“清除搜索”的下一步；
- 删除仍使用服务端修订号、冲突和未知结果核对语义，不做乐观移除。

### 2.2 结果与追溯页

核心任务：质量与工程人员筛选历史、扫描结果、查看诊断和技术追溯。

- 常用筛选常驻；诊断码保持原有直接路径；精确 ISO 时间进入高级筛选；
- 列表成为主工作区，详情成为窄证据栏；
- 详情按“判定摘要 → 缺陷摘要 → 诊断 → 技术追溯”分层；
- 技术追溯默认折叠，避免哈希和标识抢占业务判断；
- 保留本机/工作站来源、九类结果、执行/判定双轴、分页、详情 404、旧格式兼容投影和只读边界。

### 2.3 工作站监控页

核心任务：现场人员扫描工作站连接与运行状态、定位异常并核对最近结果。

- 工作站表格成为主区，运行摘要与结果统计进入右侧状态栏；
- 正常、离线、降级、故障、暂停、OK、NG、执行失败使用真实语义与独立颜色；
- 诊断码和诊断说明贴近工作站名称，减少异常定位跳转；
- 九类结果改为紧凑矩阵，不再使用九张独立卡片；
- 工作站详情重排为“状态概览 + 最近结果”主工作台，下方保留健康快照与独立降级的管理信息。

## 3. 视觉处理

- 固定第一阶段的 Stitch / Quiet Precision 方向，不探索新主题；
- 继续使用现有 Design System tokens、Panel、Toolbar、DataTable、StatusBadge、PageState 与表单组件；
- 通过连续工具带、表格边界、状态侧栏和单向分隔建立层级；
- 未新增渐变、玻璃、装饰网格、宽软阴影、巨型圆角或大面积丹红；
- 中文主标签替代用户可见的 `Station`、`Stale`、`revision`、`tombstone` 等实现词；必要的 `ISO`、`StationAdmin` 和诊断码保留为次级技术信息。

## 4. 验证

### 4.1 自动化

- `npm run typecheck`：PASS；
- `npm run lint`：PASS，0 warning；
- 相关 Vitest：PASS，3 files / 16 tests；
- 相关 Playwright：PASS，20 tests；
- `npm run build`：PASS；存在既有的大 chunk warning，未在本视觉阶段扩大到代码分包；
- 全量 Vitest：485 / 489 PASS。4 个失败均为架构守卫读取用户现有 `StudioUiEnabled=true`，而测试要求默认 `false`；本阶段未修改或回滚该文件。

### 4.2 设计审计

- 已按 `$web-design-guidelines` 最新规则审计本轮 Vue 文件；
- 已修复表单 `name` / `autocomplete`、占位符、焦点、可访问名称、中文文案和长内容承载问题；
- Impeccable detector 对 4 个页面返回空 findings；
- 浏览器证据的 `horizontalOverflow` 均为 0，控制台错误和页面错误均为 0。

### 4.3 浏览器视觉证据

最终证据目录：

`C:\Users\HerverJun\Desktop\ClearVision-UI-Next\.tmp\studio-ui-next\f04\f04-1-stage2-final\`

覆盖：

- 工程有数据；
- 工程空状态；
- 结果筛选与列表；
- 结果详情打开；
- 正常、离线、降级、故障、OK、NG、执行失败混合工作站；
- 异常工作站详情、最近结果和健康快照；
- 1920×1080；
- 1366×768 与 1366×600 短屏压力场景。

浏览器夹具证明真实 Vue 产品路由、组件、查询 owner 和后端合同投影，不冒充真实 WebView2 或真实 Windows DPI。真实 Windows 125% 系统缩放：`NOT PERFORMED`；本阶段以 1366×768 / 1366×600 的更小逻辑工作区作为分层压力证据。

## 5. 人工确认

仍需用户结合真实业务数据确认：

- 工程表格在大量长中文工程名和描述下的列宽偏好；
- 结果详情栏在超长诊断说明和追溯标识下的阅读节奏；
- 监控页在真实工作站数量、真实告警密度和 Windows 125% WebView2 下的最终观感；
- 深色主题与 comfortable density 的产品级视觉批准。

`PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER`
