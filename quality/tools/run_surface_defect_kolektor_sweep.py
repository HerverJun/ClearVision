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
RUNNER_PROJECT = "quality/tools/KolektorSurfaceDefectDatasetRunner/KolektorSurfaceDefectDatasetRunner.csproj"
INDEX_PATH = "quality/datasets/kolektorsdd2_index.json"
BASELINE_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_baseline.json"
AB_REPORT = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"

SWEEP_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_sweep_v1.json"
SWEEP_MD = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_sweep_v1.md"
CANDIDATE_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_candidate_v1.json"
CANDIDATE_MD = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_candidate_v1.md"
TAXONOMY_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v1.json"
TAXONOMY_MD = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v1.md"
IMPROVEMENT_JSON = REPORT_DIR / "QualityFlywheel_surface_defect_algorithm_improvement_v1.json"
IMPROVEMENT_MD = REPORT_DIR / "QualityFlywheel_surface_defect_algorithm_improvement_v1.md"
AUDIT_MD = AUDIT_DIR / "第5批-SurfaceDefectDetection准工业算法调优报告-2026-04-29.md"


@dataclass(frozen=True)
class Profile:
    name: str
    threshold: float = 15.0
    min_area: int = 4
    max_area: int = 1_000_000
    morph_clean_size: int = 1
    morph_mode: str = "OpenClose"
    background_kernel_size: int = 31
    threshold_mode: str = "Manual"
    normalization_mode: str = "LocalMean"
    method: str = "LocalContrast"
    reference_stats_sigma: float = 2.5
    robust_reference_stats: bool = False
    response_normalize_mode: str = "RawClamp"


PROFILES = (
    Profile("baseline_default"),
    Profile("recall_floor_12_area3", threshold=12, min_area=3),
    Profile("recall_floor_10_area3", threshold=10, min_area=3),
    Profile("balanced_floor_14_area5", threshold=14, min_area=5),
    Profile("balanced_floor_14_area6", threshold=14, min_area=6),
    Profile("balanced_floor_14_area7", threshold=14, min_area=7),
    Profile("noise_guard_floor_18_area8", threshold=18, min_area=8),
    Profile("wide_background_14_area4", threshold=14, min_area=4, background_kernel_size=45),
    Profile("tight_background_12_area4", threshold=12, min_area=4, background_kernel_size=21),
    Profile("close_only_12_area3", threshold=12, min_area=3, morph_clean_size=3, morph_mode="CloseOnly"),
    Profile("otsu_local_area4", threshold=15, min_area=4, threshold_mode="Otsu"),
    Profile("gradient_percentile_stats", method="GradientMagnitude", threshold_mode="ReferenceStats", threshold=12, min_area=4, response_normalize_mode="PercentileClip"),
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


def sample_ids(records: list[dict[str, Any]], split: str, limit_positive: int, limit_negative: int) -> list[str]:
    split_records = [item for item in records if item.get("split") == split]
    positives = [item for item in split_records if item.get("is_defect") is True]
    negatives = [item for item in split_records if item.get("is_defect") is not True]
    return [
        *(item["id"] for item in even_sample(positives, limit_positive)),
        *(item["id"] for item in even_sample(negatives, limit_negative)),
    ]


def even_sample(items: list[dict[str, Any]], limit: int) -> list[dict[str, Any]]:
    if limit <= 0 or len(items) <= limit:
        return items
    if limit == 1:
        return [items[0]]
    step = (len(items) - 1) / (limit - 1)
    return [items[round(index * step)] for index in range(limit)]


def command_for(
    profile: Profile,
    output: Path,
    report: Path,
    split: str,
    case_ids: list[str],
    min_image_auroc: float,
    min_pixel_f1: float,
) -> list[str]:
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
        "--candidate-version",
        "v1",
        "--profile",
        profile.name,
        "--split",
        split,
        "--max-side",
        "256",
        "--method",
        profile.method,
        "--threshold-mode",
        profile.threshold_mode,
        "--normalization-mode",
        profile.normalization_mode,
        "--threshold",
        str(profile.threshold),
        "--min-area",
        str(profile.min_area),
        "--max-area",
        str(profile.max_area),
        "--morph-clean-size",
        str(profile.morph_clean_size),
        "--morph-mode",
        profile.morph_mode,
        "--background-kernel-size",
        str(profile.background_kernel_size),
        "--reference-stats-sigma",
        str(profile.reference_stats_sigma),
        "--robust-reference-stats",
        str(profile.robust_reference_stats).lower(),
        "--response-normalize-mode",
        profile.response_normalize_mode,
        "--pixel-sample-stride",
        "4",
        "--min-image-auroc",
        str(min_image_auroc),
        "--min-pixel-f1",
        str(min_pixel_f1),
    ]
    if case_ids:
        command.extend(["--case-ids", ",".join(case_ids)])
    return command


