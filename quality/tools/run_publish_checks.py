from __future__ import annotations

import argparse
import hashlib
import json
import platform
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
CATALOG_PATH = REPO_ROOT / "quality/evals/baselines/operator-exposure-approved-baseline.json"
TOOL_VERSION = "run_publish_checks/2026-09-01.wave3b"


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


def evaluate_candidate(
    candidate: dict[str, Any], exposures: dict[str, str]
) -> dict[str, Any]:
    findings: list[dict[str, str]] = []

    def block(code: str, message: str) -> None:
        findings.append({"code": code, "message": message})

    evidence_class = str(candidate.get("evidenceClass") or "").strip().lower()
    if evidence_class not in {"fixture", "delivery"}:
        block("EVIDENCE_CLASS_UNKNOWN", "evidenceClass must be fixture or delivery.")

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

    checks_passed = not findings
    fixture = evidence_class == "fixture"
    return {
        "candidateId": candidate.get("candidateId"),
        "evidenceClass": evidence_class or "unknown",
        "checksPassed": checks_passed,
        "deliveryEvidence": checks_passed and not fixture,
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
