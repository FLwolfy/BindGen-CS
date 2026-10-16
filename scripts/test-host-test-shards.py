#!/usr/bin/env python3
"""Pure omission/tamper regressions for Host partition evidence; fixtures are not product receipts."""
from __future__ import annotations

import copy
from importlib.util import module_from_spec, spec_from_file_location
import json
import hashlib
from pathlib import Path
import tempfile
import unittest
import uuid
import zipfile
import xml.etree.ElementTree as ET

from host_test_shards import SHARDS, NS, digest, join, partition, validate_driver, validate_identity, write_trx

A = 'a' * 64
spec = spec_from_file_location('host_shard_runner', Path(__file__).with_name('run-host-test-shards.py'))
runner = module_from_spec(spec)
spec.loader.exec_module(runner)


class HostShardEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='host-shard-proof-')
        self.root = Path(self.temp.name)
        classes = {}
        for number in range(1000):
            name = 'BGCS.Tests.ActualClass' + str(number)
            classes.setdefault(partition(name), name)
            if len(classes) == SHARDS:
                break
        self.cases = sorted([{'id': str(uuid.uuid5(uuid.NAMESPACE_DNS, name)), 'fullyQualifiedName': name + '.Theory',
                              'displayName': name + '.Theory(value: ' + str(index) + ')', 'className': name,
                              'compiledMethod': name + '.Theory', 'executorUri': 'executor://xunit/VsTestRunner2/netcoreapp'}
                             for index, name in classes.items()], key=lambda row: row['id'])
        self.methods = [{'className': case['className'], 'fullyQualifiedName': case['compiledMethod'], 'theory': True,
                         'parameterCount': 1, 'generic': False} for case in self.cases]
        required = ['host/BGCS.Tests.dll', 'host/BGCS.Tests.deps.json', 'host/BGCS.Tests.runtimeconfig.json', 'host/xunit.runner.visualstudio.testadapter.dll',
                    'worker/BGCS.Frontend.Clang.Worker.dll', 'worker/BGCS.Frontend.Clang.Worker.deps.json', 'worker/BGCS.Frontend.Clang.Worker.runtimeconfig.json',
                    'driver/BGCS.TestDriver.dll', 'driver/BGCS.TestDriver.deps.json', 'driver/BGCS.TestDriver.runtimeconfig.json',
                    'driver/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll', 'driver/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll',
                    'sdk/vstest.console.dll', 'sdk/vstest.console.deps.json', 'sdk/vstest.console.runtimeconfig.json',
                    'sdk/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll', 'sdk/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll', 'sdk/dotnet-host', 'sdk/System.Private.CoreLib.dll',
                    'fixture/NeutralLoweringPlugin/NeutralLoweringPlugin.dll']
        self.identity = {'schemaVersion': 1, 'source': {'revision': '1' * 40, 'checkedOutSourceSha256': A, 'workingTreeDirty': False},
                         'host': 'windows-x64', 'sdkVersion': '10.0.401', 'runtimeVersion': '10.0.12', 'ci': True,
                         'runnerImage': {'os': 'win22', 'version': 'locked-image'}, 'assemblySha256': A,
                         'executionClosure': {name: A for name in required}}
        self.receipts = []
        for index in range(SHARDS):
            directory = self.root / ('shard' + str(index))
            directory.mkdir()
            selected = [case for case in self.cases if partition(case['className']) == index]
            driver = {'schemaVersion': 1, 'success': True, 'discoveryComplete': True, 'discoveryTotal': len(self.cases),
                      'assemblySha256': A, 'sourceAssembly': 'BGCS.Tests.dll', 'shardCount': SHARDS, 'shardIndex': index,
                      'compiledMethods': self.methods, 'discoveredCases': self.cases, 'selectedCases': selected,
                      'executedCases': [{key: case[key] for key in ('id', 'fullyQualifiedName', 'displayName')} | {'outcome': 'Passed', 'duration': '00:00:01'} for case in selected],
                      'vstestIdentity': {name: A for name in ('consoleSha256', 'translationLayerSha256', 'objectModelSha256')}}
            file = directory / 'driver.json'
            file.write_text(json.dumps(driver))
            self.write_raw_trx(directory / ('shard' + str(index) + '.trx'), selected)
            self.receipts.append({'schemaVersion': 1, 'success': True, 'shardIndex': index, 'identity': copy.deepcopy(self.identity),
                                  'driver': {'path': 'shard' + str(index) + '/driver.json', 'sha256': digest(file)},
                                  'trx': {'path': 'shard' + str(index) + '/shard' + str(index) + '.trx', 'sha256': digest(directory / ('shard' + str(index) + '.trx'))}})

    def tearDown(self):
        self.temp.cleanup()

    @staticmethod
    def write_raw_trx(path, cases):
        t = lambda name: '{' + NS + '}' + name
        root = ET.Element(t('TestRun'))
        results = ET.SubElement(root, t('Results'))
        definitions = ET.SubElement(root, t('TestDefinitions'))
        entries = ET.SubElement(root, t('TestEntries'))
        for case in cases:
            execution = str(uuid.uuid4())
            ET.SubElement(results, t('UnitTestResult'), {'testId': case['id'], 'executionId': execution, 'testName': case['displayName'], 'outcome': 'Passed'})
            definition = ET.SubElement(definitions, t('UnitTest'), {'id': case['id']})
            ET.SubElement(definition, t('Execution'), {'id': execution})
            cls, method = case['fullyQualifiedName'].rsplit('.', 1)
            ET.SubElement(definition, t('TestMethod'), {'className': cls, 'name': method})
            ET.SubElement(entries, t('TestEntry'), {'testId': case['id'], 'executionId': execution})
        summary = ET.SubElement(root, t('ResultSummary'), {'outcome': 'Completed'})
        ET.SubElement(summary, t('Counters'), {'total': str(len(cases)), 'executed': str(len(cases)), 'passed': str(len(cases)), 'failed': '0', 'notExecuted': '0'})
        ET.ElementTree(root).write(path)

    def driver(self, index=0):
        return json.loads((self.root / self.receipts[index]['driver']['path']).read_text())

    def replace_driver(self, value, index=0):
        path = self.root / self.receipts[index]['driver']['path']
        path.write_text(json.dumps(value))
        self.receipts[index]['driver']['sha256'] = digest(path)

    def test_complete_union_keeps_all_exact_cases_and_independent_raw_hashes(self):
        result = join(self.receipts, self.root)
        self.assertEqual(8, result['passed'])
        self.assertTrue(result['certifying'])
        output = self.root / 'union.trx'
        write_trx(result, output)
        rows = ET.parse(output).findall('.//{*}UnitTestResult')
        self.assertEqual({case['displayName'] for case in self.cases}, {row.get('testName') for row in rows})
        self.assertEqual({case['id'] for case in self.cases}, {row.get('testId') for row in rows})
        self.assertEqual(8, len(result['shards']))
        self.assertFalse(result['releaseReady'])

    def test_missing_or_duplicate_shards_fail(self):
        with self.assertRaises(ValueError): join(self.receipts[:-1], self.root)
        with self.assertRaises(ValueError): join(self.receipts[:-1] + [self.receipts[0]], self.root)

    def test_failed_or_skipped_shards_fail(self):
        for outcome in ('Failed', 'Skipped', 'NotExecuted'):
            with self.subTest(outcome=outcome):
                driver = self.driver()
                driver['executedCases'][0]['outcome'] = outcome
                with self.assertRaises(ValueError): validate_driver(driver, 0)

    def test_missing_duplicate_or_extra_executions_fail(self):
        original = self.driver()
        for cases in ([], original['executedCases'] * 2, original['executedCases'] + [original['executedCases'][0] | {'id': str(uuid.uuid4())}]):
            with self.subTest(cases=cases):
                value = copy.deepcopy(original); value['executedCases'] = cases
                with self.assertRaises(ValueError): validate_driver(value, 0)

    def test_case_display_arguments_and_fqn_cannot_be_rewritten(self):
        for key in ('displayName', 'fullyQualifiedName', 'id'):
            with self.subTest(key=key):
                value = self.driver(); value['executedCases'][0][key] = 'rewritten'
                with self.assertRaises(ValueError): validate_driver(value, 0)

    def test_theory_cannot_collapse_into_a_method_name(self):
        value = self.driver()
        value['discoveredCases'][0]['displayName'] = value['discoveredCases'][0]['fullyQualifiedName']
        with self.assertRaises(ValueError): validate_driver(value, 0)

    def test_missing_compiled_methods_or_extra_unowned_discoveries_fail(self):
        for kind in ('omitted-method', 'undeclared-case', 'duplicate-discovery'):
            with self.subTest(kind=kind):
                value = self.driver()
                if kind == 'omitted-method': value['compiledMethods'].append(self.methods[0] | {'fullyQualifiedName': 'BGCS.Tests.New.Untested'})
                elif kind == 'undeclared-case': value['discoveredCases'][0]['compiledMethod'] = 'BGCS.Tests.New.Untested'
                else: value['discoveredCases'].append(value['discoveredCases'][0]); value['discoveryTotal'] += 1
                with self.assertRaises(ValueError): validate_driver(value, 0)

    def test_hash_partition_cannot_be_bypassed_by_reassignment(self):
        value = self.driver()
        value['selectedCases'] = [case for case in value['discoveredCases'] if partition(case['className']) == 1]
        with self.assertRaises(ValueError): validate_driver(value, 0)

    def test_each_source_sdk_image_adapter_worker_and_assembly_identity_is_bound(self):
        mutations = [('source', 'revision'), ('source', 'checkedOutSourceSha256'), ('runnerImage', 'version'), ('executionClosure', 'worker/BGCS.Frontend.Clang.Worker.dll'),
                     ('executionClosure', 'host/xunit.runner.visualstudio.testadapter.dll'), ('executionClosure', 'sdk/vstest.console.dll')]
        for section, field in mutations:
            with self.subTest(section=section, field=field):
                receipts = copy.deepcopy(self.receipts)
                receipts[1]['identity'][section][field] = ('2' * 40 if field == 'revision' else 'b' * 64)
                with self.assertRaises(ValueError): join(receipts, self.root)
        for field in ('assemblySha256', 'sdkVersion', 'runtimeVersion'):
            with self.subTest(field=field):
                receipts = copy.deepcopy(self.receipts); receipts[1]['identity'][field] = 'contradiction'
                with self.assertRaises(ValueError): join(receipts, self.root)

    def test_missing_mandatory_closure_and_local_dirty_shortcuts_fail(self):
        for key in ('host/BGCS.Tests.dll', 'host/xunit.runner.visualstudio.testadapter.dll', 'worker/BGCS.Frontend.Clang.Worker.deps.json', 'sdk/System.Private.CoreLib.dll'):
            identity = copy.deepcopy(self.identity); del identity['executionClosure'][key]
            with self.subTest(key=key), self.assertRaises(ValueError): validate_identity(identity)
        identity = copy.deepcopy(self.identity); identity['source']['workingTreeDirty'] = True
        with self.assertRaises(ValueError): validate_identity(identity)
        identity['ci'] = False
        validate_identity(identity, allow_dirty=True)

    def test_changed_raw_bytes_or_missing_files_fail(self):
        for kind in ('driver', 'trx'):
            path = self.root / self.receipts[0][kind]['path']
            original = path.read_bytes(); path.write_bytes(original + b' ')
            with self.subTest(kind=kind), self.assertRaises(ValueError): join(self.receipts, self.root)
            path.write_bytes(original)
        (self.root / self.receipts[0]['trx']['path']).unlink()
        with self.assertRaises(ValueError): join(self.receipts, self.root)

    def test_even_rehashed_trx_cannot_omit_or_change_a_required_case(self):
        path = self.root / self.receipts[0]['trx']['path']
        original = path.read_bytes()
        for fault in ('arguments', 'failed', 'summary', 'missing-result', 'entry', 'entry-execution', 'definition-execution'):
            with self.subTest(fault=fault):
                path.write_bytes(original); document = ET.parse(path)
                result = document.find('.//{*}UnitTestResult')
                if fault == 'arguments': result.set('testName', 'different argument')
                elif fault == 'failed': result.set('outcome', 'NotExecuted')
                elif fault == 'summary': document.find('.//{*}ResultSummary').set('outcome', 'Failed')
                elif fault == 'missing-result': document.find('.//{*}Results').remove(result)
                elif fault == 'entry': document.find('.//{*}TestEntry').set('testId', str(uuid.uuid4()))
                elif fault == 'entry-execution': document.find('.//{*}TestEntry').set('executionId', str(uuid.uuid4()))
                else: document.find('.//{*}Execution').set('id', str(uuid.uuid4()))
                document.write(path); self.receipts[0]['trx']['sha256'] = digest(path)
                with self.assertRaises(ValueError): join(self.receipts, self.root)

    def test_platform_report_cannot_contradict_frozen_sdk_bytes(self):
        value = self.driver(); value['vstestIdentity']['objectModelSha256'] = 'b' * 64
        self.replace_driver(value)
        with self.assertRaises(ValueError): join(self.receipts, self.root)

    def test_unknown_or_bool_schema_fields_fail(self):
        for key, val in (('schemaVersion', True), ('shardIndex', False), ('discoveryTotal', True), ('newUnreviewedField', True)):
            with self.subTest(key=key):
                value = self.driver(); value[key] = val
                with self.assertRaises(ValueError): validate_driver(value, 0)
        for identity in (self.identity | {'schemaVersion': True}, self.identity | {'unreviewed': True},
                         self.identity | {'source': self.identity['source'] | {'unreviewed': True}}):
            with self.assertRaises(ValueError): validate_identity(identity)

    def test_path_traversal_cannot_select_external_evidence(self):
        for relative in ('../outside.json', '/outside.json', 'shard0/../driver.json', 'C:/outside.json', 'shard0\\driver.json'):
            value = copy.deepcopy(self.receipts); value[0]['driver']['path'] = relative
            with self.subTest(relative=relative), self.assertRaises(ValueError): join(value, self.root)

    def asset_manifest(self):
        return {'schemaVersion': 1, 'identity': copy.deepcopy(self.identity), 'configuration': 'Release',
                'files': [{'path': 'owned/' + name, 'component': name, 'sha256': value}
                          for name, value in self.identity['executionClosure'].items() if not name.startswith('sdk/')]}

    def test_asset_manifest_requires_every_exact_component_without_duplicates(self):
        descriptor = {'schemaVersion': 1, 'archiveSha256': A, 'identity': self.identity}
        original = self.asset_manifest()
        expected = runner.validate_asset_manifest(descriptor, original, 'Release')
        self.assertEqual(len(original['files']), len(expected))
        for mutation in ('missing', 'duplicate-path', 'duplicate-component', 'additional', 'changed-hash'):
            with self.subTest(mutation=mutation):
                value = copy.deepcopy(original)
                if mutation == 'missing': value['files'].pop()
                elif mutation == 'duplicate-path': value['files'].append(value['files'][0])
                elif mutation == 'duplicate-component': value['files'].append(value['files'][0] | {'path': 'other/path'})
                elif mutation == 'additional': value['files'].append({'path': 'new/file', 'component': 'host/undeclared.dll', 'sha256': A})
                else: value['files'][0]['sha256'] = 'b' * 64
                with self.assertRaises(RuntimeError): runner.validate_asset_manifest(descriptor, value, 'Release')

    def test_asset_manifest_rejects_schema_config_sdk_and_nonportable_paths(self):
        descriptor = {'schemaVersion': 1, 'archiveSha256': A, 'identity': self.identity}
        for mutation in ('bool-schema', 'unknown-schema', 'config', 'identity', 'sdk', 'parent', 'absolute', 'windows', 'backslash', 'unknown-file-field'):
            with self.subTest(mutation=mutation):
                value = self.asset_manifest()
                if mutation == 'bool-schema': value['schemaVersion'] = True
                elif mutation == 'unknown-schema': value['unknown'] = True
                elif mutation == 'config': value['configuration'] = 'Debug'
                elif mutation == 'identity': value['identity']['source']['revision'] = '2' * 40
                elif mutation == 'sdk': value['files'][0]['component'] = 'sdk/vstest.console.dll'
                elif mutation == 'unknown-file-field': value['files'][0]['unsafe'] = True
                else: value['files'][0]['path'] = {'parent': '../escape.dll', 'absolute': '/escape.dll', 'windows': 'C:/escape.dll', 'backslash': 'dir\\file.dll'}[mutation]
                with self.assertRaises(RuntimeError): runner.validate_asset_manifest(descriptor, value, 'Release')

    def test_release_recheck_requires_exact_clean_source_and_selected_sdk(self):
        sdk = {name: value for name, value in self.identity['executionClosure'].items() if name.startswith('sdk/')}
        runner.validate_accepted_source_sdk(self.identity, self.identity['source'], sdk)
        for source in (self.identity['source'] | {'revision': '2' * 40}, self.identity['source'] | {'workingTreeDirty': True},
                       self.identity['source'] | {'checkedOutSourceSha256': 'b' * 64}):
            with self.assertRaises(RuntimeError): runner.validate_accepted_source_sdk(self.identity, source, sdk)
        for actual in (sdk | {'sdk/vstest.console.dll': 'b' * 64}, {name: value for name, value in sdk.items() if name != 'sdk/dotnet-host'}):
            with self.assertRaises(RuntimeError): runner.validate_accepted_source_sdk(self.identity, self.identity['source'], actual)

    def test_release_recheck_hashes_original_archive_without_restoring_binaries(self):
        payload = b'original executed fixture bytes'
        value = hashlib.sha256(payload).hexdigest()
        identity = copy.deepcopy(self.identity)
        identity['executionClosure'] = {name: value for name in identity['executionClosure']}
        identity['assemblySha256'] = value
        manifest = self.asset_manifest()
        manifest['identity'] = identity
        for entry in manifest['files']: entry['sha256'] = value
        archive = self.root / 'execution.zip'
        def write(mutation=None):
            with zipfile.ZipFile(archive, 'w') as target:
                target.writestr('host-execution.manifest.json', json.dumps(manifest))
                for index, entry in enumerate(manifest['files']):
                    if mutation == 'missing' and index == 0: continue
                    target.writestr(entry['path'], b'tampered' if mutation == 'bytes' and index == 0 else payload)
                if mutation == 'additional': target.writestr('undeclared.dll', payload)
            descriptor = {'schemaVersion': 1, 'identity': identity, 'archiveSha256': digest(archive)}
            (self.root / 'asset.json').write_text(json.dumps(descriptor))
        write()
        runner.verify_accepted_archive(self.root, identity, 'Release')
        self.assertFalse((self.root / 'owned').exists())
        for mutation in ('missing', 'bytes', 'additional'):
            with self.subTest(mutation=mutation):
                write(mutation)
                with self.assertRaises(RuntimeError): runner.verify_accepted_archive(self.root, identity, 'Release')
        write()
        with self.assertRaises(RuntimeError): runner.verify_accepted_archive(self.root, identity | {'assemblySha256': A}, 'Release')


if __name__ == '__main__':
    unittest.main()
