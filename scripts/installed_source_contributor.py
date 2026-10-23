"""Verify one delivered Contracts-only source plugin through installed SDK and CLI."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from acceptance_evidence import digest, publication, source_identity
from native_fixture_build import compile_shared
import platform

ROOT = Path(__file__).resolve().parents[1]

CHECK = 'installed-contracts-source-plugin-same-manifest-cpp-cache-and-rollback'
REJECTION = 'INSTALLED_SOURCE_CONTRIBUTION_REJECTED'


def configuration_schema() -> dict:
    return {'type': 'object', 'additionalProperties': False, 'properties': {
        'increment': {'type': 'integer', 'minimum': -1000, 'maximum': 1000}, 'invalidManaged': {'type': 'boolean'}}}


def _configuration(directory: Path, manifest: Path) -> dict:
    (directory / 'empty.hpp').write_text('#pragma once\n', encoding='utf-8')
    return {'schemaVersion': 2, 'library': {'id': 'installed_sources', 'nativeName': 'installed_sources'},
            'source': {'language': 'cpp', 'headers': ['empty.hpp'], 'parseMacros': False},
            'api': {'namespace': 'Package.SourceProtocol', 'name': 'NativeApi'},
            'runtime': {'mode': 'package'},
            'output': {'root': 'Generated', 'layout': 'singleFile', 'singleFileName': 'Complete.cs'},
            'cache': {'enabled': True, 'directory': str(directory / 'cache')},
            'extensions': {'plugins': [str(manifest)], 'options': {'fixture.source-plugin': {'values': {'increment': 12}}}}}


def _settings(configuration: dict, increment: int, invalid: bool = False) -> None:
    configuration['extensions']['options']['fixture.source-plugin']['values'] = {'increment': increment, 'invalidManaged': invalid}


def _write(path: Path, configuration: dict) -> None:
    path.write_text(json.dumps(configuration, indent=2) + '\n', encoding='utf-8')


def _snapshot(output: Path) -> dict:
    publication(output, 'installed_sources')
    return {path.relative_to(output).as_posix(): digest(path) for path in output.rglob('*') if path.is_file()}


def _inspect(output: Path, status: str, increment: int, input_directory: Path) -> tuple[list[Path], Path, dict]:
    manifest = publication(output, 'installed_sources')
    artifacts = manifest['artifacts']
    managed = [output / item['relativePath'] for item in artifacts if item['mediaType'] == 'text/x-csharp']
    if len(managed) != 1 or not managed[0].as_posix().endswith('/managed/Complete.cs'):
        raise RuntimeError('Installed source contributions did not compose into the exact configured single managed file.')
    for name in ('generated.h', 'generated.cpp', 'payload.txt'):
        matches = [item for item in artifacts if item['relativePath'].endswith('/contributions/fixture.source/helpers/' + name)]
        if len(matches) != 1:
            raise RuntimeError('Installed source contribution omitted its exact owned artifact: ' + name)
        if name == 'generated.cpp' and 'value + ' + str(increment) + ';' not in (output / matches[0]['relativePath']).read_text():
            raise RuntimeError('The changed contributor fingerprint did not replace the actual native implementation.')
    report = json.loads((output / 'generation.report.json').read_text())
    emissions = [row for row in report['cache'] if row['stage'] == 'csharpEmission']
    if len(emissions) != 1 or emissions[0]['status'] != status:
        raise RuntimeError('Installed source-contributor emission cache did not report the expected ' + status + ': ' + str(emissions))
    verification = [output / item['relativePath'] for item in artifacts if item['relativePath'].endswith('/metadata/verification.json')]
    if len(verification) != 1:
        raise RuntimeError('Installed source contribution has no exact target input evidence.')
    inputs = json.loads(verification[0].read_text())
    frozen = {name: copy.deepcopy(inputs[name]) for name in ('inputs', 'bridgeInputs', 'configurationInputs',
        'managedProjectionInputs', 'managedProjectionEnvironment', 'extensionInputs', 'plugins')}
    memberships = []
    def inspect_snapshots(value):
        if isinstance(value, dict):
            for directory in value.get('searchDirectories', []):
                if Path(directory['path']).resolve() == input_directory.resolve():
                    memberships.append(directory['digest']['value'])
                    directory['digest'] = None
            for child in value.values(): inspect_snapshots(child)
        elif isinstance(value, list):
            for child in value: inspect_snapshots(child)
    inspect_snapshots(frozen)
    if not memberships or len(set(memberships)) != 1:
        raise RuntimeError('Installed source-contributor source-root membership evidence is absent or contradictory.')
    other = hashlib.sha256(json.dumps(frozen, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return managed, output, {'emission': status, 'artifactCount': len(artifacts), 'manifestSha256': digest(output / '.bgcs-artifacts.json'),
                                'inputRootMembershipSha256': memberships[0], 'otherInputFactsSha256': other}



def _compile(source: Path, generated: Path, work: Path, evidence: Path, run, label: str) -> Path:
    name = 'installed_sources.dll' if platform.system() == 'Windows' else 'libinstalled_sources.dylib' if platform.system() == 'Darwin' else 'libinstalled_sources.so'
    return compile_shared(source, generated, work / 'external-native' / label / name, [], run, evidence, exports=('contributed_add',))


def _consumer(dotnet: str, version: str, directory: Path, managed: list[Path], binary: Path,
              expected: int, evidence: Path, run, write_project, label: str) -> None:
    items = ET.Element('ItemGroup')
    for path in managed:
        ET.SubElement(items, 'Compile', Include=str(path))
    write_project(directory, version, ['BGCS.Runtime'], ET.tostring(items, encoding='unicode'))
    (directory / 'Program.cs').write_text('''using System;
using Package.SourceProtocol;
using NativeApi api = NativeApi.Load(args[0]);
int expected = int.Parse(args[1], global::System.Globalization.CultureInfo.InvariantCulture);
if (api.ContributedAdd(29) != expected || ContributedHelper.Marker != expected)
    throw new InvalidOperationException("The installed source-contributor native/managed oracle disagreed.");
Console.WriteLine("Actual contributed C++ native and managed values: " + expected);
''', encoding='utf-8')
    run([dotnet, 'build', 'Consumer.csproj', '-c', 'Release', '-m:1', '-nr:false'], directory, evidence, label + '-consumer-build')
    run([dotnet, str(directory / 'bin/Release/net10.0/Consumer.dll'), str(binary), str(expected)], directory, evidence, label + '-consumer-invoke')


def _reject(arguments: list[str], directory: Path, evidence: Path, run, name: str) -> None:
    try:
        run(arguments, directory, evidence, name)
    except RuntimeError:
        log = evidence / (name + '.log')
        if not log.is_file() or REJECTION not in log.read_text(encoding='utf-8-sig', errors='replace'):
            raise RuntimeError('The expected installed contribution rejection did not report its exact managed-code fault.')
    else:
        raise RuntimeError('Invalid contributed managed code was accepted by the installed entry point.')


def prepare(dotnet: str, version: str, work: Path, evidence: Path, manifest: Path, run, write_project) -> dict:
    source_at_start = source_identity(ROOT)
    source = work / 'source-protocol-sdk'
    write_project(source, version, ['BGCS'])
    configuration = _configuration(source, manifest)
    path = source / 'bindgen.json'
    (source / 'Program.cs').write_text('''using System;
using System.Linq;
using BGCS;
await using BindingHost host = BindingGenerator.CreateDefault();
GenerationResult result = await host.GenerateAsync(GenerationRequest.FromFile("bindgen.json"));
if (!result.success) throw new InvalidOperationException(result.GetDiagnosticSummary());
if (!result.artifacts.Any(item => item.relativePath.EndsWith("/contributions/fixture.source/helpers/generated.cpp", StringComparison.Ordinal)))
    throw new InvalidOperationException("The installed SDK did not generate the manifest's contributed C++ source.");
''', encoding='utf-8')
    run([dotnet, 'build', 'Consumer.csproj', '-c', 'Release', '-m:1', '-nr:false'], source, evidence, 'source-protocol-sdk-build')
    command = [dotnet, str(source / 'bin/Release/net10.0/Consumer.dll')]
    phases = []
    for label, increment, status in (('cold', 12, 'miss'), ('warm', 12, 'hit'), ('changed', 13, 'miss')):
        _settings(configuration, increment); _write(path, configuration)
        run(command, source, evidence, 'source-protocol-sdk-' + label)
        managed, binary, proof = _inspect(source / 'Generated', status, increment, source)
        phases.append({'phase': label, **proof})
        if label == 'changed':
            binary = _compile(source, binary, work, evidence, run, 'source-sdk')
            _consumer(dotnet, version, work / 'source-protocol-sdk-consumer', managed, binary, 42, evidence, run, write_project, 'source-protocol-sdk')
    before = _snapshot(source / 'Generated')
    _settings(configuration, 13, True); _write(path, configuration)
    _reject(command, source, evidence, run, 'source-protocol-sdk-rejected')
    if _snapshot(source / 'Generated') != before:
        raise RuntimeError('Installed SDK replaced its complete prior publication after invalid source contribution.')
    state = {'manifest': manifest, 'manifestSha256': digest(manifest), 'source': source_at_start,
             'pluginAssets': json.loads(manifest.read_text())['assets'], 'entries': {
                 'sdk': {'language': 'cpp', 'nativeAndManagedInvoked': True, 'phases': phases, 'rejectionPreservedAllFiles': True}}}
    return state




def verify_cli(tool: Path, dotnet: str, version: str, work: Path, evidence: Path, state: dict, run, write_project) -> dict:
    source = work / 'source-protocol-cli'
    source.mkdir()
    configuration = _configuration(source, state['manifest'])
    path = source / 'bindgen.json'
    command = [str(tool), 'generate', 'bindgen.json', '--json']
    phases = []
    for label, increment, status in (('cold', 12, 'miss'), ('warm', 12, 'hit'), ('changed', 13, 'miss')):
        _settings(configuration, increment); _write(path, configuration)
        run(command, source, evidence, 'source-protocol-cli-' + label)
        managed, binary, proof = _inspect(source / 'Generated', status, increment, source)
        phases.append({'phase': label, **proof})
        if label == 'changed':
            binary = _compile(source, binary, work, evidence, run, 'source-cli')
            _consumer(dotnet, version, work / 'source-protocol-cli-consumer', managed, binary, 42, evidence, run, write_project, 'source-protocol-cli')
    before = _snapshot(source / 'Generated')
    _settings(configuration, 13, True); _write(path, configuration)
    _reject(command, source, evidence, run, 'source-protocol-cli-rejected')
    if _snapshot(source / 'Generated') != before:
        raise RuntimeError('Installed CLI replaced its complete prior publication after invalid contribution.')
    state['entries']['cli'] = {'language': 'cpp', 'nativeAndManagedInvoked': True, 'phases': phases, 'rejectionPreservedAllFiles': True}
    if digest(state['manifest']) != state['manifestSha256']:
        raise RuntimeError('The installed source manifest changed between SDK and CLI execution.')
    for asset in state['pluginAssets']:
        if digest(state['manifest'].parent / asset['path']) != asset['digest']['value']:
            raise RuntimeError('The installed Contracts-only plugin changed between entry points.')
    if source_identity(ROOT) != state['source']:
        raise RuntimeError('Source identity changed during the installed source-contributor scenario.')
    result = {'schemaVersion': 1, 'success': True, 'installedProducts': True, 'check': CHECK,
              'source': state['source'], 'manifestSha256': state['manifestSha256'], 'pluginAssets': state['pluginAssets'], 'entries': state['entries']}
    target = evidence / 'installed-source-contributor.json'
    _write(target, result)
    return {'receipt': target.name, 'receiptSha256': digest(target), 'manifestSha256': state['manifestSha256'],
            'pluginAssets': state['pluginAssets'], 'entries': copy.deepcopy(state['entries'])}
