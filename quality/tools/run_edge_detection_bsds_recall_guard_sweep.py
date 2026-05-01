from __future__ import annotations

import argparse
import json
import subprocess
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
REPLAY_MANIFEST = REPORT_DIR / "QualityFlywheel_public_benchmark_replay_manifest.json"
RUNNER_PROJECT = "quality/tools/BsdsEdgeContourDatasetRunner/BsdsEdgeContourDatasetRunner.csproj"
INDEX_PATH = "quality/datasets/bsds500_index.json"
OUTPUT_JSON = REPORT_DIR / "QualityFlywheel_edge_detection_recall_guard_sweep_v2.json"
OUTPUT_MD = REPORT_DIR / "QualityFlywheel_edge_detection_recall_guard_sweep_v2.md"


@dataclass(frozen=True)
class Profile:
    name: str
    threshold1: float
    threshold2: float
    l2_gradient: bool
    auto_threshold: bool = False
    auto_threshold_strategy: str = "MedianIntensity"
    enable_gaussian_blur: bool = True
    gaussian_kernel_size: int = 5
    aperture_size: int = 3
    candidate_version: str = "recall_not_lower_v2"


PROFILES = (
    Profile("canny_default_50_150", 50, 150, False, candidate_version="baseline_proxy"),
    Profile("canny_l2_50_150", 50, 150, True),
    Profile("canny_fixed_low_45_135", 45, 135, True),
    Profile("canny_fixed_low_40_120", 40, 120, True),
    Profile("canny_fixed_low_35_105", 35, 105, True),
    Profile("canny_recall_guard_percentile", 50, 150, True, auto_threshold=True, auto_threshold_strategy="RecallGuardPercentile"),
    Profile("canny_otsu_gradient", 50, 150, True, auto_threshold=True, auto_threshold_strategy="OtsuGradient"),
)


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


def edge_replay_case_ids() -> list[str]:
    manifest = read_json(REPLAY_MANIFEST)
    return [
        str(item["caseId"])
        for item in manifest.get("cases", [])
        if item.get("operator") == "EdgeDetection"
    ]


def output_path(profile: Profile) -> Path:
    return REPORT_DIR / f"EdgeDetection_bsds500_recall_guard_sweep_v2_{profile.name}.json"


def report_path(profile: Profile) -> Path:
    return REPORT_DIR / f"EdgeDetection_bsds500_recall_guard_sweep_v2_{profile.name}.md"


