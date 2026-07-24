# ClearVision × GR 机器人上位机控制 TODO

> **状态：`BLOCKED_BY_DEVICE_EVIDENCE`**
> **G0 审计结论：代码架构已审计；关键设备协议与最小参数化程序缺少可复现实机证据，禁止进入正式产品开发。**
> **目标设备：珠海格力 GR / GMEEMC06A 控制器，LAN2，Modbus TCP**
> **核心原则：先证明、后控制；先 Desktop 调试、后 Station 生产；机器人控制器负责运动，ClearVision 负责任务。**

---

## 0. 最佳情况下能做到什么

ClearVision 最终可以成为一套**视觉引导机器人任务控制上位机**，形成下面的生产闭环：

```text
相机采图
  → 视觉检测/定位
  → 像素坐标转换为机器人工作坐标
  → 选择机器人程序与目标参数
  → Station 通过 Modbus 写入任务
  → 机器人参数化程序执行 MovJ / MovL
  → 返回 Ack / Busy / Done / Error
  → ClearVision 保存图像、坐标、程序、结果、耗时和报警证据
  → 继续下一目标或进入异常恢复
```

### 0.1 最终可达能力

- 实时读取笛卡尔位姿、J1–J6、使能、运行、暂停、模式、急停、报警、倍率、DI/DO、产量；
- 在安全条件满足时修改倍率、复位、加载/选择程序、上/下使能、暂停和启动；
- 通过用户寄存器向机器人参数化程序下发：
  - 关节目标 `JointTarget`；
  - 笛卡尔目标 `RobTarget`；
  - 程序号、工件号、动作类型、速度和业务参数；
- 机器人返回命令确认、执行中、完成、失败和错误码；
- ClearVision 流程中使用专用机器人算子完成：
  - 视觉定位；
  - 2D 平面标定后的 X/Y 下发；
  - 固定安全 Z、姿态和速度；
  - 多目标排序与逐点执行；
  - NG 跳过、超时停止、报警恢复；
- Station 在生产现场独立运行，Studio 关闭后机器人协同仍可持续；
- Studio 显示机器人实时状态、命令时间线、任务结果和故障证据；
- 若 FTP 外部工程部署经现场证明可行，可增加受控的“机器人程序包部署/回滚”工具。

### 0.2 明确达不到或不应实现

- 不成为浏览器版示教器；
- 不提供 J1–J6 连续点动或实时摇杆控制；
- 不承担逆运动学、关节插补、伺服周期和速度曲线；
- 不绕过机器人控制器的限位、碰撞、安全门和急停；
- 不用软件停止冒充硬件急停；
- 不通过反复 FTP 上传文件实时改变位姿；
- 不允许网页、流程算子、Studio 和 Station 分别建立机器人连接；
- 不开放“任意地址 + 任意值”的生产写寄存器 API。

---

## 1. 产品与运行时边界

### Studio / Desktop

负责：

- 设备配置；
- 只读监控；
- 寄存器诊断；
- 通信联调；
- 手动受控命令测试；
- 参数化程序和坐标协议验证；
- 生产状态观察。

### Station

负责：

- 正式生产运行；
- 唯一 Modbus 会话所有权；
- 视觉流程执行；
- 坐标转换与机器人任务下发；
- 命令重试、超时、恢复；
- 本地日志和证据；
- 向 Studio 汇报状态。

### GR 控制器

负责：

- 参数化机器人程序；
- `ModbusRead/Write` 或 Socket 数据读取；
- `MovJ/MovL` 等运动指令；
- 坐标系、工具、工件和运动参数；
- 关节插补、限位和安全控制；
- 返回 Ack / Busy / Done / Error。

---

## 2. Opus 4.8 执行规则

- [x] 不得直接按本文猜测路径写代码，先完成 G0 代码与设备审计；
- [x] 每个 Goal 开始前记录 `Branch / Initial SHA / Remote SHA / Worktree`；
- [x] 每个 Goal 只能解决一个主问题，禁止多轨并行；
- [x] 不恢复 `FrontendV2`；
- [x] 不复用 `StationHub` 承载 Desktop 本机机器人页面状态；
- [x] 不新建第二套独立 Modbus 连接体系；
- [x] 不修改现有 PLC、TCP、Station 合同时顺便重构无关区域；
- [x] 写控制和运动测试必须晚于只读稳定性 Gate；
- [x] 任何不受手册支持的地址、FTP 行为或程序格式只能标记为 `FIELD_OBSERVED`；
- [x] 发现事实与计划冲突时，停止开发、更新 ADR，不得“先实现再解释”。

