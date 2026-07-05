from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import run_public_benchmark_proof as proof  # noqa: E402


VALID_SHA = "a" * 64


def valid_replay_manifest() -> dict[str, object]:
    return {
        "schemaVersion": proof.REPLAY_SCHEMA_VERSION,
        "generatedAtUtc": "2026-04-29T00:00:00Z",
        "sourceProofBaseline": proof.repo(proof.OUTPUT_JSON),
        "sourceProofSha256": VALID_SHA,
        "accepted": True,
        "summary": {
            "replayCaseCount": 2,
            "operatorCount": 2,
            "classCounts": {"boundary": 1, "failure": 1},
        },
        "cases": [
            {
                "caseId": "calibration-001",
                "split": "test",
                "operator": "CameraCalibration",
                "datasetId": "opencv_calibration_samples",
                "replayClass": "boundary",
                "triageLabel": "worst-ReprojectionRmsPx",
                "boundaryMetric": "ReprojectionRmsPx",
                "boundaryMetricValue": 0.1,
                "replayCommand": [
                    "python",
                    "quality/tools/run_algorithm_ab_replay.py",
                    "--execute-camera-calibration",
                ],
            },
            {
                "caseId": "matching-001",
                "split": "test",
                "operator": "TemplateMatching",
                "datasetId": "hpatches-style-homography-bridge",
                "replayClass": "failure",
                "triageLabel": "threshold-failed",
                "boundaryMetric": "PositionErrorPx",
                "boundaryMetricValue": 9.0,
                "replayCommand": [
                    "python",
                    "quality/tools/run_algorithm_ab_replay.py",
                    "--execute-matching",
                ],
            },
        ],
    }


def valid_retained_summary() -> dict[str, object]:
    return {
        "schemaVersion": "quality-report-summary/v1",
        "evidenceKind": "public-benchmark-proof",
        "sourceReport": {
            "originalPath": proof.repo(proof.OUTPUT_JSON),
            "originalSha256": f"sha256:{VALID_SHA}",
            "originalSizeBytes": 128,
            "retentionDecision": "raw-json-removed-from-git",
        },
        "accepted": True,
        "summary": {
            "operatorCount": 8,
            "acceptedCount": 8,
            "failedCount": 0,
            "realIndustrialValidationComplete": 0,
            "replayCaseCount": 8,
        },
        "requiredRunnerFields": sorted(proof.REQUIRED_RESULT_FIELDS),
        "operators": [
            {
                "operator": f"Operator{index}",
                "datasetId": f"dataset-{index}",
                "proofLevel": "public-benchmark",
                "industrialStatus": "real field sign-off pending; industrial validation not complete",
                "sourceBaseline": f"quality/evals/reports/operator_{index}.json",
                "sourceBaselineSha256": VALID_SHA,
                "manifestSha256": VALID_SHA,
                "metrics": {"CaseCount": 1},
                "thresholdResultCount": 1,
                "perCaseResultCount": 1,
                "replayCaseCount": 1,
                "privacyLeakCount": 0,
                "missingCaseResults": False,
                "accepted": True,
            }
            for index in range(8)
        ],
    }


class PublicBenchmarkProofTests(unittest.TestCase):
    def test_replay_manifest_accepts_current_proof_binding(self) -> None:
        errors = proof.validate_replay_manifest(
            valid_replay_manifest(),
            expected_source_sha=VALID_SHA,
            expected_replay_case_count=2,
            expected_operator_count=2,
        )

        self.assertEqual([], errors)

    def test_replay_manifest_rejects_source_sha_mismatch(self) -> None:
        errors = proof.validate_replay_manifest(
            valid_replay_manifest(),
            expected_source_sha="b" * 64,
            expected_replay_case_count=2,
            expected_operator_count=2,
        )

        self.assertTrue(any("sourceProofSha256 must match" in error for error in errors))

    def test_replay_manifest_rejects_summary_count_mismatch(self) -> None:
        manifest = valid_replay_manifest()
        manifest["summary"] = copy.deepcopy(manifest["summary"])
        manifest["summary"]["replayCaseCount"] = 99

        errors = proof.validate_replay_manifest(
            manifest,
            expected_source_sha=VALID_SHA,
            expected_replay_case_count=2,
            expected_operator_count=2,
        )

        self.assertTrue(any("summary.replayCaseCount must equal case count" in error for error in errors))
        self.assertTrue(any("summary.replayCaseCount must match proof summary" in error for error in errors))

    def test_replay_manifest_rejects_unapproved_command(self) -> None:
        manifest = valid_replay_manifest()
        manifest["cases"] = copy.deepcopy(manifest["cases"])
        manifest["cases"][0]["replayCommand"] = ["python", "-c", "print('not a replay')"]

        errors = proof.validate_replay_manifest(
            manifest,
            expected_source_sha=VALID_SHA,
            expected_replay_case_count=2,
            expected_operator_count=2,
        )

        self.assertTrue(any("replayCommand is not allowed" in error for error in errors))

    def test_retained_summary_rejects_bool_counts(self) -> None:
        summary = valid_retained_summary()
        summary["operators"] = copy.deepcopy(summary["operators"])
        summary["operators"][0]["perCaseResultCount"] = True

        errors = proof.validate_retained_summary(summary)

        self.assertTrue(any("perCaseResultCount must be positive" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
