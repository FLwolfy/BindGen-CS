#!/usr/bin/env python3
"""Reject incomplete, skipped, stale and dirty feature certification receipts."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from acceptance_evidence import test_receipt

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("feature_release_verifier", ROOT / "scripts/verify-feature-release-evidence.py")
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


class FeatureReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT):
            raise RuntimeError("The feature evidence test root escaped its workspace.")
        self.temporary = tempfile.TemporaryDirectory(prefix="feature-release-proof-", dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "Feature.cs").write_text("reviewed implementation", encoding="utf-8")
        self.hosts = {"windows-x64", "linux-x64", "macos-x64"}
        self.feature = {"featureId": "feature.one", "status": "implementationReady", "implementation": ["Feature.cs"],
            "acceptanceRationale": "The exact test independently proves the generated native call on each required host.",
            "requiredHosts": sorted(self.hosts), "releaseEvidence": [{"host": host, "kind": "test",
            "project": "BGCS.Tests", "match": r"^BGCS\.Tests\.FeatureTests\.ProvesNativeCall$",
            "requiredCases": ["BGCS.Tests.FeatureTests.ProvesNativeCall"]} for host in sorted(self.hosts)]}
        self.reports = [{"schemaVersion": 3, "platform": host, "status": "passed", "scope": "one-host-maintenance-candidate",
            "usesPrebuiltOutputs": False, "source": {"revision": "a" * 40, "workingTreeDirty": False, "checkedOutSourceSha256": "b" * 64},
            "executedTests": [{"trx": "tests/BGCS.Tests.trx", "sha256": "c" * 64,
                "results": [{"name": "BGCS.Tests.FeatureTests.ProvesNativeCall", "displayName": "BGCS.Tests.FeatureTests.ProvesNativeCall", "outcome": "Passed"}]}],
            "categories": [{"name": "upstream-native-invocation", "status": "passed"}]} for host in sorted(self.hosts)]

    def verify(self, ready=True):
        return VERIFIER.validate({"features": [self.feature]}, {"implementationReady": ready, "releaseReady": False}, self.reports,
            "a" * 40, "b" * 64, self.root, self.hosts)

    def test_exact_clean_same_revision_proofs_are_accepted(self):
        result = self.verify()
        self.assertEqual(3, len(result["features"][0]["proofs"]))
        self.assertIs(True, result["featureAcceptanceVerified"])
        self.assertIs(False, result["releaseReady"])

    def test_readiness_review_does_not_need_prior_same_commit_receipts_or_certify_release(self):
        rows = VERIFIER.validate_readiness({"features": [self.feature]},
            {"implementationReady": True, "releaseReady": False}, self.root, self.hosts)
        self.assertEqual([self.feature], rows)
        self.assertNotIn("featureAcceptanceVerified", rows[0])
        with self.assertRaisesRegex(RuntimeError, "complete declared desktop"):
            VERIFIER.validate({"features": rows}, {"implementationReady": True, "releaseReady": False}, [],
                "a" * 40, "b" * 64, self.root, self.hosts)

    def test_checked_in_release_claims_and_old_complete_status_cannot_shortcut_certification(self):
        for claim in ({"releaseReady": True}, {"implementationReady": True, "releaseReady": True},
                      {"implementationReady": True}, {"implementationReady": False, "releaseReady": False}):
            with self.subTest(claim=claim), self.assertRaisesRegex(RuntimeError, "certification is derived"):
                VERIFIER.validate_readiness({"features": [self.feature]}, claim, self.root, self.hosts)
        for status in ("complete", "verified", "inProgress", "pending"):
            self.feature["status"] = status
            with self.subTest(status=status), self.assertRaisesRegex(RuntimeError, "implementationReady source review"):
                self.verify()

    def test_readiness_requires_reviewed_assertions_exact_methods_and_host_scope(self):
        original = copy.deepcopy(self.feature)
        for field, value in (("acceptanceRationale", ""), ("requiredHosts", []), ("implementation", ["missing.cs"])):
            self.feature = copy.deepcopy(original)
            self.feature[field] = value
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                self.verify()
        self.feature = original
        for pattern in (".*", r"^BGCS\.Tests\..*$", "ProvesNativeCall"):
            self.feature["releaseEvidence"][0]["match"] = pattern
            with self.subTest(pattern=pattern), self.assertRaisesRegex(RuntimeError, "fully qualified test method"):
                self.verify()

    def test_readiness_boolean_cannot_replace_missing_feature_implementation_or_mapping(self):
        for key, value in (("status", "pending"), ("implementation", []), ("releaseEvidence", [])):
            original = copy.deepcopy(self.feature)
            self.feature[key] = value
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                self.verify()
            self.feature = original
        with self.assertRaises(RuntimeError):
            self.verify(ready=False)

    def test_dirty_stale_content_or_prebuilt_report_cannot_be_promoted(self):
        for key, value in (("revision", "d" * 40), ("workingTreeDirty", True), ("checkedOutSourceSha256", "e" * 64)):
            original = self.reports[0]["source"][key]
            self.reports[0]["source"][key] = value
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                self.verify()
            self.reports[0]["source"][key] = original
        self.reports[0]["usesPrebuiltOutputs"] = True
        with self.assertRaises(RuntimeError):
            self.verify()

    def test_not_executed_test_does_not_certify_even_with_another_passed_row(self):
        rows = self.reports[0]["executedTests"][0]["results"]
        for outcome in ("NotExecuted", "Failed", "Skipped"):
            rows.append({"name": "BGCS.Tests.FeatureTests.ProvesNativeCall", "outcome": outcome})
            with self.subTest(outcome=outcome), self.assertRaisesRegex(RuntimeError, "not executed"):
                self.verify()
            rows.pop()

    def test_finite_host_scope_needs_a_reviewed_reason(self):
        self.feature["requiredHosts"] = ["windows-x64"]
        self.feature["releaseEvidence"] = [row for row in self.feature["releaseEvidence"] if row["host"] == "windows-x64"]
        with self.assertRaisesRegex(RuntimeError, "finite host scope"):
            self.verify()
        self.feature["scopeJustification"] = "An actual MSVC-only source-view compiler protocol."
        self.verify()

    def test_duplicate_host_or_missing_native_category_is_rejected(self):
        self.reports.append(copy.deepcopy(self.reports[0]))
        with self.assertRaises(RuntimeError):
            self.verify()
        self.reports.pop()
        self.feature["releaseEvidence"] = [{"kind": "category", "host": host,
            "category": "upstream-native-invocation"} for host in sorted(self.hosts)]
        self.verify()
        self.reports[0]["categories"][0]["status"] = "pending"
        with self.assertRaisesRegex(RuntimeError, "category did not pass"):
            self.verify()

    def test_directory_placeholders_and_unhashed_test_receipts_cannot_certify_a_feature(self):
        self.feature["implementation"] = ["."]
        with self.assertRaisesRegex(RuntimeError, "implementation paths"):
            self.verify()
        self.feature["implementation"] = ["Feature.cs"]
        del self.reports[0]["executedTests"][0]["sha256"]
        with self.assertRaisesRegex(RuntimeError, "TRX content digest"):
            self.verify()

    def test_theory_requires_every_exact_argument_case_instead_of_one_matching_method(self):
        cases = ['BGCS.Tests.FeatureTests.ProvesNativeCall(mode: FunctionTable, source: False)',
                 'BGCS.Tests.FeatureTests.ProvesNativeCall(mode: LibraryImport, source: True)']
        for mapping in self.feature['releaseEvidence']:
            mapping['requiredCases'] = cases.copy()
        for report in self.reports:
            report['executedTests'][0]['results'] = [
                {'name': 'BGCS.Tests.FeatureTests.ProvesNativeCall', 'displayName': case, 'outcome': 'Passed'} for case in cases]
        self.verify()
        self.reports[0]['executedTests'][0]['results'].pop()
        with self.assertRaisesRegex(RuntimeError, 'reviewed exact display names'):
            self.verify()

    def test_case_declarations_reject_missing_duplicates_and_regex_shortcuts(self):
        proof = self.feature['releaseEvidence'][0]
        for cases in (None, [], ['same', 'same'], [None], ['.*']):
            proof['requiredCases'] = cases
            with self.subTest(cases=cases), self.assertRaises(RuntimeError):
                self.verify()

    def test_duplicate_renamed_failed_or_unidentified_execution_cases_do_not_certify(self):
        original = copy.deepcopy(self.reports[0]['executedTests'][0]['results'])
        for change in ('duplicate', 'rename', 'failed', 'missingDisplayName', 'extra'):
            rows = copy.deepcopy(original)
            if change == 'duplicate': rows.append(copy.deepcopy(rows[0]))
            if change == 'rename': rows[0]['displayName'] += '(mode: Wrong)'
            if change == 'failed': rows[0]['outcome'] = 'Failed'
            if change == 'missingDisplayName': del rows[0]['displayName']
            if change == 'extra': rows.append(rows[0] | {'displayName': 'unreviewed extra case'})
            self.reports[0]['executedTests'][0]['results'] = rows
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                self.verify()

    def test_trx_receipt_preserves_literal_case_arguments_independently_of_the_method_identity(self):
        cases = ['BGCS.Tests.FeatureTests.ProvesNativeCall(mode: FunctionTable, path: "目录 & 50%")',
                 'BGCS.Tests.FeatureTests.ProvesNativeCall(mode: LibraryImport, source: True)']
        document = ET.Element('TestRun')
        definitions, results = ET.SubElement(document, 'TestDefinitions'), ET.SubElement(document, 'Results')
        for index, case in enumerate(cases):
            definition = ET.SubElement(definitions, 'UnitTest', id=str(index), name=case)
            ET.SubElement(definition, 'TestMethod', className='BGCS.Tests.FeatureTests', name='ProvesNativeCall')
            ET.SubElement(results, 'UnitTestResult', testId=str(index), testName=case, outcome='Passed', executionId=str(index))
        path = self.root / 'actual.trx'
        ET.ElementTree(document).write(path, encoding='utf-8', xml_declaration=True)
        receipt = test_receipt(path)
        self.assertEqual(cases, [row['displayName'] for row in receipt['results']])
        self.assertEqual(['BGCS.Tests.FeatureTests.ProvesNativeCall'] * 2, [row['name'] for row in receipt['results']])

    def test_global_execution_feature_cannot_use_an_unrelated_host_test_or_category(self):
        self.feature['featureId'] = VERIFIER.FEATURE_ID
        with self.assertRaisesRegex(RuntimeError, 'exact global execution proof'):
            self.verify()
        self.feature['releaseEvidence'] = [{'host':host, 'kind':'global', 'proofId':VERIFIER.PROOF_ID} for host in self.hosts]
        with self.assertRaisesRegex(RuntimeError, 'global execution proof is missing'):
            self.verify()
        self.feature['requiredHosts'] = ['windows-x64']
        self.feature['scopeJustification'] = 'An attempted unsupported finite shortcut.'
        self.feature['releaseEvidence'] = [row for row in self.feature['releaseEvidence'] if row['host']=='windows-x64']
        with self.assertRaisesRegex(RuntimeError, 'every launch host'):
            self.verify()


class FeatureBaselineTests(unittest.TestCase):
    def setUp(self):
        self.baseline = {"schemaVersion": 1, "baselineRevision": "d" * 40, "features": [
            {"featureId": "native.one", "owner": "BGCS.Core", "basis": "original declared scope"},
            {"featureId": "native.two", "owner": "BGCS.Analysis", "origin": "3.0 requirement"}]}
        self.inventory = copy.deepcopy(self.baseline)

    def test_unchanged_feature_set_can_only_be_extended_after_its_frozen_prefix(self):
        self.inventory["features"].append({"featureId": "native.three", "owner": "BGCS.Runtime", "origin": "new requirement"})
        VERIFIER.validate_feature_baseline(self.inventory, self.baseline)
        self.inventory["features"].insert(0, self.inventory["features"].pop())
        with self.assertRaisesRegex(RuntimeError, "omitted, duplicated, reordered"):
            VERIFIER.validate_feature_baseline(self.inventory, self.baseline)

    def test_missing_duplicate_reordered_and_renamed_features_cannot_shrink_the_required_set(self):
        original = copy.deepcopy(self.inventory["features"])
        renamed = copy.deepcopy(original)
        renamed[0]["featureId"] = "native.other"
        for rows in (original[:1], [original[0], original[0]], list(reversed(original)), renamed):
            self.inventory["features"] = rows
            with self.subTest(rows=rows), self.assertRaisesRegex(RuntimeError, "omitted, duplicated, reordered"):
                VERIFIER.validate_feature_baseline(self.inventory, self.baseline)

    def test_original_owner_and_feature_definition_cannot_be_retargeted(self):
        for index, key in ((0, "owner"), (0, "basis"), (1, "origin")):
            self.inventory = copy.deepcopy(self.baseline)
            self.inventory["features"][index][key] = "different scope"
            with self.subTest(key=key), self.assertRaisesRegex(RuntimeError, "original definition"):
                VERIFIER.validate_feature_baseline(self.inventory, self.baseline)

    def test_major_version_current_owner_is_separate_from_the_frozen_historical_definition(self):
        self.baseline['historicalDefinitionRevision'] = 'c' * 40
        for current, previous in zip(self.inventory['features'], self.baseline['features']):
            current['historicalDefinition'] = {key: previous[key] for key in ('owner', 'basis', 'origin') if key in previous}
            current['owner'] = 'Current.Implementation'
            current['basis'] = 'current/source.cs'
        VERIFIER.validate_feature_baseline(self.inventory, self.baseline)
        self.inventory['features'][0]['historicalDefinition']['owner'] = 'rewritten history'
        with self.assertRaisesRegex(RuntimeError, 'original definition'):
            VERIFIER.validate_feature_baseline(self.inventory, self.baseline)

    def test_missing_or_other_revision_baseline_is_rejected(self):
        self.baseline["baselineRevision"] = "e" * 40
        with self.assertRaisesRegex(RuntimeError, "complete frozen identity baseline"):
            VERIFIER.validate_feature_baseline(self.inventory, self.baseline)
        self.baseline["baselineRevision"] = "d" * 40
        self.baseline["features"] = []
        with self.assertRaisesRegex(RuntimeError, "complete frozen identity baseline"):
            VERIFIER.validate_feature_baseline(self.inventory, self.baseline)


class ConfigurationReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        FeatureReleaseEvidenceTests.setUp(self)
        self.baseline = {"schemaVersion": 1, "baseline": "d" * 40, "entries": [
            {"configurationId": "CFG0001", "source": "old/Config.cs", "member": "url", "type": "string?"},
            {"configurationId": "CFG0002", "source": "old/Config.cs", "member": "strict", "type": "bool"}]}
        self.inventory = {"schemaVersion": 1, "baseline": "d" * 40, "entries": []}
        for previous in self.baseline["entries"]:
            entry = copy.deepcopy(previous)
            entry.update({"status": "implementationReady", "implementation": ["Feature.cs"], "requiredHosts": sorted(self.hosts),
                "acceptanceRationale": "The named test asserts the selected configuration behavior and resulting native call.",
                "releaseEvidence": [{"host": host, "kind": "test", "project": "BGCS.Tests",
                    "match": r"^BGCS\.Tests\.FeatureTests\.ProvesNativeCall$",
                    "requiredCases": ["BGCS.Tests.FeatureTests.ProvesNativeCall"]} for host in sorted(self.hosts)]})
            self.inventory["entries"].append(entry)

    def verify(self):
        return VERIFIER.validate_configurations(self.inventory, self.baseline, {"implementationReady": True, "releaseReady": False}, self.reports,
            "a" * 40, "b" * 64, self.root, self.hosts)

    def test_exact_configuration_rows_can_share_relevant_explicit_method_proofs(self):
        result = self.verify()
        self.assertEqual(["CFG0001", "CFG0002"], [row["configurationId"] for row in result])
        self.assertTrue(all(len(row["proofs"]) == 3 for row in result))

    def test_missing_duplicate_and_reordered_configuration_rows_are_rejected(self):
        original = copy.deepcopy(self.inventory["entries"])
        for entries in (original[:1], [original[0], original[0]], list(reversed(original))):
            self.inventory["entries"] = entries
            with self.subTest(entries=entries), self.assertRaisesRegex(RuntimeError, "omitted, duplicated, reordered"):
                self.verify()

    def test_original_configuration_identities_cannot_be_retargeted(self):
        for field in ("source", "member", "type"):
            original = self.inventory["entries"][0][field]
            self.inventory["entries"][0][field] = "changed original identity"
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "original source/member/type"):
                self.verify()
            self.inventory["entries"][0][field] = original

    def test_source_correspondence_does_not_create_release_acceptance_evidence(self):
        entry = self.inventory["entries"][0]
        entry.update(status="pending", implementationMapping={"status": "mapped_builtin", "evidence": ["Feature.cs"], "executableParityCertified": True})
        del entry["releaseEvidence"]
        with self.assertRaisesRegex(RuntimeError, "no actual same-revision test proof"):
            self.verify()

    def test_pending_unimplemented_or_unreviewed_rows_still_block_release(self):
        original = copy.deepcopy(self.inventory["entries"][0])
        for field, value in (("status", "pending"), ("implementation", []), ("implementation", ["."]), ("acceptanceRationale", "")):
            self.inventory["entries"][0] = copy.deepcopy(original)
            self.inventory["entries"][0][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                self.verify()

    def test_broad_suite_patterns_and_generic_categories_cannot_certify_configuration_rows(self):
        proof = self.inventory["entries"][0]["releaseEvidence"][0]
        for pattern in (".*", r"^BGCS\.Tests\..*$", "ProvesNativeCall"):
            proof["match"] = pattern
            with self.subTest(pattern=pattern), self.assertRaisesRegex(RuntimeError, "exact fully qualified test methods"):
                self.verify()
        proof.update(kind="category", category="upstream-native-invocation")
        with self.assertRaisesRegex(RuntimeError, "exact fully qualified test methods"):
            self.verify()

    def test_unexecuted_or_unrelated_methods_do_not_prove_a_row(self):
        proof = self.inventory["entries"][0]["releaseEvidence"][0]
        original = proof["match"]
        proof["match"] = r"^BGCS\.Tests\.FeatureTests\.Unrelated$"
        with self.assertRaisesRegex(RuntimeError, "missing, failed or not executed"):
            self.verify()
        proof["match"] = original
        self.reports[0]["executedTests"][0]["results"][0]["outcome"] = "NotExecuted"
        with self.assertRaisesRegex(RuntimeError, "missing, failed or not executed"):
            self.verify()

    def test_new_configuration_ids_may_only_append_after_the_frozen_prefix(self):
        appended = copy.deepcopy(self.inventory["entries"][0])
        appended.update(configurationId="CFG0003", source="new/Config.cs", member="newOption")
        self.inventory["entries"].append(appended)
        self.assertEqual(3, len(self.verify()))
        appended["configurationId"] = "CFG0004"
        with self.assertRaisesRegex(RuntimeError, "unstable identities"):
            self.verify()

    def test_missing_or_different_baseline_cannot_replace_the_original_enumeration(self):
        self.baseline["baseline"] = "e" * 40
        with self.assertRaisesRegex(RuntimeError, "complete frozen baseline"):
            self.verify()
        self.baseline["baseline"] = "d" * 40
        self.baseline["entries"] = []
        with self.assertRaisesRegex(RuntimeError, "complete frozen baseline"):
            self.verify()


if __name__ == "__main__":
    unittest.main(verbosity=2)
