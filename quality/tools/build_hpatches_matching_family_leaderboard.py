from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
DEFAULT_REPORTS = (
    REPORT_DIR / "AkazeFeatureMatch_hpatches_candidate_v4.json",
    REPORT_DIR / "OrbFeatureMatch_hpatches_candidate_v4.json",
    REPORT_DIR / "PlanarMatching_hpatches_baseline.json",
    REPORT_DIR / "PlanarMatching_hpatches_akaze_baseline.json",
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


def pass_rate(cases: list[dict[str, Any]]) -> float:
    if not cases:
        return 0.0
    return round(sum(1 for case in cases if case.get("Passed") is True) / len(cases), 6)


def pass_count(cases: list[dict[str, Any]]) -> int:
    return sum(1 for case in cases if case.get("Passed") is True)


def row_from_report(path: Path) -> dict[str, Any]:
    document = read_json(path)
    summary = document["Summary"]
    cases = list(document.get("Cases", []))
    viewpoint_cases = [case for case in cases if case.get("SequenceType") == "viewpoint"]
    illumination_cases = [case for case in cases if case.get("SequenceType") == "illumination"]
    detector_type = summary.get("DetectorType")
    operator = summary.get("Operator", path.stem)
    label = f"{operator}({detector_type})" if operator == "PlanarMatching" and detector_type else operator
    failures = Counter(
        case.get("HomographyFailureReason") or case.get("Failure") or "unknown"
        for case in cases
        if case.get("Passed") is not True
    )

    return {
        "label": label,
        "operator": operator,
        "detectorType": detector_type,
        "sourceReport": repo(path),
        "candidateVersion": document.get("CandidateVersion"),
        "selectedProfile": document.get("Sweep", {}).get("selectedProfile"),
        "accepted": summary.get("Accepted"),
        "caseCount": summary.get("CaseCount", len(cases)),
        "passed": summary.get("Passed", pass_count(cases)),
        "passRate": summary.get("PassRate", pass_rate(cases)),
        "viewpointCaseCount": len(viewpoint_cases),
        "viewpointPassed": pass_count(viewpoint_cases),
        "viewpointPassRate": pass_rate(viewpoint_cases),
        "illuminationCaseCount": len(illumination_cases),
        "illuminationPassed": pass_count(illumination_cases),
        "illuminationPassRate": pass_rate(illumination_cases),
        "meanPositionErrorPx": summary.get("MeanPositionErrorPx"),
        "p95PositionErrorPx": summary.get("P95PositionErrorPx"),
        "runtimeMs": summary.get("RuntimeMs"),
        "maxFeatures": summary.get("MaxFeatures"),
        "matchRatio": summary.get("MatchRatio"),
        "ransacThreshold": summary.get("RansacThreshold"),
        "minInlierRatio": summary.get("MinInlierRatio"),
        "fastThreshold": summary.get("FastThreshold"),
        "edgeThreshold": summary.get("EdgeThreshold"),
        "akazeThreshold": summary.get("AkazeThreshold"),
        "scoreThreshold": summary.get("ScoreThreshold"),
        "topFailures": [
            {"reason": reason, "count": count}
            for reason, count in failures.most_common(5)
            if reason
        ],
    }


def ranking_key(row: dict[str, Any]) -> tuple[float, float, float, float]:
    return (
        -float(row.get("viewpointPassRate") or 0),
        -float(row.get("passRate") or 0),
        float(row.get("p95PositionErrorPx") or 1_000_000),
        float(row.get("runtimeMs") or 1_000_000),
    )


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# HPatches Matching Family Leaderboard",
        "",
        f"GeneratedAtUtc: `{document['generatedAtUtc']}`",
        f"RankingPolicy: `{document['rankingPolicy']}`",
        "",
        "| Rank | Candidate | Viewpoint pass | Total pass | P95 error px | Mean error px | Runtime ms | Params | Report |",
        "|---:|---|---:|---:|---:|---:|---:|---|---|",
    ]
    for index, row in enumerate(document["rows"], start=1):
        params = [
            f"max={row.get('maxFeatures')}",
            f"ratio={row.get('matchRatio')}",
            f"ransac={row.get('ransacThreshold')}",
            f"minInlierRatio={row.get('minInlierRatio')}",
        ]
        if row.get("operator") == "AkazeFeatureMatch":
            params.append(f"akazeThreshold={row.get('akazeThreshold')}")
        if row.get("operator") == "OrbFeatureMatch":
            params.append(f"fast={row.get('fastThreshold')}")
            params.append(f"edge={row.get('edgeThreshold')}")
        if row.get("operator") == "PlanarMatching" and row.get("detectorType"):
            params.append(f"detector={row['detectorType']}")
        if row.get("operator") == "PlanarMatching" and row.get("scoreThreshold") is not None:
            params.append(f"score={row['scoreThreshold']}")

        lines.append(
            f"| {index} | {row['label']} | "
            f"{row['viewpointPassed']}/{row['viewpointCaseCount']} ({row['viewpointPassRate']}) | "
            f"{row['passed']}/{row['caseCount']} ({row['passRate']}) | "
            f"{row.get('p95PositionErrorPx')} | {row.get('meanPositionErrorPx')} | {row.get('runtimeMs')} | "
            f"{', '.join(params)} | {row['sourceReport']} |"
        )

    lines.extend(["", "## Failure Focus", "", "| Candidate | Top failure reasons |", "|---|---|"])
    for row in document["rows"]:
        failures = "; ".join(f"{item['reason']} ({item['count']})" for item in row.get("topFailures", [])) or "-"
        lines.append(f"| {row['label']} | {failures} |")

    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build HPatches matching family leaderboard.")
    parser.add_argument("--report", action="append", help="Report JSON to include. Defaults to matching family reports.")
    parser.add_argument("--output", default="quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.json")
    parser.add_argument("--markdown", default="quality/evals/reports/QualityFlywheel_hpatches_matching_family_leaderboard.md")
    args = parser.parse_args()

    report_paths = [REPO_ROOT / item for item in args.report] if args.report else list(DEFAULT_REPORTS)
    missing = [repo(path) for path in report_paths if not path.exists()]
    if missing:
        raise SystemExit("error: missing report(s): " + ", ".join(missing))

    rows = sorted((row_from_report(path) for path in report_paths), key=ranking_key)
    document = {
        "schemaVersion": "2026-04-29.hpatches-matching-family-leaderboard.v1",
        "generatedAtUtc": utc_now(),
        "rankingPolicy": "viewpointPassRate desc, total passRate desc, p95PositionErrorPx asc, runtimeMs asc",
        "rows": rows,
    }
    output_path = REPO_ROOT / args.output
    markdown_path = REPO_ROOT / args.markdown
    write_json(output_path, document)
    write_text(markdown_path, render_markdown(document))
    print(f"hpatches matching family leaderboard complete: output={repo(output_path)}, rows={len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
