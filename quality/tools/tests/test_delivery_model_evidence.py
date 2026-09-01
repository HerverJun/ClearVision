from __future__ import annotations

import copy
import json
import sys
import unittest
from pathlib import Path

import jsonschema


TOOLS_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLS_DIR.parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import evaluate_delivery_model_evidence as evidence  # noqa: E402


HASH_A = "a" * 64
HASH_B = "b" * 64


def valid_manifest() -> dict[str, object]:
    return {
        "schemaVersion": "clearvision.delivery-model-manifest/v1",
        "model": {"id": "delivery-v1", "sha256": HASH_A, "artifactPath": "external://model", "labels": ["part"]},
        "inputContract": {"tensorName": "images", "shape": [1, 3, 640, 640], "dataType": "float32", "colorOrder": "RGB", "normalization": "0..1"},
        "outputContract": {"tensorNames": ["output0"], "schema": "boxes/classes/scores", "postprocess": "class-aware NMS"},
        "dataset": {"manifestPath": "external://dataset", "version": "v1", "sha256": HASH_B},
        "acceptanceThresholds": {"precisionAt50": 0.45, "recallAt50": 0.35, "ap50": 0.45},
        "providerPolicy": {
            "profiles": {
                "CPU": {"policy": "required", "requiredHardware": "target CPU"},
                "CUDA": {"policy": "optional", "requiredHardware": "target GPU"},
                "TensorRT": {"policy": "unsupported", "requiredHardware": "not shipped"},
            },
            "fallbackPolicy": "fallback is reported and does not validate requested profile",
        },
        "failureBoundaries": ["domain shift"],
        "approval": {"status": "approved", "reviewer": "release-review", "approvedAtUtc": "2026-09-01T00:00:00Z"},
    }


def valid_report() -> dict[str, object]:
    return {
        "EvidencePurpose": "DeliveryPrecisionCandidate",
        "Accepted": True,
        "EvidenceIdentity": {"ModelContentSha256": HASH_A, "DatasetChecksumSha256": HASH_B, "ActualProvider": "CPUExecutionProvider", "FallbackReason": ""},
        "Summary": {"ModelId": "delivery-v1", "ModelSha256": HASH_A, "ModelSha256Matched": True, "PrecisionAt50": 0.8, "RecallAt50": 0.7, "AP50": 0.75},
    }


class DeliveryModelEvidenceTests(unittest.TestCase):
    def test_schema_accepts_truthful_unreviewed_baseline(self) -> None:
        schema = json.loads((REPO_ROOT / "quality/evals/schemas/delivery-model-manifest.schema.json").read_text(encoding="utf-8"))
        baseline = json.loads((REPO_ROOT / "quality/evals/baselines/deep-learning-delivery-model-manifest.json").read_text(encoding="utf-8"))
        jsonschema.Draft202012Validator(schema).validate(baseline)

    def test_valid_approved_delivery_evidence_can_pass(self) -> None:
        result = evidence.evaluate_delivery_evidence(valid_report(), valid_manifest())
        self.assertTrue(result["releaseReady"])
        self.assertEqual("PASS", result["precisionDisposition"])

    def test_smoke_and_all_zero_metrics_never_pass_precision(self) -> None:
        report = valid_report()
        report["EvidencePurpose"] = "InferenceSmokeOnly"
        report["Summary"] = copy.deepcopy(report["Summary"])
        report["Summary"].update({"PrecisionAt50": 0, "RecallAt50": 0, "AP50": 0})
        result = evidence.evaluate_delivery_evidence(report, valid_manifest())
        self.assertIn("INFERENCE_SMOKE_ONLY", result["blockingReasons"])
        self.assertIn("ALL_PRECISION_METRICS_ZERO", result["blockingReasons"])
        self.assertFalse(result["releaseReady"])

    def test_checksum_mismatch_fails_closed(self) -> None:
        report = valid_report()
        report["Summary"] = copy.deepcopy(report["Summary"])
        report["Summary"]["ModelSha256Matched"] = False
        result = evidence.evaluate_delivery_evidence(report, valid_manifest())
        self.assertIn("MODEL_CHECKSUM_MISMATCH", result["blockingReasons"])

    def test_missing_manifest_fails_model_and_dataset_evidence(self) -> None:
        result = evidence.evaluate_delivery_evidence(valid_report(), None)
        self.assertIn("MODEL_MANIFEST_MISSING", result["blockingReasons"])
        self.assertIn("DATASET_MANIFEST_MISSING", result["blockingReasons"])

    def test_zero_thresholds_and_missing_approval_fail_closed(self) -> None:
        manifest = valid_manifest()
        manifest["acceptanceThresholds"] = {"precisionAt50": 0, "recallAt50": 0, "ap50": 0}
        manifest["approval"] = {"status": "unreviewed", "reviewer": None, "approvedAtUtc": None}
        result = evidence.evaluate_delivery_evidence(valid_report(), manifest)
        self.assertIn("NONZERO_THRESHOLDS_REQUIRED", result["blockingReasons"])
        self.assertIn("APPROVAL_MISSING", result["blockingReasons"])
        self.assertIn("APPROVER_MISSING", result["blockingReasons"])

    def test_provider_profiles_are_independent_and_smoke_does_not_validate_delivery(self) -> None:
        provider_report = {
            "EvidencePurpose": "InferenceSmokeOnly",
            "Cases": [
                {"RequestedProvider": "CPUExecutionProvider", "ActiveProvider": "CPUExecutionProvider", "ProfileStatus": "smoke-validated"},
                {"RequestedProvider": "CUDAExecutionProvider", "ActiveProvider": "NotRun", "ProfileStatus": "unvalidated"},
                {"RequestedProvider": "TensorrtExecutionProvider", "ActiveProvider": "Unavailable", "ProfileStatus": "unsupported"},
            ],
        }
        result = evidence.evaluate_delivery_evidence(valid_report(), valid_manifest(), provider_report)
        self.assertEqual("unvalidated", result["providerProfiles"]["CPU"]["supportStatus"])
        self.assertEqual("unvalidated", result["providerProfiles"]["CUDA"]["supportStatus"])
        self.assertEqual("unsupported", result["providerProfiles"]["TensorRT"]["supportStatus"])


if __name__ == "__main__":
    unittest.main()
