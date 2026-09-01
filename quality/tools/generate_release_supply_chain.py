from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable
from urllib.parse import unquote
from xml.etree import ElementTree


TOOL_VERSION = "generate_release_supply_chain/2026-09-01.wave3c"
SPDX_VERSION = "SPDX-2.3"
SPDX_TOKEN = re.compile(r"[A-Za-z0-9][A-Za-z0-9.+-]*")


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def stable_id(value: str) -> str:
    normalized = re.sub(r"[^A-Za-z0-9.-]+", "-", value).strip("-.")
    return normalized or "unknown"


def split_library_key(value: str) -> tuple[str, str]:
    name, separator, version = value.rpartition("/")
    return (name, version) if separator else (value, "NOASSERTION")


def zip_names(path: Path) -> list[str]:
    with zipfile.ZipFile(path) as archive:
        return sorted(
            name.replace("\\", "/")
            for name in archive.namelist()
            if name and not name.endswith("/")
        )


def read_portable_dependencies(path: Path) -> tuple[list[dict[str, Any]], list[str], str]:
    with zipfile.ZipFile(path) as archive:
        names = zip_names(path)
        deps_names = [name for name in names if name.lower().endswith(".deps.json")]
        if len(deps_names) != 1:
            raise ValueError(f"Portable ZIP must contain exactly one .deps.json; found {len(deps_names)}")
        deps_name = deps_names[0]
        deps = json.loads(archive.read(deps_name).decode("utf-8-sig"))

    targets = deps.get("targets", {})
    target_name = deps.get("runtimeTarget", {}).get("name")
    target = targets.get(target_name, {}) if target_name else {}
    libraries = deps.get("libraries", {})
    packaged_basenames = {Path(name).name.lower() for name in names}
    components: list[dict[str, Any]] = []

    for key, library in sorted(libraries.items(), key=lambda item: item[0].lower()):
        if not isinstance(library, dict) or library.get("type") != "package":
            continue
        assets: list[str] = []
        target_entry = target.get(key, {})
        if not isinstance(target_entry, dict):
            continue
        for group in ("runtime", "native", "resources"):
            values = target_entry.get(group, {})
            if isinstance(values, dict):
                assets.extend(str(item) for item in values)
        runtime_targets = target_entry.get("runtimeTargets", {})
        if isinstance(runtime_targets, dict):
            assets.extend(str(item) for item in runtime_targets)

        present_assets = sorted(
            asset.replace("\\", "/")
            for asset in assets
            if Path(asset).name.lower() in packaged_basenames
        )
        if not assets or not present_assets:
            continue
        name, version = split_library_key(key)
        components.append(
            {
                "name": name,
                "version": version,
                "scopes": ["portable-runtime"],
                "evidence": {
                    "source": "portable-zip-deps-json",
                    "depsJson": Path(deps_name).name,
                    "packagedAssets": present_assets,
                },
            }
        )
    return components, names, deps_name


