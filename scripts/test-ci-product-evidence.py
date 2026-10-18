#!/usr/bin/env python3
"""Regress cross-revision workflow_run association and incomplete installed-product artifacts."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
import hashlib
from browser_lifecycle_evidence import verify_receipt
from cpp_acceptance_fixture import expected_lifecycle_checks
from source_contribution_evidence import CHECK as SOURCE_CHECK, PREREQUISITES as SOURCE_CHECKS, verify_report as verify_source_report
from installed_custom_target import CHECK as TARGET_CHECK

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('ci_product_evidence', ROOT / 'scripts/verify-ci-product-evidence.py')
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


def custom_target_evidence():
    """Synthetic parser-control data, never an actual installed consumer receipt."""
    return {"manifestSha256": "a" * 64, "configurationSha256": "b" * 64,
        "pluginSourceSha256": "c" * 64, "pluginReferences": ["BGCS.Contracts"],
        "pluginAssets": [{"file": name, "sha256": "d" * 64} for name in
            ("Package.CustomTarget.dll", "Package.CustomTarget.deps.json", "BGCS.Contracts.dll")],
        "entries": {entry: {"targetId": "example-private-host", "phases": [
            {"phase": phase, "sdkValue": sdk + (index >= 2), "profileValue": profile + (index == 3),
                "primaryFrontend": "hit" if index == 1 else "miss", "csharpEmission": "hit" if index == 1 else "miss",
                "sdkHeaderSha256": hashlib.sha256(str(sdk + (index >= 2)).encode()).hexdigest(),
                "profileSha256": hashlib.sha256(str(profile + (index == 3)).encode()).hexdigest(),
                "publicationSha256": "e" * 64}
            for index, phase in enumerate(("baseline", "warm", "sdkChanged", "profileChanged"))]}
            for entry, sdk, profile in (("sdk", 42, 1), ("cli", 44, 3))}}


def complete_receipt(*, applications=False):
    revision = 'a' * 40
    content = 'b' * 64
    version = '0.0.0-local.ci123'
    packages = [f'{name}.{version}.{extension}' for name in ('BGCS.Contracts', 'BGCS', 'BGCS.Runtime') for extension in ('nupkg', 'snupkg')]
    packages += [f'BindGen-CS.{rid}.{version}.nupkg' for rid in ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    packages += [f'BindGen-CS.{version}.nupkg', f'BGCS.ParserRuntime.osx-x64.{version}.nupkg']
    return {'schemaVersion': 1, 'success': True, 'sourceRevision': revision, 'sourceWorktreeDirty': False,
            'sourceBytesStable': True, 'completeProductSet': True, 'checkedOutSourceSha256': content,
            'hostRuntimeIdentifier': 'win-x64', 'version': version,
            'sourceAtCompletion': {'sourceRevision': revision, 'sourceWorktreeDirty': False, 'checkedOutSourceSha256': content},
            'packagingEvidence': {'sourceRevision': revision, 'sourceWorktreeDirty': False, 'sourceRevisionStable': True,
                'sourceBytesStable': True, 'checkedOutSourceSha256': content, 'completeProductSet': True,
                'physicalPackageDigestsVerified': True, 'packages': [{'file': name, 'bytes': 4096, 'sha256': 'c' * 64} for name in packages]},
            'featureMatrices': [{'name': name, 'requiredChecks': count, 'receipt': name + '/report.json', 'receiptSha256': '8' * 64,
                'executionIdentity': {**{field: '9' * 64 for field in ('executionClosureSha256', 'generatorEntrySha256', 'generatorAssemblySha256', 'runtimeAssemblySha256')}, 'executionAssetCount': 12},
                **({'nonblockingLifecycle': {'success': True, 'checks': sorted(expected_lifecycle_checks()),
                    'requiredCount': len(expected_lifecycle_checks())}} if name.startswith('installed-wasm-') else {})}
                for name, count in (VERIFIER.MATRICES.items() if applications else [])],
            'installedAdvancedWorkerPlugin': {'manifestSha256': 'e' * 64, 'parserIdentity': 'ClangSharp/20.1.2.4;LLVM/20',
                'pluginAssets': [{'file': name, 'sha256': 'f' * 64} for name in ('ClangFactPlugin.dll', 'ClangFactPlugin.deps.json')]},
            'installedSourceContributor': {'receipt': 'installed-source-contributor.json', 'receiptSha256': '6' * 64,
                'manifestSha256': '5' * 64, 'pluginAssets': [{'path': name, 'digest': {'value': '4' * 64}}
                    for name in ('Package.Plugin.dll', 'Package.Plugin.deps.json', 'BGCS.Contracts.dll')],
                'entries': {entry: {'language': 'cpp', 'nativeAndManagedInvoked': True, 'rejectionPreservedAllFiles': True,
                    'phases': [{'phase': phase, 'emission': status, 'artifactCount': 11, 'manifestSha256': str(index + 1) * 64,
                                'inputRootMembershipSha256': ('1' if index == 0 else '2') * 64,
                                'otherInputFactsSha256': '3' * 64}
                        for index, (phase, status) in enumerate((('cold', 'miss'), ('warm', 'hit'), ('changed', 'miss')))]}
                    for entry in ('sdk', 'cli')}},
            'installedCustomTarget': custom_target_evidence(),
            'checks': [*(VERIFIER.MATRICES if applications else {}), *VERIFIER.ADVANCED_CHECKS, *SOURCE_CHECKS, *VERIFIER.SDK_CONTENT_CHECKS, VERIFIER.COMPLETE_SOURCE_CHECK, TARGET_CHECK, 'installed-tool-all-unified-generation-commands']}


def complete_reports(receipt):
    reports = {}
    for matrix in receipt['featureMatrices']:
        aggregates = matrix['requiredChecks'] == 113
        report = {'success': True, 'installedProducts': True, 'sourceRevision': receipt['sourceRevision'],
                  'sourceWorktreeDirty': False, 'checkedOutSourceSha256': receipt['checkedOutSourceSha256'],
                  **matrix['executionIdentity'], 'invocation': {'success': True, 'pointerSize': 8 if aggregates else 4,
                    'checks': sorted(VERIFIER.expected_checks(aggregates=aggregates))},
                  'generatedAbiManifests': [{'mode': mode, 'nativeTargetId': 'windows-x64-msvc' if aggregates else 'emscripten-wasm32-emscripten',
                    'nativePointerSize': 8 if aggregates else 4, 'nativeByteOrder': 1, 'abiManifestSha256': '4' * 64} for mode in VERIFIER.MODES]}
        if aggregates:
            report['aggregateNativeComparisonPassed'] = True
            report['aggregateNativeOracle'] = {'success': True, 'checks': len(VERIFIER.AGGREGATE_CHECKS), 'signature': 'native oracle', 'probeSha256': '5' * 64}
            report['invocation']['aggregateSignatures'] = {mode: 'native oracle' for mode in VERIFIER.MODES}
        else:
            report['nonblockingLifecycle'] = matrix['nonblockingLifecycle']
            report['invocation']['lifecycleChecks'] = list(matrix['nonblockingLifecycle']['checks'])
            report['aot'] = '-aot-' in matrix['name']
            report['browserVersion'] = 'Fixture browser version'
        payload = json.dumps(report).encode('utf-8')
        matrix['receiptSha256'] = hashlib.sha256(payload).hexdigest()
        reports[matrix['receipt']] = payload
    summary = receipt['installedSourceContributor']
    source_report = {'schemaVersion': 1, 'success': True, 'installedProducts': True, 'check': SOURCE_CHECK,
        'source': {'revision': receipt['sourceRevision'], 'workingTreeDirty': False, 'checkedOutSourceSha256': receipt['checkedOutSourceSha256']},
        **{key: summary[key] for key in ('manifestSha256', 'pluginAssets', 'entries')}}
    payload = json.dumps(source_report).encode()
    summary['receiptSha256'] = hashlib.sha256(payload).hexdigest()
    reports[summary['receipt']] = payload
    return reports


class WorkflowProductEvidenceTests(unittest.TestCase):
    def test_custom_target_requires_both_installed_entry_points_and_changed_input_facts(self):
        for fault in ('marker', 'closure', 'reference', 'sdk', 'cli', 'phase', 'duplicate', 'warm',
                      'sdk-hit', 'profile-hit', 'stale-fact', 'header-unchanged', 'profile-unchanged'):
            receipt = complete_receipt()
            target = receipt['installedCustomTarget']
            phases = target['entries']['cli']['phases']
            if fault == 'marker': receipt['checks'].remove(TARGET_CHECK)
            elif fault == 'closure': target['pluginAssets'].pop()
            elif fault == 'reference': target['pluginReferences'].append('BGCS')
            elif fault in ('sdk', 'cli'): del target['entries'][fault]
            elif fault == 'phase': phases.pop()
            elif fault == 'duplicate': phases[-1] = phases[0]
            elif fault == 'warm': phases[1]['primaryFrontend'] = 'miss'
            elif fault == 'sdk-hit': phases[2]['primaryFrontend'] = 'hit'
            elif fault == 'profile-hit': phases[3]['primaryFrontend'] = 'hit'
            elif fault == 'stale-fact': phases[2]['sdkValue'] = phases[1]['sdkValue']
            elif fault == 'header-unchanged': phases[2]['sdkHeaderSha256'] = phases[1]['sdkHeaderSha256']
            else: phases[3]['profileSha256'] = phases[2]['profileSha256']
            with self.subTest(fault=fault), self.assertRaises(RuntimeError):
                VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_partial_dirty_source_contributor_proof_requires_explicit_local_opt_in(self):
        receipt = complete_receipt()
        receipt['completeProductSet'] = False
        receipt['sourceWorktreeDirty'] = True
        report = json.loads(complete_reports(receipt)['installed-source-contributor.json'])
        report['source']['workingTreeDirty'] = True
        verify_source_report(report, receipt, require_clean=False)
        with self.assertRaises(RuntimeError):
            verify_source_report(report, receipt)
        with self.assertRaises(RuntimeError):
            VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_partial_source_contributor_opt_in_cannot_relax_full_or_undeclared_scope(self):
        for scope in (True, None, 0, 'false'):
            receipt = complete_receipt()
            if scope is None:
                del receipt['completeProductSet']
            else:
                receipt['completeProductSet'] = scope
            report = json.loads(complete_reports(receipt)['installed-source-contributor.json'])
            with self.subTest(scope=scope), self.assertRaises(RuntimeError):
                verify_source_report(report, receipt, require_clean=False)

    def test_partial_source_contributor_proof_still_requires_exact_actual_source_identity(self):
        receipt = complete_receipt()
        receipt['completeProductSet'] = False
        receipt['sourceWorktreeDirty'] = True
        report = json.loads(complete_reports(receipt)['installed-source-contributor.json'])
        report['source']['workingTreeDirty'] = True
        for key, value in (('revision', 'd' * 40), ('checkedOutSourceSha256', 'e' * 64),
                           ('workingTreeDirty', False), ('workingTreeDirty', 1)):
            changed = json.loads(json.dumps(report))
            changed['source'][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                verify_source_report(changed, receipt, require_clean=False)
        for value in (None, 1, 'true'):
            receipt['sourceWorktreeDirty'] = value
            with self.subTest(parent_dirty=value), self.assertRaises(RuntimeError):
                verify_source_report(report, receipt, require_clean=False)
        receipt['sourceWorktreeDirty'] = True
        for parent_key, child_key, value in (('sourceRevision', 'revision', None),
                                            ('sourceRevision', 'revision', 'not-a-revision'),
                                            ('checkedOutSourceSha256', 'checkedOutSourceSha256', None),
                                            ('checkedOutSourceSha256', 'checkedOutSourceSha256', 'z' * 64)):
            changed_parent = json.loads(json.dumps(receipt))
            changed_report = json.loads(json.dumps(report))
            changed_parent[parent_key] = changed_report['source'][child_key] = value
            with self.subTest(parent_identity=parent_key, value=value), self.assertRaises(RuntimeError):
                verify_source_report(changed_report, changed_parent, require_clean=False)

    def test_partial_source_contributor_proof_does_not_relax_execution_or_summary_checks(self):
        receipt = complete_receipt()
        receipt['completeProductSet'] = False
        report = json.loads(complete_reports(receipt)['installed-source-contributor.json'])
        verify_source_report(report, receipt, require_clean=False)
        for key, value in (('success', False), ('installedProducts', False), ('manifestSha256', 'd' * 64)):
            changed = json.loads(json.dumps(report))
            changed[key] = value
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                verify_source_report(changed, receipt, require_clean=False)
        report['entries']['sdk']['phases'][1]['emission'] = 'miss'
        with self.assertRaises(RuntimeError):
            verify_source_report(report, receipt, require_clean=False)

    def test_complete_runtime_source_requires_its_actual_installed_consumption_check(self):
        receipt = complete_receipt()
        receipt['checks'].remove(VERIFIER.COMPLETE_SOURCE_CHECK)
        with self.assertRaisesRegex(RuntimeError, 'complete Runtime source'):
            VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_matching_clean_complete_artifact_is_accepted(self):
        VERIFIER.validate(complete_receipt(), 'a' * 40, 'win-x64')

    def test_green_run_metadata_does_not_accept_previous_source_receipt(self):
        with self.assertRaises(RuntimeError):
            VERIFIER.validate(complete_receipt(), 'd' * 40, 'win-x64')
        for location in ('packagingEvidence', 'sourceAtCompletion'):
            receipt = complete_receipt()
            receipt[location]['sourceRevision'] = 'd' * 40
            with self.subTest(location=location), self.assertRaises(RuntimeError):
                VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_dirty_or_changed_bytes_are_rejected(self):
        for location, key, value in ((None, 'sourceWorktreeDirty', True), (None, 'sourceBytesStable', False),
                                     ('sourceAtCompletion', 'sourceWorktreeDirty', True),
                                     ('sourceAtCompletion', 'checkedOutSourceSha256', 'd' * 64),
                                     ('packagingEvidence', 'checkedOutSourceSha256', 'd' * 64)):
            receipt = complete_receipt()
            (receipt if location is None else receipt[location])[key] = value
            with self.subTest(location=location, key=key), self.assertRaises(RuntimeError):
                VERIFIER.validate(receipt, 'a' * 40, 'win-x64')


    def test_previous_fixture_counts_do_not_certify_new_callback_and_alias_matrix(self):
        for native, browser in ((95, 56), (101, 62)):
            previous = {'installed-native-aot-full-abi-cpp-and-lifetime-matrix': native,
                        'installed-wasm-interpreter-full-import-matrix': browser,
                        'installed-wasm-aot-full-import-matrix': browser}
            for name, count in previous.items():
                receipt = complete_receipt(applications=True)
                next(matrix for matrix in receipt['featureMatrices'] if matrix['name'] == name)['requiredChecks'] = count
                with self.subTest(matrix=name, count=count), self.assertRaises(RuntimeError):
                    VERIFIER.validate(receipt, 'a' * 40, 'win-x64')


    def test_raw_browser_success_does_not_certify_missing_or_partial_lifecycle_protocols(self):
        for name in (name for name in VERIFIER.MATRICES if name.startswith('installed-wasm-')):
            for fault in ('missing', 'false', 'previous-count', 'duplicate', 'different-mode'):
                receipt = complete_receipt(applications=True)
                matrix = next(matrix for matrix in receipt['featureMatrices'] if matrix['name'] == name)
                lifecycle = matrix['nonblockingLifecycle']
                if fault == 'missing': del matrix['nonblockingLifecycle']
                elif fault == 'false': lifecycle['success'] = False
                elif fault == 'previous-count': lifecycle['requiredCount'] = 0
                elif fault == 'duplicate': lifecycle['checks'][-1] = lifecycle['checks'][0]
                else: lifecycle['checks'][-1] = 'UnknownImport:native-quiescence'
                with self.subTest(matrix=name, fault=fault), self.assertRaisesRegex(RuntimeError, 'lifecycle matrix'):
                    VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_installed_browser_subreport_must_match_actual_invocation(self):
        summary = {'success': True, 'checks': sorted(expected_lifecycle_checks()), 'requiredCount': len(expected_lifecycle_checks())}
        receipt = {'nonblockingLifecycle': summary, 'invocation': {'success': True, 'lifecycleChecks': list(summary['checks'])}}
        self.assertEqual(summary, verify_receipt(receipt))
        for fault in ('missing', 'false', 'duplicate', 'unmatched'):
            changed = json.loads(json.dumps(receipt))
            if fault == 'missing': del changed['invocation']['lifecycleChecks']
            elif fault == 'false': changed['invocation']['success'] = False
            elif fault == 'duplicate': changed['invocation']['lifecycleChecks'][-1] = changed['invocation']['lifecycleChecks'][0]
            else: changed['invocation']['lifecycleChecks'][-1] = 'UnknownImport:native-quiescence'
            with self.subTest(fault=fault), self.assertRaisesRegex(RuntimeError, 'subreport disagree'):
                verify_receipt(changed)

    def test_each_matrix_requires_receipt_hash_and_the_same_complete_execution_closure(self):
        for fault in ('receipt-hash', 'missing-closure', 'runtime-drift', 'tool-drift', 'asset-count'):
            receipt = complete_receipt(applications=True)
            matrix = receipt['featureMatrices'][-1]
            if fault == 'receipt-hash': matrix['receiptSha256'] = ''
            elif fault == 'missing-closure': del matrix['executionIdentity']
            elif fault == 'runtime-drift': matrix['executionIdentity']['runtimeAssemblySha256'] = '7' * 64
            elif fault == 'tool-drift': matrix['executionIdentity']['executionClosureSha256'] = '7' * 64
            else: matrix['executionIdentity']['executionAssetCount'] = 0
            with self.subTest(fault=fault), self.assertRaises(RuntimeError):
                VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_previous_or_partial_advanced_plugin_receipt_is_rejected(self):
        for missing in ('marker', 'manifest-digest', 'plugin-dependency'):
            receipt = complete_receipt()
            if missing == 'marker': receipt['checks'].remove(VERIFIER.ADVANCED_CHECKS[1])
            elif missing == 'manifest-digest': receipt['installedAdvancedWorkerPlugin']['manifestSha256'] = ''
            else: receipt['installedAdvancedWorkerPlugin']['pluginAssets'].pop()
            with self.subTest(missing=missing), self.assertRaisesRegex(RuntimeError, 'advanced worker plugin'):
                VERIFIER.validate(receipt, 'a' * 40, 'win-x64')

    def test_source_contribution_requires_all_entry_points_native_calls_cache_and_rollback(self):
        for fault in ('missing', 'check', 'entry', 'native', 'rollback', 'cache', 'phase', 'digest', 'contract-asset', 'unchanged-content'):
            receipt = complete_receipt()
            value = receipt['installedSourceContributor']
            if fault == 'missing': del receipt['installedSourceContributor']
            elif fault == 'check': receipt['checks'].remove(SOURCE_CHECK)
            elif fault == 'entry': del value['entries']['sdk']
            elif fault == 'native': value['entries']['cli']['nativeAndManagedInvoked'] = False
            elif fault == 'rollback': value['entries']['cli']['rejectionPreservedAllFiles'] = False
            elif fault == 'cache': value['entries']['sdk']['phases'][1]['emission'] = 'miss'
            elif fault == 'phase': value['entries']['sdk']['phases'].pop()
            elif fault == 'digest': value['receiptSha256'] = ''
            elif fault == 'contract-asset': value['pluginAssets'].pop()
            else: value['entries']['sdk']['phases'][2]['manifestSha256'] = value['entries']['sdk']['phases'][1]['manifestSha256']
            with self.subTest(fault=fault), self.assertRaises(RuntimeError): VERIFIER.validate(receipt, 'a' * 40, 'win-x64')


    def test_actual_source_contribution_subreport_is_mandatory_hashed_and_same_revision(self):
        parent = ROOT / 'artifacts/temporary'
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='source-product-report-', dir=parent) as directory:
            path = Path(directory) / 'evidence.zip'
            for fault in ('missing', 'hash', 'source', 'manifest', 'execution'):
                receipt = complete_receipt()
                reports = complete_reports(receipt)
                summary = receipt['installedSourceContributor']
                name = summary['receipt']
                if fault == 'missing': del reports[name]
                elif fault == 'hash': reports[name] += b' '
                else:
                    report = json.loads(reports[name])
                    if fault == 'source': report['source']['revision'] = 'd' * 40
                    elif fault == 'manifest': report['manifestSha256'] = 'd' * 64
                    else: report['entries']['cli']['nativeAndManagedInvoked'] = False
                    reports[name] = json.dumps(report).encode()
                    summary['receiptSha256'] = hashlib.sha256(reports[name]).hexdigest()
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr('verification.json', json.dumps(receipt))
                    for name, payload in reports.items(): archive.writestr(name, payload)
                with self.subTest(fault=fault), self.assertRaises(RuntimeError): VERIFIER.read_archive(path)

    def test_archive_requires_one_exact_bounded_receipt_without_extracting_files(self):
        parent = ROOT / 'artifacts/temporary'
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT): raise RuntimeError('Temporary directory escapes repository')
        with tempfile.TemporaryDirectory(prefix='ci-product-artifacts-', dir=parent) as directory:
            path = Path(directory) / 'evidence.zip'
            receipt = complete_receipt()
            reports = complete_reports(receipt)
            with zipfile.ZipFile(path, 'w') as archive:
                archive.writestr('verification.json', json.dumps(receipt))
                for name, payload in reports.items(): archive.writestr(name, payload)
                archive.writestr('../ignored-untrusted-file', 'never extracted')
            VERIFIER.validate(VERIFIER.read_archive(path), 'a' * 40, 'win-x64')
            self.assertFalse((parent / 'ignored-untrusted-file').exists())
            with zipfile.ZipFile(path, 'w') as archive: archive.writestr('nested/verification.json', '{}')
            with self.assertRaises(RuntimeError): VERIFIER.read_archive(path)

    def test_archived_reports_cannot_be_missing_tampered_or_claim_previous_protocols(self):
        parent = ROOT / 'artifacts/temporary'
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='ci-product-report-', dir=parent) as directory:
            path = Path(directory) / 'evidence.zip'
            for fault in ('missing', 'wrong-hash', 'old-raw', 'old-lifecycle', 'wrong-aot', 'wrong-abi', 'no-oracle', 'runtime-drift'):
                receipt = complete_receipt(applications=True)
                reports = complete_reports(receipt)
                matrix = receipt['featureMatrices'][0 if fault == 'no-oracle' else -1]
                report = json.loads(reports[matrix['receipt']])
                if fault == 'missing': del reports[matrix['receipt']]
                elif fault == 'wrong-hash': reports[matrix['receipt']] += b' '
                else:
                    if fault == 'old-raw': report['invocation']['checks'].pop()
                    elif fault == 'old-lifecycle': del report['nonblockingLifecycle']
                    elif fault == 'wrong-aot': report['aot'] = False
                    elif fault == 'wrong-abi': report['generatedAbiManifests'][0]['nativePointerSize'] = 8
                    elif fault == 'no-oracle': report['aggregateNativeOracle']['signature'] = 'different'
                    else: report['runtimeAssemblySha256'] = '0' * 64
                    reports[matrix['receipt']] = json.dumps(report).encode('utf-8')
                    matrix['receiptSha256'] = hashlib.sha256(reports[matrix['receipt']]).hexdigest()
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr('verification.json', json.dumps(receipt))
                    for name, payload in reports.items(): archive.writestr(name, payload)
                with self.subTest(fault=fault), self.assertRaises(RuntimeError): VERIFIER.read_archive(path)
            with zipfile.ZipFile(path, 'w') as archive: archive.writestr('verification.json', ' ' * (1024 ** 2 + 1))
            with self.assertRaises(RuntimeError): VERIFIER.read_archive(path)


if __name__ == '__main__':
    unittest.main(verbosity=2)
