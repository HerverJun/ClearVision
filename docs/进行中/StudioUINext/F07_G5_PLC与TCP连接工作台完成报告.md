# ClearVision Studio UI Next F07 G5：PLC 与 TCP 连接工作台完成报告

## 状态

```text
REPORT_STATE=DONE
INITIAL_SHA=c36d19178a93c5c45336c50a8cda13f62951aad3
G5_SHA=2390e120
REMOTE_SHA=SEE_FINAL_DELIVERY_MESSAGE
G5_IMPLEMENTATION=DONE
G6_IMPLEMENTATION=NOT_ENTERED_IN_THIS_REPORT
G7_IMPLEMENTATION=NOT_ENTERED
```

本报告以执行时 HEAD `c36d19178a93c5c45336c50a8cda13f62951aad3` 为基线。实现保持在既有 Settings Shell、唯一 `SettingsOwner`、共享 `ProductRuntime.api` 和 `SETTINGS_ENDPOINT_MATRIX` 内，没有修改默认入口、Legacy、Project save chain、Runtime、Station 或 G7 能力。

## 交付能力

### PLC

- 支持 S7、MC、FINS 协议切换；每个协议保留独立地址、端口和协议字段草稿，切换协议不会覆盖其他协议草稿。
- 通过 `/api/plc/settings` 读取和保存协议设置，通过 `/api/plc/mappings` 读取和保存当前协议映射。
- 连接测试单独调用 `/api/plc/test-connection`，不复用保存请求，不创建长期 PLC client/session。
- 前端保留服务端 validation issues，展示字段和索引错误；保存成功后只接受服务端返回的 settings projection。
- 保存不会自动连接 PLC。

### TCP

- 支持 Client / Server profile、profile 草稿、profile 保存和 profile 删除/新增。
- profile 保存只调用 `PUT /api/tcp/profiles`，不会自动 connect 或 start server。
- Client connect/disconnect、Server start/stop、runtime status、文本/HEX 发送均调用 `/api/tcp/profiles/{id}/...` 专用 endpoint。
- 发送结果显示服务端 `response`；收发 frame log 只消费后端运行时投影，前端 decoder 将记录限制为最近 200 条，并支持清空。
- Profile 校验覆盖地址、端口、超时和定长报文长度；服务端 validation/error feedback 不被前端伪装为成功。

## 权限与 authority

| 操作 | Admin | Engineer | Operator |
| --- | --- | --- | --- |
| PLC settings/mappings 读取 | 允许 | 允许 | Settings route 禁止 |
| PLC 保存 | 允许 | 禁止 | 禁止 |
| PLC connection test | 允许 | 允许 | 禁止 |
| TCP profile 读取/runtime | 允许 | 允许 | Settings route 禁止 |
| TCP profile 保存 | 允许 | 禁止 | 禁止 |

- PLC/TCP 只使用专用 `/api/plc/**`、`/api/tcp/**`；没有 generic `/api/settings` fallback 或双写。
- 一个 Settings capability 只有一个 mounted owner、一个 write coordinator 和一个 shared `ApiTransport` 路径。
- Pinia、Vue state 和 DOM 只保存草稿及运行时投影；真实 PLC client、socket、server 和 frame authority 仍在后端。
- 保存、测试、连接、发送和 server 操作是不同 command，不把“保存成功”推断为“已连接/已监听”。

## 验证结果

| 门禁 | 结果 |
| --- | --- |
| `npm run lint` | PASS |
| `npm run typecheck` | PASS |
| `npm run test:unit` | PASS，115 files / 674 tests |
| Settings capability-local unit | PASS，9 files / 49 tests |
| `npm run build:production` | PASS |
| `npm run bundle:gate` | PASS |
| Desktop test project build | PASS，0 warnings / 0 errors |
| Desktop PLC/TCP/Camera 定向套件 | PASS，71/71 |
| virtual PLC + TCP runtime/loopback | PASS，13/13 |
| F07 Settings shell + device Browser fixture | PASS，11/11 Chromium tests |

Browser 证据覆盖 Admin 保存与运行分离、Engineer runtime access、Operator forbidden、PLC validation error、TCP response/log 和 no-auto-connect。TCP loopback 证据来自 `TcpEndpointsTests` 与 `TcpDeviceManagerTests`；不是生产现场网络验收。

独立 `ClearVision.Product.Tests` 定向构建阶段出现 `System.Collections.Immutable` 8/9 版本冲突 warning（由 `OperatorLibraryReadOnlyAuditRunner` 引用链产生），测试无错误且目标测试全部通过；该无关 warning 未改变 G5 行为。

## 未运行证据

- 真实 PLC、真实 S7/MC/FINS 设备和产线网络：`NOT_PERFORMED`。
- 真实 Camera/Huaray/Hikvision 硬件：`NOT_PERFORMED`，由 G6 deterministic fixture 覆盖 UI 合同。
- 真实 WebView2、Windows 125% DPI、Release publish、完整 CI：`NOT_PERFORMED`。

## 受保护路径

`ClearVision.Product/src/capabilities/` 是执行开始前已存在的受保护未知未跟踪路径。本轮未读取其内容用于实现、未修改、未删除、未加入 staging、未提交。

## 交付边界

G5 已完成。G6 与 G5 的共用 device contract/adapter/owner 基础在本分支继续交付，但本报告不把 G6 或 G7 宣称为完成；本轮不会进入 G7。
