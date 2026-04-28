---
title: "Dataview 工作台"
doc_type: "index"
status: "active"
topic: "文档索引"
created: "2026-03-21"
updated: "2026-04-28"
---

# Dataview 工作台

- [进行中](./进行中/README.md)
- [待复核索引](./进行中/待复核/索引.md)
- [已关闭事项索引](./归档/已关闭事项/索引.md)

## 全局总览

```dataview
TABLE WITHOUT ID
  file.link AS 文档,
  status AS 状态,
  topic AS 主题,
  doc_type AS 类型,
  updated AS 更新
FROM "docs"
WHERE contains(list("active", "needs-review", "closed"), status) AND doc_type != "index"
SORT status ASC, topic ASC, updated DESC
```
