from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_matching_default_off_profiles_v3.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_matching_default_off_profiles_v3.md"
SWEEP_REPORT = REPORT_DIR / "QualityFlywheel_hpatches_matching_sweep_v5.json"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")

PROFILE_CONFIGS = [
    {
        "operator": "AkazeFeatureMatch",
        "profile": "default_v3",
        "baseline": REPORT_DIR / "AkazeFeatureMatch_hpatches_baseline.json",
        "candidate": REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_v5.json",
        "status": "default_off_ready_no_accuracy_delta",
        "decision": "keep-default-off-neutral-candidate",
        "profileRole": "AKAZE opt-in profile aligned with current verified default-v3 parameters.",
    },
    {
        "operator": "OrbFeatureMatch",
        "profile": "replay_safe_dense_strict",
        "baseline": REPORT_DIR / "OrbFeatureMatch_hpatches_baseline.json",
        "candidate": REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_v5.json",
        "status": "default_off_ready_metric_gain_runtime_tradeoff",
        "decision": "keep-default-off-metric-gain-candidate",
        "profileRole": "ORB opt-in profile with lower P95 position/corner error and an explicit runtime tradeoff.",
    },
]


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


def get_summary(path: Path) -> dict[str, Any]:
    document = read_json(path)
    summary = document.get("Summary")
    if not isinstance(summary, dict):
        raise ValueError(f"{repo(path)} is missing Summary")
    return summary


def metric_delta(value: Any, baseline: Any) -> float | None:
    if value is None or baseline is None:
        return None
    return round(float(value) - float(baseline), 6)


def compact_summary(path: Path) -> dict[str, Any]:
    summary = get_summary(path)
    return {
        "sourceReport": repo(path),
        "caseCount": summary.get("CaseCount"),
        "passed": summary.get("Passed"),
        "failed": summary.get("Failed"),
        "passRate": summary.get("PassRate"),
        "meanPositionErrorPx": summary.get("MeanPositionErrorPx"),
        "p95PositionErrorPx": summary.get("P95PositionErrorPx"),
        "p95CornerErrorPx": summary.get("P95CornerErrorPx"),
        "runtimeMs": summary.get("RuntimeMs"),
        "parameters": {
            "MaxFeatures": summary.get("MaxFeatures"),
            "MinInliers": summary.get("MinInliers"),
            "MatchRatio": summary.get("MatchRatio"),
            "RansacThreshold": summary.get("RansacThreshold"),
            "MinInlierRatio": summary.get("MinInlierRatio"),
            "FastThreshold": summary.get("FastThreshold"),
            "EdgeThreshold": summary.get("EdgeThreshold"),
            "AkazeThreshold": summary.get("AkazeThreshold"),
            "AllowCenterOnlyProjection": summary.get("AllowCenterOnlyProjection"),
        },
    }


def sweep_rows() -> dict[tuple[str, str], dict[str, Any]]:
    if not SWEEP_REPORT.exists():
        return {}
    document = read_json(SWEEP_REPORT)
    rows: dict[tuple[str, str], dict[str, Any]] = {}
    for result in document.get("results", []):
        if not isinstance(result, dict):
            continue
        key = (str(result.get("operator")), str(result.get("selectedProfile")))
        rows[key] = {
            "sourceReport": repo(SWEEP_REPORT),
            "candidateJson": result.get("candidateJson"),
            "validation": result.get("validation"),
            "replay": result.get("replay"),
            "holdout": result.get("holdout"),
        }
    return rows


