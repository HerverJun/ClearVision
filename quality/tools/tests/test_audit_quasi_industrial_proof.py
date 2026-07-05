from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import audit_quasi_industrial_proof as audit  # noqa: E402


VALID_SHA = "c" * 64


def retained_public_proof() -> dict[str, object]:
    operators: list[dict[str, object]] = []
    names = [
        "DeepLearning",
        "TemplateMatching",
        "AnomalyDetection",
        "SurfaceDefectDetection",
        "EdgeDetection",
        "CameraCalibration",
        "AkazeFeatureMatch",
        "OrbFeatureMatch",
    ]
    for name in names:
        operators.append(
            {
                "operator": name,
                "datasetId": f"{name.lower()}-dataset",
                "proofLevel": "public-benchmark",
                "evidenceClaim": "public benchmark proof",
                "industrialStatus": "real field sign-off pending; industrial validation not complete",
                "sourceBaseline": (
                    "quality/evals/reports/DeepLearning_coco_real_model_baseline.json"
                    if name == "DeepLearning"
                    else f"quality/evals/reports/{name}_baseline.json"
                ),
                "manifestSha256": VALID_SHA,
                "metrics": {"CaseCount": 1},
                "splitSummary": {"test": 1},
                "failureTaxonomy": {"passed": 1},
                "privacyLeakCount": 0,
                "missingCaseResults": False,
                "accepted": True,
                "thresholdResultCount": 1,
                "perCaseResultCount": 1,
                "replayCaseCount": 1,
            }
        )
    return {
        "schemaVersion": "quality-report-summary/v1",
        "evidenceKind": "public-benchmark-proof",
        "sourceReport": {
            "originalPath": "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json",
            "originalSha256": f"sha256:{VALID_SHA}",
            "originalSizeBytes": 128,
            "retentionDecision": "raw-json-removed-from-git",
        },
        "accepted": True,
        "summary": {
            "operatorCount": len(operators),
            "acceptedCount": len(operators),
            "failedCount": 0,
            "realIndustrialValidationComplete": 0,
            "replayCaseCount": len(operators),
        },
        "operators": operators,
    }


def replay_manifest(source_sha: str = VALID_SHA) -> dict[str, object]:
    proof = retained_public_proof()
    cases: list[dict[str, object]] = []
    for row in proof["operators"]:
        operator = row["operator"]
        command = [
            "python",
            "quality/tools/run_algorithm_ab_replay.py",
            "--execute-camera-calibration" if operator == "CameraCalibration" else "--execute-matching",
        ]
        cases.append(
            {
                "caseId": f"{operator}-case-001",
                "operator": operator,
                "datasetId": row["datasetId"],
                "replayClass": "boundary",
                "triageLabel": "worst-case",
                "replayCommand": command,
            }
        )
    return {
        "schemaVersion": audit.PUBLIC_REPLAY_SCHEMA_VERSION,
        "sourceProofBaseline": "quality/evals/reports/QualityFlywheel_public_benchmark_proof_baseline.json",
        "sourceProofSha256": source_sha,
        "accepted": True,
        "summary": {
            "replayCaseCount": len(cases),
            "operatorCount": len(cases),
            "classCounts": {"boundary": len(cases)},
        },
        "cases": cases,
    }


class QuasiIndustrialAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        self._old_repo_root = audit.REPO_ROOT
        self._old_report_dir = audit.REPORT_DIR
        self._tmp = tempfile.TemporaryDirectory()
        self.repo_root = Path(self._tmp.name)
        self.report_dir = self.repo_root / "quality" / "evals" / "reports"
        self.report_dir.mkdir(parents=True, exist_ok=True)
        audit.REPO_ROOT = self.repo_root
        audit.REPORT_DIR = self.report_dir

    def tearDown(self) -> None:
        audit.REPO_ROOT = self._old_repo_root
        audit.REPORT_DIR = self._old_report_dir
        self._tmp.cleanup()

    def write_public_benchmark_inputs(self, replay: dict[str, object]) -> None:
        (self.report_dir / "QualityFlywheel_public_benchmark_proof_baseline.summary.json").write_text(
            json.dumps(retained_public_proof()),
            encoding="utf-8",
        )
        (self.report_dir / "QualityFlywheel_public_benchmark_replay_manifest.json").write_text(
            json.dumps(replay),
            encoding="utf-8",
        )

    def inspect_public_checks(self) -> dict[str, bool]:
        checks: list[dict[str, object]] = []
        audit.inspect_public_benchmark_proof(checks)
        return {str(check["id"]): bool(check["passed"]) for check in checks}

    def test_public_benchmark_replay_binding_passes_for_matching_sha(self) -> None:
        self.write_public_benchmark_inputs(replay_manifest())

        checks = self.inspect_public_checks()

        self.assertTrue(checks["public_benchmark_replay_source_sha_matches_proof"])
        self.assertTrue(checks["public_benchmark_replay_summary_counts_consistent"])
        self.assertTrue(checks["public_benchmark_replay_commands_allowed"])

    def test_public_benchmark_replay_binding_rejects_stale_sha(self) -> None:
        self.write_public_benchmark_inputs(replay_manifest(source_sha="d" * 64))

        checks = self.inspect_public_checks()

        self.assertFalse(checks["public_benchmark_replay_source_sha_matches_proof"])

    def test_public_benchmark_replay_rejects_unapproved_command(self) -> None:
        replay = replay_manifest()
        replay["cases"] = copy.deepcopy(replay["cases"])
        replay["cases"][0]["replayCommand"] = ["python", "-c", "print('skip replay')"]
        self.write_public_benchmark_inputs(replay)

        checks = self.inspect_public_checks()

        self.assertFalse(checks["public_benchmark_replay_commands_allowed"])


if __name__ == "__main__":
    unittest.main()
