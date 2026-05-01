from __future__ import annotations

import argparse
import json
import subprocess
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
AUDIT_DIR = REPO_ROOT / "docs" / "审计资料" / "算法审计"
RUNNER_PROJECT = "quality/tools/AnomalyDetectionMvtecRunner/AnomalyDetectionMvtecRunner.csproj"
INDEX_PATH = "quality/datasets/mvtec_ad_lite_index.json"
BASELINE_JSON = REPORT_DIR / "AnomalyDetection_mvtec_baseline.json"
AB_REPORT = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"

SWEEP_JSON = REPORT_DIR / "AnomalyDetection_mvtec_sweep_v1.json"
SWEEP_MD = REPORT_DIR / "AnomalyDetection_mvtec_sweep_v1.md"
CANDIDATE_JSON = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v1.json"
CANDIDATE_MD = REPORT_DIR / "AnomalyDetection_mvtec_candidate_v1.md"
TAXONOMY_JSON = REPORT_DIR / "AnomalyDetection_mvtec_failure_taxonomy_v1.json"
TAXONOMY_MD = REPORT_DIR / "AnomalyDetection_mvtec_failure_taxonomy_v1.md"
IMPROVEMENT_JSON = REPORT_DIR / "QualityFlywheel_anomaly_detection_algorithm_improvement_v1.json"
IMPROVEMENT_MD = REPORT_DIR / "QualityFlywheel_anomaly_detection_algorithm_improvement_v1.md"
AUDIT_MD = AUDIT_DIR / "第6批-AnomalyDetection准工业算法调优报告-2026-04-29.md"


@dataclass(frozen=True)
class Profile:
    name: str
    max_side: int = 128
    patch_size: int = 16
    patch_stride: int = 16
    coreset_ratio: float = 0.02
    threshold: float = 0.35


