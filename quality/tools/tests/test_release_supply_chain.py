from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import generate_release_supply_chain as supply  # noqa: E402


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def write_nuspec(path: Path, package: str, version: str, license_xml: str = "") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{package}</id><version>{version}</version><authors>fixture</authors>
    <description>fixture</description>{license_xml}
  </metadata>
</package>
""",
        encoding="utf-8",
    )


class ReleaseSupplyChainTests(unittest.TestCase):
    def test_generation_is_derived_from_final_zip_and_nupkg(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            portable = root / "ClearVision-Studio-test.zip"
            nupkg = root / "ClearVision.OperatorLibrary.1.2.3.nupkg"
            output = root / "output"
            nuget = root / "nuget"
            target_name = ".NETCoreApp,Version=v8.0/win-x64"
            deps = {
                "runtimeTarget": {"name": target_name},
                "targets": {
                    target_name: {
                        "Fixture.Allowed/1.0.0": {"runtime": {"lib/net8.0/Fixture.Allowed.dll": {}}},
                    }
                },
                "libraries": {
                    "Fixture.Allowed/1.0.0": {"type": "package"},
                    "Fixture.NotPackaged/9.9.9": {"type": "package"},
                },
            }
            with zipfile.ZipFile(portable, "w") as archive:
                archive.writestr("ClearVision.Product.Desktop.deps.json", json.dumps(deps))
                archive.writestr("Fixture.Allowed.dll", b"fixture")
                archive.writestr("Launch-ClearVision.cmd", "@echo off\r\n")
            nuspec = """<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
