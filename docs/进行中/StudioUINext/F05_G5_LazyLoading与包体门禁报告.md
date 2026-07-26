# F05 G5 Lazy Loading 与包体门禁报告

## 结论

F05 G5 已建立可重复、按原始字节统计、由 CI fail-closed 的 StudioUI 包体门禁。基线为 `d921165b35284255aa47cd191b2529fcd5ca367f`；production build 输出仅位于 StudioUI `.tmp/` 或 CI 临时目录，`dist` 不入库。

Route lazy loading 覆盖 Overview、Projects、Workspace、Operators、连续检测、Stations、Results、Diagnostics、About 与 Labs。Product Shell、Auth、403/404 保持 eager；`/studio/` 与 hash history 不变。动态 chunk 加载失败由 router error handler 导入 eager 404 恢复页，提供刷新恢复，不改变 Leave Guard、role、flag 或 capability owner。

Vite 自动形成 route chunk 与共享 runtime/design/canvas chunk。最大单文件为共享 `productRuntime` JS，约 `365.62 kB`；没有异常共享大块或依赖复制证据，因此本轮未配置 `manualChunks`。

## 统计口径

- 所有数值为 production 输出原始字节，不使用 gzip/brotli。
- 同步闭包包含 root chunk、递归 `imports`、各记录 CSS/assets；按相对文件路径去重。
- `dynamicImports` 单列，绝不计入父入口同步闭包。
- 报告列出所有输出文件、原始字节、SHA-256、entry/route chunk、同步闭包分项与总包体。
- 缺文件、manifest 解析失败、同步闭包循环、记录内重复引用、预算 root 不唯一或超预算均非零退出。

## 测量

| 项目 | 优化前 | 优化后 | 冻结预算 |
|---|---:|---:|---:|
| Initial / Shell 同步闭包 | 1,191,108 B | 809,031 B | Shell 850,000 B；硬上限 963,630 B |
| Workspace | 随 Initial eager | 853,743 B | 900,000 B |
| Inspection（最坏 route） | 随 Initial eager | 735,853 B | 790,000 B |
| Stations（最坏 route） | 随 Initial eager | 765,378 B | 820,000 B |
| Results | 随 Initial eager | 760,022 B | 820,000 B |
| 全部 production 输出 | 1,191,752 B | 1,218,583 B | 仅报告，不以总包体替代关键闭包门禁 |

总包体因 route manifest、独立 CSS/JS chunk 与 hash 元数据小幅增加；首屏同步闭包减少 `382,077 B`（约 32.1%）。这是有解释的网络加载边界变化，不以牺牲 route owner 生命周期换取总包体数字。

## 门禁与证据

- `npm run build:production`：production build 到 `.tmp/bundle/dist`。
- `npm run bundle:report`：生成稳定 JSON/Markdown。
- `npm run bundle:gate`：读取 `bundle-budgets.json` 并硬失败。
- `npm run bundle:verify`：连续两次 production build，规范化 JSON/Markdown 字节一致。
- `bundleReport.spec.ts`：覆盖 SHA/字节、dynamic 排除、缺文件、重复引用、同步环和人工 1 B 超预算非零退出。
- `.github/workflows/ci.yml` 的 `studio-ui` job 在 lint/typecheck/unit 后实际执行 production build 与 budget gate，并上传报告。

Station unknown-outcome 恢复已收紧：同一 package 的候选命令必须恰好一个才确认成功；零个或多个均保持 `unknown-outcome`。

Browser Playwright 已覆盖冷启动、9 个主要 lazy route、Workspace/Inspection/Stations/Results 深层直达、cold/mounted 两种 chunk 404 恢复、console error、重复加载与 20-cycle Workspace DOM owner 卸载。正常旅程 39 个 production asset 各请求一次，无重复；正常及 404 旅程 browser error logs 均为 0。fixture 无工程数据，Workspace 正式 owner 未创建，资源 ledger 归零另由 unit 20-cycle 覆盖。真实 WebView2 Debug/Release、Windows 125% DPI、Release publish、Remote Final Gate 均留给 G6，不在本报告冒充完成。
