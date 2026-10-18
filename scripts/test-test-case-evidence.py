#!/usr/bin/env python3
"""Pure exact-case omission/tamper controls; no synthetic fixture is a product receipt."""
import copy
from pathlib import Path
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET
from test_case_evidence import validate_driver, _trx, _trx_name

class ExactCaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.cases = [{'id': str(uuid.uuid5(uuid.NAMESPACE_DNS, str(index))), 'fullyQualifiedName': 'BGCS.Tests.Example.Theory',
            'displayName': 'BGCS.Tests.Example.Theory(value: ' + str(index) + ')', 'className': 'BGCS.Tests.Example',
            'compiledMethod': 'BGCS.Tests.Example.Theory', 'executorUri': 'executor://xunit/VsTestRunner2/netcoreapp'} for index in range(2)]
        self.report = {'schemaVersion': 1, 'success': True, 'discoveryComplete': True, 'discoveryTotal': 2,
            'assemblySha256': 'a' * 64, 'sourceAssembly': 'BGCS.Tests.dll', 'executionRequested': True,
            'compiledMethods': [{'className': 'BGCS.Tests.Example', 'fullyQualifiedName': 'BGCS.Tests.Example.Theory', 'theory': True, 'parameterCount': 1, 'generic': False}],
            'discoveredCases': self.cases, 'selectedCases': copy.deepcopy(self.cases),
            'executedCases': [{key: case[key] for key in ('id', 'fullyQualifiedName', 'displayName')} | {'outcome': 'Passed', 'duration': '00:00:01'} for case in self.cases],
            'vstestIdentity': {key: 'b' * 64 for key in ('consoleSha256', 'translationLayerSha256', 'objectModelSha256')}}

    def test_full_execution_preserves_every_literal_argument(self):
        actual = validate_driver(self.report, True)
        self.assertEqual(self.cases, actual['discoveredCases'])

    def test_missing_duplicate_skipped_failed_or_changed_cases_are_rejected(self):
        for fault in ('missing-discovery', 'duplicate-discovery', 'missing-selection', 'missing-execution', 'duplicate-execution', 'skip', 'fail', 'literal', 'method', 'assembly', 'platform', 'collapsed-theory'):
            value = copy.deepcopy(self.report)
            if fault == 'missing-discovery': value['discoveredCases'].pop()
            elif fault == 'duplicate-discovery': value['discoveredCases'][1] = value['discoveredCases'][0]
            elif fault == 'missing-selection': value['selectedCases'].pop()
            elif fault == 'missing-execution': value['executedCases'].pop()
            elif fault == 'duplicate-execution': value['executedCases'][1] = value['executedCases'][0]
            elif fault in ('skip', 'fail'): value['executedCases'][0]['outcome'] = 'NotExecuted' if fault == 'skip' else 'Failed'
            elif fault == 'literal': value['executedCases'][0]['displayName'] += 'changed'
            elif fault == 'method': value['compiledMethods'][0]['fullyQualifiedName'] += 'Missing'
            elif fault == 'assembly': value['assemblySha256'] = 'unhashed'
            elif fault == 'platform': value['vstestIdentity'].pop('consoleSha256')
            else: value['discoveredCases'][0]['displayName'] = value['discoveredCases'][0]['fullyQualifiedName']
            with self.subTest(fault=fault), self.assertRaises(ValueError): validate_driver(value, True)

    def test_discovery_cannot_claim_execution_or_certify_a_partial_run(self):
        value = copy.deepcopy(self.report)
        with self.assertRaises(ValueError): validate_driver(value, False)
        value.update(executionRequested=False, selectedCases=[], executedCases=None)
        validate_driver(value, False)
        with self.assertRaises(ValueError): validate_driver(value, True)

    def test_trx_uses_the_locked_loggers_utf16_escaping_without_decoding_literals(self):
        self.assertEqual('λé\\ud83d\\ude00\\ud800\\udc00', _trx_name('λé😀𐀀'))
        self.assertEqual(r'\u0061\ud83d\ude00', _trx_name(r'\u0061\ud83d\ude00'))
        self.assertEqual('\\u0000\\ud800\\ufffe\\uffff\t\n\r', _trx_name('\0\ud800\ufffe\uffff\t\n\r'))

    def test_independent_trx_requires_exact_names_ids_counts_and_success(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'tests.trx'
            self.cases[0]['displayName'] = 'BGCS.Tests.Example.Theory(value: "λ😀\\ud83d\\ude00")'
            for fault in ('valid', 'missing', 'duplicate', 'skip', 'literal', 'unicode-literal', 'decoded-literal', 'execution-id', 'counter'):
                root = ET.Element('TestRun'); results = ET.SubElement(root, 'Results'); definitions = ET.SubElement(root, 'TestDefinitions'); entries = ET.SubElement(root, 'TestEntries')
                for case in self.cases:
                    execution = str(uuid.uuid4()); definition = ET.SubElement(definitions, 'UnitTest', id=case['id'])
                    ET.SubElement(definition, 'Execution', id=execution)
                    ET.SubElement(definition, 'TestMethod', className=case['className'], name='Theory')
                    ET.SubElement(results, 'UnitTestResult', testId=case['id'], testName=_trx_name(case['displayName']), outcome='Passed', executionId=execution)
                    ET.SubElement(entries, 'TestEntry', testId=case['id'], executionId=execution)
                counters = ET.SubElement(ET.SubElement(root, 'ResultSummary', outcome='Completed'), 'Counters', total='2', executed='2', passed='2', failed='0', notExecuted='0')
                if fault == 'missing': results.remove(results[0])
                elif fault == 'duplicate': results[1].set('testId', results[0].get('testId'))
                elif fault == 'skip': results[0].set('outcome', 'NotExecuted')
                elif fault == 'literal': results[0].set('testName', 'collapsed')
                elif fault == 'unicode-literal': results[0].set('testName', _trx_name(self.cases[0]['displayName'].replace('😀', '😁')))
                elif fault == 'decoded-literal': results[0].set('testName', self.cases[0]['displayName'])
                elif fault == 'execution-id': entries[0].set('executionId', str(uuid.uuid4()))
                elif fault == 'counter': counters.set('passed', '1')
                ET.ElementTree(root).write(path, encoding='utf-8')
                if fault == 'valid': _trx(path, self.cases)
                else:
                    with self.subTest(fault=fault), self.assertRaises(ValueError): _trx(path, self.cases)

if __name__ == '__main__': unittest.main(verbosity=2)
