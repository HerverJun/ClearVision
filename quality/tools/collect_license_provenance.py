from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from xml.etree import ElementTree

import generate_release_supply_chain as supply


TOOL_VERSION = "collect_license_provenance/2026-09-01.wave3d"
USER_AGENT = "ClearVision-Release-Provenance/1.0"


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def request(url: str, accept: str = "application/json") -> tuple[bytes, dict[str, Any]]:
    retrieved_at = utc_now()
    req = urllib.request.Request(
        url,
        headers={"Accept": accept, "User-Agent": USER_AGENT},
        method="GET",
    )
    with urllib.request.urlopen(req, timeout=45) as response:
        body = response.read()
        headers = response.headers
        evidence = {
            "requestedUrl": url,
            "responseUrl": response.geturl(),
            "retrievedAtUtc": retrieved_at,
            "statusCode": response.status,
            "etag": headers.get("ETag"),
            "lastModified": headers.get("Last-Modified"),
            "contentSha256": sha256_bytes(body),
        }
        return body, evidence


def json_request(url: str) -> tuple[Any, dict[str, Any]]:
    body, evidence = request(url, "application/vnd.github+json, application/json")
    return json.loads(body.decode("utf-8-sig")), evidence


def xml_child(element: ElementTree.Element | None, name: str) -> ElementTree.Element | None:
    if element is None:
        return None
    return next((item for item in element if item.tag.rsplit("}", 1)[-1] == name), None)


def xml_text(element: ElementTree.Element | None, name: str) -> str | None:
    item = xml_child(element, name)
    return item.text.strip() if item is not None and item.text else None


def parse_package_bytes(package_bytes: bytes, expected_id: str, expected_version: str) -> dict[str, Any]:
    from io import BytesIO

    with zipfile.ZipFile(BytesIO(package_bytes)) as archive:
        names = sorted(name for name in archive.namelist() if name and not name.endswith("/"))
        nuspec_names = [name for name in names if name.lower().endswith(".nuspec")]
        if len(nuspec_names) != 1:
            raise ValueError(f"{expected_id}@{expected_version}: expected one nuspec")
        nuspec_bytes = archive.read(nuspec_names[0])
        root = ElementTree.fromstring(nuspec_bytes)
        metadata = xml_child(root, "metadata")
        package_id = xml_text(metadata, "id")
        version = xml_text(metadata, "version")
        if (package_id or "").lower() != expected_id.lower() or version != expected_version:
            raise ValueError(
                f"package identity mismatch: expected {expected_id}@{expected_version}, got {package_id}@{version}"
            )
        license_element = xml_child(metadata, "license")
        license_expression = None
        license_file = None
        license_file_bytes = None
        if license_element is not None and license_element.text:
            if str(license_element.attrib.get("type") or "").lower() == "expression":
                license_expression = license_element.text.strip()
            else:
                license_file = license_element.text.strip().replace("\\", "/")
                match = next((name for name in names if name.lower() == license_file.lower()), None)
                if match:
                    license_file_bytes = archive.read(match)
        repository_element = xml_child(metadata, "repository")
        repository = None
        if repository_element is not None:
            repository = {
                "type": repository_element.attrib.get("type"),
                "url": repository_element.attrib.get("url"),
                "commit": repository_element.attrib.get("commit"),
            }
        return {
            "id": package_id,
            "version": version,
            "nuspecPath": nuspec_names[0],
            "nuspecSha256": sha256_bytes(nuspec_bytes),
            "license": {
                "expression": license_expression,
                "file": license_file,
                "fileSha256": sha256_bytes(license_file_bytes) if license_file_bytes else None,
                "fileBytes": license_file_bytes,
                "legacyUrl": xml_text(metadata, "licenseUrl"),
            },
            "projectUrl": xml_text(metadata, "projectUrl"),
            "repository": repository,
        }


