# G0 参数化程序最小验证方案

> 目的：用示教器中正式创建的无运动 `T_Robot` 证明用户区可由 PC 与机器人程序双向交换；然后才在独立安全批准下验证“当前位置无运动”和极小低速偏移。
> 本文是现场执行规程，不是机器人程序源码，也不授权 PC 写入。当前状态：`UNVERIFIED`。

## 1. 已知事实与边界

- 原始通信手册（`GR机器人通信总线手册 v3.0-1.pdf` 第 31 页）描述：用户区 `0–124` 可由 PC 使用 FC03/FC06/FC10 访问，机器人程序可使用 `ModbusRead(addr,value,NumType)` 与 `ModbusWrite(addr,value,NumType)` 交换数据。
- 原始示教器手册证明外部编辑程序导入不受支持（见 [`G0_FTP_Feasibility.md`](G0_FTP_Feasibility.md)）；本验证**必须**在示教器中创建，不经 FTP。
- 用户区的实际 PDU 偏移、`NumType`、组合数值字序及运行时访问权限未验证。不得把 `0–124` 假定成既有 FLOAT 坐标区。
- `T_Robot` 作为运动任务限制是计划要求；在本轮没有示教器实际创建/运行证据前，不声称已证明其 MovJ/MovL 权限。

## 2. 前置安全条件（全部必需）

1. 厂商/现场安全负责人书面批准，设备处于隔离非生产区域；急停、安全门、人工接管和可视观察都可用。
2. 现场人员确认控制器、固件、示教器版本，LAN2 IP/端口、Unit ID 和 0/1 基寻址；PC 只由授权人员接入。
3. 先完成 FC03 只读状态块与姿态/关节 block 的地址、Unit ID、FLOAT 解码验证；未完成则不得写用户区。
4. 确定一段未被其它程序使用的用户区范围，建立 PC/机器人所有权表；任何生产 PLC、HMI、诊断工具均断开或由现场负责人确认互斥。
5. 初始版本禁止 PC 写 `1000–1010`、禁止外部 FTP 部署、禁止自动重试写命令、禁止由 ClearVision 启动/使能/移动机器人。

## 3. 分阶段最小证明

| 阶段 | 现场动作 | 预期结果 | 必须留存的证据 | 未通过处理 |
|---|---|---|---|---|
| P0：示教器程序存在 | 在示教器正式创建 `T_Robot`，仅含安全等待、`ModbusRead`、`ModbusWrite`、状态回写；不含 MovJ/MovL。 | 程序保存、可打开、可在不运动下运行/等待。 | 任务名、程序截图/示教器导出（若官方支持）、变量类型、`NumType`、地址表、版本/时间。 | 停止；不尝试 FTP 注入或猜测语言语法。 |
| P1：机器人可读取 | 现场授权的 PC 向一组已批准、无副作用的 PC-owned U16 写入 `ProtocolVersion`、`CommandId`、测试 sentinel；最后写 Commit。 | 程序识别新 command，回写 Ack 与所读 sentinel。 | 每次 FC06/FC16 ADU、机器人读值截图、回写 block raw words、状态时间线。 | 立即停止写入；记录偏移/Unit ID/NumType 假设，恢复安全默认值。 |
| P2：机器人可回写 | 程序从 robot-owned 区按状态机写 `Ack=CommandId`、`Busy`、`Done` 或 `Error`。PC 只 FC03 读取。 | PC 读到单调状态变化；不得把长时 Trigger=1 当完成。 | 完整 raw block、decode 版本、程序日志或示教器截图、超时值。 | 记录断点；不得以客户端猜测终态。 |
| P3：当前位置无运动 | 仅在 P0–P2 通过后，将现场确认的实际当前位置以已验证的数据类型写为目标，程序验证范围后执行“同位姿 no-op”路径。 | 没有机械运动；Ack/Busy/Done 与 CommandId 对应。 | 前后姿态/关节截图、raw payload、速度设定、状态时间线、安全负责人签字。 | 触发人工安全流程；终态记 `Unknown/Error`，不自动重发。 |
| P4：极小低速偏移 | 仅在独立审批下，由现场人员输入在机器人安全工作区内的极小偏移、最低可用速度；程序使用经示教器确认允许的 MovJ 或 MovL。 | 一次可观察、可停止的微小运动；Done 或 Error 有回写。 | 目标/实际值、速度、工具/工件、保护区、视频或示教器记录、所有 Modbus ADU。 | 立即停止/人工接管；禁止自动 retry 或复制到生产。 |

## 4. `T_Robot` 逻辑要求（伪流程，不是语法）

```text
等待安全模式与现场人工许可
读取 robot-owned? no: 读取 PC-owned ProtocolVersion、CommandId、Commit、类型和 payload
若 Commit 与 payload 的 CommandId 不一致：保持 Idle，不执行
若 CommandId 已处理：回放对应终态，不重复运动
校验版本、范围、坐标系、工具/工件、速度和允许模式
先写 Ack(CommandId)，再写 Busy
P0–P2：执行无运动数据往返
P3：只允许已确认的当前姿态 no-op
P4：仅授权的极小低速动作
写 Done(CommandId) 或 Error(CommandId, ErrorCode)
心跳继续；通信失联、超时、安全条件失败时停留在机器人控制器安全逻辑下
```

程序的真实机器人语言、变量构造和 `MovJ`/`MovL` 语法必须由示教器及官方指令资料验证。本文件不会把 F1 `MainFile.proc` 的 `<STR>`、`<PAR>` 占位内容翻译为可执行代码。

## 5. PC 写入次序和禁止项

1. 读取 robot-owned 状态，确认 `Idle`、无报警、心跳新鲜、当前 `Ack/Done` 已与上次命令对齐。
2. 写 PC-owned payload 与 `CommandId`；对多 word 字段应以现场已证明的 FC16/字序写完整块。
3. 最后写独立 `Commit=CommandId`（或等价单调提交字段），再开始轮询 Ack。这样程序不会把半写 payload 当成任务。
4. 不使用一个长期保持为 `1` 的 Trigger；没有 Ack 前不再写同 CommandId；超时后标记 `Unknown` 并交由人工/对账策略。
5. 初始 Gate 前只允许现场人员使用批准的临时诊断客户端，ClearVision 不应实施写控制。

## 6. 记录与通过标准

每阶段应有 `PASS / FAIL / INCONCLUSIVE`，其中 PASS 需要全部同时成立：原始 request/response、示教器/机器人独立可观察证据、版本与地址/Unit ID/字序信息、完整状态时间线和安全负责人确认。仅“客户端未报错”或“脚本有打印”不合格。

G0 的参数化程序 Gate 至少要求 P0、P1、P2、P3 被真实设备证明。P4 是进入后续运动控制设计的安全证明，但仍不允许替代 G4 的完整压力、重复、断线与对账验证。

关联合同：[`GR_Robot_Application_Register_Contract_v0.md`](GR_Robot_Application_Register_Contract_v0.md)。
