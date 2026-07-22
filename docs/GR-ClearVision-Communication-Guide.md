# GR 机器人与 ClearVision 通讯指南

## 1. 适用范围

本指南针对当前已经确认的 GR 机器人控制器通信参数：

| 项目 | 当前值 |
| --- | --- |
| 设备角色 | Modbus TCP 服务端 |
| ClearVision 角色 | Modbus TCP 客户端 |
| 机器人 IP | `172.16.87.12` |
| TCP 端口 | `502` |
| Unit ID / Slave ID | `255` |
| 状态功能码 | `ReadHolding`，即 Modbus 功能码 03 |
| 状态起始地址 | `437` |
| 状态寄存器数量 | `23`，读取 `437-459` |

本版本的 GR 配置是只读配置。它用于确认控制器上电、报警、安全门、急停、运行模式和程序状态，不用于向机器人下发使能、启动、运动或复位命令。

## 2. TCP 与 Modbus TCP 的区别

普通 TCP 只提供字节流。双方还要自行约定字符串、分隔符、编码、粘包和回复格式，例如发送 `START\r\n`。

Modbus TCP 则是在 TCP 连接之上定义了固定的二进制工业协议，报文中包含事务号、Unit ID、功能码、寄存器地址、数量和数据。读取保持寄存器必须发送 Modbus 读命令，不能在 ClearVision 的普通字符串 TCP 窗口中随意输入测试文本。

因此，GR 当前这组状态寄存器应使用 ClearVision 的 `Modbus TCP通信` 算子，不使用自定义字符串 TCP 算子。普通 TCP 连接成功只代表端口接受了 TCP 连接，不代表 Modbus 参数正确。

## 3. 接线和电脑网络配置

### 3.1 物理连接

1. 网线直连机器人控制柜或控制器网口。
2. 确认机器人控制器已上电，并确认示教器或控制器网络设置中的 IP 为 `172.16.87.12`。
3. 电脑网卡配置为同一网段的静态地址，例如：

   - IP：`172.16.87.20`
   - 子网掩码：`255.255.255.0`
   - 默认网关：直连调试通常可以留空
   - DNS：直连调试通常不需要

   电脑地址不能与机器人地址重复。

4. Windows 防火墙如果拦截出站连接，需要允许 ClearVision 或 TCP 端口 `502` 的出站访问。

### 3.2 检查 TCP 端口

在 PowerShell 中执行：

```powershell
ping 172.16.87.12
Test-NetConnection 172.16.87.12 -Port 502
```

`ping` 失败不一定说明设备不可用，因为设备可能禁用 ICMP。判断 Modbus TCP 是否具备基础条件，重点看：

```text
TcpTestSucceeded : True
```

如果 TCP 端口测试失败，先检查 IP、网线、网卡静态地址、机器人端 Modbus 服务是否启用和防火墙，不要先修改寄存器地址。

## 4. 在 ClearVision 中创建工程

1. 登录 ClearVision。
2. 新建工程。
3. 工程用途选择 `通信调试`，对应内部流程用途 `Commissioning`。
4. 保存工程。
5. 在流程画布中添加 `Modbus TCP通信` 算子。

通信调试工程和视觉检测工程要分开。通信调试工程不会强制视觉最终 OK/NG 判定，但仍然受到后端执行准入和权限保护。

当前账号需要具备 `Engineer` 或 `Admin` 角色，也就是 `CanOperateHardware` 权限。普通 Operator 账号访问设备通信端点会被拒绝。

## 5. Modbus 算子参数

在 `Modbus TCP通信` 算子中填写：

| 参数 | 建议值 | 说明 |
| --- | --- | --- |
| `Protocol` | `TCP` | 当前 GR 设备使用 Modbus TCP |
| `IpAddress` | `172.16.87.12` | 机器人控制器地址 |
| `Port` | `502` | Modbus TCP 默认端口 |
| `SlaveId` | `255` | GR 当前确认的 Unit ID |
| `RegisterAddress` | `437` | 状态起始地址 |
| `RegisterCount` | `23` | 读取到 `459` |
| `FunctionCode` | `ReadHolding` | 读取保持寄存器，功能码 03 |
| `TimeoutMs` | `5000` | 首次调试可保持默认值 |
| `WriteValue` | 留空 | 读取操作不使用 |
| `ProfileId` | 可留空 | 使用设备配置时填写 |
| `TemplateId` | `gr-robot` | 使用 GR 模板时填写 |
| `TemplateVersion` | `3.0` | 模板版本 |

