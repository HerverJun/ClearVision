from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
import zipfile
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


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def file_sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build_release_evidence(root: Path) -> dict[str, str]:
    portable = root / "portable.zip"
    nupkg = root / "ClearVision.OperatorLibrary.1.0.0.nupkg"
    source_identity = {
        "schemaVersion": "clearvision.package-source-identity/v1",
        "gitSha": "1" * 40,
        "repositoryDirty": False,
        "runtimeIdentifier": "win-x64",
        "profile": "field-self-contained",
    }
    source_payload = (json.dumps(source_identity) + "\n").encode()
    app_payload = b"fixture-app"
    content_manifest = {
        "gitSha": "1" * 40,
        "runtimeIdentifier": "win-x64",
        "profile": "field-self-contained",
        "contentFingerprint": "sha256:" + "2" * 64,
        "fileCount": 2,
        "files": [
            {"path": "App.exe", "sizeBytes": len(app_payload), "sha256": hashlib.sha256(app_payload).hexdigest()},
            {
                "path": "release/source-identity.json",
                "sizeBytes": len(source_payload),
                "sha256": hashlib.sha256(source_payload).hexdigest(),
            },
        ],
    }
    with zipfile.ZipFile(portable, "w") as archive:
        archive.writestr("App.exe", app_payload)
        archive.writestr("release/source-identity.json", source_payload)
        archive.writestr("release/package-content-manifest.json", json.dumps(content_manifest))
    nupkg.write_bytes(b"fixture-nupkg")

    package_result = root / "package-result.json"
    identity = root / "identity-manifest.json"
    report = root / "dependency-report.json"
    sbom = root / "SBOM.spdx.json"
    notices = root / "THIRD-PARTY-NOTICES.txt"
    validation = root / "validation-summary.json"
    checksums = root / "SHA256SUMS"
    write_json(
        package_result,
        {
            "gitSha": "1" * 40,
            "repositoryDirty": False,
            "runtimeIdentifier": "win-x64",
            "profile": "field-self-contained",
            "contentFingerprint": "sha256:" + "2" * 64,
            "portableZip": {"sha256": file_sha(portable)},
            "operatorLibraryPackage": {"sha256": file_sha(nupkg)},
            "releaseEligible": True,
        },
    )
    write_json(
        identity,
        {
            **source_identity,
            "schemaVersion": "clearvision.release-identity/v1",
            "portablePackage": {"sha256": file_sha(portable)},
            "operatorLibraryPackage": {"sha256": file_sha(nupkg)},
        },
    )
    component = {"name": "Fixture.Dependency", "version": "1.0.0", "license": "MIT", "policyDisposition": "allowed"}
    write_json(report, {"componentCount": 1, "components": [component]})
    write_json(sbom, {"packages": [{"name": "Fixture.Dependency", "versionInfo": "1.0.0"}]})
    notices.write_text("Component: Fixture.Dependency@1.0.0\n", encoding="utf-8")
    write_json(
        validation,
        {
            "generationPassed": True,
            "artifactConsistencyPassed": True,
            "releaseEligible": True,
            "blockingFindings": [],
        },
    )
    governed = [portable, nupkg, sbom, notices, report, identity, validation]
    checksums.write_text(
        "\n".join(f"{file_sha(path)}  {path.name}" for path in governed) + "\n",
        encoding="utf-8",
    )
    return {
        "packageResult": str(package_result),
        "portableZip": str(portable),
        "operatorLibraryPackage": str(nupkg),
        "sbom": str(sbom),
        "notices": str(notices),
        "dependencyReport": str(report),
        "identityManifest": str(identity),
        "supplyChainValidation": str(validation),
        "checksums": str(checksums),
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

    def test_delivery_requires_real_release_and_human_evidence(self) -> None:
        candidate = base_candidate()
        candidate["evidenceClass"] = "delivery"
        result = checks.evaluate_candidate(candidate, EXPOSURES)
        codes = {finding["code"] for finding in result["findings"]}
        self.assertTrue(
            {"RELEASE_EVIDENCE_MISSING", "REAL_MODEL_EVIDENCE_MISSING", "CORE20_HUMAN_EVIDENCE_MISSING"}.issubset(codes)
        )
        self.assertFalse(result["deliveryEvidence"])

    def test_real_package_structure_can_pass_while_missing_human_evidence_keeps_delivery_false(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            candidate = base_candidate()
            candidate["evidenceClass"] = "delivery"
            candidate["releaseEvidence"] = build_release_evidence(Path(temporary))
            candidate["deliveryApprovals"] = {
                "realModelEvidenceApproved": False,
                "core20HumanEvidenceApproved": False,
            }
            result = checks.evaluate_candidate(candidate, EXPOSURES)
        codes = {finding["code"] for finding in result["findings"]}
        self.assertTrue(result["releaseEvidence"]["structuralValidationPassed"])
        self.assertTrue(result["releaseEvidence"]["releaseEligible"])
        self.assertEqual(
            {"REAL_MODEL_EVIDENCE_MISSING", "CORE20_HUMAN_EVIDENCE_MISSING"}, codes
        )
        self.assertFalse(result["deliveryEvidence"])


if __name__ == "__main__":
    unittest.main()
