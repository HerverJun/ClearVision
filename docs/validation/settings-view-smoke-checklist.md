# Studio 设置页前端烟测清单

适用范围：Studio 设置页低风险产品化收尾验证。该清单用于人工 smoke，不替代后端端点测试或现场联调。

## 基础加载与生命周期

- 打开 Studio 设置页，浏览器控制台无 ES module 导入错误，`window.initializeSettingsView` 可重复调用。
- 依次切换常规设置、PLC 通讯、工站通讯、文件存储、生产运行保护、相机管理、AI 大模型、用户管理；页面可滚动、按钮可点击、当前保存按钮文案随 tab 改变。
- 反复进入/离开设置页后，已打开的模态框被关闭，预览请求被取消，Station token 与 AI API Key 输入不残留在界面。

## 保存边界

- 常规、文件存储、生产运行保护、用户安全策略只通过 `/settings` 保存当前页相关配置，不提交 PLC、Station、相机或 AI 草稿。
- PLC tab 顶部保存只调用 `/plc/settings`；非法端口、S7 Rack、S7 Slot 或缺少 IP 时阻断提交并显示中文错误。
- Station tab 顶部保存只调用 `/station-communication/settings`；token reveal/regenerate 不写入 `/settings`，离开 tab 后 token 重新隐藏。
- 相机 tab 顶部保存只调用 `/cameras/bindings`；未选相机、曝光、增益、帧率、光电触发超时或串口号非法时阻断提交。
- AI tab 顶部保存只调用 `/ai/models` 或 `/ai/models/{id}`；API Key 保存后从输入框清空，不由其他 tab 的保存动作隐式提交。

## 高风险操作

- 恢复默认设置、删除用户、重置密码、重新生成 Station token、删除相机均有明确二次确认，确认文案能说明后果。
- 非管理员用户看不到用户管理入口，也不能通过设置页前端触发用户新增、删除或重置密码操作。

## 相机预览释放

- 打开连续或外触发预览后关闭弹窗，请求被取消，连续预览 session 停止，ObjectURL 释放，重新打开预览仍可正常加载。
- 打开软件触发或光电触发预览后关闭弹窗，自动重新布防计时器停止，AbortController 取消，刷新按钮和 Enter 捕获监听不叠加。
- 发现相机弹窗反复打开和关闭后，“添加绑定”按钮不会重复提交同一绑定。
