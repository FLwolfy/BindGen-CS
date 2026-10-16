#!/usr/bin/env python3
"""Run only the tool's private worker deployment target; never compile product assemblies."""
from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
DOTNET = os.environ.get("DOTNET_HOST_PATH", "dotnet")


class ToolWorkerPublishTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="tool-worker-publish-", dir=parent)
        self.root = Path(self.temporary.name).resolve()
        if not self.root.is_relative_to(ROOT):
            raise RuntimeError("The worker target fixture escaped its owned workspace.")
        self.addCleanup(self.temporary.cleanup)
        self.output = self.root / "publish output"
        self.worker = self.output / "bgcs/worker"
        self.worker.mkdir(parents=True)
        (self.worker / "Newtonsoft.Json.dll").write_bytes(b"retired dependency")
        (self.worker / "old/retired.txt").parent.mkdir()
        (self.worker / "old/retired.txt").write_bytes(b"retired nested artifact")
        (self.output / "sibling-sentinel").write_bytes(b"unrelated publish output")

    def source(self, inside_output=False):
        source = (self.worker if inside_output else self.root) / "source closure"
        for directory in ("common", "native/win-x64", "manifests"):
            (source / directory).mkdir(parents=True)
        for name, content in {
            "common/BGCS.Frontend.Clang.Worker.dll": b"new managed worker fixture",
            "common/sub/input.txt": b"original nested input",
            "native/win-x64/libclang.dll": b"new native fixture",
            "manifests/win-x64.json": b"locked fixture manifest",
        }.items():
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        return source

    @staticmethod
    def files(root):
        return {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                for path in root.rglob("*") if path.is_file()}

    def run_target(self, source):
        # The explicit target has no Build/Publish/Restore dependency and cannot
        # write shared assembly outputs. Every destination is this owned fixture.
        command = [DOTNET, "msbuild", str(ROOT / "src/BGCS.Tool/BGCS.Tool.csproj"),
                   "-target:BGCSIncludeToolWorker", "-p:Configuration=Release", "-p:RuntimeIdentifier=win-x64",
                   "-p:PublishDir=" + str(self.output) + os.sep, "-p:BGCSWorkerPackageRoot=" + str(source),
                   "-m:1", "-nr:false", "-verbosity:minimal"]
        environment = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en")
        return subprocess.run(command, cwd=ROOT, env=environment, capture_output=True,
                              text=True, encoding="utf-8", errors="replace", timeout=60)

    def test_reused_worker_is_replaced_without_touching_siblings_or_inputs(self):
        source = self.source()
        original = self.files(source)
        result = self.run_target(source)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(original, self.files(source))
        self.assertEqual(b"unrelated publish output", (self.output / "sibling-sentinel").read_bytes())
        expected = {name.removeprefix("common/"): digest for name, digest in original.items() if name.startswith("common/")}
        expected["runtimes/win-x64/native/libclang.dll"] = original["native/win-x64/libclang.dll"]
        expected["worker-assets.manifest.json"] = original["manifests/win-x64.json"]
        self.assertEqual(expected, self.files(self.worker))

    def test_source_nested_in_the_owned_destination_is_rejected_before_cleanup(self):
        source = self.source(inside_output=True)
        before = self.files(self.output)
        result = self.run_target(source)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("separate from its source closure", result.stdout + result.stderr)
        self.assertEqual(before, self.files(self.output))

    def test_missing_locked_source_cannot_erase_an_existing_worker(self):
        source = self.source()
        (source / "common/BGCS.Frontend.Clang.Worker.dll").unlink()
        before = self.files(self.output)
        result = self.run_target(source)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("locked tool worker closure is missing", result.stdout + result.stderr)
        self.assertEqual(before, self.files(self.output))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=DOTNET)
    options, remaining = parser.parse_known_args()
    DOTNET = shutil.which(options.dotnet) or str(Path(options.dotnet).resolve())
    unittest.main(argv=[__file__, *remaining], verbosity=2)
