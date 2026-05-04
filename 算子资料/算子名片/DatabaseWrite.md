# 数据库写入 / DatabaseWrite

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DatabaseWriteOperator` |
| 枚举值 (Enum) | `OperatorType.DatabaseWrite` |
| 分类 (Category) | 数据 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `database` |
| 关键词 (Keywords) | 数据库, 写入, 存储, SQL, SQLite, SQLServer, MySQL, Upsert |

## 算法原理 / Algorithm Principle
> **中文：** 将输入数据持久化到关系数据库表中，支持 SQLite、SQL Server、MySQL 三种后端。
>
> 核心流程：
> 1. 将输入 `Data` 序列化为 JSON 字符串
> 2. 解析或自动生成 `RecordId`（GUID）
> 3. 通过 Provider 模式创建数据库连接
> 4. 自动建表（如果不存在）：表结构为 `Id`(PK) + `Data`(TEXT) + `Timestamp`(DATETIME)
> 5. 执行 Upsert（INSERT or UPDATE）写入记录
> 6. 瞬态失败自动重试（最多 3 次，指数退避 200ms*attempt）
>
> **English:** Persists input data to a relational database table, supporting SQLite, SQL Server, and MySQL.
>
> Core flow:
> 1. Serialize `Data` input to JSON string
> 2. Parse or auto-generate `RecordId` (GUID)
> 3. Create database connection via Provider pattern
> 4. Auto-create table if not exists: `Id`(PK) + `Data`(TEXT) + `Timestamp`(DATETIME)
> 5. Execute Upsert (INSERT or UPDATE)
> 6. Auto-retry transient failures (max 3 attempts, exponential backoff 200ms*attempt)

## 实现策略 / Implementation Strategy
- 通过 `IDatabaseWriteProvider` 接口抽象三种数据库后端，每种 Provider 实现连接创建、建表、Upsert 和瞬态错误判断。
- 表名通过正则 `^[a-zA-Z_][a-zA-Z0-9_]*$` 校验，防止 SQL 注入。
- 连接字符串通过 SHA256 哈希后作为缓存键，避免明文存储。
- 建表使用 `SemaphoreSlim` + `ConcurrentDictionary` 双重锁，确保并发安全且只建一次。
- 命令超时固定 5 秒（`CommandTimeoutSeconds`），`RecordId` 最大长度 128 字符。
- `RecordId` 未提供时自动生成 `Guid.NewGuid().ToString("N")`（32 位无连字符）。

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("Data", out data)` -> 校验非空
2. `GetRawStringParameter(@operator, "DbType", "SQLite")` -> `TryGetProvider(dbType, out provider)`
3. `GetRawStringParameter(@operator, "ConnectionString", "")` -> 校验非空
4. `GetRawStringParameter(@operator, "TableName", "InspectionResults")` -> `IsValidTableName(tableName)`
5. `ResolveRecordId(inputs)` -> 自动生成或校验输入 RecordId
6. `JsonSerializer.Serialize(data, SerializerOptions)` -> 序列化为 JSON
7. `WriteToDatabaseAsync(...)` -> 循环重试：
   - `provider.CreateConnection(connectionString)` -> `connection.OpenAsync`
   - `EnsureTableExistsAsync(...)` -> 建表（带缓存 + 双重锁）
   - `ExecuteUpsertAsync(...)` -> `provider.CreateUpsertCommand(...)` -> `ExecuteNonQueryAsync`
8. `OperatorExecutionOutput.Success(...)` -> Status, RecordId, TableName, DbType, Timestamp

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ConnectionString` | `string` | `""` | 非空 | 目标数据库连接字符串。 |
| `TableName` | `string` | `"InspectionResults"` | 符合正则 `^[a-zA-Z_][a-zA-Z0-9_]*$` | 目标表名。仅允许字母、数字、下划线，且必须以字母或下划线开头。 |
| `DbType` | `enum` | `"SQLite"` | `SQLite` / `SQLServer` / `MySQL` | 数据库类型。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Data` | 数据 | `Any` | Yes | 待写入的业务载荷（内部序列化为 JSON）。 |
| `RecordId` | 记录ID | `String` | No | 可选幂等键；相同 Id 会被更新而非重复插入。未提供时自动生成 GUID。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Status` | 状态 | `Boolean` | 写入是否成功。 |
| `RecordId` | 记录ID | `String` | 实际落库使用的记录标识。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `TableName` | `String` | 实际写入的表名。 |
| `DbType` | `String` | 实际使用的数据库类型。 |
| `Timestamp` | `DateTime` | 写入时间戳（UTC）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) 单次写入；网络延迟为主要因素 |
| 典型耗时 (Typical Latency) | 5-50ms（SQLite 本地）；50-500ms（远程 SQL Server/MySQL） |
| 内存特征 (Memory Profile) | O(n) JSON 序列化缓冲（n 为 Data 大小） |

## 适用场景 / Use Cases
- 适合 (Suitable)：检测结果的持久化存储
- 适合 (Suitable)：需要幂等写入的场景（通过 RecordId 实现 Upsert）
- 适合 (Suitable)：多数据库后端统一写入接口
- 不适合 (Not Suitable)：需要复杂列映射或自定义表结构的场景（固定 Id/Data/Timestamp 三列）
- 不适合 (Not Suitable)：大批量批量写入（每次执行独立连接，无批量优化）
- 不适合 (Not Suitable)：需要事务管理或多表关联写入的场景

## 已知限制 / Known Limitations
1. 统一表结构为 `Id / Data / Timestamp` 三列，更复杂的列映射不在本算子内展开。
2. 每次写入独立创建连接，无连接池复用。
3. 命令超时固定 5 秒，不可配置。
4. `RecordId` 最大长度 128 字符，超长直接失败。
5. 建表缓存基于进程生命周期，重启后首次写入会重新建表检查。
6. 多库真实回归依赖 Docker/Testcontainers 环境。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 Provider 模式、建表缓存、重试机制、运行时附加输出 |
| 1.0.0 | 2026-04-12 | 收口为真实三库实现，新增 `RecordId` 输入与幂等 upsert/merge 语义 |
