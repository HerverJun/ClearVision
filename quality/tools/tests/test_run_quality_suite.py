from __future__ import annotations

import json
import sys
import tempfile
import time
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import run_quality_suite as rqs  # noqa: E402


class RunQualitySuiteTests(unittest.TestCase):
    def setUp(self) -> None:
        self._old_globals = {
            "REPO_ROOT": rqs.REPO_ROOT,
            "REPO_ROOT_RESOLVED": rqs.REPO_ROOT_RESOLVED,
            "SUITE_DIR": rqs.SUITE_DIR,
            "DOTNET_SHIM": rqs.DOTNET_SHIM,
            "_DOTNET_EXECUTABLE": rqs._DOTNET_EXECUTABLE,
        }
        self._tmp = tempfile.TemporaryDirectory()
        self.repo_root = Path(self._tmp.name)
        self.suite_dir = self.repo_root / "quality" / "evals" / "suites"
        self.report_dir = self.repo_root / "quality" / "evals" / "reports"
        self.tool_dir = self.repo_root / "quality" / "tools"
        self.script_dir = self.repo_root / "scripts"
        for directory in (self.suite_dir, self.report_dir, self.tool_dir, self.script_dir):
            directory.mkdir(parents=True, exist_ok=True)
        (self.tool_dir / "fake_runner.py").write_text("print('ok')\n", encoding="utf-8")
        (self.script_dir / "fake.ps1").write_text("Write-Output ok\n", encoding="utf-8")

        rqs.REPO_ROOT = self.repo_root
        rqs.REPO_ROOT_RESOLVED = self.repo_root.resolve()
        rqs.SUITE_DIR = self.suite_dir
        rqs.DOTNET_SHIM = self.script_dir / "dotnet.ps1"
        rqs._DOTNET_EXECUTABLE = None

    def tearDown(self) -> None:
        rqs.REPO_ROOT = self._old_globals["REPO_ROOT"]
        rqs.REPO_ROOT_RESOLVED = self._old_globals["REPO_ROOT_RESOLVED"]
        rqs.SUITE_DIR = self._old_globals["SUITE_DIR"]
        rqs.DOTNET_SHIM = self._old_globals["DOTNET_SHIM"]
        rqs._DOTNET_EXECUTABLE = self._old_globals["_DOTNET_EXECUTABLE"]
        self._tmp.cleanup()

    def make_entry(
        self,
        *,
        raw_overrides: dict[str, object] | None = None,
        baseline_name: str = "contract_baseline.json",
        include_report: bool = False,
    ) -> rqs.SuiteEntry:
        raw: dict[str, object] = {
            "id": "contract_entry",
            "status": "active",
            "evidenceKind": "contract",
            "baselineJson": f"quality/evals/reports/{baseline_name}",
            "reportMarkdown": "quality/evals/reports/contract_baseline.md",
            "command": ["python", "quality/tools/fake_runner.py"],
            "operators": ["Foo"],
            "estimatedSeconds": 1,
        }
        if raw_overrides:
            raw.update(raw_overrides)
        if include_report:
            (self.report_dir / "contract_baseline.md").write_text("# report\n", encoding="utf-8")
        return rqs.SuiteEntry(
            stage_id="stage",
            entry_id=str(raw.get("id") or ""),
            status=str(raw.get("status") or "active"),
            evidence_kind=str(raw.get("evidenceKind") or ""),
            dataset_manifest=rqs.resolve_repo_path(raw.get("datasetManifest")),
            baseline_json=rqs.resolve_repo_path(raw.get("baselineJson")),
            report_markdown=rqs.resolve_repo_path(raw.get("reportMarkdown")),
            command=list(raw.get("command") or []),
            estimated_seconds=int(raw.get("estimatedSeconds") or 0),
            raw=raw,
        )

    def write_retained_summary(
        self,
        entry: rqs.SuiteEntry,
        *,
        operators: list[dict[str, object]] | None = None,
        summary: dict[str, object] | None = None,
    ) -> Path:
        assert entry.baseline_json is not None
        summary_path = rqs.retained_summary_path(entry.baseline_json)
        payload = {
            "schemaVersion": "quality-report-summary/v1",
            "evidenceKind": entry.evidence_kind,
            "sourceReport": {
                "originalPath": rqs.repo_relative(entry.baseline_json),
                "retentionDecision": "removed-from-git after summary retention",
                "originalSha256": "sha256:abc123",
                "originalSizeBytes": 128,
            },
            "accepted": True,
            "summary": summary or {"CaseCount": 1, "Passed": 1, "Failed": 0},
            "operators": operators or [{"operator": "Foo", "accepted": True, "Failed": 0}],
        }
        summary_path.write_text(json.dumps(payload), encoding="utf-8")
        return summary_path

    def test_retained_summary_requires_suite_operators(self) -> None:
        entry = self.make_entry(raw_overrides={"operators": None})
        self.write_retained_summary(entry)

        errors = rqs.validate_declared_artifact_content(entry)

        self.assertTrue(any("suite entry operators must be a non-empty list of strings" in error for error in errors))

    def test_retained_summary_rejects_duplicate_suite_operators(self) -> None:
        entry = self.make_entry(raw_overrides={"operators": ["Foo", "Foo"]})
        self.write_retained_summary(entry)

        errors = rqs.validate_declared_artifact_content(entry)

        self.assertTrue(any("duplicate operator in suite entry: Foo" in error for error in errors))

    def test_retained_summary_rejects_operator_mismatch(self) -> None:
        entry = self.make_entry(raw_overrides={"operators": ["Foo", "Bar"]})
        self.write_retained_summary(entry)

        errors = rqs.validate_declared_artifact_content(entry)

        self.assertTrue(any("retained summary operators must match suite entry operators" in error for error in errors))

    def test_retained_summary_passes_when_raw_baseline_removed(self) -> None:
        entry = self.make_entry()
        self.write_retained_summary(entry)

        errors = rqs.validate_declared_artifact_content(entry)

        self.assertEqual([], errors)

    def test_non_contract_retained_summary_does_not_require_suite_operators(self) -> None:
        entry = self.make_entry(raw_overrides={"evidenceKind": "public-benchmark", "operators": None})
        self.write_retained_summary(entry)

        errors = rqs.validate_declared_artifact_content(entry)

        self.assertEqual([], errors)

    def test_generating_entry_requires_fresh_raw_artifact_even_with_summary(self) -> None:
        entry = self.make_entry()
        self.write_retained_summary(entry)

        errors = rqs.validate_fresh_artifacts(entry, time.time_ns(), require_fresh=True)

        self.assertTrue(any("expected artifact was not produced" in error for error in errors))

    def test_generating_entry_requires_auxiliary_command_outputs(self) -> None:
        entry = self.make_entry(
            raw_overrides={
                "baselineJson": None,
                "reportMarkdown": None,
                "command": [
                    "python",
                    "quality/tools/fake_runner.py",
                    "--field-output",
                    "quality/evals/reports/field.json",
                    "--field-report",
                    "quality/evals/reports/field.md",
                ],
            }
        )

        errors = rqs.validate_fresh_artifacts(entry, time.time_ns(), require_fresh=True)

        self.assertTrue(any("field.json" in error and "expected artifact was not produced" in error for error in errors))
        self.assertTrue(any("field.md" in error and "expected artifact was not produced" in error for error in errors))

    def test_dry_run_entry_does_not_refresh_artifacts(self) -> None:
        entry = self.make_entry(
            raw_overrides={
                "command": [
                    "python",
                    "quality/tools/fake_runner.py",
                    "--output",
                    "quality/evals/reports/generated.json",
                    "--dry-run",
                ],
            }
        )

        self.assertFalse(rqs.entry_refreshes_artifacts(entry))

    def test_validate_only_entry_accepts_retained_summary(self) -> None:
        entry = self.make_entry(raw_overrides={"reportMarkdown": None})
        self.write_retained_summary(entry)

        errors = rqs.validate_fresh_artifacts(entry, time.time_ns(), require_fresh=False)

        self.assertEqual([], errors)

    def test_command_output_path_cannot_escape_repo(self) -> None:
        entry = self.make_entry(
            raw_overrides={"command": ["python", "quality/tools/fake_runner.py", "--output", "../outside.json"]}
        )

        errors = rqs.validate_command_path_arguments("stage/contract_entry", entry)

        self.assertTrue(any("--output path must stay within the repository" in error for error in errors))

    def test_explicit_nested_suite_path_must_exist(self) -> None:
        entry = self.make_entry(
            raw_overrides={"command": ["python", "quality/tools/fake_runner.py", "--suite", "missing/path/suite.json"]}
        )

        errors = rqs.validate_command_path_arguments("stage/contract_entry", entry)

        self.assertTrue(any("--suite target is invalid" in error for error in errors))

    def test_estimated_seconds_rejects_bool_without_traceback(self) -> None:
        suite = {
            "stages": [
                {
                    "id": "stage",
                    "entries": [
                        {
                            "id": "bool_seconds",
                            "status": "active",
                            "evidenceKind": "contract",
                            "command": ["python", "quality/tools/fake_runner.py"],
                            "operators": ["Foo"],
                            "estimatedSeconds": True,
                        }
                    ],
                }
            ]
        }
        entries = rqs.iter_entries(suite)

        errors = rqs.validate_entries(entries, require_existing_baselines=False, validate_artifact_content=False)

        self.assertTrue(any("estimatedSeconds must be a non-negative integer" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
