# GR 机器人应用寄存器合同 v0（验证草案）

> 状态：`DRAFT — NOT DEPLOYED — DEVICE VALIDATION REQUIRED`。
> 范围：建议使用手册所述用户区 `0–124`（UINT16）建立 PC 与示教器内参数化程序的事务握手。**不代表**这些地址已分配、这些字段能作为 FLOAT、或任何机器人已部署此协议。

## 1. 目标与不变量

- 机器人控制器始终拥有运动、安全、限位、模式、急停和最终执行决定权；上位机只提交受限任务。
- PC 与机器人各自只写自己的寄存器区域；读取对方区域。双方均不写对方的状态字段。
- 一条命令以单调 `CommandId` + 最后写入的 `Commit` 原子发布；不得采用长期 `Trigger=1`。
- `Ack`、`Busy`、`Done`、`Error` 必须关联 CommandId；任何断线/超时不得推定成功，也不得自动重放可能已被机器人接收的动作。
- 本合同不使用 `1000–1010` 控制寄存器，也不假设 0–124 已经定义为坐标 FLOAT 区。

## 2. 地址分区（待现场验证后分配）

所有下列 offset 都是逻辑字段，`TBD` 表示不能在 G0 前映射为实际 PDU 地址；总占用和对齐须经机器人程序、现场 PLC/HMI 与安全负责人共同审批。

| 逻辑区 | 写所有者 | 读所有者 | 内容 | 地址/长度 |
|---|---|---|---|---|
| PC command header | PC | Robot | `ProtocolVersion`、`CommandId`、`CommandType`、`TargetType`、options、`Commit` | `TBD` U16 words |
| PC payload | PC | Robot | 目标参数、ProgramId、速度等 | `TBD`；明确编码后才允许多 word 写 |
| Robot acknowledgement | Robot | PC | `AckCommandId`、state、`ErrorCode` | `TBD` U16 words |
| Robot telemetry | Robot | PC | heartbeat、协议版本、诊断/能力 | `TBD` U16 words |
| Reserved | none | both | 未来扩展；必须写零/忽略 | `TBD` |

**建议的提交原则：** PC 先 FC16 写 payload/header（不含 Commit），最后用已证明安全的单 word FC06 或 FC16 写 `Commit=CommandId`；机器人仅在完整一致的 `CommandId/Commit` 上升时采样。若 FC16 原子语义无法从实机证实，程序必须通过双读/版本校验拒绝半写数据。

## 3. 字段定义

| 字段 | 方向 | 语义 | 初始编码建议 | 状态 |
|---|---|---|---|---|
| `ProtocolVersion` | PC → Robot；Robot → PC | 合同大版本；不兼容版本必须拒绝。 | U16，初始候选 `1` | 需双方实现及现场读写验证 |
| `CommandId` | PC → Robot | 每条任务唯一、单调递增，0 保留。 | U16 或由两个 U16 组成的 U32，**TBD** | 位宽/回绕未定 |
| `Commit` | PC → Robot | 最后写入，值等于 CommandId 才表示完整发布。 | 与 CommandId 同编码 | 未部署 |
| `CommandType` | PC → Robot | 例如 NoOp、MoveJoint、MoveCartesian；G0 P0–P3 只允许 NoOp/当前位姿 no-op。 | U16 枚举，**TBD** | 运动枚举不得在验证前开放 |
| `TargetType` | PC → Robot | Joint / Cartesian / None。 | U16 枚举，**TBD** | 未部署 |
| `ProgramId` | PC → Robot | 仅允许机器人端白名单内的任务/程序标识。 | U16，**TBD** | 不等同 1006–1009 的程序槽 |
| `Target[0..5]` | PC → Robot | 六个关节或笛卡尔分量；仅在 TargetType 明确时有意义。 | U16、INT32 或 IEEE-754 FLOAT 的编码待验证 | 不得假定 FLOAT/坐标布局 |
| `SpeedOverride` | PC → Robot | 上位机请求值；机器人仍以安全上限裁剪。 | 缩放整数优先候选，**TBD** | 范围由安全负责人定义 |
| `Options` | PC → Robot | 版本化 flags；未知 bit 必须拒绝。 | U16 | 未部署 |
| `AckCommandId` | Robot → PC | 已接受/已解析命令。 | 与 CommandId 同编码 | 未部署 |
| `RobotState` | Robot → PC | `Idle / Accepted / Busy / Done / Error`。 | U16 枚举 | 未部署 |
| `DoneCommandId` | Robot → PC | 正常终态关联的命令。 | 与 CommandId 同编码 | 未部署 |
| `ErrorCode` | Robot → PC | 机器人端可审计错误码；PC 不覆盖。 | U16；码表 TBD | 未部署 |
| `RobotHeartbeat` | Robot → PC | 单调变动/递增值，表示程序仍在服务。 | U16，回绕规则 TBD | 未部署 |

