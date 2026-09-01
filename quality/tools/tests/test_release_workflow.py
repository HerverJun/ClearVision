from __future__ import annotations

import unittest
from pathlib import Path

import yaml


REPO_ROOT = Path(__file__).resolve().parents[3]
WORKFLOW_PATH = REPO_ROOT / ".github/workflows/ci.yml"


class ReleaseWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.raw = WORKFLOW_PATH.read_text(encoding="utf-8-sig")
        cls.workflow = yaml.safe_load(cls.raw)
        cls.jobs = cls.workflow["jobs"]

    def test_yaml_parses_and_release_is_downstream_of_successful_bundle_job(self) -> None:
        release_build = self.jobs["release-build"]
        create_release = self.jobs["create-release"]
        self.assertEqual("build-and-test", release_build["needs"])
        self.assertEqual("release-build", create_release["needs"])
        self.assertIn("startsWith(github.ref, 'refs/tags/v')", create_release["if"])
        self.assertEqual("${{ steps.version.outputs.VERSION }}", release_build["outputs"]["version"])

    def test_tag_bundle_uses_only_canonical_formal_profile_and_policy_enforcement(self) -> None:
        release_steps = self.jobs["release-build"]["steps"]
        governed = next(step for step in release_steps if step.get("name") == "Build Governed Tag Release Bundle")
        command = governed["run"]
        self.assertIn("./scripts/package-portable-deployment.ps1", command)
        for required in (
            "-Application Studio",
            "-RuntimeIdentifier win-x64",
            "-Profile field-self-contained",
            '-SourceRevisionId "${{ github.sha }}"',
            "-NoRestore",
            "-RunOperatorSmoke",
            "-AttemptVulnerabilityScan",
            "-EnforceReleasePolicy",
        ):
            self.assertIn(required, command)
        self.assertNotRegex(command, r"(?i)\bdotnet\s+publish\b|\bCompress-Archive\b")
        self.assertTrue(all("package-portable-deployment.ps1" in path.read_text(encoding="utf-8-sig") for path in (
            REPO_ROOT / "scripts/package-studio-station-full.ps1",
            REPO_ROOT / "scripts/package-studio-station-lite.ps1",
        )))

    def test_tag_upload_contains_all_governed_outputs_from_one_release_root(self) -> None:
        release_steps = self.jobs["release-build"]["steps"]
        upload = next(step for step in release_steps if step.get("name") == "Upload Governed Tag Release Bundle")
        paths = [line.strip() for line in upload["with"]["path"].splitlines() if line.strip()]
        required_names = {
            "*.zip",
            "*.nupkg",
            "SHA256SUMS",
            "SBOM.spdx.json",
            "THIRD-PARTY-NOTICES.txt",
            "dependency-report.json",
            "dependency-report.md",
            "identity-manifest.json",
            "validation-summary.json",
            "vulnerability-scan.json",
            "license-provenance.json",
            "nuget-audit-source.json",
            "package-result.json",
        }
        self.assertEqual(required_names, {Path(value).name for value in paths})
        self.assertTrue(all("${{ env.PUBLISH_DIR }}/release/" in value for value in paths))
        self.assertEqual(
            "ClearVision-Release-${{ steps.version.outputs.VERSION }}", upload["with"]["name"]
        )

    def test_release_asset_step_uses_downloaded_governed_artifact(self) -> None:
        steps = self.jobs["create-release"]["steps"]
        download = next(step for step in steps if step.get("name") == "Download Build Artifact")
        publish = next(step for step in steps if step.get("name") == "Create Release")
        self.assertEqual(
            "ClearVision-Release-${{ needs.release-build.outputs.version }}", download["with"]["name"]
        )
        asset_names = {Path(line.strip()).name for line in publish["with"]["files"].splitlines() if line.strip()}
        self.assertTrue(
            {
                "*.zip",
                "*.nupkg",
                "SHA256SUMS",
                "SBOM.spdx.json",
                "THIRD-PARTY-NOTICES.txt",
                "dependency-report.json",
                "dependency-report.md",
                "identity-manifest.json",
                "validation-summary.json",
                "vulnerability-scan.json",
                "license-provenance.json",
                "nuget-audit-source.json",
            }.issubset(asset_names)
        )

    def test_main_branch_artifact_is_explicitly_diagnostic(self) -> None:
        release_steps = self.jobs["release-build"]["steps"]
        diagnostic = next(step for step in release_steps if step.get("name") == "Build Main Diagnostic Artifact")
        upload = next(step for step in release_steps if step.get("name") == "Upload Main Diagnostic Artifact")
        self.assertEqual("github.ref == 'refs/heads/main'", diagnostic["if"])
        self.assertIn("DIAGNOSTIC-ARTIFACT-NOT-FOR-SITE-DEPLOYMENT", diagnostic["run"])
        self.assertIn("ClearVision-Diagnostic-RawBuild", upload["with"]["name"])

    def test_locked_restore_and_sdk_policy_validator_are_retained(self) -> None:
        release_steps = self.jobs["release-build"]["steps"]
        joined = "\n".join(str(step.get("run", "")) for step in release_steps)
        self.assertIn("validate-dotnet-sdk-policy.ps1", joined)
        self.assertIn("--locked-mode", joined)


if __name__ == "__main__":
    unittest.main()