<id>ClearVision.OperatorLibrary</id><version>1.2.3</version><authors>fixture</authors>
<description>fixture</description><dependencies><group targetFramework="net8.0">
<dependency id="Fixture.Allowed" version="[1.0.0]" />
<dependency id="S7NetPlus" version="[0.20.0]" />
</group></dependencies></metadata></package>"""
            with zipfile.ZipFile(nupkg, "w") as archive:
                archive.writestr("ClearVision.OperatorLibrary.nuspec", nuspec)
                archive.writestr("lib/net8.0/ClearVision.OperatorLibrary.dll", b"fixture")

            write_nuspec(
                nuget / "fixture.allowed/1.0.0/fixture.allowed.nuspec",
                "Fixture.Allowed",
                "1.0.0",
                '<license type="expression">MIT</license>',
            )
            write_nuspec(
                nuget / "s7netplus/0.20.0/s7netplus.nuspec", "S7NetPlus", "0.20.0"
            )
            identity = root / "source-identity.json"
            write_json(
                identity,
                {
                    "schemaVersion": "clearvision.package-source-identity/v1",
                    "gitSha": "1" * 40,
                    "repositoryDirty": False,
                    "sdkVersion": "8.0.fixture",
                    "runtimeInventory": ["Microsoft.NETCore.App 8.0.fixture"],
                    "runtimeIdentifier": "win-x64",
                    "profile": "field-self-contained",
                },
            )
            policy = root / "policy.json"
            write_json(
                policy,
                {
                    "allowedLicenses": ["MIT"],
                    "deniedLicenses": ["GPL-3.0-only"],
                    "approvedExceptions": [],
                    "pendingExceptions": [
                        {
                            "scope": "license",
                            "package": "S7NetPlus",
                            "version": "0.20.0",
                            "status": "pending-human-review-not-approved",
                        }
                    ],
                    "vulnerabilityPolicy": {
                        "maximumDataAgeHours": 24,
                        "blockedSeverities": ["low", "moderate", "high", "critical"],
                    },
                },
            )
            vulnerability = root / "vulnerability-scan.json"
            write_json(
                vulnerability,
                {
                    "status": "available",
                    "checkedAtUtc": datetime.now(timezone.utc).isoformat(),
                    "dataAsOfUtc": datetime.now(timezone.utc).isoformat(),
                    "vulnerabilities": [],
                },
            )

            completed = subprocess.run(
                [
                    sys.executable,
                    str(TOOLS_DIR / "generate_release_supply_chain.py"),
                    "--portable-zip",
                    str(portable),
                    "--nupkg",
                    str(nupkg),
                    "--output-dir",
                    str(output),
                    "--identity-input",
                    str(identity),
                    "--policy",
                    str(policy),
                    "--nuget-packages-root",
                    str(nuget),
                    "--vulnerability-report",
                    str(vulnerability),
                ],
                text=True,
                capture_output=True,
                check=False,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)

            report = supply.read_json(output / "dependency-report.json")
            sbom = supply.read_json(output / "SBOM.spdx.json")
            manifest = supply.read_json(output / "identity-manifest.json")
            validation = supply.read_json(output / "validation-summary.json")
            components = {(row["name"], row["version"]) for row in report["components"]}
            self.assertEqual(
                {("Fixture.Allowed", "1.0.0"), ("S7NetPlus", "0.20.0")}, components
            )
            self.assertNotIn(("Fixture.NotPackaged", "9.9.9"), components)
            self.assertEqual("clearvision.release-identity/v1", manifest["schemaVersion"])
            self.assertEqual(
                "clearvision.package-source-identity/v1", manifest["sourceIdentitySchemaVersion"]
            )
            self.assertTrue(validation["generationPassed"])
            self.assertTrue(validation["artifactConsistencyPassed"])
            self.assertFalse(validation["releaseEligible"])
            self.assertIn(
                "LICENSE_NOASSERTION", {row["code"] for row in validation["blockingFindings"]}
            )
            s7 = next(row for row in report["components"] if row["name"] == "S7NetPlus")
            self.assertEqual("NOASSERTION", s7["license"])
            self.assertEqual("blocked-noassertion", s7["policyDisposition"])
            self.assertTrue(all("checksums" not in row for row in sbom["packages"]))
            self.assertEqual("1" * 40, report["evidenceBinding"]["gitSha"])
            self.assertIn("portablePackageSha256", report["evidenceBinding"]["inputChecksums"])
            notice = (output / "THIRD-PARTY-NOTICES.txt").read_text(encoding="utf-8")
            self.assertIn("Component: Fixture.Allowed@1.0.0", notice)
            self.assertIn("Component: S7NetPlus@0.20.0", notice)
            self.assertNotIn("Fixture.NotPackaged", notice)
            self.assertIn("vulnerability-scan.json", supply.checksum_lines(output / "SHA256SUMS"))

    def test_stale_vulnerability_data_blocks(self) -> None:
        vulnerability = {
            "status": "available",
            "dataAsOfUtc": (datetime.now(timezone.utc) - timedelta(hours=25)).isoformat(),
            "vulnerabilities": [],
        }
        blocks, _ = supply.evaluate_policy(
            [],
            {
                "allowedLicenses": [],
                "deniedLicenses": [],
                "approvedExceptions": [],
                "vulnerabilityPolicy": {"maximumDataAgeHours": 24, "blockedSeverities": []},
            },
            vulnerability,
            datetime.now(timezone.utc),
        )
        self.assertIn("VULNERABILITY_DATA_STALE", {row["code"] for row in blocks})

    def test_vulnerability_exception_requires_exact_scope_package_version_and_advisory(self) -> None:
        now = datetime.now(timezone.utc)
        policy = {
            "allowedLicenses": [],
            "deniedLicenses": [],
            "approvedExceptions": [
                {
                    "scope": "vulnerability",
                    "package": "Fixture.Package",
                    "version": "1.0.0",
                    "advisoryUrl": "https://example.invalid/CVE-1",
                    "reason": "fixture",
                    "owner": "fixture-owner",
                    "approvalReference": "fixture-approval",
                    "expiry": (now + timedelta(days=1)).isoformat(),
                }
            ],
            "vulnerabilityPolicy": {"maximumDataAgeHours": 24, "blockedSeverities": ["high"]},
        }
        vulnerability = {
            "status": "available",
            "dataAsOfUtc": now.isoformat(),
            "vulnerabilities": [
                {
                    "package": "Fixture.Package",
                    "version": "1.0.0",
                    "severity": "high",
                    "advisoryUrl": "https://example.invalid/CVE-1",
                },
                {
                    "package": "Fixture.Package",
                    "version": "1.0.1",
                    "severity": "high",
                    "advisoryUrl": "https://example.invalid/CVE-1",
                },
            ],
        }
        blocks, _ = supply.evaluate_policy([], policy, vulnerability, now)
        self.assertEqual(1, sum(row["code"] == "VULNERABILITY_BLOCKED" for row in blocks))
        self.assertEqual("approved-exception", vulnerability["vulnerabilities"][0]["policyDisposition"])
        self.assertEqual("blocked", vulnerability["vulnerabilities"][1]["policyDisposition"])


if __name__ == "__main__":
    unittest.main()
