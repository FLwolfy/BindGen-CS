#!/usr/bin/env python3
"""Verify the finite early GNU smoke cases; this does not replace full Host gates."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


PREFIX = 'BGCS.Tests.'
STANDARD = PREFIX + 'NativeStandardLoweringTests.StandardValues_ParseBridgeReparseCompileAndInvokeRealNativeOperations'
BRANCH = PREFIX + 'NativeSourceViewCacheTests.NativePreprocessingStillObservesAbsoluteMissingHeadersBeforeDetachedFactReuse'
DTO = PREFIX + 'NativeCppDtoStorageTests.CompilerProvenDtos_PreserveNestedAndArrayFieldsThroughPointerCopyChannels'
DRIFT = PREFIX + 'NativeCppDtoStorageTests.SameSizedNativeDtoFieldDrift_RejectsFinalBuildAndPreservesPublication'
TYPEDEF = PREFIX + 'NeutralSourceFactParityTests.SourceAliasesAnonymousRecordsAndCallbacksSurvivePublicGenerationAndCompilation'
C_BUILD = PREFIX + 'NeutralNativeBuildTests.CBuild_UsesC17AndPreservesPreviousOutputWhenCompilationOrExportsFail'
METHODS = (STANDARD, BRANCH, DTO, DRIFT, TYPEDEF, C_BUILD)
REQUIRED_CASES = {
    STANDARD: STANDARD,
    BRANCH + '(family: "gnu")': BRANCH,
    DTO + '(runtimeMode: "package")': DTO,
    DTO + '(runtimeMode: "source")': DTO,
    DRIFT + '(drift: "type")': DRIFT,
    DRIFT + '(drift: "offset")': DRIFT,
    TYPEDEF + '(singleFile: False)': TYPEDEF,
    TYPEDEF + '(singleFile: True)': TYPEDEF,
    C_BUILD: C_BUILD,
}


def filter_expression() -> str:
    # DisplayName is an xUnit-supported VSTest property. Keep the GNU theory
    # row exact in the receipt too, so no empty or gated foreign row can pass.
    return '|'.join('(' + 'FullyQualifiedName=' + method
                    + ('&DisplayName~gnu' if method == BRANCH else '') + ')' for method in METHODS)


def verify(path: Path) -> dict:
    tree = ET.parse(path).getroot()
    definitions = {}
    for definition in tree.findall('.//{*}TestDefinitions/{*}UnitTest'):
        identifier = definition.get('id')
        method = definition.find('{*}TestMethod')
        if not identifier or identifier in definitions or method is None:
            raise RuntimeError('GNU smoke has missing/duplicate test definitions.')
        definitions[identifier] = method.get('className', '') + '.' + method.get('name', '')
    cases = []
    names, identifiers = set(), set()
    results = tree.find('{*}Results')
    if results is None:
        raise RuntimeError('GNU smoke contains no executed results.')
    for result in results:
        name, identifier = result.get('testName'), result.get('testId')
        if result.tag.rsplit('}', 1)[-1] != 'UnitTestResult' or result.get('outcome') != 'Passed':
            raise RuntimeError('GNU smoke contains failed/skipped/incomplete result.')
        if name not in REQUIRED_CASES or name in names or not identifier or identifier in identifiers:
            raise RuntimeError('GNU smoke contains an unknown/duplicate literal case.')
        if definitions.get(identifier) != REQUIRED_CASES[name]:
            raise RuntimeError('GNU smoke changed an executed fully qualified method.')
        names.add(name)
        identifiers.add(identifier)
        cases.append({'displayName': name, 'fullyQualifiedName': definitions[identifier], 'testId': identifier})
    if names != set(REQUIRED_CASES) or set(definitions) != identifiers:
        raise RuntimeError('GNU smoke is missing a required literal case or has extra definitions.')
    counters = tree.find('{*}ResultSummary/{*}Counters')
    if counters is None or any(counters.get(key) != str(len(REQUIRED_CASES)) for key in ('total', 'executed', 'passed')):
        raise RuntimeError('GNU smoke counters do not prove all required cases executed and passed.')
    if any(counters.get(key) != '0' for key in ('failed', 'error', 'timeout', 'aborted', 'notExecuted')):
        raise RuntimeError('GNU smoke reports failed, timed-out, aborted or unexecuted tests.')
    if any(info.get('outcome') == 'Error' for info in tree.findall('.//{*}RunInfo')):
        raise RuntimeError('GNU smoke test run reported an execution error.')
    return {'schemaVersion': 1, 'diagnosticOnly': True, 'releaseCertification': False,
            'scope': 'Early actual GNU compatibility smoke; all full desktop/Host/installed gates remain required.',
            'requiredMethodCount': len(METHODS), 'requiredCaseCount': len(REQUIRED_CASES),
            'trxSha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'cases': sorted(cases, key=lambda case: case['displayName'])}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--print-filter', action='store_true')
    group.add_argument('--trx', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.print_filter:
        print(filter_expression())
        return
    report = verify(args.trx)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f"Verified {len(REQUIRED_CASES)} exact GNU smoke cases across {len(METHODS)} methods.")


if __name__ == '__main__':
    main()
