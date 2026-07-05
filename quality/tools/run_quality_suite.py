from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
REPO_ROOT_RESOLVED = REPO_ROOT.resolve()
SUITE_DIR = REPO_ROOT / "quality" / "evals" / "suites"
DOTNET_SHIM = REPO_ROOT / "scripts" / "dotnet.ps1"
ALLOWED_COMMANDS = {"dotnet", "python", "powershell", "pwsh"}
COMMAND_REPO_PATH_FLAGS = {
    "--audit-output",
    "--audit-report",
    "--baseline-output",
    "--deploy-output",
    "--deploy-report",
    "--field-output",
    "--field-report",
    "--index",
    "--manifest",
    "--model-manifest",
    "--output",
    "--report",
}
COMMAND_OUTPUT_PATH_FLAGS = {
    "--audit-output",
    "--audit-report",
    "--baseline-output",
    "--deploy-output",
    "--deploy-report",
    "--field-output",
    "--field-report",
    "--output",
    "--report",
}
COMMAND_EXISTING_PATH_FLAGS = {"--index", "--manifest", "--model-manifest"}
_DOTNET_EXECUTABLE: str | None = None


@dataclass(frozen=True)
class SuiteEntry:
    stage_id: str
    entry_id: str
    status: str
    evidence_kind: str
    dataset_manifest: Path | None
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


def resolve_repo_path(value: Any) -> Path | None:
    if not isinstance(value, str) or not value.strip():
        return None
    path = Path(value.strip())
    if path.is_absolute():
        return path
    return REPO_ROOT / path


def is_within_repo(path: Path) -> bool:
    try:
        path.resolve(strict=False).relative_to(REPO_ROOT_RESOLVED)
    except ValueError:
        return False
    return True


def retained_summary_path(path: Path) -> Path:
    return path.with_name(f"{path.stem}.summary.json")


def suite_path(value: str) -> Path:
    raw = value.strip()
    if not raw:
        raise FileNotFoundError("Suite not found: <empty>")

    path = Path(raw)
    explicit_path = path.is_absolute() or path.parent != Path(".")
    if explicit_path:
        resolved_path = path if path.is_absolute() else REPO_ROOT / path
        if not is_within_repo(resolved_path):
            raise ValueError(f"Suite path must stay within the repository: {value}")
        if not resolved_path.exists():
            raise FileNotFoundError(f"Suite not found: {value}")
        if not resolved_path.is_file():
            raise ValueError(f"Suite path must be a file: {value}")
        return resolved_path

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
                    dataset_manifest=resolve_repo_path(raw.get("datasetManifest")),
                    baseline_json=resolve_repo_path(raw.get("baselineJson")),
                    report_markdown=resolve_repo_path(raw.get("reportMarkdown")),
                    command=command,
                    estimated_seconds=raw.get("estimatedSeconds") if is_strict_int(raw.get("estimatedSeconds")) else 0,
                    raw=raw,
                )
            )
    return entries


def validate_entries(
    entries: list[SuiteEntry],
    require_existing_baselines: bool,
    validate_artifact_content: bool,
) -> list[str]:
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

        if entry.status not in {"active", "manual", "planned", "disabled", "blocked-missing-field-data"}:
            errors.append(f"{label}: unsupported status '{entry.status}'.")
        if not entry.evidence_kind:
            errors.append(f"{label}: missing evidenceKind.")

        if entry.status in {"active", "manual"} and not entry.command:
            errors.append(f"{label}: active/manual entry must define command.")
        if entry.command:
            errors.extend(validate_command(label, entry.command))
            errors.extend(validate_command_path_arguments(label, entry))
        if entry.command and entry.command[0] == "dotnet":
            errors.extend(validate_dotnet_command(label, entry.command))
        errors.extend(validate_entry_paths(label, entry))
        errors.extend(validate_estimated_seconds(label, entry))

        if require_existing_baselines and entry.status == "active" and entry.baseline_json is not None:
            errors.extend(validate_baseline_presence(entry, label))
        if require_existing_baselines and entry.status == "active" and entry.dataset_manifest is not None:
            if not entry.dataset_manifest.exists():
                errors.append(f"{label}: datasetManifest does not exist: {repo_relative(entry.dataset_manifest)}")
        if require_existing_baselines and entry.status == "active" and entry.report_markdown is not None:
            if not entry.report_markdown.exists():
                errors.append(f"{label}: reportMarkdown does not exist: {repo_relative(entry.report_markdown)}")

        if require_existing_baselines and validate_artifact_content and entry.status == "active":
            errors.extend(validate_declared_artifact_content(entry))

    return errors