---

# G0 — 现实审计与协议证明

> **目标：将所有“推测可行”转换为可复现事实。G0 未通过，不进入产品开发。**

## G0.1 仓库审计

- [x] 确认当前目标分支和生效前端；
- [x] 定位并审计：
  - `ModbusCommunicationOperator`；
  - NModbus 连接池、超时、串行锁和断线清理；
  - `ClearVision.PlcComm` 中 HslCommunication 的实际用途与许可边界；
  - 当前 Settings 路由、Tab 安装和 Capability；
  - Desktop 与 Station 的依赖注入入口；
  - `.cvpkg` 运行包配置与 Station 运行时扩展点；
  - Station 状态、健康、日志和结果上报合同；
- [x] 判断是否存在能够复用的设备 Profile / Connection Manager；
- [x] 输出 `G0_Code_Audit.md`；
- [x] 输出 ADR：机器人 Modbus 复用 NModbus，还是有证据支持其他实现。

## G0.2 设备通信审计

在闲置机器人、低速、隔离区域完成：

- [ ] 固化设备型号、控制器版本、示教器版本；
- [ ] 确认 LAN2 IP、端口、Unit ID；
- [ ] 确认地址是 0 基还是 1 基；
- [ ] 验证 FC03 / FC06 / FC16；
- [ ] 验证 FLOAT 字节序；
- [ ] 验证 `300–311` 位姿和 `320–331` 关节值；
- [ ] 验证 `437–459` 为独立状态寄存器；
- [ ] 验证 `1000–1009` 每条命令：
  - 写入值；
  - 是否需要脉冲；
  - 是否自动清零；
  - 对应状态回读；
- [ ] 单独验证 `1010`，未通过则永久禁用；
- [ ] 验证 `1006–1009` 的 8421 编码、槽位和成功指示；
- [ ] 复现或否定“单连接 + FIN_WAIT_2”；
- [ ] 测量安全轮询周期和静默恢复时间；
- [ ] 输出抓包、寄存器快照和复现步骤。

## G0.3 参数化程序审计

- [ ] 取得 GR 语言/指令手册；
- [ ] 在示教器内创建最小 `T_Robot` 参数化程序；
- [ ] 证明 `ModbusRead` 可读取用户区；
- [ ] 证明用户区数据可构造成 `JointTarget` 或 `RobTarget`；
- [ ] 低速执行一个安全目标点；
- [ ] 验证机器人可以写回执行状态；
- [ ] 记录程序语法、变量类型和任务限制；
- [ ] 不把外部编写 `.proc/.pro` 的 FTP 导入视为已支持能力。

## G0.4 FTP 探索，独立 Gate

- [ ] 确认是否匿名登录、可读、可写和可覆盖；
- [ ] 备份原始工程目录；
- [x] 获取并审计现有 `MainFile.proc`，确认其中存在静态文本 `SocketReceive → MovJ`（不等于可编译/可运行）；
- [ ] 验证外部上传工程是否：
  - 出现在工程列表；
  - 被控制器识别；
  - 可加载；
  - 可编译；
  - 可运行；
  - 重启后仍存在；
- [x] 若与手册“不支持导入外部编辑程序”冲突，记录为非官方实验能力；
- [x] 未形成完整证据前，FTP 不进入产品主链路；
- [x] 输出 `G0_FTP_Feasibility.md`。

### G0 Gate

必须同时满足：

- [ ] 只读地址和类型已确认；
- [ ] 单连接约束已确认；
- [ ] 最小参数化程序已在示教器中安全运行；
- [ ] 自定义区至少完成一次“PC 写入 → 程序读取 → 程序回写”；
- [ ] 形成 `READY_FOR_IMPLEMENTATION` 结论。

### G0 审计回填（2026-07-24）

