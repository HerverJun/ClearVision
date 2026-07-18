# ADR F04-G3A：Project 生命周期合同

## 状态

```text
STATUS=ACCEPTED
APPROVED_BY=PRODUCT_OWNER
APPROVED_AT=2026-07-18
APPROVAL_SOURCE=F04_PROMPT_3_OF_4
IMPLEMENTATION_PHASE=G3B
```

## 决策

F04 Project 生命周期采用以下单一路线，完整 schema、兼容与测试输入见 [F04_G3A_Project生命周期合同决策](./F04_G3A_Project生命周期合同决策.md)：

1. create 使用 user-scoped `clientOperationId`、durable operation journal 与只读 reconcile；同 identity 不重复创建。
2. F04 只支持 blank Project create；initial Flow、assets、template 与 import 不进入新合同。
3. delete 使用新 command、expected persistence revision、operation identity、durable tombstone 与 retryable cleanup；response loss 通过 operation query reconcile。
4. Project not-found、revision、payload mismatch、active mutation 与 validation 使用稳定 HTTP + code。
5. 最近工程由显式 open command 写入 `LastOpenedAt`；不改内容 revision、Flow 或 `ModifiedAt`。
6. template create 延期且不阻塞 F04/default entry。

## 权威边界

- `ProjectService`、`ProjectSaveCoordinator` 与既有 Project repository 保持业务权威。
- operation journal 是 lifecycle command outcome authority，不是第二 Project repository。
- G3B 才允许 migration、endpoint 与 coordinator 实现；G3A 不修改运行代码。
- Legacy create/delete 通过同一 Application Service/coordinator 兼容，不复制第二实现。

## 后果

- G3B 必须实现 crash recovery、retention、cross-user non-disclosure、并发与 response-loss 测试。
- G3C 只能实现一个 `projectLifecycleCommandOwner`，并复用批准 endpoint。
- 未按 ADR 完成 G3B 前，G3C 与 Project create/delete UI 不得进入。