def run_profile(profile: Profile, case_ids: list[str]) -> dict[str, Any]:
    output = output_path(profile)
    report = report_path(profile)
    command = [
        "dotnet",
        "run",
        "--project",
        RUNNER_PROJECT,
        "--",
        "--index",
        INDEX_PATH,
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--split",
        "test",
        "--case-ids",
        ",".join(case_ids),
        "--candidate-version",
        profile.candidate_version,
        "--profile",
        profile.name,
        "--threshold1",
        str(profile.threshold1),
        "--threshold2",
        str(profile.threshold2),
        "--auto-threshold",
        str(profile.auto_threshold).lower(),
        "--auto-threshold-strategy",
        profile.auto_threshold_strategy,
        "--enable-gaussian-blur",
        str(profile.enable_gaussian_blur).lower(),
        "--gaussian-kernel-size",
        str(profile.gaussian_kernel_size),
        "--aperture-size",
        str(profile.aperture_size),
        "--l2-gradient",
        str(profile.l2_gradient).lower(),
    ]
    completed = subprocess.run(command, cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            f"error: EdgeDetection recall-guard profile failed: {profile.name}\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return read_json(output)


def compact(document: dict[str, Any], profile: Profile) -> dict[str, Any]:
    summary = document["Summary"]
    return {
        "profile": profile.name,
        "sourceReport": repo(output_path(profile)),
        "caseCount": summary["CaseCount"],
        "threshold1": profile.threshold1,
        "threshold2": profile.threshold2,
        "l2Gradient": profile.l2_gradient,
        "autoThreshold": profile.auto_threshold,
        "autoThresholdStrategy": profile.auto_threshold_strategy,
        "predictedEdgePixels": summary["PredictedEdgePixels"],
        "boundaryPrecision": summary["BoundaryPrecision"],
        "boundaryRecall": summary["BoundaryRecall"],
        "boundaryF1": summary["BoundaryF1"],
        "consensusBoundaryPrecision": summary["ConsensusBoundaryPrecision"],
        "consensusBoundaryRecall": summary["ConsensusBoundaryRecall"],
        "consensusBoundaryF1": summary["ConsensusBoundaryF1"],
        "boundaryToPredictedMeanDistancePx": summary.get("BoundaryToPredictedMeanDistancePx"),
        "consensusToPredictedMeanDistancePx": summary.get("ConsensusToPredictedMeanDistancePx"),
        "runtimeMsP95": summary["RuntimeMsP95"],
    }


def build_document(rows: list[dict[str, Any]]) -> dict[str, Any]:
    baseline = next(row for row in rows if row["profile"] == "canny_default_50_150")
    current = next(row for row in rows if row["profile"] == "canny_l2_50_150")
    recall_safe_next_round = [
        row for row in rows
        if row["profile"] not in {baseline["profile"], current["profile"]}
        and row["boundaryRecall"] >= current["boundaryRecall"]
        and row["consensusBoundaryRecall"] >= current["consensusBoundaryRecall"]
        and row["boundaryF1"] >= current["boundaryF1"]
        and row["boundaryPrecision"] >= baseline["boundaryPrecision"] - 0.02
        and row["predictedEdgePixels"] <= baseline["predictedEdgePixels"] * 1.5
    ]
    return {
        "schemaVersion": "2026-05-01.edge-detection-recall-guard-sweep.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "BSDS500 replay-subset threshold probe only; EdgeDetection promotion is paused and product defaults are unchanged.",
        "caseIds": edge_replay_case_ids(),
        "baselineProxy": baseline,
        "currentCandidate": current,
        "rows": rows,
        "selectedProfile": None,
        "recallSafeNextRoundProfiles": [row["profile"] for row in recall_safe_next_round],
        "decision": "promotion-paused-recall-not-lower-required",
        "promotionPaused": True,
        "nextObjective": "Find a profile with boundary recall and consensus boundary recall not lower than canny_l2_50_150; F1 alone is not sufficient.",
        "recallGuardPolicy": "Candidate must keep boundaryRecall >= currentCandidate.boundaryRecall and consensusBoundaryRecall >= currentCandidate.consensusBoundaryRecall.",
        "productDefaultChange": False,
    }


def validate_document(document: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    if len(document.get("caseIds", [])) < 20:
        errors.append("EdgeDetection recall-guard sweep must include the full replay subset")
    rows = document.get("rows", [])
    if len(rows) < len(PROFILES):
        errors.append("EdgeDetection recall-guard sweep missing profile rows")
    if any(row.get("caseCount", 0) < 20 for row in rows):
        errors.append("Every EdgeDetection recall-guard profile must include 20 replay cases")
    if document.get("productDefaultChange") is not False:
        errors.append("EdgeDetection recall-guard sweep must not change product defaults")
    if document.get("promotionPaused") is not True:
        errors.append("EdgeDetection promotion must remain paused")
    if document.get("selectedProfile") is not None:
        errors.append("EdgeDetection v2 must not select a promoted profile")
    return errors


def fmt(value: Any) -> str:
    if isinstance(value, float):
        return f"{value:.4f}".rstrip("0").rstrip(".")
    return "-" if value is None else str(value)


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# EdgeDetection Recall-Guard Sweep v2",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"Decision: `{document['decision']}`",
        f"SelectedProfile: `{document['selectedProfile']}`",
        f"PromotionPaused: `{document['promotionPaused']}`",
        f"NextObjective: `{document['nextObjective']}`",
        f"ClaimBoundary: `{document['claimBoundary']}`",
        "",
        "| Profile | Thresholds | Auto | Strategy | L2 | Precision | Recall | F1 | Consensus recall | B->P px | Predicted | P95 ms |",
        "|---|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in document["rows"]:
        lines.append(
            f"| {row['profile']} | {fmt(row['threshold1'])}/{fmt(row['threshold2'])} | {row['autoThreshold']} | {row['autoThresholdStrategy']} | {row['l2Gradient']} | "
            f"{fmt(row['boundaryPrecision'])} | {fmt(row['boundaryRecall'])} | {fmt(row['boundaryF1'])} | "
            f"{fmt(row['consensusBoundaryRecall'])} | {fmt(row['boundaryToPredictedMeanDistancePx'])} | "
            f"{row['predictedEdgePixels']} | {fmt(row['runtimeMsP95'])} |"
        )
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run a small EdgeDetection recall-guard threshold sweep over replay cases.")
    parser.add_argument("--validate-only", action="store_true", help="Validate existing sweep report without rerunning profiles.")
    args = parser.parse_args()

    if args.validate_only:
        document = read_json(OUTPUT_JSON)
    else:
        case_ids = edge_replay_case_ids()
        rows = [compact(run_profile(profile, case_ids), profile) for profile in PROFILES]
        document = build_document(rows)
        write_json(OUTPUT_JSON, document)
        write_text(OUTPUT_MD, render_markdown(document))
    errors = validate_document(document)
    if errors:
        for error in errors:
            print(f"error: {error}")
        return 2
    action = "valid" if args.validate_only else "complete"
    print(
        "EdgeDetection recall-guard sweep "
        f"{action}: decision={document['decision']} selected={document['selectedProfile']} "
        f"output={repo(OUTPUT_JSON)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
