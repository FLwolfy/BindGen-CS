#!/usr/bin/env python3
"""Check compact review references preserve exact proof declarations; no certification is produced."""
import copy
import json
from pathlib import Path
import re
import unittest

from readiness_review_candidates import expand

ROOT = Path(__file__).resolve().parents[1]


class ReadinessCandidateTests(unittest.TestCase):
    def fixture(self):
        method = 'Example.Tests.ValueCases.Values'
        test = {'project': 'Example.Tests', 'method': method, 'requiredCases': [method + '(mode: 1)', method + '(mode: 2)']}
        declaration = {'status': 'inProgress', 'acceptanceRationale': 'Review still required.', 'releaseEvidence': [
            {'host': host, 'kind': 'test', 'project': test['project'], 'match': '^' + re.escape(method) + '$', 'requiredCasesRef': 'T0001'}
            for host in ('linux-x64', 'windows-x64', 'macos-x64')]}
        return {'schemaVersion': 2, 'changesActualInventories': False, 'implementationReady': False, 'releaseReady': False,
                'testReviews': {'T0001': test}, 'declarationReviews': {'D0001': declaration}}, {'proposedDeclarationRef': 'D0001'}

    def test_expansion_preserves_every_host_and_literal_case_without_promoting_state(self):
        document, row = self.fixture()
        original = copy.deepcopy(document)
        result = expand(document, row)
        self.assertEqual('inProgress', result['status'])
        self.assertEqual(3, len(result['releaseEvidence']))
        for proof in result['releaseEvidence']:
            self.assertEqual(document['testReviews']['T0001']['requiredCases'], proof['requiredCases'])
            self.assertNotIn('requiredCasesRef', proof)
        self.assertEqual(original, document)

    def test_wrong_test_project_or_method_cannot_borrow_case_expectations(self):
        for field in ('project', 'match'):
            document, row = self.fixture()
            document['declarationReviews']['D0001']['releaseEvidence'][0][field] = 'unrelated'
            with self.assertRaises(ValueError): expand(document, row)

    def test_certified_state_or_unknown_reference_is_not_a_review_input(self):
        for field in ('implementationReady', 'releaseReady', 'changesActualInventories'):
            document, row = self.fixture(); document[field] = True
            with self.assertRaises(ValueError): expand(document, row)
        document, _ = self.fixture()
        with self.assertRaises(ValueError): expand(document, {'proposedDeclarationRef': '../outside'})

    def test_checked_in_compact_candidates_expand_all_exact_references(self):
        document = json.loads((ROOT / 'eng/acceptance/readiness-review-candidates.json').read_text())
        for group in document['groups']:
            for reference in group['testReviewRefs']:
                self.assertIn(reference, document['testReviews'])
            for row in group['features'] + group['configurations']:
                declaration = expand(document, row)
                for mapping in declaration['releaseEvidence']:
                    if mapping['kind'] == 'test':
                        if declaration['status'] == 'implementationReady':
                            self.assertTrue(mapping['requiredCases'], mapping['match'])
                        self.assertEqual(len(mapping['requiredCases']), len(set(mapping['requiredCases'])))

    def test_closeout_proposal_keeps_every_frozen_identity_once_without_certification(self):
        document = json.loads((ROOT / 'eng/acceptance/readiness-review-candidates.json').read_text())
        features = [row['featureId'] for group in document['groups'] for row in group['features']]
        configurations = [row['configurationId'] for group in document['groups'] for row in group['configurations']]
        self.assertEqual(len(features), len(set(features)))
        self.assertEqual(len(configurations), len(set(configurations)))
        self.assertEqual({row['featureId'] for row in json.loads((ROOT / 'eng/acceptance/feature-inventory.json').read_text())['features']}, set(features))
        self.assertEqual({row['configurationId'] for row in json.loads((ROOT / 'eng/acceptance/configuration-inventory.json').read_text())['entries']}, set(configurations))
        self.assertFalse(document['releaseReady'])
        self.assertFalse(document['implementationReady'])
        self.assertFalse(document['changesActualInventories'])
        for declaration in document['declarationReviews'].values():
            self.assertTrue(all((ROOT / path).is_file() for path in declaration['implementation']))


if __name__ == '__main__':
    unittest.main()