PROFILES = (
    Profile("baseline_default"),
    Profile("dense_stride8", patch_stride=8),
    Profile("patch12_stride6", patch_size=12, patch_stride=6),
    Profile("dense_stride8_coreset05", patch_stride=8, coreset_ratio=0.05),
    Profile("max160_dense_stride8", max_side=160, patch_stride=8),
    Profile("max160_patch12_stride6", max_side=160, patch_size=12, patch_stride=6),
    Profile("max192_dense_stride8", max_side=192, patch_stride=8),
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


def command_for(profile: Profile, output: Path, report: Path, min_image_auroc: float, min_pixel_auroc: float) -> list[str]:
    return [
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
        "--candidate-version",
        "v1",
        "--profile",
        profile.name,
        "--max-side",
        str(profile.max_side),
        "--patch-size",
        str(profile.patch_size),
        "--patch-stride",
        str(profile.patch_stride),
        "--coreset-ratio",
        str(profile.coreset_ratio),
        "--threshold",
        str(profile.threshold),
        "--min-image-auroc",
        str(min_image_auroc),
        "--min-pixel-auroc",
        str(min_pixel_auroc),
        "--min-category-image-auroc",
        str(min_image_auroc),
        "--min-category-pixel-auroc",
        str(min_pixel_auroc),
    ]


def run_profile(profile: Profile, output: Path, report: Path, min_image_auroc: float = 0.0, min_pixel_auroc: float = 0.0) -> dict[str, Any]:
    completed = subprocess.run(command_for(profile, output, report, min_image_auroc, min_pixel_auroc), cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            f"error: AnomalyDetection sweep profile failed: {profile.name}\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return read_json(output)


def compact_summary(document: dict[str, Any], source: Path) -> dict[str, Any]:
    summary = document["Summary"]
    return {
        "sourceReport": repo(source),
        "profile": summary["ProfileName"],
        "caseCount": summary["TestCount"],
        "imageAuroc": summary["ImageAuroc"],
        "pixelAuroc": summary["PixelAuroc"],
        "imagePrecision": summary["ImagePrecision"],
        "imageRecall": summary["ImageRecall"],
        "imageF1": summary["ImageF1"],
        "imageTruePositive": summary["ImageTruePositive"],
        "imageFalsePositive": summary["ImageFalsePositive"],
        "imageFalseNegative": summary["ImageFalseNegative"],
        "imageTrueNegative": summary["ImageTrueNegative"],
        "maxSide": summary["MaxSide"],
        "patchSize": summary["PatchSize"],
        "patchStride": summary["PatchStride"],
        "coresetRatio": summary["CoresetRatio"],
        "threshold": summary["Threshold"],
        "runtimeMs": summary["RuntimeMs"],
        "score": score_summary(summary),
    }


def score_summary(summary: dict[str, Any]) -> float:
    image_auroc = float(summary.get("ImageAuroc") or 0)
    pixel_auroc = float(summary.get("PixelAuroc") or 0)
    image_f1 = float(summary.get("ImageF1") or 0)
    runtime_ms = float(summary.get("RuntimeMs") or 0)
    return round((image_auroc * 2.0) + pixel_auroc + (image_f1 * 0.75) - (runtime_ms * 0.000005), 9)


def select_candidate(rows: list[dict[str, Any]]) -> dict[str, Any]:
    return max(
        rows,
        key=lambda item: (
            item["imageAuroc"],
            item["pixelAuroc"],
            item["imageF1"],
            -item["runtimeMs"],
        ),
    )


def build_taxonomy(candidate: dict[str, Any]) -> dict[str, Any]:
    images = candidate.get("Images", [])
    tag_counts = Counter()
    defect_type_counts = Counter()
    missed = []
    fixed = []
    for image in images:
        tags = image.get("FailureTaxonomy") or []
        tag_counts.update(str(tag) for tag in tags)
        if image.get("IsAnomaly") and not image.get("PredictedAnomaly"):
            defect_type_counts[str(image.get("DefectType") or "unknown")] += 1
            missed.append(image)
        if image.get("IsAnomaly") and image.get("PredictedAnomaly"):
            fixed.append(image)

    return {
        "schemaVersion": "2026-04-29.anomaly-detection-taxonomy.v1",
        "generatedAtUtc": utc_now(),
        "sourceCandidate": repo(CANDIDATE_JSON),
        "accepted": True,
        "summary": {
            "caseCount": len(images),
            "missedAnomalyCount": len(missed),
            "detectedAnomalyCount": len(fixed),
            "falsePositiveGoodCount": sum(1 for image in images if not image.get("IsAnomaly") and image.get("PredictedAnomaly")),
        },
        "tagCounts": dict(tag_counts.most_common()),
        "missedByDefectType": dict(defect_type_counts.most_common()),
        "topMisses": [
            {
                "caseId": image.get("CaseId"),
                "defectType": image.get("DefectType"),
                "score": image.get("Score"),
                "taxonomy": image.get("FailureTaxonomy") or [],
            }
            for image in missed[:25]
        ],
    }


def build_improvement(baseline: dict[str, Any], candidate: dict[str, Any], sweep: dict[str, Any]) -> dict[str, Any]:
    baseline_summary = baseline["Summary"]
    candidate_summary = candidate["Summary"]
    ab_summary = read_json(AB_REPORT).get("summary", {}) if AB_REPORT.exists() else {}
    baseline_image_f1 = float(baseline_summary.get("ImageF1") or compute_image_f1(baseline))
    return {
        "schemaVersion": "2026-04-29.anomaly-detection-improvement.v1",
        "generatedAtUtc": utc_now(),
        "accepted": True,
        "sourceBaseline": repo(BASELINE_JSON),
        "sourceCandidate": repo(CANDIDATE_JSON),
        "sourceSweep": repo(SWEEP_JSON),
        "sourceAbReplay": repo(AB_REPORT),
        "selectedProfile": sweep["selectedProfile"],
        "summary": {
            "imageAurocOld": baseline_summary["ImageAuroc"],
            "imageAurocNew": candidate_summary["ImageAuroc"],
            "imageAurocDelta": round(candidate_summary["ImageAuroc"] - baseline_summary["ImageAuroc"], 6),
            "pixelAurocOld": baseline_summary["PixelAuroc"],
            "pixelAurocNew": candidate_summary["PixelAuroc"],
            "pixelAurocDelta": round(candidate_summary["PixelAuroc"] - baseline_summary["PixelAuroc"], 6),
            "imageF1Old": baseline_image_f1,
            "imageF1New": candidate_summary["ImageF1"],
            "imageF1Delta": round(candidate_summary["ImageF1"] - baseline_image_f1, 6),
            "abReplayExecutedCandidateCases": ab_summary.get("executedCandidateCaseCount"),
            "abReplayAnomalyCases": ab_summary.get("anomalyDetectionCaseCount"),
            "abReplayAnomalyImproved": ab_summary.get("anomalyDetectionImprovedCaseCount"),
            "abReplayAnomalyDetected": ab_summary.get("anomalyDetectionDetectedAnomalyCaseCount"),
            "abReplayAnomalyImageCorrect": ab_summary.get("anomalyDetectionImageCorrectCaseCount"),
            "abReplayAnomalyRegressed": ab_summary.get("anomalyDetectionRegressedCaseCount"),
        },
        "claimBoundary": "MVTec AD Lite is public benchmark evidence for quasi-industrial tuning, not real production line sign-off.",
    }


def compute_image_f1(document: dict[str, Any]) -> float:
    images = document.get("Images") or []
    true_positive = sum(1 for image in images if image.get("IsAnomaly") and image.get("PredictedAnomaly"))
    false_positive = sum(1 for image in images if not image.get("IsAnomaly") and image.get("PredictedAnomaly"))
    false_negative = sum(1 for image in images if image.get("IsAnomaly") and not image.get("PredictedAnomaly"))
    denominator = (2 * true_positive) + false_positive + false_negative
    return 0.0 if denominator <= 0 else round((2 * true_positive) / denominator, 6)


def render_sweep_markdown(sweep: dict[str, Any]) -> str:
    lines = [
        "# AnomalyDetection MVTec AD Lite Sweep v1",
        "",
        f"GeneratedAtUtc: `{sweep['generatedAtUtc']}`",
        f"SelectedProfile: `{sweep['selectedProfile']}`",
        "",
        "| Profile | Image AUROC | Pixel AUROC | Image F1 | TP | FP | FN | Max side | Patch / stride | Runtime ms |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in sweep["profiles"]:
        lines.append(
            f"| {row['profile']} | {row['imageAuroc']:.4f} | {row['pixelAuroc']:.4f} | {row['imageF1']:.4f} | "
            f"{row['imageTruePositive']} | {row['imageFalsePositive']} | {row['imageFalseNegative']} | "
            f"{row['maxSide']} | {row['patchSize']} / {row['patchStride']} | {row['runtimeMs']:.1f} |"
        )
    lines.extend(["", sweep["selectionPolicy"], ""])
    return "\n".join(lines)


def render_taxonomy_markdown(taxonomy: dict[str, Any]) -> str:
    lines = [
        "# AnomalyDetection Failure Taxonomy v1",
        "",
        f"GeneratedAtUtc: `{taxonomy['generatedAtUtc']}`",
        "",
        "| Tag | Count |",
        "|---|---:|",
    ]
    for tag, count in taxonomy["tagCounts"].items():
        lines.append(f"| {tag} | {count} |")
    lines.extend(["", "## Top Misses", "", "| Case | Defect | Score | Taxonomy |", "|---|---|---:|---|"])
    for item in taxonomy["topMisses"]:
        lines.append(f"| {item['caseId']} | {item['defectType']} | {item['score']:.4f} | {', '.join(item['taxonomy'])} |")
    return "\n".join(lines) + "\n"


def render_improvement_markdown(improvement: dict[str, Any]) -> str:
    summary = improvement["summary"]
    lines = [
        "# Quality Flywheel AnomalyDetection Algorithm Improvement v1",
        "",
        f"GeneratedAtUtc: `{improvement['generatedAtUtc']}`",
        f"SelectedProfile: `{improvement['selectedProfile']}`",
        "",
        "| Metric | Old | New | Delta |",
        "|---|---:|---:|---:|",
        f"| Image AUROC | {summary['imageAurocOld']:.4f} | {summary['imageAurocNew']:.4f} | {summary['imageAurocDelta']:.4f} |",
        f"| Pixel AUROC | {summary['pixelAurocOld']:.4f} | {summary['pixelAurocNew']:.4f} | {summary['pixelAurocDelta']:.4f} |",
        f"| Image F1 | {summary['imageF1Old']:.4f} | {summary['imageF1New']:.4f} | {summary['imageF1Delta']:.4f} |",
        "",
        f"A/B replay anomaly score-improved: `{summary['abReplayAnomalyImproved']}` / `{summary['abReplayAnomalyCases']}`, detected/image-correct: `{summary['abReplayAnomalyDetected']}`, regressed: `{summary['abReplayAnomalyRegressed']}`.",
        "",
        improvement["claimBoundary"],
        "",
    ]
    return "\n".join(lines)


def render_audit_markdown(improvement: dict[str, Any], taxonomy: dict[str, Any]) -> str:
    summary = improvement["summary"]
    return "\n".join(
        [
            "# 第6批 AnomalyDetection 准工业算法调优报告",
            "",
            f"GeneratedAtUtc: `{improvement['generatedAtUtc']}`",
            "",
            "## Scope",
            "",
            "- Operator: `AnomalyDetection`",
            "- Dataset: `MVTec AD Lite`",
            "- Claim boundary: public benchmark quasi-industrial evidence; no real field sign-off claim.",
            "",
            "## Result",
            "",
            f"- Image AUROC: `{summary['imageAurocOld']:.4f}` -> `{summary['imageAurocNew']:.4f}`",
            f"- Pixel AUROC: `{summary['pixelAurocOld']:.4f}` -> `{summary['pixelAurocNew']:.4f}`",
            f"- A/B anomaly replay score-improved: `{summary['abReplayAnomalyImproved']}` / `{summary['abReplayAnomalyCases']}`",
            f"- A/B anomaly replay detected/image-correct: `{summary['abReplayAnomalyDetected']}`",
            f"- A/B anomaly replay regressed: `{summary['abReplayAnomalyRegressed']}`",
            f"- Remaining missed anomalies in full candidate: `{taxonomy['summary']['missedAnomalyCount']}`",
            "",
            "## Evidence",
            "",
            f"- `{repo(CANDIDATE_JSON)}`",
            f"- `{repo(SWEEP_JSON)}`",
            f"- `{repo(TAXONOMY_JSON)}`",
            f"- `{repo(IMPROVEMENT_JSON)}`",
            "",
        ]
    )


def run() -> None:
    rows: list[dict[str, Any]] = []
    for profile in PROFILES:
        output = REPORT_DIR / f"AnomalyDetection_mvtec_sweep_v1_{profile.name}.json"
        report = REPORT_DIR / f"AnomalyDetection_mvtec_sweep_v1_{profile.name}.md"
        document = run_profile(profile, output, report)
        rows.append(compact_summary(document, output))

    selected = select_candidate(rows)
    selected_profile = next(profile for profile in PROFILES if profile.name == selected["profile"])
    candidate = run_profile(selected_profile, CANDIDATE_JSON, CANDIDATE_MD, min_image_auroc=0.70, min_pixel_auroc=0.70)
    sweep = {
        "schemaVersion": "2026-04-29.anomaly-detection-sweep.v1",
        "generatedAtUtc": utc_now(),
        "accepted": True,
        "selectedProfile": selected["profile"],
        "selectionPolicy": "Select highest Image AUROC, then Pixel AUROC, Image F1, and lower runtime.",
        "profiles": sorted(rows, key=lambda item: (item["imageAuroc"], item["pixelAuroc"], item["imageF1"]), reverse=True),
    }
    taxonomy = build_taxonomy(candidate)
    improvement = build_improvement(read_json(BASELINE_JSON), candidate, sweep)

    write_json(SWEEP_JSON, sweep)
    write_text(SWEEP_MD, render_sweep_markdown(sweep))
    write_json(TAXONOMY_JSON, taxonomy)
    write_text(TAXONOMY_MD, render_taxonomy_markdown(taxonomy))
    write_json(IMPROVEMENT_JSON, improvement)
    write_text(IMPROVEMENT_MD, render_improvement_markdown(improvement))
    write_text(AUDIT_MD, render_audit_markdown(improvement, taxonomy))

    print(
        "AnomalyDetection MVTec sweep complete: "
        f"selected={sweep['selectedProfile']} "
        f"imageAuroc={candidate['Summary']['ImageAuroc']:.4f} "
        f"pixelAuroc={candidate['Summary']['PixelAuroc']:.4f} "
        f"output={repo(CANDIDATE_JSON)}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Run AnomalyDetection MVTec candidate sweep and reports.")
    parser.parse_args()
    run()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
