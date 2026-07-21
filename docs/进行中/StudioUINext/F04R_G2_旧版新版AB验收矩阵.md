# Studio UI Next F04-R G2 旧版 / 新版 A/B 验收矩阵

> 状态：`FROZEN`
> 证据基线：F04.2 runner、截图与当前 `483a212...` 代码复核；本轮未重新执行 Browser、Playwright 或截图。

## 1. 证据等级与计数规则

| 标记 | 含义 |
|---|---|
| `RUNNER` | F04.2 已实际执行：Legacy Playwright 3/3、Next Playwright 22/22；相应截图来自实际 Browser fixture 操作 |
| `TEST_PATH` | 当前 Playwright/单元测试中的真实 locator、输入和命令序列；未在本轮重跑 |
| `CODE_PATH` | 当前页面和 handler 的可达操作路径复核；不是本轮人手点击计时 |
| `BLOCKED` | 当前产品没有可完成入口或 owner；不得推算点击数 |
| `NOT_PERFORMED` | 真实 WebView2、Windows 125% DPI、真实端点或硬件证据未执行 |

点击数只统计完成任务所需的指针激活，不统计每个文本字符、画布拖动距离或等待。键盘输入单列。直接 fixture URL 只用于稳定复现，不计作用户导航。区间表示可从当前上下文直接进入或需先经过产品导航；任何“0 点击”都不能掩盖自动跳页造成的上下文丢失。

当前 HEAD 相对 F04.2 的 `56fbf18...` 没有产品代码和 UI 测试漂移，只有治理文档提交，因此 F04.2 基线仍可用于 G2；它仍然不是 WebView2/DPI/真实端点证据。

## 2. 逐步骤可量化基线

