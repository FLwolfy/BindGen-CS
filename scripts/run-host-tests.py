#!/usr/bin/env python3
"""Run every Host case once against a freshly pinned local execution closure."""
from __future__ import annotations
import argparse, hashlib, json, os, platform, shutil, subprocess
from pathlib import Path
from acceptance_evidence import source_identity, test_receipt
from compiler_test_environment import isolated_native_test_environment
from test_case_evidence import digest, validate_driver, _trx
ROOT=Path(__file__).resolve().parents[1]
SDK_FILES=('vstest.console.dll','vstest.console.deps.json','vstest.console.runtimeconfig.json',
           'Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll','Microsoft.VisualStudio.TestPlatform.ObjectModel.dll')
def environment(dotnet: Path) -> dict:
    result = isolated_native_test_environment(os.environ)
    result.update(DOTNET_ROOT=str(dotnet.parent), DOTNET_HOST_PATH=str(dotnet), DOTNET_CLI_UI_LANGUAGE='en-US', VSTEST_CONNECTION_TIMEOUT='30')
    result['PATH'] = str(dotnet.parent) + os.pathsep + result.get('PATH', '')
    for name in ('Platform', 'PLATFORM', 'platform'):
        result.pop(name, None)
    return result


def sdk_root(dotnet: Path) -> tuple[Path, str, str]:
    env = environment(dotnet)
    version = subprocess.check_output([str(dotnet), '--version'], cwd=ROOT, env=env, text=True).strip()
    runtimes = subprocess.check_output([str(dotnet), '--list-runtimes'], cwd=ROOT, env=env, text=True)
    versions = {line.split()[1] for line in runtimes.splitlines() if line.startswith('Microsoft.NETCore.App 10.')}
    lock = json.loads((ROOT / 'eng/toolchains/toolchains.lock.json').read_text())
    if version != lock['sdkVersion'] or versions != {lock['managedRuntimeVersion']}:
        raise RuntimeError('Use exactly the isolated locked SDK/runtime for the Host baseline.')
    sdk = dotnet.parent / 'sdk' / version
    if not sdk.is_dir():
        raise RuntimeError('The selected host has no exact locked SDK directory.')
    return sdk, version, next(iter(versions))


def components(configuration: str) -> dict[str, Path]:
    suffix = Path('bin') / configuration / 'net10.0'
    result = {'host': ROOT / 'tests/BGCS.Tests' / suffix, 'worker': ROOT / 'src/BGCS.Frontend.Clang.Worker' / suffix,
            'driver': ROOT / 'eng/BGCS.TestDriver' / suffix,
            **{'fixture/' + name: ROOT / 'tests/fixtures' / kind / name / suffix for kind, name in
               (('plugins', 'NeutralLoweringPlugin'), ('plugins', 'ClangFactPlugin'), ('hosts', 'EmbeddedSdkHost'))}}
    parser = os.environ.get('BGCS_CLANG_RUNTIME_DIR')
    if parser:
        directory = Path(parser).resolve()
        if not directory.is_relative_to(ROOT.resolve()):
            raise RuntimeError('The parser override must be an owned checkout artifact before it can enter the frozen execution closure.')
        result['parser'] = directory
        result['parser-tools'] = ROOT / 'artifacts/clang-runtime/bin'
    return result


def closure(configuration: str, dotnet: Path) -> tuple[dict, dict[str, Path]]:
    sdk, version, runtime = sdk_root(dotnet)
    entries = {}
    for prefix, directory in components(configuration).items():
        if not directory.is_dir():
            raise RuntimeError('Required closed execution component is missing: ' + str(directory))
        for path in directory.rglob('*'):
            if path.is_symlink():
                raise RuntimeError('Execution closure symlinks require explicit deployment and are not accepted: ' + str(path))
            if path.is_file():
                entries[prefix + '/' + path.relative_to(directory).as_posix()] = path
    for name in SDK_FILES:
        entries['sdk/' + name] = sdk / name
    entries['sdk/dotnet-host'] = dotnet
    entries['sdk/System.Private.CoreLib.dll'] = dotnet.parent / 'shared/Microsoft.NETCore.App' / runtime / 'System.Private.CoreLib.dll'
    files = {name: digest(path) for name, path in sorted(entries.items())}
    system = platform.system()
    machine = platform.machine().lower()
    if machine not in ('amd64', 'x86_64'):
        raise RuntimeError('This Host baseline is for launch x64 hosts.')
    host = {'Windows': 'windows-x64', 'Linux': 'linux-x64', 'Darwin': 'macos-x64'}[system]
    identity = {'schemaVersion': 1, 'source': source_identity(ROOT), 'host': host, 'sdkVersion': version, 'runtimeVersion': runtime,
                'ci': os.environ.get('GITHUB_ACTIONS') == 'true',
                'runnerImage': {'os': os.environ.get('ImageOS', system), 'version': os.environ.get('ImageVersion', platform.release())},
                'assemblySha256': files['host/BGCS.Tests.dll'], 'executionClosure': files}
    return identity, entries


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',type=Path,required=True)
    parser.add_argument('--configuration',default='Release',choices=('Release','Debug'))
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args(); args.dotnet=args.dotnet.resolve(); output=args.output.resolve()
    output.mkdir(parents=True,exist_ok=True)
    before,_=closure(args.configuration,args.dotnet)
    sdk,_,_=sdk_root(args.dotnet); roots=components(args.configuration)
    report=output/'BGCS.Tests.discovery.json'
    command=[str(args.dotnet),str(roots['driver']/'BGCS.TestDriver.dll'),'--assembly',str(roots['host']/'BGCS.Tests.dll'),
             '--sdk-root',str(sdk),'--output',str(report),'--execute','true']
    subprocess.run(command,cwd=ROOT,env=environment(args.dotnet),check=True)
    document=json.loads(report.read_text(encoding='utf-8')); validate_driver(document,True)
    trx=output/'BGCS.Tests.trx'; _trx(trx,document['discoveredCases'])
    after,_=closure(args.configuration,args.dotnet)
    if after!=before: raise RuntimeError('Source, SDK or complete execution bytes changed during the all-case baseline.')
    receipt={'schemaVersion':1,'success':True,'certifying':not before['source']['workingTreeDirty'],
             'identity':before,'passed':document['discoveryTotal'],'discoverySha256':digest(report),'trxSha256':digest(trx)}
    (output/'BGCS.Tests.complete.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    (output/'BGCS.Tests.json').write_text(json.dumps(test_receipt(trx),indent=2)+'\n',encoding='utf-8')
    print('Verified every Host case exactly once:',document['discoveryTotal'])
if __name__=='__main__': main()
