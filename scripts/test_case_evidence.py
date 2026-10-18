"""Validate complete exact discovered test cases and one ordinary all-case execution."""
from __future__ import annotations
import copy, hashlib, json, re, uuid
from pathlib import Path
import xml.etree.ElementTree as ET
SHA = re.compile(r'[a-f0-9]{64}\Z')
GUID = re.compile(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}\Z')
def digest(path):
    with Path(path).open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()
def _require(condition: bool, reason: str) -> None:
    if not condition:
        raise ValueError(reason)


def _case(case: dict) -> tuple[str, str, str]:
    _require(isinstance(case, dict) and isinstance(case.get('id'), str) and bool(GUID.fullmatch(case['id'])), 'Invalid discovered case ID.')
    _require(set(case) == {'id', 'fullyQualifiedName', 'displayName', 'className', 'compiledMethod', 'executorUri'}, 'Unknown case schema fields.')
    for name in ('fullyQualifiedName', 'displayName', 'className', 'compiledMethod', 'executorUri'):
        _require(isinstance(case.get(name), str) and bool(case[name].strip()), 'Missing exact case identity: ' + name)
    _require(case['compiledMethod'].startswith(case['className'] + '.'), 'Case class and compiled method disagree.')
    return case['id'], case['fullyQualifiedName'], case['displayName']


def validate_driver(report: dict, execute: bool) -> dict:
    _require(type(report.get('schemaVersion')) is int and report.get('schemaVersion') == 1 and report.get('success') is True and report.get('discoveryComplete') is True,
             'Driver did not complete discovery successfully.')
    _require(set(report) == {'schemaVersion', 'success', 'discoveryComplete', 'discoveryTotal', 'assemblySha256', 'sourceAssembly', 'executionRequested',
                             'compiledMethods', 'discoveredCases', 'selectedCases', 'executedCases', 'vstestIdentity'}, 'Unknown structured driver schema fields.')
    _require(type(report.get('executionRequested')) is bool and report['executionRequested'] == execute, 'Unexpected execution mode.')
    _require(isinstance(report.get('assemblySha256'), str) and bool(SHA.fullmatch(report['assemblySha256'])), 'Missing assembly hash.')
    _require(report.get('sourceAssembly') == 'BGCS.Tests.dll', 'The Host source assembly changed.')
    cases = report.get('discoveredCases')
    _require(isinstance(cases, list) and len(cases) > 0 and type(report.get('discoveryTotal')) is int and report.get('discoveryTotal') == len(cases), 'Missing discovered case universe.')
    identities = [_case(case) for case in cases]
    _require(len({item[0] for item in identities}) == len(cases) and len({item[1:] for item in identities}) == len(cases), 'Duplicate discovery ID or literal case identity.')
    methods = report.get('compiledMethods')
    _require(isinstance(methods, list) and bool(methods), 'Compiled Fact/Theory census is required.')
    method_names = [item.get('fullyQualifiedName') for item in methods]
    _require(all(isinstance(item, str) and bool(item) for item in method_names) and len(set(method_names)) == len(method_names), 'Invalid compiled method census.')
    _require(set(method_names) == {case['compiledMethod'] for case in cases}, 'Compiled Fact/Theory methods and discovered coverage differ.')
    for method in methods:
        _require(set(method) == {'className', 'fullyQualifiedName', 'theory', 'parameterCount', 'generic'}
                 and type(method.get('theory')) is bool and type(method.get('generic')) is bool
                 and type(method.get('parameterCount')) is int and method['parameterCount'] >= 0, 'Invalid compiled method schema.')
        rows = [case for case in cases if case['compiledMethod'] == method['fullyQualifiedName']]
        _require(all(case['className'] == method['className'] for case in rows), 'Compiled class coverage changed.')
        _require(all(case['fullyQualifiedName'].replace('+', '.') == method['fullyQualifiedName']
                     or method['generic'] and case['fullyQualifiedName'].replace('+', '.').startswith(method['fullyQualifiedName'] + '<') for case in rows),
                 'A discovered FQN does not correspond to its compiled Fact/Theory.')
        if method.get('theory') and method.get('parameterCount', 0) > 0:
            _require(all(case['displayName'] != case['fullyQualifiedName'] and '(' in case['displayName'] and case['displayName'].endswith(')') for case in rows),
                     'An argument theory was not pre-enumerated.')
    selected = report.get('selectedCases')
    expected = cases if execute else []
    _require(isinstance(selected, list) and selected == expected, 'The complete execution selection omitted or reassigned cases.')
    if not execute:
        _require(report.get('executedCases') is None, 'Discovery-only evidence unexpectedly executed tests.')
    else:
        _require(bool(selected), 'Required execution is empty.')
        executed = report.get('executedCases')
        _require(isinstance(executed, list) and len(executed) == len(selected), 'Execution count differs from the selected discovery cases.')
        seen = set()
        target = {case['id']: case for case in selected}
        for item in executed:
            _require(isinstance(item, dict) and item.get('id') in target and item['id'] not in seen, 'Duplicate or unknown executed case.')
            _require(set(item) == {'id', 'fullyQualifiedName', 'displayName', 'outcome', 'duration'}
                     and isinstance(item.get('duration'), str) and bool(re.fullmatch(r'\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?', item['duration'])),
                     'Unknown execution result schema or invalid recorded duration.')
            seen.add(item['id'])
            wanted = target[item['id']]
            _require(item.get('fullyQualifiedName') == wanted['fullyQualifiedName'] and item.get('displayName') == wanted['displayName'], 'Executed theory/FQN identity changed.')
            _require(item.get('outcome') == 'Passed', 'Required case failed or was skipped.')
    platform = report.get('vstestIdentity', {})
    _require(set(platform) == {'consoleSha256', 'translationLayerSha256', 'objectModelSha256'}
             and all(isinstance(value, str) and bool(SHA.fullmatch(value)) for value in platform.values()), 'Missing exact test-platform identity.')
    return {key: copy.deepcopy(report[key]) for key in ('assemblySha256', 'sourceAssembly', 'compiledMethods', 'discoveredCases', 'vstestIdentity')}