不要填写 `WriteSingle` 或 `WriteMultiple`。当前 GR 模板没有任何允许写入地址，ClearVision 后端也会在网络访问前拦截诊断写入。

## 6. 推荐的验证顺序

### 第一步：Connect

先做 TCP 连接测试，只验证：

- `172.16.87.12:502` 是否可建立 TCP 连接；
- 控制器是否监听 Modbus TCP 端口；
- 网络延迟是否正常。

连接成功不等于 Modbus 读取成功，也不等于 Unit ID、功能码和地址正确。

### 第二步：ReadOnce

执行一次只读保持寄存器读取：

```text
Operation      = ReadOnce
FunctionCode   = ReadHolding
StartAddress   = 437
Count          = 23
UnitId         = 255
```

成功结果中应包含：

- `Output.Values`：原始寄存器值；
- `Output.Registers`：带地址的寄存器值；
- `Output.StartAddress`；
- `Output.Count`；
- `Output.Operation`；
- `Output.LatencyMs`；
- GR 模板匹配时的 `Decoded`：已解释的状态值。

### 第三步：流程单次运行

在通信调试工程中点击单次运行。前端会调用：

```text
POST /api/commissioning/execute
```

它不会把当前选择的相机 ID 加入请求，也不会隐式调用视觉检测接口。成功表示通信流程执行成功，结果属于“执行成功、视觉判定不适用”；读失败则显示为执行失败。

## 7. GR 状态寄存器映射

当前模板读取 `437-459`。其中已确认的关键寄存器如下：

| 地址 | ClearVision key | 含义 | 本次读取值 | 解释 |
| ---: | --- | --- | ---: | --- |
| 437 | `powered` | 控制器上电 | `1` | 已上电 |
| 438 | `enabled` | 机器人使能 | `0` | 未使能 |
| 442 | `alarmCode` | 报警代码 | `2050` | 当前存在报警代码 |
| 443 | `safetyDoor` | 安全门状态 | `0` | 当前安全门状态不满足安全运行条件，具体 0/1 语义应以 GR 手册定义为准 |
| 444 | `emergencyStop` | 急停状态 | `1` | 急停处于有效状态 |
| 445 | `alarmActive` | 报警有效 | `1` | 报警有效 |
| 449 | `operatingMode` | 运行模式 | `2` | 模板解码为 `ManualHigh` |
| 456 | `programState456` | 程序状态 | `0` | 原始值，当前未做语义推断 |
| 457 | `programState457` | 程序状态 | `0` | 原始值，当前未做语义推断 |
| 458 | `programState458` | 程序状态 | `0` | 原始值，当前未做语义推断 |
| 459 | `programState459` | 程序状态 | `0` | 原始值，当前未做语义推断 |

`439-441`、`446-448`、`450-455` 目前保留为原始值，没有根据名称猜测具体业务含义。后续只有在 GR 手册或实机对照实验确认后，才能扩展解码。

## 8. 当前状态的安全判断

本次读取的组合状态是：

```text
Powered       = true
Enabled       = false
AlarmCode     = 2050
EmergencyStop = true
AlarmActive   = true
OperatingMode = ManualHigh
```

这不是允许启动机器人的状态。特别是：

- 上电不代表可以运动；
- `enabled=0` 表示当前未使能；
- `alarmActive=1` 且报警代码为 `2050`，必须先按 GR 手册处理报警；
- `emergencyStop=1` 时不得尝试启动；
- 手动模式也不能作为自动运行许可；
- ClearVision 当前不提供绕过这些状态的写入按钮或写入接口。

不要为了验证通信而写入使能、急停复位、报警复位、程序号、启动或运动寄存器。通信验证阶段只读取 `437-459`。

## 9. API 级配置方式

当前后端提供以下接口，均要求 `CanOperateHardware` 权限：

```text
GET    /api/communication/templates/gr
GET    /api/communication/profiles
PUT    /api/communication/profiles/{id}
DELETE /api/communication/profiles/{id}
POST   /api/communication/diagnostics/execute
```

### 9.1 查看 GR 模板

```text
GET /api/communication/templates/gr
```

应看到：

