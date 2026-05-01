---
title: "ClearVision AI 密钥本地配置说明"
doc_type: "guide"
status: "active"
topic: "secret-management"
created: "2026-04-29"
updated: "2026-04-29"
---

# ClearVision AI 密钥本地配置说明

`Acme.Product/src/Acme.Product.Desktop/appsettings.json` 只保留非敏感默认值，不再提交真实 `AiFlowGeneration.ApiKey`。

本地开发和 CI/发布环境需要调用 AI 服务时，使用环境变量覆盖配置：

```powershell
$env:AiFlowGeneration__ApiKey = "<REDACTED>"
$env:AiFlowGeneration__Provider = "OpenAI"
$env:AiFlowGeneration__Model = "deepseek-chat"
$env:AiFlowGeneration__BaseUrl = "https://api.deepseek.com/chat/completions"
```

注意事项：

- 真实 key 只允许存在于个人用户环境变量、受保护的 secret store、CI secrets 或发布环境的安全配置中。
- 不要把真实 key 写入 `appsettings.json`、`ai_models.json`、测试结果、日志、截图或审计文档。
- 泄漏过的 key 应视为失效凭据，由服务商控制台人工吊销或轮换。
- 提交前可运行 `& "./scripts/scan-secrets.ps1"` 做当前工作区扫描。
