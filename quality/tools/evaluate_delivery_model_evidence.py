from __future__ import annotations

import argparse
import json
import platform
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
TOOL_VERSION = "evaluate_delivery_model_evidence/2026-09-01.wave3b"
PROFILE_NAMES = {
    "CPU": "CPUExecutionProvider",
    "CUDA": "CUDAExecutionProvider",
    "TensorRT": "TensorrtExecutionProvider",
}


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def _sha(value: Any) -> str:
    text = str(value or "").strip().lower()
    return text[7:] if text.startswith("sha256:") else text


def _number(value: Any) -> float | None:
    return float(value) if isinstance(value, (int, float)) and not isinstance(value, bool) else None


def _git(*arguments: str) -> str:
    completed = subprocess.run(
        ["git", *arguments],
        cwd=REPO_ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    return completed.stdout.strip() if completed.returncode == 0 else ""


def _provider_profiles(
    manifest: dict[str, Any], provider_report: dict[str, Any] | None
) -> dict[str, dict[str, Any]]:
    policies = manifest.get("providerPolicy", {}).get("profiles", {})
    cases = provider_report.get("Cases", []) if isinstance(provider_report, dict) else []
    result: dict[str, dict[str, Any]] = {}
    for profile, requested_provider in PROFILE_NAMES.items():
        policy = policies.get(profile, {}) if isinstance(policies, dict) else {}
        matching = next(
            (
                case
                for case in cases
                if isinstance(case, dict)
                and str(case.get("RequestedProvider", "")).lower() == requested_provider.lower()
            ),
            None,
        )
        smoke_status = str((matching or {}).get("ProfileStatus") or "unvalidated").lower()
        if policy.get("policy") == "unsupported" or smoke_status == "unsupported":
            support_status = "unsupported"
        else:
            # Identity/constant-model provider smoke never proves a delivery profile.
            support_status = "unvalidated"
        result[profile] = {
            "policy": policy.get("policy", "missing"),
            "supportStatus": support_status,
            "smokeStatus": smoke_status,
            "requestedProvider": requested_provider,
            "actualProvider": (matching or {}).get("ActiveProvider"),
            "fallbackReason": (matching or {}).get("FallbackReason"),
        }
    return result


def evaluate_delivery_evidence(
    report: dict[str, Any],
    manifest: dict[str, Any] | None,
    provider_report: dict[str, Any] | None = None,
) -> dict[str, Any]:
    reasons: list[str] = []
    summary = report.get("Summary", {}) if isinstance(report.get("Summary"), dict) else {}
    identity = report.get("EvidenceIdentity", {}) if isinstance(report.get("EvidenceIdentity"), dict) else {}
    purpose = str(report.get("EvidencePurpose") or "").strip()

    if purpose.lower() == "inferencesmokeonly" or "smoke" in str(summary.get("ModelId", "")).lower():
        reasons.append("INFERENCE_SMOKE_ONLY")

    metrics = {
        "precisionAt50": _number(summary.get("PrecisionAt50")),
        "recallAt50": _number(summary.get("RecallAt50")),
        "ap50": _number(summary.get("AP50")),
    }
    if all(value == 0 for value in metrics.values() if value is not None) and all(
        value is not None for value in metrics.values()
    ):
        reasons.append("ALL_PRECISION_METRICS_ZERO")
    if any(value is None for value in metrics.values()):
        reasons.append("PRECISION_METRICS_MISSING")
    if summary.get("ModelSha256Matched") is not True:
        reasons.append("MODEL_CHECKSUM_MISMATCH")

    if manifest is None:
        reasons.extend(["MODEL_MANIFEST_MISSING", "DATASET_MANIFEST_MISSING"])
        profiles = _provider_profiles({}, provider_report)
    else:
        model = manifest.get("model", {}) if isinstance(manifest.get("model"), dict) else {}
        dataset = manifest.get("dataset", {}) if isinstance(manifest.get("dataset"), dict) else {}
        thresholds = manifest.get("acceptanceThresholds", {})
        approval = manifest.get("approval", {}) if isinstance(manifest.get("approval"), dict) else {}

        expected_model_sha = _sha(model.get("sha256"))
        actual_model_sha = _sha(identity.get("ModelContentSha256") or summary.get("ModelSha256"))
        if len(expected_model_sha) != 64:
            reasons.append("MODEL_MANIFEST_HASH_MISSING")
        elif actual_model_sha != expected_model_sha:
            reasons.append("DELIVERY_MODEL_HASH_MISMATCH")

        expected_dataset_sha = _sha(dataset.get("sha256"))
        actual_dataset_sha = _sha(identity.get("DatasetChecksumSha256"))
        if not dataset.get("manifestPath") or len(expected_dataset_sha) != 64:
            reasons.append("DATASET_MANIFEST_MISSING")
        elif actual_dataset_sha != expected_dataset_sha:
            reasons.append("DATASET_CHECKSUM_MISMATCH")

        required_thresholds = ("precisionAt50", "recallAt50", "ap50")
        if not isinstance(thresholds, dict) or any(
            _number(thresholds.get(name)) is None or float(thresholds[name]) <= 0
            for name in required_thresholds
        ):
            reasons.append("NONZERO_THRESHOLDS_REQUIRED")
        else:
            for name in required_thresholds:
                value = metrics[name]
                if value is None or value < float(thresholds[name]):
                    reasons.append(f"THRESHOLD_FAILED_{name.upper()}")

        if approval.get("status") != "approved":
            reasons.append("APPROVAL_MISSING")
        if not str(approval.get("reviewer") or "").strip():
            reasons.append("APPROVER_MISSING")
        if not str(approval.get("approvedAtUtc") or "").strip():
            reasons.append("APPROVAL_DATE_MISSING")
        profiles = _provider_profiles(manifest, provider_report)

    reasons = list(dict.fromkeys(reasons))
    return {
        "schemaVersion": "clearvision.delivery-model-evidence-evaluation/v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "toolVersion": TOOL_VERSION,
        "gitSha": _git("rev-parse", "HEAD"),
        "repositoryDirty": bool(_git("status", "--porcelain")),
        "environment": f"Python {platform.python_version()}; {platform.platform()}",
        "evidencePurpose": purpose or "unclassified",
        "precisionDisposition": "PASS" if not reasons else "FAIL",
        "releaseReady": not reasons,
        "metrics": metrics,
        "providerProfiles": profiles,
        "blockingReasons": reasons,
        "sourceIdentity": identity,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Fail-closed delivery model evidence evaluator.")
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--provider-report", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    report = read_json(args.report)
    manifest = read_json(args.manifest) if args.manifest and args.manifest.exists() else None
    provider_report = read_json(args.provider_report) if args.provider_report and args.provider_report.exists() else None
    evaluation = evaluate_delivery_evidence(report, manifest, provider_report)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(evaluation, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(evaluation, ensure_ascii=False, indent=2))
    return 0 if evaluation["releaseReady"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
