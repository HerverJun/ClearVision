# AI 视觉与内容审查证据

- 日期：2026-09-05。
- 代码基线：`4fa90458`。
- 本次使用 Codex 内置浏览器，加载当前 `wwwroot`，未修改生产 JS/CSS。
- `server.cjs` 通过 TypeScript AST 复用仓库已有 AI 测试场景，提供隔离的身份和 HTTP 数据。页面样式、AiPanel、设置页和应用预览均为生产实现。
- 测试数据不代表实际 LLM 输出。未连接模型、相机、PLC 或 Station，未操作生产账号、配置或会话库。
- 本次没有运行 .NET / Playwright 全套测试，也没有把历史回归通过数作为当前全产品结论。

## 复现

在仓库根目录执行以下命令。需要已安装的 UI 测试依赖和 FrontendV2 的 TypeScript 依赖，端口 5082 必须空闲。

```powershell
node ./quality/evidence/ai-visual-content-review-20260905/server.cjs
```

打开 `http://127.0.0.1:5082/index.html?scenario=clarify&theme=light`。

- `scenario`: `idle`, `plan`, `clarify`, `ready`, `parameters`, `resources`, `mixed`, `validation-failed`, `dryrun-failed`, `applied`。
- `theme`: `light`, `dark`。
- 设置页：从顶部“设置”进入“AI 大模型”。
- 场景用于呈现审查，不应用来验收真实创建、保存、推理或部署操作。

## 本次截图

| 文件 | 视口 | 观察内容 |
| --- | --- | --- |
| `idle-light-1366.png` | 1366×768 | 空白页、输入框、快捷示例 |
| `clarify-top-light-1366.png` | 1366×768 | 方案首屏，2 项阻断与 3 项待补并存 |
| `resource-light-1366.png` | 1366×768 | 滚动后的资源卡片与操作 |
| `build-ready-dark-1366.png` | 1366×768 | 构建摘要、状态重复、流程摘要 |
| `build-validation-dark-1366.png` | 1366×768 | 点击“验证”后的区域与检查结果 |
| `apply-preview-dark-1366.png` | 1366×768 | 应用预览模态框、连线内部 ID |
| `settings-ai-dark-1366.png` | 1366×768 | 模型设置、固定性能指标、压缩表单 |
| `clarify-dark-1024.png` | 1024×768 | 工作台/会话切换和方案层级 |
| `conversation-dark-1024.png` | 1024×768 | 紧凑模式会话及输入区 |
| `build-failed-light-1366.png` | 1366×768 | 校验失败摘要、禁用的应用入口 |

## DOM 测量

在 `clarify/light` 初始位置、1366×768 CSS 视口：

- `#ai-plan-workspace`: y=186，height≈534.67。
- `[data-ai-hook="clarification-workspace"]`: y≈837.19，height≈768.17；第一个问题在首屏外。
- `.ai-resource-audit-card`: height≈399.34。
- 资源标签：10px，`rgb(135,139,147)`；资源值：11px，`rgb(92,96,104)`。
- 字段背景：`rgba(7,17,27,0.28)`；卡片背景：`rgba(241,245,249,0.75)`，底层为白色。
- 按透明度合成得到字段底色约 `rgb(178,183,188)`；WCAG 相对亮度公式估算标签对比度 1.69:1、值 3.12:1。未使用截图抗锯齿像素作为文字原色。
- `#ai-input` 有 3px 红色 box-shadow，外层输入容器同时有聚焦边框/光晕，形成重复强调。

在模型设置页、1366×768 CSS 视口：

- `cfg-ai-name`, `cfg-ai-display-name`, `cfg-ai-provider`, `cfg-ai-protocol`, `cfg-ai-wireapi`, `cfg-ai-authmode`, `cfg-ai-model` 宽度均约 75.44px。
- 点击“本地模型”后仍显示 LLM 配置，未切换内容；源代码对应带 `cursor:pointer` 的普通 `div`。
- 当前模型的真实测试字段为 `untested` / `Latency: -`，性能概览仍显示固定 450ms、14.2K/50K、45/100。

## 边界说明

- Build 夹具直接设置测试结果，未完整回放正式生命周期。因此截图里任务栏的“正在构建”与正文终态并存不作为本次确认的生产状态缺陷。
- 方案夹具的 2 个问题 + 1 个资源可用于复现展示计数差异；判断依据另有生产代码的不同计数来源。
- 点击 Build“验证”能够滚动并聚焦目标区域，应用预览能够打开和取消。没有确认应用，也未运行流程。
- 已有真实 WebView2 资源阻断截图仅作交叉参考，见相邻 `ai-plan-build-readiness-p1/after/`；不是本次重新执行的桌面端验收。
