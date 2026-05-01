# ClearVision 虚拟 Modbus TCP PLC 接入计划（给 Codex 执行）

## 0. 任务目标

在 ClearVision 仓库中安装并接入一个本地可运行的 **Modbus TCP 虚拟 PLC 示例包**，用于无实体 PLC 时测试项目里的 PLC 通讯能力，尤其是：

- TCP 连接；
- 读 Holding Register；
- 写 Holding Register；
- 读 Coil；
- 简单寄存器握手流程；
- 后续可扩展为自动化集成测试。

本次任务优先使用“方案 1：协议级虚拟 PLC”。不要把它做成厂商 PLC 仿真器，也不要修改生产 PLC 通讯架构。

---

## 1. ClearVision 当前项目约束

请先理解 ClearVision 当前 PLC 通讯相关结构：

1. `Acme.PlcComm` 当前支持 `S7 / MC / FINS`。

   相关文件：

   ```text
   Acme.Product/src/Acme.PlcComm/PlcClientFactory.cs
   Acme.Product/src/Acme.PlcComm/Siemens/SiemensS7Client.cs
   Acme.Product/src/Acme.PlcComm/Core/PlcBaseClient.cs
   ```

   `PlcClientFactory.CreateFromConnectionString` 当前只识别：

   ```text
   S7://...
   MC://...
   FINS://...
   ```

   本次不要在这里强行加入 Modbus。

2. ClearVision 已经有独立的 Modbus 算子。

   相关文件：

   ```text
   Acme.Product/src/Acme.Product.Infrastructure/Operators/ModbusCommunicationOperator.cs
   ```

   这个算子当前支持：

   ```text
   ReadCoils
   ReadHolding
   WriteSingle
   WriteMultiple
   ```

   参数包括：

   ```text
   Protocol
   IpAddress
   Port
   SlaveId
   RegisterAddress
   RegisterCount
   FunctionCode
   WriteValue
   TimeoutMs
   ```

3. `Acme.Product.Infrastructure.csproj` 已经引用了 `NModbus 3.0.81`。

   相关文件：

   ```text
   Acme.Product/src/Acme.Product.Infrastructure/Acme.Product.Infrastructure.csproj
   ```

   ClearVision 侧不需要为 Modbus 算子新增 NuGet 包。

4. `/api/plc/test-connection` 当前只支持 `S7 / MC / FINS`。

   相关文件：

   ```text
   Acme.Product/src/Acme.Product.Desktop/Endpoints/PlcEndpoints.cs
   ```

   本次不要把 Modbus 虚拟 PLC 接入这个接口。Modbus 连接测试先通过 `ModbusCommunicationOperator` 和 Python `test_client.py` 完成。

5. `CommunicationConfig` 当前也只定义了 `S7 / MC / FINS` 三种全局协议配置。

   相关文件：

   ```text
   Acme.Product/src/Acme.Product.Core/Entities/AppConfig.cs
   ```

   本次不要改全局通信配置结构。

6. 当前已有 Modbus 单元测试，但还没有连真实 Modbus server 的成功读写集成测试。

   相关文件：

   ```text
   Acme.Product/tests/Acme.Product.Tests/Operators/ModbusCommunicationOperatorTests.cs
   ```

---

## 2. 网络与代理说明

优先不要依赖外部 zip 下载包。请直接在 ClearVision 仓库中生成虚拟 PLC 示例文件。

只有下面几类操作可能需要外网或代理：

1. 执行 `pip install pymodbus` 时需要访问 PyPI；
2. 执行 `docker compose build` 时，Dockerfile 会从 PyPI 安装依赖；
3. 首次构建 Docker 镜像时，如果本机没有 `python:3.11-slim`，需要访问 Docker Hub；
4. 公司内网或国内网络环境可能需要配置：

   ```text
   HTTP_PROXY
   HTTPS_PROXY
   PIP_INDEX_URL
   PIP_TRUSTED_HOST
   Docker daemon proxy
   ```