## 4. 数据类型、比例与字序

1. 原始手册仅确认用户区为 `UINT16`；使用两个 word 表示 INT32/FLOAT、带符号、比例、低/高 word 及 byte order 均要由实机 round-trip 验证。
2. 手册对 `300–331` 状态/位置区说明 FLOAT 为大端、高字在前；这不能自动外推到用户区中的程序变量转换。合同只可在 `ModbusRead/Write NumType` 和 PC 解码双向证明后锁定。
3. 推荐先以 U16 sentinel 验证（`0x1357`、`0x2468` 之类的非安全测试值，实际值由现场批准），再测试双 word 定点整数；FLOAT 是最后一层，不作为 G0 的先决假设。
4. 所有缩放系数、单位、坐标框架（base/tool/workobject）、角度单位、工具与工件 ID 须逐字段写入 versioned map；不能由 UI 标签或临时注释暗示。

## 5. 状态机、幂等与恢复

```text
Idle
  PC complete payload + Commit(CommandId) → Accepted(AckCommandId=CommandId)
  → Busy
  → Done(DoneCommandId=CommandId) | Error(AckCommandId=CommandId, ErrorCode)

重复的 CommandId：机器人不得重复运动；返回此前已记录的终态或明确 Unknown。
PC 超时/断线：PC 记录 Unknown；重连后先读 robot state、Ack/Done/heartbeat，再由人工或显式对账决定，禁止自动重放。
```

- PC timeout、机器人程序 timeout、心跳 stale 时间及重连 backoff 都是 `TBD`，需基于只读稳定性与安全评估测得。
- `Busy` 不等于成功；只有匹配 CommandId 的 `Done` 才是正常终态。`Error` 与通信断开必须保留失败/未知证据。
- PC 重启前需持久化 CommandId、payload hash、最后已知状态、时间戳与原始 ADU 摘要；机器人侧要在可行的范围内保留最近一次已处理 CommandId。具体存储机制是后续实现，不是 G0 改动。

## 6. 安全范围与命令授权

| 范围 | 合同要求 |
|---|---|
| 坐标 | 每个 X/Y/Z、姿态、关节、速度、加速度、工具/工件组合均由安全负责人和机器人程序设置硬上限；当前均为 `TBD by safety authority`。 |
| 初始动作 | 先当前实际位置 no-op；下一步才是现场人员批准的极小、低速偏移。 |
| 校验 | PC 与机器人都做版本、范围、模式、状态、CommandId 校验；机器人端拒绝是最终防线。 |
| 停止 | 软件 `Error`/timeout 不是硬件急停替代；急停、安全门、人工接管保持控制器原生语义。 |
| 控制寄存器 | `1000–1010` 完全在本合同外；1010 尤其在被实机证明前永久禁用。 |

## 7. 合同接受 Gate

在把本草案变成 deployed v1 前，必须完成：地址/Unit ID/基准确认；PC→Robot→PC U16 往返；多 word 编码 round-trip；commit 半写拒绝；重复 CommandId 不重复动作；断线后不自动重放；安全边界审签；当前位姿 no-op；极小低速偏移及 Ack/Busy/Done/Error 全路径证据。未满足任一项时本文件只能作为设计输入。
