from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_matching_replay_safe_profile_candidates_v2.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_matching_replay_safe_profile_candidates_v2.md"
SWEEP_REPORT = REPORT_DIR / "QualityFlywheel_hpatches_matching_sweep_v4.json"
RAW_PATH_RE = re.compile(r"([A-Za-z]:\\|\\\\|/Users/|/home/|/mnt/)")
PROMOTION_READY_PROFILES = {
    ("OrbFeatureMatch", "center_only_projection_v1"),
    ("AkazeFeatureMatch", "center_only_projection_v1"),
}


BASELINES = {
    "AkazeFeatureMatch": REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_v4.json",
    "OrbFeatureMatch": REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_v4.json",
}


CANDIDATES = [
    {
        "operator": "OrbFeatureMatch",
        "profile": "center_only_projection_v1",
        "path": REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_center_only_v1.json",
        "replayPath": REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_replay_center_only_v1.json",
        "source": "default-off geometry gate candidate",
    },
    {
        "operator": "AkazeFeatureMatch",
        "profile": "center_only_projection_v1",
        "path": REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_center_only_v1.json",
        "replayPath": REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_replay_center_only_v1.json",
        "source": "default-off geometry gate candidate",
    },
    {
        "operator": "OrbFeatureMatch",
        "profile": "replay_safe_high_ratio",
        "path": REPORT_DIR / "OrbFeatureMatch_hpatches_replay_safe_high_ratio_profile.json",
        "source": "sweep replay-safe candidate",
    },
    {
        "operator": "OrbFeatureMatch",
        "profile": "high_ratio_ransac6",
        "path": REPORT_DIR / "OrbFeatureMatch_hpatches_profile_high_ratio_ransac6.json",
        "replayPath": REPORT_DIR / "OrbFeatureMatch_hpatches_profile_high_ratio_ransac6_replay.json",
        "source": "ad-hoc full+replay candidate",
    },
    {
        "operator": "OrbFeatureMatch",
        "profile": "dense_ransac5",
        "path": REPORT_DIR / "OrbFeatureMatch_hpatches_profile_dense_ransac5.json",
        "replayPath": REPORT_DIR / "OrbFeatureMatch_hpatches_profile_dense_ransac5_replay.json",
        "source": "ad-hoc full+replay candidate",
    },
    {
        "operator": "OrbFeatureMatch",
        "profile": "mid_ratio_ransac6",
        "path": REPORT_DIR / "OrbFeatureMatch_hpatches_profile_mid_ratio_ransac6.json",
        "replayPath": REPORT_DIR / "OrbFeatureMatch_hpatches_profile_mid_ratio_ransac6_replay.json",
        "source": "ad-hoc full+replay candidate",
    },
    {
        "operator": "AkazeFeatureMatch",
        "profile": "partial_plane_low_detector_threshold",
        "path": REPORT_DIR / "AkazeFeatureMatch_hpatches_partial_plane_low_detector_profile.json",
        "source": "sweep risk candidate",
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


def summary(path: Path) -> dict[str, Any]:
    document = read_json(path)
    value = document["Summary"]
    scenarios = {item["Scenario"]: item for item in document.get("Scenarios", [])}
    return {
        "sourceReport": repo(path),
        "operator": value["Operator"],
        "caseCount": value["CaseCount"],
        "passed": value["Passed"],
        "failed": value["Failed"],
        "passRate": value["PassRate"],
        "meanPositionErrorPx": value["MeanPositionErrorPx"],
        "p95PositionErrorPx": value["P95PositionErrorPx"],
        "p95CornerErrorPx": value.get("P95CornerErrorPx"),
        "runtimeMs": value["RuntimeMs"],
        "viewpointPassed": scenarios.get("viewpoint", {}).get("Passed"),
        "viewpointCaseCount": scenarios.get("viewpoint", {}).get("CaseCount"),
        "parameters": {
            "MaxFeatures": value.get("MaxFeatures"),
            "MinInliers": value.get("MinInliers"),
            "MatchRatio": value.get("MatchRatio"),
            "RansacThreshold": value.get("RansacThreshold"),
            "MinInlierRatio": value.get("MinInlierRatio"),
            "FastThreshold": value.get("FastThreshold"),
            "EdgeThreshold": value.get("EdgeThreshold"),
            "AkazeThreshold": value.get("AkazeThreshold"),
            "AllowCenterOnlyProjection": value.get("AllowCenterOnlyProjection"),
        },
    }


def sweep_rows() -> dict[tuple[str, str], dict[str, Any]]:
    if not SWEEP_REPORT.exists():
        return {}
    rows: dict[tuple[str, str], dict[str, Any]] = {}
    sweep = read_json(SWEEP_REPORT)
    for item in sweep.get("results", []):
        operator = item.get("operator")
        candidate = read_json(REPORT_DIR / f"{operator}_hpatches_candidate_v4.json")
        for bucket in ("validationRows", "replayRows", "holdoutRows"):
            for row in candidate.get("Sweep", {}).get(bucket, []):
                key = (operator, row["profile"])
                rows.setdefault(key, {})[bucket[:-4]] = row
    return rows


def replay_summary(candidate: dict[str, Any], sweep: dict[tuple[str, str], dict[str, Any]]) -> dict[str, Any] | None:
    replay_path = candidate.get("replayPath")
    if replay_path and replay_path.exists():
        return summary(replay_path)
    row = sweep.get((candidate["operator"], candidate["profile"]), {}).get("replay")
    if not row:
        return None
    return {
        "sourceReport": repo(SWEEP_REPORT),
        "caseCount": row.get("caseCount"),
        "passed": row.get("passed"),
        "failed": row.get("failed"),
        "passRate": row.get("passRate"),
        "meanPositionErrorPx": row.get("meanPositionErrorPx"),
        "p95PositionErrorPx": row.get("p95PositionErrorPx"),
        "p95CornerErrorPx": row.get("p95CornerErrorPx"),
        "runtimeMs": row.get("runtimeMs"),
    }


def delta(value: Any, baseline: Any) -> Any:
    if value is None or baseline is None:
        return None
    return round(float(value) - float(baseline), 6)


def build_document() -> dict[str, Any]:
    sweep = sweep_rows()
    baseline_rows = {operator: summary(path) for operator, path in BASELINES.items()}
    rows = []
    for candidate in CANDIDATES:
        if not candidate["path"].exists():
            continue
        full = summary(candidate["path"])
        replay = replay_summary(candidate, sweep)
        baseline = baseline_rows[candidate["operator"]]
        baseline_replay = sweep.get((candidate["operator"], read_json(BASELINES[candidate["operator"]]).get("Sweep", {}).get("selectedProfile")), {}).get("replay")
        replay_pass_delta = None if replay is None or baseline_replay is None else int(replay["passed"]) - int(baseline_replay["passed"])
        full_pass_delta = int(full["passed"]) - int(baseline["passed"])
        p95_position_delta = delta(full["p95PositionErrorPx"], baseline["p95PositionErrorPx"])
        p95_corner_delta = delta(full["p95CornerErrorPx"], baseline["p95CornerErrorPx"])
        promotion_ready = (
            replay_pass_delta is not None
            and replay_pass_delta >= 0
            and full_pass_delta >= 0
            and p95_position_delta is not None
            and p95_position_delta <= 0
            and p95_corner_delta is not None
            and p95_corner_delta <= 0
        )
        if promotion_ready:
            decision = "promote-candidate"
        elif replay_pass_delta is not None and replay_pass_delta < 0:
            decision = "reject-replay-regression"
        elif full_pass_delta < 0:
            decision = "reject-full-pass-regression"
        elif p95_position_delta is not None and p95_position_delta > 0:
            decision = "hold-position-regression"
        else:
            decision = "hold-corner-or-evidence-gap"
        rows.append(
            {
                "operator": candidate["operator"],
                "profile": candidate["profile"],
                "source": candidate["source"],
                "full": full,
                "replay": replay,
                "deltas": {
                    "fullPassDelta": full_pass_delta,
                    "replayPassDelta": replay_pass_delta,
                    "p95PositionErrorPxDelta": p95_position_delta,
                    "p95CornerErrorPxDelta": p95_corner_delta,
                    "runtimeMsDelta": delta(full["runtimeMs"], baseline["runtimeMs"]),
                },
                "decision": decision,
                "promotionReady": promotion_ready,
            }
        )
    promotion_rows = [row for row in rows if row["promotionReady"]]
    return {
        "schemaVersion": "2026-04-30.matching-replay-safe-profile-candidates.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "Public HPatches profile triage only; no default profile is promoted unless replay, pass count, P95 position, and P95 corner all avoid regression.",
        "baselines": baseline_rows,
        "rows": rows,
        "summary": {
            "candidateCount": len(rows),
            "promotionCount": len(promotion_rows),
            "holdOrRejectCount": len(rows) - len(promotion_rows),
            "promotionReadyProfiles": [
                {
                    "operator": row["operator"],
                    "profile": row["profile"],
                    "fullPass": row["full"]["passed"],
                    "fullCaseCount": row["full"]["caseCount"],
                    "replayPass": (row["replay"] or {}).get("passed"),
                    "replayCaseCount": (row["replay"] or {}).get("caseCount"),
                }
                for row in promotion_rows
            ],
        },
        "profileGate": {
            "status": "promotion-ready-default-off",
            "productDefaultChange": False,
            "primaryCandidate": "OrbFeatureMatch/center_only_projection_v1",
            "fallbackCandidate": "AkazeFeatureMatch/center_only_projection_v1",
            "releaseGateStatus": "blocked-missing-field-replay",
            "requiredBeforeDefaultOn": [
                "run release/field replay with product default enabled in a separate candidate profile",
                "show zero matching regressions and no P95 position/corner regression against the pinned default-off baseline",
                "attach runtime budget and field owner sign-off",
            ],
        },
        "promotionCount": len(promotion_rows),
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    rows = document.get("rows")
    if not isinstance(rows, list) or not rows:
        errors.append("report must include candidate rows")
        return errors

    rows_by_profile = {
        (str(row.get("operator")), str(row.get("profile"))): row
        for row in rows
        if isinstance(row, dict)
    }
    for key in sorted(PROMOTION_READY_PROFILES):
        row = rows_by_profile.get(key)
        label = "/".join(key)
        if row is None:
            errors.append(f"{label} missing from replay-safe report")
            continue
        if row.get("decision") != "promote-candidate" or row.get("promotionReady") is not True:
            errors.append(f"{label} must be promotion-ready")
        full = row.get("full") if isinstance(row.get("full"), dict) else {}
        replay = row.get("replay") if isinstance(row.get("replay"), dict) else {}
        full_parameters = full.get("parameters") if isinstance(full.get("parameters"), dict) else {}
        replay_parameters = replay.get("parameters") if isinstance(replay.get("parameters"), dict) else {}
        if full_parameters.get("AllowCenterOnlyProjection") is not True:
            errors.append(f"{label} full profile must opt in to AllowCenterOnlyProjection")
        if replay_parameters.get("AllowCenterOnlyProjection") is not True:
            errors.append(f"{label} replay profile must opt in to AllowCenterOnlyProjection")
        deltas = row.get("deltas") if isinstance(row.get("deltas"), dict) else {}
        if deltas.get("fullPassDelta") is None or deltas["fullPassDelta"] < 0:
            errors.append(f"{label} must not reduce full HPatches pass count")
        if deltas.get("replayPassDelta") is None or deltas["replayPassDelta"] < 0:
            errors.append(f"{label} must not reduce matching replay pass count")
        if deltas.get("p95PositionErrorPxDelta") is None or deltas["p95PositionErrorPxDelta"] > 0:
            errors.append(f"{label} must not regress P95 position error")
        if deltas.get("p95CornerErrorPxDelta") is None or deltas["p95CornerErrorPxDelta"] > 0:
            errors.append(f"{label} must not regress P95 corner error")

    promotion_count = document.get("promotionCount")
    if promotion_count != len(PROMOTION_READY_PROFILES):
        errors.append(f"promotionCount must be {len(PROMOTION_READY_PROFILES)}, got {promotion_count}")

    summary = document.get("summary") if isinstance(document.get("summary"), dict) else {}
    if summary.get("promotionCount") != promotion_count:
        errors.append("summary.promotionCount must match top-level promotionCount")

    gate = document.get("profileGate") if isinstance(document.get("profileGate"), dict) else {}
    if gate.get("status") != "promotion-ready-default-off":
        errors.append("profileGate.status must be promotion-ready-default-off")
    if gate.get("productDefaultChange") is not False:
        errors.append("profileGate.productDefaultChange must remain false")
    if gate.get("releaseGateStatus") != "blocked-missing-field-replay":
        errors.append("profileGate.releaseGateStatus must remain blocked until field replay is attached")

    if RAW_PATH_RE.search(json.dumps(document, ensure_ascii=False)):
        errors.append("report contains raw local path pattern")
    return errors


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.3f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Matching Replay-Safe Profile Candidates v2",
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
        "## Candidate Decisions",
        "",
        "| Operator | Profile | Full pass | Replay pass delta | P95 pos delta | P95 corner delta | Runtime delta ms | Decision |",
        "|---|---|---:|---:|---:|---:|---:|---|",
    ]
    for row in document["rows"]:
        full = row["full"]
        replay = row["replay"] or {}
        deltas = row["deltas"]
        lines.append(
            f"| {row['operator']} | {row['profile']} | {full['passed']}/{full['caseCount']} | "
            f"{fmt(deltas['replayPassDelta'])} ({fmt(replay.get('passed'))}/{fmt(replay.get('caseCount'))}) | "
            f"{fmt(deltas['p95PositionErrorPxDelta'])} | {fmt(deltas['p95CornerErrorPxDelta'])} | "
            f"{fmt(deltas['runtimeMsDelta'])} | {row['decision']} |"
        )
    lines.extend(["", "## Baselines", ""])
    lines.extend([
        "| Operator | Full pass | P95 position | P95 corner | Runtime ms | Source |",
        "|---|---:|---:|---:|---:|---|",
    ])
    for operator, baseline in document["baselines"].items():
        lines.append(
            f"| {operator} | {baseline['passed']}/{baseline['caseCount']} | "
            f"{fmt(baseline['p95PositionErrorPx'])} | {fmt(baseline['p95CornerErrorPx'])} | "
            f"{fmt(baseline['runtimeMs'])} | {baseline['sourceReport']} |"
        )
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build or validate matching replay-safe profile candidate gates.")
    parser.add_argument("--validate-only", action="store_true", help="Validate the existing report without rewriting it.")
    args = parser.parse_args()

    document = read_json(OUTPUT_JSON) if args.validate_only else build_document()
    errors = validate_document(document)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    if not args.validate_only:
        write_json(OUTPUT_JSON, document)
        write_text(OUTPUT_MD, render_markdown(document))
    action = "valid" if args.validate_only else "complete"
    print(f"matching replay-safe profile report {action}: promotionCount={document['promotionCount']}, output={repo(OUTPUT_JSON)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
