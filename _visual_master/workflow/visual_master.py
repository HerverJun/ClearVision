#!/usr/bin/env python3
"""Manifest-driven ClearVision UI visual reference workflow.

The script is deliberately scoped to _visual_master. It reuses PPT Master's
OpenAI image backend and never writes credentials or production UI files.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable
from urllib.parse import quote, quote_plus, urlsplit, urlunsplit

from PIL import Image, ImageDraw, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = ROOT.parent
MANIFEST_PATH = ROOT / "image_prompts.json"
BASELINE_MANIFEST_PATH = ROOT / "current" / "baseline_manifest.json"
PPT_ROOT = Path(r"C:\Users\HerverJun\Desktop\ppt")
PPT_SKILL_ROOT = PPT_ROOT / ".agents" / "skills" / "ppt-master"
PPT_IMAGE_GEN = PPT_SKILL_ROOT / "scripts" / "image_gen.py"
PPT_PYTHON = PPT_ROOT / ".venv" / "Scripts" / "python.exe"
SECURE_PPT_IMAGE_GEN = Path(__file__).with_name("secure_ppt_image_gen.py")
R2_SOURCE = (
    REPOSITORY_ROOT
    / ".tmp"
    / "studio-ui-next"
    / "view-polish-r2"
    / "R2.7"
    / "visual-master-current-20260815"
    / "final-matrix"
)
F07_SOURCE = (
    REPOSITORY_ROOT
    / ".tmp"
    / "studio-ui-next"
    / "f07"
    / "visual-master-current-20260815"
)
F04_EMPTY_SOURCE = (
    REPOSITORY_ROOT
    / ".tmp"
    / "studio-ui-next"
    / "f04"
    / "visual-master-current-empty-20260815"
)

ALLOWED_STATUSES = {
    "Pending",
    "Generated",
    "Failed",
    "Needs-Manual",
    "Approved-Candidate",
}
RETRYABLE_STATUSES = {"Pending", "Failed", "Needs-Manual"}
ALLOWED_ROLES = {"anchor", "local"}
ALLOWED_TEXT_POLICIES = {"preserve-semantic-no-copy"}
ENV_KEYS = {
    "IMAGE_BACKEND",
    "OPENAI_API_KEY",
    "OPENAI_BASE_URL",
    "OPENAI_MODEL",
    "OPENAI_OUTPUT_FORMAT",
    "OPENAI_SIZE_PRESET",
    "OPENAI_RESPONSE_FORMAT",
    "OPENAI_QUALITY",
    "OPENAI_BACKGROUND",
}
UPLOAD_APPROVAL_HOST_ENV = "CLEARVISION_VISUAL_UPLOAD_APPROVED_HOST"
UPLOAD_APPROVAL_SCOPE_ENV = "CLEARVISION_VISUAL_UPLOAD_APPROVED_SCOPE"
UPLOAD_APPROVAL_SCOPE = "models-and-clearvision-composite-reference-boards"
PPT_GENERATOR_PATH_ENV = "CLEARVISION_PPT_IMAGE_GEN_PATH"
PPT_GENERATOR_SHA_ENV = "CLEARVISION_PPT_IMAGE_GEN_SHA256"
SECURE_LAUNCHER_SHA_ENV = "CLEARVISION_SECURE_PPT_IMAGE_GEN_SHA256"
CHILD_ENV_ALLOWLIST = {
    "SystemRoot",
    "WINDIR",
    "COMSPEC",
    "PATH",
    "PATHEXT",
    "TEMP",
    "TMP",
    "TMPDIR",
    "USERPROFILE",
    "HOMEDRIVE",
    "HOMEPATH",
    "APPDATA",
    "LOCALAPPDATA",
    "PROGRAMDATA",
    "NUMBER_OF_PROCESSORS",
    "PROCESSOR_ARCHITECTURE",
    "LANG",
    "LC_ALL",
    "PYTHONUTF8",
    "PYTHONIOENCODING",
}

SCENE_META = {
    "S00": ("Login", "Public authentication and auth error recovery"),
    "S01": ("Overview", "System and recent-project operational overview"),
    "S02": ("Projects", "Project discovery and lifecycle entry"),
    "S03": ("Flow Workspace", "Core flow editing workspace"),
    "S04": ("Flow Inspector", "Selected-node editing and validation"),
    "S05": ("Flow Preview and ROI", "Image preview, ROI editing, and stale evidence"),
    "S06": ("Flow Run", "Run readiness, result, and run details"),
    "S07": ("Results", "Inspection result and evidence investigation"),
    "S08": ("Stations", "Station fleet and detail operations"),
    "S09": ("Inspection", "Formal inspection preparation and recent result"),
    "S10": ("Settings", "System settings and authority boundary"),
    "S11": ("AI Workbench", "AI clarification, build, handoff, and recovery"),
    "S12": ("Operators", "Operator library and catalog integrity"),
    "S13": ("Diagnostics and About", "Service health and product identity"),
}

ANCHOR_BASELINE_IDS = {
    "S04-B0",
    "S05-B2",
    "S07-B0",
    "S08-B0",
    "S11-B0",
}
F07_ANCHOR_FILES = {"settings-camera-b0-1920x1080-light-compact.png"}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def rel(path: Path) -> str:
    return path.resolve().relative_to(ROOT.resolve()).as_posix()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def build_child_environment(
    runtime: dict[str, str], *, expected_generator_sha256: str | None = None,
    expected_secure_launcher_sha256: str | None = None,
) -> dict[str, str]:
    """Build the smallest environment needed by the secured PPT generator."""
    actual_generator_sha256 = sha256(PPT_IMAGE_GEN)
    expected_generator_sha256 = (
        expected_generator_sha256 or runtime.get(PPT_GENERATOR_SHA_ENV)
    )
    if not expected_generator_sha256:
        raise RuntimeError("PPT Master generator is not bound to a preflight hash")
    if (
        expected_generator_sha256 != actual_generator_sha256
    ):
        raise RuntimeError("PPT Master generator changed after the active preflight")
    actual_secure_launcher_sha256 = sha256(SECURE_PPT_IMAGE_GEN)
    expected_secure_launcher_sha256 = (
        expected_secure_launcher_sha256 or runtime.get(SECURE_LAUNCHER_SHA_ENV)
    )
    if not expected_secure_launcher_sha256:
        raise RuntimeError("Secured launcher is not bound to a preflight hash")
    if expected_secure_launcher_sha256 != actual_secure_launcher_sha256:
        raise RuntimeError("Secured launcher changed after the active preflight")
    approved_host = require_upload_consent(runtime.get("OPENAI_BASE_URL"))
    child = {
        key: value
        for key in CHILD_ENV_ALLOWLIST
        if (value := os.environ.get(key))
    }
    child.update({key: runtime[key] for key in ENV_KEYS if key in runtime})
    child.update(
        {
            UPLOAD_APPROVAL_HOST_ENV: approved_host,
            UPLOAD_APPROVAL_SCOPE_ENV: UPLOAD_APPROVAL_SCOPE,
            PPT_GENERATOR_PATH_ENV: str(PPT_IMAGE_GEN.resolve()),
            PPT_GENERATOR_SHA_ENV: actual_generator_sha256,
            SECURE_LAUNCHER_SHA_ENV: actual_secure_launcher_sha256,
            "PYTHONNOUSERSITE": "1",
            "PYTHONUTF8": "1",
        }
    )
    return child


def atomic_write_json(path: Path, payload: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", dir=path.parent, suffix=".tmp", delete=False
    ) as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
        temporary = Path(handle.name)
    os.replace(temporary, path)


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def root_path(value: str, *, must_exist: bool = False) -> Path:
    candidate = (ROOT / value).resolve()
    if not candidate.is_relative_to(ROOT.resolve()):
        raise ValueError(f"Path escapes _visual_master: {value}")
    if must_exist and not candidate.is_file():
        raise FileNotFoundError(f"Missing referenced file: {value}")
    return candidate


def safe_named_path(directory: Path, filename: str, *, must_exist: bool = False) -> Path:
    if Path(filename).name != filename or not filename.endswith(".png"):
        raise ValueError(f"Unsafe image filename: {filename}")
    directory_resolved = directory.resolve()
    candidate = directory / filename
    if candidate.is_symlink():
        raise ValueError(f"Symbolic-link image paths are not allowed: {candidate}")
    resolved = candidate.resolve()
    if not resolved.is_relative_to(directory_resolved):
        raise ValueError(f"Image path escapes its managed directory: {candidate}")
    if must_exist and not resolved.is_file():
        raise FileNotFoundError(f"Missing managed image: {candidate}")
    return resolved


def tolerant_capture_metadata(path: Path) -> tuple[dict[str, Any], str]:
    text = path.read_text(encoding="utf-8", errors="replace")
    try:
        parsed = json.loads(text)
        dom = parsed.get("dom", {})
        return {
            "scene": parsed.get("scene"),
            "variant": parsed.get("variant"),
            "route": parsed.get("route"),
            "state": parsed.get("state"),
            "viewport": dom.get("viewport"),
            "theme": dom.get("theme"),
            "density": dom.get("density"),
        }, "valid-json"
    except json.JSONDecodeError as exc:
        def string_value(key: str) -> str | None:
            match = re.search(rf'"{re.escape(key)}"\s*:\s*"([^"\r\n]*)"', text)
            return match.group(1) if match else None

        viewport_match = re.search(
            r'"viewport"\s*:\s*\{[^}]*"width"\s*:\s*(\d+)[^}]*"height"\s*:\s*(\d+)',
            text,
            re.DOTALL,
        )
        viewport = None
        if viewport_match:
            viewport = {
                "width": int(viewport_match.group(1)),
                "height": int(viewport_match.group(2)),
                "dpr": 1,
            }
        return {
            "scene": string_value("scene"),
            "variant": string_value("variant"),
            "route": string_value("route"),
            "state": string_value("state"),
            "viewport": viewport,
            "theme": string_value("theme"),
            "density": string_value("density"),
            "parse_error": f"line {exc.lineno}, column {exc.colno}",
        }, "invalid-json-preserved"


def copy_baseline() -> None:
    if not R2_SOURCE.is_dir():
        raise FileNotFoundError(f"Missing R2 evidence root: {R2_SOURCE}")
    if not F07_SOURCE.is_dir():
        raise FileNotFoundError(f"Missing F07 evidence root: {F07_SOURCE}")

    screens: list[dict[str, Any]] = []
    r2_target = ROOT / "current" / "r2"
    r2_evidence = r2_target / "evidence"
    r2_target.mkdir(parents=True, exist_ok=True)
    r2_evidence.mkdir(parents=True, exist_ok=True)

    for source_dir in sorted(path for path in R2_SOURCE.iterdir() if path.is_dir()):
        source_png = source_dir / "after.png"
        source_json = source_dir / "capture-final.json"
        if not source_png.is_file() or not source_json.is_file():
            raise FileNotFoundError(f"Incomplete R2 evidence group: {source_dir}")
        target_png = r2_target / f"{source_dir.name}.png"
        target_json = r2_evidence / f"{source_dir.name}.json"
        shutil.copy2(source_png, target_png)
        shutil.copy2(source_json, target_json)
        metadata, metadata_status = tolerant_capture_metadata(source_json)
        scene = str(metadata.get("scene") or source_dir.name.split("-")[0])
        page_name, purpose = SCENE_META.get(scene, (scene, "Current UI state"))
        screens.append(
            {
                "id": source_dir.name,
                "page_name": page_name,
                "route": metadata.get("route"),
                "purpose": purpose,
                "state": metadata.get("state"),
                "variant": metadata.get("variant"),
                "viewport": metadata.get("viewport"),
                "theme": metadata.get("theme"),
                "density": metadata.get("density"),
                "screenshot": rel(target_png),
                "capture_metadata": rel(target_json),
                "source_screenshot": str(source_png),
                "sha256": sha256(target_png),
                "metadata_status": metadata_status,
                "metadata_parse_error": metadata.get("parse_error"),
                "anchor_candidate": source_dir.name in ANCHOR_BASELINE_IDS,
                "evidence_scope": "current-vue-static-chromium-fixture",
            }
        )

    settings_target = ROOT / "current" / "settings"
    settings_evidence = settings_target / "evidence"
    settings_target.mkdir(parents=True, exist_ok=True)
    settings_evidence.mkdir(parents=True, exist_ok=True)
    device_names = {
        "settings-camera": ("Camera Settings", "Camera binding, acquisition, preview, and trigger input"),
        "settings-plc": ("PLC Settings", "PLC connection, mapping, validation, and diagnostics"),
        "settings-tcp": ("TCP Settings", "TCP connection, send/receive, and diagnostics"),
        "settings-station": ("Station Communication", "Studio-to-Station communication configuration"),
        "settings-ai-model": ("AI Model Settings", "AI provider and model availability configuration"),
    }
    for source_png in sorted(F07_SOURCE.glob("*.png")):
        source_json = source_png.with_suffix(".json")
        if not source_json.is_file():
            raise FileNotFoundError(f"Missing F07 metadata: {source_json}")
        metadata = read_json(source_json)
        target_png = settings_target / source_png.name
        target_json = settings_evidence / source_json.name
        shutil.copy2(source_png, target_png)
        shutil.copy2(source_json, target_json)
        prefix = next((key for key in device_names if source_png.name.startswith(key)), "settings")
        page_name, purpose = device_names.get(prefix, ("Settings", "Settings subsection"))
        screens.append(
            {
                "id": metadata.get("scenario", source_png.stem),
                "page_name": page_name,
                "route": "#/settings",
                "purpose": purpose,
                "state": metadata.get("scenario"),
                "variant": "F07-current",
                "viewport": metadata.get("viewport"),
                "theme": metadata.get("theme"),
                "density": metadata.get("density"),
                "screenshot": rel(target_png),
                "capture_metadata": rel(target_json),
                "source_screenshot": str(source_png),
                "sha256": sha256(target_png),
                "metadata_status": "valid-json",
                "metadata_parse_error": None,
                "anchor_candidate": source_png.name in F07_ANCHOR_FILES,
                "evidence_scope": "current-vue-static-chromium-device-fixture",
            }
        )

    empty_png = F04_EMPTY_SOURCE / "projects-empty-1600x1000-dpr-1.png"
    empty_json = empty_png.with_suffix(".json")
    if empty_png.is_file() and empty_json.is_file():
        target_png = r2_target / "S02-EMPTY.png"
        target_json = r2_evidence / "S02-EMPTY.json"
        shutil.copy2(empty_png, target_png)
        shutil.copy2(empty_json, target_json)
        metadata = read_json(empty_json)
        screens.append(
            {
                "id": "S02-EMPTY",
                "page_name": "Projects",
                "route": "#/projects",
                "purpose": "Current-revision project empty state",
                "state": "empty",
                "variant": "F04-current",
                "viewport": metadata.get("viewport"),
                "theme": metadata.get("theme"),
                "density": metadata.get("density"),
                "screenshot": rel(target_png),
                "capture_metadata": rel(target_json),
                "source_screenshot": str(empty_png),
                "sha256": sha256(target_png),
                "metadata_status": "valid-json",
                "metadata_parse_error": None,
                "anchor_candidate": False,
                "evidence_scope": "current-vue-static-chromium-project-lifecycle-fixture",
            }
        )

    payload = {
        "schema_version": "clearvision-current-baseline.v1",
        "generated_at": utc_now(),
        "source_revision": _git_revision(),
        "evidence_boundary": {
            "valid_for": ["current Vue composition", "deterministic UI state projections"],
            "not_performed": [
                "real WebView2",
                "Windows 125% DPI",
                "live authenticated endpoints",
                "real camera/PLC/Station hardware",
                "release publish",
            ],
        },
        "screen_count": len(screens),
        "screens": screens,
    }
    atomic_write_json(BASELINE_MANIFEST_PATH, payload)
    print(f"Copied {len(screens)} current screenshots.")
    print(f"Baseline manifest: {BASELINE_MANIFEST_PATH}")


def _git_revision() -> str | None:
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=REPOSITORY_ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    return result.stdout.strip() if result.returncode == 0 else None


def load_manifest() -> dict[str, Any]:
    if not MANIFEST_PATH.is_file():
        raise FileNotFoundError(f"Missing image manifest: {MANIFEST_PATH}")
    manifest = read_json(MANIFEST_PATH)
    schema = manifest.get("schema_version") if isinstance(manifest, dict) else None
    if schema != "clearvision-ui-visual-master.v1":
        raise ValueError(
            "Legacy workflow refuses manifest schema "
            f"{schema!r}; use visual_options.py for the active D/E v3 manifest."
        )
    return manifest


def validate_manifest(manifest: dict[str, Any], *, require_references: bool = False) -> list[str]:
    errors: list[str] = []
    if manifest.get("schema_version") != "clearvision-ui-visual-master.v1":
        errors.append("schema_version must be clearvision-ui-visual-master.v1")
    if manifest.get("model") != "gpt-image-2":
        errors.append("top-level model must be exact gpt-image-2")
    entries = manifest.get("entries")
    if not isinstance(entries, list) or not entries:
        return errors + ["entries must be a non-empty array"]
    ids: set[str] = set()
    filenames: set[str] = set()
    required_prompt_terms = (
        "Preserve product semantics and functional hierarchy",
        "visual design reference",
        "PROHIBITIONS",
    )
    for index, entry in enumerate(entries):
        label = f"entries[{index}]"
        if not isinstance(entry, dict):
            errors.append(f"{label} must be an object")
            continue
        entry_id = entry.get("id")
        if not isinstance(entry_id, str) or not re.fullmatch(r"[0-9]{2}_[a-z0-9_]+", entry_id):
            errors.append(f"{label}.id must be stable NN_snake_case")
        elif entry_id in ids:
            errors.append(f"duplicate id: {entry_id}")
        else:
            ids.add(entry_id)
        filename = entry.get("filename")
        if not isinstance(filename, str) or Path(filename).name != filename or not filename.endswith(".png"):
            errors.append(f"{label}.filename must be a bare .png filename")
        elif filename in filenames:
            errors.append(f"duplicate filename: {filename}")
        else:
            filenames.add(filename)
        if entry.get("page_role") not in ALLOWED_ROLES:
            errors.append(f"{label}.page_role must be anchor or local")
        if entry.get("text_policy") not in ALLOWED_TEXT_POLICIES:
            errors.append(f"{label}.text_policy is unsupported")
        if entry.get("aspect_ratio") != "16:9":
            errors.append(f"{label}.aspect_ratio must be 16:9")
        if entry.get("image_size") != "2K":
            errors.append(f"{label}.image_size must be 2K")
        if entry.get("model") != "gpt-image-2":
            errors.append(f"{label}.model must be exact gpt-image-2")
        if entry.get("status") not in ALLOWED_STATUSES:
            errors.append(f"{label}.status is unsupported")
        prompt = entry.get("prompt")
        if not isinstance(prompt, str) or len(prompt.strip()) < 700:
            errors.append(f"{label}.prompt must be a structured prompt of at least 700 characters")
        elif any(term not in prompt for term in required_prompt_terms):
            errors.append(f"{label}.prompt is missing required semantic/reference/prohibition language")
        current = entry.get("current_reference")
        if not isinstance(current, str):
            errors.append(f"{label}.current_reference must be a path")
        else:
            try:
                root_path(current, must_exist=require_references)
            except (ValueError, FileNotFoundError) as exc:
                errors.append(f"{label}.current_reference: {exc}")
        masters = entry.get("master_references")
        if not isinstance(masters, list) or not all(isinstance(value, str) for value in masters):
            errors.append(f"{label}.master_references must be an array of paths")
        else:
            for value in masters:
                try:
                    root_path(value, must_exist=require_references)
                except (ValueError, FileNotFoundError) as exc:
                    errors.append(f"{label}.master_references: {exc}")
    return errors


def validate_command(require_references: bool = False) -> None:
    manifest = load_manifest()
    errors = validate_manifest(manifest, require_references=require_references)
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        raise SystemExit(1)
    print(f"Manifest valid: {len(manifest['entries'])} entries")


def parse_env_file(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    if not path.is_file():
        return values
    for raw_line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("export "):
            line = line[7:].strip()
        match = re.match(r"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$", line)
        if not match or match.group(1) not in ENV_KEYS:
            continue
        value = match.group(2).strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
            value = value[1:-1]
        if value and not re.fullmatch(r"<[^>]+>", value):
            values[match.group(1)] = value
    return values


def parse_env_value(path: Path, requested_key: str) -> str | None:
    """Resolve one env field without materializing unrelated credentials."""
    if requested_key not in ENV_KEYS or not path.is_file():
        return None
    with path.open("r", encoding="utf-8-sig", errors="replace") as handle:
        for raw_line in handle:
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("export "):
                line = line[7:].strip()
            match = re.match(r"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$", line)
            if not match or match.group(1) != requested_key:
                continue
            value = match.group(2).strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
                value = value[1:-1]
            if value and not re.fullmatch(r"<[^>]+>", value):
                return value
    return None


def _canonical_approved_host(value: str) -> str:
    raw = value.strip().lower().rstrip(".")
    if (
        not raw
        or "://" in raw
        or any(character in raw for character in "/?#@:")
        or ".." in raw
    ):
        raise RuntimeError(
            f"{UPLOAD_APPROVAL_HOST_ENV} must be one bare host name without scheme, port, path, query, or fragment."
        )
    try:
        host = raw.encode("idna").decode("ascii")
    except UnicodeError as exc:
        raise RuntimeError(f"{UPLOAD_APPROVAL_HOST_ENV} is not a valid host name.") from exc
    if not re.fullmatch(r"[a-z0-9.-]+", host):
        raise RuntimeError(f"{UPLOAD_APPROVAL_HOST_ENV} is not a valid host name.")
    return host


def require_upload_consent(api_root: str | None = None) -> str:
    approved_value = os.environ.get(UPLOAD_APPROVAL_HOST_ENV, "").strip()
    approved_scope = os.environ.get(UPLOAD_APPROVAL_SCOPE_ENV, "").strip()
    if not approved_value or not approved_scope:
        raise RuntimeError(
            "External image upload blocked: explicit current-process approval is required via "
            f"{UPLOAD_APPROVAL_HOST_ENV}=<approved-host> and "
            f"{UPLOAD_APPROVAL_SCOPE_ENV}={UPLOAD_APPROVAL_SCOPE}."
        )
    if approved_scope != UPLOAD_APPROVAL_SCOPE:
        raise RuntimeError(
            f"External image upload blocked: {UPLOAD_APPROVAL_SCOPE_ENV} must equal "
            f"'{UPLOAD_APPROVAL_SCOPE}'."
        )
    approved_host = _canonical_approved_host(approved_value)
    if api_root is not None:
        parsed = urlsplit(api_root)
        resolved_host = _canonical_approved_host(parsed.hostname or "")
        if resolved_host != approved_host:
            raise RuntimeError(
                f"External image upload blocked: resolved API host '{resolved_host}' is not approved by "
                f"{UPLOAD_APPROVAL_HOST_ENV}."
            )
    return approved_host


def normalize_api_root(base_url: str) -> str:
    raw = base_url.strip()
    parsed = urlsplit(raw)
    if (
        parsed.scheme.lower() not in {"http", "https"}
        or not parsed.hostname
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
    ):
        raise ValueError(
            "OPENAI_BASE_URL must be an HTTP(S) API root without credentials, query, or fragment"
        )
    try:
        port = parsed.port
    except ValueError as exc:
        raise ValueError("OPENAI_BASE_URL contains an invalid port") from exc
    expected_port = 443 if parsed.scheme.lower() == "https" else 80
    if port not in {None, expected_port}:
        raise ValueError("OPENAI_BASE_URL must use the default port for its HTTP(S) scheme")
    host = (parsed.hostname or "").lower().rstrip(".")
    if parsed.scheme.lower() != "https":
        raise ValueError("OPENAI_BASE_URL must use HTTPS before credentials or visual assets may be sent")
    path = parsed.path.rstrip("/")
    suffixes = ("/images/generations", "/images/edits")
    for suffix in suffixes:
        if path.lower().endswith(suffix):
            path = path[: -len(suffix)]
            break
    path = path.rstrip("/")
    if not path.lower().endswith("/v1"):
        path += "/v1"
    if not path.startswith("/"):
        path = "/" + path
    return urlunsplit((parsed.scheme.lower(), host, path, "", ""))


def _config_provider_base_url(config_path: Path) -> str | None:
    if not config_path.is_file():
        return None
    config = tomllib.loads(config_path.read_text(encoding="utf-8"))
    provider_name = str(config.get("model_provider") or "custom")
    providers = config.get("model_providers", {})
    provider = providers.get(provider_name) or providers.get("custom") or {}
    value = provider.get("base_url")
    return str(value).strip() if value else None


def resolve_runtime_environment() -> tuple[dict[str, str], str, str]:
    approved_host = require_upload_consent()
    env_candidates = [
        PPT_ROOT / ".env",
        PPT_SKILL_ROOT / ".env",
        Path.home() / ".ppt-master" / ".env",
    ]

    # Resolve and approve the endpoint before reading any credential source.
    base_url = os.environ.get("OPENAI_BASE_URL", "").strip()
    base_source = "current process" if base_url else ""
    if not base_url:
        for candidate in env_candidates:
            value = parse_env_value(candidate, "OPENAI_BASE_URL")
            if value:
                base_url = value
                base_source = f"PPT Master env ({candidate})"
                break
    config_path = Path.home() / ".codex" / "config.toml"
    if not base_url:
        base_url = _config_provider_base_url(config_path) or ""
        if base_url:
            base_source = "Codex global provider store"
    if not base_url:
        raise RuntimeError(
            "GPT-image endpoint is missing in process environment, PPT Master env, and Codex global storage."
        )
    api_root = normalize_api_root(base_url)
    resolved_host = require_upload_consent(api_root)
    if resolved_host != approved_host:
        raise RuntimeError("External image upload approval changed during endpoint resolution.")

    api_key = os.environ.get("OPENAI_API_KEY", "").strip()
    key_source = "current process" if api_key else ""
    if not api_key:
        for candidate in env_candidates:
            value = parse_env_value(candidate, "OPENAI_API_KEY")
            if value:
                api_key = value
                key_source = f"PPT Master env ({candidate})"
                break
    if not api_key:
        auth_path = Path.home() / ".codex" / "auth.json"
        if auth_path.is_file():
            auth = read_json(auth_path)
            value = auth.get("OPENAI_API_KEY")
            if value:
                api_key = str(value).strip()
                key_source = "Codex global auth store"
    if not api_key:
        raise RuntimeError(
            "GPT-image credential is missing in process environment, PPT Master env, and Codex global storage."
        )

    resolved: dict[str, str] = {}
    resolved.update(
        {
            "IMAGE_BACKEND": "openai",
            "OPENAI_API_KEY": api_key,
            "OPENAI_BASE_URL": api_root,
            "OPENAI_MODEL": "gpt-image-2",
            "OPENAI_OUTPUT_FORMAT": "png",
            "OPENAI_SIZE_PRESET": "gpt-image-2",
            "OPENAI_RESPONSE_FORMAT": "b64_json",
            "OPENAI_QUALITY": "high",
            "OPENAI_BACKGROUND": "auto",
        }
    )
    parsed = urlsplit(api_root)
    safe_endpoint = f"{parsed.scheme}://{parsed.hostname or '<unknown>'}/v1"
    source = f"endpoint: {base_source}; credential: {key_source}"
    return resolved, source, safe_endpoint


class _NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request: Any, file_pointer: Any, code: int, message: str,
                         headers: Any, new_url: str) -> Any:
        raise RuntimeError("Model discovery redirect blocked; approved-host consent is not transferable.")


def _read_model_discovery_response(response: Any) -> dict[str, Any]:
    status = response.getcode()
    if status != 200:
        raise RuntimeError(f"Model discovery failed: HTTP status {status!r}; expected 200")
    try:
        payload = json.loads(response.read().decode("utf-8"))
    except (AttributeError, TypeError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(
            f"Model discovery failed: invalid response ({type(exc).__name__})"
        ) from exc
    if not isinstance(payload, dict) or not isinstance(payload.get("data"), list):
        raise RuntimeError("Model discovery failed: response must contain a data array")
    return payload


def assert_gpt_image_2(runtime: dict[str, str]) -> None:
    api_root = normalize_api_root(runtime.get("OPENAI_BASE_URL", ""))
    require_upload_consent(api_root)
    request = urllib.request.Request(
        f"{api_root.rstrip('/')}/models",
        headers={"Authorization": f"Bearer {runtime['OPENAI_API_KEY']}"},
        method="GET",
    )
    try:
        opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}), _NoRedirectHandler()
        )
        with opener.open(request, timeout=60) as response:
            payload = _read_model_discovery_response(response)
    except RuntimeError:
        raise
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError) as exc:
        raise RuntimeError(f"Model discovery failed: {type(exc).__name__}") from exc
    model_ids = {
        str(item.get("id"))
        for item in payload.get("data", [])
        if isinstance(item, dict) and item.get("id")
    }
    if "gpt-image-2" not in model_ids:
        raise RuntimeError("Model discovery succeeded, but exact model gpt-image-2 is unavailable.")


def preflight() -> dict[str, str]:
    if not PPT_IMAGE_GEN.is_file():
        raise FileNotFoundError(f"Missing PPT Master image generator: {PPT_IMAGE_GEN}")
    if not PPT_PYTHON.is_file():
        raise FileNotFoundError(f"Missing PPT Master Python environment: {PPT_PYTHON}")
    if not SECURE_PPT_IMAGE_GEN.is_file():
        raise FileNotFoundError(f"Missing secured PPT Master launcher: {SECURE_PPT_IMAGE_GEN}")
    generator_sha256 = sha256(PPT_IMAGE_GEN)
    secure_launcher_sha256 = sha256(SECURE_PPT_IMAGE_GEN)
    runtime, source, safe_endpoint = resolve_runtime_environment()
    assert_gpt_image_2(runtime)
    runtime[PPT_GENERATOR_SHA_ENV] = generator_sha256
    runtime[SECURE_LAUNCHER_SHA_ENV] = secure_launcher_sha256
    print(f"Credential source: {source}")
    print(f"API endpoint: {safe_endpoint}")
    print("Model discovery: gpt-image-2 available")
    return runtime


def fit_image(path: Path, size: tuple[int, int], background: str = "#171c22") -> Image.Image:
    with Image.open(path) as opened:
        image = opened.convert("RGB")
    contained = ImageOps.contain(image, size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", size, background)
    x = (size[0] - contained.width) // 2
    y = (size[1] - contained.height) // 2
    canvas.paste(contained, (x, y))
    return canvas


def build_reference_board(entry: dict[str, Any]) -> Path:
    current = root_path(entry["current_reference"], must_exist=True)
    masters = [root_path(value, must_exist=True) for value in entry["master_references"]]
    paths = [current, *masters]
    output = safe_named_path(ROOT / "workflow" / "reference_boards", entry["filename"])
    output.parent.mkdir(parents=True, exist_ok=True)
    width, height, gutter = 2048, 1152, 12
    board = Image.new("RGB", (width, height), "#171c22")
    if len(paths) == 1:
        board.paste(fit_image(paths[0], (width, height)), (0, 0))
    else:
        left_width = 1240
        board.paste(fit_image(paths[0], (left_width, height)), (0, 0))
        right_x = left_width + gutter
        right_width = width - right_x
        if len(paths) == 2:
            board.paste(fit_image(paths[1], (right_width, height)), (right_x, 0))
        else:
            cell_height = (height - gutter) // 2
            board.paste(fit_image(paths[1], (right_width, cell_height)), (right_x, 0))
            board.paste(
                fit_image(paths[2], (right_width, height - cell_height - gutter)),
                (right_x, cell_height + gutter),
            )
    board.save(output, format="PNG", optimize=True)
    return output


def sanitize_log(text: str, runtime: dict[str, str]) -> str:
    sanitized = re.sub(
        r"(?i)https?%(?:25)*3a%(?:25)*2f%(?:25)*2f[^\s]+",
        "<redacted-url>",
        text,
    )
    sanitized = re.sub(r"(?i)https?://[^\s]+", "<redacted-url>", sanitized)
    api_key = runtime.get("OPENAI_API_KEY")
    sensitive_values = {
        runtime.get("OPENAI_BASE_URL"),
        api_key,
        quote(api_key, safe="") if api_key else None,
        quote_plus(api_key) if api_key else None,
    }
    for sensitive in sensitive_values:
        if sensitive:
            sanitized = sanitized.replace(sensitive, "<redacted>")
    api_root = runtime.get("OPENAI_BASE_URL", "")
    if api_root:
        host = urlsplit(api_root).hostname
        if host:
            sanitized = re.sub(re.escape(host), "<redacted-host>", sanitized, flags=re.IGNORECASE)
    sanitized = re.sub(
        r"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
        "Bearer <redacted>",
        sanitized,
    )
    sanitized = re.sub(
        r"(?i)\b(Authorization|Proxy-Authorization)\s*[:=]\s*[^\r\n]+",
        r"\1: <redacted>",
        sanitized,
    )
    sanitized = re.sub(
        r"(?i)([\"']?(?:api[_-]?key|access[_-]?token|token|signature|key|auth(?:orization)?|credential|secret)[\"']?\s*[:=]\s*[\"']?)([^\"'&,\s}\]]+)",
        r"\1<redacted>",
        sanitized,
    )
    sanitized = re.sub(
        r"\?[^\s]+",
        "?<redacted-query>",
        sanitized,
    )
    sanitized = re.sub(
        r"#[^\s]+",
        "#<redacted-fragment>",
        sanitized,
    )
    sanitized = re.sub(
        r"(?i)%(?:25)*(?:3f|23)[^\s]+",
        "%<redacted-encoded-url-component>",
        sanitized,
    )
    return sanitized


def select_entries(
    manifest: dict[str, Any], ids: set[str] | None, role: str | None, force: bool
) -> list[dict[str, Any]]:
    entries = manifest["entries"]
    selected = [
        entry
        for entry in entries
        if (not ids or entry["id"] in ids) and (not role or entry["page_role"] == role)
    ]
    if ids:
        missing = ids - {entry["id"] for entry in selected}
        if missing:
            raise ValueError(f"Unknown manifest ids: {', '.join(sorted(missing))}")
    if not force:
        selected = [entry for entry in selected if entry["status"] in RETRYABLE_STATUSES]
    return selected


def generate(ids: set[str] | None, role: str | None, force: bool) -> None:
    manifest = load_manifest()
    errors = validate_manifest(manifest, require_references=False)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    selected = select_entries(manifest, ids, role, force)
    if not selected:
        print("No retryable entries selected.")
        return
    runtime = preflight()
    child_env = build_child_environment(runtime)
    failures = 0
    for entry in selected:
        print(f"[{entry['id']}] generating {entry['page_name']}")
        try:
            board = build_reference_board(entry)
            reference_note = (
                "REFERENCE BOARD: the large left region is the current ClearVision screen and is the semantic/layout source. "
                "Any right-side region is an approved-candidate Master Reference for visual language only. "
                "LANGUAGE AND FACTS: keep all visible product navigation, labels, states, and commands in concise Simplified Chinese, "
                "matching the current product language. Never translate the product shell into English. Do not add plausible new "
                "actions, run modes, fields, filters, settings, recipes, telemetry, or technical facts. When exact copy is uncertain, "
                "prefer a visually quiet existing structure instead of inventing content. "
                "Return one single full-screen desktop UI, never a collage, board, before/after comparison, or presentation slide.\n\n"
            )
            prompt = reference_note + entry["prompt"]
            output_dir = ROOT / "candidates"
            output_dir.mkdir(parents=True, exist_ok=True)
            command = [
                str(PPT_PYTHON),
                str(SECURE_PPT_IMAGE_GEN),
                str(PPT_IMAGE_GEN),
                prompt,
                "--backend",
                "openai",
                "--model",
                "gpt-image-2",
                "--aspect_ratio",
                entry["aspect_ratio"],
                "--image_size",
                entry["image_size"],
                "--reference-image",
                str(board),
                "--output",
                str(output_dir),
                "--filename",
                Path(entry["filename"]).stem,
            ]
            result = subprocess.run(
                command,
                cwd=PPT_ROOT,
                env=child_env,
                text=True,
                capture_output=True,
                check=False,
            )
            combined = sanitize_log(result.stdout + "\n" + result.stderr, runtime)
            log_path = ROOT / "audit" / "logs" / f"{entry['id']}.log"
            log_path.parent.mkdir(parents=True, exist_ok=True)
            log_path.write_text(combined.strip() + "\n", encoding="utf-8")
            candidate = safe_named_path(output_dir, entry["filename"])
            if result.returncode != 0 or not candidate.is_file():
                excerpt = " ".join(combined.strip().split())[-1200:]
                raise RuntimeError(excerpt or f"PPT Master exited {result.returncode}")
            entry["status"] = "Generated"
            entry["output"] = rel(candidate)
            entry["reference_board"] = rel(board)
            entry["generated_at"] = utc_now()
            entry["sha256"] = sha256(candidate)
            with Image.open(candidate) as generated_image:
                entry["actual_dimensions"] = {
                    "width": generated_image.width,
                    "height": generated_image.height,
                }
            entry.pop("last_error", None)
            print(f"[{entry['id']}] generated {candidate.name}")
        except Exception as exc:  # Keep every failure retryable and recorded.
            failures += 1
            entry["status"] = "Failed"
            entry["last_error"] = sanitize_log(str(exc), runtime)[:2000]
            entry["failed_at"] = utc_now()
            print(f"[{entry['id']}] FAILED: {entry['last_error']}")
        atomic_write_json(MANIFEST_PATH, manifest)
    if failures:
        raise SystemExit(1)


def promote(ids: set[str]) -> None:
    if not ids:
        raise ValueError("promote requires --ids")
    manifest = load_manifest()
    errors = validate_manifest(manifest, require_references=False)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    known = {entry["id"]: entry for entry in manifest["entries"]}
    missing = ids - known.keys()
    if missing:
        raise ValueError(f"Unknown manifest ids: {', '.join(sorted(missing))}")
    for entry_id in sorted(ids):
        entry = known[entry_id]
        candidate = safe_named_path(ROOT / "candidates", entry["filename"], must_exist=True)
        target = safe_named_path(ROOT / "masters", entry["filename"])
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(candidate, target)
        entry["status"] = "Approved-Candidate"
        entry["master_path"] = rel(target)
        entry["selected_for_master_chain_at"] = utc_now()
        entry["approval_scope"] = "selected-for-chain-not-product-owner-approved"
        print(f"[{entry_id}] promoted to Master Reference candidate")
    atomic_write_json(MANIFEST_PATH, manifest)


def load_font(size: int) -> ImageFont.ImageFont:
    candidates = [
        Path(r"C:\Windows\Fonts\segoeui.ttf"),
        Path(r"C:\Windows\Fonts\arial.ttf"),
    ]
    for candidate in candidates:
        if candidate.is_file():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


def comparison_sheet(entry: dict[str, Any], candidate: Path, output: Path) -> None:
    width, height, label_height = 2400, 720, 42
    half = width // 2
    canvas = Image.new("RGB", (width, height), "#171c22")
    current = root_path(entry["current_reference"], must_exist=True)
    canvas.paste(fit_image(current, (half, height - label_height), "#222932"), (0, label_height))
    canvas.paste(
        fit_image(candidate, (half, height - label_height), "#222932"),
        (half, label_height),
    )
    draw = ImageDraw.Draw(canvas)
    font = load_font(20)
    draw.text((18, 10), f"CURRENT | {entry['id']} | {entry['page_name']}", fill="#f0f3f5", font=font)
    draw.text((half + 18, 10), "CANDIDATE | VISUAL REFERENCE, COPY/DATA NOT AUTHORITATIVE", fill="#f0f3f5", font=font)
    draw.line((half, 0, half, height), fill="#67717c", width=2)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=True)


def contact_sheet(items: list[tuple[str, Path]], output: Path, title: str) -> None:
    if not items:
        return
    columns = 3
    card_width, card_height = 650, 410
    title_height = 58
    rows = (len(items) + columns - 1) // columns
    canvas = Image.new("RGB", (columns * card_width, title_height + rows * card_height), "#171c22")
    draw = ImageDraw.Draw(canvas)
    title_font = load_font(26)
    label_font = load_font(18)
    draw.text((20, 14), title, fill="#f4f6f7", font=title_font)
    for index, (label, path) in enumerate(items):
        row, column = divmod(index, columns)
        x, y = column * card_width, title_height + row * card_height
        thumb = fit_image(path, (620, 349), "#222932")
        canvas.paste(thumb, (x + 15, y + 12))
        draw.text((x + 18, y + 368), label, fill="#d8dde1", font=label_font)
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=True)


def audit() -> None:
    manifest = load_manifest()
    errors = validate_manifest(manifest, require_references=False)
    if errors:
        raise ValueError("Manifest invalid:\n" + "\n".join(errors))
    comparison_items: list[tuple[str, Path]] = []
    master_items: list[tuple[str, Path]] = []
    rows: list[str] = []
    dimensions: set[str] = set()
    final_entries: list[dict[str, Any]] = []
    final_dir = ROOT / "final_candidate"
    final_dir.mkdir(parents=True, exist_ok=True)
    for entry in manifest["entries"]:
        candidate = safe_named_path(ROOT / "candidates", entry["filename"])
        if not candidate.is_file():
            continue
        comparison = safe_named_path(ROOT / "audit" / "comparisons", entry["filename"])
        comparison_sheet(entry, candidate, comparison)
        final_target = safe_named_path(final_dir, entry["filename"])
        shutil.copy2(candidate, final_target)
        comparison_items.append((f"{entry['id']} | {entry['page_name']}", candidate))
        with Image.open(candidate) as image:
            dimensions.add(f"{image.width}x{image.height}")
            actual_dimensions = {"width": image.width, "height": image.height}
        final_entries.append(
            {
                "id": entry["id"],
                "page_name": entry["page_name"],
                "file": final_target.name,
                "source_candidate": rel(candidate),
                "sha256": sha256(final_target),
                "actual_dimensions": actual_dimensions,
                "status": entry["status"],
                "text_policy": entry["text_policy"],
                "master_references": entry["master_references"],
            }
        )
        if entry.get("master_path"):
            master = root_path(entry["master_path"], must_exist=True)
            master_items.append((f"{entry['id']} | {entry['page_name']}", master))
        master_labels = ", ".join(Path(value).stem for value in entry["master_references"]) or "none"
        rows.append(
            f"| `{entry['id']}` | {entry['page_name']} | `{entry['current_reference']}` | "
            f"`{entry.get('output', rel(candidate))}` | {master_labels} | {entry['status']} |"
        )
    contact_sheet(comparison_items, ROOT / "audit" / "candidate_contact_sheet.png", "ClearVision UI Visual Master - Candidate Suite")
    contact_sheet(master_items, ROOT / "audit" / "master_contact_sheet.png", "ClearVision UI Visual Master - Master References")
    atomic_write_json(
        final_dir / "manifest.json",
        {
            "schema_version": "clearvision-final-candidate.v1",
            "generated_at": utc_now(),
            "approval_status": "awaiting-product-owner-visual-review",
            "copy_and_data_policy": "Generated copy, data, device facts, and imagery are not product authority.",
            "count": len(final_entries),
            "entries": final_entries,
        },
    )
    status_counts = {
        status: sum(1 for entry in manifest["entries"] if entry["status"] == status)
        for status in sorted(ALLOWED_STATUSES)
    }
    master_ids = [entry["id"] for entry in manifest["entries"] if entry.get("master_path")]
    audit_md = f"""# ClearVision Visual Audit Index

