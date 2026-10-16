#!/usr/bin/env python3
"""Exercise actual Git ignore matching, including case-insensitive source directories."""
from __future__ import annotations

from pathlib import Path
import subprocess
import tempfile
import unittest

from source_ownership import verify_source_ownership

ROOT = Path(__file__).resolve().parents[1]


class SourceOwnershipTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / 'artifacts/temporary'
        parent.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix='source-ownership-', dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        if not self.root.is_relative_to(ROOT):
            raise RuntimeError('The source ownership fixture escaped its owned workspace.')
        self.git('init', '--quiet')
        self.git('config', 'core.ignorecase', 'true')

    def git(self, *arguments):
        return subprocess.run(['git', *arguments], cwd=self.root, check=True, capture_output=True).stdout

    def write(self, name, value='fixture source'):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value, encoding='utf-8')
        return path

    def test_unanchored_artifacts_rule_reproduces_the_missing_contract_source(self):
        self.write('.gitignore', 'artifacts/\n')
        self.write('src/Contracts/Artifacts/ArtifactDescriptor.cs')
        with self.assertRaisesRegex(RuntimeError, 'src/Contracts/Artifacts/ArtifactDescriptor.cs'):
            verify_source_ownership(self.root)

    def test_anchored_rule_keeps_source_visible_without_adding_build_outputs(self):
        self.write('.gitignore', '/artifacts/\n[Bb]in/\n[Oo]bj/\n')
        self.write('src/Contracts/Artifacts/ArtifactDescriptor.cs')
        for name in ('artifacts/compiler-output.cs', 'src/Contracts/bin/Generated.cs', 'src/Contracts/obj/Generated.cs'):
            self.write(name)
        self.assertEqual(1, verify_source_ownership(self.root))
        self.assertIn(b'src/Contracts/Artifacts/ArtifactDescriptor.cs', self.git('ls-files', '--others', '--exclude-standard'))
        self.assertNotIn(b'Generated.cs', self.git('ls-files', '--others', '--exclude-standard'))

    def test_only_explicit_owned_generated_examples_are_excluded(self):
        self.write('.gitignore', 'Generated/\nGeneratedBridge/\n')
        self.write('src/Contracts/Port.cs')
        self.write('examples/NativeShim/Generated/Bindings.cs')
        self.write('examples/NativeShim/GeneratedBridge/src/bridge.cpp')
        self.write('examples/QuickStart/Generated/Bindings.cs')
        self.assertEqual(1, verify_source_ownership(self.root))
        self.write('tests/fixtures/Generated/RequiredFixture.cs')
        with self.assertRaisesRegex(RuntimeError, 'RequiredFixture.cs'):
            verify_source_ownership(self.root)

    def test_project_native_schema_and_tool_sources_cannot_be_hidden(self):
        self.write('.gitignore', 'Hidden/\n')
        expected = ('integrations/Hidden/Task.targets', 'distribution/Hidden/Product.nuspec',
                    'schemas/Hidden/bindgen.json', 'eng/Hidden/Tool.csproj', 'tests/Hidden/fixture.hpp',
                    'docs/Hidden/guide.md', 'scripts/Hidden/tool.py', '.github/Hidden/workflow.yml')
        for name in expected:
            self.write(name)
        with self.assertRaises(RuntimeError) as error:
            verify_source_ownership(self.root)
        for name in expected:
            self.assertIn(name, str(error.exception))

    def test_check_does_not_stage_or_mutate_visible_source(self):
        self.write('.gitignore', '/artifacts/\n*.user\n')
        source = self.write('src/Model.cs', 'exact original bytes')
        self.write('src/project.csproj.user')
        before = self.git('status', '--porcelain')
        self.assertEqual(1, verify_source_ownership(self.root))
        self.assertEqual(before, self.git('status', '--porcelain'))
        self.assertEqual('exact original bytes', source.read_text())


if __name__ == '__main__':
    unittest.main(verbosity=2)