def validate_command(label: str, command: list[str]) -> list[str]:
    if not command:
        return []

    executable = command[0].strip()
    errors: list[str] = []
    if executable not in ALLOWED_COMMANDS:
        return [f"{label}: command executable is not allowed: {executable}"]

    if executable == "python":
        if len(command) < 2:
            return [f"{label}: python command must include a script or module."]
        if command[1] == "-m":
            if len(command) < 3 or not command[2].strip():
                return [f"{label}: python -m command must include a module name."]
            return []
        script_path = resolve_repo_path(command[1])
        if script_path is None or script_path.suffix.lower() != ".py":
            return [f"{label}: python command must target a repository .py script."]
        if not is_within_repo(script_path):
            errors.append(f"{label}: python script must stay within the repository: {command[1]}")
        elif not script_path.exists():
            errors.append(f"{label}: python script does not exist: {command[1]}")
        return errors

    if executable in {"powershell", "pwsh"}:
        lower_args = [arg.lower() for arg in command]
        if "-file" in lower_args:
            file_index = lower_args.index("-file") + 1
            if file_index >= len(command):
                return [f"{label}: PowerShell -File requires a script path."]
            script_path = resolve_repo_path(command[file_index])
            if script_path is None or script_path.suffix.lower() != ".ps1":
                return [f"{label}: PowerShell -File must target a repository .ps1 script."]
            if not is_within_repo(script_path):
                errors.append(f"{label}: PowerShell script must stay within the repository: {command[file_index]}")
            elif not script_path.exists():
                errors.append(f"{label}: PowerShell script does not exist: {command[file_index]}")
            return errors
        if "-command" in lower_args:
            command_index = lower_args.index("-command") + 1
            if command_index >= len(command) or not command[command_index].strip():
                return [f"{label}: PowerShell -Command requires a command body."]
            return []
        return [f"{label}: PowerShell command must use -File or -Command."]

    return errors


def validate_command_path_arguments(label: str, entry: SuiteEntry) -> list[str]:
    errors: list[str] = []
    command = entry.command
    for index, arg in enumerate(command[:-1]):
        value = command[index + 1]
        if arg in COMMAND_REPO_PATH_FLAGS:
            if value.startswith("-"):
                errors.append(f"{label}: {arg} requires a path value.")
                continue
            path = resolve_repo_path(value)
            if path is None:
                errors.append(f"{label}: {arg} requires a non-empty path value.")
                continue
            if not is_within_repo(path):
                errors.append(f"{label}: {arg} path must stay within the repository: {value}")
                continue
            if entry.status == "active" and arg in COMMAND_EXISTING_PATH_FLAGS and not path.exists():
                errors.append(f"{label}: {arg} path does not exist: {value}")
        elif arg == "--suite":
            if value.startswith("-"):
                errors.append(f"{label}: --suite requires a suite name or path.")
                continue
            try:
                suite_path(value)
            except (FileNotFoundError, ValueError) as exc:
                errors.append(f"{label}: --suite target is invalid: {value} ({exc})")

    for index, arg in enumerate(command):
        if arg in COMMAND_REPO_PATH_FLAGS or arg == "--suite":
            if index == len(command) - 1:
                errors.append(f"{label}: {arg} requires a value.")
    return errors


