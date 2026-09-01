from __future__ import annotations

import argparse
import hashlib
import json
import platform
import re
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable


REPO_ROOT = Path(__file__).resolve().parents[2]
CATALOG_PATH = REPO_ROOT / "quality/evals/baselines/operator-exposure-approved-baseline.json"
TOOL_VERSION = "run_publish_checks/2026-09-01.wave3c"


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def _repo_path(value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else REPO_ROOT / path


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _normalize_sha(value: Any) -> str:
    text = str(value or "").strip().lower()
    return text[7:] if text.startswith("sha256:") else text


def _git(*arguments: str) -> str:
    completed = subprocess.run(
        ["git", *arguments], cwd=REPO_ROOT, text=True, capture_output=True, check=False
    )
    return completed.stdout.strip() if completed.returncode == 0 else ""


def governed_exposures(catalog: dict[str, Any] | None = None) -> dict[str, str]:
    source = catalog or read_json(CATALOG_PATH)
    entries = source.get("entries", [])
    return {
        str(entry.get("operatorType")): str(entry.get("exposureClassification"))
        for entry in entries
        if isinstance(entry, dict) and entry.get("operatorType")
    }


def _checksum_rows(path: Path) -> dict[str, str]:
    rows: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line or line.startswith("#"):
            continue
        digest, separator, name = line.partition("  ")
        if not separator or len(digest) != 64 or not name:
            raise ValueError(f"Invalid SHA256SUMS line: {line}")
        rows[name] = digest.lower()
    return rows


def _validate_release_evidence(
    candidate: dict[str, Any],
    block: Callable[[str, str], None],
    findings: list[dict[str, str]],
) -> dict[str, Any]:
    evidence = candidate.get("releaseEvidence")
    required = {
        "packageResult": "PACKAGE_RESULT_MISSING",
        "portableZip": "PORTABLE_ZIP_MISSING",
        "operatorLibraryPackage": "OPERATOR_LIBRARY_PACKAGE_MISSING",
        "sbom": "SBOM_MISSING",
        "notices": "THIRD_PARTY_NOTICES_MISSING",
        "dependencyReport": "DEPENDENCY_REPORT_MISSING",
        "identityManifest": "SOURCE_IDENTITY_MISSING",
        "supplyChainValidation": "SUPPLY_CHAIN_VALIDATION_MISSING",
        "checksums": "CHECKSUMS_MISSING",
    }
    if not isinstance(evidence, dict):
        block("RELEASE_EVIDENCE_MISSING", "Delivery candidate must reference the governed portable release evidence set.")
        return {"provided": False, "structuralValidationPassed": False, "releaseEligible": False}
    paths: dict[str, Path] = {}
    for field, code in required.items():
        value = evidence.get(field)
        path = _repo_path(str(value or ""))
        if not value or not path.is_file():
            block(code, f"releaseEvidence.{field} must reference an existing final artifact.")
        else:
            paths[field] = path
    if len(paths) != len(required):
        return {"provided": True, "structuralValidationPassed": False, "releaseEligible": False}

    try:
        result = read_json(paths["packageResult"])
        identity = read_json(paths["identityManifest"])
        report = read_json(paths["dependencyReport"])
        sbom = read_json(paths["sbom"])
        validation = read_json(paths["supplyChainValidation"])
        portable_sha = _sha256(paths["portableZip"])
        nupkg_sha = _sha256(paths["operatorLibraryPackage"])
        if _normalize_sha(result.get("portableZip", {}).get("sha256")) != portable_sha:
            block("PORTABLE_HASH_MISMATCH", "package-result does not match the final portable ZIP hash.")
        if _normalize_sha(result.get("operatorLibraryPackage", {}).get("sha256")) != nupkg_sha:
            block("NUPKG_HASH_MISMATCH", "package-result does not match the final OperatorLibrary nupkg hash.")
        if identity.get("schemaVersion") != "clearvision.release-identity/v1":
            block("SOURCE_IDENTITY_SCHEMA_INVALID", "Release identity manifest schema is not the governed v1 schema.")
        if _normalize_sha(identity.get("portablePackage", {}).get("sha256")) != portable_sha:
            block("SOURCE_IDENTITY_PORTABLE_MISMATCH", "Release identity does not match the portable ZIP.")
        if _normalize_sha(identity.get("operatorLibraryPackage", {}).get("sha256")) != nupkg_sha:
            block("SOURCE_IDENTITY_NUPKG_MISMATCH", "Release identity does not match the OperatorLibrary nupkg.")

        with zipfile.ZipFile(paths["portableZip"]) as archive:
            names = {name.replace("\\", "/") for name in archive.namelist() if not name.endswith("/")}
            content_name = "release/package-content-manifest.json"
            source_name = "release/source-identity.json"
            if content_name not in names or source_name not in names:
                block("PORTABLE_MANIFEST_MISSING", "Portable ZIP must contain its content and source identity manifests.")
                content_manifest: dict[str, Any] = {}
                source_identity: dict[str, Any] = {}
            else:
                content_manifest = json.loads(archive.read(content_name).decode("utf-8-sig"))
                source_identity = json.loads(archive.read(source_name).decode("utf-8-sig"))
                for row in content_manifest.get("files", []):
                    name = str(row.get("path") or "").replace("\\", "/")
                    if name not in names:
                        block("PORTABLE_CONTENT_MISSING", f"Content manifest references a missing ZIP entry: {name}")
                        continue
                    payload = archive.read(name)
                    if len(payload) != row.get("sizeBytes") or hashlib.sha256(payload).hexdigest() != _normalize_sha(row.get("sha256")):
                        block("PORTABLE_CONTENT_HASH_MISMATCH", f"Content manifest hash/size mismatch: {name}")
            for field in ("gitSha", "runtimeIdentifier", "profile"):
                expected = result.get(field)
                if content_manifest.get(field) != expected or source_identity.get(field) != expected or identity.get(field) != expected:
                    block("SOURCE_IDENTITY_MISMATCH", f"Release identity differs for {field}.")
            if bool(result.get("repositoryDirty")) or bool(source_identity.get("repositoryDirty")) or bool(identity.get("repositoryDirty")):
                block("SOURCE_IDENTITY_DIRTY", "Delivery evidence must come from a clean checkout.")

        report_components = {
            (str(row.get("name")), str(row.get("version"))) for row in report.get("components", [])
        }
        sbom_components = {
            (str(row.get("name")), str(row.get("versionInfo"))) for row in sbom.get("packages", [])
        }
        if report_components != sbom_components:
            block("SBOM_COMPONENT_MISMATCH", "SBOM packages do not match dependency-report components.")
        notice_components = set(
            re.findall(
                r"(?m)^Component: ([^@\r\n]+)@([^\r\n]+)$",
                paths["notices"].read_text(encoding="utf-8-sig"),
            )
        )
        if notice_components != report_components:
            block("NOTICE_COMPONENT_MISMATCH", "THIRD-PARTY-NOTICES does not exactly match packaged components.")
        blocked_licenses = [
            row
            for row in report.get("components", [])
            if row.get("policyDisposition") not in {"allowed", "approved-exception"}
        ]
        if blocked_licenses:
            block(
                "LICENSE_POLICY_BLOCKED",
                "Final dependency report contains blocked license dispositions: "
                + ", ".join(f"{row.get('name')}@{row.get('version')}" for row in blocked_licenses),
            )
        if not validation.get("generationPassed") or not validation.get("artifactConsistencyPassed"):
            block("SUPPLY_CHAIN_INCONSISTENT", "Final supply-chain structural validation did not pass.")
        if not validation.get("releaseEligible"):
            codes = ", ".join(str(row.get("code")) for row in validation.get("blockingFindings", []))
            block("SUPPLY_CHAIN_RELEASE_BLOCKED", f"Final supply-chain policy remains blocked: {codes}")
        checksums = _checksum_rows(paths["checksums"])
        for field in (
            "portableZip",
            "operatorLibraryPackage",
            "sbom",
            "notices",
            "dependencyReport",
            "identityManifest",
            "supplyChainValidation",
        ):
            path = paths[field]
            if checksums.get(path.name) != _sha256(path):
                block("CHECKSUM_MISMATCH", f"SHA256SUMS does not match {path.name}.")
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        block("RELEASE_EVIDENCE_INVALID", f"Unable to validate governed release evidence: {error}")
        return {"provided": True, "structuralValidationPassed": False, "releaseEligible": False}

    release_eligible = bool(validation.get("releaseEligible"))
    structural_findings = {
        "PORTABLE_HASH_MISMATCH",
        "NUPKG_HASH_MISMATCH",
        "SOURCE_IDENTITY_SCHEMA_INVALID",
        "SOURCE_IDENTITY_PORTABLE_MISMATCH",
        "SOURCE_IDENTITY_NUPKG_MISMATCH",
        "PORTABLE_MANIFEST_MISSING",
        "PORTABLE_CONTENT_MISSING",
        "PORTABLE_CONTENT_HASH_MISMATCH",
        "SOURCE_IDENTITY_MISMATCH",
        "SOURCE_IDENTITY_DIRTY",
        "SBOM_COMPONENT_MISMATCH",
        "NOTICE_COMPONENT_MISMATCH",
        "SUPPLY_CHAIN_INCONSISTENT",
        "CHECKSUM_MISMATCH",
    }
    candidate_codes = {row["code"] for row in findings}
    return {
        "provided": True,
        "structuralValidationPassed": not bool(candidate_codes & structural_findings),
        "releaseEligible": release_eligible,
        "gitSha": result.get("gitSha"),
        "contentFingerprint": result.get("contentFingerprint"),
        "portableSha256": portable_sha,
        "componentCount": report.get("componentCount"),
    }


def evaluate_candidate(
    candidate: dict[str, Any], exposures: dict[str, str]
) -> dict[str, Any]:
    findings: list[dict[str, str]] = []

    def block(code: str, message: str) -> None:
        findings.append({"code": code, "message": message})

    evidence_class = str(candidate.get("evidenceClass") or "").strip().lower()
    if evidence_class not in {"fixture", "delivery"}:
        block("EVIDENCE_CLASS_UNKNOWN", "evidenceClass must be fixture or delivery.")

    release_evidence = {
        "provided": False,
        "structuralValidationPassed": False,
        "releaseEligible": False,
    }
    if evidence_class == "delivery" or candidate.get("releaseEvidence") is not None:
        release_evidence = _validate_release_evidence(candidate, block, findings)

    source_chain = candidate.get("sourceChain", {})
    for field in ("portablePackagingReport", "visionAgentReadinessReport", "releaseReviewReport"):
        value = source_chain.get(field) if isinstance(source_chain, dict) else None
        if not value or not _repo_path(str(value)).is_file():
            block("SOURCE_CHAIN_MISSING", f"{field} must reference an existing governed report.")

    for asset in candidate.get("assets", []):
        if not isinstance(asset, dict):
            block("ASSET_ENTRY_INVALID", "Asset entry must be an object.")
            continue
        path = _repo_path(str(asset.get("path") or ""))
        expected = _normalize_sha(asset.get("sha256"))
        if not path.is_file():
            block("ASSET_MISSING", f"Asset does not exist: {asset.get('path')}")
        elif len(expected) != 64 or _sha256(path) != expected:
            block("ASSET_HASH_MISMATCH", f"Asset hash mismatch: {asset.get('path')}")

    flows = candidate.get("flows", [])
    for flow in flows:
        if not isinstance(flow, dict):
            block("FLOW_ENTRY_INVALID", "Flow entry must be an object.")
            continue
        flow_id = str(flow.get("id") or "<unknown>")
        for operator_type in flow.get("operatorTypes", []):
            exposure = exposures.get(str(operator_type))
            if exposure is None:
                block("UNKNOWN_OPERATOR", f"{flow_id}: unknown operator {operator_type}.")
            elif exposure == "disabled":
                block("DISABLED_OPERATOR", f"{flow_id}: disabled operator {operator_type}.")
            elif exposure == "legacy-alias":
                block("LEGACY_ALIAS_OPERATOR", f"{flow_id}: legacy alias {operator_type} must be canonicalized.")
        pending = flow.get("parametersNeedingReview", [])
        if not isinstance(pending, list) or pending:
            block("PARAMETERS_NEEDING_REVIEW", f"{flow_id}: parametersNeedingReview must be an empty list.")
        roi = flow.get("roi")
        if isinstance(roi, dict):
            width = roi.get("width")
            height = roi.get("height")
            if not isinstance(width, (int, float)) or not isinstance(height, (int, float)) or width <= 0 or height <= 0:
                block("ZERO_ROI", f"{flow_id}: ROI width and height must be positive.")

    external_models = candidate.get("externalModels", [])
    has_deep_learning = any(
        "DeepLearning" in flow.get("operatorTypes", [])
        for flow in flows
        if isinstance(flow, dict)
    )
    if has_deep_learning and not external_models:
        block("EXTERNAL_MODEL_MANIFEST_MISSING", "DeepLearning flow requires an external delivery model manifest.")
    for model in external_models:
        if not isinstance(model, dict):
            block("EXTERNAL_MODEL_ENTRY_INVALID", "External model entry must be an object.")
            continue
        manifest_path = _repo_path(str(model.get("manifestPath") or ""))
        if not manifest_path.is_file():
            block("EXTERNAL_MODEL_MANIFEST_MISSING", f"External model manifest missing: {model.get('manifestPath')}")
        if model.get("approvalStatus") != "approved":
            block("EXTERNAL_MODEL_UNAPPROVED", f"External model is not approved: {model.get('modelId')}")
        expected = _normalize_sha(model.get("manifestSha256"))
        if manifest_path.is_file() and (len(expected) != 64 or _sha256(manifest_path) != expected):
            block("EXTERNAL_MODEL_MANIFEST_HASH_MISMATCH", f"External model manifest hash mismatch: {model.get('modelId')}")

    if evidence_class == "delivery":
        approvals = candidate.get("deliveryApprovals", {})
        if not isinstance(approvals, dict) or approvals.get("realModelEvidenceApproved") is not True:
            block("REAL_MODEL_EVIDENCE_MISSING", "Real delivery model evidence is not approved.")
        if not isinstance(approvals, dict) or approvals.get("core20HumanEvidenceApproved") is not True:
            block("CORE20_HUMAN_EVIDENCE_MISSING", "Core20 human evidence is not approved.")

    checks_passed = not findings
    fixture = evidence_class == "fixture"
    return {
        "candidateId": candidate.get("candidateId"),
        "evidenceClass": evidence_class or "unknown",
        "checksPassed": checks_passed,
        "deliveryEvidence": checks_passed and not fixture,
        "releaseEvidence": release_evidence,
        "releaseDisposition": (
            "FIXTURE_PASS_ONLY" if checks_passed and fixture else "DELIVERY_CHECKS_PASS" if checks_passed else "BLOCKED"
        ),
        "expectedDisposition": candidate.get("expectedDisposition"),
        "expectationMatched": (
            candidate.get("expectedDisposition") is None
            or candidate.get("expectedDisposition")
            == ("pass" if checks_passed else "blocked")
        ),
        "findings": findings,
    }


def build_report(candidates: list[dict[str, Any]]) -> dict[str, Any]:
    catalog = read_json(CATALOG_PATH)
    results = [evaluate_candidate(candidate, governed_exposures(catalog)) for candidate in candidates]
    fixture_only = all(row["evidenceClass"] == "fixture" for row in results)
    return {
        "schemaVersion": "clearvision.publish-checks-report/v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "toolVersion": TOOL_VERSION,
        "gitSha": _git("rev-parse", "HEAD"),
        "repositoryDirty": bool(_git("status", "--porcelain")),
        "environment": f"Python {platform.python_version()}; {platform.platform()}",
        "populationFingerprint": catalog.get("populationFingerprint"),
        "evidenceClass": "fixture-only" if fixture_only else "mixed-or-delivery",
        "deliveryEvidence": any(row["deliveryEvidence"] for row in results),
        "verificationPassed": all(row["expectationMatched"] for row in results),
        "claimBoundary": "Fixture pass/fail exercises validate PublishChecks logic only and are never delivery, release, portable zip, SBOM, license, or GitHub Release evidence.",
        "summary": {
            "caseCount": len(results),
            "checksPassedCount": sum(row["checksPassed"] for row in results),
            "blockedCount": sum(not row["checksPassed"] for row in results),
            "deliveryEvidenceCount": sum(row["deliveryEvidence"] for row in results),
        },
        "cases": results,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Governed pre-release PublishChecks evaluator.")
    parser.add_argument("--candidate", action="append", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = build_report([read_json(path) for path in args.candidate])
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["verificationPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
