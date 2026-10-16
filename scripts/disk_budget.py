"""Bound the free-space floor while owned package subprocesses are running."""
from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import time


def check_free_space(root: Path) -> None:
    floor = int(os.environ.get('BGCS_MINIMUM_FREE_BYTES', str(2 * 1024**3)))
    if floor <= 0 or shutil.disk_usage(root).free < floor:
        raise RuntimeError('The owned packaging process reached its declared positive free-space floor.')


def wait_for_process(process, root: Path, timeout: float | None = None) -> int:
    deadline = None if timeout is None else time.monotonic() + timeout
    while True:
        check_free_space(root)
        remaining = None if deadline is None else deadline - time.monotonic()
        if remaining is not None and remaining <= 0:
            raise subprocess.TimeoutExpired(process.args, timeout)
        try:
            result = process.wait(timeout=1 if remaining is None else min(1, remaining))
            check_free_space(root)
            return result
        except subprocess.TimeoutExpired:
            pass