def validate_entry_paths(label: str, entry: SuiteEntry) -> list[str]:
    errors: list[str] = []
    for field, path in (
        ("datasetManifest", entry.dataset_manifest),
        ("fieldReplayManifest", resolve_repo_path(entry.raw.get("fieldReplayManifest"))),
        ("modelManifest", resolve_repo_path(entry.raw.get("modelManifest"))),
        ("baselineJson", entry.baseline_json),
        ("reportMarkdown", entry.report_markdown),
    ):
        raw = entry.raw.get(field)
        if raw is None or raw == "":
            continue
        if not isinstance(raw, str):
            errors.append(f"{label}: {field} must be a string path.")
            continue
        if path is None:
            errors.append(f"{label}: {field} must not be empty.")
            continue
        if not is_within_repo(path):
            errors.append(f"{label}: {field} must stay within the repository: {raw}")
    return errors


def validate_estimated_seconds(label: str, entry: SuiteEntry) -> list[str]:
    if "estimatedSeconds" not in entry.raw or entry.raw.get("estimatedSeconds") is None:
        return []
    value = entry.raw.get("estimatedSeconds")
    if not is_strict_int(value) or value < 0:
        return [f"{label}: estimatedSeconds must be a non-negative integer."]
    return []


def validate_dotnet_command(label: str, command: list[str]) -> list[str]:
    errors: list[str] = []
    if "--project" not in command:
        return errors
    project_index = command.index("--project") + 1
    if project_index >= len(command):
        return [f"{label}: dotnet command has --project without a path."]
    project_path = resolve_repo_path(command[project_index])
    if project_path is not None and not is_within_repo(project_path):
        errors.append(f"{label}: dotnet project must stay within the repository: {command[project_index]}")
        return errors
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
        if entry.status == "manual" and not entry_ids:
            continue
        if entry.status in {"disabled", "planned", "blocked-missing-field-data"}:
            continue
        selected.append(entry)
    return selected


def run_entries(entries: list[SuiteEntry], dry_run: bool) -> int:
    for entry in entries:
        command_text = " ".join(entry.command)
        print(f"[suite] {entry.stage_id}/{entry.entry_id}: {command_text}")
        if dry_run:
            continue
        started_at_ns = time.time_ns()
        resolved_command, env = resolve_command(entry.command)
        completed = subprocess.run(resolved_command, cwd=REPO_ROOT, env=env)
        if completed.returncode != 0:
            print(f"[suite] failed: {entry.entry_id} exit={completed.returncode}", file=sys.stderr)
            return completed.returncode
        artifact_errors = validate_fresh_artifacts(
            entry,
            started_at_ns,
            require_fresh=entry_refreshes_artifacts(entry),
        )
        if artifact_errors:
            for error in artifact_errors:
                print(f"[suite] artifact error: {error}", file=sys.stderr)
            return 2
    return 0


def entry_refreshes_artifacts(entry: SuiteEntry) -> bool:
    return "--validate-only" not in entry.command and "--dry-run" not in entry.command


