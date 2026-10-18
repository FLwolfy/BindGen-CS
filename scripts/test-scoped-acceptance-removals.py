import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("scope_verifier", ROOT / "scripts/verify-feature-release-evidence.py")
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)

class ScopeRemovalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.old_root = VERIFY.ROOT
        VERIFY.ROOT = Path(self.temp.name)
        self.addCleanup(setattr, VERIFY, "ROOT", self.old_root)
        self.path = VERIFY.ROOT / "eng/acceptance/scope-removals.json"
        self.path.parent.mkdir(parents=True)
        self.inventory = {"scopeRemovals": "eng/acceptance/scope-removals.json"}
        self.baseline = {**self.inventory, "baselineRevision": "a" * 40}
        self.audit = {"schemaVersion": 1, "previousBaselineRevision": "a" * 40, "authorization": "reviewed generator scope", "removedFeatures": [{"featureId": "retired", "reason": "external responsibility", "originalRequirement": {"featureId": "retired"}}]}
    def verify(self, active=("remaining",)):
        self.path.write_text(json.dumps(self.audit))
        return VERIFY.scoped_removals(self.inventory, self.baseline, "removedFeatures", list(active))
    def test_explicit_removal_cannot_certify_the_active_feature(self):
        self.assertEqual({"retired"}, self.verify())
    def test_active_or_duplicate_retirement_is_rejected(self):
        with self.assertRaises(RuntimeError): self.verify(("retired",))
        self.audit["removedFeatures"] *= 2
        with self.assertRaises(RuntimeError): self.verify()
    def test_pass_claim_and_missing_reason_are_rejected(self):
        row = self.audit["removedFeatures"][0]
        row["status"] = "passed"
        with self.assertRaises(RuntimeError): self.verify()
        del row["status"]; row["reason"] = ""
        with self.assertRaises(RuntimeError): self.verify()
    def test_changed_baseline_or_mismatched_reference_is_rejected(self):
        self.audit["previousBaselineRevision"] = "b" * 40
        with self.assertRaises(RuntimeError): self.verify()
        self.inventory["scopeRemovals"] = "different.json"
        with self.assertRaises(RuntimeError): self.verify()

if __name__ == "__main__": unittest.main()