| # | 起点 | Legacy：点击 / 切页 / 滚动 | Next 当前：点击 / 切页 / 滚动 | 鼠标与键盘 | 上下文、入口距离与首屏信息 | 错误恢复 | 验收目标与证据 |
|---:|---|---|---|---|---|---|---|
| 1 登录 | 登录页 | `1 / 1 / 0`；填写凭据后提交 | `1 / 1 / 0`；setup/login/returnTo 分路由 | 键盘输入账号密码；Enter 可提交 | Legacy 进入 shell；Next 当前默认 `/overview`，目标 `/projects`，少一次无效再导航 | Next 401 quarantine/reauth 优于 Legacy | `TASK_STEPS<=Legacy`；成功直接看到工程任务。`CODE_PATH` + F04 auth 既有测试 |
| 2 浏览/创建工程 | 产品 shell | 浏览 `1 / 1 / 0`；创建并打开约 `4 / 1 / 0` | 浏览目标为登录落点 `0`；创建并打开约 `4 / 1 / 0` | 搜索和工程名键盘输入；删除需指针确认 | Legacy 直接操作多但信息行弱；Next 搜索、最近工程、表格首屏更易扫描 | Next create/delete operation reconcile 明显更强 | blank create 不多于 Legacy；import 不在本轮。`RUNNER`/F04.2 任务表 |
| 3 打开工作区 | 已有工程列表 | 导航工程 + 打开 + 流程，约 `3 / 2 / 0` | 工程页到工作区约 `2 / 1 / 0` | 指针选择工程；可键盘激活行操作 | Next 路由化更短，但正式 flag 仍关闭；目标保留 project/revision 上下文 | 404 回工程页；401 后保留 return/reconcile identity | `Next<=2`，且真实 pilot 可达。`RUNNER` fixture + `CODE_PATH` |
| 4 添加算子 | 已打开 Workspace | Rail 搜索/分类后选择，通常 `2 / 0 / rail 内部可滚动` | Flyout/搜索后选择，通常 `2 / 0 / rail 内部可滚动` | 鼠标点击或拖拽；键盘搜索与激活 | 两者同屏不丢 Canvas；Next 分类、搜索、焦点和 drag 状态有测试 | 无结果、搜索清空、添加失败就地反馈 | 完成且步骤不增加；1920/1366 Canvas 仍为主体。`TEST_PATH` F03 workspace |
| 5 基础参数 | 已选节点 | 节点选择 `1` + 字段操作；`0` 切页；Inspector 滚动按参数量 | 同左；字段错误就地；`0` 切页 | 数字/文本键盘，选择/布尔指针；支持快捷键 | Inspector 保留节点、端口、参数；Next 长中文和范围错误首屏层级更好 | Next 字段级错误优于 Legacy toast | `ERROR_RECOVERY>Legacy`；不增加跨页。`RUNNER`/`TEST_PATH` |
| 6 相机绑定/单帧 | 已选采集节点 | binding 选择 + 单帧，约 `2-3 / 0 / Inspector 内` | `BLOCKED`：extension slot 无 owner | Legacy 选择 binding、点击捕获；硬件动作需等待/取消 | Legacy 同屏并把帧送 Preview；Next 仅显示“专用编辑器”提示 | Legacy stable `bea40439` 新增 source invalidation；Next 尚无等价语义 | G3 目标 `2-3 / 0`，且 source identity/失效恢复优于 Legacy；真实相机 `NOT_PERFORMED` |
| 7 全局变量 | Workspace | 打开管理、新建、保存、source/target 绑定约 `4-6 / 0 / dialog 内` | `BLOCKED`：按钮 disabled，contract 只读 | 名称/初始值键盘；选择类型与绑定；删除确认 | Legacy 跨节点管理同屏 modal；当前 Next 不可完成 | Legacy 删除会级联 binding，但独立保存恢复弱 | 目标 `<=6 / 0`；统一 Project dirty/save/409 恢复必须优于 Legacy。`CODE_PATH` |
| 8 最终判定 | Workspace | 打开、选候选、配置、保存约 `3` 指针 + 字段输入；`0` 切页 | `BLOCKED`：按钮 disabled；Flow contract 可 round-trip | selector/阈值/映射键盘；后端 validation | Legacy 候选、规则、错误同一 dialog；当前 Next 无可达 owner | Legacy 已显示稳定 issue code；Next 目标字段定位 + 保留 draft | 目标 `<=Legacy`，后端候选与校验不降级。`TEST_PATH` `final-decision.spec.ts` |
| 9 Preview/ROI | 已选节点 | Preview + ROI + 应用约 `3 / 0 / Preview 内` | Preview + ROI + 应用约 `3 / 0 / Preview 内` | 图像缩放/平移/ROI 拖拽；按钮/快捷键 | 两者同屏；Next image/result/ROI、空态和错误层级更稳定 | Next cancel、stale、artifact cleanup、ROI discard/undo 更强 | `Next<=Legacy`；保持 1350×704 无全局滚动。`RUNNER`/`TEST_PATH` |
| 10 保存/冲突 | Workspace dirty | 保存 `1 / 0 / 0`；冲突恢复语义分散 | 保存 `1 / 0 / 0`；409/unknown 时再 `1` 次 reconcile/reload | Ctrl+S 或按钮；冲突由明确选择恢复 | Next revision、dirty、状态栏和 Canvas 上下文持续可见 | Next fresh GET reconcile + Leave Guard 明显更强 | 普通保存 1 步；增加恢复确认有可靠性理由。`TEST_PATH` |
| 11 Run/Stop/Reconcile | 已保存 Workspace | Run `1`；Stop 额外 `1`；结果未知恢复分散 | Run `1`；Stop `1`；unknown Reconcile `1`；`0` 切页直到当前自动 handoff | 指针或命令按钮；运行期间编辑锁定 | Next identity、阶段与 mutation gate 同屏；不丢选中节点 | Next 锁定重复写入并用 authority reconcile | 相同主步骤，unknown 恢复必须严格优于 Legacy。`TEST_PATH` |
| 12 本次结果/Evidence | 运行终态 | 进入检测结果并选详情约 `1-2 / 1 /` 记录常在 KPI 下方需滚动 | 当前成功自动跳转 `0 / 1 / 0`，但丢失 Workspace 上下文；Evidence `BLOCKED` | 目标为点击“查看本次结果”1 次；导出再 1 次 | Next Results 表格/详情首屏优于 Legacy；当前自动跳转违反已批准上下文保留 | Failed/Cancelled 留原位；目标 OK/NG 也留原位并可回看；Evidence 409/413 明确 | 目标 `1 / 1 / 0`，以一次自愿点击换取上下文安全；`CRITICAL_INFO_VISIBILITY>=Legacy` |
| 13 Runtime Package | 工程或 Workspace | 打开导出、选择运行包约 `2-3 / 0 / modal 内`；当前工程会先保存 | `BLOCKED`：无入口 | Admin 指针确认；dirty 时先保存；无键盘特殊要求 | Legacy 可选工程但混放 editable JSON；Next 目标只显示 Runtime Package 和真实 revision | 403、运行中 409、校验失败；unknown 不自动重试 | 目标 `2-3 / 0`；额外 dirty/权限确认属于安全理由。`CODE_PATH` |

