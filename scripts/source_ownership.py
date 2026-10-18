#!/usr/bin/env python3
"""Reject intended source files hidden from Git by build-output ignore rules."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess

SOURCE_ROOTS = ('src', 'integrations', 'tests', 'eng', 'distribution', 'schemas', 'examples', 'docs', 'scripts', '.github')
SOURCE_EXTENSIONS = frozenset(('.cs', '.csproj', '.props', '.targets', '.proj', '.slnx', '.py', '.sh', '.json',
                               '.xml', '.md', '.txt', '.yml', '.yaml', '.h', '.hpp', '.hh', '.c', '.cpp', '.cc',
                               '.in', '.cmake', '.nuspec'))
BUILD_DIRECTORIES = frozenset(('bin', 'obj', '__pycache__', 'testresults', '.bindgen-cache'))
GENERATED_ROOTS = frozenset(('examples/NativeShim/Generated', 'examples/NativeShim/GeneratedBridge',
                            'examples/QuickStart/Generated', 'examples/QuickStart/.bgcs/cache'))


def verify_source_ownership(root: Path) -> int:
    """Check real source paths against Git's actual ignore policy without changing its index or files."""
    root = root.resolve()
    candidates = []
    for name in SOURCE_ROOTS:
        directory = root / name
        if not directory.is_dir():
            continue
        for current, children, files in os.walk(directory, followlinks=False):
            children[:] = sorted(child for child in children
                if child.casefold() not in BUILD_DIRECTORIES
                and (Path(current) / child).relative_to(root).as_posix() not in GENERATED_ROOTS)
            for filename in sorted(files):
                path = Path(current) / filename
                if path.suffix.lower() in SOURCE_EXTENSIONS:
                    candidates.append(path.relative_to(root).as_posix())
    if not candidates:
        raise RuntimeError('No intended source files were found for the Git ownership preflight.')
    result = subprocess.run(['git', 'check-ignore', '--stdin', '-z'], cwd=root,
                            input=b'\0'.join(name.encode('utf-8') for name in candidates) + b'\0',
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode not in (0, 1):
        raise RuntimeError('Git source ownership could not be verified: ' + result.stderr.decode('utf-8', errors='replace'))
    ignored = sorted(name.decode('utf-8') for name in result.stdout.split(b'\0') if name)
    if ignored:
        raise RuntimeError('Intended source files are hidden by Git ignore rules: ' + ', '.join(ignored))
    return len(candidates)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path, nargs='?', default=Path(__file__).resolve().parents[1])
    arguments = parser.parse_args()
    print('Verified Git visibility of', verify_source_ownership(arguments.root), 'intended source files.')
