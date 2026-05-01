from __future__ import annotations

import argparse
import csv
import json
import os
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
RUNNER_DLL = REPO_ROOT / "quality" / "tools" / "KolektorSurfaceDefectDatasetRunner" / "bin" / "Debug" / "net8.0" / "KolektorSurfaceDefectDatasetRunner.dll"
INDEX_PATH = "quality/datasets/kolektorsdd2_index.json"
BASELINE_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_baseline.json"
AB_REPORT = REPORT_DIR / "QualityFlywheel_algorithm_ab_replay_report.json"

SWEEP_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_sweep_v2.json"
SWEEP_MD = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_sweep_v2.md"
CANDIDATE_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_candidate_v2.json"
CANDIDATE_MD = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_candidate_v2.md"
TAXONOMY_JSON = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v2.json"
TAXONOMY_MD = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_failure_taxonomy_v2.md"
COMPONENT_TELEMETRY_CSV = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_component_telemetry_v2.csv"
COMPONENT_DISTRIBUTION_CSV = REPORT_DIR / "SurfaceDefectDetection_kolektorsdd2_component_distribution_v2.csv"
RULE_SELECTOR_JSON = REPORT_DIR / "QualityFlywheel_surface_defect_component_rule_selector_v2.json"
RULE_SELECTOR_MD = REPORT_DIR / "QualityFlywheel_surface_defect_component_rule_selector_v2.md"
IMPROVEMENT_JSON = REPORT_DIR / "QualityFlywheel_surface_defect_algorithm_improvement_v2.json"
IMPROVEMENT_MD = REPORT_DIR / "QualityFlywheel_surface_defect_algorithm_improvement_v2.md"
AUDIT_MD = AUDIT_DIR / "第5批-SurfaceDefectDetection准工业算法调优报告-2026-04-29.md"

BASELINE_THRESHOLD = 15.0
SURFACE_CANDIDATE_VERSION = "v2"
TARGET_TAXONOMY = (
    "texture_noise_false_positive",
    "low_contrast_defect_miss",
    "undersegmentation_false_negative",
)


@dataclass(frozen=True)
class Profile:
    name: str
    threshold: float = BASELINE_THRESHOLD
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
    clahe_clip_limit: float = 2.0
    clahe_tile_grid_size: int = 8
    component_filter_mode: str = "AreaOnly"
    small_noise_area_max: int = 0
    min_elongation_for_small_component: float = 0.0
    compact_noise_area_max: int = 0
    compact_noise_circularity_min: float = 0.0
    compact_noise_fill_ratio_min: float = 0.0
    min_local_response_prominence: float = 0.0
    target_taxonomy: tuple[str, ...] = TARGET_TAXONOMY


