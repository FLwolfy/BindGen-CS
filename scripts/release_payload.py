#!/usr/bin/env python3
"""Authenticate accepted package and repository-tool bytes without rebuilding or reinstalling."""
from __future__ import annotations
import argparse, importlib.util, json
from pathlib import Path
from acceptance_evidence import digest, source_identity

ROOT = Path(__file__).resolve().parents[1]

def _module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module

def _files(directory):
    files = {}
    for path in directory.rglob('*'):
        if path.is_symlink() or not path.resolve().is_relative_to(directory.resolve()):
            raise RuntimeError('Accepted release tooling cannot contain linked or escaping entries.')
        if path.is_file() and path != directory / 'release-tooling.json': files[path.relative_to(directory).as_posix()] = digest(path)
    if not files or 'BGCS.RepositoryTools.dll' not in files: raise RuntimeError('The accepted repository tools closure is absent.')
    return files

def tooling(directory, revision, *, seal=False):
    source = source_identity(ROOT)
    if source['revision'] != revision or source['workingTreeDirty']:
        raise RuntimeError('Accepted payloads require the exact clean source revision.')
    path = directory / 'release-tooling.json'
    value = {'schemaVersion': 1, 'source': source, 'files': _files(directory)}
    if seal:
        if path.exists(): raise RuntimeError('Do not replace an existing accepted tooling receipt.')
        path.write_text(json.dumps(value, sort_keys=True, indent=2) + '\n', encoding='utf-8')
    elif json.loads(path.read_text(encoding='utf-8')) != value:
        raise RuntimeError('Accepted repository tooling differs from its exact source or complete file hashes.')
    return value

def packages(directory, verification, revision, version):
    source = source_identity(ROOT)
    if source['revision'] != revision or source['workingTreeDirty']:
        raise RuntimeError('Accepted package publication requires its exact clean source revision.')
    verifier = _module('accepted_product_verifier', 'eng/verify-packages.py')
    report = json.loads(verification.read_text(encoding='utf-8'))
    if report.get('version') != version:
        raise RuntimeError('The accepted package version differs from the declared immutable release.')
    policy = _module('accepted_product_policy', 'scripts/verify-ci-product-evidence.py')
    policy.validate(report, revision, 'win-x64')
    actual = verifier.verify_package_receipt(directory, version, revision, True)
    if actual != report['packagingEvidence']:
        raise RuntimeError('The release payload differs from the exact packages installed by the accepted product job.')
    _module('accepted_package_closure', 'eng/pack.py').verify_packages(directory, version, True)
    return actual

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('seal-tooling', 'verify-tooling', 'verify-packages'))
    parser.add_argument('directory', type=Path)
    parser.add_argument('--revision', required=True)
    parser.add_argument('--version')
    parser.add_argument('--verification', type=Path)
    args = parser.parse_args()
    if args.command == 'verify-packages':
        if not args.version or not args.verification: parser.error('Package authentication needs --version and --verification.')
        packages(args.directory, args.verification, args.revision, args.version)
    else: tooling(args.directory, args.revision, seal=args.command == 'seal-tooling')

if __name__ == '__main__': main()
