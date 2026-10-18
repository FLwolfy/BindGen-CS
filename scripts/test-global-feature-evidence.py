#!/usr/bin/env python3
"""Pure same-head generator/product proof controls; fixtures are not release evidence."""
from __future__ import annotations
import copy, hashlib, importlib.util, json
from pathlib import Path
import tempfile
import unittest
import zipfile
from global_feature_evidence import verify_global_features

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('product_control_fixture', ROOT / 'scripts/test-ci-product-evidence.py')
PRODUCT = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(PRODUCT)

class GlobalFeatureEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.revision = 'a' * 40; self.content = 'b' * 64
        self.gates = json.loads((ROOT / 'eng/acceptance/required-release-gates.json').read_text(encoding='utf-8'))
        def workflow(name, event, jobs):
            return {'workflow': name, 'id': 123, 'status': 'completed', 'conclusion': 'success',
                'headBranch': 'main', 'event': event, 'headSha': self.revision,
                'displayTitle': 'BGCS installed products at ' + self.revision,
                'jobs': [{'name': job, 'status': 'completed', 'conclusion': 'success'} for job in jobs]}
        self.evidence = {'schemaVersion': 1, 'sourceRevision': self.revision,
            'ci': workflow('ci.yml', 'push', self.gates['requiredJobNames']),
            'products': workflow('packages.yml', 'workflow_run', self.gates['requiredProductPackageJobNames']),
            'archiveSha256': {}}
        self.archives = {}
        for item in self.gates['requiredProductEvidence']:
            receipt = PRODUCT.complete_receipt(); receipt['hostRuntimeIdentifier'] = item['hostRid']
            payloads = PRODUCT.complete_reports(receipt)
            path = self.root / (item['artifact'] + '.zip')
            with zipfile.ZipFile(path, 'w') as archive:
                archive.writestr('verification.json', json.dumps(receipt))
                for name, payload in payloads.items(): archive.writestr(name, payload)
            self.archives[item['artifact']] = path
            self.evidence['archiveSha256'][item['artifact']] = hashlib.sha256(path.read_bytes()).hexdigest()

    def verify(self):
        return verify_global_features(self.gates, self.evidence, self.archives, self.revision, self.content)

    def test_one_full_product_baseline_is_not_application_or_all_platform_certification(self):
        result = self.verify()['installedProducts']
        self.assertEqual(self.revision, result['sourceRevision'])
        self.assertEqual(1, len(result['artifacts']))
        self.assertEqual([], result['artifacts'][0]['matrices'])
        self.assertEqual({}, self.gates['globalFeatureProofs'])

    def test_each_three_host_job_and_product_job_must_finish_without_skip_or_duplicates(self):
        for workflow in ('ci', 'products'):
            for fault in ('missing', 'duplicate', 'failed', 'skipped', 'incomplete'):
                original = copy.deepcopy(self.evidence[workflow]['jobs']); jobs = self.evidence[workflow]['jobs']
                if fault == 'missing': jobs.pop()
                elif fault == 'duplicate': jobs.append(copy.deepcopy(jobs[0]))
                elif fault == 'incomplete': jobs[0]['status'] = 'in_progress'
                else: jobs[0]['conclusion'] = fault
                with self.subTest(workflow=workflow, fault=fault), self.assertRaises(RuntimeError): self.verify()
                self.evidence[workflow]['jobs'] = original

    def test_wrong_head_branch_event_workflow_and_product_source_are_rejected(self):
        for workflow, key, value in (('ci','headSha','d'*40),('ci','headBranch','feature'),('ci','event','pull_request'),
                                    ('products','displayTitle','BGCS installed products at '+'d'*40),('products','workflow','untrusted.yml')):
            original = self.evidence[workflow][key]; self.evidence[workflow][key] = value
            with self.subTest(workflow=workflow,key=key), self.assertRaises(RuntimeError): self.verify()
            self.evidence[workflow][key] = original

    def test_missing_extra_or_changed_archive_is_rejected(self):
        name = next(iter(self.archives)); original = self.archives[name]
        self.archives['unexpected'] = original
        with self.assertRaises(RuntimeError): self.verify()
        del self.archives['unexpected']; del self.archives[name]
        with self.assertRaises(RuntimeError): self.verify()
        self.archives[name] = original
        original.write_bytes(original.read_bytes() + b'tamper')
        with self.assertRaises(RuntimeError): self.verify()

    def test_different_source_bytes_or_unreviewed_application_proof_is_rejected(self):
        self.content = 'd' * 64
        with self.assertRaises(RuntimeError): self.verify()
        self.content = 'b' * 64; self.gates['globalFeatureProofs'] = {'unreviewed': {}}
        with self.assertRaises(RuntimeError): self.verify()

if __name__ == '__main__': unittest.main(verbosity=2)