def _trx_name(value: str) -> str:
    # The locked VSTest TRX logger escapes invalid XML UTF-16 code units, including
    # both halves of non-BMP characters. Encode the expected name; never decode
    # literal backslash-u arguments or normalize the independently captured cases.
    units = value.encode('utf-16-le', errors='surrogatepass')
    result = []
    for index in range(0, len(units), 2):
        unit = int.from_bytes(units[index:index + 2], 'little')
        valid = unit in (9, 10, 13) or 0x20 <= unit <= 0xD7FF or 0xE000 <= unit <= 0xFFFD
        result.append(chr(unit) if valid else f'\\u{unit:04x}')
    return ''.join(result)


def _trx(path: Path, selected: list[dict]) -> tuple[ET.Element, list[ET.Element], list[ET.Element], list[ET.Element]]:
    root = ET.parse(path).getroot()
    definitions = root.findall('./{*}TestDefinitions/{*}UnitTest')
    results = root.findall('./{*}Results/{*}UnitTestResult')
    entries = root.findall('./{*}TestEntries/{*}TestEntry')
    target = {case['id']: case for case in selected}
    summary = root.find('./{*}ResultSummary')
    counters = summary.find('{*}Counters') if summary is not None else None
    _require(summary is not None and summary.get('outcome') == 'Completed' and counters is not None
             and all(counters.get(name) == str(len(target)) for name in ('total', 'executed', 'passed'))
             and all(counters.get(name) == '0' for name in ('failed', 'notExecuted')), 'TRX did not complete with the exact all-passing case counters.')
    _require(len(definitions) == len(target) and len(results) == len(target) and len(entries) == len(target), 'TRX discovery/results/entry counts do not cover the exact complete discovered case set.')
    defmap = {}
    for definition in definitions:
        identifier = definition.get('id', '').lower()
        _require(identifier in target and identifier not in defmap, 'TRX has duplicate or unexpected definitions.')
        method = definition.find('{*}TestMethod')
        _require(method is not None and method.get('className', '') + '.' + method.get('name', '') == target[identifier]['fullyQualifiedName'], 'TRX changed a fully qualified method identity.')
        defmap[identifier] = definition
    seen = set()
    executions = {}
    for result in results:
        identifier = result.get('testId', '').lower()
        _require(identifier in target and identifier not in seen, 'TRX has duplicate or unexpected results.')
        seen.add(identifier)
        expected_name = _trx_name(target[identifier]['displayName'])
        _require(result.get('outcome') == 'Passed' and result.get('testName') == expected_name,
                 f'TRX skipped/failed a case or changed its literal arguments: {identifier}: expected={expected_name!r}, actual={result.get("testName")!r}.')
        execution = result.get('executionId', '').lower()
        definition_execution = defmap[identifier].find('{*}Execution')
        _require(bool(GUID.fullmatch(execution)) and execution not in executions.values() and definition_execution is not None
                 and definition_execution.get('id', '').lower() == execution, 'TRX definition/result execution identities disagree or duplicate.')
        executions[identifier] = execution
    _require({entry.get('testId', '').lower() for entry in entries} == set(target), 'TRX omitted case execution entries.')
    _require(all(entry.get('executionId', '').lower() == executions[entry.get('testId', '').lower()] for entry in entries),
             'TRX result and TestEntry execution identities disagree.')
    return root, results, definitions, entries