def build_row(config: dict[str, Any], sweep: dict[tuple[str, str], dict[str, Any]]) -> dict[str, Any]:
    baseline = compact_summary(config["baseline"])
    candidate = compact_summary(config["candidate"])
    deltas = {
        "fullPassDelta": int(candidate["passed"]) - int(baseline["passed"]),
        "passRateDelta": metric_delta(candidate["passRate"], baseline["passRate"]),
        "p95PositionErrorPxDelta": metric_delta(candidate["p95PositionErrorPx"], baseline["p95PositionErrorPx"]),
        "p95CornerErrorPxDelta": metric_delta(candidate["p95CornerErrorPx"], baseline["p95CornerErrorPx"]),
        "runtimeMsDelta": metric_delta(candidate["runtimeMs"], baseline["runtimeMs"]),
    }
    attached_sweep = sweep.get((config["operator"], config["profile"]), {})
    replay = attached_sweep.get("replay") if isinstance(attached_sweep, dict) else None
    holdout = attached_sweep.get("holdout") if isinstance(attached_sweep, dict) else None
    has_accuracy_gain = (
        deltas["fullPassDelta"] >= 0
        and (
            (deltas["p95PositionErrorPxDelta"] is not None and deltas["p95PositionErrorPxDelta"] < 0)
            or (deltas["p95CornerErrorPxDelta"] is not None and deltas["p95CornerErrorPxDelta"] < 0)
        )
    )
    has_no_full_regression = (
        deltas["fullPassDelta"] >= 0
        and deltas["p95PositionErrorPxDelta"] is not None
        and deltas["p95PositionErrorPxDelta"] <= 0
        and deltas["p95CornerErrorPxDelta"] is not None
        and deltas["p95CornerErrorPxDelta"] <= 0
    )
    blockers = ["Release/field replay is still required before any default-on promotion."]
    if deltas["runtimeMsDelta"] is not None and deltas["runtimeMsDelta"] > 0:
        blockers.append("Signed runtime budget must be met by release/field replay before default-on.")
    if not has_accuracy_gain:
        blockers.append("Accuracy delta is neutral; keep this as an opt-in evidence/observability profile.")
    status = config["status"] if has_no_full_regression and replay and holdout else "hold_evidence_or_metric_regression"
    return {
        "operator": config["operator"],
        "profile": config["profile"],
        "profileRole": config["profileRole"],
        "status": status,
        "decision": config["decision"] if status == config["status"] else "hold",
        "defaultOff": True,
        "productDefaultChange": False,
        "baseline": baseline,
        "candidate": candidate,
        "sweep": attached_sweep,
        "deltas": deltas,
        "hasAccuracyGain": has_accuracy_gain,
        "readyForDefaultOff": status == config["status"],
        "readyForDefaultOn": False,
        "promotionGate": (
            "Keep default-off. Full HPatches pass count and P95 errors must not regress; "
            "default-on requires release/field replay and runtime acceptance."
        ),
        "blockers": blockers,
    }


