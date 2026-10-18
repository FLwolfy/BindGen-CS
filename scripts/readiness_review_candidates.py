#!/usr/bin/env python3
"""Expand compact review-only declarations without modifying either acceptance inventory."""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def expand(document: dict, row: dict) -> dict:
    if (document.get('schemaVersion') != 2 or document.get('changesActualInventories') is not False
            or document.get('implementationReady') is not False or document.get('releaseReady') is not False):
        raise ValueError('Only the compact review-only candidate schema can be expanded.')
    reference = row.get('proposedDeclarationRef')
    if not isinstance(reference, str) or not re.fullmatch(r'D[0-9]{4}', reference):
        raise ValueError('A finite exact declaration reference is required.')
    declaration = copy.deepcopy(document['declarationReviews'][reference])
    for mapping in declaration.get('releaseEvidence', []):
        reference = mapping.pop('requiredCasesRef', None)
        if mapping.get('kind') != 'test':
            if reference is not None:
                raise ValueError('A non-test proof cannot substitute a literal test-case reference.')
            continue
        if not isinstance(reference, str) or not re.fullmatch(r'T[0-9]{4}', reference) or 'requiredCases' in mapping:
            raise ValueError('An exact reviewed test reference is required.')
        test = document['testReviews'][reference]
        if mapping.get('project') != test['project'] or mapping.get('match') != '^' + re.escape(test['method']) + '$':
            raise ValueError('The reviewed literal cases do not belong to the selected exact test method.')
        mapping['requiredCases'] = copy.deepcopy(test['requiredCases'])
    return declaration


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, default=ROOT / 'eng/acceptance/readiness-review-candidates.json')
    selector = parser.add_mutually_exclusive_group(required=True)
    selector.add_argument('--feature')
    selector.add_argument('--configuration')
    args = parser.parse_args()
    document = json.loads(args.input.read_text(encoding='utf-8'))
    collection, key, selected = ('features', 'featureId', args.feature) if args.feature else ('configurations', 'configurationId', args.configuration)
    rows = [row for group in document['groups'] for row in group[collection] if row[key] == selected]
    if len(rows) != 1:
        raise ValueError('Select exactly one existing reviewed candidate identity.')
    print(json.dumps({key: selected, 'reviewOnly': True, 'proposedDeclaration': expand(document, rows[0])}, indent=2))


if __name__ == '__main__':
    main()