推荐 Codex 支持两种安装方式：

```bash
# 方式 A：本地 Python 虚拟环境
python -m venv .venv
.venv\Scripts\activate
pip install -r tools/virtual-plc/modbus/requirements.txt
python tools/virtual-plc/modbus/virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

```bash
# 方式 B：Docker
docker compose -f tools/virtual-plc/modbus/docker-compose.yml up --build
```

如果 PyPI 访问失败，在 README 中提示用户配置代理或临时使用镜像源，例如：

```bash
pip install -r tools/virtual-plc/modbus/requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
```

注意：如果用户手里有之前的 `virtual-plc-modbus-demo.zip`，也可以手动解压到本目录；但 Codex 不应依赖 ChatGPT 沙盒下载链接，因为该链接通常不能被 Codex 环境直接访问。

---

## 3. TODO 1：新增虚拟 PLC 目录

在仓库根目录新增：

```text
tools/
  virtual-plc/
    modbus/
      virtual_plc_modbus.py
      test_client.py
      requirements.txt
      Dockerfile
      docker-compose.yml
      README.md
```

不要修改现有 ClearVision 生产通讯代码。

---

## 4. TODO 2：新增 `requirements.txt`

文件路径：

```text
tools/virtual-plc/modbus/requirements.txt
```

内容：

```text
pymodbus>=3.6,<4
```

---

## 5. TODO 3：实现 Modbus TCP 虚拟 PLC 服务端

文件路径：

```text
tools/virtual-plc/modbus/virtual_plc_modbus.py
```

### 5.1 服务端启动要求

实现一个 Modbus TCP server，默认监听：

```text
host: 0.0.0.0
port: 1502
unit/slave id: 1
```

支持命令行参数：

```bash
python virtual_plc_modbus.py \
  --host 0.0.0.0 \
  --port 1502 \
  --unit-id 1 \
  --cycle-ms 100 \
  --process-delay-ms 500 \
  --error-on-command 0
```

参数含义：

```text
--host                监听地址，默认 0.0.0.0
--port                监听端口，默认 1502
--unit-id             Modbus slave id，默认 1
--cycle-ms            虚拟 PLC 循环周期，默认 100ms
--process-delay-ms    模拟 PLC 处理耗时，默认 500ms
--error-on-command    为 1 时，收到启动命令后进入错误状态
```

### 5.2 必须支持的 Modbus 功能

ClearVision 当前 `ModbusCommunicationOperator` 已支持这些功能，所以虚拟 PLC 至少要支持：

```text
Read Coils
Read Holding Registers
Write Single Register
Write Multiple Registers
```

当前 ClearVision 算子没有 `WriteSingleCoil`，所以本次握手不要依赖写 Coil。握手用 Holding Register 完成。

### 5.3 点位表

初始化以下点位：

```text
Coil 0   APP_HEARTBEAT        可读，预留
Coil 1   PLC_HEARTBEAT        虚拟 PLC 自动翻转
Coil 2   PLC_READY            常置 true
Coil 10  START_REQ_VIEW       可读，镜像 HR0 == 1
Coil 11  START_ACK_VIEW       可读，镜像 HR1 == 100
Coil 12  DONE_VIEW            可读，镜像 HR1 == 200
Coil 13  ERROR_VIEW           可读，镜像 HR1 == 500

