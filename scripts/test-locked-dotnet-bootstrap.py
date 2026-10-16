#!/usr/bin/env python3
"""Pure locked-archive bootstrap controls; no SDK downloads or builds run."""
from __future__ import annotations

import hashlib
import copy
from importlib.util import module_from_spec, spec_from_file_location
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = spec_from_file_location('locked_dotnet_bootstrap', Path(__file__).with_name('setup-locked-dotnet.py'))
bootstrap = module_from_spec(spec)
spec.loader.exec_module(bootstrap)
smoke_spec = spec_from_file_location('gnu_smoke', Path(__file__).with_name('verify-gnu-smoke.py'))
gnu_smoke = module_from_spec(smoke_spec)
smoke_spec.loader.exec_module(gnu_smoke)


class LockedDotnetBootstrapTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='bgcs-sdk-control-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.destination = self.root / 'installed'

    def archive(self, *, tar=False, extra=None):
        path = self.root / ('input.tar.gz' if tar else 'input.zip')
        files = {'dotnet' if tar else 'dotnet.exe': b'locked SDK host',
                 'sdk/10.0.401/sdk.marker': b'exact SDK',
                 'shared/Microsoft.NETCore.App/10.0.12/runtime.marker': b'exact runtime'}
        files.update(extra or {})
        if tar:
            with tarfile.open(path, 'w:gz') as output:
                for name, content in files.items():
                    entry = tarfile.TarInfo(name)
                    entry.size, entry.mode = len(content), 0o755 if name == 'dotnet' else 0o644
                    output.addfile(entry, io.BytesIO(content))
        else:
            with zipfile.ZipFile(path, 'w') as output:
                for name, content in files.items():
                    output.writestr(name, content)
        return path, {'url': 'https://example.invalid/' + path.name, 'sha512': hashlib.sha512(path.read_bytes()).hexdigest()}

    def install(self, archive, entry, verifier):
        with patch.object(bootstrap, 'download_archive', side_effect=lambda _, target: shutil.copyfile(archive, target)):
            bootstrap.install_sdk(self.destination, entry, verifier)

    def test_lock_selects_exact_sdk_archives_and_runtime_workload_versions(self):
        lock = json.loads((ROOT / 'eng/toolchains/toolchains.lock.json').read_text())
        workloads = json.loads((ROOT / 'eng/toolchains/workloads.lock.json').read_text())
        self.assertEqual('10.0.401', lock['sdkVersion'])
        self.assertEqual('10.0.12', lock['managedRuntimeVersion'])
        self.assertEqual(lock['sdkVersion'], workloads['sdkVersion'])
        self.assertEqual(lock['managedRuntimeVersion'], workloads['runtimePackVersion'])
        for rid in ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'):
            entry = lock['sdkArchives'][rid]
            self.assertEqual('https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-' + rid + ('.zip' if rid.startswith('win-') else '.tar.gz'), entry['url'])
            self.assertRegex(entry['sha512'], r'^[a-f0-9]{128}$')

    def test_zip_installs_only_archive_contents_after_staged_verification(self):
        archive, entry = self.archive()
        checked = []
        def verify(staging):
            self.assertFalse(self.destination.exists())
            self.assertEqual(['10.0.12'], [item.name for item in (staging / 'shared/Microsoft.NETCore.App').iterdir()])
            self.assertEqual(['10.0.401'], [item.name for item in (staging / 'sdk').iterdir()])
            checked.append(staging)
        self.install(archive, entry, verify)
        self.assertEqual(1, len(checked))
        self.assertFalse(checked[0].exists())
        self.assertEqual(b'locked SDK host', (self.destination / 'dotnet.exe').read_bytes())
        self.assertFalse(list(self.root.glob('.bgcs-sdk-*')))

    def test_tar_preserves_host_executable_mode_and_exact_runtime(self):
        archive, entry = self.archive(tar=True)
        self.install(archive, entry, lambda staging: self.assertTrue((staging / 'dotnet').is_file()))
        self.assertEqual(b'exact runtime', (self.destination / 'shared/Microsoft.NETCore.App/10.0.12/runtime.marker').read_bytes())
        if os.name != 'nt':
            self.assertTrue(os.access(self.destination / 'dotnet', os.X_OK))

    def test_bad_digest_is_rejected_before_extraction_or_verification(self):
        archive, entry = self.archive()
        entry['sha512'] = '0' * 128
        with patch.object(bootstrap, 'extract_archive') as extract, self.assertRaisesRegex(RuntimeError, 'SHA512'):
            self.install(archive, entry, lambda _: self.fail('Unverified archive reached runtime verification'))
        extract.assert_not_called()
        self.assertFalse(self.destination.exists())

    def test_existing_runtime_destination_is_never_merged_or_removed(self):
        stale = self.destination / 'shared/Microsoft.NETCore.App/10.0.13'
        stale.mkdir(parents=True)
        marker = stale / 'existing-runtime'
        marker.write_bytes(b'leave this input untouched')
        with patch.object(bootstrap, 'download_archive') as download, self.assertRaisesRegex(RuntimeError, 'cannot be merged'):
            bootstrap.install_sdk(self.destination, {})
        download.assert_not_called()
        self.assertEqual(b'leave this input untouched', marker.read_bytes())

    def test_runtime_verification_failure_publishes_no_installation(self):
        archive, entry = self.archive(extra={'shared/Microsoft.NETCore.App/10.0.13/runtime.marker': b'extra'})
        def reject(_):
            raise subprocess.CalledProcessError(1, ['verify-locked-toolchain.py'])
        with self.assertRaises(subprocess.CalledProcessError):
            self.install(archive, entry, reject)
        self.assertFalse(self.destination.exists())
        self.assertFalse(list(self.root.glob('.bgcs-sdk-*')))

    def test_archive_escape_is_rejected_without_writing_outside_staging(self):
        archive, entry = self.archive(extra={'../escape': b'bad'})
        with self.assertRaisesRegex(RuntimeError, 'escapes'):
            self.install(archive, entry, lambda _: self.fail('Unsafe archive was verified'))
        self.assertFalse((self.root / 'escape').exists())
        self.assertFalse(self.destination.exists())

    def test_bootstrap_invokes_unchanged_strict_verifier_with_exact_host(self):
        host = self.root / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
        host.write_bytes(b'fixture only')
        with patch.object(bootstrap.subprocess, 'run') as execute:
            bootstrap.verify_installation(self.root)
        arguments = execute.call_args.args[0]
        self.assertEqual([str(ROOT / 'scripts/verify-locked-toolchain.py'), '--dotnet', str(host)], arguments[1:])
        self.assertTrue(execute.call_args.kwargs['check'])

    def test_github_exports_select_exact_host_without_mutating_parent(self):
        env_file, path_file = self.root / 'environment', self.root / 'path'
        env_file.write_text('EXISTING=value\n')
        path_file.write_text('existing-path\n')
        with patch.dict(os.environ, {'GITHUB_ENV': str(env_file), 'GITHUB_PATH': str(path_file)}):
            before = dict(os.environ)
            bootstrap.export_environment(self.destination, 'win-x64')
            self.assertEqual(before, dict(os.environ))
        values = dict(line.split('=', 1) for line in env_file.read_text().splitlines())
        self.assertEqual('value', values['EXISTING'])
        self.assertEqual(str(self.destination.resolve()), values['DOTNET_ROOT'])
        self.assertEqual(values['DOTNET_ROOT'], values['DOTNET_ROOT_X64'])
        self.assertEqual(str(self.destination.resolve() / 'dotnet.exe'), values['DOTNET_HOST_PATH'])
        self.assertEqual('0', values['DOTNET_MULTILEVEL_LOOKUP'])
        self.assertEqual(['existing-path', str(self.destination.resolve())], path_file.read_text().splitlines())

    def test_all_workflow_bootstraps_use_python_then_exact_archive_then_strict_check(self):
        counts = {'ci.yml': 7, 'packages.yml': 1, 'publish-bgcs-runtime-nuget.yml': 1}
        for name, expected in counts.items():
            with self.subTest(workflow=name):
                workflow = (ROOT / '.github/workflows' / name).read_text()
                self.assertNotIn('actions/setup-dotnet@', workflow)
                jobs = re.split(r'^  [a-zA-Z0-9_-]+:\s*$', workflow, flags=re.MULTILINE)
                observed = 0
                for job in jobs:
                    if 'scripts/setup-locked-dotnet.py' not in job:
                        continue
                    self.assertEqual(1, job.count('scripts/setup-locked-dotnet.py'))
                    self.assertLess(job.index('actions/setup-python@'), job.index('scripts/setup-locked-dotnet.py'))
                    self.assertLess(job.index('scripts/setup-locked-dotnet.py'), job.index('scripts/verify-locked-toolchain.py'))
                    self.assertIn('--install-dir "${{ runner.temp }}/bgcs-dotnet"', job)
                    observed += 1
                self.assertEqual(expected, observed)

    def test_early_gnu_job_is_independent_locked_real_and_keeps_all_existing_gates(self):
        workflow = (ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8')
        blocks = dict(re.findall(r'^  ([a-zA-Z0-9_-]+):\s*\n(.*?)(?=^  [a-zA-Z0-9_-]+:\s*$|\Z)',
                                 workflow, re.MULTILINE | re.DOTALL))
        self.assertTrue({'wasm-invocation', 'native-aot-invocation', 'build-and-test', 'host-shards',
                         'host-completeness', 'acceptance'} <= set(blocks))
        job = blocks['gnu-compatibility-smoke']
        self.assertIn('runs-on: ubuntu-24.04', job)
        self.assertNotRegex(job, re.compile(r'^    (?:needs|if|continue-on-error):', re.MULTILINE))
        self.assertIn('BGCS_CPP2C_CXX: g++', job)
        self.assertIn('BGCS_CC: gcc', job)
        self.assertIn('BGCS_REQUIRE_GNU_SOURCE_VIEW: "1"', job)
        self.assertIn('gcc --version', job)
        self.assertIn('g++ --version', job)
        self.assertLess(job.index('scripts/setup-locked-dotnet.py'), job.index('dotnet restore tests/BGCS.Tests/'))
        self.assertIn('dotnet build tests/BGCS.Tests/BGCS.Tests.csproj', job)
        self.assertNotIn('dotnet build BindGen-CS.slnx', job)
        self.assertIn('scripts/compiler_test_environment.py -- dotnet test tests/BGCS.Tests/', job)
        self.assertIn('scripts/verify-gnu-smoke.py --print-filter', job)
        self.assertIn('--trx artifacts/gnu-smoke/gnu-smoke.trx', job)
        self.assertIn('if: always()', job)
        self.assertIn('path: artifacts/gnu-smoke', job)
        expression = gnu_smoke.filter_expression()
        self.assertEqual(6, len(gnu_smoke.METHODS))
        self.assertEqual(9, len(gnu_smoke.REQUIRED_CASES))
        self.assertIn('FullyQualifiedName=' + gnu_smoke.BRANCH + '&DisplayName~gnu', expression)
        for method in gnu_smoke.METHODS:
            self.assertIn('FullyQualifiedName=' + method, expression)
            class_name, member = method.removeprefix('BGCS.Tests.').split('.')
            self.assertIn('public async Task ' + member + '(',
                          (ROOT / 'tests/BGCS.Tests' / (class_name + '.cs')).read_text(encoding='utf-8'))
        gates = json.loads((ROOT / 'eng/acceptance/required-release-gates.json').read_text(encoding='utf-8'))
        self.assertTrue(gates['requireAllObservedJobsSuccessful'])

    def gnu_receipt(self):
        tree = ET.Element('TestRun')
        definitions, results = ET.SubElement(tree, 'TestDefinitions'), ET.SubElement(tree, 'Results')
        for index, (display, method) in enumerate(gnu_smoke.REQUIRED_CASES.items()):
            identifier = 'case-' + str(index)
            definition = ET.SubElement(definitions, 'UnitTest', id=identifier)
            class_name, member = method.rsplit('.', 1)
            ET.SubElement(definition, 'TestMethod', className=class_name, name=member)
            ET.SubElement(results, 'UnitTestResult', testId=identifier, testName=display, outcome='Passed')
        summary = ET.SubElement(tree, 'ResultSummary')
        ET.SubElement(summary, 'Counters', total='9', executed='9', passed='9', failed='0', error='0', timeout='0', aborted='0', notExecuted='0')
        return tree

    def test_early_gnu_trx_requires_exact_literals_fqns_and_successful_complete_execution(self):
        original = self.gnu_receipt()
        path = self.root / 'gnu.trx'
        ET.ElementTree(original).write(path, encoding='utf-8')
        verified = gnu_smoke.verify(path)
        self.assertEqual(9, len(verified['cases']))
        self.assertFalse(verified['releaseCertification'])
        for fault in ('missing', 'duplicate', 'skipped', 'foreign-branch', 'wrong-fqn', 'extra-definition', 'failed-counter', 'run-error'):
            with self.subTest(fault=fault):
                tree = copy.deepcopy(original)
                results = tree.find('Results')
                if fault == 'missing':
                    results.remove(results[0])
                elif fault == 'duplicate':
                    results.append(copy.deepcopy(results[0]))
                elif fault == 'skipped':
                    results[0].set('outcome', 'NotExecuted')
                elif fault == 'foreign-branch':
                    next(item for item in results if item.get('testName').startswith(gnu_smoke.BRANCH)).set('testName', gnu_smoke.BRANCH + '(family: "clang")')
                elif fault == 'wrong-fqn':
                    tree.find('TestDefinitions')[0].find('TestMethod').set('name', 'DifferentMethod')
                elif fault == 'extra-definition':
                    ET.SubElement(ET.SubElement(tree.find('TestDefinitions'), 'UnitTest', id='extra'), 'TestMethod', className='Unrelated', name='Passing')
                elif fault == 'failed-counter':
                    tree.find('ResultSummary/Counters').set('failed', '1')
                else:
                    ET.SubElement(tree, 'RunInfo', outcome='Error')
                ET.ElementTree(tree).write(path, encoding='utf-8')
                with self.assertRaises(RuntimeError):
                    gnu_smoke.verify(path)


if __name__ == '__main__':
    unittest.main()
