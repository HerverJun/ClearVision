from __future__ import annotations

import argparse
import json
import shutil
import subprocess
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
TMP_DIR = REPO_ROOT / ".tmp" / "hpatches-sweep"
HPATCHES_PROJECT = "quality/tools/HPatchesFeatureMatchDatasetRunner/HPatchesFeatureMatchDatasetRunner.csproj"
REPLAY_MANIFEST = REPO_ROOT / "quality" / "evals" / "reports" / "QualityFlywheel_public_benchmark_replay_manifest.json"
OPERATORS = ("AkazeFeatureMatch", "OrbFeatureMatch")
DEFAULT_CANDIDATE_VERSION = "v4"


@dataclass(frozen=True)
class SweepProfile:
    name: str
    max_features: int = 1200
    min_inliers: int = 6
    match_ratio: float = 0.75
    ransac_threshold: float = 5.0
    min_inlier_ratio: float = 0.25
    fast_threshold: int = 20
    edge_threshold: int = 15
    akaze_threshold: float = 0.001


PROFILES = (
    SweepProfile("default_v3"),
    SweepProfile("looser_ransac_v3", ransac_threshold=7.0, min_inlier_ratio=0.20),
    SweepProfile("orb_v3", max_features=1600, match_ratio=0.70, ransac_threshold=6.0, min_inlier_ratio=0.20, fast_threshold=12),
    SweepProfile("dense_low_detector_threshold", max_features=2000, ransac_threshold=7.0, min_inlier_ratio=0.20, akaze_threshold=0.0006),
    SweepProfile("dense_high_ratio_low_detector_threshold", max_features=2000, match_ratio=0.82, ransac_threshold=8.0, min_inlier_ratio=0.15, akaze_threshold=0.0006),
    SweepProfile("partial_plane_low_detector_threshold", max_features=2000, min_inliers=4, match_ratio=0.88, ransac_threshold=10.0, min_inlier_ratio=0.10, akaze_threshold=0.0005),
    SweepProfile("strict_geometry", max_features=1600, match_ratio=0.70, ransac_threshold=6.0, min_inlier_ratio=0.20),
    SweepProfile("orb_low_edge_dense", max_features=2000, match_ratio=0.70, ransac_threshold=6.0, min_inlier_ratio=0.20, fast_threshold=8, edge_threshold=5),
    SweepProfile("orb_low_edge_loose_ransac", max_features=2000, match_ratio=0.75, ransac_threshold=8.0, min_inlier_ratio=0.15, fast_threshold=8, edge_threshold=5),
    SweepProfile("orb_fast_low_threshold", max_features=2000, match_ratio=0.82, ransac_threshold=8.0, min_inlier_ratio=0.15, fast_threshold=6, edge_threshold=8),
    SweepProfile("replay_safe_dense_strict", max_features=2000, match_ratio=0.70, ransac_threshold=7.0, min_inlier_ratio=0.25, fast_threshold=16, edge_threshold=10),
    SweepProfile("replay_safe_high_ratio", max_features=2000, match_ratio=0.78, ransac_threshold=5.0, min_inlier_ratio=0.20, fast_threshold=16, edge_threshold=10),
    SweepProfile("replay_safe_balanced_1800", max_features=1800, match_ratio=0.70, ransac_threshold=6.0, min_inlier_ratio=0.25, fast_threshold=20, edge_threshold=10),
    SweepProfile("partial_plane_v4", max_features=2000, min_inliers=4, match_ratio=0.85, ransac_threshold=10.0, min_inlier_ratio=0.10, fast_threshold=6, edge_threshold=5, akaze_threshold=0.0005),
    SweepProfile("precision_more_features", max_features=2000, match_ratio=0.65, ransac_threshold=7.0, min_inlier_ratio=0.20, fast_threshold=10, edge_threshold=10),
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


def run_command(command: list[str]) -> None:
    completed = subprocess.run(
        command,
        cwd=REPO_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    if completed.returncode != 0:
        raise SystemExit(
            "error: command failed\n"
            + " ".join(command)
            + "\n"
            + completed.stdout
            + "\n"
            + completed.stderr
        )


def runner_command(
    operator: str,
    profile: SweepProfile,
    output: Path,
    report: Path,
    *,
    pair_index: int,
    viewpoint_only: bool,
    max_sequences: int,
    case_ids: list[str] | None = None,
) -> list[str]:
    command = [
        "dotnet",
        "run",
        "--no-build",
        "--project",
        HPATCHES_PROJECT,
        "--",
        "--operator",
        operator,
        "--index",
        "quality/datasets/hpatches_index.json",
        "--output",
        repo(output),
        "--report",
        repo(report),
        "--max-sequences",
        str(max_sequences),
        "--pair-index",
        str(pair_index),
        "--max-features",
        str(profile.max_features),
        "--min-inliers",
        str(profile.min_inliers),
        "--match-ratio",
        f"{profile.match_ratio:.3f}",
        "--ransac-threshold",
        f"{profile.ransac_threshold:.3f}",
        "--min-inlier-ratio",
        f"{profile.min_inlier_ratio:.3f}",
        "--fast-threshold",
        str(profile.fast_threshold),
        "--edge-threshold",
        str(profile.edge_threshold),
        "--akaze-threshold",
        f"{profile.akaze_threshold:.6f}",
        "--min-pass-rate",
        "0",
        "--max-p95-position-error-px",
        "100000",
    ]
    if viewpoint_only:
        command.append("--viewpoint-only")
    if case_ids:
        command.extend(["--case-ids", ",".join(case_ids)])
    return command


def summarize(operator: str, profile: SweepProfile, pair_index: int, document: dict[str, Any]) -> dict[str, Any]:
    summary = document["Summary"]
    viewpoint_cases = [
        case
        for case in document.get("Cases", [])
        if str(case.get("SequenceType")) == "viewpoint"
    ]
    return {
        "operator": operator,
        "profile": profile.name,
        "pair": f"1-{pair_index}",
        "maxFeatures": profile.max_features,
        "minInliers": profile.min_inliers,
        "matchRatio": profile.match_ratio,
        "ransacThreshold": profile.ransac_threshold,
        "minInlierRatio": profile.min_inlier_ratio,
        "fastThreshold": profile.fast_threshold,
        "edgeThreshold": profile.edge_threshold,
        "akazeThreshold": profile.akaze_threshold,
        "caseCount": summary.get("CaseCount", 0),
        "passed": summary.get("Passed", 0),
        "failed": summary.get("Failed", 0),
        "passRate": summary.get("PassRate", 0),
        "meanPositionErrorPx": summary.get("MeanPositionErrorPx"),
        "p95PositionErrorPx": summary.get("P95PositionErrorPx"),
        "runtimeMs": summary.get("RuntimeMs"),
        "viewpointFixedProxyCount": sum(1 for case in viewpoint_cases if case.get("Passed") is True),
    }


def ranking_key(row: dict[str, Any]) -> tuple[float, float, float, float]:
    return (
        -float(row.get("passRate") or 0),
        float(row.get("meanPositionErrorPx") or 1_000_000),
        float(row.get("p95PositionErrorPx") or 1_000_000),
        float(row.get("runtimeMs") or 1_000_000),
    )


def replay_case_ids(operator: str) -> list[str]:
    replay = read_json(REPLAY_MANIFEST)
    return [
        str(case.get("caseId"))
        for case in replay.get("cases", [])
        if case.get("operator") == operator
    ]


def selection_key(validation: dict[str, Any], holdout: dict[str, Any], replay: dict[str, Any]) -> tuple[float, float, float, float, float, float]:
    validation_pass = float(validation.get("passRate") or 0)
    holdout_pass = float(holdout.get("passRate") or 0)
    replay_pass = float(replay.get("passRate") or 0)
    mean_error = (
        float(validation.get("meanPositionErrorPx") or 1_000_000) +
        float(holdout.get("meanPositionErrorPx") or 1_000_000) +
        float(replay.get("meanPositionErrorPx") or 1_000_000)
    ) / 3
    p95_error = (
        float(validation.get("p95PositionErrorPx") or 1_000_000) +
        float(holdout.get("p95PositionErrorPx") or 1_000_000) +
        float(replay.get("p95PositionErrorPx") or 1_000_000)
    ) / 3
    runtime = (
        float(validation.get("runtimeMs") or 1_000_000) +
        float(holdout.get("runtimeMs") or 1_000_000) +
        float(replay.get("runtimeMs") or 1_000_000)
    ) / 3
    return (
        -replay_pass,
        -validation_pass,
        -holdout_pass,
        mean_error,
        p95_error,
        runtime,
    )


def run_operator(operator: str, max_sequences: int, candidate_version: str, holdout_candidates: int) -> dict[str, Any]:
    TMP_DIR.mkdir(parents=True, exist_ok=True)
    sweep_rows: list[dict[str, Any]] = []
    documents: dict[str, dict[str, Any]] = {}
    replay_rows: list[dict[str, Any]] = []
    replay_ids = replay_case_ids(operator)

    for profile in PROFILES:
        output = TMP_DIR / f"{operator}_{profile.name}_pair2.json"
        report = TMP_DIR / f"{operator}_{profile.name}_pair2.md"
        run_command(
            runner_command(
                operator,
                profile,
                output,
                report,
                pair_index=2,
                viewpoint_only=True,
                max_sequences=max_sequences,
            )
        )
        document = read_json(output)
        documents[profile.name] = document
        sweep_rows.append(summarize(operator, profile, 2, document))

        replay_output = TMP_DIR / f"{operator}_{profile.name}_replay_gate.json"
        replay_report = TMP_DIR / f"{operator}_{profile.name}_replay_gate.md"
        run_command(
            runner_command(
                operator,
                profile,
                replay_output,
                replay_report,
                pair_index=2,
                viewpoint_only=False,
                max_sequences=max_sequences,
                case_ids=replay_ids,
            )
        )
        replay_document = read_json(replay_output)
        replay_summary = summarize(operator, profile, 2, replay_document)
        replay_summary["pair"] = "replay"
        replay_rows.append(replay_summary)

    validation_by_profile = {row["profile"]: row for row in sweep_rows}
    replay_by_profile = {row["profile"]: row for row in replay_rows}
    holdout_rows: list[dict[str, Any]] = []
    top_validation_rows = sorted(
        sweep_rows,
        key=lambda row: (
            -float(replay_by_profile[row["profile"]].get("passRate") or 0),
            *ranking_key(row),
        ),
    )[:max(1, holdout_candidates)]
    for row in top_validation_rows:
        profile = next(profile for profile in PROFILES if profile.name == row["profile"])
        holdout_output = TMP_DIR / f"{operator}_{profile.name}_pair3_holdout.json"
        holdout_report = TMP_DIR / f"{operator}_{profile.name}_pair3_holdout.md"
        run_command(
            runner_command(
                operator,
                profile,
                holdout_output,
                holdout_report,
                pair_index=3,
                viewpoint_only=True,
                max_sequences=max_sequences,
            )
        )
        holdout = read_json(holdout_output)
        holdout_rows.append(summarize(operator, profile, 3, holdout))

    best_holdout = sorted(
        holdout_rows,
        key=lambda row: selection_key(validation_by_profile[row["profile"]], row, replay_by_profile[row["profile"]]),
    )[0]
    best_row = validation_by_profile[best_holdout["profile"]]
    best_profile = next(profile for profile in PROFILES if profile.name == best_row["profile"])

    candidate_json = REPORT_DIR / f"{operator}_hpatches_candidate_{candidate_version}.json"
    candidate_md = REPORT_DIR / f"{operator}_hpatches_candidate_{candidate_version}.md"
    candidate_tmp_md = TMP_DIR / f"{operator}_candidate_{candidate_version}.md"
    run_command(
        runner_command(
            operator,
            best_profile,
            candidate_json,
            candidate_tmp_md,
            pair_index=2,
            viewpoint_only=False,
            max_sequences=max_sequences,
        )
    )
    candidate = read_json(candidate_json)
    candidate["CandidateVersion"] = candidate_version
    candidate["Sweep"] = {
        "schemaVersion": "2026-04-29.hpatches-matching-sweep.v1",
        "generatedAtUtc": utc_now(),
        "selectionPolicy": "Run viewpoint pair 1-2 validation, public replay gate, and pair 1-3 holdout. Select by replay passRate desc, validation passRate desc, holdout passRate desc, then mean error asc.",
        "validationPair": "1-2",
        "holdoutPair": "1-3",
        "selectedProfile": best_profile.name,
        "selectedParameters": {
            "maxFeatures": best_profile.max_features,
            "minInliers": best_profile.min_inliers,
            "matchRatio": best_profile.match_ratio,
            "ransacThreshold": best_profile.ransac_threshold,
            "minInlierRatio": best_profile.min_inlier_ratio,
            "fastThreshold": best_profile.fast_threshold,
            "edgeThreshold": best_profile.edge_threshold,
            "akazeThreshold": best_profile.akaze_threshold,
        },
        "validationRows": sweep_rows,
        "replayRows": replay_rows,
        "holdoutRows": holdout_rows,
        "holdoutSummary": best_holdout,
    }
    write_json(candidate_json, candidate)
    write_text(candidate_md, render_markdown(candidate))
    return {
        "operator": operator,
        "candidateJson": repo(candidate_json),
        "candidateMarkdown": repo(candidate_md),
        "selectedProfile": best_profile.name,
        "validation": best_row,
        "replay": replay_by_profile[best_profile.name],
        "holdout": best_holdout,
    }


def render_markdown(candidate: dict[str, Any]) -> str:
    summary = candidate["Summary"]
    sweep = candidate["Sweep"]
    lines = [
        f"# {summary['Operator']} HPatches Candidate {candidate['CandidateVersion']}",
        "",
        f"GeneratedAtUtc: `{summary['GeneratedAtUtc']}`",
        f"CandidateVersion: `{candidate['CandidateVersion']}`",
        f"SelectedProfile: `{sweep['selectedProfile']}`",
        "",
        "## Candidate Summary",
        "",
        "| Metric | Value |",
        "|---|---:|",
        f"| Cases | {summary['CaseCount']} |",
        f"| Passed | {summary['Passed']} |",
        f"| Failed | {summary['Failed']} |",
        f"| Pass rate | {summary['PassRate']} |",
        f"| Mean position error px | {summary['MeanPositionErrorPx']} |",
        f"| P95 position error px | {summary['P95PositionErrorPx']} |",
        f"| Runtime ms | {summary['RuntimeMs']} |",
        "",
        "## Sweep Validation",
        "",
        "| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms | Params |",
        "|---|---|---:|---:|---:|---:|---:|---|",
    ]
    for row in sweep["validationRows"]:
        lines.append(
            f"| {row['profile']} | {row['pair']} | {row['passed']}/{row['caseCount']} | "
            f"{row['passRate']} | {row['meanPositionErrorPx']} | {row['p95PositionErrorPx']} | "
            f"{row['runtimeMs']} | ratio={row['matchRatio']}, ransac={row['ransacThreshold']}, "
            f"minInlierRatio={row['minInlierRatio']}, maxFeatures={row['maxFeatures']}, "
            f"fast={row['fastThreshold']}, edge={row['edgeThreshold']}, akazeThreshold={row['akazeThreshold']} |"
        )

    lines.extend(
        [
            "",
            "## Replay Gate",
            "",
            "| Profile | Pass | Pass rate | Mean error | P95 error | Runtime ms |",
            "|---|---:|---:|---:|---:|---:|",
        ]
    )
    for row in sweep.get("replayRows", []):
        lines.append(
            f"| {row['profile']} | {row['passed']}/{row['caseCount']} | {row['passRate']} | "
            f"{row['meanPositionErrorPx']} | {row['p95PositionErrorPx']} | {row['runtimeMs']} |"
        )

    holdout = sweep["holdoutSummary"]
    lines.extend(
        [
            "",
            "## Holdout Selection",
            "",
            "| Profile | Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |",
            "|---|---|---:|---:|---:|---:|---:|",
        ]
    )
    for row in sweep.get("holdoutRows", [holdout]):
        lines.append(
            f"| {row['profile']} | {row['pair']} | {row['passed']}/{row['caseCount']} | "
            f"{row['passRate']} | {row['meanPositionErrorPx']} | {row['p95PositionErrorPx']} | {row['runtimeMs']} |"
        )

    lines.extend(
        [
            "",
            "## Selected Holdout",
            "",
            "| Pair | Pass | Pass rate | Mean error | P95 error | Runtime ms |",
            "|---|---:|---:|---:|---:|---:|",
            f"| {holdout['pair']} | {holdout['passed']}/{holdout['caseCount']} | {holdout['passRate']} | {holdout['meanPositionErrorPx']} | {holdout['p95PositionErrorPx']} | {holdout['runtimeMs']} |",
            "",
            "## Case Diagnostics",
            "",
            "| Case | Type | Pair | Passed | Error px | Inlier ratio | Mean reproj | Area ratio | Corners in | Center in | Homography failure |",
            "|---|---|---|---|---:|---:|---:|---:|---:|---|---|",
        ]
    )
    for case in candidate.get("Cases", []):
        lines.append(
            f"| {case['CaseId']} | {case['SequenceType']} | {case['Pair']} | {case['Passed']} | "
            f"{case['PositionErrorPx']} | {case.get('InlierRatio') or '-'} | "
            f"{case.get('MeanReprojectionError') or '-'} | {case.get('AreaRatio') or '-'} | "
            f"{case.get('CornersInsideCount', '-')} | {case.get('ProjectedCenterInside', '-')} | "
            f"{case.get('HomographyFailureReason') or '-'} |"
        )
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run HPatches viewpoint parameter sweeps for matching operators.")
    parser.add_argument("--operator", choices=OPERATORS, action="append", help="Operator to sweep. Defaults to both.")
    parser.add_argument("--max-sequences", type=int, default=200)
    parser.add_argument("--candidate-version", default=DEFAULT_CANDIDATE_VERSION)
    parser.add_argument("--holdout-candidates", type=int, default=4)
    parser.add_argument("--keep-temp", action="store_true")
    args = parser.parse_args()

    operators = tuple(args.operator) if args.operator else OPERATORS
    run_command(["dotnet", "build", HPATCHES_PROJECT, "--nologo", "--verbosity", "minimal"])
    results = [run_operator(operator, args.max_sequences, args.candidate_version, args.holdout_candidates) for operator in operators]

    output = REPORT_DIR / f"QualityFlywheel_hpatches_matching_sweep_{args.candidate_version}.json"
    write_json(
        output,
        {
            "schemaVersion": "2026-04-29.hpatches-matching-sweep.v1",
            "generatedAtUtc": utc_now(),
            "results": results,
        },
    )
    if not args.keep_temp and TMP_DIR.exists():
        shutil.rmtree(TMP_DIR)

    print(
        "hpatches matching sweep complete: "
        + ", ".join(f"{item['operator']}={item['selectedProfile']}" for item in results)
        + f", output={repo(output)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
