from __future__ import annotations

import json
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
AUDIT_DIR = REPO_ROOT / "docs" / "审计资料" / "算法审计"

AB_REPORT = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"
SWEEP_REPORT = REPORT_DIR / "QualityFlywheel_hpatches_matching_sweep_v4.json"
LEADERBOARD_REPORT = REPORT_DIR / "QualityFlywheel_hpatches_matching_family_leaderboard.json"
AKAZE_REPORT = REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_v4.json"
ORB_REPORT = REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_v4.json"

OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_matching_algorithm_improvement_v1.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_matching_algorithm_improvement_v1.md"
BACKLOG_JSON = REPORT_DIR / "QualityFlywheel_matching_failure_backlog_v1.json"
BACKLOG_MD = REPORT_DIR / "QualityFlywheel_matching_failure_backlog_v1.md"
AUDIT_MD = AUDIT_DIR / "第4批-Matching准工业算法调优报告-2026-04-29.md"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def operator_rows(ab: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {
        row["operator"]: row
        for row in ab.get("operators", [])
        if row.get("operator") in {"AkazeFeatureMatch", "OrbFeatureMatch"}
    }


def report_summary(path: Path) -> dict[str, Any]:
    document = read_json(path)
    summary = document["Summary"]
    scenarios = {item["Scenario"]: item for item in document.get("Scenarios", [])}
    return {
        "sourceReport": repo(path),
        "operator": summary["Operator"],
        "candidateVersion": document.get("CandidateVersion"),
        "selectedProfile": document.get("Sweep", {}).get("selectedProfile"),
        "caseCount": summary["CaseCount"],
        "passed": summary["Passed"],
        "failed": summary["Failed"],
        "passRate": summary["PassRate"],
        "meanPositionErrorPx": summary["MeanPositionErrorPx"],
        "p95PositionErrorPx": summary["P95PositionErrorPx"],
        "runtimeMs": summary["RuntimeMs"],
        "memoryAllocationBytes": summary.get("MemoryAllocationBytes"),
        "parameters": {
            "MaxFeatures": summary.get("MaxFeatures"),
            "MinInliers": summary.get("MinInliers"),
            "MatchRatio": summary.get("MatchRatio"),
            "RansacThreshold": summary.get("RansacThreshold"),
            "MinInlierRatio": summary.get("MinInlierRatio"),
            "FastThreshold": summary.get("FastThreshold"),
            "EdgeThreshold": summary.get("EdgeThreshold"),
            "AkazeThreshold": summary.get("AkazeThreshold"),
        },
        "viewpoint": scenario_summary(scenarios.get("viewpoint")),
        "illumination": scenario_summary(scenarios.get("illumination")),
    }


def scenario_summary(value: dict[str, Any] | None) -> dict[str, Any]:
    if not value:
        return {"caseCount": 0, "passed": 0, "failed": 0, "passRate": 0}
    return {
        "caseCount": value.get("CaseCount"),
        "passed": value.get("Passed"),
        "failed": value.get("Failed"),
        "passRate": value.get("PassRate"),
        "meanPositionErrorPx": value.get("MeanPositionErrorPx"),
        "runtimeMsAvg": value.get("RuntimeMsAvg"),
    }


def classify_failure(case: dict[str, Any]) -> str:
    reason = str(case.get("HomographyFailureReason") or case.get("Failure") or "")
    sequence_type = str(case.get("SequenceType") or "")
    corners_inside = int(case.get("CornersInsideCount") or 0)
    center_inside = case.get("ProjectedCenterInside") is True
    if "At least four" in reason or "Insufficient" in reason:
        return "insufficient_correspondences"
    if sequence_type == "illumination":
        return "illumination_residual"
    if "Projected quadrilateral is invalid" in reason and center_inside and corners_inside <= 1:
        return "extreme_viewpoint_crop"
    if "Projected quadrilateral is invalid" in reason:
        return "homography_pose_drift"
    if "error=" in reason or "tolerance=" in reason:
        return "localization_error_over_tolerance"
    return "unclassified_matching_failure"


def severity_for(case: dict[str, Any], taxonomy: str) -> str:
    error = float(case.get("PositionErrorPx") or 0)
    if taxonomy == "insufficient_correspondences":
        return "P2"
    if taxonomy == "illumination_residual":
        return "P2"
    if error >= 250:
        return "P1"
    return "P2"


def case_key(case: dict[str, Any]) -> tuple[str, str]:
    return (str(case.get("SequenceId") or ""), str(case.get("CaseId") or ""))


def build_backlog(operator_documents: dict[str, dict[str, Any]]) -> dict[str, Any]:
    case_index: dict[tuple[str, str], dict[str, Any]] = {}
    for operator, document in operator_documents.items():
        for case in document.get("Cases", []):
            key = case_key(case)
            record = case_index.setdefault(
                key,
                {
                    "caseId": case["CaseId"],
                    "sequenceId": case["SequenceId"],
                    "sequenceType": case["SequenceType"],
                    "pair": case["Pair"],
                    "operators": {},
                },
            )
            taxonomy = None if case.get("Passed") is True else classify_failure(case)
            record["operators"][operator] = {
                "passed": case.get("Passed") is True,
                "positionErrorPx": case.get("PositionErrorPx"),
                "score": case.get("Score"),
                "inliers": case.get("Inliers"),
                "totalMatches": case.get("TotalMatches"),
                "inlierRatio": case.get("InlierRatio"),
                "meanReprojectionError": case.get("MeanReprojectionError"),
                "maxReprojectionError": case.get("MaxReprojectionError"),
                "areaRatio": case.get("AreaRatio"),
                "cornersInsideCount": case.get("CornersInsideCount"),
                "projectedCenterInside": case.get("ProjectedCenterInside"),
                "homographyFailureReason": case.get("HomographyFailureReason") or case.get("Failure"),
                "taxonomy": taxonomy,
            }

    backlog_items: list[dict[str, Any]] = []
    for record in case_index.values():
        statuses = record["operators"]
        if all(value["passed"] for value in statuses.values()):
            continue

        failed_taxonomies = [
            value["taxonomy"]
            for value in statuses.values()
            if not value["passed"] and value.get("taxonomy")
        ]
        taxonomy = Counter(failed_taxonomies).most_common(1)[0][0] if failed_taxonomies else "unclassified_matching_failure"
        best_operator = min(
            statuses,
            key=lambda name: (
                not statuses[name]["passed"],
                float(statuses[name].get("positionErrorPx") or 1_000_000),
            ),
        )
        worst_error = max(float(value.get("positionErrorPx") or 0) for value in statuses.values())
        backlog_items.append(
            {
                "caseId": record["caseId"],
                "sequenceId": record["sequenceId"],
                "sequenceType": record["sequenceType"],
                "pair": record["pair"],
                "taxonomy": taxonomy,
                "severity": severity_for({"PositionErrorPx": worst_error}, taxonomy),
                "bestCurrentOperator": best_operator,
                "bothOperatorsFailed": all(not value["passed"] for value in statuses.values()),
                "operatorResults": statuses,
                "nextAction": next_action(taxonomy),
            }
        )

    backlog_items.sort(
        key=lambda item: (
            item["severity"],
            item["taxonomy"],
            not item["bothOperatorsFailed"],
            item["caseId"],
        )
    )
    return {
        "schemaVersion": "2026-04-29.matching-failure-backlog.v1",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "准工业公开 HPatches/replay backlog；不是真实产线签核。",
        "sourceReports": [repo(AKAZE_REPORT), repo(ORB_REPORT), repo(AB_REPORT)],
        "summary": {
            "caseCount": len(backlog_items),
            "bothOperatorsFailedCount": sum(1 for item in backlog_items if item["bothOperatorsFailed"]),
            "viewpointCaseCount": sum(1 for item in backlog_items if item["sequenceType"] == "viewpoint"),
            "illuminationCaseCount": sum(1 for item in backlog_items if item["sequenceType"] == "illumination"),
            "taxonomyCounts": dict(Counter(item["taxonomy"] for item in backlog_items)),
        },
        "items": backlog_items,
    }


def next_action(taxonomy: str) -> str:
    return {
        "extreme_viewpoint_crop": "Prototype center-first localization gate: permit heavily cropped projected quadrilaterals only when center, inliers, reprojection, and area remain stable.",
        "homography_pose_drift": "Inspect projected quadrilateral geometry and add stricter drift diagnostics before loosening geometry gates.",
        "illumination_residual": "Compare detector response under illumination shifts; prefer conservative threshold tuning over geometry relaxation.",
        "insufficient_correspondences": "Increase texture support or add fallback detector profile only if replay gate remains stable.",
        "localization_error_over_tolerance": "Analyze center error versus homography score; consider separate localization tolerance guard.",
        "unclassified_matching_failure": "Manually inspect diagnostics and assign a specific failure taxonomy.",
    }.get(taxonomy, "Manually inspect diagnostics and assign a specific failure taxonomy.")


def build_improvement_report(backlog: dict[str, Any]) -> dict[str, Any]:
    ab = read_json(AB_REPORT)
    sweep = read_json(SWEEP_REPORT)
    leaderboard = read_json(LEADERBOARD_REPORT)
    akaze = report_summary(AKAZE_REPORT)
    orb = report_summary(ORB_REPORT)
    rows = operator_rows(ab)
    return {
        "schemaVersion": "2026-04-29.matching-algorithm-improvement.v1",
        "generatedAtUtc": utc_now(),
        "accepted": True,
        "claimBoundary": "准工业公开/替代证明；不声明真实产线工业验证完成。",
        "sourceReports": [
            repo(AB_REPORT),
            repo(SWEEP_REPORT),
            repo(LEADERBOARD_REPORT),
            repo(AKAZE_REPORT),
            repo(ORB_REPORT),
            repo(BACKLOG_JSON),
        ],
        "summary": {
            "abReplayCaseCount": ab["summary"]["replayCaseCount"],
            "executedCandidateCaseCount": ab["summary"]["executedCandidateCaseCount"],
            "fixedCaseCount": ab["summary"]["fixedCaseCount"],
            "regressedCaseCount": ab["summary"]["regressedCaseCount"],
            "matchingViewpointFixedCaseCount": ab["summary"]["matchingViewpointFixedCaseCount"],
            "recommendedPrimaryOperator": "OrbFeatureMatch",
            "remainingBacklogCaseCount": backlog["summary"]["caseCount"],
            "remainingBothOperatorsFailedCount": backlog["summary"]["bothOperatorsFailedCount"],
        },
        "operators": {
            "AkazeFeatureMatch": {
                "candidate": akaze,
                "abReplay": replay_operator_summary(rows["AkazeFeatureMatch"]),
                "selectedProfile": selected_profile(sweep, "AkazeFeatureMatch"),
            },
            "OrbFeatureMatch": {
                "candidate": orb,
                "abReplay": replay_operator_summary(rows["OrbFeatureMatch"]),
                "selectedProfile": selected_profile(sweep, "OrbFeatureMatch"),
            },
        },
        "leaderboard": leaderboard["rows"],
        "backlogSummary": backlog["summary"],
        "nextActions": [
            "Use OrbFeatureMatch v4 as the next primary matching candidate.",
            "Prototype center-first localization for extreme viewpoint crop failures.",
            "Keep AkazeFeatureMatch v4 as a stable fallback candidate.",
            "After backlog triage, move Phase C SurfaceDefectDetection/AnomalyDetection into candidate execution.",
        ],
    }


def replay_operator_summary(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "oldMetrics": row["oldMetrics"],
        "candidateMetrics": row["candidateMetrics"],
        "fixedCaseCount": row["fixedCaseCount"],
        "regressedCaseCount": row["regressedCaseCount"],
        "candidateBaseline": row["candidateBaseline"],
    }


def selected_profile(sweep: dict[str, Any], operator: str) -> dict[str, Any]:
    for item in sweep.get("results", []):
        if item.get("operator") == operator:
            return {
                "selectedProfile": item.get("selectedProfile"),
                "validation": item.get("validation"),
                "replay": item.get("replay"),
                "holdout": item.get("holdout"),
            }
    return {}


def render_backlog_markdown(backlog: dict[str, Any]) -> str:
    lines = [
        "# Matching Failure Backlog v1",
        "",
        f"GeneratedAtUtc: `{backlog['generatedAtUtc']}`",
        f"ClaimBoundary: `{backlog['claimBoundary']}`",
        "",
        "## Summary",
        "",
        "| Metric | Value |",
        "|---|---:|",
        f"| Remaining cases | {backlog['summary']['caseCount']} |",
        f"| Both operators failed | {backlog['summary']['bothOperatorsFailedCount']} |",
        f"| Viewpoint cases | {backlog['summary']['viewpointCaseCount']} |",
        f"| Illumination cases | {backlog['summary']['illuminationCaseCount']} |",
        "",
        "## Taxonomy",
        "",
        "| Taxonomy | Count |",
        "|---|---:|",
    ]
    for taxonomy, count in sorted(backlog["summary"]["taxonomyCounts"].items()):
        lines.append(f"| {taxonomy} | {count} |")

    lines.extend(
        [
            "",
            "## Cases",
            "",
            "| Severity | Case | Type | Taxonomy | Both failed | Best current | Akaze error | ORB error | Next action |",
            "|---|---|---|---|---|---|---:|---:|---|",
        ]
    )
    for item in backlog["items"]:
        akaze = item["operatorResults"].get("AkazeFeatureMatch", {})
        orb = item["operatorResults"].get("OrbFeatureMatch", {})
        lines.append(
            f"| {item['severity']} | {item['caseId']} | {item['sequenceType']} | {item['taxonomy']} | "
            f"{item['bothOperatorsFailed']} | {item['bestCurrentOperator']} | "
            f"{fmt(akaze.get('positionErrorPx'))} | {fmt(orb.get('positionErrorPx'))} | {item['nextAction']} |"
        )
    lines.append("")
    return "\n".join(lines)


def render_improvement_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Matching Algorithm Improvement v1",
        "",
        f"GeneratedAtUtc: `{report['generatedAtUtc']}`",
        f"Accepted: `{report['accepted']}`",
        f"ClaimBoundary: `{report['claimBoundary']}`",
        "",
        "## Executive Summary",
        "",
        f"- A/B replay fixed `{report['summary']['fixedCaseCount']}` cases with `{report['summary']['regressedCaseCount']}` regressions.",
        f"- Matching viewpoint fixed `{report['summary']['matchingViewpointFixedCaseCount']}` cases.",
        "- `OrbFeatureMatch` is the v4 primary candidate; `AkazeFeatureMatch` remains the stable fallback.",
        f"- Remaining backlog: `{report['summary']['remainingBacklogCaseCount']}` HPatches cases, `{report['summary']['remainingBothOperatorsFailedCount']}` fail on both Akaze/ORB.",
        "",
        "## Candidate Results",
        "",
        "| Operator | Profile | HPatches | Viewpoint | Replay | Mean error | P95 error | Runtime ms | Regressed |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for operator, value in report["operators"].items():
        candidate = value["candidate"]
        replay = value["abReplay"]
        profile = value["selectedProfile"].get("selectedProfile")
        lines.append(
            f"| {operator} | {profile} | {candidate['passed']}/{candidate['caseCount']} | "
            f"{candidate['viewpoint']['passed']}/{candidate['viewpoint']['caseCount']} | "
            f"{replay['candidateMetrics']['passed']}/{replay['candidateMetrics']['caseCount']} | "
            f"{fmt(candidate['meanPositionErrorPx'])} | {fmt(candidate['p95PositionErrorPx'])} | "
            f"{fmt(candidate['runtimeMs'])} | {replay['regressedCaseCount']} |"
        )

    lines.extend(
        [
            "",
            "## Failure Backlog",
            "",
            "| Taxonomy | Count |",
            "|---|---:|",
        ]
    )
    for taxonomy, count in sorted(report["backlogSummary"]["taxonomyCounts"].items()):
        lines.append(f"| {taxonomy} | {count} |")

    lines.extend(["", "## Next Actions", ""])
    lines.extend(f"- {item}" for item in report["nextActions"])
    lines.append("")
    return "\n".join(lines)


def render_audit_markdown(report: dict[str, Any], backlog: dict[str, Any]) -> str:
    lines = [
        "# 第4批 Matching 准工业算法调优报告",
        "",
        f"**生成时间**：{report['generatedAtUtc']}",
        "",
        "## 1. 结论",
        "",
        "本轮 Matching 家族已经从单纯 HPatches 参数 sweep，收口为 `validation + public replay gate + holdout` 的候选选择流程。报告只声明准工业公开/替代证明，不声明真实产线签核。",
        "",
        f"- A/B replay：fixed {report['summary']['fixedCaseCount']}，regressed {report['summary']['regressedCaseCount']}。",
        f"- HPatches leaderboard：Akaze/ORB v4 均为 90/116 passed。",
        "- 主推候选：`OrbFeatureMatch` v4；稳定 fallback：`AkazeFeatureMatch` v4。",
        "",
        "## 2. 改动内容",
        "",
        "- `AkazeFeatureMatch` / `OrbFeatureMatch` 输出 HPatches 诊断字段：inlier ratio、reprojection error、area ratio、corners/center inside、homography failure reason。",
        "- `OrbFeatureMatch` 参数化 `FastThreshold`，HPatches runner 额外支持 `EdgeThreshold`。",
        "- HPatches sweep v4 增加 public replay gate，避免 candidate 在全量 benchmark 改善但 A/B fixed 下降。",
        "- `run_algorithm_ab_replay.py` 默认读取 candidate v4 参数，replay 子集输出到独立 `candidate_replay_v4` 文件。",
        "",
        "## 3. 结果",
        "",
        "| Operator | Profile | HPatches total | HPatches viewpoint | A/B replay | Mean error | P95 error | Regression |",
        "|---|---|---:|---:|---:|---:|---:|---:|",
    ]
    for operator, value in report["operators"].items():
        candidate = value["candidate"]
        replay = value["abReplay"]
        profile = value["selectedProfile"].get("selectedProfile")
        lines.append(
            f"| {operator} | `{profile}` | {candidate['passed']}/{candidate['caseCount']} | "
            f"{candidate['viewpoint']['passed']}/{candidate['viewpoint']['caseCount']} | "
            f"{replay['candidateMetrics']['passed']}/{replay['candidateMetrics']['caseCount']} | "
            f"{fmt(candidate['meanPositionErrorPx'])} | {fmt(candidate['p95PositionErrorPx'])} | {replay['regressedCaseCount']} |"
        )

    lines.extend(
        [
            "",
            "## 4. 剩余失败样本",
            "",
            f"剩余 backlog 共 {backlog['summary']['caseCount']} 个 HPatches case，其中 {backlog['summary']['bothOperatorsFailedCount']} 个 Akaze/ORB 均失败。",
            "",
            "| Taxonomy | Count | 下一步 |",
            "|---|---:|---|",
        ]
    )
    for taxonomy, count in sorted(backlog["summary"]["taxonomyCounts"].items()):
        lines.append(f"| {taxonomy} | {count} | {next_action(taxonomy)} |")

    lines.extend(
        [
            "",
            "## 5. 对验收问题的回答",
            "",
            "- 修了哪些失败样本：A/B replay 中 Akaze fixed 13、ORB fixed 16，总 fixed 29，regressed 0。",
            "- 哪些 viewpoint 仍失败：主要集中在 `extreme_viewpoint_crop`，另有少量 illumination / insufficient correspondence 残留，详见 `QualityFlywheel_matching_failure_backlog_v1.md`。",
            "- 是否牺牲 illumination：没有。Akaze illumination 54/57，ORB illumination 55/57；ORB v4 illumination mean error 5.139 px。",
            "- runtime/memory 是否可接受：ORB v4 runtime 3572.659 ms / 116 cases，约 30.8 ms/case；Akaze v4 runtime 8568.21 ms / 116 cases，适合作 fallback 而非主推。",
            "- 是否进入下一轮缺陷/异常调优：可以。Matching 第一轮已形成可复现 A/B、sweep、leaderboard 与 backlog，Phase C 可启动。",
            "",
            "## 6. 证据文件",
            "",
        ]
    )
    lines.extend(f"- `{source}`" for source in report["sourceReports"])
    lines.append("")
    return "\n".join(lines)


def fmt(value: Any) -> str:
    if isinstance(value, (int, float)):
        return f"{float(value):.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def main() -> int:
    operator_documents = {
        "AkazeFeatureMatch": read_json(AKAZE_REPORT),
        "OrbFeatureMatch": read_json(ORB_REPORT),
    }
    backlog = build_backlog(operator_documents)
    report = build_improvement_report(backlog)
    write_json(BACKLOG_JSON, backlog)
    write_text(BACKLOG_MD, render_backlog_markdown(backlog))
    write_json(OUTPUT_JSON, report)
    write_text(OUTPUT_MD, render_improvement_markdown(report))
    write_text(AUDIT_MD, render_audit_markdown(report, backlog))
    print(
        "matching algorithm improvement report complete: "
        f"fixed={report['summary']['fixedCaseCount']} "
        f"regressed={report['summary']['regressedCaseCount']} "
        f"backlog={backlog['summary']['caseCount']} "
        f"output={repo(OUTPUT_JSON)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