生成图片只作为视觉设计参考。当前产品文案、字段语义、业务数据、工作流名称、设备事实与后端 authority 仍以运行中的 ClearVision 和现有 contracts 为准。

## 交付状态

- 当前截图基线：`48` 张；R2 final matrix `42/42`，F07 device/settings `5/5`，当前提交项目空态 `1/1`。
- 候选稿：`{len(comparison_items)}` 张；Master Reference：`{len(master_ids)}` 张；本轮最终失败：`{status_counts.get('Failed', 0)}`。
- Manifest 状态：`Approved-Candidate={status_counts.get('Approved-Candidate', 0)}`，`Generated={status_counts.get('Generated', 0)}`。
- 请求规格：`2K / 16:9`；兼容端点实际统一返回：`{', '.join(sorted(dimensions))}`。未对原始候选冒充放大。
- 每张图均有 current reference、reference board、SHA-256、生成状态和 current-vs-candidate 对照。
- `final_candidate/manifest.json` 可独立校验最终候选文件、哈希、实际尺寸与 Master 依赖。
- `Approved-Candidate` 仅表示可供链式风格约束，不表示产品负责人已批准。

## 建议先审

| 顺序 | 页面 | 审计重点 |
| --- | --- | --- |
| 1 | `01_flow_editor` | 整个产品的壳层、Canvas/Inspector/Preview 比例、密度与基础控件语言。 |
| 2 | `03_ai_workspace` | AI 是否足够高级，同时仍是受门禁、验证和交接约束的工程工作台。 |
| 3 | `04_results_investigation` | OK/NG、结果列表、证据图、判定依据与过期状态是否清楚可信。 |
| 4 | `06_camera_settings` | Settings 家族的导航、表单、设备状态、预览与诊断关系。 |
| 5 | `09_projects_empty` | 空状态是否克制、保留上下文且没有发明业务能力。 |
| 6 | `16_run_ng_modal` | Modal 的信息层级、背景失焦、NG 强度与返回路径。 |

