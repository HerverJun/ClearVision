from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import build_core20_proof_assets as core20  # noqa: E402


class Core20HumanEvidenceLedgerTests(unittest.TestCase):
    def setUp(self) -> None:
        self._old_values = (
            core20.REPO_ROOT,
            core20.CORE20_HUMAN_LEDGER,
            core20.GOVERNED_CATALOG,
        )
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        core20.REPO_ROOT = self.root
        core20.CORE20_HUMAN_LEDGER = self.root / "ledger.json"
        core20.GOVERNED_CATALOG = self.root / "catalog.json"
        self.fingerprints = {operator: f"{index + 1:064x}" for index, operator in enumerate(core20.CORE20_OPERATORS)}
        core20.GOVERNED_CATALOG.write_text(
            json.dumps(
                {
                    "operators": [
                        {"id": operator, "generationFingerprint": fingerprint}
                        for operator, fingerprint in self.fingerprints.items()
                    ]
                }
            ),
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        core20.REPO_ROOT, core20.CORE20_HUMAN_LEDGER, core20.GOVERNED_CATALOG = self._old_values
        self._tmp.cleanup()

    def ledger(self) -> dict[str, object]:
        return {
            "schemaVersion": "2026-09-01.core20-human-evidence-ledger.v1",
            "claimBoundary": "manual evidence",
            "entries": [
                {
                    "operatorType": operator,
                    "cardFingerprint": fingerprint,
                    "verdict": "unreviewed",
                    "algorithmBoundary": "",
                    "failureModes": [],
                    "typicalInputs": [],
                    "typicalOutputs": [],
                    "unavailableScenarios": [],
                    "reviewer": None,
                    "reviewedAt": None,
                }
                for operator, fingerprint in self.fingerprints.items()
            ],
        }

    def test_unreviewed_entries_remain_release_not_ready_without_fake_approval(self) -> None:
        core20.CORE20_HUMAN_LEDGER.write_text(json.dumps(self.ledger()), encoding="utf-8")

        summary, errors = core20.evaluate_human_evidence_ledger()

        self.assertEqual(errors, [])
        self.assertEqual(summary["reviewedCount"], 0)
        self.assertEqual(summary["unreviewedCount"], 20)
        self.assertEqual(summary["staleCount"], 0)
        self.assertFalse(summary["releaseReady"])

    def test_stale_reviewed_fingerprint_invalidates_verdict_and_requires_review(self) -> None:
        ledger = copy.deepcopy(self.ledger())
        entry = ledger["entries"][0]
        entry.update(
            {
                "cardFingerprint": "f" * 64,
                "verdict": "pass",
                "algorithmBoundary": "reviewed boundary",
                "failureModes": ["failure"],
                "typicalInputs": ["input"],
                "typicalOutputs": ["output"],
                "unavailableScenarios": ["unsupported"],
                "reviewer": "human-reviewer",
                "reviewedAt": "2026-09-01",
            }
        )
        core20.CORE20_HUMAN_LEDGER.write_text(json.dumps(ledger), encoding="utf-8")

        summary, errors = core20.evaluate_human_evidence_ledger()

        self.assertEqual(errors, [])
        self.assertEqual(summary["staleCount"], 1)
        self.assertEqual(summary["reviewedCount"], 0)
        self.assertEqual(summary["passCount"], 0)
        self.assertFalse(summary["releaseReady"])


if __name__ == "__main__":
    unittest.main()
