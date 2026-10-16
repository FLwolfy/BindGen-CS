#!/usr/bin/env python3
"""Raw host-specific workload-lock controls; no workload downloads or installs run."""
from __future__ import annotations

import copy
import hashlib
from importlib.util import module_from_spec, spec_from_file_location
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = spec_from_file_location('locked_toolchain_verifier', Path(__file__).with_name('verify-locked-toolchain.py'))
verifier = module_from_spec(spec)
spec.loader.exec_module(verifier)
HOSTS = ('win-x64', 'linux-x64', 'osx-x64')


class LockedWorkloadManifestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='workload-raw-lock-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.sdk = {'sdkVersion': '10.0.401', 'managedRuntimeVersion': '10.0.12',
                    'sdkArchives': {rid: {'url': 'https://example.invalid/sdk-' + rid, 'sha512': str(index) * 128}
                                    for index, rid in enumerate(HOSTS)}}
        manifest = {'version': '10.0.112', 'workloads': {'wasm-tools': {'packs': ['tool'], 'platforms': list(HOSTS)}},
                    'packs': {'tool': {'kind': 'Framework', 'version': '10.0.12',
                                       'alias-to': {rid: 'Actual.Tool.' + rid for rid in HOSTS}}}}
        unix = {'WorkloadManifest.json': (json.dumps(manifest, indent=2) + '\n').encode(),
                'WorkloadManifest.targets': b'<Project>\n</Project>\n'}
        self.bytes = {rid: {name: data.replace(b'\n', b'\r\n') if rid == 'win-x64' else data
                           for name, data in unix.items()} for rid in HOSTS}
        self.lock = {'schemaVersion': 2, 'sdkVersion': '10.0.401', 'runtimePackVersion': '10.0.12',
                     'requiredWorkload': 'wasm-tools', 'sourceSdkArchives': copy.deepcopy(self.sdk['sdkArchives']),
                     'manifests': [{'id': 'fixture', 'manifestFeatureBand': '10.0.100', 'version': '10.0.112',
                                    'filesByHost': {rid: {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
                                                    for rid, files in self.bytes.items()}}]}

    def install_fixture(self, rid):
        directory = self.root / rid
        manifest = directory / 'sdk-manifests/10.0.100/fixture/10.0.112'
        manifest.mkdir(parents=True)
        for name, data in self.bytes[rid].items():
            (manifest / name).write_bytes(data)
        (directory / 'packs' / ('Actual.Tool.' + rid) / '10.0.12').mkdir(parents=True)
        return directory, manifest

    def test_each_host_accepts_only_its_exact_raw_bytes_and_actual_pack(self):
        for rid in HOSTS:
            with self.subTest(host=rid):
                directory, _ = self.install_fixture(rid)
                verifier.verify_workload_archive_provenance(self.lock, self.sdk, rid)
                report = verifier.verify_workloads(directory, self.lock, rid)
                self.assertEqual([{'id': 'Actual.Tool.' + rid, 'version': '10.0.12'}], report['resolvedRequiredPacks'])
                self.assertEqual(set(self.lock['manifests'][0]['filesByHost'][rid].values()),
                                 {entry['sha256'] for entry in report['manifestInputs']})

    def test_equivalent_json_with_other_host_line_endings_is_rejected(self):
        for rid, other in (('win-x64', 'linux-x64'), ('linux-x64', 'win-x64'), ('osx-x64', 'win-x64')):
            with self.subTest(host=rid):
                directory, manifest = self.install_fixture(rid)
                self.assertEqual(json.loads(self.bytes[rid]['WorkloadManifest.json']), json.loads(self.bytes[other]['WorkloadManifest.json']))
                (manifest / 'WorkloadManifest.json').write_bytes(self.bytes[other]['WorkloadManifest.json'])
                with self.assertRaisesRegex(RuntimeError, 'differs from the tested lock'):
                    verifier.verify_workloads(directory, self.lock, rid)

    def test_both_manifest_files_reject_byte_tamper_and_missing_input(self):
        directory, manifest = self.install_fixture('linux-x64')
        for name, original in self.bytes['linux-x64'].items():
            with self.subTest(file=name):
                path = manifest / name
                path.write_bytes(original + b' ')
                with self.assertRaisesRegex(RuntimeError, 'differs from the tested lock'):
                    verifier.verify_workloads(directory, self.lock, 'linux-x64')
                path.unlink()
                with self.assertRaisesRegex(RuntimeError, 'differs from the tested lock'):
                    verifier.verify_workloads(directory, self.lock, 'linux-x64')
                path.write_bytes(original)

    def test_unrecorded_host_and_incomplete_file_lock_are_refused(self):
        directory, _ = self.install_fixture('linux-x64')
        with self.assertRaisesRegex(RuntimeError, 'no verified raw manifest bytes'):
            verifier.verify_workloads(directory, self.lock, 'linux-arm64')
        for mutation in ('host', 'file'):
            changed = copy.deepcopy(self.lock)
            files = changed['manifests'][0]['filesByHost']
            if mutation == 'host':
                del files['linux-x64']
            else:
                del files['linux-x64']['WorkloadManifest.targets']
            with self.subTest(mutation=mutation), self.assertRaisesRegex(RuntimeError, 'no raw file lock'):
                verifier.verify_workloads(directory, changed, 'linux-x64')

    def test_archive_identity_sdk_and_runtime_drift_are_refused(self):
        for field in ('url', 'sha512', 'sdkVersion', 'runtimePackVersion', 'host'):
            changed = copy.deepcopy(self.lock)
            if field in ('url', 'sha512'):
                changed['sourceSdkArchives']['linux-x64'][field] += 'changed'
            elif field == 'host':
                del changed['sourceSdkArchives']['linux-x64']
            else:
                changed[field] = 'unlocked-version'
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, 'archive provenance'):
                verifier.verify_workload_archive_provenance(changed, self.sdk, 'linux-x64')

    def test_expected_manifest_does_not_hide_missing_actual_workload_pack(self):
        directory, _ = self.install_fixture('osx-x64')
        (directory / 'packs/Actual.Tool.osx-x64/10.0.12').rmdir()
        with self.assertRaisesRegex(RuntimeError, 'required tested workload pack/version is absent'):
            verifier.verify_workloads(directory, self.lock, 'osx-x64')

    def test_checked_in_raw_hashes_bind_every_manifest_to_verified_sdk_archives(self):
        sdk = json.loads((ROOT / 'eng/toolchains/toolchains.lock.json').read_text())
        workloads = json.loads((ROOT / 'eng/toolchains/workloads.lock.json').read_text())
        self.assertEqual(set(HOSTS), set(workloads['sourceSdkArchives']))
        for rid in HOSTS:
            verifier.verify_workload_archive_provenance(workloads, sdk, rid)
        for entry in workloads['manifests']:
            self.assertEqual(set(HOSTS), set(entry['filesByHost']))
            for hashes in entry['filesByHost'].values():
                self.assertEqual({'WorkloadManifest.json', 'WorkloadManifest.targets'}, set(hashes))
                for digest in hashes.values():
                    self.assertRegex(digest, r'^[a-f0-9]{64}$')
            self.assertNotEqual(entry['filesByHost']['win-x64'], entry['filesByHost']['linux-x64'])
            self.assertEqual(entry['filesByHost']['linux-x64'], entry['filesByHost']['osx-x64'])


if __name__ == '__main__':
    unittest.main()
