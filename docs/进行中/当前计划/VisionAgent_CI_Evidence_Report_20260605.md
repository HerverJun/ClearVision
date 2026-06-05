# Vision Agent CI Evidence Report 20260605

本报告记录 Vision Agent Quality Suite 的 CI artifact 证据闭环。2026-06-05T16:18:46Z 触发的 push run 已成功完成并上传 artifact。

## CI Run

| 字段 | 值 |
| --- | --- |
| commitSha | c48d2cf9caac88198ddf0e8e40b86223a45fe959 |
| branchName | codex初稿 |
| runId | 27026501955 |
| runAttempt | 1 |
| artifact name | vision-agent-quality-suite |
| artifact id | 7441437925 |
| artifact size | 103589 bytes |
| artifact expired | false |
| workflow | `.github/workflows/vision-agent-quality.yml` |
| trigger | push on `codex*` branch |
| run URL | `https://github.com/HerverJun/ClearVision/actions/runs/27026501955` |
| job URL | `https://github.com/HerverJun/ClearVision/actions/runs/27026501955/job/79767769568` |

## Artifact Manifest

CI artifact `vision-agent-quality-suite` 必须包含：

- `VisionAgent_business_benchmark_baseline.json`
- `VisionAgent_business_benchmark_baseline.md`
- `planner_autonomy_benchmark.json`
- `planner_autonomy_benchmark.md`
- `real_llm_planner_shadow_eval.json`
- `real_llm_planner_shadow_eval.md`
- `vision_agent_quality_artifact_manifest.json`
- `vision_agent_quality_artifact_manifest.md`
- `agent_engineering_harness.trx`
- `agent_ui_contract_output.txt`

GitHub artifact API returned `vision-agent-quality-suite` with id `7441437925`, size `103589` bytes, created at `2026-06-05T16:20:58Z`.

上传 artifact 前必须执行：

```powershell
python quality/tools/assert_vision_agent_report_artifacts.py `
  --require-non-local-workflow-run `
  --write-manifest quality/evals/reports/vision_agent_quality_artifact_manifest.json `
  --write-report quality/evals/reports/vision_agent_quality_artifact_manifest.md
```

## Workflow Run Metadata Gate

CI artifact 内三个 JSON 报告必须满足：

- `workflowRun.commitSha != local`
- `workflowRun.branchName != local`
- `workflowRun.runId != local`
- `workflowRun.runAttempt != local`

本地运行允许 `local`，但 CI artifact assertion 不允许 `local` 并会在上传前失败。

本次 CI job step 结果：

- `Run Vision Agent Quality Suite`：success。
- `Generate Real LLM Shadow Eval Sample`：success。
- `Assert Vision Agent Artifact Reports`：success，workflow 使用 `--require-non-local-workflow-run`。
- `Upload Vision Agent Quality Reports`：success。

## Benchmark Summary

- Executable business benchmark：36 cases，accepted=true。
- 生成成功率：100.00%。
- 结构校验通过率：97.22%。
- dryrun 通过率：94.44%。
- previewReady 比例：83.33%。
- 参数补录完成率：80.56%。
- 用户可应用率：97.22%。

## Planner Autonomy Summary

- Mock planner autonomy cases：15，全部通过。
- Permission negative cases：6，全部通过。
- 覆盖 RuntimePreview consent=false、RuntimePreview permission missing、DeploymentPrepare permission missing、ConfigWrite deny、非白名单工具 deny、DeploymentPrepare 非 precheck deny。
- Mock planner autonomy benchmark 继续作为稳定门禁，不被 Real LLM shadow eval 替代。

## Shadow Eval Status

- 默认状态：关闭。
- 默认 CI 行为：生成 skipped/sample artifact。
- 手动 dry run 文档：`docs/进行中/当前计划/VisionAgent_Real_LLM_Shadow_Dry_Run.md`。
- 安全约束：只做 planner output parse、policy check、tool plan match score；不执行 RuntimePreview、DeploymentPrepare、workflow、打包、部署或配置写入。

## Dogfood Result Summary

- Human dogfood result：`docs/进行中/当前计划/VisionAgent_Dogfood_Result_20260605_Human.md`。
- 截图证据：`.codex_tmp/dogfood/DF-01.png` 至 `.codex_tmp/dogfood/DF-14.png`，另含 `.codex_tmp/dogfood/DF-13-undo.png`。
- 关键覆盖：左右布局、Camera/File 参数互斥、中文算子类型显示、RuntimePreview 未授权 pendingAction、RuntimePreview 授权 offline metadata、workflow draft 应用到画布、撤销应用。
- 结果：无阻断问题。

## 未推进声明

- 未接真实相机 SDK。
- 未访问真实 Station。
- 未读取真实图片文件。
- 未加载真实模型文件。
- 未写 PLC。
- 未打包、未下发、未热加载。
- RuntimePreview 继续保持 offline/metadata-only。
- legacy GenerateFlow 默认仍不启用 Agent。
- developer hidden UI 默认仍关闭。