HR 0     COMMAND              ClearVision 写入；0=idle，1=start，9=reset
HR 1     STATUS               虚拟 PLC 写入；0=idle，100=ack/running，200=done，500=error
HR 2     SEQUENCE             ClearVision 写入流水号
HR 3     SEQUENCE_ECHO        虚拟 PLC 回写流水号
HR 4     ERROR_CODE           错误码；0=无错误
HR 10    TEST_VALUE           普通读写测试寄存器，默认 1234
```

### 5.4 握手逻辑

服务端循环检测 `HR0`。

正常流程：

```text
1. ClearVision 写 HR2 = sequence，例如 123
2. ClearVision 写 HR0 = 1
3. 虚拟 PLC 检测 HR0 == 1
4. 虚拟 PLC 写 HR1 = 100
5. 虚拟 PLC 等待 300~500ms
6. 虚拟 PLC 写 HR3 = HR2
7. 虚拟 PLC 写 HR1 = 200
8. ClearVision 读取 HR1，看到 200 后认为完成
9. ClearVision 写 HR0 = 9 或 0
10. 虚拟 PLC 清理 HR1、HR4，并回到 idle
```

错误流程：

当启动参数为：

```bash
--error-on-command 1
```

收到 `HR0 = 1` 后不要完成，改为：

```text
HR1 = 500
HR4 = 1001
Coil 13 = true
```

用于测试 ClearVision 的错误处理。

### 5.5 日志要求

服务端启动后打印：

```text
Virtual Modbus PLC listening on 0.0.0.0:1502, unit id 1
```

握手开始、完成、错误、复位时打印简短日志，方便人工观察。

---

## 6. TODO 4：实现 Python smoke test 客户端

文件路径：

```text
tools/virtual-plc/modbus/test_client.py
```

### 6.1 测试客户端参数

支持命令行参数：

```bash
python test_client.py --host 127.0.0.1 --port 1502 --unit-id 1 --timeout 5
```

默认值：

```text
host: 127.0.0.1
port: 1502
unit-id: 1
timeout: 5 seconds
```

### 6.2 测试内容

测试客户端需要完成：

1. 连接 `127.0.0.1:1502`；
2. 读取 `Coil 2`，应为 `true`；
3. 读取 `HR10`，应为 `1234`；
4. 写入 `HR10 = 5678`；
5. 再读取 `HR10`，确认变为 `5678`；
6. 执行握手：

   ```text
   写 HR2 = 123
   写 HR0 = 1
   轮询 HR1，直到 200 或超时
   读取 HR3，确认等于 123
   写 HR0 = 9
   确认 HR1 回到 0
   ```

成功时打印：

```text
Virtual Modbus PLC smoke test passed.
```

失败时退出码非 0，并打印明确错误原因。

---

## 7. TODO 5：实现 Docker 支持

新增：

```text
tools/virtual-plc/modbus/Dockerfile
tools/virtual-plc/modbus/docker-compose.yml
```

### 7.1 Dockerfile

内容建议：

```dockerfile
FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY virtual_plc_modbus.py .

EXPOSE 1502

CMD ["python", "virtual_plc_modbus.py", "--host", "0.0.0.0", "--port", "1502"]
```

### 7.2 docker-compose.yml

内容建议：

```yaml
services:
  virtual-modbus-plc:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "1502:1502"
    command:
      - python
      - virtual_plc_modbus.py
      - --host
      - 0.0.0.0
      - --port
      - "1502"
```

启动命令：

```bash
cd tools/virtual-plc/modbus
docker compose up --build
```

---

## 8. TODO 6：实现 README

文件路径：

```text
tools/virtual-plc/modbus/README.md
```

README 至少包含以下内容。

### 8.1 用途

说明这是 ClearVision 本地开发用 Modbus TCP 虚拟 PLC，用于测试 `ModbusCommunicationOperator`，不是实体 PLC 的完整替代品。

### 8.2 本地 Python 启动方式

Windows：

```powershell
cd tools/virtual-plc/modbus
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

Linux/macOS：