def identify_license(text: str) -> str:
    normalized = " ".join(text.lower().split())
    if "reciprocal public license 1.5" in normalized and "license agreement" in normalized:
        return "RPL-1.5 OR LicenseRef-LuckyPenny-Commercial"
    if "microsoft.data.sqlclient.sni library" in normalized and "microsoft software license terms" in normalized:
        return "LicenseRef-Microsoft-Data-SqlClient-SNI"
    if "the mit license" in normalized and "permission is hereby granted, free of charge" in normalized:
        return "MIT"
    hsl_markers = (
        "可以免费运行24小时",
        "需要商业授权",
        "未经正式合同授权而商业使用均视为侵权",
    )
    if sum(marker in text for marker in hsl_markers) >= 2:
        return "LicenseRef-HslCommunication-Commercial"
    return "NOASSERTION"


def github_coordinates(repository_url: str) -> tuple[str, str]:
    parsed = urllib.parse.urlparse(repository_url.rstrip("/"))
    if parsed.netloc.lower() != "github.com":
        raise ValueError(f"Only authoritative GitHub repository URLs are supported: {repository_url}")
    parts = [part for part in parsed.path.split("/") if part]
    if len(parts) != 2:
        raise ValueError(f"Invalid GitHub repository URL: {repository_url}")
    return parts[0], parts[1].removesuffix(".git")


def github_repository_evidence(config: dict[str, Any]) -> tuple[dict[str, Any], bytes | None]:
    owner, repo = github_coordinates(str(config["repositoryUrl"]))
    tag = config.get("tag")
    ref_evidence: dict[str, Any]
    commit: str | None = None
    if tag:
        encoded_tag = urllib.parse.quote(str(tag), safe="")
        ref, ref_source = json_request(
            f"https://api.github.com/repos/{owner}/{repo}/git/ref/tags/{encoded_tag}"
        )
        commit = str(ref.get("object", {}).get("sha") or "") or None
        ref_evidence = {
            "tag": tag,
            "commit": commit,
            "objectType": ref.get("object", {}).get("type"),
            "source": ref_source,
        }
    else:
        branch = "master"
        commit_row, ref_source = json_request(
            f"https://api.github.com/repos/{owner}/{repo}/commits/{branch}"
        )
        commit = str(commit_row.get("sha") or "") or None
        ref_evidence = {
            "tag": None,
            "branch": branch,
            "commit": commit,
            "objectType": "commit",
            "source": ref_source,
        }

    license_bytes = None
    license_evidence = None
    license_path = config.get("licensePath")
    if license_path and commit:
        encoded_path = "/".join(urllib.parse.quote(part, safe="") for part in str(license_path).split("/"))
        content, content_source = json_request(
            f"https://api.github.com/repos/{owner}/{repo}/contents/{encoded_path}?ref={commit}"
        )
        if content.get("encoding") != "base64" or not content.get("content"):
            raise ValueError(
                f"GitHub license content is not base64: {config['repositoryUrl']}@{commit}:{license_path}"
            )
        license_bytes = base64.b64decode(re.sub(r"\s+", "", str(content["content"])))
        license_evidence = {
            "path": content.get("path"),
            "blobSha": content.get("sha"),
            "sha256": sha256_bytes(license_bytes),
            "htmlUrl": content.get("html_url"),
            "source": content_source,
        }
    return (
        {
            "url": config["repositoryUrl"],
            "ref": ref_evidence,
            "versionBound": bool(tag and commit),
            "licenseFile": license_evidence,
        },
        license_bytes,
    )


def catalog_repository(catalog: dict[str, Any]) -> dict[str, Any] | None:
    value = catalog.get("repository")
    return value if isinstance(value, dict) else None


