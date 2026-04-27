from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"


@dataclass(frozen=True)
class SuiteEntry:
    stage_id: str
    entry_id: str
    status: str
    evidence_kind: str
    baseline_json: Path | None
    report_markdown: Path | None
    command: list[str]
    estimated_seconds: int
    raw: dict[str, Any]


def repo_relative(path: Path | None) -> str:
    if path is None:
        return "-"
    try:
        return path.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return str(path)


def resolve_repo_path(value: str | None) -> Path | None:
    if not value:
        return None
    path = Path(value)
    if path.is_absolute():
        return path
    return REPO_ROOT / path


def suite_path(value: str) -> Path:
    path = Path(value)
    if path.exists():
        return path
    if path.suffix != ".json":
        path = path.with_suffix(".json")
    candidate = SUITE_DIR / path.name
    if candidate.exists():
        return candidate
    raise FileNotFoundError(f"Suite not found: {value}")


def load_suite(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        suite = json.load(handle)
    if not isinstance(suite, dict):
        raise ValueError(f"Suite root must be an object: {path}")
    return suite


def iter_entries(suite: dict[str, Any]) -> list[SuiteEntry]:
    entries: list[SuiteEntry] = []
    stages = suite.get("stages", [])
    if not isinstance(stages, list):
        raise ValueError("Suite field 'stages' must be a list.")

    for stage in stages:
        if not isinstance(stage, dict):
            raise ValueError("Every suite stage must be an object.")
        stage_id = str(stage.get("id") or "").strip()
        if not stage_id:
            raise ValueError("Every suite stage must have an id.")
        stage_entries = stage.get("entries", [])
        if not isinstance(stage_entries, list):
            raise ValueError(f"Stage {stage_id} field 'entries' must be a list.")

        for raw in stage_entries:
            if not isinstance(raw, dict):
                raise ValueError(f"Stage {stage_id} contains a non-object entry.")
            command = raw.get("command", [])
            if command is None:
                command = []
            if not isinstance(command, list) or any(not isinstance(item, str) for item in command):
                raise ValueError(f"Entry {raw.get('id')} command must be a list of strings.")
            entries.append(
                SuiteEntry(
                    stage_id=stage_id,
                    entry_id=str(raw.get("id") or "").strip(),
                    status=str(raw.get("status") or "active").strip(),
                    evidence_kind=str(raw.get("evidenceKind") or "").strip(),
                    baseline_json=resolve_repo_path(raw.get("baselineJson")),
                    report_markdown=resolve_repo_path(raw.get("reportMarkdown")),
                    command=command,
                    estimated_seconds=int(raw.get("estimatedSeconds") or 0),
                    raw=raw,
                )
            )
    return entries


def validate_entries(entries: list[SuiteEntry], require_existing_baselines: bool) -> list[str]:
    errors: list[str] = []
    seen_ids: set[str] = set()

    for entry in entries:
        label = f"{entry.stage_id}/{entry.entry_id or '<missing-id>'}"
        if not entry.entry_id:
            errors.append(f"{label}: missing entry id.")
            continue
        if entry.entry_id in seen_ids:
            errors.append(f"{label}: duplicate entry id.")
        seen_ids.add(entry.entry_id)

        if entry.status not in {"active", "manual", "planned", "disabled"}:
            errors.append(f"{label}: unsupported status '{entry.status}'.")
        if not entry.evidence_kind:
            errors.append(f"{label}: missing evidenceKind.")

        if entry.status in {"active", "manual"} and not entry.command:
            errors.append(f"{label}: active/manual entry must define command.")
        if entry.command and entry.command[0] == "dotnet":
            errors.extend(validate_dotnet_command(label, entry.command))

        if require_existing_baselines and entry.status == "active" and entry.baseline_json is not None:
            if not entry.baseline_json.exists():
                errors.append(f"{label}: baselineJson does not exist: {repo_relative(entry.baseline_json)}")
        if require_existing_baselines and entry.status == "active" and entry.report_markdown is not None:
            if not entry.report_markdown.exists():
                errors.append(f"{label}: reportMarkdown does not exist: {repo_relative(entry.report_markdown)}")

    return errors


def validate_dotnet_command(label: str, command: list[str]) -> list[str]:
    errors: list[str] = []
    if "--project" not in command:
        return errors
    project_index = command.index("--project") + 1
    if project_index >= len(command):
        return [f"{label}: dotnet command has --project without a path."]
    project_path = resolve_repo_path(command[project_index])
    if project_path is None or not project_path.exists():
        errors.append(f"{label}: dotnet project does not exist: {command[project_index]}")
    return errors


def print_entries(entries: list[SuiteEntry], include_planned: bool) -> None:
    for entry in entries:
        if entry.status == "planned" and not include_planned:
            continue
        command_text = " ".join(entry.command) if entry.command else "-"
        print(
            f"{entry.stage_id}/{entry.entry_id} "
            f"status={entry.status} evidence={entry.evidence_kind} "
            f"baseline={repo_relative(entry.baseline_json)}"
        )
        print(f"  command: {command_text}")


def selected_entries(entries: list[SuiteEntry], entry_ids: set[str], include_planned: bool) -> list[SuiteEntry]:
    selected: list[SuiteEntry] = []
    for entry in entries:
        if entry_ids and entry.entry_id not in entry_ids:
            continue
        if entry.status == "planned" and not include_planned:
            continue
        if entry.status in {"disabled", "planned"}:
            continue
        selected.append(entry)
    return selected


def run_entries(entries: list[SuiteEntry], dry_run: bool) -> int:
    for entry in entries:
        command_text = " ".join(entry.command)
        print(f"[suite] {entry.stage_id}/{entry.entry_id}: {command_text}")
        if dry_run:
            continue
        completed = subprocess.run(entry.command, cwd=REPO_ROOT)
        if completed.returncode != 0:
            print(f"[suite] failed: {entry.entry_id} exit={completed.returncode}", file=sys.stderr)
            return completed.returncode
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Run or inspect ClearVision quality suite manifests.")
    parser.add_argument("--suite", required=True, help="Suite name or path under quality/evals/suites.")
    parser.add_argument("--entry", action="append", default=[], help="Run only a specific entry id. Repeatable.")
    parser.add_argument("--list", action="store_true", help="Print suite entries.")
    parser.add_argument("--validate-only", action="store_true", help="Validate suite shape and existing active baselines.")
    parser.add_argument("--run", action="store_true", help="Execute selected active/manual entries serially.")
    parser.add_argument("--dry-run", action="store_true", help="Print selected commands without executing them.")
    parser.add_argument("--include-planned", action="store_true", help="Show planned entries in listing.")
    parser.add_argument(
        "--allow-missing-baseline",
        action="store_true",
        help="Do not require active baseline/report files to already exist during validation.",
    )
    args = parser.parse_args()

    path = suite_path(args.suite)
    suite = load_suite(path)
    entries = iter_entries(suite)
    errors = validate_entries(entries, require_existing_baselines=not args.allow_missing_baseline)
    if errors:
        for error in errors:
            print(f"error: {error}", file=sys.stderr)
        return 2

    if args.list:
        print_entries(entries, include_planned=args.include_planned)

    if args.validate_only and not args.run:
        print(f"validated {path.relative_to(REPO_ROOT).as_posix()} entries={len(entries)}")
        return 0

    if args.run or args.dry_run:
        run_list = selected_entries(entries, set(args.entry), include_planned=args.include_planned)
        if not run_list:
            print("No runnable entries selected.")
            return 0
        return run_entries(run_list, dry_run=args.dry_run or not args.run)

    if not args.list:
        print(f"validated {path.relative_to(REPO_ROOT).as_posix()} entries={len(entries)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