```bash
cd tools/virtual-plc/modbus
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

### 8.3 Docker 启动方式

```bash
cd tools/virtual-plc/modbus
docker compose up --build
```

### 8.4 代理说明

README 中明确写入：

```text
pip install pymodbus 需要访问 PyPI。
docker compose build 可能需要访问 Docker Hub 和 PyPI。
如网络受限，请配置 HTTP_PROXY / HTTPS_PROXY，或使用 PIP_INDEX_URL。
```

可提供示例：

```bash
pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
```

### 8.5 ClearVision Modbus 算子测试参数

用于 ClearVision `ModbusCommunicationOperator` 的基础参数：

```text
Protocol: TCP
IpAddress: 127.0.0.1
Port: 1502
SlaveId: 1
TimeoutMs: 5000
```

读 Holding Register：

```text
FunctionCode: ReadHolding
RegisterAddress: 10
RegisterCount: 1
期望 Response: 1234
```

写 Single Register：

```text
FunctionCode: WriteSingle
RegisterAddress: 10
WriteValue: 5678
期望 Response: Write succeeded: 5678
```

握手：

```text
Step 1: WriteSingle HR2 = 123
Step 2: WriteSingle HR0 = 1
Step 3: ReadHolding HR1，轮询直到 200
Step 4: ReadHolding HR3，期望 123
Step 5: WriteSingle HR0 = 9
```

### 8.6 当前限制

README 中说明：

```text
ClearVision 当前 ModbusCommunicationOperator 支持 ReadCoils / ReadHolding / WriteSingle Register / WriteMultiple Registers。
当前没有 WriteSingleCoil，所以虚拟 PLC 的主握手使用 Holding Register，不要求写 Coil。
/api/plc/test-connection 当前只支持 S7 / MC / FINS，不用于本 Modbus 虚拟 PLC 测试。
```

---

## 9. TODO 7：新增便捷启动脚本

新增：

```text
scripts/start-virtual-modbus-plc.ps1
scripts/test-virtual-modbus-plc.ps1
```

### 9.1 `scripts/start-virtual-modbus-plc.ps1`

内容建议：

```powershell
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workdir = Join-Path $root "tools\virtual-plc\modbus"

Set-Location $workdir

if (-not (Test-Path ".venv")) {
    python -m venv .venv
}

& ".\.venv\Scripts\python.exe" -m pip install -r requirements.txt
& ".\.venv\Scripts\python.exe" virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

### 9.2 `scripts/test-virtual-modbus-plc.ps1`

内容建议：

```powershell
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workdir = Join-Path $root "tools\virtual-plc\modbus"

Set-Location $workdir

& ".\.venv\Scripts\python.exe" test_client.py --host 127.0.0.1 --port 1502
```

---

## 10. TODO 8：用 ClearVision 的 Modbus 算子做人工验证

先启动虚拟 PLC：

```bash
python tools/virtual-plc/modbus/virtual_plc_modbus.py --host 0.0.0.0 --port 1502
```

然后在 ClearVision 中配置 `Modbus Communication` 算子。

### 10.1 测试 A：读寄存器

参数：

```text
Protocol = TCP
IpAddress = 127.0.0.1
Port = 1502
SlaveId = 1
FunctionCode = ReadHolding
RegisterAddress = 10
RegisterCount = 1
TimeoutMs = 5000
```

期望：

```text
Status = true
Response 包含 1234
```

### 10.2 测试 B：写寄存器

参数：

```text
FunctionCode = WriteSingle
RegisterAddress = 10
WriteValue = 5678
```

期望：

```text
Status = true
Response = Write succeeded: 5678
```

然后再次读 HR10，期望：

```text
Response 包含 5678
```

### 10.3 测试 C：寄存器握手

依次执行：

```text
WriteSingle HR2 = 123
WriteSingle HR0 = 1
ReadHolding HR1，轮询直到 200
ReadHolding HR3，期望 123
WriteSingle HR0 = 9
ReadHolding HR1，期望 0
```

---

## 11. TODO 9：可选增强，新增 ClearVision 自动化集成测试

当前项目已有：

```text
Acme.Product/tests/Acme.Product.Tests/Operators/ModbusCommunicationOperatorTests.cs
```

但目前主要覆盖参数校验、RTU 不支持、TCP 连接失败后的连接池清理。可以可选新增：

```text
Acme.Product/tests/Acme.Product.Tests/Operators/ModbusCommunicationOperatorVirtualPlcTests.cs
```

测试策略：

