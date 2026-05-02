# Runtime Performance Smoke

This pass establishes the software smoke baseline, not a hardware-certified field benchmark.

## Verified in automation

- solution build success
- Station build success
- RuntimeHost single-run path success
- RuntimeHost folder replay stop/idempotence success

## Not claimed here

- cold-start timing on low-end industrial PCs
- real camera acquisition latency
- PLC writeback latency
- long-run memory trend on production hardware

Those need on-device capture during field rollout. The MVP software path is in place and test-covered; hardware numbers should be recorded against the actual station SKU.
