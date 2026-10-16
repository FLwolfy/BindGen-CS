#!/usr/bin/env python3
"""Authenticate the exact structured eight-way Host test partition and its real TRX union."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import re
import uuid
import xml.etree.ElementTree as ET

SHARDS = 8
SHA = re.compile(r'[a-f0-9]{64}\Z')
GUID = re.compile(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}\Z')
NS = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def partition(class_name: str) -> int:
    return int.from_bytes(hashlib.sha256(class_name.encode()).digest()[:4], 'big') % SHARDS


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


def validate_driver(report: dict, shard: int | None) -> dict:
    _require(type(report.get('schemaVersion')) is int and report.get('schemaVersion') == 1 and report.get('success') is True and report.get('discoveryComplete') is True,
             'Driver did not complete discovery successfully.')
    _require(set(report) == {'schemaVersion', 'success', 'discoveryComplete', 'discoveryTotal', 'assemblySha256', 'sourceAssembly', 'shardCount', 'shardIndex',
                             'compiledMethods', 'discoveredCases', 'selectedCases', 'executedCases', 'vstestIdentity'}, 'Unknown structured driver schema fields.')
    _require(report.get('shardCount') == SHARDS and report.get('shardIndex') == shard, 'Unexpected shard identity/count.')
    _require(shard is None and report['shardIndex'] is None or type(report['shardIndex']) is int, 'The shard index must be an integer or discovery-only null.')
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
    expected = [] if shard is None else [case for case in cases if partition(case['className']) == shard]
    _require(isinstance(selected, list) and selected == expected, 'The deterministic class partition omitted or reassigned cases.')
    if shard is None:
        _require(report.get('executedCases') is None, 'Discovery-only evidence unexpectedly executed tests.')
    else:
        _require(bool(selected), 'Required shard is empty.')
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


def validate_identity(identity: dict, *, allow_dirty: bool = False) -> None:
    _require(isinstance(identity, dict) and set(identity) == {'schemaVersion', 'source', 'host', 'sdkVersion', 'runtimeVersion', 'ci',
                                                            'runnerImage', 'assemblySha256', 'executionClosure'}
             and type(identity.get('schemaVersion')) is int and identity.get('schemaVersion') == 1, 'Unknown Host execution identity schema.')
    source = identity.get('source', {})
    _require(isinstance(source, dict) and set(source) == {'revision', 'checkedOutSourceSha256', 'workingTreeDirty'}, 'Unknown source identity schema.')
    _require(isinstance(source.get('revision'), str) and bool(re.fullmatch(r'[a-f0-9]{40}', source['revision'])), 'Missing source revision.')
    _require(isinstance(source.get('checkedOutSourceSha256'), str) and bool(SHA.fullmatch(source['checkedOutSourceSha256'])), 'Missing source content identity.')
    _require(isinstance(source.get('workingTreeDirty'), bool) and (allow_dirty or source['workingTreeDirty'] is False), 'Dirty local observations cannot certify CI.')
    _require(identity.get('host') in {'windows-x64', 'linux-x64', 'macos-x64'}, 'Unknown launch host.')
    _require(identity.get('sdkVersion') == '10.0.401', 'Host sharding requires the locked actual SDK.')
    _require(identity.get('runtimeVersion') == '10.0.12', 'Host sharding requires the locked actual runtime.')
    _require(isinstance(identity.get('ci'), bool) and (allow_dirty or identity['ci'] is True), 'Local observations are not CI certification.')
    image = identity.get('runnerImage', {})
    _require(isinstance(image, dict) and set(image) == {'os', 'version'}
             and all(isinstance(image.get(name), str) and bool(image[name]) for name in ('os', 'version')), 'The actual runner image identity is required.')
    closure = identity.get('executionClosure')
    _require(isinstance(closure, dict) and bool(closure), 'Missing complete execution closure.')
    for name, value in closure.items():
        _require(isinstance(name, str) and not name.startswith('/') and '\\' not in name and ':' not in name
                 and all(part not in ('', '.', '..') for part in name.split('/')) and isinstance(value, str) and bool(SHA.fullmatch(value)), 'Invalid closure member/hash.')
    for prefix in ('host/', 'worker/', 'driver/', 'sdk/', 'fixture/'):
        _require(any(name.startswith(prefix) for name in closure), 'Missing execution closure component ' + prefix)
    for name in ('host/BGCS.Tests.dll', 'host/BGCS.Tests.deps.json', 'host/BGCS.Tests.runtimeconfig.json',
                 'host/xunit.runner.visualstudio.testadapter.dll',
                 'worker/BGCS.Frontend.Clang.Worker.dll', 'worker/BGCS.Frontend.Clang.Worker.deps.json', 'worker/BGCS.Frontend.Clang.Worker.runtimeconfig.json',
                 'driver/BGCS.TestDriver.dll', 'driver/BGCS.TestDriver.deps.json', 'driver/BGCS.TestDriver.runtimeconfig.json',
                 'driver/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll', 'driver/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll',
                 'sdk/vstest.console.dll', 'sdk/vstest.console.deps.json', 'sdk/vstest.console.runtimeconfig.json',
                 'sdk/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll', 'sdk/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll',
                 'sdk/dotnet-host', 'sdk/System.Private.CoreLib.dll'):
        _require(name in closure, 'Missing required execution closure member ' + name)
    for name in ('Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll', 'Microsoft.VisualStudio.TestPlatform.ObjectModel.dll'):
        _require(closure['driver/' + name] == closure['sdk/' + name], 'The driver test-platform references must match the exact SDK assemblies.')
    _require(closure.get('host/BGCS.Tests.dll') == identity.get('assemblySha256'), 'Assembly hash is not bound to the execution closure.')


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
    _require(len(definitions) == len(target) and len(results) == len(target) and len(entries) == len(target), 'TRX discovery/results/entry counts do not cover the exact partition.')
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
        _require(result.get('outcome') == 'Passed' and result.get('testName') == target[identifier]['displayName'], 'TRX skipped/failed a case or changed its literal arguments.')
        execution = result.get('executionId', '').lower()
        definition_execution = defmap[identifier].find('{*}Execution')
        _require(bool(GUID.fullmatch(execution)) and execution not in executions.values() and definition_execution is not None
                 and definition_execution.get('id', '').lower() == execution, 'TRX definition/result execution identities disagree or duplicate.')
        executions[identifier] = execution
    _require({entry.get('testId', '').lower() for entry in entries} == set(target), 'TRX omitted case execution entries.')
    _require(all(entry.get('executionId', '').lower() == executions[entry.get('testId', '').lower()] for entry in entries),
             'TRX result and TestEntry execution identities disagree.')
    return root, results, definitions, entries


def join(shards: list[dict], directory: Path, *, allow_dirty: bool = False) -> dict:
    _require(len(shards) == SHARDS, 'All eight shard receipts are mandatory.')
    common = None
    identity = None
    seen = set()
    union = set()
    proof = []
    documents = []
    for receipt in shards:
        _require(set(receipt) == {'schemaVersion', 'success', 'shardIndex', 'identity', 'driver', 'trx'}
                 and type(receipt.get('schemaVersion')) is int and receipt.get('schemaVersion') == 1 and receipt.get('success') is True, 'A shard did not succeed or has an unknown schema.')
        index = receipt.get('shardIndex')
        _require(type(index) is int and 0 <= index < SHARDS and index not in seen, 'Duplicate/invalid shard index.')
        seen.add(index)
        validate_identity(receipt.get('identity', {}), allow_dirty=allow_dirty)
        if identity is None:
            identity = receipt['identity']
        _require(receipt['identity'] == identity, 'Shard source/SDK/adapter/worker/closure identities disagree.')
        files = {}
        for kind in ('driver', 'trx'):
            record = receipt.get(kind, {})
            _require(isinstance(record, dict) and set(record) == {'path', 'sha256'}, 'Unknown raw shard report schema.')
            relative = record.get('path')
            _require(isinstance(relative, str) and not Path(relative).is_absolute() and '\\' not in relative and ':' not in relative
                     and all(part not in ('', '.', '..') for part in relative.split('/')),
                     'Shard report path escapes the accepted artifact.')
            path = (directory / relative).resolve()
            _require(path.is_relative_to(directory.resolve()) and path.is_file() and digest(path) == record.get('sha256'), 'Shard report is missing or has changed bytes.')
            files[kind] = path
        driver = json.loads(files['driver'].read_text(encoding='utf-8-sig'))
        base = validate_driver(driver, index)
        _require(driver['assemblySha256'] == identity['assemblySha256'], 'Driver used a different Host assembly.')
        for field, name in (('consoleSha256', 'sdk/vstest.console.dll'),
                            ('translationLayerSha256', 'driver/Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll'),
                            ('objectModelSha256', 'driver/Microsoft.VisualStudio.TestPlatform.ObjectModel.dll')):
            _require(driver['vstestIdentity'][field] == identity['executionClosure'][name], 'Actual test-platform bytes differ from the accepted execution closure.')
        if common is None:
            common = base
        _require(common == base, 'Shards discovered different case universes or test-platform bytes.')
        selected = driver['selectedCases']
        _require(not union.intersection(case['id'] for case in selected), 'Shard cases overlap.')
        union.update(case['id'] for case in selected)
        documents.append(_trx(files['trx'], selected))
        proof.append({'shardIndex': index, 'driverSha256': digest(files['driver']), 'trxSha256': digest(files['trx']), 'passed': len(selected)})
    assert common is not None
    _require(union == {case['id'] for case in common['discoveredCases']}, 'The executed shard union omitted discovered cases.')
    return {'schemaVersion': 1, 'success': True, 'releaseReady': False, 'certifying': identity['ci'] and not identity['source']['workingTreeDirty'],
            'identity': identity, 'shardCount': SHARDS, 'discoverySha256': hashlib.sha256(json.dumps(common, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
            'discoveredCases': common['discoveredCases'], 'passed': len(union), 'shards': sorted(proof, key=lambda row: row['shardIndex']), '_documents': documents}


def write_trx(joined: dict, path: Path) -> None:
    ET.register_namespace('', NS)
    tag = lambda name: '{' + NS + '}' + name
    root = ET.Element(tag('TestRun'), {'id': str(uuid.uuid4()), 'name': 'BGCS.Tests exact eight-shard union'})
    results = ET.SubElement(root, tag('Results'))
    definitions = ET.SubElement(root, tag('TestDefinitions'))
    entries = ET.SubElement(root, tag('TestEntries'))
    execution_ids = set()
    for _, actual, declared, executed in joined['_documents']:
        for result in actual:
            _require(bool(result.get('executionId')) and result.get('executionId') not in execution_ids, 'Duplicate or missing TRX execution identity.')
            execution_ids.add(result.get('executionId'))
            results.append(copy.deepcopy(result))
        definitions.extend(copy.deepcopy(declared))
        entries.extend(copy.deepcopy(executed))
    summary = ET.SubElement(root, tag('ResultSummary'), {'outcome': 'Completed'})
    ET.SubElement(summary, tag('Counters'), {'total': str(joined['passed']), 'executed': str(joined['passed']), 'passed': str(joined['passed']), 'failed': '0', 'notExecuted': '0'})
    path.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)