**当前 Gate：`BLOCKED_BY_DEVICE_EVIDENCE`。** 未勾选上方任何设备/参数化程序 Gate：本轮未连接实机、未占用 502、未发 Modbus 请求、未写寄存器、未启动或移动机器人、未进行 FTP 操作。代码侧结论不能替代这些实机证据。

- [x] 已输出仓库实际架构审计：[`GR机器人/G0_Code_Audit.md`](GR机器人/G0_Code_Audit.md)；
- [x] 已输出机器人地址、命令、单连接与恢复验证矩阵：[`GR机器人/G0_Device_Protocol_Verification_Matrix.md`](GR机器人/G0_Device_Protocol_Verification_Matrix.md)；
- [x] 已输出示教器内最小参数化程序验证方案：[`GR机器人/G0_Parameterized_Program_Verification.md`](GR机器人/G0_Parameterized_Program_Verification.md)；
- [x] 已输出用户区应用寄存器合同草案：[`GR机器人/GR_Robot_Application_Register_Contract_v0.md`](GR机器人/GR_Robot_Application_Register_Contract_v0.md)；
- [x] 已输出 FTP 证据冲突审计：[`GR机器人/G0_FTP_Feasibility.md`](GR机器人/G0_FTP_Feasibility.md)；
- [x] 已输出 NModbus 单栈 ADR：[`GR机器人/G0_ADR.md`](GR机器人/G0_ADR.md)。

---

# G1 — 统一 Modbus 会话与仿真基础

> **目标：同一个 `IP:Port` 在一个进程内永远只有一个连接所有者。**

- [ ] 抽取 `IModbusTcpSessionManager`；
- [ ] 复用现有 NModbus 的连接创建、存活检测、超时和清理逻辑；
- [ ] 所有同端点读写进入同一异步串行队列；
- [ ] 增加 Endpoint Reservation：
  - 机器人 Gateway 已占用时，通用 Modbus 算子不得另开连接；
  - 通用算子需要访问同一端点时必须复用 Session；
- [ ] 增加状态：
  - `Disconnected`
  - `Connecting`
  - `Online`
  - `Degraded`
  - `Recovering`
  - `LockedOut`
- [ ] 增加最近成功时间、延迟、错误码、连续失败次数；
- [ ] 建立虚拟 GR Modbus Server；
- [ ] 覆盖正常响应、超时、断线、静默、异常码、半开连接测试；
- [ ] 保持现有 `ModbusCommunicationOperator` 行为兼容。

### G1 Gate

- [ ] 同端点 100 个并发操作只创建一个 Socket；
- [ ] 所有读写严格串行；
- [ ] 断线后不会形成快速连接风暴；
- [ ] 现有 Modbus 算子测试全部通过；
- [ ] 模拟器合同测试通过。

---

# G2 — GR 只读 Gateway 与 Studio 调试页

> **目标：先交付稳定、可信、不会驱动机械臂的监控能力。**

## G2.1 后端

- [ ] 创建 `IGrRobotGateway`；
- [ ] 创建 `GrRobotRegisterMap`；
- [ ] 创建 FLOAT / raw16 Codec；
- [ ] 创建不可变 `GrRobotSnapshot`；
- [ ] 创建唯一 `GrRobotPollingService`；
- [ ] 分级轮询：
  - 核心状态：250 ms；
  - 位姿/关节：300 ms；
  - IO/倍率：500 ms；
  - 产量：1000 ms；
- [ ] 每个快照包含：
  - `sampledAtUtc`
  - `ageMs`
  - `quality`
  - `lastError`
  - `connectionState`
- [ ] 前端 API 只读取内存快照，不能触发 Modbus 请求。

## G2.2 Studio 页面

- [ ] 在当前正式设置体系增加「GR 机器人」；
- [ ] 显示：
  - 连接状态；
  - 笛卡尔位姿；
  - J1–J6；
  - 通电、使能、运行、暂停；
  - 机器人模式、运行模式；
  - 急停、安全门、报警编号；
  - 倍率、DI/DO、产量；
  - 数据新鲜度和最近错误；
- [ ] 页面关闭时清理 UI 定时器，但不关闭共享 Session；
- [ ] 不使用 `StationHub`；
- [ ] 首版使用 HTTP 读取快照，不急于增加 RobotHub。