```json
{
  "templateId": "gr-robot",
  "version": "3.0",
  "protocol": "ModbusTcp",
  "defaultPort": 502,
  "defaultUnitId": 255,
  "statusRange": {
    "startAddress": 437,
    "count": 23,
    "functionCode": "ReadHolding"
  },
  "writePolicy": {
    "enabledByDefault": false,
    "allowedAddresses": []
  }
}
```

### 9.2 保存只读设备配置

```text
PUT /api/communication/profiles/gr-robot-172-16-87-12
```

请求体：

```json
{
  "name": "GR Robot 172.16.87.12",
  "host": "172.16.87.12",
  "port": 502,
  "unitId": 255,
  "templateId": "gr-robot",
  "templateVersion": "3.0"
}
```

服务端会重新绑定当前模板的版本和 SHA-256，并强制保存为 `readOnly=true`。配置文件位置是：

```text
%LOCALAPPDATA%\ClearVision\Communication\modbus-profiles.json
```

### 9.3 只读诊断

连接测试：

```json
{
  "operation": "Connect",
  "profileId": "gr-robot-172-16-87-12",
  "timeoutMs": 5000
}
```

状态读取：

```json
{
  "operation": "ReadOnce",
  "profileId": "gr-robot-172-16-87-12",
  "functionCode": "ReadHolding",
  "startAddress": 437,
  "count": 23,
  "timeoutMs": 5000
}
```

诊断接口只允许 `Connect`、`ReadHolding` 和 `ReadCoils`。任何写操作或未知操作都会返回错误，不会建立 Modbus 写操作连接。

## 10. 常见错误判断

| 现象 | 优先检查 |
| --- | --- |
| `TcpTestSucceeded=False` | 电脑静态 IP、网线、机器人 IP、端口监听、防火墙 |
| Connect 成功但 ReadOnce 失败 | Unit ID `255`、功能码 `ReadHolding`、地址 `437`、数量 `23` |
| 返回 Modbus exception | 地址基准、功能码、Unit ID，确认没有把 `437` 改成 `40437` |
| `COMMUNICATION_ENDPOINT_INVALID` | Host、Port、UnitId 是否完整且在范围内 |
| `COMMUNICATION_PROFILE_NOT_FOUND` | ProfileId 是否已保存，大小写和 ID 是否正确 |
| `COMMUNICATION_WRITE_BLOCKED` | 这是预期安全保护，不要绕过；当前版本不允许写入 |
| `ADMISSION_FLOW_PURPOSE_MISMATCH` | 工程是否为“通信调试”，是否从单次通信调试入口执行 |
| Read 成功但没有 OK/NG | 正常；通信调试只确认执行和状态，不做视觉判定 |
| 读到的值和手册不一致 | 首先确认地址基准、Unit ID、功能码和控制器版本，不要立即修改模板 |

## 11. 验收清单

- [ ] 电脑和机器人使用同一网段且 IP 不冲突。
- [ ] `Test-NetConnection 172.16.87.12 -Port 502` 成功。
- [ ] ClearVision 使用 Engineer 或 Admin 账号。
- [ ] 工程用途为 `通信调试`。
- [ ] 算子使用 `Modbus TCP通信`，不是普通字符串 TCP。
- [ ] `Protocol=TCP`、`Port=502`、`SlaveId=255`。
- [ ] `FunctionCode=ReadHolding`、`RegisterAddress=437`、`RegisterCount=23`。
- [ ] Connect 成功。
- [ ] ReadOnce 返回 23 个寄存器，并能看到 437、438、442、443、444、445、449 的解码结果。
- [ ] 没有使用任何写功能码。
- [ ] 机器人报警和急停状态已单独由现场人员确认，不能把“通信成功”当成“允许运动”。

## 12. 后续要实现机器人动作前的必要条件

当前版本不应直接让机器人动起来。若后续要增加使能、程序选择、启动或运动控制，必须先依据 GR 通信总线手册建立经过现场验证的命令映射，并增加：

1. 明确的写地址白名单和数据范围；
2. 报警、急停、安全门、模式、伺服和远程控制权限互锁；
3. 后端二次确认和危险操作授权；
4. 操作人、时间、工程、寄存器、原值和新值审计记录；
5. 虚拟 PLC/仿真回归测试；
6. 低速、单步、空载和现场急停验证方案。

在这些条件完成前，ClearVision 与 GR 的通信验收目标应限定为“TCP 连通 + Modbus 只读状态读取成功”。