def run_profile(
    profile: Profile,
    output: Path,
    report: Path,
    split: str,
    case_ids: list[str],
    min_image_auroc: float = 0.0,
    min_pixel_f1: float = 0.0,
) -> dict[str, Any]:
    completed = subprocess.run(command_for(profile, output, report, split, case_ids, min_image_auroc, min_pixel_f1), cwd=REPO_ROOT, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        raise SystemExit(
            f"error: SurfaceDefectDetection sweep profile failed: {profile.name}\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return read_json(output)


def score_summary(summary: dict[str, Any]) -> float:
    pixel_f1 = float(summary.get("PixelF1") or 0)
    image_f1 = float(summary.get("ImageF1") or 0)
    image_auroc = float(summary.get("ImageAuroc") or 0)
    pixel_auroc = float(summary.get("PixelAuroc") or 0)
    false_positive = float(summary.get("FalsePositivePerImage") or 0)
    runtime = float(summary.get("RuntimeMsP95") or 0)
    return round((pixel_f1 * 3.0) + image_f1 + (image_auroc * 0.75) + (pixel_auroc * 0.35) - (false_positive * 0.4) - (runtime * 0.002), 9)


def compact_summary(document: dict[str, Any], source: Path) -> dict[str, Any]:
    summary = document["Summary"]
    image_f1 = summary.get("ImageF1")
    if image_f1 is None:
        confusion = document.get("ImageConfusion", {})
        true_positive = int(confusion.get("TruePositive") or 0)
        false_positive = int(confusion.get("FalsePositive") or 0)
        false_negative = int(confusion.get("FalseNegative") or 0)
        denominator = (2 * true_positive) + false_positive + false_negative
        image_f1 = 0.0 if denominator <= 0 else (2 * true_positive) / denominator
    return {
        "sourceReport": repo(source),
        "profile": summary.get("ProfileName") or "baseline_default",
        "caseCount": summary["CaseCount"],
        "pixelF1": summary["PixelF1"],
        "imageAuroc": summary["ImageAuroc"],
        "pixelAuroc": summary["PixelAuroc"],
        "imageF1": image_f1,
        "falsePositivePerImage": summary["FalsePositivePerImage"],
        "runtimeMsP95": summary["RuntimeMsP95"],
        "score": score_summary(summary),
        "parameters": {
            "Method": summary.get("Method"),
            "ThresholdMode": summary.get("ThresholdMode"),
            "Threshold": summary.get("Threshold"),
            "MinArea": summary.get("MinArea"),
            "MorphCleanSize": summary.get("MorphCleanSize"),
            "MorphMode": summary.get("MorphMode"),
            "BackgroundKernelSize": summary.get("BackgroundKernelSize"),
            "ResponseNormalizeMode": summary.get("ResponseNormalizeMode"),
        },
    }


def build_taxonomy(candidate: dict[str, Any]) -> dict[str, Any]:
    items: list[dict[str, Any]] = []
    counter: Counter[str] = Counter()
    for image in candidate.get("Images", []):
        taxonomy = [str(item) for item in image.get("FailureTaxonomy", [])]
        if not taxonomy:
            continue
        for label in taxonomy:
            counter[label] += 1
        totals = image.get("PixelTotals", {})
        items.append(
            {
                "caseId": image["Id"],
                "isDefect": image["IsDefect"],
                "predictedDefect": image["PredictedDefect"],
                "imageCorrect": image.get("ImageCorrect"),
                "taxonomy": taxonomy,
                "pixelF1": totals.get("F1"),
                "falsePositivePixels": totals.get("FalsePositive"),
                "falseNegativePixels": totals.get("FalseNegative"),
                "defectArea": image.get("DefectArea"),
                "score": image.get("Score"),
                "nextAction": next_action(taxonomy[0]),
            }
        )
    return {
        "schemaVersion": "2026-04-29.surface-defect-failure-taxonomy.v1",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "准工业公开 KolektorSDD2 failure taxonomy；不是真实产线签核。",
        "sourceReport": repo(CANDIDATE_JSON),
        "summary": {
            "caseCount": len(items),
            "taxonomyCounts": dict(counter),
        },
        "items": sorted(items, key=lambda item: (item["taxonomy"][0], item["caseId"])),
    }


def next_action(label: str) -> str:
    return {
        "texture_noise_false_positive": "Raise area/noise guard or add connected-component shape filtering while preserving defect recall.",
        "oversegmentation_false_positive": "Tune morphology and max-area handling; inspect whether broad response bands should be suppressed.",
        "small_defect_miss": "Use lower local-contrast floor on validation positives and protect with replay false-positive gate.",
        "low_contrast_defect_miss": "Compare local background kernel sizes and response normalization for low-contrast positives.",
        "undersegmentation_false_negative": "Relax threshold or morphology only for validation positives with stable false-positive budget.",
        "mask_overgrowth_false_positive": "Tighten cleanup or postprocess boundaries; prefer shape filters over global threshold increases.",
        "mask_boundary_mismatch": "Inspect mask boundary tolerance and postprocess connected components before changing detector response.",
        "execution_error": "Fix runner/operator crash before metric tuning.",
    }.get(label, "Inspect image diagnostics and assign a more specific failure action.")


def surface_row_from_ab() -> dict[str, Any] | None:
    if not AB_REPORT.exists():
        return None
    report = read_json(AB_REPORT)
    for row in report.get("operators", []):
        if row.get("operator") == "SurfaceDefectDetection":
            return row
    return None


def build_improvement_report(sweep: dict[str, Any], taxonomy: dict[str, Any]) -> dict[str, Any]:
    candidate = read_json(CANDIDATE_JSON)
    baseline = read_json(BASELINE_JSON)
    ab_row = surface_row_from_ab()
    candidate_summary = compact_summary(candidate, CANDIDATE_JSON)
    baseline_summary = compact_summary(baseline, BASELINE_JSON)
    return {
        "schemaVersion": "2026-04-29.surface-defect-algorithm-improvement.v1",
        "generatedAtUtc": utc_now(),
        "accepted": candidate_summary["pixelF1"] >= baseline_summary["pixelF1"] and candidate_summary["imageAuroc"] >= 0.70,
        "claimBoundary": "准工业公开/替代证明；不声明真实产线工业验证完成。",
        "sourceReports": [
            repo(BASELINE_JSON),
            repo(CANDIDATE_JSON),
            repo(SWEEP_JSON),
            repo(TAXONOMY_JSON),
            repo(AB_REPORT),
        ],
        "baseline": baseline_summary,
        "candidate": candidate_summary,
        "sweep": sweep["summary"],
        "taxonomySummary": taxonomy["summary"],
        "abReplay": {
            "status": ab_row.get("comparisonStatus") if ab_row else "not-yet-run",
            "replayCaseCount": ab_row.get("replayCaseCount") if ab_row else 0,
            "improvedMetricCaseCount": ab_row.get("improvedMetricCaseCount") if ab_row else 0,
            "regressedCaseCount": ab_row.get("regressedCaseCount") if ab_row else 0,
            "worseMetricCaseCount": ab_row.get("worseMetricCaseCount") if ab_row else 0,
            "candidateBaseline": ab_row.get("candidateBaseline") if ab_row else None,
        },
        "nextActions": [
            "Keep v1 profile as the replay-gated SurfaceDefectDetection candidate.",
            "Next tuning should target residual low-contrast misses and undersegmentation before lowering global thresholds further.",
            "Move AnomalyDetection into candidate execution after this SurfaceDefectDetection evidence chain stays green.",
        ],
    }


def render_sweep_markdown(sweep: dict[str, Any]) -> str:
    lines = [
        "# SurfaceDefectDetection KolektorSDD2 Sweep v1",
        "",
        f"GeneratedAtUtc: `{sweep['generatedAtUtc']}`",
        f"SelectedProfile: `{sweep['summary']['selectedProfile']}`",
        "",
        "| Profile | Split | Cases | Pixel F1 | Image AUROC | Image F1 | FP/normal | P95 ms | Score |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in sweep["profiles"]:
        lines.append(
            f"| {row['profile']} | {row['split']} | {row['caseCount']} | {row['pixelF1']:.4f} | "
            f"{row['imageAuroc']:.4f} | {row['imageF1']:.4f} | {row['falsePositivePerImage']:.4f} | "
            f"{row['runtimeMsP95']:.3f} | {row['score']:.4f} |"
        )
    lines.append("")
    return "\n".join(lines)


def render_taxonomy_markdown(taxonomy: dict[str, Any]) -> str:
    lines = [
        "# SurfaceDefectDetection KolektorSDD2 Failure Taxonomy v1",
        "",
        f"GeneratedAtUtc: `{taxonomy['generatedAtUtc']}`",
        "",
        "## Summary",
        "",
        "| Taxonomy | Count |",
        "|---|---:|",
    ]
    for label, count in sorted(taxonomy["summary"]["taxonomyCounts"].items()):
        lines.append(f"| {label} | {count} |")
    lines.extend([
        "",
        "## Cases",
        "",
        "| Case | Is defect | Predicted | Pixel F1 | FP px | FN px | Taxonomy | Next action |",
        "|---|---|---|---:|---:|---:|---|---|",
    ])
    for item in taxonomy["items"][:80]:
        lines.append(
            f"| {item['caseId']} | {item['isDefect']} | {item['predictedDefect']} | "
            f"{item['pixelF1']:.4f} | {item['falsePositivePixels']} | {item['falseNegativePixels']} | "
            f"{', '.join(item['taxonomy'])} | {item['nextAction']} |"
        )
    lines.append("")
    return "\n".join(lines)


def render_improvement_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel SurfaceDefectDetection Improvement v1",
        "",
        f"GeneratedAtUtc: `{report['generatedAtUtc']}`",
        f"Accepted: `{report['accepted']}`",
        "",
        "## Result",
        "",
        "| Metric | Baseline | Candidate |",
        "|---|---:|---:|",
        f"| Pixel F1 | {report['baseline']['pixelF1']:.4f} | {report['candidate']['pixelF1']:.4f} |",
        f"| Image AUROC | {report['baseline']['imageAuroc']:.4f} | {report['candidate']['imageAuroc']:.4f} |",
        f"| Image F1 | {report['baseline']['imageF1']:.4f} | {report['candidate']['imageF1']:.4f} |",
        f"| FP/normal | {report['baseline']['falsePositivePerImage']:.4f} | {report['candidate']['falsePositivePerImage']:.4f} |",
        "",
        "## A/B Replay",
        "",
        f"- Status: `{report['abReplay']['status']}`",
        f"- Replay cases: `{report['abReplay']['replayCaseCount']}`",
        f"- Improved metric cases: `{report['abReplay']['improvedMetricCaseCount']}`",
        f"- Regressed cases: `{report['abReplay']['regressedCaseCount']}`",
        f"- Worse metric cases: `{report['abReplay']['worseMetricCaseCount']}`",
        "",
        "## Taxonomy",
        "",
        "| Taxonomy | Count |",
        "|---|---:|",
    ]
    for label, count in sorted(report["taxonomySummary"]["taxonomyCounts"].items()):
        lines.append(f"| {label} | {count} |")
    lines.extend(["", "## Next Actions", ""])
    lines.extend(f"- {item}" for item in report["nextActions"])
    lines.append("")
    return "\n".join(lines)


def render_audit_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# 第5批 SurfaceDefectDetection 准工业算法调优报告",
        "",
        f"**生成时间**：{report['generatedAtUtc']}",
        "",
        "## 1. 结论",
        "",
        "本轮把 SurfaceDefectDetection 接入 executable candidate replay，并在 KolektorSDD2 上形成 validation sweep、test candidate、failure taxonomy 与 A/B replay 证据链。报告只声明准工业公开/替代证明，不声明真实产线签核。",
        "",
        "## 2. 结果",
        "",
        "| Metric | Baseline | Candidate |",
        "|---|---:|---:|",
        f"| Pixel F1 | {report['baseline']['pixelF1']:.4f} | {report['candidate']['pixelF1']:.4f} |",
        f"| Image AUROC | {report['baseline']['imageAuroc']:.4f} | {report['candidate']['imageAuroc']:.4f} |",
        f"| Image F1 | {report['baseline']['imageF1']:.4f} | {report['candidate']['imageF1']:.4f} |",
        f"| FP/normal | {report['baseline']['falsePositivePerImage']:.4f} | {report['candidate']['falsePositivePerImage']:.4f} |",
        "",
        "## 3. A/B replay",
        "",
        f"- Replay cases: {report['abReplay']['replayCaseCount']}",
        f"- Improved metric cases: {report['abReplay']['improvedMetricCaseCount']}",
        f"- Regressed cases: {report['abReplay']['regressedCaseCount']}",
        f"- Worse metric cases: {report['abReplay']['worseMetricCaseCount']}",
        "",
        "## 4. 失败 taxonomy",
        "",
        "| Taxonomy | Count |",
        "|---|---:|",
    ]
    for label, count in sorted(report["taxonomySummary"]["taxonomyCounts"].items()):
        lines.append(f"| {label} | {count} |")
    lines.extend([
        "",
        "## 5. 证据文件",
        "",
    ])
    lines.extend(f"- `{source}`" for source in report["sourceReports"])
    lines.append("")
    return "\n".join(lines)


def run_sweep() -> dict[str, Any]:
    index = read_json(REPO_ROOT / INDEX_PATH)
    validation_ids = sample_ids(index["records"], "train", limit_positive=120, limit_negative=360)
    profiles: list[dict[str, Any]] = []
    for profile in PROFILES:
        output = REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_sweep_v1_{profile.name}.json"
        report = REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_sweep_v1_{profile.name}.md"
        document = run_profile(profile, output, report, "train", validation_ids)
        row = compact_summary(document, output)
        row["split"] = "train-validation"
        profiles.append(row)

    baseline = next(row for row in profiles if row["profile"] == "baseline_default")
    safe_profiles = [
        row for row in profiles
        if row["pixelF1"] >= baseline["pixelF1"]
        and row["imageAuroc"] >= baseline["imageAuroc"] - 0.01
        and row["falsePositivePerImage"] <= baseline["falsePositivePerImage"]
    ]
    selection_pool = safe_profiles or profiles
    selected = max(selection_pool, key=lambda row: (row["score"], row["pixelF1"], row["imageAuroc"], -row["falsePositivePerImage"]))
    selected_profile = next(profile for profile in PROFILES if profile.name == selected["profile"])
    candidate = run_profile(selected_profile, CANDIDATE_JSON, CANDIDATE_MD, "test", [], min_image_auroc=0.70, min_pixel_f1=0.20)
    candidate_summary = compact_summary(candidate, CANDIDATE_JSON)
    candidate_summary["split"] = "test"

    sweep = {
        "schemaVersion": "2026-04-29.surface-defect-kolektor-sweep.v1",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "准工业公开 KolektorSDD2 validation/test sweep；不是真实产线签核。",
        "sourceBaseline": repo(BASELINE_JSON),
        "summary": {
            "profileCount": len(profiles),
            "validationCaseCount": len(validation_ids),
            "selectedProfile": selected["profile"],
            "selectedValidationScore": selected["score"],
            "selectionPolicy": "Prefer validation profiles that improve PixelF1 without increasing false positives per normal image, then rank by composite score.",
            "candidatePixelF1": candidate_summary["pixelF1"],
            "candidateImageAuroc": candidate_summary["imageAuroc"],
            "candidateImageF1": candidate_summary["imageF1"],
        },
        "profiles": profiles,
        "candidate": candidate_summary,
    }
    write_json(SWEEP_JSON, sweep)
    write_text(SWEEP_MD, render_sweep_markdown(sweep))
    return sweep


def write_reports(sweep: dict[str, Any]) -> None:
    taxonomy = build_taxonomy(read_json(CANDIDATE_JSON))
    write_json(TAXONOMY_JSON, taxonomy)
    write_text(TAXONOMY_MD, render_taxonomy_markdown(taxonomy))
    improvement = build_improvement_report(sweep, taxonomy)
    write_json(IMPROVEMENT_JSON, improvement)
    write_text(IMPROVEMENT_MD, render_improvement_markdown(improvement))
    write_text(AUDIT_MD, render_audit_markdown(improvement))
    print(
        "SurfaceDefectDetection KolektorSDD2 sweep/report complete: "
        f"profile={improvement['candidate']['profile']} "
        f"pixelF1={improvement['candidate']['pixelF1']:.4f} "
        f"imageAuroc={improvement['candidate']['imageAuroc']:.4f} "
        f"taxonomyCases={taxonomy['summary']['caseCount']} "
        f"output={repo(IMPROVEMENT_JSON)}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Run SurfaceDefectDetection KolektorSDD2 candidate sweep and reports.")
    parser.add_argument("--report-only", action="store_true", help="Do not rerun sweep; rebuild taxonomy/improvement/audit reports from existing candidate output.")
    args = parser.parse_args()

    sweep = read_json(SWEEP_JSON) if args.report_only else run_sweep()
    write_reports(sweep)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