## Master Reference Chain

`01 Flow -> 03 AI -> 04 Results -> 06 Camera/Settings`

- Flow 定义主壳、精密工作面、Panel、Border、Spacing 与动作层级。
- AI 继承 Flow 的产品框架，定义方案、差异、门禁、预演、交接和诊断密度。
- Results 同时继承 Flow/AI，定义数据、证据、OK/NG 和调查型 split view。
- Camera 同时继承 Flow/Results，定义 Settings、设备配置、预览和诊断；PLC/TCP/Station/AI Model 再继承 Camera。

## 变化最大

- `03_ai_workspace`：从稀疏长页变成完整的 AI 工程工作台，语义仍围绕候选、校验和交接。
- `07_overview`：从低利用率页面变成连续工作、最近结果与权限上下文的高效入口。
- `05_station_management`：从稀疏站点列表变成可扫描的 fleet list/detail 工作面。
- `06/12/13/14/15/18` Settings 家族：统一导航、label/value/unit 对齐、状态、保存边界和诊断区域。
- `16_run_ng_modal`：把结果、证据和节点级输出集中到一个明确的调查边界。

## 已统一规律

- Graphite 应用框架配冷静浅色/深色工作面，不做单色 SaaS Dashboard。
- 蓝色只承担清晰命令；朱红用于品牌/选择；绿/红/琥珀分别表达 OK/NG/Warning，并配合图标和文字。
- 高密度但不拥挤：稳定分栏、1 px 边界、3-6 px 圆角、4/8 px 节奏、紧凑行高。
- Canvas、图像证据、ROI 和运行结果在相关页面获得最大有效面积。
- Empty/Error/NG/Recovery 状态保留原页面几何，不用巨型插画或整页警报取代上下文。

