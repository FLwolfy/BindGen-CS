#!/usr/bin/env python3
"""Pure environment and child-process controls; no native tools or builds run."""
from __future__ import annotations

from importlib.util import module_from_spec, spec_from_file_location
import json
import os
from pathlib import Path
import subprocess
import sys
from types import MappingProxyType
import unittest
from unittest.mock import patch

from compiler_test_environment import NATIVE_INJECTION_VARIABLES, isolated_native_test_environment


SCRIPT = Path(__file__).with_name('compiler_test_environment.py')
spec = spec_from_file_location('host_shard_runner_environment', Path(__file__).with_name('run-host-test-shards.py'))
runner = module_from_spec(spec)
spec.loader.exec_module(runner)


class CompilerTestEnvironmentTests(unittest.TestCase):
    def test_removes_every_native_injection_without_mutating_parent(self):
        parent = {name: 'ambient-' + name for name in NATIVE_INJECTION_VARIABLES}
        parent['BGCS_CUSTOM_TEST_OPTION'] = '1'
        before = dict(parent)
        isolated = isolated_native_test_environment(MappingProxyType(parent), windows=False)
        self.assertEqual({'BGCS_CUSTOM_TEST_OPTION': '1'}, isolated)
        self.assertEqual(before, parent)
        isolated['BGCS_CUSTOM_TEST_OPTION'] = 'changed-child'
        self.assertEqual('1', parent['BGCS_CUSTOM_TEST_OPTION'])

    def test_windows_names_are_case_insensitive_and_posix_names_are_exact(self):
        parent = {'Include': 'sdk', 'cPaTh': 'headers', '_cL_': '/DAPPENDED=1',
                  'LiBrArY_pAtH': 'libs', 'Path': 'driver-path', 'include': 'custom-posix'}
        self.assertEqual({'Path': 'driver-path'}, isolated_native_test_environment(parent, windows=True))
        self.assertEqual(parent, isolated_native_test_environment(parent, windows=False))

    def test_preserves_discovery_runtime_and_security_inputs_exactly(self):
        preserved = {
            'PATH': 'compiler;runtime', 'CC': 'selected-cc', 'CXX': 'selected-cxx',
            'VCINSTALLDIR': 'vc', 'VCToolsInstallDir': 'tools', 'VSINSTALLDIR': 'vs',
            'WindowsSdkDir': 'sdk', 'WindowsSDKVersion': 'version', 'DEVELOPER_DIR': 'apple-tools',
            'DOTNET_ROOT': 'dotnet-root', 'DOTNET_HOST_PATH': 'exact-dotnet', 'DOTNET_CLI_UI_LANGUAGE': 'en',
            'DOTNET_DiagnosticPorts': 'diagnostic-endpoint', 'DOTNET_STARTUP_HOOKS': 'hook',
            'DOTNET_ADDITIONAL_DEPS': 'deps', 'CORECLR_ENABLE_PROFILING': '1', 'COR_PROFILER': 'profiler',
            'SOURCE_DATE_EPOCH': '1234', 'BGCS_CLANG_RUNTIME_DIR': 'parser', 'BGCS_REQUIRE_WASM_NATIVE': '1',
        }
        self.assertEqual(preserved, isolated_native_test_environment(preserved | {'INCLUDE': 'ambient'}, windows=True))

    def test_host_shard_selects_exact_dotnet_and_preserves_parent_environment(self):
        selected = Path('selected-sdk') / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
        parent = {'PATH': 'native-driver-path', 'DOTNET_ROOT': 'parent-sdk', 'DOTNET_HOST_PATH': 'parent-dotnet',
                  'DOTNET_STARTUP_HOOKS': 'retained-security-input', 'VCINSTALLDIR': 'discovery-selector',
                  'INCLUDE': 'untracked-header-path', 'CL': '/DUNTRACKED=1', 'Platform': 'x64'}
        with patch.dict(os.environ, parent, clear=True):
            before = dict(os.environ)
            isolated = runner.environment(selected)
            self.assertEqual(before, dict(os.environ))
        self.assertEqual(str(selected), isolated['DOTNET_HOST_PATH'])
        self.assertEqual(str(selected.parent), isolated['DOTNET_ROOT'])
        self.assertEqual(str(selected.parent) + os.pathsep + parent['PATH'], isolated['PATH'])
        self.assertEqual('retained-security-input', isolated['DOTNET_STARTUP_HOOKS'])
        self.assertEqual('discovery-selector', isolated['VCINSTALLDIR'])
        self.assertFalse(set(NATIVE_INJECTION_VARIABLES) & isolated.keys())
        self.assertNotIn('Platform', isolated)

    def test_wrapper_child_is_isolated_without_altering_argv_or_parent(self):
        parent = dict(os.environ)
        parent.update({name: 'untracked-native-input' for name in NATIVE_INJECTION_VARIABLES})
        preserved = {'CC': 'exact driver with spaces', 'CXX': 'exact cxx', 'VCINSTALLDIR': 'vc-discovery',
                     'DOTNET_ROOT': 'exact sdk with spaces', 'DOTNET_HOST_PATH': 'exact dotnet',
                     'DOTNET_DiagnosticPorts': 'preserved-diagnostic-input', 'DOTNET_STARTUP_HOOKS': 'preserved-hook'}
        parent.update(preserved)
        before = dict(parent)
        actual_parent = dict(os.environ)
        child = subprocess.run([sys.executable, str(SCRIPT), '--', sys.executable, '-c',
                                'import json,os,sys; print(json.dumps({"environment":dict(os.environ),"arguments":sys.argv[1:]}))',
                                'space argument', 'literal;$value', '--option=value'],
                               env=parent, capture_output=True, text=True, check=True)
        result = json.loads(child.stdout)
        self.assertEqual(['space argument', 'literal;$value', '--option=value'], result['arguments'])
        self.assertFalse(set(NATIVE_INJECTION_VARIABLES) & {name.upper() for name in result['environment']})
        child_environment = {name.upper() if os.name == 'nt' else name: value
                             for name, value in result['environment'].items()}
        for name, value in preserved.items():
            self.assertEqual(value, child_environment[name.upper() if os.name == 'nt' else name])
        self.assertEqual(parent['PATH'], child_environment['PATH'])
        self.assertEqual(before, parent)
        self.assertEqual(actual_parent, dict(os.environ))

    def test_wrapper_preserves_failure_exit_and_requires_command(self):
        failed = subprocess.run([sys.executable, str(SCRIPT), '--', sys.executable, '-c', 'raise SystemExit(7)'],
                                capture_output=True, text=True)
        self.assertEqual(7, failed.returncode)
        missing = subprocess.run([sys.executable, str(SCRIPT), '--'], capture_output=True, text=True)
        self.assertEqual(2, missing.returncode)
        self.assertIn('A test command is required', missing.stderr)


if __name__ == '__main__':
    unittest.main()
