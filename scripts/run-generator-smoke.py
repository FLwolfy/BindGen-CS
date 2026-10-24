#!/usr/bin/env python3
"""Execute finite real generator baselines; this is not a full-platform certification."""
from __future__ import annotations
import argparse, json, os, platform, subprocess
from pathlib import Path
from acceptance_evidence import digest, source_identity, test_receipt
from compiler_test_environment import isolated_native_test_environment

ROOT = Path(__file__).resolve().parents[1]
FACTS = (
    'BGCS.Tests.PortableNativeSourceTests.CManifestKeepsTheParsersCSearchOrderWhenCppSdkHeadersWouldShadowIt',
    'BGCS.Tests.EmbeddedWorkerGenerationTests.FreshEmbeddedHost_GeneratesUsingRealWorkerWithoutLoadingTheClangFrontend',
    'BGCS.Tests.PrimaryFrontendCacheTests.ExplicitSdkRootsRejectUnlistedImplicitHeadersAndPreserveAdditiveInputsAndWarmReuse',
    'BGCS.Tests.NativeCustomFunctionTableTests.RuntimeOnlySlotAdapterCallsUnexportedFunctionsAndRetainsModuleUntilLastRealCallReturns',
    'BGCS.Tests.NativeStandardLoweringTests.StandardValues_ParseBridgeReparseCompileAndInvokeRealNativeOperations',
    'BGCS.Tests.NativeStandardLoweringTests.ExplicitClassAndFunctionTemplates_UseConcreteWorkerFactsAndRealNativeExports',
)

def required_cases(host):
    cases = {name: name for name in FACTS}
    for method in ('SameNamedHeadersUseVerifiedQualifiedIncludesAndRootOrderInvalidatesCache',
                   'UnmappedInputOrAbsoluteUserIncludeDoesNotReplacePublishedOutput'):
        name = 'BGCS.Tests.PortableNativeSourceTests.' + method
        cases[name] = name
    def theory(method, arguments):
        for argument in arguments: cases[method + '(' + argument + ')'] = method
    theory('BGCS.Tests.NativeAnonymousStorageProofTests.CppAnonymousContainerQualifiersRemainPartOfTheFrozenProof',
           ['volatileContainer: False', 'volatileContainer: True'])
    theory('BGCS.Tests.NativeAnonymousStorageProofTests.FrozenCopyTraitsStillRejectAnIncompatibleConsumer',
           ['trivial: False', 'trivial: True'])
    theory('BGCS.Tests.NativeAnonymousStorageProofTests.SourceNamesRespectTagAliasAndQualifiedScopes',
           ['language: C', 'language: Cpp'])
    theory('BGCS.Tests.NativeAnonymousStorageProofTests.AnonymousMembersUseStructuralProofsWithRealLeafIdentity',
           ['language: ' + language + ', system: ' + system for language in ('C', 'Cpp') for system in ('False', 'True')])
    theory('BGCS.Tests.NativeAnonymousStorageProofTests.StructuralProofRejectsEqualSizeTypeExtentQualifierOffsetAndPackingDrift',
           ['change: ' + str(change) for change in range(1, 7)])
    theory('BGCS.Tests.NativeAnonymousStorageProofTests.AnonymousDtoArgumentsResultsAndCallbacksInvokeWithoutNamingTheirStorage',
           ['mode: FunctionTable, sourceRuntime: False', 'mode: DllImport, sourceRuntime: False', 'mode: LibraryImport, sourceRuntime: True'])
    theory('BGCS.Tests.PortableNativeSourceTests.RelocatedSourcesCompileAndInvokeAfterOriginalInputsAreHidden',
           ['language: C, staticAdapter: False', 'language: Cpp, staticAdapter: False', 'language: Cpp, staticAdapter: True'])
    theory('BGCS.Tests.SiblingHeaderRelocationTests.RealWorkerGeneratesIdenticalArraysAndLayoutGuardsAfterCheckoutRelocation',
           ['mode: ' + mode for mode in ('FunctionTable', 'DllImport', 'LibraryImport')])
    theory('BGCS.Tests.NativeWideTextTests.CNativeCalls_PreserveUtf16Utf32AndTheActualWcharWidth',
           ['sourceRuntime: False', 'sourceRuntime: True'])
    theory('BGCS.Tests.NativeBridgeErrorAllocationTests.ErrorCapture_UsesItsOwnCAllocatorInsteadOfUserGlobalNewDelete',
           ['runtimeMode: "package"', 'runtimeMode: "source"'])
    theory('BGCS.Tests.NativeManagedEnumTests.GeneratedEnumsPreserveFullWidthNativeRoundTripsAndSeparateRawCarriers',
           ['mode: ' + mode + ', sourceRuntime: ' + runtime for mode in ('FunctionTable', 'DllImport', 'LibraryImport') for runtime in ('False', 'True')])
    if host == 'macos-x64':
        name = 'BGCS.Tests.NativeSdkStorageAccessibilityTests.RequiredSdkStoragePreservesAccessAndProvesOnlyNameableTypesWithoutLosingPrivateFacts'
        cases[name] = name
    elif host == 'linux-x64':
        theory('BGCS.Tests.NativeStorageAssertionTests.ContextRelativeSdkTemplateValuesRequireAnExternallyNameablePublicContract',
               ['exposure: "' + value + '"' for value in ('private-sdk', 'alias', 'signature', 'field', 'base', 'template-argument', 'qualified')])
    else: raise ValueError('Only the explicit Linux/macOS smoke hosts are allowed.')
    return cases

def verify_cases(receipt, host):
    expected = required_cases(host)
    rows = receipt['results']
    if (len(rows) != len(expected) or {row['displayName']: row['name'] for row in rows} != expected
            or any(row['outcome'] != 'Passed' for row in rows)):
        raise RuntimeError('The finite real generator smoke omitted, duplicated or changed a literal case.')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(); args.output.mkdir(parents=True, exist_ok=False)
    before = source_identity(ROOT)
    host = {'Darwin': 'macos-x64', 'Linux': 'linux-x64'}[platform.system()]
    assembly = ROOT / 'tests/BGCS.Tests/bin/Release/net10.0/BGCS.Tests.dll'
    assembly_hash = digest(assembly)
    selection = '|'.join('FullyQualifiedName=' + method for method in sorted(set(required_cases(host).values())))
    subprocess.run([args.dotnet, 'test', 'tests/BGCS.Tests/BGCS.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore',
        '-m:1', '-nr:false', '--filter', selection, '--results-directory', str(args.output.resolve()),
        '--logger', 'trx;LogFileName=smoke.trx', '--logger', 'console;verbosity=normal'], cwd=ROOT,
        env=isolated_native_test_environment(os.environ), check=True)
    receipt = test_receipt(args.output / 'smoke.trx')
    verify_cases(receipt, host)
    if source_identity(ROOT) != before or digest(assembly) != assembly_hash:
        raise RuntimeError('Source or compiled generator baseline changed during execution.')
    report = {'schemaVersion': 3, 'success': True, 'source': before, 'sourceBytesStable': True,
              'scope': 'generator-platform-smoke', 'status': 'passed', 'usesPrebuiltOutputs': False,
              'platform': host, 'fullFunctionalBaseline': False,
              'assemblySha256': assembly_hash, 'executedTests': [receipt], 'categories': []}
    (args.output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

if __name__ == '__main__': main()
