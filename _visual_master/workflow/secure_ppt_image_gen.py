#!/usr/bin/env python3
"""Run PPT Master's image generator with an approved-host-only transport."""

from __future__ import annotations

import os
import re
import runpy
import sys
import hashlib
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

import requests


APPROVED_HOST_ENV = "CLEARVISION_VISUAL_UPLOAD_APPROVED_HOST"
APPROVED_SCOPE_ENV = "CLEARVISION_VISUAL_UPLOAD_APPROVED_SCOPE"
APPROVED_SCOPE = "models-and-clearvision-composite-reference-boards"
EXPECTED_GENERATOR_ENV = "CLEARVISION_PPT_IMAGE_GEN_PATH"
EXPECTED_GENERATOR_SHA_ENV = "CLEARVISION_PPT_IMAGE_GEN_SHA256"
EXPECTED_LAUNCHER_SHA_ENV = "CLEARVISION_SECURE_PPT_IMAGE_GEN_SHA256"
APPROVED_GENERATOR = (
    Path.home()
    / "Desktop"
    / "ppt"
    / ".agents"
    / "skills"
    / "ppt-master"
    / "scripts"
    / "image_gen.py"
).resolve()


def canonical_host(value: str) -> str:
    raw = value.strip().lower().rstrip(".")
    if (
        not raw
        or "://" in raw
        or any(character in raw for character in "/?#@:")
        or ".." in raw
    ):
        raise RuntimeError(f"{APPROVED_HOST_ENV} must contain one bare host name")
    try:
        host = raw.encode("idna").decode("ascii")
    except UnicodeError as exc:
        raise RuntimeError(f"{APPROVED_HOST_ENV} is not a valid host name") from exc
    if not re.fullmatch(r"[a-z0-9.-]+", host):
        raise RuntimeError(f"{APPROVED_HOST_ENV} is not a valid host name")
    return host


def validate_outbound_url(url: str, approved_host: str) -> None:
    parsed = urlsplit(url)
    if parsed.scheme.lower() != "https":
        raise RuntimeError("Blocked non-HTTPS image API request")
    if parsed.username is not None or parsed.password is not None:
        raise RuntimeError("Blocked image API URL containing credentials")
    if parsed.query or parsed.fragment:
        raise RuntimeError("Blocked image API URL containing query or fragment")
    host = canonical_host(parsed.hostname or "")
    if host != approved_host:
        raise RuntimeError("Blocked image API request to an unapproved host")
    try:
        port = parsed.port
    except ValueError as exc:
        raise RuntimeError("Blocked image API URL with an invalid port") from exc
    if port not in {None, 443}:
        raise RuntimeError("Blocked image API request using a non-default HTTPS port")


def has_items(value: Any) -> bool:
    if value is None:
        return False
    if isinstance(value, (str, bytes)):
        return len(value) > 0
    try:
        iterator = iter(value)
    except TypeError:
        return True
    try:
        next(iterator)
    except StopIteration:
        return False
    return True


def harden_request_context(session: requests.sessions.Session, kwargs: dict[str, Any]) -> None:
    if has_items(kwargs.get("params")):
        raise RuntimeError("Blocked image API request containing query parameters")
    if has_items(kwargs.get("proxies")):
        raise RuntimeError("Blocked image API request using an explicit proxy")
    if "verify" in kwargs and kwargs["verify"] is not True:
        raise RuntimeError("Blocked image API request without standard TLS verification")
    if "auth" in kwargs and kwargs["auth"] is not None:
        raise RuntimeError("Blocked image API request containing implicit authentication")
    if "cert" in kwargs and kwargs["cert"] is not None:
        raise RuntimeError("Blocked image API request containing a client certificate")
    if has_items(getattr(session, "params", None)):
        raise RuntimeError("Blocked image API session containing query parameters")
    if has_items(getattr(session, "proxies", None)):
        raise RuntimeError("Blocked image API session using a proxy")
    if getattr(session, "verify", True) is not True:
        raise RuntimeError("Blocked image API session without standard TLS verification")
    if getattr(session, "auth", None) is not None:
        raise RuntimeError("Blocked image API session containing implicit authentication")
    if getattr(session, "cert", None) is not None:
        raise RuntimeError("Blocked image API session containing a client certificate")
    session.trust_env = False
    session.params = {}
    session.proxies = {}
    kwargs["params"] = {}
    kwargs["proxies"] = {}
    kwargs["verify"] = True
    kwargs["allow_redirects"] = False