def resolve_command(command: list[str]) -> tuple[list[str], dict[str, str] | None]:
    if command and command[0] == "dotnet" and DOTNET_SHIM.exists():
        env = os.environ.copy()
        env.setdefault("DOTNET_CLI_HOME", str(REPO_ROOT / ".dotnet_cli_home"))
        env.setdefault("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
        env.setdefault("DOTNET_NOLOGO", "1")
        return [resolve_dotnet_executable(), *command[1:]], env
    return command, None


def resolve_dotnet_executable() -> str:
    global _DOTNET_EXECUTABLE
    if _DOTNET_EXECUTABLE:
        return _DOTNET_EXECUTABLE

    completed = run_powershell(
        [
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(DOTNET_SHIM),
            "-PrintPath",
        ]
    )
    if completed.returncode != 0:
        details = (completed.stderr or completed.stdout or "").strip()
        raise RuntimeError(f"Unable to resolve repository dotnet SDK with {repo_relative(DOTNET_SHIM)}. {details}")

    lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    if not lines:
        raise RuntimeError(f"Repository dotnet resolver returned an empty path: {repo_relative(DOTNET_SHIM)}")

    _DOTNET_EXECUTABLE = lines[-1]
    return _DOTNET_EXECUTABLE


def run_powershell(args: list[str]) -> subprocess.CompletedProcess[str]:
    shells = ["pwsh"]
    if os.name == "nt":
        shells.append("powershell.exe")

    missing: list[str] = []
    for shell in shells:
        try:
            return subprocess.run(
                [shell, *args],
                cwd=REPO_ROOT,
                text=True,
                capture_output=True,
            )
        except FileNotFoundError:
            missing.append(shell)

    raise RuntimeError(f"Unable to start PowerShell. Tried: {', '.join(missing)}")


def validate_fresh_artifacts(entry: SuiteEntry, started_at_ns: int, require_fresh: bool) -> list[str]:
    errors: list[str] = []
    expected_artifacts = expected_artifact_paths(entry, include_command_outputs=require_fresh)
    freshness_floor_ns = started_at_ns - 2_000_000_000

    for artifact in expected_artifacts:
        if not artifact.exists():
            if artifact == entry.baseline_json and not require_fresh:
                errors.extend(validate_baseline_presence(entry, entry.entry_id))
            else:
                errors.append(f"{entry.entry_id}: expected artifact was not produced: {repo_relative(artifact)}")
            continue

        modified_at_ns = artifact.stat().st_mtime_ns
        if require_fresh and modified_at_ns < freshness_floor_ns:
            errors.append(
                f"{entry.entry_id}: artifact was not refreshed by this run: "
                f"{repo_relative(artifact)}"
            )

    errors.extend(validate_declared_artifact_content(entry))
    return errors


def expected_artifact_paths(entry: SuiteEntry, include_command_outputs: bool) -> list[Path]:
    artifacts = [
        path for path in (entry.baseline_json, entry.report_markdown)
        if path is not None
    ]
    if include_command_outputs:
        artifacts.extend(command_output_artifact_paths(entry.command))

    unique_artifacts: list[Path] = []
    seen: set[Path] = set()
    for artifact in artifacts:
        key = artifact.resolve(strict=False)
        if key in seen:
            continue
        seen.add(key)
        unique_artifacts.append(artifact)
    return unique_artifacts


def command_output_artifact_paths(command: list[str]) -> list[Path]:
    artifacts: list[Path] = []
    for index, arg in enumerate(command[:-1]):
        if arg not in COMMAND_OUTPUT_PATH_FLAGS:
            continue
        value = command[index + 1]
        if value.startswith("-"):
            continue
        path = resolve_repo_path(value)
        if path is not None and is_within_repo(path):
            artifacts.append(path)
    return artifacts


def validate_declared_artifact_content(entry: SuiteEntry) -> list[str]:
    errors: list[str] = []

    if entry.report_markdown is not None and entry.report_markdown.exists() and entry.report_markdown.stat().st_size == 0:
        errors.append(f"{entry.entry_id}: reportMarkdown is empty: {repo_relative(entry.report_markdown)}")

    if entry.baseline_json is not None and not entry.baseline_json.exists():
        errors.extend(validate_retained_summary(entry))
        return errors

    if entry.evidence_kind != "contract" or entry.baseline_json is None:
        return errors

    errors.extend(validate_contract_baseline(entry))
    return errors


def validate_baseline_presence(entry: SuiteEntry, label: str) -> list[str]:
    if entry.baseline_json is None or entry.baseline_json.exists():
        return []

    summary_path = retained_summary_path(entry.baseline_json)
    if summary_path.exists():
        return validate_retained_summary(entry, label=label)

    return [f"{label}: baselineJson does not exist: {repo_relative(entry.baseline_json)}"]


def validate_retained_summary(entry: SuiteEntry, label: str | None = None) -> list[str]:
    label = label or entry.entry_id
    if entry.baseline_json is None:
        return []

    summary_path = retained_summary_path(entry.baseline_json)
    if not summary_path.exists():
        return [f"{label}: retained summary does not exist: {repo_relative(summary_path)}"]

    errors: list[str] = []
    try:
        with summary_path.open("r", encoding="utf-8") as handle:
            retained = json.load(handle)
    except Exception as exc:  # noqa: BLE001 - surface retained evidence parse details.
        return [f"{label}: retained summary is not valid JSON: {repo_relative(summary_path)} ({exc})"]

    if not isinstance(retained, dict):
        return [f"{label}: retained summary root must be an object: {repo_relative(summary_path)}"]

    if retained.get("schemaVersion") != "quality-report-summary/v1":
        errors.append(f"{label}: retained summary schemaVersion must be quality-report-summary/v1.")
    if str(retained.get("evidenceKind") or "").strip() != entry.evidence_kind:
        errors.append(
            f"{label}: retained summary evidenceKind must be {entry.evidence_kind}, "
            f"found {retained.get('evidenceKind')!r}."
        )

    source_report = retained.get("sourceReport")
    if not isinstance(source_report, dict):
        errors.append(f"{label}: retained summary missing sourceReport object.")
        source_report = {}
    original_path = str(source_report.get("originalPath") or "").strip()
    if original_path != repo_relative(entry.baseline_json):
        errors.append(f"{label}: retained summary originalPath must match baselineJson: {original_path!r}.")
    retention_decision = str(source_report.get("retentionDecision") or "")
    if "removed-from-git" not in retention_decision:
        errors.append(f"{label}: retained summary retentionDecision must document raw payload removal.")
    original_sha = str(source_report.get("originalSha256") or "").strip()
    if not original_sha.startswith("sha256:") or len(original_sha) <= len("sha256:"):
        errors.append(f"{label}: retained summary originalSha256 must be present.")
    original_size = source_report.get("originalSizeBytes")
    if not is_strict_int(original_size) or original_size <= 0:
        errors.append(f"{label}: retained summary originalSizeBytes must be a positive integer.")

    if "accepted" in retained and retained.get("accepted") is not True:
        errors.append(f"{label}: retained summary accepted must be true.")

    summary = retained.get("summary")
    if not isinstance(summary, dict):
        errors.append(f"{label}: retained summary missing summary object.")
        summary = {}

    validate_summary_counts(label, summary, errors)
    validate_summary_operators(label, retained, entry, errors)
    return errors


def validate_summary_counts(label: str, summary: dict[str, Any], errors: list[str]) -> None:
    if "CaseCount" in summary or "Passed" in summary or "Failed" in summary:
        case_count = read_nonnegative_int(summary, "CaseCount", label, Path(label), errors, prefix="retained summary")
        passed = read_nonnegative_int(summary, "Passed", label, Path(label), errors, prefix="retained summary")
        failed = read_nonnegative_int(summary, "Failed", label, Path(label), errors, prefix="retained summary")
        if case_count is not None and case_count <= 0:
            errors.append(f"{label}: retained summary CaseCount must be greater than 0.")
        if case_count is not None and passed is not None and passed != case_count:
            errors.append(f"{label}: retained summary Passed ({passed}) must equal CaseCount ({case_count}).")
        if failed is not None and failed != 0:
            errors.append(f"{label}: retained summary Failed must be 0, found {failed}.")

    if "operatorCount" in summary or "acceptedCount" in summary or "failedCount" in summary:
        operator_count = read_nonnegative_int(summary, "operatorCount", label, Path(label), errors, prefix="retained summary")
        accepted_count = read_nonnegative_int(summary, "acceptedCount", label, Path(label), errors, prefix="retained summary")
        failed_count = read_nonnegative_int(summary, "failedCount", label, Path(label), errors, prefix="retained summary")
        if operator_count is not None and operator_count <= 0:
            errors.append(f"{label}: retained summary operatorCount must be greater than 0.")
        if operator_count is not None and accepted_count is not None and accepted_count != operator_count:
            errors.append(
                f"{label}: retained summary acceptedCount ({accepted_count}) must equal operatorCount ({operator_count})."
            )
        if failed_count is not None and failed_count != 0:
            errors.append(f"{label}: retained summary failedCount must be 0, found {failed_count}.")


def validate_summary_operators(
    label: str,
    retained: dict[str, Any],
    entry: SuiteEntry,
    errors: list[str],
) -> None:
    expected_operators = read_expected_operators(
        label,
        entry,
        errors,
        required=entry.evidence_kind == "contract",
    )
    operators = retained.get("operators") or retained.get("Operators")
    if not isinstance(operators, list) or not operators:
        errors.append(f"{label}: retained summary must contain a non-empty operators array.")
        return

    actual: set[str] = set()
    for item in operators:
        if not isinstance(item, dict):
            errors.append(f"{label}: retained summary operators contains a non-object entry.")
            continue
        operator_name = str(item.get("operator") or item.get("Operator") or "").strip()
        if not operator_name:
            errors.append(f"{label}: retained summary operators contains an entry without operator name.")
            continue
        if operator_name in actual:
            errors.append(f"{label}: retained summary duplicate operator: {operator_name}")
        actual.add(operator_name)

        accepted = item.get("accepted")
        if accepted is not None and accepted is not True:
            errors.append(f"{label}: retained summary operator {operator_name} accepted must be true.")
        if item.get("Failed") not in (None, 0):
            errors.append(f"{label}: retained summary operator {operator_name} Failed must be 0.")
        if item.get("missingCaseResults") is True:
            errors.append(f"{label}: retained summary operator {operator_name} has missingCaseResults.")

    if expected_operators:
        expected_set = set(expected_operators)
        if actual != expected_set:
            errors.append(
                f"{label}: retained summary operators must match suite entry operators "
                f"expected={sorted(expected_set)} actual={sorted(actual)}."
            )


def validate_contract_baseline(entry: SuiteEntry) -> list[str]:
    label = entry.entry_id
    path = entry.baseline_json
    assert path is not None
    errors: list[str] = []

    try:
        with path.open("r", encoding="utf-8") as handle:
            baseline = json.load(handle)
    except Exception as exc:  # noqa: BLE001 - surface artifact parse details in CI.
        return [f"{label}: baselineJson is not valid JSON: {repo_relative(path)} ({exc})"]

    if not isinstance(baseline, dict):
        return [f"{label}: baselineJson root must be an object: {repo_relative(path)}"]

    summary = baseline.get("Summary")
    if not isinstance(summary, dict):
        errors.append(f"{label}: baselineJson is missing Summary object: {repo_relative(path)}")
        summary = {}

    summary_case_count = read_nonnegative_int(summary, "CaseCount", label, path, errors)
    summary_passed = read_nonnegative_int(summary, "Passed", label, path, errors)
    summary_failed = read_nonnegative_int(summary, "Failed", label, path, errors)

    if summary_case_count is not None and summary_case_count <= 0:
        errors.append(f"{label}: Summary.CaseCount must be greater than 0: {repo_relative(path)}")
    if summary_case_count is not None and summary_passed is not None and summary_passed != summary_case_count:
        errors.append(
            f"{label}: Summary.Passed ({summary_passed}) must equal Summary.CaseCount ({summary_case_count})."
        )
    if summary_failed is not None and summary_failed != 0:
        errors.append(f"{label}: Summary.Failed must be 0, found {summary_failed}.")

    operators = baseline.get("Operators")
    if not isinstance(operators, list) or not operators:
        errors.append(f"{label}: baselineJson must contain a non-empty Operators array: {repo_relative(path)}")
        operators = []

    expected_operators = read_expected_operators(label, entry, errors, required=True)
    expected_operator_set = set(expected_operators)

    operator_map: dict[str, dict[str, Any]] = {}
    for item in operators:
        if not isinstance(item, dict):
            errors.append(f"{label}: Operators contains a non-object entry.")
            continue
        operator_name = str(item.get("Operator") or "").strip()
        if not operator_name:
            errors.append(f"{label}: Operators contains an entry without Operator.")
            continue
        if operator_name in operator_map:
            errors.append(f"{label}: duplicate operator in baselineJson: {operator_name}")
            continue
        if expected_operator_set and operator_name not in expected_operator_set:
            errors.append(f"{label}: unexpected operator in baselineJson: {operator_name}")
        operator_map[operator_name] = item

    expected_case_sum = 0
    for operator_name in expected_operators:
        operator = operator_map.get(operator_name)
        if operator is None:
            errors.append(f"{label}: expected operator missing from baselineJson: {operator_name}")
            continue

        case_count = read_nonnegative_int(operator, "CaseCount", label, path, errors, context=operator_name)
        passed = read_nonnegative_int(operator, "Passed", label, path, errors, context=operator_name)
        failed = read_nonnegative_int(operator, "Failed", label, path, errors, context=operator_name)

        if case_count is not None:
            expected_case_sum += case_count
            if case_count <= 0:
                errors.append(f"{label}: operator {operator_name} CaseCount must be greater than 0.")
        if case_count is not None and passed is not None and passed != case_count:
            errors.append(
                f"{label}: operator {operator_name} Passed ({passed}) must equal CaseCount ({case_count})."
            )
        if failed is not None and failed != 0:
            errors.append(f"{label}: operator {operator_name} Failed must be 0, found {failed}.")

    if summary_case_count is not None and expected_case_sum != summary_case_count:
        errors.append(
            f"{label}: expected operator CaseCount sum ({expected_case_sum}) must equal Summary.CaseCount ({summary_case_count})."
        )

    return errors


def read_expected_operators(
    label: str,
    entry: SuiteEntry,
    errors: list[str],
    required: bool,
) -> list[str]:
    raw_operators = entry.raw.get("operators")
    if raw_operators is None and not required:
        return []
    if not isinstance(raw_operators, list) or not raw_operators:
        errors.append(f"{label}: suite entry operators must be a non-empty list of strings.")
        return []

    expected_operators: list[str] = []
    seen: set[str] = set()
    for item in raw_operators:
        if not isinstance(item, str) or not item.strip():
            errors.append(f"{label}: suite entry operators must be a non-empty list of strings.")
            return []
        operator_name = item.strip()
        if operator_name in seen:
            errors.append(f"{label}: duplicate operator in suite entry: {operator_name}")
            continue
        seen.add(operator_name)
        expected_operators.append(operator_name)
    return expected_operators


def read_nonnegative_int(
    source: dict[str, Any],
    field: str,
    label: str,
    path: Path,
    errors: list[str],
    context: str | None = None,
    prefix: str = "baselineJson",
) -> int | None:
    if field not in source:
        location = f"{context}.{field}" if context else f"Summary.{field}"
        errors.append(f"{label}: {prefix} missing {location}: {repo_relative(path)}")
        return None
    raw = source[field]
    if not is_strict_int(raw):
        location = f"{context}.{field}" if context else f"Summary.{field}"
        errors.append(f"{label}: {prefix} {location} must be an integer: {raw!r}")
        return None
    value = raw
    if value < 0:
        location = f"{context}.{field}" if context else f"Summary.{field}"
        errors.append(f"{label}: {prefix} {location} must be non-negative: {value}")
        return None
    return value


def is_strict_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


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

    try:
        path = suite_path(args.suite)
        suite = load_suite(path)
        entries = iter_entries(suite)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    errors = validate_entries(
        entries,
        require_existing_baselines=not args.allow_missing_baseline and not (args.run or args.dry_run),
        validate_artifact_content=not (args.run or args.dry_run),
    )
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
            print("error: no runnable entries selected.", file=sys.stderr)
            return 2
        return run_entries(run_list, dry_run=args.dry_run or not args.run)

    if not args.list:
        print(f"validated {path.relative_to(REPO_ROOT).as_posix()} entries={len(entries)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
