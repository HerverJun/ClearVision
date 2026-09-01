from __future__ import annotations

import json
import sys
import unittest
import zipfile
from datetime import datetime, timezone
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import collect_license_provenance as licenses  # noqa: E402
import collect_vulnerability_provenance as vulnerabilities  # noqa: E402


FIXTURES = Path(__file__).resolve().parent / "fixtures" / "vulnerability-provenance"
IDENTITIES = [{"id": "Fixture.Package", "version": "1.0.0"}]
NOW = datetime(2026, 9, 1, 1, 0, tzinfo=timezone.utc)


class LicenseProvenanceTests(unittest.TestCase):
    def test_authoritative_license_identification(self) -> None:
        self.assertEqual("MIT", licenses.identify_license("The MIT License (MIT)\nPermission is hereby granted, free of charge"))
        self.assertEqual(
            "RPL-1.5 OR LicenseRef-LuckyPenny-Commercial",
            licenses.identify_license("Reciprocal Public License 1.5 and a License Agreement"),
        )
        self.assertEqual(
            "LicenseRef-Microsoft-Data-SqlClient-SNI",
            licenses.identify_license("MICROSOFT SOFTWARE LICENSE TERMS\nMICROSOFT.DATA.SQLCLIENT.SNI LIBRARY"),
        )
        self.assertEqual("NOASSERTION", licenses.identify_license("not authoritative license text"))

    def test_nupkg_parser_binds_nuspec_and_license_hash(self) -> None:
        from io import BytesIO

        package = BytesIO()
        nuspec = b"""<package><metadata><id>Fixture.Package</id><version>1.0.0</version><license type=\"file\">LICENSE.txt</license><repository type=\"git\" url=\"https://github.com/example/repo\" commit=\"abc\" /></metadata></package>"""
        license_text = b"The MIT License (MIT)\nPermission is hereby granted, free of charge"
        with zipfile.ZipFile(package, "w") as archive:
            archive.writestr("Fixture.Package.nuspec", nuspec)
            archive.writestr("LICENSE.txt", license_text)
        parsed = licenses.parse_package_bytes(package.getvalue(), "Fixture.Package", "1.0.0")
        self.assertEqual(licenses.sha256_bytes(nuspec), parsed["nuspecSha256"])
        self.assertEqual(licenses.sha256_bytes(license_text), parsed["license"]["fileSha256"])
        self.assertEqual("abc", parsed["repository"]["commit"])


class VulnerabilityProvenanceTests(unittest.TestCase):
    def load(self, name: str) -> list[dict[str, object]]:
        value = json.loads((FIXTURES / f"{name}.json").read_text(encoding="utf-8"))
        self.assertTrue(value["fixture"])
        return value["sources"]

    def test_clean_requires_two_fresh_full_sources(self) -> None:
        report = vulnerabilities.build_report(IDENTITIES, self.load("clean"), NOW, 24)
        self.assertEqual("available", report["scanStatus"])
        self.assertEqual(0, report["vulnerabilityCount"])
        self.assertTrue(report["zeroFindingClaimSupported"])

    def test_vulnerable_merges_aliases_and_sources(self) -> None:
        report = vulnerabilities.build_report(IDENTITIES, self.load("vulnerable"), NOW, 24)
        self.assertEqual("available", report["scanStatus"])
        self.assertEqual(1, report["vulnerabilityCount"])
        self.assertEqual(["NuGet fixture", "OSV fixture"], report["vulnerabilities"][0]["sources"])

    def test_unavailable_never_becomes_zero(self) -> None:
        report = vulnerabilities.build_report(IDENTITIES, self.load("unavailable"), NOW, 24)
        self.assertEqual("unavailable", report["scanStatus"])
        self.assertIsNone(report["vulnerabilityCount"])
        self.assertFalse(report["zeroFindingClaimSupported"])

    def test_stale_never_becomes_zero(self) -> None:
        report = vulnerabilities.build_report(IDENTITIES, self.load("stale"), NOW, 24)
        self.assertEqual("stale", report["scanStatus"])
        self.assertIsNone(report["vulnerabilityCount"])

    def test_source_conflict_preserves_finding(self) -> None:
        report = vulnerabilities.build_report(IDENTITIES, self.load("conflict"), NOW, 24)
        self.assertEqual("available", report["scanStatus"])
        self.assertEqual(1, report["vulnerabilityCount"])
        self.assertEqual(1, len(report["sourceConflicts"]))

    def test_one_empty_source_cannot_claim_clean(self) -> None:
        sources = self.load("clean")[:1]
        report = vulnerabilities.build_report(IDENTITIES, sources, NOW, 24)
        self.assertEqual("unavailable", report["scanStatus"])
        self.assertIsNone(report["vulnerabilityCount"])


if __name__ == "__main__":
    unittest.main()