### G2 Gate / R1

- [ ] 连续运行 8 小时无连接数增长；
- [ ] 页面刷新和切换不新增机器人连接；
- [ ] 状态与示教器一致；
- [ ] 数据过期后明显显示 `STALE`；
- [ ] 断线后不保留“运行中”假状态；
- [ ] 全程没有任何寄存器写入。

---

# G3 — 命令合同与受控基础操作

> **目标：实现任务级控制，不实现位姿运动。**

## G3.1 安全框架

- [ ] 默认 `RobotControlWriteEnabled=false`；
- [ ] 只有管理员可启用配置；
- [ ] 只有硬件操作权限用户可执行命令；
- [ ] 增加短时“调试控制已布防”；
- [ ] 页面离开、报警、急停、断线、数据过期时自动解除；
- [ ] 所有命令进入单一 `GrRobotCommandQueue`；
- [ ] 每个命令包含：
  - `CommandId`
  - `IdempotencyKey`
  - 用户；
  - 时间；
  - 目标设备；
  - 前置状态；
  - 写入值；
  - 回读结果；
- [ ] 禁止提供原始寄存器写 API。

## G3.2 基础命令

按风险从低到高逐项开放：

- [ ] 修改倍率；
- [ ] 复位；
- [ ] 选择/加载程序；
- [ ] 暂停；
- [ ] 下使能；
- [ ] 上使能；
- [ ] 启动；
- [ ] `1010` 停止仅在 G0 验证后开放。

## G3.3 前置条件

启动、上使能和程序加载至少要求：

- [ ] 快照未过期；
- [ ] 外部自动模式；
- [ ] 急停未触发；
- [ ] 无报警；
- [ ] 安全门满足现场规则；
- [ ] 没有未完成命令；
- [ ] 调试控制已布防；
- [ ] 写后完成状态回读。

### G3 Gate / R2

- [ ] 默认配置下所有写命令不可用；
- [ ] 非法状态全部由后端拒绝；
- [ ] 重复请求不重复触发；
- [ ] 命令结果不允许“无确认即成功”；
- [ ] 每条命令可审计和回放。

---

# G4 — 参数化机器人程序与握手协议

> **目标：打通 ClearVision 下发目标 → 机器人程序运动 → 状态返回。**

## G4.1 设计应用寄存器合同

不要直接把 `0–124` 写死成 FLOAT 坐标区。先形成：

`GR_Robot_Application_Register_Contract_v1.md`

至少定义：

- [ ] `ProtocolVersion`；
- [ ] `CommandId`；
- [ ] `CommandType`；
- [ ] `TargetType`：Joint / Cartesian；
- [ ] 六个目标分量；
- [ ] ProgramId；
- [ ] SpeedOverride；
- [ ] Options；
- [ ] RobotAckCommandId；
- [ ] RobotState：Idle / Accepted / Busy / Done / Error；
- [ ] ErrorCode；
- [ ] RobotHeartbeat；
- [ ] PC 和机器人各自拥有的寄存器区域；
- [ ] FLOAT 或缩放 INT32 的选择与字序；
- [ ] 写入顺序；
- [ ] 原子提交规则；
- [ ] 超时、重复命令和断线后的恢复；
- [ ] 坐标、角度和速度范围。

推荐采用：

```text
先写完整 Payload
→ 最后写 CommandId / Commit
→ 机器人检测到新的 CommandId
→ 复制数据并校验
→ 写 Ack
→ 执行运动
→ 写 Busy / Done / Error
```

禁止仅使用一个长期保持为 1 的 Trigger。

## G4.2 机器人侧最小程序

- [ ] 先在示教器正式创建，不依赖 FTP；
- [ ] 只允许在 `T_Robot` 中执行运动；
- [ ] 启动时停留在安全等待点；
- [ ] 读取新 CommandId；
- [ ] 校验范围、模式和参数；
- [ ] 将目标转换为机器人变量；
- [ ] 低速调用 `MovJ` 或 `MovL`；
- [ ] 写回 Ack / Busy / Done / Error；
- [ ] 处理重复命令；
- [ ] 处理 ClearVision 超时和断线；
- [ ] 保留硬件停止与人工接管。

