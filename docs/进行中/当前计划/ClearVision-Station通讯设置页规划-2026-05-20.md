# ClearVision Station 通讯设置页规划

## Summary

- 本次仅做只读排查和规划，未修改业务代码。现状是 Station 主动通过 SignalR/HTTP 连接 Studio：Hub 为 `/hubs/station-ingest`，监控 REST 为 `/api/stations*`。
- 当前 `StationIngress` 在 Studio 的 `appsettings.json`，`StationSync` 在 Station 的 `appsettings.json`，没有接入现有设置页保存链路；现有“通讯连接”页实际主要是 PLC 配置。
- 第一版目标：在 Studio 设置界面新增“Station 设置”子页，支持“关闭 / 本机通讯 / 局域网总控”三种模式，只管理本机 Studio + 本机 Station，保存后提示重启生效。

## Key Changes

- 新增 Station 通讯配置存储，不再让用户改 `appsettings.json`：
  - Studio 侧保存 `StationIngress` 覆盖配置到 `%LocalAppData%\ClearVisionStudio\station-communication.json`。
  - Station 侧保存 `StationSync` 覆盖配置到 `%LocalAppData%\ClearVisionStation\station-sync-settings.json`。
  - `appsettings.json` 继续作为默认值和部署模板，不由 UI 回写。
- 启动读取逻辑：
  - Studio 启动时，在 `Acme.Product/src/Acme.Product.Desktop/Program.cs` 解析 Kestrel 监听前读取持久化 `StationIngress` 覆盖值。
  - Station 启动时在默认 `appsettings.json` 之后加载本机 `station-sync-settings.json`，让 UI 保存值覆盖默认配置。
- 模式映射固定为：
  - `Disabled`：Studio ingress 关闭，本机 Station sync 关闭。
  - `LocalLoopback`：Studio `Enabled=true, ListenMode=Loopback, Port=<port>`；本机 Station `Enabled=true, StudioBaseUrl=http://127.0.0.1:<port>`。
  - `LanController`：Studio `Enabled=true, ListenMode=Lan, Port=<port>`；本机 Station 默认仍用 `127.0.0.1` 连本机 Studio，同时页面显示给远端 Station 使用的 LAN 地址和 token。
- 安全默认：
  - 启用通讯时若没有 token，自动生成强 token；LAN 模式必须有 token。
  - 不在 UI 暴露 `AllowInsecureDevelopment` 开关，默认 false。
  - token 默认只显示掩码；Admin 可点击“显示/复制”或“重新生成”。

## APIs And UI

- 新增 Admin-only 后端接口：
  - `GET /api/station-communication/settings`：返回模式、端口、LAN 主机名/IP、token 掩码、配置路径、当前运行值、是否需要重启、诊断提示。
  - `PUT /api/station-communication/settings`：校验并保存 Studio ingress 与本机 Station sync 覆盖配置，返回保存后的诊断和重启提示。
  - `POST /api/station-communication/token`：`reveal` 返回当前 token，`regenerate` 生成并保存新 token。
- 设置页调整：
  - 将现有“通讯连接”改名为“PLC 通讯”。
  - 新增“Station 设置”页签，放在 PLC 通讯附近。
  - 页面包含模式分段控件、端口输入、LAN 地址输入、本机 Station 同步开关、token 操作、配置路径、重启提示、跳转 Station 监控按钮。
  - “本机通讯”一键生成可用的 loopback 配置；“局域网总控”显示远端 Station 需要填写的连接片段，但不尝试远程改其他机器配置。

## Test Plan

- 后端单元/集成测试：
  - 配置 store 能从空文件生成默认值，能保存并重新读取 Studio/Station 两侧配置。
  - 三种模式映射到正确的 `StationIngressOptions` 和 `StationSyncOptions`。
  - LAN 模式无 token 时自动生成或校验失败，端口超界失败。
  - 非 Admin 访问保存、显示 token、重生成 token 返回 403。
  - 保存后正确计算 `requiresRestart.studio` 和 `requiresRestart.localStation`。
- Station 启动配置测试：
  - `station-sync-settings.json` 覆盖 `appsettings.json`。
  - `StudioBaseUrl` 正确解析为 `/hubs/station-ingest`。
- 前端测试：
  - 设置页能切换到“Station 设置”，选择本机通讯后展示 `127.0.0.1:<port>`。
  - 保存成功后显示“Studio/Station 需重启生效”提示。
  - token 默认掩码，点击显示/复制只对 Admin 生效。
- 目标测试命令使用现有串行脚本，优先跑 `Acme.Product.Desktop.Tests` 中新增 Station 通讯配置相关测试。

## Assumptions

- 这里按“Studio 作为局域网总控，Station 作为现场执行站”理解前置描述里重复出现的 station。
- 第一版不做热切换、不自动重启进程，只保存配置并明确提示哪一侧需要重启。
- 第一版只管理本机 Station；远端 Station 通过复制出来的地址和 token 到远端机器配置，后续再规划集中下发。
- 不新增数据库迁移，不改变现有 Station 监控、命令、部署、结果同步协议。