PROFILES = (
    Profile("baseline_default"),
    Profile("texture_noise_shape_response_area6", min_area=6, normalization_mode="ClaheLocalMean", clahe_clip_limit=1.5, component_filter_mode="ShapeAndResponseStats", small_noise_area_max=32, min_elongation_for_small_component=2.5, compact_noise_area_max=64, compact_noise_circularity_min=0.68, compact_noise_fill_ratio_min=0.45, min_local_response_prominence=4.0, target_taxonomy=("texture_noise_false_positive",)),
    Profile("texture_noise_compact_circularity_area8", threshold=18, min_area=8, component_filter_mode="ShapeAndResponseStats", small_noise_area_max=48, min_elongation_for_small_component=2.5, compact_noise_area_max=96, compact_noise_circularity_min=0.62, compact_noise_fill_ratio_min=0.40, min_local_response_prominence=3.0, target_taxonomy=("texture_noise_false_positive",)),
    Profile("texture_noise_prominence_guard", min_area=4, normalization_mode="ClaheLocalMean", clahe_clip_limit=1.5, component_filter_mode="ShapeAndResponseStats", compact_noise_area_max=80, compact_noise_circularity_min=0.55, compact_noise_fill_ratio_min=0.35, min_local_response_prominence=6.0, target_taxonomy=("texture_noise_false_positive",)),
    Profile("low_contrast_clahe_local_mean", normalization_mode="ClaheLocalMean", clahe_clip_limit=1.5, clahe_tile_grid_size=8, target_taxonomy=("low_contrast_defect_miss",)),
    Profile("low_contrast_clahe_percentile_stats", normalization_mode="ClaheLocalMean", response_normalize_mode="PercentileClip", component_filter_mode="ResponseStats", clahe_clip_limit=2.0, clahe_tile_grid_size=12, target_taxonomy=("low_contrast_defect_miss",)),
    Profile("undersegmentation_closeopen_kernel3", morph_clean_size=3, morph_mode="CloseOpen", background_kernel_size=21, target_taxonomy=("undersegmentation_false_negative",)),
    Profile("undersegmentation_closeonly_kernel3", morph_clean_size=3, morph_mode="CloseOnly", background_kernel_size=21, component_filter_mode="ResponseStats", target_taxonomy=("undersegmentation_false_negative",)),
    Profile("targeted_combined_v2", min_area=6, normalization_mode="ClaheLocalMean", component_filter_mode="ShapeAndResponseStats", small_noise_area_max=32, min_elongation_for_small_component=2.5, compact_noise_area_max=64, compact_noise_circularity_min=0.68, compact_noise_fill_ratio_min=0.45, min_local_response_prominence=4.0, clahe_clip_limit=1.5, clahe_tile_grid_size=8),
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def dotnet_env() -> dict[str, str]:
    dotnet_home = REPO_ROOT / ".tmp" / "dotnet-cli-home"
    dotnet_home.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env.setdefault("DOTNET_CLI_HOME", str(dotnet_home))
    env.setdefault("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
    return env


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


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
        "--no-build",
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
        SURFACE_CANDIDATE_VERSION,
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
        "--clahe-clip-limit",
        str(profile.clahe_clip_limit),
        "--clahe-tile-grid-size",
        str(profile.clahe_tile_grid_size),
        "--component-filter-mode",
        profile.component_filter_mode,
        "--small-noise-area-max",
        str(profile.small_noise_area_max),
        "--min-elongation-for-small-component",
        str(profile.min_elongation_for_small_component),
        "--compact-noise-area-max",
        str(profile.compact_noise_area_max),
        "--compact-noise-circularity-min",
        str(profile.compact_noise_circularity_min),
        "--compact-noise-fill-ratio-min",
        str(profile.compact_noise_fill_ratio_min),
        "--min-local-response-prominence",
        str(profile.min_local_response_prominence),
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
    completed = subprocess.run(
        command_for(profile, output, report, split, case_ids, min_image_auroc, min_pixel_f1),
        cwd=REPO_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
        env=dotnet_env(),
    )
    if completed.returncode != 0:
        raise SystemExit(
            f"error: SurfaceDefectDetection sweep profile failed: {profile.name}\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )
    return read_json(output)


def ensure_runner_built() -> None:
    if RUNNER_DLL.exists():
        return

    env = os.environ.copy()
    env.setdefault("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
    completed = subprocess.run(
        ["dotnet", "build", RUNNER_PROJECT, "-v", "minimal", "--no-restore"],
        cwd=REPO_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
        env=env,
    )
    if completed.returncode != 0:
        raise SystemExit(
            "error: failed to build KolektorSurfaceDefectDatasetRunner\n"
            f"{completed.stdout}\n{completed.stderr}".strip()
        )


def score_summary(summary: dict[str, Any]) -> float:
    pixel_f1 = float(summary.get("PixelF1") or 0)
    image_f1 = float(summary.get("ImageF1") or 0)
    image_auroc = float(summary.get("ImageAuroc") or 0)
    pixel_auroc = float(summary.get("PixelAuroc") or 0)
    false_positive = float(summary.get("FalsePositivePerImage") or 0)
    runtime = float(summary.get("RuntimeMsP95") or 0)
    return round((pixel_f1 * 3.0) + image_f1 + (image_auroc * 0.75) + (pixel_auroc * 0.35) - (false_positive * 0.4) - (runtime * 0.002), 9)


def taxonomy_counts(document: dict[str, Any]) -> dict[str, int]:
    counter: Counter[str] = Counter()
    for image in document.get("Images", []):
        counter.update(str(item) for item in image.get("FailureTaxonomy", []))
    return {label: counter.get(label, 0) for label in TARGET_TAXONOMY}


def target_taxonomy_total(document: dict[str, Any]) -> int:
    counts = taxonomy_counts(document)
    return sum(counts.values())


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
    target_counts = taxonomy_counts(document)
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
        "targetTaxonomyCounts": target_counts,
        "targetTaxonomyCaseCount": sum(target_counts.values()),
        "parameters": {
            "Method": summary.get("Method"),
            "ThresholdMode": summary.get("ThresholdMode"),
            "Threshold": summary.get("Threshold"),
            "MinArea": summary.get("MinArea"),
            "MorphCleanSize": summary.get("MorphCleanSize"),
            "MorphMode": summary.get("MorphMode"),
            "BackgroundKernelSize": summary.get("BackgroundKernelSize"),
            "ResponseNormalizeMode": summary.get("ResponseNormalizeMode"),
            "ClaheClipLimit": summary.get("ClaheClipLimit"),
            "ClaheTileGridSize": summary.get("ClaheTileGridSize"),
            "ComponentFilterMode": summary.get("ComponentFilterMode"),
            "SmallNoiseAreaMax": summary.get("SmallNoiseAreaMax"),
            "MinElongationForSmallComponent": summary.get("MinElongationForSmallComponent"),
            "CompactNoiseAreaMax": summary.get("CompactNoiseAreaMax"),
            "CompactNoiseCircularityMin": summary.get("CompactNoiseCircularityMin"),
            "CompactNoiseFillRatioMin": summary.get("CompactNoiseFillRatioMin"),
            "MinLocalResponseProminence": summary.get("MinLocalResponseProminence"),
        },
    }


COMPONENT_TELEMETRY_FIELDS = [
    "profile",
    "split",
    "caseId",
    "isDefect",
    "predictedDefect",
    "imageTaxonomy",
    "imagePixelF1",
    "source",
    "kind",
    "componentIndex",
    "area",
    "pixelArea",
    "elongation",
    "fillRatio",
    "circularity",
    "componentMean",
    "componentPeak",
    "ringMean",
    "ringProminence",
    "overlapPixels",
    "truePositivePixels",
    "falsePositivePixels",
    "falseNegativePixels",
    "rectX",
    "rectY",
    "rectWidth",
    "rectHeight",
    "sourceReport",
]

COMPONENT_DISTRIBUTION_FIELDS = [
    "profile",
    "split",
    "kind",
    "source",
    "metric",
    "count",
    "min",
    "p10",
    "p25",
    "median",
    "p75",
    "p90",
    "max",
    "mean",
]

COMPONENT_METRICS = [
    "area",
    "pixelArea",
    "elongation",
    "fillRatio",
    "circularity",
    "componentMean",
    "componentPeak",
    "ringProminence",
]


def build_component_tables(sweep: dict[str, Any]) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    for profile in sweep.get("profiles", []):
        source_report = REPO_ROOT / profile["sourceReport"]
        if not source_report.exists():
            continue
        document = read_json(source_report)
        rows.extend(component_rows_from_document(document, profile["profile"], profile.get("split", ""), source_report))

    candidate_source = REPO_ROOT / sweep["candidate"]["sourceReport"]
    if candidate_source.exists():
        candidate_document = read_json(candidate_source)
        rows.extend(component_rows_from_document(candidate_document, sweep["candidate"]["profile"], "test", candidate_source))

    distribution = build_component_distribution(rows)
    write_csv(COMPONENT_TELEMETRY_CSV, rows, COMPONENT_TELEMETRY_FIELDS)
    write_csv(COMPONENT_DISTRIBUTION_CSV, distribution, COMPONENT_DISTRIBUTION_FIELDS)
    return {
        "telemetryCsv": repo(COMPONENT_TELEMETRY_CSV),
        "distributionCsv": repo(COMPONENT_DISTRIBUTION_CSV),
        "rowCount": len(rows),
        "distributionRowCount": len(distribution),
    }


def component_rows_from_document(document: dict[str, Any], profile: str, split: str, source_report: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for image in document.get("Images", []):
        totals = image.get("PixelTotals", {})
        taxonomy = ",".join(str(item) for item in image.get("FailureTaxonomy", []))
        for component in image.get("Components", []):
            rows.append(
                {
                    "profile": profile,
                    "split": split,
                    "caseId": image.get("Id"),
                    "isDefect": image.get("IsDefect"),
                    "predictedDefect": image.get("PredictedDefect"),
                    "imageTaxonomy": taxonomy,
                    "imagePixelF1": totals.get("F1"),
                    "source": component.get("Source"),
                    "kind": component.get("Kind"),
                    "componentIndex": component.get("ComponentIndex"),
                    "area": component.get("Area"),
                    "pixelArea": component.get("PixelArea"),
                    "elongation": component.get("Elongation"),
                    "fillRatio": component.get("FillRatio"),
                    "circularity": component.get("Circularity"),
                    "componentMean": component.get("ComponentMean"),
                    "componentPeak": component.get("ComponentPeak"),
                    "ringMean": component.get("RingMean"),
                    "ringProminence": component.get("RingProminence"),
                    "overlapPixels": component.get("OverlapPixels"),
                    "truePositivePixels": component.get("TruePositivePixels"),
                    "falsePositivePixels": component.get("FalsePositivePixels"),
                    "falseNegativePixels": component.get("FalseNegativePixels"),
                    "rectX": component.get("RectX"),
                    "rectY": component.get("RectY"),
                    "rectWidth": component.get("RectWidth"),
                    "rectHeight": component.get("RectHeight"),
                    "sourceReport": repo(source_report),
                }
            )
    return rows


def build_component_distribution(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str, str, str, str], list[float]] = {}
    for row in rows:
        for metric in COMPONENT_METRICS:
            value = to_float(row.get(metric))
            if value is None:
                continue
            key = (
                str(row.get("profile") or ""),
                str(row.get("split") or ""),
                str(row.get("kind") or ""),
                str(row.get("source") or ""),
                metric,
            )
            grouped.setdefault(key, []).append(value)

    distribution: list[dict[str, Any]] = []
    for (profile, split, kind, source, metric), values in sorted(grouped.items()):
        values.sort()
        distribution.append(
            {
                "profile": profile,
                "split": split,
                "kind": kind,
                "source": source,
                "metric": metric,
                "count": len(values),
                "min": round(values[0], 6),
                "p10": round(percentile(values, 0.10), 6),
                "p25": round(percentile(values, 0.25), 6),
                "median": round(percentile(values, 0.50), 6),
                "p75": round(percentile(values, 0.75), 6),
                "p90": round(percentile(values, 0.90), 6),
                "max": round(values[-1], 6),
                "mean": round(sum(values) / len(values), 6),
            }
        )
    return distribution


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    if len(values) == 1:
        return values[0]
    index = (len(values) - 1) * fraction
    lower = int(index)
    upper = min(lower + 1, len(values) - 1)
    weight = index - lower
    return (values[lower] * (1.0 - weight)) + (values[upper] * weight)


def to_float(value: Any) -> float | None:
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def build_component_rule_selector(sweep: dict[str, Any]) -> dict[str, Any]:
    baseline_row = next(row for row in sweep["profiles"] if row["profile"] == "baseline_default")
    baseline_document = read_json(REPO_ROOT / baseline_row["sourceReport"])
    baseline_counts = taxonomy_counts(baseline_document)
    baseline_pixel_f1 = float(baseline_row["pixelF1"])

    candidates = []
    for area_max in (8, 12, 16, 24, 32, 48, 64, 96):
        for circularity_min in (0.55, 0.60, 0.65, 0.70, 0.75, 0.80):
            for fill_ratio_min in (0.35, 0.40, 0.45, 0.50, 0.55, 0.60):
                for elongation_max in (1.25, 1.50, 2.00, 2.50, 3.00):
                    for ring_prominence_max in (2.0, 4.0, 6.0, 8.0, 12.0, 16.0, 24.0, None):
                        rule = {
                            "areaMax": area_max,
                            "circularityMin": circularity_min,
                            "fillRatioMin": fill_ratio_min,
                            "elongationMax": elongation_max,
                            "ringProminenceMax": ring_prominence_max,
                        }
                        candidates.append(evaluate_component_rule(baseline_document, baseline_row, baseline_counts, baseline_pixel_f1, rule))

    accepted = [item for item in candidates if item["accepted"]]
    selected = None
    if accepted:
        selected = min(
            accepted,
            key=lambda item: (
                item["taxonomyCounts"]["texture_noise_false_positive"],
                item["rejectedTruePositiveComponents"],
                -item["pixelF1"],
                item["rejectedFalsePositiveComponents"],
            ),
        )

    ranked = sorted(
        candidates,
        key=lambda item: (
            not item["accepted"],
            item["taxonomyCounts"]["texture_noise_false_positive"],
            item["taxonomyCounts"]["low_contrast_defect_miss"],
            -item["pixelF1"],
            item["rejectedTruePositiveComponents"],
        ),
    )

    report = {
        "schemaVersion": "2026-05-01.surface-defect-component-rule-selector.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "KolektorSDD2 validation-only component rule selector; no product default change.",
        "sourceReport": baseline_row["sourceReport"],
        "telemetryCsv": repo(COMPONENT_TELEMETRY_CSV),
        "distributionCsv": repo(COMPONENT_DISTRIBUTION_CSV),
        "promotionGate": {
            "textureNoiseFalsePositiveMustDecrease": True,
            "lowContrastDefectMissMustNotIncrease": True,
            "pixelF1MustNotDropBelowBaseline": True,
        },
        "baseline": {
            "profile": baseline_row["profile"],
            "pixelF1": baseline_pixel_f1,
            "targetTaxonomyCounts": baseline_counts,
        },
        "status": "accepted-rule-found" if selected else "hold-no-rule-met-fixed-gate",
        "selectedRule": selected,
        "acceptedRuleCount": len(accepted),
        "evaluatedRuleCount": len(candidates),
        "topRules": ranked[:25],
    }
    write_json(RULE_SELECTOR_JSON, report)
    write_text(RULE_SELECTOR_MD, render_rule_selector_markdown(report))
    return report


def evaluate_component_rule(
    baseline_document: dict[str, Any],
    baseline_row: dict[str, Any],
    baseline_counts: dict[str, int],
    baseline_pixel_f1: float,
    rule: dict[str, Any],
) -> dict[str, Any]:
    true_positive = 0
    false_positive = 0
    false_negative = 0
    true_negative = 0
    taxonomy_counter: Counter[str] = Counter()
    rejected_true_positive_components = 0
    rejected_false_positive_components = 0
    rejected_component_count = 0

    for image in baseline_document.get("Images", []):
        totals = image.get("PixelTotals", {})
        tp = int(totals.get("TruePositive") or 0)
        fp = int(totals.get("FalsePositive") or 0)
        fn = int(totals.get("FalseNegative") or 0)
        tn = int(totals.get("TrueNegative") or 0)
        kept_components = []
        rejected_area = 0.0

        for component in image.get("Components", []):
            if component.get("Source") != "predicted":
                continue
            if component_rule_rejects(component, rule):
                rejected_component_count += 1
                rejected_area += float(component.get("Area") or 0.0)
                component_tp = int(component.get("TruePositivePixels") or 0)
                component_fp = int(component.get("FalsePositivePixels") or 0)
                tp -= component_tp
                fp -= component_fp
                fn += component_tp
                tn += component_fp
                if component.get("Kind") == "true_positive":
                    rejected_true_positive_components += 1
                elif component.get("Kind") == "false_positive":
                    rejected_false_positive_components += 1
                continue
            kept_components.append(component)

        tp = max(0, tp)
        fp = max(0, fp)
        fn = max(0, fn)
        tn = max(0, tn)
        true_positive += tp
        false_positive += fp
        false_negative += fn
        true_negative += tn

        defect_count = len(kept_components)
        defect_area = sum(float(component.get("Area") or 0.0) for component in kept_components)
        predicted_defect = defect_count > 0 or (tp + fp) > 0
        labels = classify_simulated_image(
            bool(image.get("IsDefect")),
            predicted_defect,
            tp,
            fp,
            fn,
            defect_count,
            defect_area,
        )
        taxonomy_counter.update(labels)

    pixel_f1 = pixel_f1_from_counts(true_positive, false_positive, false_negative)
    target_counts = {label: taxonomy_counter.get(label, 0) for label in TARGET_TAXONOMY}
    accepted = (
        target_counts["texture_noise_false_positive"] < baseline_counts["texture_noise_false_positive"]
        and target_counts["low_contrast_defect_miss"] <= baseline_counts["low_contrast_defect_miss"]
        and pixel_f1 >= baseline_pixel_f1
    )
    return {
        "rule": rule,
        "accepted": accepted,
        "pixelF1": round(pixel_f1, 6),
        "pixelF1Delta": round(pixel_f1 - baseline_pixel_f1, 6),
        "taxonomyCounts": target_counts,
        "textureNoiseDelta": target_counts["texture_noise_false_positive"] - baseline_counts["texture_noise_false_positive"],
        "lowContrastDelta": target_counts["low_contrast_defect_miss"] - baseline_counts["low_contrast_defect_miss"],
        "rejectedComponentCount": rejected_component_count,
        "rejectedTruePositiveComponents": rejected_true_positive_components,
        "rejectedFalsePositiveComponents": rejected_false_positive_components,
        "pixelTotals": {
            "truePositive": true_positive,
            "falsePositive": false_positive,
            "falseNegative": false_negative,
            "trueNegative": true_negative,
        },
    }


def component_rule_rejects(component: dict[str, Any], rule: dict[str, Any]) -> bool:
    area = float(component.get("Area") or 0.0)
    if area > rule["areaMax"]:
        return False
    if float(component.get("Circularity") or 0.0) < rule["circularityMin"]:
        return False
    if float(component.get("FillRatio") or 0.0) < rule["fillRatioMin"]:
        return False
    if float(component.get("Elongation") or 0.0) > rule["elongationMax"]:
        return False
    ring_prominence_max = rule.get("ringProminenceMax")
    if ring_prominence_max is not None and float(component.get("RingProminence") or 0.0) > ring_prominence_max:
        return False
    return True


def classify_simulated_image(
    is_defect: bool,
    predicted_defect: bool,
    true_positive: int,
    false_positive: int,
    false_negative: int,
    defect_count: int,
    defect_area: float,
) -> list[str]:
    labels: list[str] = []
    ground_truth_area = true_positive + false_negative
    predicted_area = true_positive + false_positive
    image_f1 = pixel_f1_from_counts(true_positive, false_positive, false_negative)

    if not is_defect and predicted_defect:
        labels.append(
            "texture_noise_false_positive"
            if predicted_area <= 32 or defect_area <= 32 or defect_count <= 1
            else "oversegmentation_false_positive"
        )
    elif is_defect and not predicted_defect:
        labels.append("small_defect_miss" if ground_truth_area <= 96 else "low_contrast_defect_miss")
    elif is_defect and predicted_defect and image_f1 < 0.35:
        if false_negative > false_positive * 2:
            labels.append("undersegmentation_false_negative")
        elif false_positive > false_negative * 2:
            labels.append("mask_overgrowth_false_positive")
        else:
            labels.append("mask_boundary_mismatch")
    return labels


def pixel_f1_from_counts(true_positive: int, false_positive: int, false_negative: int) -> float:
    denominator = (2 * true_positive) + false_positive + false_negative
    return 1.0 if denominator == 0 else (2 * true_positive) / denominator


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
        "schemaVersion": "2026-05-01.surface-defect-failure-taxonomy.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "准工业公开 KolektorSDD2 failure taxonomy；不是真实产线签核。",
        "sourceReport": repo(CANDIDATE_JSON),
        "candidateVersion": SURFACE_CANDIDATE_VERSION,
        "targetTaxonomy": list(TARGET_TAXONOMY),
        "summary": {
            "caseCount": len(items),
            "taxonomyCounts": dict(counter),
            "targetTaxonomyCounts": {label: counter.get(label, 0) for label in TARGET_TAXONOMY},
        },
        "items": sorted(items, key=lambda item: (item["taxonomy"][0], item["caseId"])),
    }


def next_action(label: str) -> str:
    return {
        "texture_noise_false_positive": "Raise area/noise guard or add connected-component shape filtering while preserving defect recall.",
        "oversegmentation_false_positive": "Tune morphology and max-area handling; inspect whether broad response bands should be suppressed.",
        "small_defect_miss": "Use lower local-contrast floor on validation positives and protect with replay false-positive gate.",
        "low_contrast_defect_miss": "Compare CLAHE, local background kernels, and response normalization for low-contrast positives without lowering the global threshold.",
        "undersegmentation_false_negative": "Tune morphology and component filtering for validation positives without lowering the global threshold.",
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


def build_improvement_report(sweep: dict[str, Any], taxonomy: dict[str, Any], rule_selector: dict[str, Any]) -> dict[str, Any]:
    candidate = read_json(CANDIDATE_JSON)
    baseline = read_json(BASELINE_JSON)
    ab_row = surface_row_from_ab()
    candidate_summary = compact_summary(candidate, CANDIDATE_JSON)
    baseline_summary = compact_summary(baseline, BASELINE_JSON)
    accepted = (
        candidate_summary["profile"] != "baseline_default"
        and candidate_summary["pixelF1"] >= baseline_summary["pixelF1"]
        and candidate_summary["targetTaxonomyCounts"]["texture_noise_false_positive"] < baseline_summary["targetTaxonomyCounts"]["texture_noise_false_positive"]
        and candidate_summary["targetTaxonomyCounts"]["low_contrast_defect_miss"] <= baseline_summary["targetTaxonomyCounts"]["low_contrast_defect_miss"]
    )
    return {
        "schemaVersion": "2026-05-01.surface-defect-algorithm-improvement.v2",
        "generatedAtUtc": utc_now(),
        "accepted": accepted,
        "status": "targeted-improvement-accepted" if accepted else "hold-current-no-targeted-improvement",
        "claimBoundary": "准工业公开/替代证明；不声明真实产线工业验证完成。",
        "sourceReports": [
            repo(BASELINE_JSON),
            repo(CANDIDATE_JSON),
            repo(SWEEP_JSON),
            repo(TAXONOMY_JSON),
            repo(COMPONENT_TELEMETRY_CSV),
            repo(COMPONENT_DISTRIBUTION_CSV),
            repo(RULE_SELECTOR_JSON),
            repo(AB_REPORT),
        ],
        "baseline": baseline_summary,
        "candidate": candidate_summary,
        "targetTaxonomy": list(TARGET_TAXONOMY),
        "globalThresholdPolicy": f"No v2 profile lowers the manual global threshold below baseline {BASELINE_THRESHOLD}.",
        "sweep": sweep["summary"],
        "taxonomySummary": taxonomy["summary"],
        "componentRuleSelector": {
            "status": rule_selector["status"],
            "acceptedRuleCount": rule_selector["acceptedRuleCount"],
            "evaluatedRuleCount": rule_selector["evaluatedRuleCount"],
            "selectedRule": rule_selector.get("selectedRule"),
        },
        "abReplay": {
            "status": ab_row.get("comparisonStatus") if ab_row else "not-yet-run",
            "replayCaseCount": ab_row.get("replayCaseCount") if ab_row else 0,
            "improvedMetricCaseCount": ab_row.get("improvedMetricCaseCount") if ab_row else 0,
            "regressedCaseCount": ab_row.get("regressedCaseCount") if ab_row else 0,
            "worseMetricCaseCount": ab_row.get("worseMetricCaseCount") if ab_row else 0,
            "candidateBaseline": ab_row.get("candidateBaseline") if ab_row else None,
        },
        "nextActions": [
            "Keep product defaults unchanged until the fixed component-rule gate passes on validation and test.",
            "Promote only when texture_noise_false_positive decreases, low_contrast_defect_miss does not increase, and PixelF1 stays at or above baseline.",
            "Use the exported component telemetry distribution to choose the next compact-noise rule; do not lower the global manual threshold.",
            "If a selector rule passes, convert it into a default-off SurfaceDefectDetection profile and replay on test.",
        ],
    }


def render_sweep_markdown(sweep: dict[str, Any]) -> str:
    lines = [
        "# SurfaceDefectDetection KolektorSDD2 Sweep v2",
        "",
        f"GeneratedAtUtc: `{sweep['generatedAtUtc']}`",
        f"SelectedProfile: `{sweep['summary']['selectedProfile']}`",
        f"TargetTaxonomy: `{', '.join(sweep.get('targetTaxonomy', []))}`",
        f"GlobalThresholdPolicy: `{sweep.get('globalThresholdPolicy')}`",
        "",
        "| Profile | Target taxonomy cases | Split | Cases | Pixel F1 | Image AUROC | Image F1 | FP/normal | P95 ms | Score |",
        "|---|---:|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in sweep["profiles"]:
        lines.append(
            f"| {row['profile']} | {row['targetTaxonomyCaseCount']} | {row['split']} | {row['caseCount']} | {row['pixelF1']:.4f} | "
            f"{row['imageAuroc']:.4f} | {row['imageF1']:.4f} | {row['falsePositivePerImage']:.4f} | "
            f"{row['runtimeMsP95']:.3f} | {row['score']:.4f} |"
        )
    lines.append("")
    return "\n".join(lines)


def render_taxonomy_markdown(taxonomy: dict[str, Any]) -> str:
    lines = [
        "# SurfaceDefectDetection KolektorSDD2 Failure Taxonomy v2",
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


def render_rule_selector_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# SurfaceDefectDetection Component Rule Selector v2",
        "",
        f"GeneratedAtUtc: `{report['generatedAtUtc']}`",
        f"Status: `{report['status']}`",
        f"TelemetryCsv: `{report['telemetryCsv']}`",
        f"DistributionCsv: `{report['distributionCsv']}`",
        "",
        "## Fixed Promotion Gate",
        "",
        "| Gate | Baseline | Required |",
        "|---|---:|---|",
        f"| texture_noise_false_positive | {report['baseline']['targetTaxonomyCounts']['texture_noise_false_positive']} | decrease |",
        f"| low_contrast_defect_miss | {report['baseline']['targetTaxonomyCounts']['low_contrast_defect_miss']} | not increase |",
        f"| Pixel F1 | {report['baseline']['pixelF1']:.6f} | not decrease |",
        "",
    ]
    selected = report.get("selectedRule")
    if selected:
        lines.extend([
            "## Selected Rule",
            "",
            f"- Rule: `{selected['rule']}`",
            f"- Pixel F1: `{selected['pixelF1']:.6f}` (`{selected['pixelF1Delta']:+.6f}`)",
            f"- texture_noise_false_positive delta: `{selected['textureNoiseDelta']}`",
            f"- low_contrast_defect_miss delta: `{selected['lowContrastDelta']}`",
            "",
        ])
    else:
        lines.extend([
            "## Selected Rule",
            "",
            "- None. No evaluated compact-component rule met all fixed gates.",
            "",
        ])

    lines.extend([
        "## Top Rules",
        "",
        "| Accepted | Texture noise | Low contrast | Pixel F1 | TP comps rejected | FP comps rejected | Rule |",
        "|---|---:|---:|---:|---:|---:|---|",
    ])
    for item in report["topRules"][:15]:
        lines.append(
            f"| {item['accepted']} | {item['taxonomyCounts']['texture_noise_false_positive']} | "
            f"{item['taxonomyCounts']['low_contrast_defect_miss']} | {item['pixelF1']:.6f} | "
            f"{item['rejectedTruePositiveComponents']} | {item['rejectedFalsePositiveComponents']} | `{item['rule']}` |"
        )
    lines.append("")
    return "\n".join(lines)


def render_improvement_markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel SurfaceDefectDetection Improvement v2",
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
        "## Component Rule Selector",
        "",
        f"- Status: `{report['componentRuleSelector']['status']}`",
        f"- Accepted rules: `{report['componentRuleSelector']['acceptedRuleCount']}` / `{report['componentRuleSelector']['evaluatedRuleCount']}`",
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
    ensure_runner_built()
    lowered_profiles = [profile.name for profile in PROFILES if profile.threshold < BASELINE_THRESHOLD]
    if lowered_profiles:
        raise SystemExit(
            "error: SurfaceDefectDetection v2 profiles must not lower the global manual threshold below "
            f"{BASELINE_THRESHOLD}: {', '.join(lowered_profiles)}"
        )

    index = read_json(REPO_ROOT / INDEX_PATH)
    validation_ids = sample_ids(index["records"], "train", limit_positive=120, limit_negative=360)
    profiles: list[dict[str, Any]] = []
    for profile in PROFILES:
        output = REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_sweep_v2_{profile.name}.json"
        report = REPORT_DIR / f"SurfaceDefectDetection_kolektorsdd2_sweep_v2_{profile.name}.md"
        document = run_profile(profile, output, report, "train", validation_ids)
        row = compact_summary(document, output)
        row["split"] = "train-validation"
        row["targetTaxonomy"] = list(profile.target_taxonomy)
        profiles.append(row)

    baseline = next(row for row in profiles if row["profile"] == "baseline_default")
    safe_profiles = [
        row for row in profiles
        if row["profile"] != "baseline_default"
        and row["targetTaxonomyCounts"]["texture_noise_false_positive"] < baseline["targetTaxonomyCounts"]["texture_noise_false_positive"]
        and row["targetTaxonomyCounts"]["low_contrast_defect_miss"] <= baseline["targetTaxonomyCounts"]["low_contrast_defect_miss"]
        and row["pixelF1"] >= baseline["pixelF1"]
    ]
    selected = min(
        safe_profiles,
        key=lambda row: (
            row["targetTaxonomyCounts"]["texture_noise_false_positive"],
            row["targetTaxonomyCounts"]["low_contrast_defect_miss"],
            -row["pixelF1"],
            -row["score"],
        ),
    ) if safe_profiles else baseline
    selected_profile = next(profile for profile in PROFILES if profile.name == selected["profile"])
    candidate = run_profile(selected_profile, CANDIDATE_JSON, CANDIDATE_MD, "test", [], min_image_auroc=0.70, min_pixel_f1=0.20)
    candidate_summary = compact_summary(candidate, CANDIDATE_JSON)
    candidate_summary["split"] = "test"

    sweep = {
        "schemaVersion": "2026-05-01.surface-defect-kolektor-sweep.v2",
        "generatedAtUtc": utc_now(),
        "claimBoundary": "准工业公开 KolektorSDD2 validation/test sweep；不是真实产线签核。",
        "candidateVersion": SURFACE_CANDIDATE_VERSION,
        "targetTaxonomy": list(TARGET_TAXONOMY),
        "globalThresholdPolicy": f"No v2 profile may lower the manual global threshold below baseline {BASELINE_THRESHOLD}.",
        "sourceBaseline": repo(BASELINE_JSON),
        "summary": {
            "profileCount": len(profiles),
            "validationCaseCount": len(validation_ids),
            "selectedProfile": selected["profile"],
            "selectedValidationScore": selected["score"],
            "selectionPolicy": "Fixed gate: texture_noise_false_positive must decrease, low_contrast_defect_miss must not increase, and PixelF1 must not drop below baseline; otherwise hold baseline.",
            "candidatePixelF1": candidate_summary["pixelF1"],
            "candidateImageAuroc": candidate_summary["imageAuroc"],
            "candidateImageF1": candidate_summary["imageF1"],
            "candidateTargetTaxonomyCaseCount": candidate_summary["targetTaxonomyCaseCount"],
        },
        "profiles": profiles,
        "candidate": candidate_summary,
    }
    write_json(SWEEP_JSON, sweep)
    write_text(SWEEP_MD, render_sweep_markdown(sweep))
    return sweep


def write_reports(sweep: dict[str, Any]) -> None:
    component_tables = build_component_tables(sweep)
    rule_selector = build_component_rule_selector(sweep)
    taxonomy = build_taxonomy(read_json(CANDIDATE_JSON))
    write_json(TAXONOMY_JSON, taxonomy)
    write_text(TAXONOMY_MD, render_taxonomy_markdown(taxonomy))
    improvement = build_improvement_report(sweep, taxonomy, rule_selector)
    write_json(IMPROVEMENT_JSON, improvement)
    write_text(IMPROVEMENT_MD, render_improvement_markdown(improvement))
    write_text(AUDIT_MD, render_audit_markdown(improvement))
    print(
        "SurfaceDefectDetection KolektorSDD2 sweep/report complete: "
        f"profile={improvement['candidate']['profile']} "
        f"pixelF1={improvement['candidate']['pixelF1']:.4f} "
        f"imageAuroc={improvement['candidate']['imageAuroc']:.4f} "
        f"componentRows={component_tables['rowCount']} "
        f"selector={rule_selector['status']} "
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