## G4.3 ClearVision 联调

- [ ] 第一次只下发当前实际位置，验证机器人不产生运动；
- [ ] 第二次下发极小安全偏移；
- [ ] 再验证固定安全点 A → B；
- [ ] 验证越界、报警、急停和重复命令；
- [ ] 验证断线恢复后不自动重放旧任务。

### G4 Gate / R3

- [ ] 完成 100 次低速目标任务无重复动作；
- [ ] 每次动作都有 CommandId 对应的终态；
- [ ] 超时和断线不会继续执行未知新任务；
- [ ] 坐标越界在机器人和 ClearVision 两端均被拒绝。

---

# G5 — ClearVision 流程与视觉坐标集成

> **目标：从“调试按钮控制”升级为“视觉流程自动控制”。**

## G5.1 机器人设备 Profile

- [ ] 增加 `GrRobotProfile`：
  - IP / Port / UnitId；
  - 寄存器合同版本；
  - ProgramId；
  - 坐标类型；
  - 工件坐标系标识；
  - 速度上限；
  - 超时；
  - 安全范围；
- [ ] Profile 进入项目持久化和 `.cvpkg`；
- [ ] 密钥或高风险配置不明文下发；
- [ ] GET → PUT → GET 无损。

## G5.2 专用流程算子

新增 `GR机器人任务` 算子，不让业务流程直接写寄存器。

建议输入：

- `Execute`
- `TargetPose` 或 `JointTarget`
- `ProgramId`
- `SpeedOverride`
- `TimeoutMs`

建议输出：

- `Accepted`
- `CommandId`
- `RobotState`
- `Done`
- `ErrorCode`
- `ErrorMessage`
- `ActualPose`

约束：

- [ ] 复用全局 Robot Gateway；
- [ ] 不建立独立 Socket；
- [ ] 不暴露寄存器地址；
- [ ] 运行取消时执行明确的取消策略；
- [ ] 终态进入运行证据和结果记录。

## G5.3 视觉坐标链路

首版只做可控的 2D 平面任务：

- [ ] 相机标定；
- [ ] 图像像素 → 工件坐标 X/Y；
- [ ] Z、A/B/C 使用验证过的固定安全值；
- [ ] 设置机器人安全工作区域；
- [ ] 支持单点和多点；
- [ ] 多点按业务规则排序；
- [ ] 每个点单独生成 CommandId；
- [ ] 未完成上一个点前禁止发送下一个点；
- [ ] 保存：
  - 原图；
  - 检测位置；
  - 标定版本；
  - 下发坐标；
  - 机器人终态；
  - 周期耗时。

### G5 Gate / R4

- [ ] 完成“采图 → 定位 → 坐标转换 → 机器人动作 → Done”闭环；
- [ ] 视觉 NG 时机器人不动作；
- [ ] 标定失效、坐标越界和结果不确定时 fail-closed；
- [ ] 流程停止不会留下未追踪机器人命令。

---

# G6 — Station 生产运行时

> **目标：让生产不依赖打开 Studio HTML 页面。**

- [ ] Station 成为机器人连接唯一所有者；
- [ ] Studio 调试连接在 Station 接管前必须释放；
- [ ] 增加启动时机器人 Profile 校验；
- [ ] Station 加载 `.cvpkg` 后创建 Robot Gateway；
- [ ] 机器人连接异常不导致 Station 进程崩溃；
- [ ] Station 本地保存机器人命令和状态 JSONL；
- [ ] 重启后不自动重放 `Accepted/Busy/Unknown` 命令；
- [ ] 明确恢复策略：
  - 人工确认；
  - 查询机器人状态；
  - 对账 CommandId；
  - 决定继续或废弃；
- [ ] 通过正式 Station 同步合同向 Studio 汇报机器人健康摘要；
- [ ] 不把 Desktop 本机 Robot 页面消息混入 StationHub；
- [ ] Studio 只能观察和发送受治理的 Station 命令。

### G6 Gate / R5

