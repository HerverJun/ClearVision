# F01 Design Foundation Lab 截图索引

## 1. 视觉方向

```text
Quiet Precision
简洁、高端、克制的蓝白科技风
工业视觉软件的信息密度
品牌色与 OK / NG / Warning / Info / Idle 严格分离
```

正式截图来自最终 Central Playwright PASS run：

`.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/`

截图总数：12。每个 viewport 覆盖 light/dark × comfortable/compact。所有场景横向 overflow ≤ 1px，Modal 触发器在可视/可滚动范围内。

## 2. 1366×768

| Theme | Density | Screenshot |
| --- | --- | --- |
| light | comfortable | [1366x768-light-comfortable](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x768-light-comfortable.png) |
| light | compact | [1366x768-light-compact](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x768-light-compact.png) |
| dark | comfortable | [1366x768-dark-comfortable](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x768-dark-comfortable.png) |
| dark | compact | [1366x768-dark-compact](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x768-dark-compact.png) |

## 3. 1920×1080

| Theme | Density | Screenshot |
| --- | --- | --- |
| light | comfortable | [1920x1080-light-comfortable](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1920x1080-light-comfortable.png) |
| light | compact | [1920x1080-light-compact](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1920x1080-light-compact.png) |
| dark | comfortable | [1920x1080-dark-comfortable](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1920x1080-dark-comfortable.png) |
| dark | compact | [1920x1080-dark-compact](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1920x1080-dark-compact.png) |

## 4. 1366×600 短屏

| Theme | Density | Screenshot |
| --- | --- | --- |
| light | comfortable | [1366x600-short-light-comfortable](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x600-short-light-comfortable.png) |
| light | compact | [1366x600-short-light-compact](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x600-short-light-compact.png) |
| dark | comfortable | [1366x600-short-dark-comfortable](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x600-short-dark-comfortable.png) |
| dark | compact | [1366x600-short-dark-compact](../../../.tmp/studio-ui-next/f01/playwright/final-central/studio-ui-next-design-foun-d9d29-and-desktop-short-viewports-chromium/1366x600-short-dark-compact.png) |

## 5. Reduced motion 与可访问性

Reduced motion 是时间行为，静态像素不能证明 duration 已归零，因此不伪造“动画截图”。同一最终 Playwright run 实际验证：

- `prefers-reduced-motion: reduce` 使 `--cv-motion-duration-normal` 为 `0ms/0s`；
- 显式 reduced-motion toggle 使 `--cv-motion-duration-slow` 为 `0ms/0s`；
- root `data-reduced-motion=true`；
- Modal focus trap、Escape、焦点恢复；
- Toast timer/unmount 清理；
- Splitter keyboard/pointer 与 listener 生命周期；
- focus-visible、disabled、loading、error 状态。

## 6. 自动与人工门禁

```text
DESIGN_FOUNDATION_AUTOMATED=PASS
VISUAL_CONFIRMATION=AWAITING_USER
```

自动化证明主题、密度、状态色、布局和交互契约；最终审美方向、信息密度与表面层级仍等待用户从上述截图明确确认。
