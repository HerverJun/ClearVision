# G0 设备与协议验证矩阵

> 状态口径：`MANUAL_CONFIRMED` 仅表示原始手册明确描述；`FIELD_OBSERVED` 表示本地现场文件/记录存在；`UNVERIFIED` 需实机复核；`CONFLICTED` 表示来源互相矛盾。任何条目在取得可复现实机证据前均不构成 G0 PASS。
> 本轮没有连接设备、没有发送 Modbus 请求、没有写寄存器、没有抓包。

## 1. 证据来源与强度

| ID | 来源 | 证据强度与限定 |
|---|---|---|
| M1 | `C:\Users\11234\Desktop\GR机器人通信总线手册 v3.0-1.pdf`，SHA-256 `EF4FEE63A79CAD80E638F53FA847E3CC6D73B87E726C74F246BC943FBCAB6CD4` | 原始通信总线手册；PDF 页码以阅读器页码计。 |
| M2 | `C:\Users\11234\Desktop\GR机器人示教器手册 v3.0-1.pdf`，SHA-256 `5F56D18C9BD48A112EA3629C5CD7C8C4ED4241EB4317C70850BF68415415FD74` | 原始示教器手册。 |
| F1 | `C:\Users\11234\Desktop\ftp\robot-folder\MySystem\ModbusTCPapplmap.txt`（2026-07-24 09:09，11,718 bytes） | 本地下载件中的映射文本，不证明当前控制器配置或读取/写入成功。 |
| F2 | `C:\Users\11234\Desktop\ftp\RobotLog\RunningLog.log` 与本地 `tmp/` 中的历史脚本/注释 | 仅能表明历史记录/脚本存在；缺少原始 PCAP、原始 stdout 或可验证时间戳回读，不作为 PASS。脚本含危险动作，本轮未运行。 |
| R1 | `docs/GR机械臂-ModbusTCP速查手册.md`、`docs/GR-ClearVision-Communication-Guide.md`、内嵌 GR template | 仓库二次摘要/诊断默认值，只能作为待验证线索，不能覆盖原始手册或原始现场响应。 |

## 2. 验证矩阵

