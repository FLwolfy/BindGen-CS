#!/usr/bin/env python3
"""Use a real isolated Git source to reject changed accepted release tooling."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
import release_payload as payload

class ReleasePayloadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.tools = self.root / 'artifacts/tools'; self.tools.mkdir(parents=True)
        self.hooks = self.root / 'empty-hooks'; self.hooks.mkdir()
        (self.root / '.gitignore').write_text('artifacts/\n', encoding='utf-8')
        (self.root / 'source.txt').write_text('reviewed source\n', encoding='utf-8')
        self.git('init', '--quiet'); self.git('add', '.')
        self.git('-c','user.name=BGCS Controls','-c','user.email=controls@example.invalid','commit','--quiet','-m','test(release): establish isolated source')
        self.revision = self.git('rev-parse', 'HEAD').strip()
        self.old_root = payload.ROOT; payload.ROOT = self.root; self.addCleanup(setattr, payload, 'ROOT', self.old_root)
        (self.tools / 'BGCS.RepositoryTools.dll').write_bytes(b'synthetic test executable bytes')
        (self.tools / 'BGCS.Core.dll').write_bytes(b'synthetic exact dependency bytes')

    def git(self, *args):
        return subprocess.check_output(['git', '-c', 'core.hooksPath=' + str(self.hooks), *args], cwd=self.root, text=True, stderr=subprocess.DEVNULL)

    def test_exact_clean_tooling_roundtrip_does_not_reseal_or_accept_another_revision(self):
        first = payload.tooling(self.tools, self.revision, seal=True)
        self.assertEqual(first, payload.tooling(self.tools, self.revision))
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, self.revision, seal=True)
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, 'd' * 40)

    def test_missing_extra_changed_dependency_and_nested_receipt_are_rejected(self):
        payload.tooling(self.tools, self.revision, seal=True)
        path = self.tools / 'BGCS.Core.dll'; content = path.read_bytes()
        path.write_bytes(b'changed')
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, self.revision)
        path.unlink()
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, self.revision)
        path.write_bytes(content); extra = self.tools / 'stale.dll'; extra.write_bytes(b'extra')
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, self.revision)
        extra.unlink(); nested = self.tools / 'nested/release-tooling.json'; nested.parent.mkdir(); nested.write_text('{}', encoding='utf-8')
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, self.revision)

    def test_changed_real_source_cannot_reuse_an_accepted_tooling_seal(self):
        payload.tooling(self.tools, self.revision, seal=True)
        (self.root / 'source.txt').write_text('changed source\n', encoding='utf-8')
        with self.assertRaises(RuntimeError): payload.tooling(self.tools, self.revision)

if __name__ == '__main__': unittest.main(verbosity=2)
