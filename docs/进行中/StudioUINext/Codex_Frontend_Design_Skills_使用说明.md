# Codex 前端设计 Skills 使用说明

## 安装范围

本仓库使用 Codex 官方的仓库级 Skills 目录：`.agents/skills/`。以下内容固定安装到本仓库，不会影响其他项目；每个目录均来自列出的固定 Git 提交。

| Skill | 来源（固定提交） | 实际安装位置 | 显式调用 |
| --- | --- | --- | --- |
| Impeccable | [pbakaus/impeccable](https://github.com/pbakaus/impeccable/tree/e4ab5e24bdf5321b72163d2fbcbe6fa985c848ba/.agents/skills/impeccable) `e4ab5e2` | `.agents/skills/impeccable/` | `$impeccable`，例如 `$impeccable critique <目标>` |
| Vue Best Practices | [antfu/skills](https://github.com/antfu/skills/tree/a74f281a27dadc02397bc1a174b0f2c97531b6ae/skills/vue-best-practices) `a74f281` | `.agents/skills/vue-best-practices/` | `$vue-best-practices` |
| Web Design Guidelines | [vercel-labs/agent-skills](https://github.com/vercel-labs/agent-skills/tree/f8a72b9603728bb92a217a879b7e62e43ad76c81/skills/web-design-guidelines) `f8a72b9` | `.agents/skills/web-design-guidelines/` | `$web-design-guidelines <文件或模式>` |

Codex 也会按描述自动选择适用 Skill；涉及界面实现或审查时，建议显式写出名称，以便控制顺序和范围。

## 适用场景与顺序

1. 用 `$impeccable` 分析现有页面结构、信息层级、设计方向或提出视觉 critique。对既有界面的局部分析不需要运行 `init`；不要未经明确决定就创建其 `PRODUCT.md`、`DESIGN.md` 或 `.impeccable/` 工作文件。
2. 实现 Vue 3 页面、组件、组合式逻辑、Pinia 或路由改动时，同时使用 `$vue-best-practices`。它约束 Composition API、TypeScript、响应式边界和组件工程质量，不负责重定义产品视觉。
3. 在真实浏览器或 WebView2 中生成并查看截图。静态 Chromium 测试不能替代该证据。
4. 再用 `$impeccable critique` 或 `$impeccable polish` 对截图和已实现界面进行视觉精修。
5. 用 `$web-design-guidelines <文件或模式>` 做最后的交互、响应式和可访问性审查，并逐条落实有证据的发现。
6. 最后进行人工视觉确认；需要时重复第 3–5 步。

这些是通用辅助能力，不能替代 ClearVision Studio 的 **Quiet Precision** 设计系统、现有设计 tokens、工业工作流和高信息密度要求。不得将工业桌面工程工具改造成低信息密度的营销落地页，也不得借由 Skill 绕过仓库 `AGENTS.md` 的 Runtime、保存、HostBridge、Canvas 或权威状态边界。

## 注意事项与已知边界

- Impeccable 与 Vue Best Practices 都可能匹配 Vue UI 改动：前者负责设计/视觉与 critique，后者负责 Vue 实现质量。不要让前者覆盖既有组件体系或视觉 tokens。
- Impeccable 上游还提供编辑后自动 detector hook；本次**未**安装 `.codex/hooks.json`，因此没有新增自动检测或编辑后写入。仅在团队单独审查并同意其行为后，才考虑启用 `$impeccable hooks`。
- Impeccable 的 `context.mjs` 会检查 `https://impeccable.style/api/version`；其 live 模式会启动本地服务并可能要求为 `localhost` 调整开发 CSP。只有在明确需要该功能且确认变更范围时才运行。
- Web Design Guidelines 每次审查都要求从 `https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md` 获取最新规则。规则可随远端变化，且需要联网；它不会上传本仓库源码，但应避免把私有内容粘贴到外部服务。
- Vue Best Practices 是本地静态规则与参考资料，无运行时服务或 hook。

## 更新与移除

更新前先审阅上游 `SKILL.md`、引用文件、版本/提交和本说明中的冲突边界。使用 Codex 的 GitHub Skill 安装器，以明确 `--ref <已审阅提交>` 安装到 `.agents/skills`；安装器不会覆盖同名目录，因此应先在一个临时目录验证，再通过受审计的仓库变更替换旧目录。

移除时，从版本控制中删除对应目录：`.agents/skills/impeccable/`、`.agents/skills/vue-best-practices/` 或 `.agents/skills/web-design-guidelines/`。不要删除 `.agents/skills/` 下其他团队 Skill。完成更新或移除后，重启 Codex 或开启新会话以确保重新发现。

`nextlevelbuilder/ui-ux-pro-max-skill` 本轮未安装：其当前说明没有 Codex 作为已支持目标，主要安装流依赖其 CLI/其他 AI harness，且它的“新页面必须生成并持久化设计系统”与本项目既有设计系统治理重叠。待有明确 Codex 安装方式、且完成与 Quiet Precision 的冲突审查后再评估。
