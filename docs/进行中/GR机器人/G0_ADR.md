# ADR-G0-001：GR Modbus 会话、运行时与 FTP 边界

- 状态：`ACCEPTED FOR FUTURE DESIGN; DEVICE GATE BLOCKED`
- 日期：2026-07-24
- 决策范围：后续 G1+ 的设计约束；本 ADR 不实现任何产品功能。

## 上下文

当前代码已有 NModbus 通用算子及一套只读 GR 诊断基线，也有基于 HslCommunication 的 PLC 实现、普通 TCP 管理器、StationHub 与 FrontendV2。此前计划含有“抽取 session manager / Gateway”等候选结构，但 G0 必须以当前 HEAD 为准，不能假设这些类型已经存在，也不能由模板或现场脚本替代实机协议证明。

## 决策

1. **唯一协议栈：** GR Modbus TCP 后续仅基于现有 `ModbusCommunicationOperator` 的 NModbus 实现抽取/复用。不得引入 Hsl Modbus 或任何第二个 Modbus client。
2. **唯一会话所有权：** 会话的身份是 IP:Port，不是 Unit ID；所有通用 Modbus 算子、GR gateway、轮询与受控命令都必须经同一个 endpoint queue / reservation 路径。现有静态池已按 IP:Port 锁定和串行化，但尚不具备跨 Desktop/Station 所有权，G1 才能在不破坏现有算子兼容性的前提下补足。
3. **运行时边界：** Desktop 仅做明确受治理的调试/观察。Station 进入生产运行后成为连接唯一所有者；Desktop 必须释放或被拒绝，不允许双方占用 502。
4. **只读基线边界：** GR profile/catalog/state decoder 与 Communication endpoint 的只读诊断可复用，但其临时裸 TCP Connect 不能成为生产 session；写拒绝语义保持不变，直到独立安全功能阶段。
5. **不混用：**
   - `ClearVision.PlcComm` / Hsl 栈只继续服务 S7、MC、FINS；
   - 普通 `TcpDeviceManager`/TCP operator 不提供 Modbus session；
   - `StationHub` 是 Station ingress，不承载 Desktop 本机机器人状态或调试；
   - FrontendV2 不在 G0 恢复，后续 UI 取决于实际启用的正式 startup path；
   - `.cvpkg` 当前没有 GR profile，未来必须走现有 field-extension/package validation 机制。
6. **FTP：** 外部程序部署保持 `EXPERIMENTAL`，不进入主链路。官方“不支持导入外部编辑程序”与本地 FTP 快照冲突未消除前，禁止把 FTP 当部署承诺或运行控制手段。
7. **设备默认拒绝：** Unit ID=255、Unit ID=1、1010、控制命令脉冲、单连接/FIN_WAIT_2、用户区 FLOAT/转换，均在实机复现前保持未验证/冲突状态。无 WRITE、无运动、无自动重试。

## 依据

- `Infrastructure/Operators/ModbusCommunicationOperator.cs:52–62,218–279,356–431,537–610`：NModbus、IP:Port pool、连接/操作锁、timeout、purge/idle 回收。
- `Infrastructure/Services/ConnectionPoolManager.cs:37–45,152–248`：key 含 slaveId 且仅提供普通 TCP lease，不适合唯一 GR 会话。
- `ClearVision.PlcComm/*.csproj` 与 Siemens/Mitsubishi/Omron clients：Hsl 是 PLC 协议实现，不是 GR NModbus 依据。
- `CommunicationEndpoints.cs:94–143`：GR Connect 临时 TCP 与只读诊断写拒绝。
- `VisionRuntimeServiceCollectionExtensions.cs:37–82,221–290`、Station 启动与同步合同：共享 core 与 Station 生产边界。
- [`G0_Device_Protocol_Verification_Matrix.md`](G0_Device_Protocol_Verification_Matrix.md)、[`G0_FTP_Feasibility.md`](G0_FTP_Feasibility.md)：手册、现场文件与冲突分析。

## 被否定或修正的计划假设

| 原假设/建议 | G0 结论 |
|---|---|
| 已有 `IModbusTcpSessionManager` 可直接接入 | 不存在；这是 G1 候选，必须从现有 NModbus 实现兼容演进。 |
| 可让 GR Gateway 使用 Hsl `ModbusTcpNet` | 否决；会形成第二套 Modbus 栈。 |
| `ConnectionPoolManager` 可做统一 endpoint owner | 否决；key 含 Unit ID，缺操作级 endpoint 串行化且未注册。 |
| Unit ID 255 可作为事实默认 | 否决；与现场脚本 Unit ID 1 冲突，手册不定义。 |
| StationHub 可承担 Desktop 机器人实时页 | 否决；它是 Station ingress，语义和命令合同不同。 |
| FrontendV2 应恢复来做机器人页 | 否决；本轮不恢复，实际正式前端由 startup flag 决定。 |
| FTP 工程文件存在即可作为部署路线 | 否决；只证明下载件静态文本，与官方限制冲突，完整部署链未证明。 |
| `0–124` 是既有 FLOAT 坐标区 | 否决；手册只确认 UINT16 用户区，编码/所有权/语义待实机。 |

## 后果

- G1 设计必须复用 NModbus 的连接创建、`IsConnectionAlive`、timeout 与 purge 原则，且用统一 endpoint queue/reservation 覆盖 reader、writer、gateway 和 operator。
- G2 不得写寄存器，不得因页面刷新创建新的机器人连接。
- G3/G4 前置的设备证据（Unit ID、寻址、控制命令、用户区往返、安全程序）尚未取得，任何产品开发不得将其默认值编码为“已验证”。
- G0 Gate 为 `BLOCKED_BY_DEVICE_EVIDENCE`；代码清晰只是必要条件，不是充分条件。