def collect_package(
    config: dict[str, Any], nuget_root: Path, generated_at: str
) -> dict[str, Any]:
    package_id = str(config["id"])
    version = str(config["version"])
    lower_id = package_id.lower()
    package_path = nuget_root / lower_id / version.lower() / f"{lower_id}.{version.lower()}.nupkg"
    if not package_path.is_file():
        raise FileNotFoundError(package_path)
    local_bytes = package_path.read_bytes()
    local_sha256 = sha256_bytes(local_bytes)
    package_metadata = parse_package_bytes(local_bytes, package_id, version)

    registration_url = (
        f"https://api.nuget.org/v3/registration5-semver1/{urllib.parse.quote(lower_id)}/"
        f"{urllib.parse.quote(version.lower())}.json"
    )
    registration, registration_source = json_request(registration_url)
    catalog_url = registration.get("catalogEntry")
    if not isinstance(catalog_url, str):
        raise ValueError(f"NuGet registration has no catalogEntry: {package_id}@{version}")
    catalog, catalog_source = json_request(catalog_url)
    package_content_url = registration.get("packageContent")
    if not isinstance(package_content_url, str):
        raise ValueError(f"NuGet registration has no packageContent: {package_id}@{version}")
    official_bytes, official_source = request(package_content_url, "application/octet-stream")
    official_sha256 = sha256_bytes(official_bytes)
    if local_sha256 != official_sha256:
        raise ValueError(f"Local nupkg does not match official NuGet bytes: {package_id}@{version}")
    official_metadata = parse_package_bytes(official_bytes, package_id, version)
    if official_metadata["nuspecSha256"] != package_metadata["nuspecSha256"]:
        raise ValueError(f"Local and official nuspec hashes differ: {package_id}@{version}")

    repository, upstream_license_bytes = github_repository_evidence(config)
    declared_repository = package_metadata.get("repository") or catalog_repository(catalog)
    declared_commit = (declared_repository or {}).get("commit")
    tag_commit = repository.get("ref", {}).get("commit")
    commit_conflict = bool(declared_commit and tag_commit and declared_commit != tag_commit)

    authoritative_url_evidence = None
    authoritative_url_bytes = None
    if config.get("authoritativeLicenseUrl"):
        authoritative_url_bytes, authoritative_url_evidence = request(
            str(config["authoritativeLicenseUrl"]), "text/html, text/plain"
        )

    package_license_bytes = package_metadata["license"].pop("fileBytes")
    license_expression = package_metadata["license"].get("expression")
    identification_source = "nuspec-license-expression" if license_expression else None
    if not license_expression and package_license_bytes:
        license_expression = identify_license(package_license_bytes.decode("utf-8-sig", errors="replace"))
        identification_source = "nupkg-license-file"
    if not license_expression and upstream_license_bytes:
        license_expression = identify_license(upstream_license_bytes.decode("utf-8-sig", errors="replace"))
        identification_source = "version-bound-upstream-license-file"
    if not license_expression and authoritative_url_bytes:
        license_expression = identify_license(authoritative_url_bytes.decode("utf-8-sig", errors="replace"))
        identification_source = "vendor-license-page"
    license_expression = license_expression or "NOASSERTION"
    expected = str(config.get("expectedLicenseExpression") or "NOASSERTION")
    expression_conflict = license_expression != expected
    version_binding_missing = bool(config.get("versionBindingRequired") and not repository["versionBound"])
    forced_conflict = bool(config.get("forceConflictingEvidence"))
    conflicting = commit_conflict or expression_conflict or version_binding_missing or forced_conflict
    disposition = "CONFLICTING_EVIDENCE" if conflicting else "IDENTIFIED"

    nuget_repository = catalog_repository(catalog)
    authority_chain = [
        {"kind": "nupkg", "sha256": local_sha256, "source": official_source},
        {
            "kind": "nuspec",
            "path": package_metadata["nuspecPath"],
            "sha256": package_metadata["nuspecSha256"],
            "licenseExpression": package_metadata["license"].get("expression"),
            "licenseFile": package_metadata["license"].get("file"),
            "licenseFileSha256": package_metadata["license"].get("fileSha256"),
            "licenseUrl": package_metadata["license"].get("legacyUrl"),
            "repository": package_metadata.get("repository"),
        },
        {
            "kind": "nuget-registration",
            "registration": registration_source,
            "catalog": catalog_source,
            "catalogMetadata": {
                "id": catalog.get("id"),
                "version": catalog.get("version"),
                "published": catalog.get("published"),
                "licenseExpression": catalog.get("licenseExpression"),
                "licenseUrl": catalog.get("licenseUrl"),
                "projectUrl": catalog.get("projectUrl"),
                "repository": nuget_repository,
                "packageHash": catalog.get("packageHash"),
                "packageHashAlgorithm": catalog.get("packageHashAlgorithm"),
            },
        },
        {"kind": "upstream-repository", **repository},
    ]
    if authoritative_url_evidence:
        authority_chain.append(
            {"kind": "vendor-license-page", "source": authoritative_url_evidence}
        )

    return {
        "id": package_id,
        "version": version,
        "packageSha256": local_sha256,
        "officialNugetPackageSha256": official_sha256,
        "packageBytesMatchOfficialNuget": True,
        "licenseExpression": license_expression,
        "identificationSource": identification_source,
        "identificationDisposition": disposition,
        "repository": repository,
        "nuspec": {
            "path": package_metadata["nuspecPath"],
            "sha256": package_metadata["nuspecSha256"],
            "licenseExpression": package_metadata["license"].get("expression"),
            "licenseFile": package_metadata["license"].get("file"),
            "licenseFileSha256": package_metadata["license"].get("fileSha256"),
            "licenseUrl": package_metadata["license"].get("legacyUrl"),
            "projectUrl": package_metadata.get("projectUrl"),
            "repository": package_metadata.get("repository"),
        },
        "licenseEvidence": {
            "packageLicenseFileSha256": package_metadata["license"].get("fileSha256"),
            "upstreamLicenseFileSha256": (repository.get("licenseFile") or {}).get("sha256"),
            "packageLicenseTakesPrecedence": bool(config.get("packageLicenseTakesPrecedence")),
        },
        "conflicts": {
            "declaredCommitDiffersFromTag": commit_conflict,
            "identifiedExpressionDiffersFromExpected": expression_conflict,
            "requiredVersionBindingMissing": version_binding_missing,
            "authorityConflictDeclared": forced_conflict,
        },
        "note": config.get("note"),
        "evidenceRetrievedAtUtc": generated_at,
        "authorityChain": authority_chain,
    }