def local_name(element: ElementTree.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def child(element: ElementTree.Element, name: str) -> ElementTree.Element | None:
    return next((item for item in element if local_name(item) == name), None)


def text(element: ElementTree.Element | None, name: str) -> str | None:
    if element is None:
        return None
    item = child(element, name)
    return item.text.strip() if item is not None and item.text else None


def read_nupkg(path: Path) -> tuple[dict[str, Any], list[dict[str, Any]], list[str]]:
    with zipfile.ZipFile(path) as archive:
        names = sorted(name.replace("\\", "/") for name in archive.namelist() if not name.endswith("/"))
        nuspec_names = [name for name in names if name.lower().endswith(".nuspec")]
        if len(nuspec_names) != 1:
            raise ValueError(f"nupkg must contain exactly one nuspec; found {len(nuspec_names)}")
        root = ElementTree.fromstring(archive.read(nuspec_names[0]))
    metadata = child(root, "metadata")
    package_id = text(metadata, "id") or path.stem
    version = text(metadata, "version") or "NOASSERTION"
    dependencies: dict[tuple[str, str], dict[str, Any]] = {}
    dependency_root = child(metadata, "dependencies") if metadata is not None else None
    if dependency_root is not None:
        for element in dependency_root.iter():
            if local_name(element) != "dependency":
                continue
            name = str(element.attrib.get("id") or "").strip()
            dependency_version = str(element.attrib.get("version") or "NOASSERTION").strip("[]() ")
            if name:
                dependencies[(name.lower(), dependency_version)] = {
                    "name": name,
                    "version": dependency_version,
                    "scopes": ["nupkg-declared"],
                    "evidence": {"source": "final-nupkg-nuspec", "nuspec": Path(nuspec_names[0]).name},
                }
    package = {
        "name": package_id,
        "version": version,
        "scopes": ["nupkg-root"],
        "evidence": {"source": "final-nupkg", "nuspec": Path(nuspec_names[0]).name},
    }
    return package, list(dependencies.values()), names


def merge_components(*groups: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    merged: dict[tuple[str, str], dict[str, Any]] = {}
    for group in groups:
        for component in group:
            key = (component["name"].lower(), component["version"].lower())
            existing = merged.get(key)
            if existing is None:
                merged[key] = component
                continue
            existing["scopes"] = sorted(set(existing["scopes"]) | set(component["scopes"]))
            evidence = existing.setdefault("evidenceByScope", [])
            if "evidence" in existing:
                evidence.append(existing.pop("evidence"))
            evidence.append(component["evidence"])
    return sorted(merged.values(), key=lambda item: (item["name"].lower(), item["version"].lower()))


def identify_license_text(value: str) -> str:
    normalized = value.lower()
    if "reciprocal public license 1.5" in normalized:
        return "RPL-1.5 OR LicenseRef-LuckyPenny-Commercial"
    if "apache license" in normalized and "version 2.0" in normalized:
        return "Apache-2.0"
    if "mozilla public license" in normalized and "version 2.0" in normalized:
        return "MPL-2.0"
    if "microsoft public license" in normalized:
        return "MS-PL"
    if "microsoft software license terms" in normalized:
        return "LicenseRef-Microsoft-Product-Terms"
    if "permission is hereby granted, free of charge" in normalized:
        return "MIT"
    if "redistribution and use in source and binary forms" in normalized:
        return "BSD-3-Clause" if "neither the name" in normalized or "names of its contributors" in normalized else "BSD-2-Clause"
    if "isc license" in normalized:
        return "ISC"
    if "zlib license" in normalized:
        return "Zlib"
    return "NOASSERTION"


def parse_nuspec_license(
    nuspec_path: Path,
) -> tuple[str, str | None, str | None, str | None]:
    root = ElementTree.parse(nuspec_path).getroot()
    metadata = child(root, "metadata")
    license_element = child(metadata, "license") if metadata is not None else None
    project_url = text(metadata, "projectUrl")
    if license_element is not None and license_element.text:
        value = license_element.text.strip()
        if license_element.attrib.get("type") == "expression":
            return value, "nuget-nuspec-license-expression", project_url, None
        license_file = nuspec_path.parent / value
        if license_file.is_file():
            license_text = license_file.read_text(encoding="utf-8-sig", errors="replace")
            identified = identify_license_text(license_text)
            return identified, f"nuget-nuspec-license-file:{Path(value).name}", project_url, license_text
        return "NOASSERTION", f"nuget-nuspec-license-file-missing:{Path(value).name}", project_url, None
    license_url = text(metadata, "licenseUrl")
    if license_url:
        decoded = unquote(license_url).upper()
        for candidate in ("APACHE-2.0", "BSD-2-CLAUSE", "BSD-3-CLAUSE", "MIT", "MS-PL", "ZLIB"):
            if candidate in decoded:
                return candidate, "nuget-nuspec-license-url", project_url, None
        local_license_files = sorted(
            path
            for path in nuspec_path.parent.iterdir()
            if path.is_file() and re.match(r"(?i)^licen[cs]e(?:\.|$)", path.name)
        )
        for license_file in local_license_files:
            license_text = license_file.read_text(encoding="utf-8-sig", errors="replace")
            identified = identify_license_text(license_text)
            if identified != "NOASSERTION":
                return identified, f"local-package-license-file:{license_file.name}", project_url, license_text
        return "NOASSERTION", "unrecognized-nuget-license-url", project_url, None
    return "NOASSERTION", "nuget-nuspec-has-no-license", project_url, None


def attach_license(component: dict[str, Any], nuget_root: Path) -> None:
    name = component["name"]
    version = component["version"]
    package_root = nuget_root / name.lower() / version.lower()
    nuspecs = sorted(package_root.glob("*.nuspec")) if package_root.is_dir() else []
    if nuspecs:
        license_value, evidence, project_url, license_text = parse_nuspec_license(nuspecs[0])
    else:
        license_value, evidence, project_url, license_text = (
            "NOASSERTION",
            "local-nuspec-unavailable",
            None,
            None,
        )
    component["license"] = license_value
    component["licenseEvidence"] = evidence
    if license_text:
        component["licenseTextSha256"] = hashlib.sha256(license_text.encode("utf-8")).hexdigest()
        component["_licenseText"] = license_text
    if project_url:
        component["projectUrl"] = project_url


def license_tokens(expression: str) -> set[str]:
    ignored = {"AND", "OR", "WITH"}
    return {token for token in SPDX_TOKEN.findall(expression) if token.upper() not in ignored}


def approved_exception(
    component: dict[str, Any],
    policy: dict[str, Any],
    now: datetime,
    scope: str,
    advisory_url: str | None = None,
) -> dict[str, Any] | None:
    for exception in policy.get("approvedExceptions", []):
        if not isinstance(exception, dict):
            continue
        if str(exception.get("scope") or "").lower() != scope.lower():
            continue
        if str(exception.get("package", "")).lower() != component["name"].lower():
            continue
        if str(exception.get("version", "")).lower() != component["version"].lower():
            continue
        if scope == "vulnerability" and advisory_url:
            approved_advisory = str(exception.get("advisoryUrl") or "").strip()
            if approved_advisory != advisory_url:
                continue
        required = ("reason", "owner", "approvalReference", "expiry")
        if any(not str(exception.get(field) or "").strip() for field in required):
            continue
        try:
            expiry = datetime.fromisoformat(str(exception["expiry"]).replace("Z", "+00:00"))
        except ValueError:
            continue
        if expiry >= now:
            return exception
    return None


def evaluate_policy(
    components: list[dict[str, Any]], policy: dict[str, Any], vulnerability: dict[str, Any], now: datetime
) -> tuple[list[dict[str, str]], list[dict[str, str]]]:
    blocks: list[dict[str, str]] = []
    warnings: list[dict[str, str]] = []
    allowed = {str(value).upper() for value in policy.get("allowedLicenses", [])}
    denied = {str(value).upper() for value in policy.get("deniedLicenses", [])}
    for component in components:
        license_value = str(component.get("license") or "NOASSERTION")
        tokens = {value.upper() for value in license_tokens(license_value)}
        exception = approved_exception(component, policy, now, "license")
        if exception:
            component["policyDisposition"] = "approved-exception"
            component["exception"] = exception
        elif license_value == "NOASSERTION" or not tokens:
            component["policyDisposition"] = "blocked-noassertion"
            blocks.append({"code": "LICENSE_NOASSERTION", "component": f"{component['name']}@{component['version']}"})
        elif tokens & denied:
            component["policyDisposition"] = "blocked-denied-license"
            blocks.append({"code": "LICENSE_DENIED", "component": f"{component['name']}@{component['version']}"})
        elif tokens <= allowed:
            component["policyDisposition"] = "allowed"
        else:
            component["policyDisposition"] = "blocked-unapproved-license"
            blocks.append({"code": "LICENSE_UNAPPROVED", "component": f"{component['name']}@{component['version']}"})

    status = str(vulnerability.get("status") or "unavailable").lower()
    vulnerability_policy = policy.get("vulnerabilityPolicy", {})
    if status != "available":
        blocks.append({"code": "VULNERABILITY_SCAN_UNAVAILABLE", "component": "release-candidate"})
    else:
        data_as_of = vulnerability.get("dataAsOfUtc") or vulnerability.get("checkedAtUtc")
        try:
            data_timestamp = datetime.fromisoformat(str(data_as_of).replace("Z", "+00:00"))
            if data_timestamp.tzinfo is None:
                data_timestamp = data_timestamp.replace(tzinfo=timezone.utc)
            age_hours = max(0.0, (now - data_timestamp.astimezone(timezone.utc)).total_seconds() / 3600)
            vulnerability["dataAgeHours"] = round(age_hours, 3)
        except (TypeError, ValueError):
            vulnerability["dataAgeHours"] = None
            blocks.append({"code": "VULNERABILITY_DATA_AGE_UNKNOWN", "component": "release-candidate"})
            age_hours = None
        maximum_age = float(vulnerability_policy.get("maximumDataAgeHours", 0))
        if age_hours is not None and (maximum_age <= 0 or age_hours > maximum_age):
            blocks.append({"code": "VULNERABILITY_DATA_STALE", "component": "release-candidate"})
        blocked_severities = {
            str(value).lower()
            for value in vulnerability_policy.get("blockedSeverities", [])
        }
        for finding in vulnerability.get("vulnerabilities", []):
            severity = str(finding.get("severity") or "unknown").lower()
            if severity in blocked_severities:
                finding_component = {
                    "name": str(finding.get("package") or ""),
                    "version": str(finding.get("version") or ""),
                }
                exception = approved_exception(
                    finding_component,
                    policy,
                    now,
                    "vulnerability",
                    str(finding.get("advisoryUrl") or ""),
                )
                if exception:
                    finding["policyDisposition"] = "approved-exception"
                    finding["exception"] = exception
                else:
                    finding["policyDisposition"] = "blocked"
                    blocks.append(
                        {
                            "code": "VULNERABILITY_BLOCKED",
                            "component": f"{finding.get('package')}@{finding.get('version')}:{severity}",
                        }
                    )
    return blocks, warnings


def spdx_package(component: dict[str, Any]) -> dict[str, Any]:
    identifier = f"SPDXRef-Package-{stable_id(component['name'])}-{stable_id(component['version'])}"
    return {
        "SPDXID": identifier,
        "name": component["name"],
        "versionInfo": component["version"],
        "downloadLocation": "NOASSERTION",
        "filesAnalyzed": False,
        "licenseConcluded": component["license"],
        "licenseDeclared": component["license"],
        "copyrightText": "NOASSERTION",
        "comment": f"Included by: {', '.join(component['scopes'])}; evidence: {component['licenseEvidence']}",
        "externalRefs": [
            {
                "referenceCategory": "PACKAGE-MANAGER",
                "referenceType": "purl",
                "referenceLocator": f"pkg:nuget/{component['name']}@{component['version']}",
            }
        ],
    }


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def public_component(component: dict[str, Any]) -> dict[str, Any]:
    return {key: deepcopy(value) for key, value in component.items() if not key.startswith("_")}


def write_notices(
    path: Path,
    components: list[dict[str, Any]],
    identity: dict[str, Any],
    binding: dict[str, Any],
) -> None:
    lines = [
        "ClearVision THIRD-PARTY NOTICES",
        "================================",
        "",
        "This file is generated from the dependencies present in the final portable ZIP",
        "and declared by the final OperatorLibrary nupkg. It is not a zero-risk or approval statement.",
        "",
        f"Git SHA: {identity.get('gitSha')}",
        f"Repository dirty: {str(identity.get('repositoryDirty')).lower()}",
        f"SDK: {identity.get('sdkVersion')}",
        f"RID/profile: {identity.get('runtimeIdentifier')} / {identity.get('profile')}",
        f"Portable package SHA-256: {identity['portablePackage']['sha256']}",
        f"OperatorLibrary nupkg SHA-256: {identity['operatorLibraryPackage']['sha256']}",
        f"Generated by: {binding['generator']} at {binding['generatedAtUtc']}",
        f"Input checksums: {json.dumps(binding['inputChecksums'], sort_keys=True)}",
        "",
    ]
    for component in components:
        lines.extend(
            [
                f"{component['name']} {component['version']}",
                "-" * min(78, len(component["name"]) + len(component["version"]) + 1),
                f"Component: {component['name']}@{component['version']}",
                f"License: {component['license']}",
                f"Evidence: {component['licenseEvidence']}",
                f"Included by: {', '.join(component['scopes'])}",
            ]
        )
        if component.get("projectUrl"):
            lines.append(f"Project URL: {component['projectUrl']}")
        lines.append("")
    license_texts: dict[str, str] = {}
    for component in components:
        if component.get("licenseTextSha256") and component.get("_licenseText"):
            license_texts.setdefault(str(component["licenseTextSha256"]), str(component["_licenseText"]))
    if license_texts:
        lines.extend(["License texts from packaged NuGet evidence", "===========================================", ""])
        for digest, license_text in sorted(license_texts.items()):
            lines.extend([f"License text SHA-256: {digest}", "", license_text.rstrip(), ""])
    path.write_text("\n".join(lines), encoding="utf-8")


def validate_no_local_paths(paths: Iterable[Path]) -> None:
    patterns = [re.compile(r"(?i)[A-Z]:[\\/]+Users[\\/]+"), re.compile(r"(?i)/home/[^/]+/")]
    for path in paths:
        value = path.read_text(encoding="utf-8-sig", errors="replace")
        if any(pattern.search(value) for pattern in patterns):
            raise ValueError(f"Generated artifact contains a local user path: {path.name}")


def checksum_lines(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line or line.startswith("#"):
            continue
        digest, separator, name = line.partition("  ")
        if not separator or len(digest) != 64 or not name:
            raise ValueError(f"Invalid checksum line: {line}")
        result[name] = digest
    return result


def validate_generated_outputs(
    sbom_path: Path,
    notices_path: Path,
    report_json_path: Path,
    identity_path: Path,
    checksum_path: Path,
    checksum_inputs: list[Path],
) -> None:
    sbom = read_json(sbom_path)
    report = read_json(report_json_path)
    identity = read_json(identity_path)
    sbom_components = {
        (str(item.get("name")), str(item.get("versionInfo"))) for item in sbom.get("packages", [])
    }
    report_components = {
        (str(item.get("name")), str(item.get("version"))) for item in report.get("components", [])
    }
    if sbom_components != report_components:
        raise ValueError("SPDX packages do not match dependency-report components.")
    notice_components = set(
        re.findall(r"(?m)^Component: ([^@\r\n]+)@([^\r\n]+)$", notices_path.read_text(encoding="utf-8-sig"))
    )
    if notice_components != report_components:
        raise ValueError("THIRD-PARTY-NOTICES components do not match dependency-report components.")
    if identity.get("portablePackage", {}).get("sha256") != report.get("identity", {}).get("portablePackage", {}).get("sha256"):
        raise ValueError("Identity manifest and dependency report portable hashes differ.")
    expected = {path.name: sha256(path) for path in checksum_inputs}
    if checksum_lines(checksum_path) != expected:
        raise ValueError("SHA256SUMS does not exactly match the governed release artifacts.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate release supply-chain evidence from final artifacts.")
    parser.add_argument("--portable-zip", required=True, type=Path)
    parser.add_argument("--nupkg", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--identity-input", required=True, type=Path)
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--nuget-packages-root", required=True, type=Path)
    parser.add_argument("--vulnerability-report", type=Path)
    args = parser.parse_args()

    for input_path in (args.portable_zip, args.nupkg, args.identity_input, args.policy):
        if not input_path.is_file():
            raise FileNotFoundError(input_path)
    args.output_dir.mkdir(parents=True, exist_ok=True)

    now = datetime.now(timezone.utc)
    generated_at = now.isoformat().replace("+00:00", "Z")
    source_identity = read_json(args.identity_input)
    policy = read_json(args.policy)
    vulnerability = (
        read_json(args.vulnerability_report)
        if args.vulnerability_report and args.vulnerability_report.is_file()
        else {
            "schemaVersion": "clearvision.vulnerability-scan/v1",
            "status": "unavailable",
            "checkedAtUtc": generated_at,
            "dataAsOfUtc": None,
            "source": policy.get("vulnerabilityPolicy", {}).get("source"),
            "reason": "No vulnerability report was supplied; this is not a zero-vulnerability result.",
            "vulnerabilities": [],
        }
    )

    portable_components, portable_files, deps_name = read_portable_dependencies(args.portable_zip)
    nupkg_root, nupkg_dependencies, nupkg_files = read_nupkg(args.nupkg)
    components = merge_components(portable_components, nupkg_dependencies)
    for component in components:
        attach_license(component, args.nuget_packages_root)

    source_identity_schema = source_identity.pop("schemaVersion", None)
    policy_sha = sha256(args.policy)
    identity_input_sha = sha256(args.identity_input)
    vulnerability_sha = (
        sha256(args.vulnerability_report)
        if args.vulnerability_report and args.vulnerability_report.is_file()
        else None
    )
    portable_sha = sha256(args.portable_zip)
    nupkg_sha = sha256(args.nupkg)
    identity = {
        "schemaVersion": "clearvision.release-identity/v1",
        "generatedAtUtc": generated_at,
        "generator": {"name": TOOL_VERSION, "python": sys.version.split()[0]},
        **source_identity,
        "sourceIdentitySchemaVersion": source_identity_schema,
        "portablePackage": {
            "fileName": args.portable_zip.name,
            "sha256": portable_sha,
            "fileCount": len(portable_files),
            "sizeBytes": args.portable_zip.stat().st_size,
            "depsJson": Path(deps_name).name,
        },
        "operatorLibraryPackage": {
            "fileName": args.nupkg.name,
            "sha256": nupkg_sha,
            "fileCount": len(nupkg_files),
            "sizeBytes": args.nupkg.stat().st_size,
            "packageId": nupkg_root["name"],
            "version": nupkg_root["version"],
        },
        "inputs": {
            "portablePackageSha256": portable_sha,
            "operatorLibraryPackageSha256": nupkg_sha,
            "policySha256": policy_sha,
            "identityInputSha256": identity_input_sha,
            "vulnerabilityReportSha256": vulnerability_sha,
        },
    }

    binding = {
        "gitSha": identity.get("gitSha"),
        "repositoryDirty": identity.get("repositoryDirty"),
        "sdkVersion": identity.get("sdkVersion"),
        "runtimeInventory": identity.get("runtimeInventory"),
        "runtimeIdentifier": identity.get("runtimeIdentifier"),
        "profile": identity.get("profile"),
        "generator": TOOL_VERSION,
        "generatedAtUtc": generated_at,
        "inputChecksums": identity["inputs"],
    }

    blocks, warnings = evaluate_policy(components, policy, vulnerability, now)
    release_eligible = not blocks
    sbom_packages = [spdx_package(component) for component in components]
    extracted_licenses: dict[str, dict[str, Any]] = {}
    for component in components:
        for token in license_tokens(str(component.get("license") or "")):
            if not token.startswith("LicenseRef-"):
                continue
            extracted_licenses.setdefault(
                token,
                {
                    "licenseId": token,
                    "extractedText": str(component.get("_licenseText") or "License text unavailable in local package evidence."),
                    "comment": f"Identified from {component.get('licenseEvidence')}; SHA-256 {component.get('licenseTextSha256') or 'unavailable'}",
                },
            )
    sbom = {
        "spdxVersion": SPDX_VERSION,
        "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT",
        "name": f"{args.portable_zip.stem}-SBOM",
        "documentNamespace": f"https://clearvision.local/spdx/{identity['portablePackage']['sha256']}",
        "creationInfo": {
            "created": generated_at,
            "creators": [f"Tool: {TOOL_VERSION}"],
            "comment": json.dumps(binding, sort_keys=True),
        },
        "documentDescribes": [package["SPDXID"] for package in sbom_packages],
        "packages": sbom_packages,
    }
    if extracted_licenses:
        sbom["hasExtractedLicensingInfos"] = list(extracted_licenses.values())
    public_components = [public_component(component) for component in components]
    dependency_report = {
        "schemaVersion": "clearvision.dependency-report/v1",
        "generatedAtUtc": generated_at,
        "generator": TOOL_VERSION,
        "evidenceBinding": binding,
        "identity": identity,
        "componentCount": len(components),
        "components": public_components,
        "vulnerabilityScan": vulnerability,
        "policy": {
            "fileName": args.policy.name,
            "sha256": policy_sha,
            "blockingFindings": blocks,
            "warnings": warnings,
            "releaseEligible": release_eligible,
        },
    }
    validation = {
        "schemaVersion": "clearvision.supply-chain-validation/v1",
        "generatedAtUtc": generated_at,
        "generator": TOOL_VERSION,
        "evidenceBinding": binding,
        "generationPassed": True,
        "artifactConsistencyPassed": True,
        "releaseEligible": release_eligible,
        "blockingFindings": blocks,
        "warnings": warnings,
        "claims": {
            "vulnerabilityCount": (
                len(vulnerability.get("vulnerabilities", []))
                if str(vulnerability.get("status")).lower() == "available"
                else None
            ),
            "vulnerabilityScanStatus": vulnerability.get("status"),
            "githubReleaseCreated": False,
            "targetMachineValidated": False,
        },
    }

    sbom_path = args.output_dir / "SBOM.spdx.json"
    notices_path = args.output_dir / "THIRD-PARTY-NOTICES.txt"
    report_json_path = args.output_dir / "dependency-report.json"
    report_md_path = args.output_dir / "dependency-report.md"
    identity_path = args.output_dir / "identity-manifest.json"
    validation_path = args.output_dir / "validation-summary.json"
    checksum_path = args.output_dir / "SHA256SUMS"

    write_json(sbom_path, sbom)
    write_notices(notices_path, components, identity, binding)
    write_json(report_json_path, dependency_report)
    write_json(identity_path, identity)
    write_json(validation_path, validation)
    report_md_path.write_text(
        "\n".join(
            [
                "# Release dependency report",
                "",
                f"- Portable package: `{args.portable_zip.name}`",
                f"- Portable SHA-256: `{identity['portablePackage']['sha256']}`",
                f"- OperatorLibrary package: `{args.nupkg.name}`",
                f"- OperatorLibrary SHA-256: `{identity['operatorLibraryPackage']['sha256']}`",
                f"- Git SHA / dirty: `{identity.get('gitSha')}` / `{str(identity.get('repositoryDirty')).lower()}`",
                f"- SDK / RID / profile: `{identity.get('sdkVersion')}` / `{identity.get('runtimeIdentifier')}` / `{identity.get('profile')}`",
                f"- Generator / generated: `{TOOL_VERSION}` / `{generated_at}`",
                f"- Input checksums: `{json.dumps(binding['inputChecksums'], sort_keys=True)}`",
                f"- Component count: {len(components)}",
                f"- Vulnerability scan: `{vulnerability.get('status')}` (unavailable is not zero)",
                f"- Release eligible: `{str(release_eligible).lower()}`",
                "",
                "| Component | Version | License | Scope | Policy |",
                "|---|---:|---|---|---|",
                *[
                    f"| {item['name']} | {item['version']} | {item['license']} | {', '.join(item['scopes'])} | {item['policyDisposition']} |"
                    for item in public_components
                ],
                "",
                "## Blocking findings",
                "",
                *([f"- `{item['code']}`: {item['component']}" for item in blocks] or ["- None."]),
                "",
            ]
        ),
        encoding="utf-8",
    )

    generated = [sbom_path, notices_path, report_json_path, report_md_path, identity_path, validation_path]
    validate_no_local_paths(generated)
    checksum_inputs = [args.portable_zip, args.nupkg, *generated]
    if args.vulnerability_report and args.vulnerability_report.is_file():
        checksum_inputs.append(args.vulnerability_report)
    checksum_path.write_text(
        "\n".join(
            [
                f"# Git SHA: {identity.get('gitSha')}",
                f"# Repository dirty: {str(identity.get('repositoryDirty')).lower()}",
                f"# SDK / RID / profile: {identity.get('sdkVersion')} / {identity.get('runtimeIdentifier')} / {identity.get('profile')}",
                f"# Generator / generated: {TOOL_VERSION} / {generated_at}",
                f"# Input checksums: {json.dumps(binding['inputChecksums'], sort_keys=True)}",
                *[f"{sha256(path)}  {path.name}" for path in checksum_inputs],
            ]
        )
        + "\n",
        encoding="utf-8",
    )
    validate_generated_outputs(
        sbom_path,
        notices_path,
        report_json_path,
        identity_path,
        checksum_path,
        checksum_inputs,
    )

    print(json.dumps(validation, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