1. 这些测试默认跳过，除非环境变量开启：

   ```text
   CLEARVISION_RUN_VIRTUAL_PLC_TESTS=1
   CLEARVISION_VIRTUAL_MODBUS_HOST=127.0.0.1
   CLEARVISION_VIRTUAL_MODBUS_PORT=1502
   ```

2. 测试用 ClearVision 的 `ModbusCommunicationOperator` 真实连接虚拟 PLC。

3. 覆盖：

   ```text
   ReadHolding HR10
   WriteSingle HR10
   WriteMultiple HR20~HR22
   握手流程 HR2 / HR0 / HR1 / HR3
   ```

4. 不要让测试自动启动 Python server，避免 .NET 测试强依赖 Python 环境。先由用户或 CI 手动启动虚拟 PLC。

验收命令：

```bash
# 终端 1：启动虚拟 PLC
python tools/virtual-plc/modbus/virtual_plc_modbus.py --host 0.0.0.0 --port 1502

# 终端 2：运行 .NET 集成测试
set CLEARVISION_RUN_VIRTUAL_PLC_TESTS=1
set CLEARVISION_VIRTUAL_MODBUS_HOST=127.0.0.1
set CLEARVISION_VIRTUAL_MODBUS_PORT=1502
dotnet test Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj --filter ModbusCommunicationOperatorVirtualPlcTests
```

PowerShell：

```powershell
$env:CLEARVISION_RUN_VIRTUAL_PLC_TESTS="1"
$env:CLEARVISION_VIRTUAL_MODBUS_HOST="127.0.0.1"
$env:CLEARVISION_VIRTUAL_MODBUS_PORT="1502"
dotnet test Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj --filter ModbusCommunicationOperatorVirtualPlcTests
```

---

## 12. TODO 10：不要误改这些文件

本任务先不要修改：

```text
Acme.Product/src/Acme.PlcComm/PlcClientFactory.cs
Acme.Product/src/Acme.Product.Desktop/Endpoints/PlcEndpoints.cs
Acme.Product/src/Acme.Product.Core/Entities/AppConfig.cs
```

原因：

1. 当前这些文件属于 `S7 / MC / FINS` 全局 PLC 配置体系；
2. 本任务目标是让 Modbus TCP 虚拟 PLC 服务于已有 `ModbusCommunicationOperator`；
3. 把 Modbus 纳入全局 PLC 设置、`/api/plc/test-connection` 和地址映射校验，是另一个更大的功能任务。

---

## 13. 验收标准

完成后应满足：

```text
[ ] tools/virtual-plc/modbus/ 下有完整虚拟 PLC 示例包。
[ ] 本地 Python 能启动虚拟 PLC，监听 1502。
[ ] test_client.py 能通过 smoke test。
[ ] Docker compose 能启动虚拟 PLC。
[ ] README 说明了 ClearVision ModbusCommunicationOperator 的测试参数。
[ ] README 说明了 PyPI / Docker Hub 可能需要代理。
[ ] ClearVision 的 ModbusCommunicationOperator 能连接 127.0.0.1:1502。
[ ] ReadHolding HR10 初始读到 1234。
[ ] WriteSingle HR10 = 5678 后再次读取能读到 5678。
[ ] HR0 / HR1 / HR2 / HR3 寄存器握手能完成。
[ ] 没有把 Modbus 强行接入当前 S7/MC/FINS 的 /api/plc/test-connection。
```

---

## 14. 后续增强任务，不属于本次必做

后续可以单独开任务：

```text
1. 给 ModbusCommunicationOperator 增加 WriteSingleCoil / WriteMultipleCoils。
2. 给 /api/plc/test-connection 增加 Modbus TCP 连接测试。
3. 给 CommunicationConfig 增加 Modbus 全局协议配置。
4. 给设置页增加 Modbus 参数 UI。
5. 把虚拟 PLC 集成到 CI，可选用 docker compose 启动服务后跑 dotnet integration tests。
```

本次优先把 **虚拟 PLC 示例包 + ClearVision Modbus 算子读写/握手验证** 做通。