def final_component_keys(portable_zip: Path, nupkg: Path) -> set[tuple[str, str]]:
    portable, _, _ = supply.read_portable_dependencies(portable_zip)
    _, dependencies, _ = supply.read_nupkg(nupkg)
    components = supply.merge_components(portable, dependencies)
    return {(str(item["name"]).lower(), str(item["version"]).lower()) for item in components}


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect authoritative package license provenance.")
    parser.add_argument("--portable-zip", required=True, type=Path)
    parser.add_argument("--nupkg", required=True, type=Path)
    parser.add_argument("--nuget-packages-root", required=True, type=Path)
    parser.add_argument("--sources", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    sources = supply.read_json(args.sources)
    generated_at = utc_now()
    final_keys = final_component_keys(args.portable_zip, args.nupkg)
    configured = sources.get("packages", [])
    missing = [
        f"{row.get('id')}@{row.get('version')}"
        for row in configured
        if (str(row.get("id")).lower(), str(row.get("version")).lower()) not in final_keys
    ]
    if missing:
        raise ValueError(f"Configured provenance packages are absent from final artifacts: {missing}")

    packages = [collect_package(row, args.nuget_packages_root, generated_at) for row in configured]
    result = {
        "schemaVersion": "clearvision.license-provenance/v1",
        "generatedAtUtc": generated_at,
        "generator": TOOL_VERSION,
        "sourcesPolicy": {"fileName": args.sources.name, "sha256": supply.sha256(args.sources)},
        "finalArtifactBinding": {
            "portableZipFileName": args.portable_zip.name,
            "portableZipSha256": supply.sha256(args.portable_zip),
            "operatorLibraryNupkgFileName": args.nupkg.name,
            "operatorLibraryNupkgSha256": supply.sha256(args.nupkg),
        },
        "packages": packages,
    }
    write_json(args.output, result)
    print(json.dumps({"output": str(args.output), "packages": len(packages)}, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, urllib.error.URLError) as exc:
        print(f"license provenance collection failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
