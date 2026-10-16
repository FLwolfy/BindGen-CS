#!/usr/bin/env python3
"""Exercise exact global gate/installed-report joins with synthetic verifier fixtures only."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from global_feature_evidence import PROOF_ID, verify_global_features

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('global_product_fixtures', ROOT / 'scripts/test-ci-product-evidence.py')
FIXTURES = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FIXTURES)
FEATURE_SPEC = importlib.util.spec_from_file_location('global_feature_integration', ROOT / 'scripts/verify-feature-release-evidence.py')
FEATURE = importlib.util.module_from_spec(FEATURE_SPEC)
FEATURE_SPEC.loader.exec_module(FEATURE)


class GlobalFeatureEvidenceTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / 'artifacts/temporary'
        parent.mkdir(exist_ok=True, parents=True)
        self.temporary = tempfile.TemporaryDirectory(prefix='global-feature-fixtures-', dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.revision, self.content = 'a' * 40, 'b' * 64
        self.gates = json.loads((ROOT / 'eng/acceptance/required-release-gates.json').read_text())
        self.evidence = {'schemaVersion': 1, 'sourceRevision': self.revision, 'archiveSha256': {},
            'ci': self.workflow_run('ci.yml', 'push', self.gates['requiredJobNames']),
            'products': self.workflow_run('packages.yml', 'workflow_run', self.gates['requiredProductPackageJobNames'])}
        self.archives = {}
        for item in self.gates['requiredProductEvidence']:
            receipt = FIXTURES.complete_receipt()
            receipt['hostRuntimeIdentifier'] = item['hostRid']
            if item.get('requireVs2026'):
                receipt['checks'].append(FIXTURES.VERIFIER.VS_CHECK)
                receipt['visualStudioMsbuildVersion'] = '18.2.1'
            payloads = FIXTURES.complete_reports(receipt)
            for matrix in receipt['featureMatrices']:
                report = json.loads(payloads[matrix['receipt']])
                if matrix['name'].startswith('installed-native-aot-'):
                    target = {'win-x64':'windows-x64-msvc','linux-x64':'linux-x64-gnu','osx-x64':'macos-x64-darwin'}[item['hostRid']]
                    for abi in report['generatedAbiManifests']: abi['nativeTargetId'] = target
                payloads[matrix['receipt']] = json.dumps(report).encode()
                matrix['receiptSha256'] = hashlib.sha256(payloads[matrix['receipt']]).hexdigest()
            payloads['verification.json'] = json.dumps(receipt).encode()
            path = self.root / (item['artifact'] + '.zip')
            self.write_archive(path, payloads)
            self.archives[item['artifact']] = path

    def workflow_run(self, workflow, event, names):
        return {'workflow': workflow, 'id': 123 if event == 'push' else 456, 'headSha': self.revision,
            'headBranch': 'main', 'event': event, 'status': 'completed', 'conclusion': 'success',
            'displayTitle': 'BGCS installed products at ' + self.revision,
            'jobs': [{'name': name, 'status': 'completed', 'conclusion': 'success'} for name in names]}

    def write_archive(self, path, payloads):
        with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, payload in payloads.items(): archive.writestr(name, payload)
        self.evidence['archiveSha256'][path.stem] = hashlib.sha256(path.read_bytes()).hexdigest()

    def verify(self):
        return verify_global_features(self.gates, self.evidence, self.archives, self.revision, self.content)

    def test_every_host_requires_actual_hashed_installed_aot_and_both_browser_reports(self):
        result = self.verify()[PROOF_ID]['hosts']
        self.assertEqual({'windows-x64','linux-x64','macos-x64'}, set(result))
        self.assertTrue(all(len(value['matrices']) == 3 and value['verificationSha256'] for value in result.values()))

    def test_feature_verifier_joins_only_processed_global_execution_proofs(self):
        proofs = self.verify()
        hosts = set(proofs[PROOF_ID]['hosts'])
        (self.root / 'Feature.cs').write_text('synthetic verifier fixture only')
        feature = {'featureId':FEATURE.FEATURE_ID, 'status':'implementationReady', 'implementation':['Feature.cs'],
            'requiredHosts':sorted(hosts), 'acceptanceRationale':'The exact independent gates and installed reports prove every host and execution mode.',
            'releaseEvidence':[{'kind':'global','proofId':PROOF_ID,'host':host} for host in sorted(hosts)]}
        reports = [{'schemaVersion':3,'platform':host,'status':'passed','scope':'one-host-maintenance-candidate',
            'usesPrebuiltOutputs':False,'source':{'revision':self.revision,'workingTreeDirty':False,'checkedOutSourceSha256':self.content}}
            for host in hosts]
        result = FEATURE.validate({'features':[feature]},{'implementationReady':True,'releaseReady':False},reports,
            self.revision,self.content,self.root,hosts,proofs)
        self.assertEqual(3,len(result['features'][0]['proofs']))
        self.assertIs(False,result['releaseReady'])

    def test_missing_failed_skipped_or_duplicated_independent_jobs_are_rejected(self):
        original = copy.deepcopy(self.evidence['ci']['jobs'])
        for change in ('missing','failed','skipped','duplicate','incomplete'):
            rows = copy.deepcopy(original)
            if change == 'missing': rows.pop()
            elif change == 'duplicate': rows.append(copy.deepcopy(rows[0]))
            elif change == 'incomplete': rows[0]['status'] = 'in_progress'
            else: rows[0]['conclusion'] = change
            self.evidence['ci']['jobs'] = rows
            with self.subTest(change=change), self.assertRaises(RuntimeError): self.verify()

    def test_unaccepted_or_other_revision_workflow_observations_cannot_certify(self):
        original = copy.deepcopy(self.evidence)
        for section, field, value in [('ci','headSha','d'*40),('ci','event','pull_request'),
                ('ci','headBranch','feature'),('products','conclusion','failure'),('products','displayTitle','other commit')]:
            self.evidence = copy.deepcopy(original)
            self.evidence[section][field] = value
            with self.subTest(field=field), self.assertRaises(RuntimeError): self.verify()

    def test_product_run_default_branch_sha_is_not_substituted_for_actual_archive_source(self):
        self.evidence['products']['headSha'] = 'd' * 40
        self.verify()

    def test_missing_taskhost_archive_or_changed_download_hash_is_rejected(self):
        name = next(item['artifact'] for item in self.gates['requiredProductEvidence'] if item.get('requireVs2026'))
        path = self.archives.pop(name)
        with self.assertRaises(RuntimeError): self.verify()
        self.archives[name] = path
        self.evidence['archiveSha256'][name] = 'f' * 64
        with self.assertRaisesRegex(RuntimeError, 'workflow observation'): self.verify()

    def test_consistently_rehashed_archive_cannot_hide_missing_browser_invocations(self):
        path = next(iter(self.archives.values()))
        with zipfile.ZipFile(path) as archive: payloads = {name: archive.read(name) for name in archive.namelist()}
        receipt = json.loads(payloads['verification.json'])
        matrix = next(row for row in receipt['featureMatrices'] if 'wasm-interpreter' in row['name'])
        report = json.loads(payloads[matrix['receipt']])
        report['invocation']['checks'].pop()
        payloads[matrix['receipt']] = json.dumps(report).encode()
        matrix['receiptSha256'] = hashlib.sha256(payloads[matrix['receipt']]).hexdigest()
        payloads['verification.json'] = json.dumps(receipt).encode()
        self.write_archive(path, payloads)
        with self.assertRaises(RuntimeError): self.verify()

    def test_global_definition_cannot_omit_a_host_report_or_use_an_unknown_proof(self):
        original = copy.deepcopy(self.gates['globalFeatureProofs'])
        for change in ('host','report','identity'):
            self.gates['globalFeatureProofs'] = copy.deepcopy(original)
            definition = self.gates['globalFeatureProofs'][PROOF_ID]
            if change == 'host': definition['hosts'].pop('macos-x64')
            elif change == 'report': definition['requiredReports'].pop()
            else: self.gates['globalFeatureProofs']['unreviewed'] = definition
            with self.subTest(change=change), self.assertRaises(RuntimeError): self.verify()

    def test_host_aliases_and_different_source_content_cannot_reuse_a_valid_product_archive(self):
        self.content = 'd' * 64
        with self.assertRaisesRegex(RuntimeError, 'different checked-out source bytes'): self.verify()
        self.content = 'b' * 64
        definition = self.gates['globalFeatureProofs'][PROOF_ID]['hosts']
        definition['linux-x64']['artifact'] = definition['windows-x64']['artifact']
        definition['linux-x64']['hostRid'] = 'win-x64'
        with self.assertRaisesRegex(RuntimeError, 'exact independent gates'): self.verify()


if __name__ == '__main__':
    unittest.main(verbosity=2)