## 3. 上下文切换与首屏门禁

| 场景 | Legacy 基线 | Next 当前 | G3/G4 目标 |
|---|---|---|---|
| 1920×1080 Workspace | Canvas 宽，右侧预览/结果块较碎 | Canvas/Inspector/Preview 三栏稳定；保存/运行可见 | 相机、变量、判定入口加入后仍不挤压 Canvas；无 page hero |
| 1366×768 / 1350×704 | 工程与设置仍有较高密度；结果记录需滚动 | Workspace 无全局溢出；结果诊断长码曾截断 | 核心动作、错误、identity 可见；每轴只有一个滚动 owner |
| Windows 125% | 本轮无真实证据 | 本轮无真实证据；DPR 1.25 不等价 | G4 `REAL_WEBVIEW2` + `WINDOWS_125_DPI=PASS` 前不得宣称通过 |
| Run 完成 | Legacy 可进入检测链 | Next 自动跳结果详情 | OK/NG 留 Workspace，摘要 + 单一入口；Failed/Cancelled/Unknown 均不自动跳 |
| 特殊编辑器 | Legacy modal/Inspector 同屏 | Next extension 占位 | capability-local owner，取消恢复 opening draft，不建 route 或第二 store |

## 4. 中文术语冻结

| 技术/旧文案 | 用户主文案 |
|---|---|
| Project / Flow / Operator | 工程 / 流程 / 算子 |
| Inspector | 属性检查器 |
| Preview / ROI | 预览 / 感兴趣区域（ROI） |
| Run / Stop / Reconcile | 正式运行 / 停止 / 协调运行结果 |
| dirty / stale / unknown outcome | 未保存 / 已过期 / 操作结果未知 |
| FinalDecision | 最终判定 |
| GlobalVariables | 全局变量 |
| Evidence manifest / export | 证据清单 / 导出本条证据 |
| Runtime Package | 运行包（技术标识可在次级信息保留 Runtime Package） |

## 5. G4 复测要求

G4 必须以同一工程、同一算子、同一错误与同一 viewport 重跑整条旅程，并记录实际 click trace、route trace、scroll offsets、焦点序列和恢复步骤。Browser fixture、Playwright、真实 WebView2、Windows 125% 分开报告；本矩阵的区间不得在 G4 被抄写成实测 PASS。

```text
TASK_COMPLETION=COMPLETE
TASK_STEPS<=LEGACY_OR_JUSTIFIED
CONTEXT_SWITCH<=LEGACY_OR_JUSTIFIED
CRITICAL_INFO_VISIBILITY>=LEGACY
ERROR_RECOVERY>LEGACY
A_B_ACCEPTANCE_MATRIX=FROZEN
```