def build_document() -> dict[str, Any]:
    sweep = sweep_rows()
    rows = [build_row(config, sweep) for config in PROFILE_CONFIGS]
    ready_rows = [row for row in rows if row["readyForDefaultOff"]]
    gain_rows = [row for row in rows if row["hasAccuracyGain"]]
    return {
        "schemaVersion": "2026-05-01.matching-default-off-profiles.v3",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "Public HPatches evidence only; these profiles are opt-in candidates and do not change product defaults.",
        "dataset": "quality/public_datasets/hpatches",
        "productDefaultChange": False,
        "sourceReports": [
            repo(SWEEP_REPORT),
            *[repo(config["baseline"]) for config in PROFILE_CONFIGS],
            *[repo(config["candidate"]) for config in PROFILE_CONFIGS],
        ],
        "profileGate": {
            "status": "default-off-candidates-ready",
            "productDefaultChange": False,
            "primaryCandidate": "OrbFeatureMatch/replay_safe_dense_strict",
            "fallbackCandidate": "AkazeFeatureMatch/default_v3",
            "releaseGateStatus": "blocked-missing-field-replay",
            "requiredBeforeDefaultOn": [
                "run release/field replay with the candidate profile explicitly enabled",
                "show no full pass, P95 position, or P95 corner regression",
                "meet the signed runtime budget for ORB replay_safe_dense_strict",
            ],
        },
        "rows": rows,
        "summary": {
            "candidateCount": len(rows),
            "readyDefaultOffCount": len(ready_rows),
            "metricGainCount": len(gain_rows),
            "defaultOnCount": 0,
        },
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if document.get("schemaVersion") != "2026-05-01.matching-default-off-profiles.v3":
        errors.append("schemaVersion must be matching-default-off-profiles.v3")
    if document.get("productDefaultChange") is not False:
        errors.append("matching profile report must not change product defaults")
    gate = document.get("profileGate") if isinstance(document.get("profileGate"), dict) else {}
    if gate.get("status") != "default-off-candidates-ready":
        errors.append("profileGate.status must be default-off-candidates-ready")
    if gate.get("releaseGateStatus") != "blocked-missing-field-replay":
        errors.append("release gate must remain blocked until field replay exists")
    rows = document.get("rows") if isinstance(document.get("rows"), list) else []
    expected = {(config["operator"], config["profile"]) for config in PROFILE_CONFIGS}
    actual = {(row.get("operator"), row.get("profile")) for row in rows if isinstance(row, dict)}
    if actual != expected:
        errors.append("matching profile rows must exactly cover current AKAZE/ORB default-off candidates")
    for row in rows:
        deltas = row.get("deltas") if isinstance(row.get("deltas"), dict) else {}
        label = f"{row.get('operator')}/{row.get('profile')}"
        if row.get("defaultOff") is not True or row.get("productDefaultChange") is not False:
            errors.append(f"{label} must remain default-off without product default changes")
        if deltas.get("fullPassDelta") is None or deltas["fullPassDelta"] < 0:
            errors.append(f"{label} must not reduce full HPatches pass count")
        if deltas.get("p95PositionErrorPxDelta") is None or deltas["p95PositionErrorPxDelta"] > 0:
            errors.append(f"{label} must not regress P95 position error")
        if deltas.get("p95CornerErrorPxDelta") is None or deltas["p95CornerErrorPxDelta"] > 0:
            errors.append(f"{label} must not regress P95 corner error")
        if row.get("readyForDefaultOn") is not False:
            errors.append(f"{label} must not be marked ready for default-on")
    summary = document.get("summary") if isinstance(document.get("summary"), dict) else {}
    if summary.get("readyDefaultOffCount") != len(expected):
        errors.append("all current matching candidates must be ready only for default-off opt-in use")
    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("report contains raw local path pattern")
    return errors


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Matching Default-Off Profiles v3",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "## Profile Gate",
        "",
        f"- Status: `{document['profileGate']['status']}`",
        f"- Product default change: `{document['profileGate']['productDefaultChange']}`",
        f"- Primary candidate: `{document['profileGate']['primaryCandidate']}`",
        f"- Fallback candidate: `{document['profileGate']['fallbackCandidate']}`",
        f"- Release gate status: `{document['profileGate']['releaseGateStatus']}`",
        "",
        "## Candidates",
        "",
        "| Operator | Profile | Status | Full pass delta | P95 pos delta | P95 corner delta | Runtime delta ms | Replay pass | Decision |",
        "|---|---|---|---:|---:|---:|---:|---:|---|",
    ]
    for row in document["rows"]:
        deltas = row["deltas"]
        replay = row.get("sweep", {}).get("replay") or {}
        lines.append(
            f"| {row['operator']} | {row['profile']} | {row['status']} | "
            f"{fmt(deltas['fullPassDelta'])} | {fmt(deltas['p95PositionErrorPxDelta'])} | "
            f"{fmt(deltas['p95CornerErrorPxDelta'])} | {fmt(deltas['runtimeMsDelta'])} | "
            f"{fmt(replay.get('passed'))}/{fmt(replay.get('caseCount'))} | {row['decision']} |"
        )
    lines.extend(["", "## Required Before Default-On", ""])
    lines.extend([f"- {item}" for item in document["profileGate"]["requiredBeforeDefaultOn"]])
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build matching default-off candidate profile report.")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    document = read_json(OUTPUT_JSON) if args.validate_only else build_document()
    errors = validate_document(document)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 1
    if not args.validate_only:
        write_json(OUTPUT_JSON, document)
        write_text(OUTPUT_MD, render_markdown(document))
    action = "valid" if args.validate_only else "ready"
    print(f"matching default-off profile report {action}: output={repo(OUTPUT_JSON)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
