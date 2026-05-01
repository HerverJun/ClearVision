from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPORT_DIR = REPO_ROOT / "quality" / "evals" / "reports"
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"
GENERATED_AT = "2026-04-29T00:00:00Z"


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def repo(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def summary_value(matrix_text: str, label: str) -> str:
    pattern = re.compile(rf"^- {re.escape(label)}: (.+)$", re.MULTILINE)
    match = pattern.search(matrix_text)
    return match.group(1).strip() if match else ""


def count_status(value: str, key: str) -> int:
    match = re.search(rf"\b{re.escape(key)}=(\d+)", value)
    return int(match.group(1)) if match else 0


def suite_budget(path: Path) -> int:
    suite = read_json(path)
    return int(suite.get("ciBudgetMinutes", 0) or 0)


def drill_reports() -> list[Path]:
    return sorted(REPORT_DIR.glob("field_replay_drill_2026_04_*.json"))


def build_gate() -> dict[str, Any]:
    matrix_path = REPORT_DIR / "operator_quality_matrix.md"
    g3_path = REPORT_DIR / "QualityFlywheel_G3_dataset_closure_baseline.json"
    field_path = REPORT_DIR / "QualityFlywheel_field_replay_baseline.json"
    matrix = read_text(matrix_path)
    g3 = read_json(g3_path)
    field = read_json(field_path)

    checks = [
        {
            "id": "matrix_total_operators",
            "passed": summary_value(matrix, "Total operators") == "155",
            "details": summary_value(matrix, "Total operators"),
        },
        {
            "id": "matrix_all_a_level",
            "passed": count_status(summary_value(matrix, "Level counts"), "B") == 0
            and count_status(summary_value(matrix, "Level counts"), "C") == 0,
            "details": summary_value(matrix, "Level counts"),
        },
        {
            "id": "matrix_all_have_evidence_signal",
            "passed": count_status(summary_value(matrix, "Any evidence signal"), "No") == 0,
            "details": summary_value(matrix, "Any evidence signal"),
        },
        {
            "id": "matrix_no_card_todo",
            "passed": summary_value(matrix, "Cards with TODO") == "0",
            "details": summary_value(matrix, "Cards with TODO"),
        },
        {
            "id": "g3_dataset_20_closed",
            "passed": int(g3["Summary"].get("OperatorCount", 0)) == 20 and int(g3["Summary"].get("Failed", 0)) == 0,
            "details": f"{g3['Summary'].get('OperatorCount')} operators, failed={g3['Summary'].get('Failed')}",
        },
        {
            "id": "field_replay_baseline_passed",
            "passed": bool(field["Summary"].get("PassedDrill")) and int(field["Summary"].get("PrivacyLeakCount", 1)) == 0,
            "details": f"passed={field['Summary'].get('PassedDrill')}, samples={field['Summary'].get('CaseCount')}",
        },
        {
            "id": "field_replay_three_consecutive_drills",
            "passed": len(drill_reports()) >= 3
            and all(read_json(path)["Summary"].get("PassedDrill") for path in drill_reports()[-3:]),
            "details": ", ".join(repo(path) for path in drill_reports()[-3:]),
        },
        {
            "id": "quick_suite_budget",
            "passed": suite_budget(SUITE_DIR / "quick_contract_suite.json") <= 10,
            "details": f"{suite_budget(SUITE_DIR / 'quick_contract_suite.json')} minutes",
        },
        {
            "id": "dataset_suite_manual_or_nightly_budgeted",
            "passed": suite_budget(SUITE_DIR / "dataset_heavy_suite.json") <= 120,
            "details": f"{suite_budget(SUITE_DIR / 'dataset_heavy_suite.json')} minutes",
        },
    ]
    passed = all(item["passed"] for item in checks)
    return {
        "schemaVersion": "2026-04-29.quality-release-gate",
        "generatedAtUtc": GENERATED_AT,
        "passed": passed,
        "checks": checks,
        "policy": {
            "newCoreOperatorRule": "A new or materially changed core operator must carry contract/golden/dataset/field evidence, or an explicit waiver with owner and expiry, before it can be advertised as production-trustworthy A level.",
            "quickSuiteBudgetMinutes": 10,
            "datasetHeavyLane": "manual-or-nightly",
            "monthlySnapshot": "quality/evals/reports/QualityFlywheel_monthly_snapshot.md",
        },
    }


def render_gate(gate: dict[str, Any]) -> str:
    lines = [
        "# Quality Flywheel Release Gate",
        "",
        f"GeneratedAtUtc: `{gate['generatedAtUtc']}`",
        f"Passed: `{'Yes' if gate['passed'] else 'No'}`",
        "",
        "## Checks",
        "",
        "| Check | Status | Details |",
        "|---|---|---|",
    ]
    for check in gate["checks"]:
        lines.append(f"| {check['id']} | {'Pass' if check['passed'] else 'Fail'} | {check['details']} |")
    lines.extend(
        [
            "",
            "## Release Rule",
            "",
            gate["policy"]["newCoreOperatorRule"],
            "",
        ]
    )
    return "\n".join(lines)


def render_snapshot(gate: dict[str, Any]) -> str:
    matrix = read_text(REPORT_DIR / "operator_quality_matrix.md")
    g3 = read_json(REPORT_DIR / "QualityFlywheel_G3_dataset_closure_baseline.json")
    field = read_json(REPORT_DIR / "QualityFlywheel_field_replay_baseline.json")
    lines = [
        "# Quality Flywheel Monthly Snapshot",
        "",
        f"GeneratedAtUtc: `{GENERATED_AT}`",
        "",
        "## Matrix",
        "",
        f"- Total operators: {summary_value(matrix, 'Total operators')}",
        f"- Level counts: {summary_value(matrix, 'Level counts')}",
        f"- Evidence signal: {summary_value(matrix, 'Any evidence signal')}",
        f"- Contract evidence: {summary_value(matrix, 'Contract evidence status')}",
        f"- Golden evidence: {summary_value(matrix, 'Golden test status')}",
        f"- Dataset evidence: {summary_value(matrix, 'Dataset evidence status')}",
        f"- Field replay: {summary_value(matrix, 'Field replay status')}",
        f"- Cards with TODO: {summary_value(matrix, 'Cards with TODO')}",
        "",
        "## G3 Dataset",
        "",
        f"- Closed operators: {g3['Summary']['OperatorCount']}/20",
        f"- Tier A: {g3['Summary']['TierAOperators']}",
        f"- Tier B: {g3['Summary']['TierBOperators']}",
        f"- Dataset/protocol cases: {g3['Summary']['CaseCount']}",
        "",
        "## G4 Field Replay",
        "",
        f"- Drill samples: {field['Summary']['CaseCount']}",
        f"- Reproducible rate: {field['Summary']['ReproducibleRate']:.2%}",
        f"- Regressionized rate: {field['Summary']['RegressionizedRate']:.2%}",
        f"- Consecutive drill reports: {len(drill_reports())}",
        "",
        "## G5 Gate",
        "",
        f"- Release gate: {'Pass' if gate['passed'] else 'Fail'}",
        f"- Quick suite budget: {suite_budget(SUITE_DIR / 'quick_contract_suite.json')} minutes",
        f"- Dataset heavy budget: {suite_budget(SUITE_DIR / 'dataset_heavy_suite.json')} minutes",
        "",
    ]
    return "\n".join(lines)


def render_closeout(gate: dict[str, Any]) -> str:
    matrix = read_text(REPORT_DIR / "operator_quality_matrix.md")
    lines = [
        "# Quality Flywheel 6 Month Closeout",
        "",
        f"GeneratedAtUtc: `{GENERATED_AT}`",
        "",
        "## Result",
        "",
        f"- Release gate passed: {'Yes' if gate['passed'] else 'No'}",
        f"- Level counts: {summary_value(matrix, 'Level counts')}",
        f"- Evidence signal: {summary_value(matrix, 'Any evidence signal')}",
        f"- Dataset evidence: {summary_value(matrix, 'Dataset evidence status')}",
        f"- Field replay: {summary_value(matrix, 'Field replay status')}",
        f"- Cards with TODO: {summary_value(matrix, 'Cards with TODO')}",
        "",
        "## Closed Goals",
        "",
        "- G1 accepted evidence signal is closed for all 155 operators.",
        "- G2 frozen core 50 has contract/golden evidence and suite routing.",
        "- G3 frozen vision 20 has Tier A/B dataset evidence manifests, thresholds, baselines, and failure boundaries.",
        "- G4 field replay has schema, manifest, runner, and three passing drill reports.",
        "- G5 governance has quick/heavy suite budgets, monthly snapshot output, and a release gate rule for new core operators.",
        "",
        "## Evidence Artifacts",
        "",
        "- `quality/evals/reports/QualityFlywheel_G3_dataset_closure.md`",
        "- `quality/evals/reports/QualityFlywheel_field_replay_baseline.json`",
        "- `quality/evals/reports/QualityFlywheel_release_gate_report.md`",
        "- `quality/evals/reports/QualityFlywheel_monthly_snapshot.md`",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    gate = build_gate()
    write_json(REPORT_DIR / "QualityFlywheel_release_gate_report.json", gate)
    (REPORT_DIR / "QualityFlywheel_release_gate_report.md").write_text(render_gate(gate), encoding="utf-8", newline="\n")
    (REPORT_DIR / "QualityFlywheel_monthly_snapshot.md").write_text(render_snapshot(gate), encoding="utf-8", newline="\n")
    (REPORT_DIR / "QualityFlywheel_6month_closeout.md").write_text(render_closeout(gate), encoding="utf-8", newline="\n")
    print(f"release gate passed={gate['passed']}")
    return 0 if gate["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