| 项目 | 当前分类 | 已有证据 | 明确未知/冲突 | 现场最小验证与合格证据 |
|---|---|---|---|---|
| 控制器型号、固件、示教器版本 | `UNVERIFIED` | 计划写 GMEEMC06A；无现场铭牌/版本页记录。 | 实际设备、固件、示教器版本未固化。 | 现场人员拍摄控制器铭牌与“关于”页面；记录序列号（可脱敏）、版本、时间。 |
| LAN2 默认 IP / 端口 | `MANUAL_CONFIRMED` | M1 PDF 页 25：LAN2，默认 `172.16.87.12:502`；页 30：最多 4 个端口，端口范围 100–1000。 | 默认不代表现场配置。 | 在示教器网络设置记录 LAN2 实际 IP、启用端口和模式；不要从 PC 扫描或占用 502。 |
| Unit ID | `CONFLICTED` | R1 template/指南为 `255`；F2 历史脚本使用 `1`；M1 未定义 Unit ID。 | 没有可复核原始响应；不可选定任一值。 | 在维护窗口用只读 FC03，对已知安全状态区分别以候选 Unit ID 读一次；保存请求/响应 ADU、时间、操作者、网络与控制器版本。只允许现场授权人员执行。 |
| PDU 地址 0 基 / 1 基 | `UNVERIFIED` | M1 列寄存器号；没有 PDU 偏移定义。 | 300/437 等文档号是否直接作为 PDU address 未证实。 | 在机器人静止、示教器可见时，对 437 与 436（或相邻非写地址）逐项 FC03；保存原始 request/response 与示教器状态对照，不通过猜测偏移。 |
| FC03 | `MANUAL_CONFIRMED` | M1 PDF 页 24 明确支持 FC03。 | 对现场当前 Unit ID/address 的真实响应未证实。 | 使用已确认 Unit ID 与只读状态区做一次最小 FC03；保存完整 ADU 与解析值。 |
| FC06 | `MANUAL_CONFIRMED` | M1 页 24 支持 FC06。 | 写入哪个地址、值、脉冲和安全后果均未实证。 | 只能在隔离、停机、现场书面授权下，对批准的无运动用户区测试 word 写入预定义 sentinel，再 FC03 回读并恢复；留存 ADU。不得写 1000–1010。 |
| FC16（手册写 FC10） | `MANUAL_CONFIRMED` | M1 页 24 支持 FC10，即 Modbus Function Code 16（FC16）。 | 现场多寄存器写入及原子性未实证。 | 同 FC06 前置条件，仅向经验证的用户区连续 word 写入，并读取完整块确认；保存 request/response。 |
| FLOAT 字节/字序 | `MANUAL_CONFIRMED` | M1 PDF 页 21：FLOAT 使用两个寄存器，大端、高字在前。 | 适用于用户区数据类型转换的实际运行时行为未实证。 | 只读比较示教器显示的已知位姿/关节值与 `300–311` / `320–331` 原始 word；记录 IEEE-754 解码及候选 byte/word order。 |
| `300–311` 笛卡尔位姿 | `MANUAL_CONFIRMED` | M1 页 21 明确地图。 | 0/1 基、Unit ID、实际数值和刷新行为未实证。 | 机器人保持静止，在示教器截屏位姿后 FC03 读全块；计算并对比六分量。 |
| `320–331` 关节值 | `MANUAL_CONFIRMED` | M1 页 21 明确地图。 | 同上。 | 同一时间窗读全块并同示教器 J1–J6 对照；保留 raw words。 |
| `437–459` 独立状态 | `MANUAL_CONFIRMED` | M1 页 22–23 列状态语义；R1 template 与 F1 亦列该区。 | 实际 bit/word 状态、地址偏移和更新周期未实证。 | 在静止、使能变化、运行变化等**非 PC 控制**的受监督状态下只读采样；每个状态附示教器照片与 raw block。 |
| `1000–1005` 控制值 | `MANUAL_CONFIRMED`（文档存在）/`UNVERIFIED`（行为） | M1 页 23–24：1000 倍率 0–100；1001–1005 文档写“0 default / 1 trigger”。 | 是否边沿、脉冲宽度、自清零和完成状态回读都未证实。 | 不得在 G0 本轮 PC 执行。未来先由安全负责人批准逐地址单变量试验：记录初值、写入 ADU、保持时长、回读、状态区快照、人工观察、恢复。 |
| `1006–1009` 8421 / 程序槽 / 成功指示 | `CONFLICTED` | M1 页 23–24 说明 8421；F1/F2 的本地映射/注释与“各地址写 1”解读不一致。 | 槽位编码、加载触发、成功或失败状态地址均未实证。 | 不得依照任一解释控制生产。现场在非生产控制器、只加载安全空程序且可人工观察时，对 8421 候选组合逐步验证；每一步保留 raw request/response 与示教器程序页。 |
| `1010` control_stop | `CONFLICTED` | M1 只列到 1009；F1 列 `1010 control_stop 停止控制`。 | 是否真实存在、读写语义及安全分类未知。 | G0 后仍默认禁用。仅在厂商/现场安全负责人确认后，针对隔离设备验证存在性与行为；未获证据永不开放。 |
| 用户区 `0–124` 类型与读写方式 | `MANUAL_CONFIRMED` | M1 页 24、31：用户区为 `UINT16`；PC 可用 FC03/FC06/FC10，程序可用 `ModbusRead(addr,value,NumType)`/`ModbusWrite(addr,value,NumType)`。 | 地址偏移、`NumType` 可用类型、INT32/FLOAT 组装、所有权、原子提交未实证。 | 先进行单 word sentinel 往返（PC→用户区→程序读→程序回写）；只在示教器创建的无运动 `T_Robot` 内进行，保存代码/截图、ADU、回读。 |
| 单连接限制 | `FIELD_OBSERVED`，未通过 | F2 脚本/笔记声称 502 单连接；缺没有可复核 capture。 | 数量、拒绝行为、现有客户端处理未知。 | 在隔离控制器，用批准的两个仅 FC03 client 做可控实验，逐步连接/释放；记录 socket state、控制器日志、每条响应。生产端口不得测试。 |
| 半开连接 / `FIN_WAIT_2` | `FIELD_OBSERVED`，未通过 | F2 历史说明提及 `FIN_WAIT_2`，无原始证据。 | 是否控制器、PC 或中间网络产生该状态未知。 | 使用隔离设备与受控测试机，记录双方 `netstat`、packet capture、时间线；禁止借此在生产 502 上进行异常断开。 |
| 安全轮询周期、静默恢复 | `UNVERIFIED` | 代码有 5,000 ms 默认操作 timeout（见代码审计），不是设备安全轮询实测值。 | 最小安全周期、长期稳定性、重连抖动和恢复时间未知。 | 在只读稳定性窗口收集至少 8 小时指标：周期、P50/P95/P99 延迟、失败、socket 数、重连、恢复；以现场风险评估批准值定义上限。 |

## 3. 不得伪造的 Gate

以下项目没有“PASS”证据：实际 LAN2 配置、Unit ID、寻址基准、任何控制写、1000–1010 行为、单连接/半开行为、轮询恢复、用户区程序往返、`T_Robot` 无运动与低速运动。模拟器、模板 JSON、脚本存在或二次指南均不能替代实机证据。

## 4. 现场执行记录模板

每一个测试步骤须单独产生以下记录，敏感 IP/用户名可脱敏但不得丢失可关联性：

```text
TestId / UTC timestamp / operator / safety approver
controller model / firmware / teach-pendant version / isolated-or-production flag
LAN2 IP:Port (masked if necessary) / Unit ID / PDU start / count / FC
request ADU hex / response ADU hex or exception / PC socket state
robot state before and after / teach-pendant photo or screenshot reference
raw register words / decoder and version / expected result / observed result
rollback or recovery action / conclusion (PASS | FAIL | INCONCLUSIVE)
artifact paths and SHA-256
```

执行顺序：先固定版本与 LAN2 配置 → 单一 FC03 状态读取 → 地址和浮点对照 → 只读稳定性/单连接试验 → 无运动用户区往返 → 仅在另有安全批准后做最小写入。任何异常、地址不一致或状态不确定立即停止，不重试控制写入。

关联：[`G0_Parameterized_Program_Verification.md`](G0_Parameterized_Program_Verification.md)、[`GR_Robot_Application_Register_Contract_v0.md`](GR_Robot_Application_Register_Contract_v0.md)。