def install_requests_guard(approved_host: str) -> None:
    original_request = requests.sessions.Session.request

    def guarded_request(
        session: requests.sessions.Session, method: str, url: str, **kwargs: Any
    ) -> requests.Response:
        validate_outbound_url(str(url), approved_host)
        harden_request_context(session, kwargs)
        response = original_request(session, method, url, **kwargs)
        if 300 <= response.status_code < 400:
            response.close()
            raise RuntimeError(
                "Image API redirect blocked; approved-host consent is not transferable"
            )
        return response

    requests.sessions.Session.request = guarded_request


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def required_flag_value(arguments: list[str], flag: str) -> str:
    if any(argument.startswith(f"{flag}=") for argument in arguments):
        raise RuntimeError(f"Secured launcher requires a separate value for {flag}")
    positions = [index for index, argument in enumerate(arguments) if argument == flag]
    if len(positions) != 1 or positions[0] + 1 >= len(arguments):
        raise RuntimeError(f"Secured launcher requires exactly one {flag}")
    return arguments[positions[0] + 1]


def validate_generator_contract(arguments: list[str]) -> None:
    if required_flag_value(arguments, "--backend") != "openai":
        raise RuntimeError("Secured launcher only permits the OpenAI backend")
    if required_flag_value(arguments, "--model") != "gpt-image-2":
        raise RuntimeError("Secured launcher only permits exact model gpt-image-2")
    expected_environment = {
        "IMAGE_BACKEND": "openai",
        "OPENAI_MODEL": "gpt-image-2",
        "OPENAI_OUTPUT_FORMAT": "png",
        "OPENAI_RESPONSE_FORMAT": "b64_json",
    }
    for key, expected in expected_environment.items():
        if os.environ.get(key, "").strip() != expected:
            raise RuntimeError(f"Secured launcher requires {key}={expected}")


def main() -> None:
    if len(sys.argv) < 2:
        raise RuntimeError("Secured launcher requires the PPT Master generator path")
    if os.environ.get(APPROVED_SCOPE_ENV, "").strip() != APPROVED_SCOPE:
        raise RuntimeError("External image upload scope is not approved")
    approved_host = canonical_host(os.environ.get(APPROVED_HOST_ENV, ""))
    api_root = os.environ.get("OPENAI_BASE_URL", "")
    validate_outbound_url(api_root, approved_host)
    if not urlsplit(api_root).path.rstrip("/").lower().endswith("/v1"):
        raise RuntimeError("Secured launcher requires an OpenAI-compatible /v1 API root")

    expected_launcher_sha256 = os.environ.get(
        EXPECTED_LAUNCHER_SHA_ENV, ""
    ).strip().lower()
    if not re.fullmatch(r"[0-9a-f]{64}", expected_launcher_sha256):
        raise RuntimeError("Secured launcher SHA-256 is missing or invalid")
    if sha256(Path(__file__).resolve()) != expected_launcher_sha256:
        raise RuntimeError("Secured launcher changed after the active preflight")

    target = Path(sys.argv[1]).resolve()
    expected_raw = os.environ.get(EXPECTED_GENERATOR_ENV, "").strip()
    if target != APPROVED_GENERATOR:
        raise RuntimeError("PPT Master generator is outside the approved workflow path")
    if not expected_raw or target != Path(expected_raw).resolve():
        raise RuntimeError("PPT Master generator path is not the approved workflow target")
    if not target.is_file():
        raise FileNotFoundError(f"Missing PPT Master image generator: {target}")
    expected_sha256 = os.environ.get(EXPECTED_GENERATOR_SHA_ENV, "").strip().lower()
    if not re.fullmatch(r"[0-9a-f]{64}", expected_sha256):
        raise RuntimeError("PPT Master generator SHA-256 is missing or invalid")
    if sha256(target) != expected_sha256:
        raise RuntimeError("PPT Master generator changed after the active preflight")
    validate_generator_contract(sys.argv[2:])

    install_requests_guard(approved_host)
    sys.argv = [str(target), *sys.argv[2:]]
    sys.path.insert(0, str(target.parent))
    runpy.run_path(str(target), run_name="__main__")


if __name__ == "__main__":
    main()
