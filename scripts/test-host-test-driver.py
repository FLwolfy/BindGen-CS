#!/usr/bin/env python3
"""Exercise the real structured runner with inherited, generic and negative xUnit controls."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import uuid

from test_case_evidence import _trx, validate_driver
from importlib.util import module_from_spec, spec_from_file_location

ROOT = Path(__file__).resolve().parents[1]
spec = spec_from_file_location('complete_host_runner', ROOT / 'scripts/run-host-tests.py')
runner = module_from_spec(spec)
spec.loader.exec_module(runner)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, default=Path(shutil.which('dotnet') or 'dotnet'))
    parser.add_argument('--configuration', choices=('Debug', 'Release'), default='Release')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/host-driver-controls' / uuid.uuid4().hex)
    args = parser.parse_args()
    dotnet = args.dotnet.resolve()
    env = runner.environment(dotnet)
    sdk, _, _ = runner.sdk_root(dotnet)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    driver = ROOT / 'eng/BGCS.TestDriver/bin' / args.configuration / 'net10.0/BGCS.TestDriver.dll'
    if not driver.is_file():
        raise RuntimeError('Build the isolated structured driver before running its controls.')
    project = ROOT / 'eng/BGCS.TestDriver.Fixtures/BGCS.TestDriver.Fixtures.csproj'
    checks = []
    for mode, define in (('complete', ''), ('dynamic', 'NEGATIVE_DYNAMIC'), ('skipped', 'NEGATIVE_SKIPPED')):
        target = output / mode
        subprocess.run([str(dotnet), 'build', str(project), '-c', args.configuration, '-m:1', '-nr:false',
                        '-p:TreatWarningsAsErrors=true', '-p:DefineConstants=' + define,
                        '-p:OutputPath=' + str(target / 'bin') + '/',
                        '-p:BaseIntermediateOutputPath=' + str(target / 'obj') + '/'], cwd=ROOT, env=env, check=True)
        assembly = target / 'bin/BGCS.Tests.dll'
        discovery = target / 'discovery.json'
        command = [str(dotnet), str(driver), '--assembly', str(assembly), '--sdk-root', str(sdk), '--output', str(discovery)]
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
        (target / 'discovery.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        if mode == 'dynamic':
            if result.returncode == 0 or discovery.exists() or 'not completely pre-enumerated' not in result.stderr:
                raise RuntimeError('The actual non-enumerated theory was not rejected with no successful receipt.')
            checks.append('collapsed-dynamic-theory-rejected')
            continue
        if result.returncode != 0:
            raise RuntimeError('Structured discovery control failed: ' + result.stderr)
        report = json.loads(discovery.read_text())
        validate_driver(report, False)
        if mode == 'complete':
            if report['discoveryTotal'] != 8 or len(report['compiledMethods']) != 5:
                raise RuntimeError('Inherited/generic/derived-attribute discovery lost or duplicated real cases.')
            methods = {item['fullyQualifiedName']: item for item in report['compiledMethods']}
            generic = methods['BGCS.TestDriverControls.GenericCases.GenericTheory']
            if not generic['generic'] or not generic['theory']:
                raise RuntimeError('The actual generic theory lost its compiled identity.')
        receipt = target / 'execution/driver.json'
        result = subprocess.run(command[:-1] + [str(receipt), '--execute', 'true'], cwd=ROOT, env=env, capture_output=True, text=True)
        receipt.parent.mkdir(parents=True, exist_ok=True)
        (receipt.parent / 'run.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        if mode == 'skipped':
            if result.returncode == 0 or receipt.exists() or 'failed, skipped, aborted' not in result.stderr:
                raise RuntimeError('The actual skipped required case was incorrectly certified.')
            checks.append('skipped-required-case-rejected')
        else:
            if result.returncode != 0:
                raise RuntimeError('The exact discovered argument objects could not be executed: ' + result.stderr)
            execution = json.loads(receipt.read_text(encoding='utf-8'))
            validate_driver(execution, True)
            _trx(receipt.parent / 'BGCS.Tests.trx', report['discoveredCases'])
        if mode == 'complete':
            checks.extend(('inherited-fact-and-theory-enumerated-and-executed', 'generic-theory-exact-objects-executed',
                           'literal-argument-identities-preserved-in-trx', 'derived-fact-attribute-enumerated'))
    (output / 'report.json').write_text(json.dumps({'schemaVersion': 1, 'success': True, 'scope': 'isolated-test-driver-controls',
                                                  'releaseReady': False, 'checks': checks}, indent=2) + '\n')
    print('Real structured discovery/execution controls passed:', len(checks))


if __name__ == '__main__':
    main()
