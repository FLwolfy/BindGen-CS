#!/usr/bin/env python3
"""Install only the SDK archive recorded in the toolchain lock, including its runtime."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
from urllib.request import urlopen
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def host_rid() -> str:
    machine = platform.machine().lower()
    if machine not in ('amd64', 'x86_64', 'arm64', 'aarch64'):
        raise RuntimeError('The locked SDK has no archive for this host CPU.')
    system = {'Windows': 'win', 'Linux': 'linux', 'Darwin': 'osx'}.get(platform.system())
    if system is None:
        raise RuntimeError('The locked SDK has no archive for this host OS.')
    return system + ('-arm64' if machine in ('arm64', 'aarch64') else '-x64')


def download_archive(url: str, destination: Path) -> None:
    if not url.startswith('https://'):
        raise RuntimeError('A locked SDK archive must use HTTPS.')
    with urlopen(url, timeout=120) as response, destination.open('wb') as output:
        shutil.copyfileobj(response, output, length=1024 * 1024)


def validate_member(name: str) -> None:
    path = PurePosixPath(name)
    if not name or '\\' in name or ':' in name or path.is_absolute() or '..' in path.parts:
        raise RuntimeError('An SDK archive member escapes its isolated installation: ' + name)


def extract_archive(archive: Path, destination: Path, url: str) -> None:
    if url.endswith('.zip'):
        with zipfile.ZipFile(archive) as source:
            names = source.namelist()
            if len(names) != len(set(names)):
                raise RuntimeError('The SDK archive contains duplicate members.')
            for member in source.infolist():
                validate_member(member.filename)
                if stat.S_ISLNK(member.external_attr >> 16):
                    raise RuntimeError('A Windows SDK archive contains a symbolic link.')
            source.extractall(destination)
    elif url.endswith('.tar.gz'):
        with tarfile.open(archive, 'r:gz') as source:
            for member in source.getmembers():
                validate_member(member.name)
            # Python 3.12's data filter also confines link destinations and
            # rejects devices while retaining the SDK host's executable mode.
            source.extractall(destination, filter='data')
    else:
        raise RuntimeError('The locked SDK archive has an unsupported format.')


def verify_installation(directory: Path) -> None:
    dotnet = directory / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
    if not dotnet.is_file():
        raise RuntimeError('The locked SDK archive has no dotnet host.')
    subprocess.run([sys.executable, str(ROOT / 'scripts/verify-locked-toolchain.py'), '--dotnet', str(dotnet)],
                   cwd=ROOT, check=True)


def install_sdk(destination: Path, entry: dict, verifier=verify_installation) -> None:
    destination = destination.absolute()
    if destination.exists() or destination.is_symlink():
        raise RuntimeError('The isolated SDK destination must be absent; existing runtimes cannot be merged: ' + str(destination))
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.bgcs-sdk-', dir=destination.parent) as temporary:
        work = Path(temporary)
        archive = work / 'sdk.archive'
        download_archive(entry['url'], archive)
        with archive.open('rb') as stream:
            actual = hashlib.file_digest(stream, 'sha512').hexdigest()
        if actual != entry['sha512']:
            raise RuntimeError('The downloaded SDK archive differs from its locked SHA512.')
        staging = work / 'sdk'
        staging.mkdir()
        extract_archive(archive, staging, entry['url'])
        verifier(staging)
        if destination.exists() or destination.is_symlink():
            raise RuntimeError('The SDK destination appeared during installation.')
        staging.rename(destination)


def export_environment(directory: Path, rid: str) -> None:
    directory = directory.resolve()
    values = {'DOTNET_INSTALL_DIR': str(directory), 'DOTNET_ROOT': str(directory),
              'DOTNET_ROOT_' + rid.rsplit('-', 1)[1].upper(): str(directory),
              'DOTNET_HOST_PATH': str(directory / ('dotnet.exe' if rid.startswith('win-') else 'dotnet')),
              'DOTNET_MULTILEVEL_LOOKUP': '0'}
    if any('\n' in value or '\r' in value for value in values.values()):
        raise RuntimeError('An SDK environment path contains a line break.')
    if os.environ.get('GITHUB_ENV'):
        with Path(os.environ['GITHUB_ENV']).open('a', encoding='utf-8') as output:
            output.writelines(name + '=' + value + '\n' for name, value in values.items())
    if os.environ.get('GITHUB_PATH'):
        with Path(os.environ['GITHUB_PATH']).open('a', encoding='utf-8') as output:
            output.write(str(directory) + '\n')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--install-dir', type=Path, required=True)
    args = parser.parse_args()
    lock = json.loads((ROOT / 'eng/toolchains/toolchains.lock.json').read_text(encoding='utf-8'))
    rid = host_rid()
    entry = lock['sdkArchives'][rid]
    install_sdk(args.install_dir, entry)
    export_environment(args.install_dir, rid)
    print('Installed isolated locked SDK ' + lock['sdkVersion'] + ' with runtime ' + lock['managedRuntimeVersion'] + ': ' + str(args.install_dir.resolve()))


if __name__ == '__main__':
    main()
