#!/usr/bin/env python3
"""Run tests with explicit native inputs while preserving tool and runtime discovery."""
from __future__ import annotations

import argparse
from collections.abc import Mapping
import os
import subprocess


# These variables inject compiler options or implicit source/link search paths.
# Discovery selectors (PATH, CC/CXX, VC/SDK directories) and runtime security or
# instrumentation settings deliberately remain visible to the tested product.
NATIVE_INJECTION_VARIABLES = (
    'INCLUDE', 'CPATH', 'C_INCLUDE_PATH', 'CPLUS_INCLUDE_PATH', 'OBJC_INCLUDE_PATH',
    'SDKROOT', 'GCC_EXEC_PREFIX', 'COMPILER_PATH', 'LIBRARY_PATH',
    'CL', '_CL_', 'LINK', '_LINK_', 'LIB', 'CCC_OVERRIDE_OPTIONS',
)


def isolated_native_test_environment(environment: Mapping[str, str], *, windows: bool | None = None) -> dict[str, str]:
    """Copy an environment, removing only ambient native compilation inputs."""
    if windows is None:
        windows = os.name == 'nt'
    blocked = frozenset(NATIVE_INJECTION_VARIABLES)
    return {name: value for name, value in environment.items()
            if (name.upper() if windows else name) not in blocked}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', nargs=argparse.REMAINDER, help='Exact child argv, optionally following --.')
    args = parser.parse_args(argv)
    command = args.command
    if command[:1] == ['--']:
        command = command[1:]
    if not command:
        parser.error('A test command is required after --.')
    return subprocess.call(command, env=isolated_native_test_environment(os.environ))


if __name__ == '__main__':
    raise SystemExit(main())