- [ ] Studio 关闭后连续生产运行；
- [ ] Station 重启、网络闪断和机器人重启均可恢复；
- [ ] Studio 与 Station 不会同时占用 502；
- [ ] 未知终态必须人工或策略化对账，不能自动宣告成功。

---

# G7 — FTP 程序部署扩展（可选）

> **仅当 G0 FTP Gate 证明控制器支持外部工程部署时执行。**

- [ ] 将 FTP 定位为部署通道，不是运行控制通道；
- [ ] 仅管理员可使用；
- [ ] 上传前自动备份当前工程；
- [ ] 对程序包做白名单、哈希和版本检查；
- [ ] 禁止上传任意路径；
- [ ] 上传后验证：
  - 文件完整；
  - 工程被识别；
  - 编译/加载成功；
  - 对应程序槽正确；
- [ ] 失败时自动回滚；
- [ ] 生产运行中禁止替换活动程序；
- [ ] 默认关闭；
- [ ] 若只能通过非官方文件注入实现，保持 `EXPERIMENTAL`，不得作为生产必需能力。

---

# G8 — 最终证据与交付

## 自动化证据

- [ ] 单元测试；
- [ ] Modbus 仿真合同测试；
- [ ] 流程算子测试；
- [ ] 项目持久化测试；
- [ ] `.cvpkg` 打包/加载测试；
- [ ] Station 重启恢复测试；
- [ ] 权限和危险能力测试；
- [ ] CI 全绿。

## 现场证据

- [ ] 8 小时只读稳定性；
- [ ] 2 小时命令稳定性；
- [ ] 100 次参数化运动；
- [ ] 急停、安全门、报警、断线；
- [ ] 坐标越界；
- [ ] 重复命令；
- [ ] Station 重启；
- [ ] Studio/Station 所有权切换；
- [ ] 操作日志与实际动作一一对应。

## 最终文档

- [ ] 架构 ADR；
- [ ] GR 寄存器映射；
- [ ] 应用寄存器合同；
- [ ] 参数化机器人程序说明；
- [ ] Studio 调试手册；
- [ ] Station 生产部署手册；
- [ ] 安全操作手册；
- [ ] 故障恢复与对账手册；
- [ ] FTP 可行性结论；
- [ ] Final SHA / Remote SHA / Worktree clean；
- [ ] R1–R5 发布结论。

---

## 3. 建议的代码职责

最终路径以 G0 审计为准，建议职责如下：

```text
ClearVision.Product.Core/
  Robots/Gr/
    IGrRobotGateway.cs
    GrRobotProfile.cs
    GrRobotSnapshot.cs
    GrRobotCommand.cs
    GrRobotCommandResult.cs

ClearVision.Product.Infrastructure/
  Communication/Modbus/
    IModbusTcpSessionManager.cs
    ModbusTcpSessionManager.cs
    ModbusEndpointReservation.cs
  Robots/Gr/
    GrRobotRegisterMap.cs
    GrRobotCodec.cs
    GrRobotGateway.cs
    GrRobotPollingService.cs
    GrRobotCommandQueue.cs
    GrRobotCommandPolicy.cs
    GrRobotAuditLog.cs

ClearVision.Product.Desktop/
  Endpoints/
    GrRobotEndpoints.cs

ClearVision.Product.Station/
  Robots/Gr/
    GrRobotRuntimeHostedService.cs

当前正式前端/
  settings/
    grRobotTab.*

Operator/
  GrRobotTaskOperator.cs
```

---

## 4. 第一轮应立即执行的事项

Opus 4.8 第一轮只执行 G0，不写正式功能代码：

- [x] 输出仓库实际架构审计；
- [x] 输出机器人地址与命令验证矩阵；
- [x] 输出最小参数化程序验证方案；
- [x] 输出用户区寄存器合同草案；
- [x] 输出单连接复现方案；
- [x] 输出 FTP 支持/不支持的证据冲突分析；
- [x] 将本文从 `PROPOSED_AUDITED` 更新为：
  - `READY_FOR_IMPLEMENTATION`，或
  - `BLOCKED_BY_DEVICE_EVIDENCE`。

> **第一原则：先在示教器中跑通“读取一个外部值并回写”，再做 ClearVision 页面；先跑通“安全小偏移运动”，再做视觉自动闭环。**
