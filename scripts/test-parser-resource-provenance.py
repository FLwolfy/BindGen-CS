#!/usr/bin/env python3
"""Reject parser resource drift using byte fixtures, without executing a native binary."""
from __future__ import annotations

import json
from pathlib import Path
import struct
import tempfile
import unittest

import parser_runtime_provenance as PROVENANCE

ROOT = Path(__file__).resolve().parents[1]


class ParserResourceProvenanceTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT):
            raise RuntimeError("The parser resource regression workspace escaped the repository.")
        self.temporary = tempfile.TemporaryDirectory(prefix="parser-resource-proof-", dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.directory = self.root / "intel-parser"
        self.directory.mkdir()
        header = self.root / "headers.zip"
        header.write_bytes(b"independently reviewed builtin header bundle")
        lock = {"schemaVersion": 1, "llvmMajor": 20, "parserIdentity": "ClangSharp/20.1.2.4;LLVM/20",
            "builtinHeaders": {"path": "headers.zip", "sha256": PROVENANCE.digest(header)},
            "supplementalRuntime": {"clangSharp": {"commit": "a" * 40}, "llvm": {
                "version": "20.1.8", "sourceArchiveSha256": "b" * 64,
                "homebrewFormula": "llvm@20", "sourceArchiveUrl": "https://example.invalid/reviewed-llvm.tar.xz"}}, "upstreamRuntimes": {}}
        lock_path = self.root / "distribution/manifests/native-parser-assets.lock.json"
        lock_path.parent.mkdir(parents=True)
        lock_path.write_text(json.dumps(lock), encoding="utf-8")
        self.lock = lock
        self.lock_path = lock_path
        for name in ("libclang.dylib", "libClangSharp.dylib"):
            (self.directory / name).write_bytes(b"\xcf\xfa\xed\xfe" + struct.pack("<I", 0x01000007) + b"\0" * 56)
        for name in ("licenses/LLVM-LICENSE.txt", "licenses/ClangSharp-LICENSE.md", "licenses/ClangSharp-NOTICE.md",
                     "build-inputs/homebrew-llvm.json"):
            path = self.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("reviewed input " + name, encoding="utf-8")
        (self.directory / "build-inputs/homebrew-llvm.json").write_text(json.dumps({"formulae": [{"name": "llvm@20",
            "versions": {"stable": "20.1.8"}, "installed": [{"version": "20.1.8"}],
            "urls": {"stable": {"url": "https://example.invalid/reviewed-llvm.tar.xz", "checksum": "b" * 64}}}]}), encoding="utf-8")
        self.manifest = {"schemaVersion": 1, "hostRid": "osx-x64", "parserIdentity": lock["parserIdentity"],
            "bgcsSourceRevision": "c" * 40, "clangSharpCommit": "a" * 40, "llvmVersion": "20.1.8",
            "llvmSourceArchiveSha256": "b" * 64,
            "components": [{"id": name, "version": "20.1.8", "source": {"uri": "https://example.invalid/source", "sha256": "b" * 64},
                "licenses": [{"path": license_path, "sha256": PROVENANCE.digest(self.directory / license_path)}],
                "files": [{"path": filename, "sha256": PROVENANCE.digest(self.directory / filename)}], "dependencies": dependencies}
                for name, filename, license_path, dependencies in [
                    ("ClangSharp", "libClangSharp.dylib", "licenses/ClangSharp-LICENSE.md", ["LLVM"]),
                    ("LLVM", "libclang.dylib", "licenses/LLVM-LICENSE.txt", [])]]}
        self.write_manifest()

    def write_manifest(self):
        self.manifest["artifacts"] = [{"path": path.relative_to(self.directory).as_posix(), "bytes": path.stat().st_size,
            "sha256": PROVENANCE.digest(path)} for path in sorted(self.directory.rglob("*"))
            if path.is_file() and path.name != "runtime-provenance.json"]
        (self.directory / "runtime-provenance.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def verify(self):
        return PROVENANCE.verify_supplemental(self.root, self.directory, "c" * 40)

    def test_complete_x64_digest_and_source_provenance_is_accepted(self):
        self.assertEqual(6, len(self.verify()["artifacts"]))

    def test_missing_actual_provenance_is_rejected(self):
        (self.directory / "runtime-provenance.json").unlink()
        with self.assertRaisesRegex(RuntimeError, "actual build provenance"):
            self.verify()

    def test_arm64_cannot_be_resealed_as_an_intel_parser(self):
        (self.directory / "libclang.dylib").write_bytes(b"\xcf\xfa\xed\xfe" + struct.pack("<I", 0x0100000C) + b"\0" * 56)
        self.write_manifest()
        with self.assertRaisesRegex(RuntimeError, "architecture does not match"):
            self.verify()

    def test_drifted_payload_or_unlisted_dependency_is_rejected(self):
        path = self.directory / "licenses/LLVM-LICENSE.txt"
        original = path.read_bytes()
        path.write_bytes(b"unreviewed changed input")
        with self.assertRaisesRegex(RuntimeError, "changed after its actual build"):
            self.verify()
        path.write_bytes(original)
        (self.directory / "unlisted.dylib").write_bytes(b"unlisted dependency")
        with self.assertRaisesRegex(RuntimeError, "complete file closure"):
            self.verify()

    def test_matching_major_does_not_allow_wrong_recipe_or_bgcs_revision(self):
        for field, value in (("clangSharpCommit", "d" * 40), ("llvmVersion", "20.1.7"),
                             ("llvmSourceArchiveSha256", "0" * 64), ("bgcsSourceRevision", "e" * 40)):
            with self.subTest(field=field):
                original = self.manifest[field]
                self.manifest[field] = value
                self.write_manifest()
                with self.assertRaisesRegex(RuntimeError, "pinned source recipe"):
                    self.verify()
                self.manifest[field] = original

    def test_changed_builtin_header_bundle_is_rejected(self):
        (self.root / "headers.zip").write_bytes(b"different LLVM bundle")
        with self.assertRaisesRegex(RuntimeError, "builtin header bundle"):
            self.verify()

    def test_resealing_different_homebrew_recipe_does_not_claim_pinned_provenance(self):
        path = self.directory / "build-inputs/homebrew-llvm.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        data["formulae"][0]["urls"]["stable"]["checksum"] = "0" * 64
        path.write_text(json.dumps(data), encoding="utf-8")
        self.write_manifest()
        with self.assertRaisesRegex(RuntimeError, "installed LLVM formula differs"):
            self.verify()

    def test_upstream_archive_identity_and_actual_native_bytes_are_both_checked(self):
        source = self.directory / "libclang.dylib"
        package = {"id": "libclang.runtime.osx-x64", "version": "20.1.2", "sha512": "archive-identity"}
        self.lock["upstreamRuntimes"]["osx-x64"] = {"packages": [package], "files": [
            {"name": source.name, "bytes": source.stat().st_size, "sha256": PROVENANCE.digest(source)}]}
        self.lock_path.write_text(json.dumps(self.lock), encoding="utf-8")
        assets = {"libraries": {"libclang.runtime.osx-x64/20.1.2": {"sha512": "archive-identity"}}}
        PROVENANCE.verify_upstream(self.root, "osx-x64", assets, {source.name: source})
        assets["libraries"]["libclang.runtime.osx-x64/20.1.2"]["sha512"] = "different-package"
        with self.assertRaisesRegex(RuntimeError, "restored parser package identity"):
            PROVENANCE.verify_upstream(self.root, "osx-x64", assets, {source.name: source})
        assets["libraries"]["libclang.runtime.osx-x64/20.1.2"]["sha512"] = "archive-identity"
        source.write_bytes(source.read_bytes() + b"altered native bytes")
        with self.assertRaisesRegex(RuntimeError, "reviewed upstream bytes"):
            PROVENANCE.verify_upstream(self.root, "osx-x64", assets, {source.name: source})

    def test_native_component_graph_cannot_omit_or_invent_delivered_dependency_identity(self):
        original = self.manifest["components"]
        for components in ([], original[:1], [dict(original[0], dependencies=["Absent"]), original[1]]):
            with self.subTest(components=components):
                self.manifest["components"] = components
                self.write_manifest()
                with self.assertRaisesRegex(RuntimeError, "component|dependency"):
                    self.verify()
        self.manifest["components"] = original

    def test_native_license_hash_cannot_be_resealed_as_different_component_evidence(self):
        self.manifest["components"][0]["licenses"][0]["sha256"] = "0" * 64
        self.write_manifest()
        with self.assertRaisesRegex(RuntimeError, "contradicts actual delivered"):
            self.verify()

    def worker_output(self):
        output = self.root / "worker-output"
        output.mkdir()
        (output / "BGCS.Frontend.Clang.Worker.dll").write_bytes(b"completed worker fixture; not executed")
        return output

    def test_worker_staging_replaces_only_native_subtree_with_complete_verified_provenance(self):
        output = self.worker_output()
        destination = output / "runtimes/osx-x64/native"
        destination.mkdir(parents=True)
        (destination / "retired.dylib").write_bytes(b"stale unowned-by-next-closure bytes")
        sibling = output / "runtimes/sibling.keep"
        sibling.write_bytes(b"independent sibling")
        companion = self.directory / "libcompanion.dylib"
        companion.write_bytes((self.directory / "libclang.dylib").read_bytes())
        self.manifest["components"][1]["files"].append({"path": companion.name, "sha256": PROVENANCE.digest(companion)})
        self.write_manifest()
        original = {path.relative_to(self.directory).as_posix(): path.read_bytes()
                    for path in self.directory.rglob("*") if path.is_file()}

        PROVENANCE.stage_worker(self.root, self.directory, output, "c" * 40)

        actual = {path.relative_to(destination).as_posix(): path.read_bytes()
                  for path in destination.rglob("*") if path.is_file()}
        self.assertEqual(original, actual)
        self.assertEqual(original, {path.relative_to(self.directory).as_posix(): path.read_bytes()
                                    for path in self.directory.rglob("*") if path.is_file()})
        self.assertEqual(b"independent sibling", sibling.read_bytes())
        self.assertEqual(["native"], sorted(path.name for path in destination.parent.iterdir()))
        self.assertEqual(self.manifest, PROVENANCE.verify_supplemental(self.root, destination, "c" * 40))

    def test_worker_staging_invalid_source_preserves_previous_bytes_without_partial_output(self):
        output = self.worker_output()
        destination = output / "runtimes/osx-x64/native"
        destination.mkdir(parents=True)
        (destination / "previous.keep").write_bytes(b"published previous")
        (self.directory / "libclang.dylib").write_bytes(b"changed after captured provenance")

        with self.assertRaisesRegex(RuntimeError, "changed after its actual build"):
            PROVENANCE.stage_worker(self.root, self.directory, output, "c" * 40)

        self.assertEqual(["previous.keep"], [path.name for path in destination.iterdir()])
        self.assertEqual(b"published previous", (destination / "previous.keep").read_bytes())
        self.assertEqual(["native"], [path.name for path in destination.parent.iterdir()])

    def test_worker_staging_requires_completed_separate_worker_output(self):
        output = self.root / "not-a-worker"
        output.mkdir()
        with self.assertRaisesRegex(RuntimeError, "completed worker output"):
            PROVENANCE.stage_worker(self.root, self.directory, output, "c" * 40)
        (self.directory / "BGCS.Frontend.Clang.Worker.dll").write_bytes(b"not a valid separate output")
        with self.assertRaisesRegex(RuntimeError, "separate owned roots"):
            PROVENANCE.stage_worker(self.root, self.directory, self.directory, "c" * 40)

    def test_worker_staging_rejects_stale_source_revision_before_replacing_native_output(self):
        output = self.worker_output()
        with self.assertRaisesRegex(RuntimeError, "current BGCS revision"):
            PROVENANCE.stage_worker(self.root, self.directory, output, "d" * 40)
        self.assertFalse((output / "runtimes").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
