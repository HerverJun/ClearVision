from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import run_publish_checks as checks  # noqa: E402


EXPOSURES = {"ImageAcquisition": "package-public", "DeepLearning": "package-public", "MqttPublish": "disabled"}


def base_candidate() -> dict[str, object]:
    return {
        "candidateId": "unit",
        "evidenceClass": "fixture",
        "sourceChain": {
            "portablePackagingReport": "quality/evals/reports/runtime_package_manifest_dry_run_final.json",
            "visionAgentReadinessReport": "quality/evals/reports/runtime_preview_package_readiness_final.json",
            "releaseReviewReport": "quality/evals/reports/runtime_preview_deploy_readiness_report.sample.json",
        },
        "assets": [],
        "externalModels": [],
        "flows": [{"id": "flow", "operatorTypes": ["ImageAcquisition"], "parametersNeedingReview": [], "roi": {"width": 10, "height": 10}}],
    }


class PublishChecksTests(unittest.TestCase):
    def test_fixture_pass_is_never_delivery_evidence(self) -> None:
        result = checks.evaluate_candidate(base_candidate(), EXPOSURES)
        self.assertTrue(result["checksPassed"])
        self.assertFalse(result["deliveryEvidence"])
        self.assertEqual("FIXTURE_PASS_ONLY", result["releaseDisposition"])

    def test_disabled_unknown_review_zero_roi_and_missing_model_all_block(self) -> None:
        candidate = base_candidate()
        candidate["flows"] = [{
            "id": "bad",
            "operatorTypes": ["MqttPublish", "UnknownOperator", "DeepLearning"],
            "parametersNeedingReview": ["ModelPath"],
            "roi": {"width": 0, "height": 0},
        }]
        result = checks.evaluate_candidate(candidate, EXPOSURES)
        codes = {finding["code"] for finding in result["findings"]}
        self.assertTrue({"DISABLED_OPERATOR", "UNKNOWN_OPERATOR", "PARAMETERS_NEEDING_REVIEW", "ZERO_ROI", "EXTERNAL_MODEL_MANIFEST_MISSING"}.issubset(codes))

    def test_asset_hash_mismatch_blocks(self) -> None:
        candidate = copy.deepcopy(base_candidate())
        candidate["assets"] = [{"path": "quality/evals/schemas/delivery-model-manifest.schema.json", "sha256": "0" * 64}]
        result = checks.evaluate_candidate(candidate, EXPOSURES)
        self.assertIn("ASSET_HASH_MISMATCH", {finding["code"] for finding in result["findings"]})


if __name__ == "__main__":
    unittest.main()
