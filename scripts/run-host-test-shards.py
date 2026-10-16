#!/usr/bin/env python3
"""Prepare a closed Host test asset, execute one exact class shard, or verify the complete union."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import stat
import subprocess
import zipfile

from acceptance_evidence import source_identity, test_receipt
from compiler_test_environment import isolated_native_test_environment
from host_test_shards import SHARDS, digest, join, validate_driver, validate_identity, write_trx

ROOT = Path(__file__).resolve().parents[1]
SDK_FILES = ('vstest.console.dll', 'vstest.console.deps.json', 'vstest.console.runtimeconfig.json',
             'Microsoft.TestPlatform.VsTestConsole.TranslationLayer.dll', 'Microsoft.VisualStudio.TestPlatform.ObjectModel.dll')


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
        raise RuntimeError('Use exactly the isolated locked SDK/runtime for all Host shards.')
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
        raise RuntimeError('This mandatory Host shard matrix is for launch x64 hosts.')
    host = {'Windows': 'windows-x64', 'Linux': 'linux-x64', 'Darwin': 'macos-x64'}[system]
    identity = {'schemaVersion': 1, 'source': source_identity(ROOT), 'host': host, 'sdkVersion': version, 'runtimeVersion': runtime,
                'ci': os.environ.get('GITHUB_ACTIONS') == 'true',
                'runnerImage': {'os': os.environ.get('ImageOS', system), 'version': os.environ.get('ImageVersion', platform.release())},
                'assemblySha256': files['host/BGCS.Tests.dll'], 'executionClosure': files}
    return identity, entries


def prepare(args) -> None:
    identity, entries = closure(args.configuration, args.dotnet)
    validate_identity(identity, allow_dirty=args.allow_dirty_local)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    manifest = {'schemaVersion': 1, 'identity': identity, 'configuration': args.configuration,
                'files': [{'path': path.relative_to(ROOT).as_posix(), 'component': name, 'sha256': identity['executionClosure'][name]}
                          for name, path in entries.items() if not name.startswith('sdk/')]}
    archive = output / 'execution.zip'
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as target:
        for entry in manifest['files']:
            target.write(ROOT / entry['path'], entry['path'])
        target.writestr('host-execution.manifest.json', json.dumps(manifest, indent=2) + '\n')
    (output / 'asset.json').write_text(json.dumps({'schemaVersion': 1, 'archiveSha256': digest(archive), 'identity': identity}, indent=2) + '\n')
    after, _ = closure(args.configuration, args.dotnet)
    if after != identity:
        raise RuntimeError('Source or compiled execution closure changed while preparing the asset.')
    print('Prepared exact Host execution closure:', len(entries), 'files,', archive.stat().st_size, 'bytes.')


def validate_asset_manifest(descriptor: dict, manifest: dict, configuration: str) -> dict:
    if (set(descriptor) != {'schemaVersion', 'archiveSha256', 'identity'} or type(descriptor.get('schemaVersion')) is not int
            or descriptor['schemaVersion'] != 1 or set(manifest) != {'schemaVersion', 'identity', 'configuration', 'files'}
            or type(manifest.get('schemaVersion')) is not int or manifest['schemaVersion'] != 1
            or manifest.get('identity') != descriptor['identity'] or manifest.get('configuration') != configuration
            or configuration not in ('Release', 'Debug')):
        raise RuntimeError('Execution manifest schema/identity/configuration differs from its accepted asset.')
    files = manifest.get('files')
    if not isinstance(files, list) or not files or len(files) > 20000:
        raise RuntimeError('The execution asset file collection is empty or exceeds its finite bound.')
    expected = {}
    components_seen = set()
    closure = descriptor['identity']['executionClosure']
    for entry in files:
        if not isinstance(entry, dict) or set(entry) != {'path', 'component', 'sha256'}:
            raise RuntimeError('Unknown execution asset file schema.')
        name, component = entry['path'], entry['component']
        for value in (name, component):
            if (not isinstance(value, str) or not value or '\\' in value or ':' in value
                    or any(part in ('', '.', '..') for part in value.split('/'))):
                raise RuntimeError('An execution asset path is not a portable confined relative path.')
        if name in expected or component in components_seen or component.startswith('sdk/') or closure.get(component) != entry['sha256']:
            raise RuntimeError('The execution manifest duplicates, adds or changes an accepted component.')
        expected[name] = entry
        components_seen.add(component)
    if components_seen != {name for name in closure if not name.startswith('sdk/')}:
        raise RuntimeError('The execution manifest omits a frozen non-framework component.')
    return expected


def restore(asset: Path, dotnet: Path, configuration: str, allow_dirty: bool) -> dict:
    if (asset / 'asset.json').stat().st_size > 16 * 1024**2:
        raise RuntimeError('The execution asset descriptor exceeds its finite input bound.')
    descriptor = json.loads((asset / 'asset.json').read_text())
    archive = asset / 'execution.zip'
    if descriptor.get('schemaVersion') != 1 or digest(archive) != descriptor.get('archiveSha256'):
        raise RuntimeError('The accepted build asset archive changed.')
    validate_identity(descriptor['identity'], allow_dirty=allow_dirty)
    with zipfile.ZipFile(archive) as source:
        if len(source.namelist()) != len(set(source.namelist())):
            raise RuntimeError('The execution asset has duplicate members.')
        if source.getinfo('host-execution.manifest.json').file_size > 16 * 1024**2:
            raise RuntimeError('The execution asset manifest exceeds its finite input bound.')
        manifest = json.loads(source.read('host-execution.manifest.json'))
        expected = validate_asset_manifest(descriptor, manifest, configuration)
        if set(source.namelist()) != set(expected) | {'host-execution.manifest.json'}:
            raise RuntimeError('The execution archive is incomplete or contains undeclared members.')
        roots = components(configuration)
        if any(not directory.resolve().is_relative_to(ROOT.resolve()) for directory in roots.values()):
            raise RuntimeError('An existing execution root escapes the checkout through a symlink.')
        if len(expected) > 20000 or sum(source.getinfo(name).file_size for name in expected) > 2 * 1024**3:
            raise RuntimeError('The execution asset exceeds its bounded complete closure budget.')
        free_floor = 2 * 1024**3 if os.environ.get('GITHUB_ACTIONS') == 'true' else 50 * 1024**3
        if shutil.disk_usage(ROOT).free < free_floor + sum(source.getinfo(name).file_size for name in expected):
            raise RuntimeError('Restoring this execution closure would violate the free disk floor.')
        pending = []
        for name, entry in expected.items():
            relative = Path(name)
            destination = (ROOT / relative).resolve()
            info = source.getinfo(name)
            component = entry.get('component', '')
            prefixes = [prefix for prefix in roots if component.startswith(prefix + '/')]
            prefix = max(prefixes, key=len) if prefixes else None
            declared_path = roots[prefix] / component[len(prefix) + 1:] if prefix is not None else None
            if (relative.is_absolute() or '\\' in name or ':' in name or any(part in ('', '.', '..') for part in name.split('/')) or stat.S_ISLNK(info.external_attr >> 16)
                or prefix is None or declared_path is None or declared_path.relative_to(ROOT).as_posix() != name
                or not destination.is_relative_to(roots[prefix].resolve())):
                raise RuntimeError('The execution asset path escapes the exact owned component roots.')
            with source.open(name) as content:
                actual_hash = hashlib.file_digest(content, 'sha256').hexdigest()
            if actual_hash != entry['sha256']:
                raise RuntimeError('An execution asset member has contradictory content identity.')
            if destination.exists() and digest(destination) != entry['sha256']:
                raise RuntimeError('Refusing to overwrite a different existing execution asset: ' + str(destination))
            pending.append((name, destination, info))
        for name, destination, info in pending:
            destination.parent.mkdir(parents=True, exist_ok=True)
            with source.open(name) as content, destination.open('wb') as target:
                shutil.copyfileobj(content, target, length=1024 * 1024)
            if os.name != 'nt':
                destination.chmod((info.external_attr >> 16) & 0o777 or 0o644)
    actual, _ = closure(configuration, dotnet)
    if actual != descriptor['identity']:
        raise RuntimeError('Restored source/SDK/runner/closure identity differs from the accepted build.')
    return actual


def execute(args) -> None:
    identity = restore(args.assets.resolve(), args.dotnet, args.configuration, args.allow_dirty_local)
    directory = args.output.resolve() / ('shard' + str(args.shard))
    directory.mkdir(parents=True, exist_ok=True)
    report = directory / 'driver.json'
    for name in ('driver.json', 'BGCS.Tests.shard' + str(args.shard) + '.trx', 'receipt.json'):
        if (directory / name).exists():
            raise RuntimeError('A shard run must start with fresh evidence paths.')
    roots = components(args.configuration)
    sdk, _, _ = sdk_root(args.dotnet)
    command = [str(args.dotnet), str(roots['driver'] / 'BGCS.TestDriver.dll'), '--assembly', str(roots['host'] / 'BGCS.Tests.dll'),
               '--sdk-root', str(sdk), '--output', str(report), '--shard', str(args.shard)]
    subprocess.run(command, cwd=ROOT, env=environment(args.dotnet), check=True)
    driver = json.loads(report.read_text())
    validate_driver(driver, args.shard)
    after, _ = closure(args.configuration, args.dotnet)
    if identity != after:
        raise RuntimeError('Source/SDK/execution bytes changed during the exact shard.')
    trx = directory / ('BGCS.Tests.shard' + str(args.shard) + '.trx')
    if not trx.is_file():
        raise RuntimeError('The exact-case runner did not produce its independent TRX evidence.')
    receipt = {'schemaVersion': 1, 'success': True, 'shardIndex': args.shard, 'identity': identity,
               'driver': {'path': report.relative_to(args.output.resolve()).as_posix(), 'sha256': digest(report)},
               'trx': {'path': trx.relative_to(args.output.resolve()).as_posix(), 'sha256': digest(trx)}}
    (directory / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')


def aggregate(args) -> None:
    directory = args.input.resolve()
    paths = sorted(directory.glob('shard*/receipt.json'))
    if len(paths) != SHARDS:
        raise RuntimeError('All eight actual shard receipts are required.')
    result = join([json.loads(path.read_text()) for path in paths], directory, allow_dirty=args.allow_dirty_local)
    actual, _ = closure(args.configuration, args.dotnet)
    if result['identity'] != actual:
        raise RuntimeError('The aggregate job is not using the exact accepted source and closed execution bytes.')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    write_trx(result, output / 'BGCS.Tests.trx')
    result.pop('_documents')
    result['trxSha256'] = digest(output / 'BGCS.Tests.trx')
    (output / 'BGCS.Tests.shards.json').write_text(json.dumps(result, indent=2) + '\n')
    (output / 'BGCS.Tests.json').write_text(json.dumps(test_receipt(output / 'BGCS.Tests.trx'), indent=2) + '\n')
    print('Verified complete exact Host test union:', result['passed'], 'cases across eight shards.')


def verify_accepted(args) -> None:
    """Recheck original executed bytes/evidence without replacing freshly built release binaries."""
    directory = args.input.resolve()
    paths = sorted(directory.glob('shard*/receipt.json'))
    if len(paths) != SHARDS:
        raise RuntimeError('All eight original accepted shard receipts are required.')
    result = join([json.loads(path.read_text()) for path in paths], directory)
    verify_accepted_archive(args.assets.resolve(), result['identity'], args.configuration)
    validate_accepted_source_sdk(result['identity'], source_identity(ROOT), sdk_identity(args.dotnet))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    write_trx(result, output / 'BGCS.Tests.trx')
    result.pop('_documents')
    result['trxSha256'] = digest(output / 'BGCS.Tests.trx')
    (output / 'BGCS.Tests.shards.json').write_text(json.dumps(result, indent=2) + '\n')
    print('Reverified original complete Host execution proof without rerunning:', result['passed'], 'cases.')


def verify_accepted_archive(asset: Path, identity: dict, configuration: str) -> None:
    if (asset / 'asset.json').stat().st_size > 16 * 1024**2:
        raise RuntimeError('The accepted execution descriptor exceeds its finite bound.')
    descriptor = json.loads((asset / 'asset.json').read_text())
    if descriptor.get('identity') != identity or digest(asset / 'execution.zip') != descriptor.get('archiveSha256'):
        raise RuntimeError('The executed case union and original execution archive disagree.')
    with zipfile.ZipFile(asset / 'execution.zip') as archive:
        if len(archive.namelist()) != len(set(archive.namelist())) or archive.getinfo('host-execution.manifest.json').file_size > 16 * 1024**2:
            raise RuntimeError('The accepted execution archive has duplicate or oversized metadata.')
        manifest = json.loads(archive.read('host-execution.manifest.json'))
        expected = validate_asset_manifest(descriptor, manifest, configuration)
        if set(archive.namelist()) != set(expected) | {'host-execution.manifest.json'} or sum(item.file_size for item in archive.infolist()) > 2 * 1024**3:
            raise RuntimeError('The accepted execution archive is incomplete or exceeds its bounded closure.')
        for name, entry in expected.items():
            if stat.S_ISLNK(archive.getinfo(name).external_attr >> 16):
                raise RuntimeError('Accepted execution entries cannot be symlinks.')
            with archive.open(name) as content:
                if hashlib.file_digest(content, 'sha256').hexdigest() != entry['sha256']:
                    raise RuntimeError('An originally executed component has changed.')


def sdk_identity(dotnet: Path) -> dict:
    sdk, _, runtime = sdk_root(dotnet)
    result = {'sdk/' + name: digest(sdk / name) for name in SDK_FILES}
    result['sdk/dotnet-host'] = digest(dotnet)
    result['sdk/System.Private.CoreLib.dll'] = digest(dotnet.parent / 'shared/Microsoft.NETCore.App' / runtime / 'System.Private.CoreLib.dll')
    return result


def validate_accepted_source_sdk(identity: dict, source: dict, sdk: dict) -> None:
    validate_identity(identity)
    if source != identity['source'] or source.get('workingTreeDirty') is not False:
        raise RuntimeError('Accepted Host proof belongs to another or dirty release source.')
    if sdk != {name: value for name, value in identity['executionClosure'].items() if name.startswith('sdk/')}:
        raise RuntimeError('Accepted Host proof belongs to another selected SDK/runtime.')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'run', 'aggregate', 'restore', 'verify-accepted'))
    parser.add_argument('--dotnet', type=Path, default=Path(shutil.which('dotnet') or 'dotnet'))
    parser.add_argument('--configuration', default='Release', choices=('Debug', 'Release'))
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/host-test-shards')
    parser.add_argument('--input', type=Path)
    parser.add_argument('--assets', type=Path)
    parser.add_argument('--shard', type=int, choices=range(SHARDS))
    parser.add_argument('--allow-dirty-local', action='store_true')
    args = parser.parse_args()
    args.dotnet = args.dotnet.resolve()
    if args.mode in ('run', 'restore', 'verify-accepted') and args.assets is None or args.mode == 'run' and args.shard is None or args.mode in ('aggregate', 'verify-accepted') and args.input is None:
        parser.error('The selected mode requires exact asset/input paths and an explicit shard index where applicable.')
    if args.mode == 'verify-accepted' and args.allow_dirty_local:
        parser.error('Release proof revalidation cannot accept dirty local source.')
    {'prepare': prepare, 'run': execute, 'aggregate': aggregate,
     'verify-accepted': verify_accepted,
     'restore': lambda value: restore(value.assets.resolve(), value.dotnet, value.configuration, value.allow_dirty_local)}[args.mode](args)


if __name__ == '__main__':
    main()