## 仍待人工指定

- 所有生成文案、名称、数值、地址、时间、图像、模型名和诊断码必须在复刻时回到真实 contracts；不得逐字抄图。
- 各页面采用浅色工作面还是深色工作面需要在人工审计后确定主题矩阵；本套同时展示两种受控变体。
- 个别候选为了表达信息结构使用了更丰富的占位数据，尤其 Overview、Station、Inspection、AI failure；只审版式，不审这些数据事实。
- `16_run_ng_modal` 只审 modal；背景导航与示例节点不是业务基线。
- 第一版 Flow 因英文壳和虚构动作被淘汰，保存在 `candidates/iterations/01_flow_editor_v1.png`；第一版项目空态因虚构一级模块被淘汰，保存在 `candidates/iterations/09_projects_empty_v1_semantic_drift.png`。
- 尚未执行真实 WebView2、Windows 125% DPI、真实 endpoints、相机/PLC/Station 硬件或 release publish；这些证据不能由本图集替代。

## 对应表

| ID | Page | Current | Candidate | Master references | Status |
| --- | --- | --- | --- | --- | --- |
""" + "\n".join(rows) + "\n"
    (ROOT / "audit" / "audit_index.md").write_text(audit_md, encoding="utf-8")
    print(f"Audit comparisons: {len(comparison_items)}")
    print(f"Audit index: {ROOT / 'audit' / 'audit_index.md'}")


def parse_ids(value: str | None) -> set[str] | None:
    if value is None:
        return None
    return {item.strip() for item in value.split(",") if item.strip()}


def main() -> None:
    parser = argparse.ArgumentParser(description="ClearVision UI Visual Master workflow")
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("baseline", help="Copy current screenshot evidence and write its manifest")
    validate_parser = subparsers.add_parser("validate", help="Validate the image manifest")
    validate_parser.add_argument("--require-references", action="store_true")
    subparsers.add_parser("preflight", help="Resolve configuration and require gpt-image-2 in /models")
    generate_parser = subparsers.add_parser("generate", help="Generate retryable manifest entries")
    generate_parser.add_argument("--ids", help="Comma-separated stable ids")
    generate_parser.add_argument("--role", choices=sorted(ALLOWED_ROLES))
    generate_parser.add_argument("--force", action="store_true")
    promote_parser = subparsers.add_parser("promote", help="Promote inspected candidates into the Master chain")
    promote_parser.add_argument("--ids", required=True, help="Comma-separated stable ids")
    subparsers.add_parser("audit", help="Build current-vs-candidate comparisons and contact sheets")
    args = parser.parse_args()

    try:
        if args.command == "baseline":
            copy_baseline()
        elif args.command == "validate":
            validate_command(args.require_references)
        elif args.command == "preflight":
            preflight()
        elif args.command == "generate":
            generate(parse_ids(args.ids), args.role, args.force)
        elif args.command == "promote":
            promote(parse_ids(args.ids) or set())
        elif args.command == "audit":
            audit()
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc


if __name__ == "__main__":
    main()
