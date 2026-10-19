#!/usr/bin/env python3
"""Exercise distribution boundaries without building or installing any product."""
from __future__ import annotations

import importlib.util
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import shutil
import subprocess
import sys
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("bgcs_distribution_closure", ROOT / "eng/pack.py")
PACK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACK)


class PackageClosureTests(unittest.TestCase):
    def parser_payload(self, archive, fault="valid"):
        files = {name: name.encode("utf-8") for name in (
            "libclang.dylib", "libClangSharp.dylib", "libLLVM.dylib", "libzstd.1.dylib",
            "licenses/LLVM-LICENSE.txt", "licenses/ClangSharp-LICENSE.md", "licenses/ClangSharp-NOTICE.md",
            "build-inputs/homebrew-llvm.json")}
        manifest = {"artifacts": [{"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
                                  for name, data in files.items()]}
        archive.writestr("runtime-provenance.json", json.dumps(manifest))
        for name, data in files.items():
            target = "runtimes/osx-x64/native/" + name if name.endswith(".dylib") else name
            if fault == "nested-native" and name.endswith(".dylib"): target = "runtimes/" + target
            if fault == "nested-license" and name.startswith("licenses/"): target = "licenses/" + target
            if fault == "missing-companion" and name == "libzstd.1.dylib": continue
            archive.writestr(target, data[:-1] + b"!" if fault == "tamper" and name == "libclang.dylib" else data)
        if fault == "unlisted": archive.writestr("runtimes/osx-x64/native/stale.dylib", b"not declared")

    def test_parser_package_preserves_native_asset_paths_and_complete_provenance_bytes(self):
        for fault in ("valid", "nested-native", "nested-license", "missing-companion", "tamper", "unlisted"):
            with self.subTest(fault=fault):
                path = self.root / (fault + ".nupkg")
                with zipfile.ZipFile(path, "w") as archive:
                    self.parser_payload(archive, fault)
                with zipfile.ZipFile(path) as archive:
                    if fault == "valid": PACK.verify_parser_runtime_archive(archive)
                    else:
                        with self.assertRaises(RuntimeError): PACK.verify_parser_runtime_archive(archive)

    def sdk_payload(self, archive, *, tamper=False, extra=False, missing=False, copy=True):
        prefix = "contentFiles/any/net10.0/bgcs/worker-assets/"
        files = {"BGCS.Frontend.Clang.Worker.dll": b"worker", "BGCS.Frontend.Clang.Worker.deps.json": b"{}",
                 "BGCS.Frontend.Clang.Worker.runtimeconfig.json": b"{}", "fr/resources.dll": b"localized", "runtimes/unix/lib/net10.0/helper.dll": b"rid-managed"}
        manifest = {"schemaVersion": 1, "hostRid": "win-x64", "parserIdentity": "ClangSharp/20.1.2.4;LLVM/20",
            "artifacts": [{"relativePath": name, "digest": {"value": hashlib.sha256(data).hexdigest()}} for name, data in files.items()]
                + [{"relativePath": "runtimes/win-x64/native/" + name + ".dll", "digest": {"value": "c" * 64}} for name in ("libclang", "libClangSharp")]}
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as common:
            for name, data in files.items():
                if not missing or name != "fr/resources.dll": common.writestr(name, data + (b"changed" if tamper else b""))
            if extra: common.writestr("stale.dll", b"not declared")
        archive.writestr(prefix + "worker-common.zip", stream.getvalue())
        archive.writestr(prefix + "manifests/win-x64.json", json.dumps(manifest))
        archive.writestr("BGCS.nuspec", '<package><metadata><contentFiles><files include="any/net10.0/bgcs/worker-assets/**/*" buildAction="None" copyToOutput="' + str(copy).lower() + '" flatten="false" /></contentFiles></metadata></package>')

    def test_sdk_standard_content_copy_metadata_and_exact_localized_worker_closure(self):
        for fault in ("valid", "tamper", "extra", "missing", "copy"):
            path = self.root / (fault + ".nupkg")
            with zipfile.ZipFile(path, "w") as archive:
                self.sdk_payload(archive, tamper=fault == "tamper", extra=fault == "extra", missing=fault == "missing", copy=fault != "copy")
            with zipfile.ZipFile(path) as archive:
                if fault == "valid": PACK.verify_sdk_worker_archive(archive)
                else:
                    with self.subTest(fault=fault), self.assertRaises(RuntimeError): PACK.verify_sdk_worker_archive(archive)

    def test_sdk_manifest_cannot_omit_a_required_host(self):
        path = self.root / "host.nupkg"
        with zipfile.ZipFile(path, "w") as archive: self.sdk_payload(archive)
        with zipfile.ZipFile(path) as archive, self.assertRaises(RuntimeError): PACK.verify_sdk_worker_archive(archive, ["win-x64", "osx-x64"])

    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT):
            raise RuntimeError("The packaging regression workspace escaped the repository.")
        self.temporary = tempfile.TemporaryDirectory(prefix="package-closure-", dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.original_root = PACK.ROOT
        PACK.ROOT = self.root
        self.addCleanup(setattr, PACK, "ROOT", self.original_root)

    def graph(self, project, *packages):
        libraries = {}
        for name, version, checksum in packages:
            libraries[name + "/" + version] = {"type": "package", "path": name.lower() + "/" + version,
                "sha512": checksum, "files": ["lib/net10.0/" + name + ".dll", "LICENSE.txt"]}
        directory = self.root / project / "obj"
        directory.mkdir(parents=True, exist_ok=True)
        document = {"libraries": libraries, "packageFolders": {str(self.root / "packages"): {}}}
        (directory / "project.assets.json").write_text(json.dumps(document), encoding="utf-8")
        return document

    def test_product_archive_rejects_repository_and_retired_native_build_assemblies(self):
        for filename in ("BGCS.RepositoryTools.dll", "BGCS.NativeBuild.dll", "Microsoft.CodeAnalysis.Workspaces.dll", "Microsoft.CodeAnalysis.CSharp.Workspaces.dll"):
            archive = self.root / (filename + ".zip")
            with zipfile.ZipFile(archive, "w") as package:
                package.writestr("lib/net10.0/" + filename, b"fixture")
            with zipfile.ZipFile(archive) as package, self.assertRaises(RuntimeError):
                PACK.verify_generator_archive(package)
        archive = self.root / "generator.zip"
        with zipfile.ZipFile(archive, "w") as package:
            package.writestr("lib/net10.0/BGCS.dll", b"fixture")
            package.writestr("lib/net10.0/Microsoft.CodeAnalysis.CSharp.dll", b"fixture")
        with zipfile.ZipFile(archive) as package:
            PACK.verify_generator_archive(package)

    def test_advanced_frontend_reference_dependency_survives_host_decoupling(self):
        self.graph("host", ("Roslyn", "1.0.0", "roslyn"))
        self.graph("frontend", ("ClangSharp", "20.1.2.4", "clang"))
        merged = PACK.merge_package_graphs(["host", "frontend"], require_single_version=True)
        self.assertEqual({"Roslyn/1.0.0", "ClangSharp/20.1.2.4"}, set(merged["libraries"]))

    def test_host_and_worker_notices_include_independent_closures(self):
        self.graph("host", ("Roslyn", "1.0.0", "roslyn"))
        self.graph("worker", ("ClangSharp", "20.1.2.4", "clang"))
        public = PACK.merge_package_graphs(["host"])
        notices = PACK.merge_package_graphs(["host", "worker"])
        self.assertNotIn("Microsoft.Build.Utilities.Core/18.9.6", public["libraries"])
        self.assertEqual(2, len(notices["libraries"]))
        self.assertIn("ClangSharp/20.1.2.4", notices["libraries"])

    def dependency_notice_fixture(self):
        directory = self.root / "packages/fixture.dependency/1.0.0"
        directory.mkdir(parents=True)
        metadata = b'\xef\xbb\xbf<?xml version="1.0" encoding="utf-8"?>\r\n<package><metadata><id>Fixture.Dependency</id><version>1.0.0</version><license type="file">LICENSE.txt</license></metadata></package>\r\n'
        notice = b'Original upstream license\r\nCopyright fixture\r\n'
        (directory / "fixture.dependency.nuspec").write_bytes(metadata)
        (directory / "LICENSE.txt").write_bytes(notice)
        assets = {"packageFolders": {str(self.root / "packages"): {}}, "resolvedDependencies": {"Fixture.Dependency/1.0.0": []},
            "libraries": {"Fixture.Dependency/1.0.0": {"type": "package", "path": "fixture.dependency/1.0.0",
                "sha512": "fixture-restored-package-checksum", "files": ["fixture.dependency.nuspec", "LICENSE.txt"]}}}
        return assets, directory, metadata, notice

    def test_dependency_notice_metadata_uses_package_safe_name_with_exact_upstream_bytes(self):
        assets, _, metadata, notice = self.dependency_notice_fixture()
        output = self.root / "licenses/dependencies"
        PACK.copy_dependency_notices(assets, output)
        inventory = json.loads((output / "license-inventory.json").read_text(encoding="utf-8"))
        self.assertEqual(1, len(inventory))
        item = inventory[0]
        path = "Fixture.Dependency/1.0.0/fixture.dependency.nuspec.xml"
        self.assertEqual({"path": path, "sha256": hashlib.sha256(metadata).hexdigest()}, item["nuspec"])
        self.assertEqual(metadata, (output / path).read_bytes())
        self.assertFalse((output / path.removesuffix(".xml")).exists())
        self.assertEqual(notice, (output / item["notices"][0]["path"]).read_bytes())
        self.assertEqual(hashlib.sha256(notice).hexdigest(), item["notices"][0]["sha256"])

    def test_dependency_notice_missing_declared_license_cannot_create_an_inventory(self):
        assets, package, _, _ = self.dependency_notice_fixture()
        (package / "LICENSE.txt").unlink()
        output = self.root / "rejected-notices"
        with self.assertRaises(FileNotFoundError):
            PACK.copy_dependency_notices(assets, output)
        self.assertFalse((output / "license-inventory.json").exists())

    def test_separate_execution_versions_are_preserved_but_public_conflicts_fail(self):
        self.graph("host", ("Shared.Dependency", "1.0.0", "one"))
        self.graph("task", ("Shared.Dependency", "2.0.0", "two"))
        self.assertEqual(2, len(PACK.merge_package_graphs(["host", "task"])["libraries"]))
        with self.assertRaisesRegex(RuntimeError, "incompatible package versions"):
            PACK.merge_package_graphs(["host", "task"], require_single_version=True)

    def test_same_package_identity_with_different_bytes_or_assets_fails(self):
        for mismatch in ("checksum", "files"):
            with self.subTest(mismatch=mismatch):
                self.graph("host", ("Same", "1.0.0", "one"))
                other = self.graph("worker", ("Same", "1.0.0", "two" if mismatch == "checksum" else "one"))
                if mismatch == "files":
                    other["libraries"]["Same/1.0.0"]["files"].append("unreviewed.dll")
                    (self.root / "worker/obj/project.assets.json").write_text(json.dumps(other), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "disagree about package content"):
                    PACK.merge_package_graphs(["host", "worker"])

    def test_restored_identity_is_case_insensitive_without_losing_package_folders(self):
        self.graph("first", ("Shared", "1.0.0", "one"))
        second = self.graph("second", ("Shared", "1.0.0", "one"))
        second["libraries"]["shared/1.0.0"] = second["libraries"].pop("Shared/1.0.0")
        second["packageFolders"] = {str(self.root / "second-packages"): {}}
        (self.root / "second/obj/project.assets.json").write_text(json.dumps(second), encoding="utf-8")
        merged = PACK.merge_package_graphs(["first", "second"])
        self.assertEqual(1, len(merged["libraries"]))
        self.assertEqual(2, len(merged["packageFolders"]))

    def test_generic_restore_does_not_count_as_a_host_tool_restore(self):
        assets = self.graph("src/BGCS.Tool")
        assets["targets"] = {"net10.0": {}}
        (self.root / "src/BGCS.Tool/obj/project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "win-x64, osx-x64"):
            PACK.require_tool_restore_rids(["win-x64", "osx-x64"])
        assets["targets"]["net10.0/win-x64"] = {}
        (self.root / "src/BGCS.Tool/obj/project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        PACK.require_tool_restore_rids(["win-x64"])
        with self.assertRaisesRegex(RuntimeError, "osx-x64"):
            PACK.require_tool_restore_rids(["win-x64", "osx-x64"])

    def test_transitive_restore_edges_use_selected_versions_after_unification(self):
        assets = self.graph("host", ("Alpha", "1.0.0", "one"), ("Beta", "2.0.0", "two"))
        assets["targets"] = {"net10.0": {
            "Alpha/1.0.0": {"type": "package", "dependencies": {"Beta": "1.0.0"}},
            "Beta/2.0.0": {"type": "package"}}}
        (self.root / "host/obj/project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        merged = PACK.merge_package_graphs(["host"])
        self.assertEqual(["Beta/2.0.0"], merged["resolvedDependencies"]["Alpha/1.0.0"])

    def test_missing_resolved_dependency_node_is_not_an_audited_graph(self):
        assets = self.graph("host", ("Alpha", "1.0.0", "one"))
        assets["targets"] = {"net10.0": {"Alpha/1.0.0": {"type": "package", "dependencies": {"Absent": "1.0.0"}}}}
        (self.root / "host/obj/project.assets.json").write_text(json.dumps(assets), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "exact resolved library"):
            PACK.merge_package_graphs(["host"])

    def create_packages(self):
        output = self.root / "delivered"
        output.mkdir()
        for product in PACK.MANIFEST["products"]:
            if product != "BindGen-CS":
                with zipfile.ZipFile(output / (product + ".0.0.0-test.nupkg"), "w") as archive:
                    archive.writestr("lib/net10.0/" + product + ".dll", b"reviewed managed payload")
                    if product == "BGCS": self.sdk_payload(archive)
        for product in PACK.MANIFEST["symbolProducts"]:
            with zipfile.ZipFile(output / (product + ".0.0.0-test.snupkg"), "w") as archive:
                archive.writestr("lib/net10.0/" + product + ".pdb", b"reviewed symbol payload")
        return output

    def test_declared_symbol_packages_are_mandatory(self):
        output = self.create_packages()
        PACK.verify_packages(output, "0.0.0-test", False)
        (output / "BGCS.Runtime.0.0.0-test.snupkg").unlink()
        with self.assertRaisesRegex(RuntimeError, "symbol package set"):
            PACK.verify_packages(output, "0.0.0-test", False)

    def test_partial_macos_closure_requires_the_explicit_parser_supplement(self):
        output = self.create_packages()
        with zipfile.ZipFile(output / "BindGen-CS.0.0.0-test.nupkg", "w") as archive:
            archive.writestr("toolsettings.json", "pointer fixture")
        self.worker_archive("osx-x64", path=output / "BindGen-CS.osx-x64.0.0.0-test.nupkg")
        supplement = output / "BGCS.ParserRuntime.osx-x64.0.0.0-test.nupkg"
        with zipfile.ZipFile(supplement, "w") as archive:
            self.parser_payload(archive)
        with self.assertRaisesRegex(RuntimeError, "unexpected=.*BGCS.ParserRuntime.osx-x64"):
            PACK.verify_packages(output, "0.0.0-test", False, "osx-x64")
        PACK.verify_packages(output, "0.0.0-test", False, "osx-x64", include_osx_x64_parser=True)
        with zipfile.ZipFile(supplement, "w") as archive:
            self.parser_payload(archive, "nested-native")
        with self.assertRaisesRegex(RuntimeError, "asset layout"):
            PACK.verify_packages(output, "0.0.0-test", False, "osx-x64", include_osx_x64_parser=True)
        supplement.unlink()
        with self.assertRaisesRegex(RuntimeError, "missing=.*BGCS.ParserRuntime.osx-x64"):
            PACK.verify_packages(output, "0.0.0-test", False, "osx-x64", include_osx_x64_parser=True)

    def test_declared_parser_supplement_does_not_allow_unrelated_packages(self):
        output = self.create_packages()
        for name in ("BGCS.ParserRuntime.osx-x64", "Unexpected"):
            with zipfile.ZipFile(output / (name + ".0.0.0-test.nupkg"), "w") as archive:
                archive.writestr("fixture.txt", "package fixture")
        with self.assertRaisesRegex(RuntimeError, "unexpected=.*Unexpected"):
            PACK.verify_packages(output, "0.0.0-test", False, include_osx_x64_parser=True)

    def test_duplicate_symbol_entries_are_rejected(self):
        output = self.create_packages()
        with zipfile.ZipFile(output / "BGCS.0.0.0-test.snupkg", "a") as archive:
            with self.assertWarns(UserWarning):
                archive.writestr("lib/net10.0/BGCS.pdb", b"ambiguous replacement")
        with self.assertRaisesRegex(RuntimeError, "ambiguous duplicate entries"):
            PACK.verify_packages(output, "0.0.0-test", False)

    def test_empty_symbol_container_is_not_successful_symbol_delivery(self):
        output = self.create_packages()
        with zipfile.ZipFile(output / "BGCS.0.0.0-test.snupkg", "w") as archive:
            archive.writestr("manifest.txt", b"no actual PDB")
        with self.assertRaisesRegex(RuntimeError, "contains no debugging symbols"):
            PACK.verify_packages(output, "0.0.0-test", False)

    def worker_archive(self, rid="win-x64", *, omit=None, extra=None, tamper=None, change_manifest=None, path=None):
        extension = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
        payload = {name: name.encode("utf-8") for name in (
            "BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json",
            "BGCS.Frontend.Clang.Worker.runtimeconfig.json", "BGCS.Contracts.dll", "BGCS.Infrastructure.dll",
            "BGCS.Frontend.Clang.dll", "ClangSharp.dll", "ClangSharp.Interop.dll",
            *(f"runtimes/{rid}/native/{name}{extension}" for name in ("libclang", "libClangSharp")))}
        manifest = {"schemaVersion": 1, "hostRid": rid, "parserIdentity": "ClangSharp/20.1.2.4;LLVM/20",
                    "artifacts": [{"relativePath": name, "mediaType": "application/octet-stream",
                                   "digest": {"value": hashlib.sha256(content).hexdigest()}}
                                  for name, content in payload.items()]}
        if change_manifest: change_manifest(manifest)
        if omit in payload: del payload[omit]
        if extra: payload.update(extra)
        if tamper: payload[tamper] = b"replaced executable bytes"
        prefix = f"tools/net10.0/{rid}/bgcs/worker/"
        path = path or self.root / "tool.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            for name, content in payload.items(): archive.writestr(prefix + name, content)
            if omit != "worker-assets.manifest.json":
                archive.writestr(prefix + "worker-assets.manifest.json", json.dumps(manifest))
        return path

    def test_every_declared_tool_rid_delivers_an_exact_hash_verified_worker(self):
        for rid in PACK.RIDS:
            with self.subTest(rid=rid), zipfile.ZipFile(self.worker_archive(rid)) as archive:
                PACK.verify_tool_worker_archive(archive, rid)

    def test_retired_dependency_cannot_survive_in_an_archived_worker(self):
        with zipfile.ZipFile(self.worker_archive(extra={"Newtonsoft.Json.dll": b"stale earlier closure"})) as archive:
            with self.assertRaisesRegex(RuntimeError, "unexpected=.*Newtonsoft.Json.dll"):
                PACK.verify_tool_worker_archive(archive, "win-x64")

    def test_missing_worker_artifact_or_manifest_is_rejected(self):
        for absent in ("worker-assets.manifest.json", "ClangSharp.Interop.dll"):
            with self.subTest(absent=absent), zipfile.ZipFile(self.worker_archive(omit=absent)) as archive:
                with self.assertRaises(RuntimeError): PACK.verify_tool_worker_archive(archive, "win-x64")

    def test_changed_worker_executable_bytes_are_rejected(self):
        with zipfile.ZipFile(self.worker_archive(tamper="BGCS.Contracts.dll")) as archive:
            with self.assertRaisesRegex(RuntimeError, "content digest differs"):
                PACK.verify_tool_worker_archive(archive, "win-x64")

    def test_manifest_cannot_erase_a_required_execution_dependency(self):
        def erase(manifest):
            manifest["artifacts"] = [item for item in manifest["artifacts"] if item["relativePath"] != "ClangSharp.Interop.dll"]
        with zipfile.ZipFile(self.worker_archive(omit="ClangSharp.Interop.dll", change_manifest=erase)) as archive:
            with self.assertRaisesRegex(RuntimeError, "omits required execution assets"):
                PACK.verify_tool_worker_archive(archive, "win-x64")

    def test_worker_manifest_identity_paths_and_case_collisions_are_rejected(self):
        def wrong_host(manifest): manifest["hostRid"] = "linux-x64"
        def traversal(manifest): manifest["artifacts"][0]["relativePath"] = "../escape.dll"
        def duplicate(manifest): manifest["artifacts"].append(copy.deepcopy(manifest["artifacts"][0]))
        def bad_digest(manifest): manifest["artifacts"][0]["digest"]["value"] = "unverified"
        for change in (wrong_host, traversal, duplicate, bad_digest):
            with self.subTest(change=change.__name__), zipfile.ZipFile(self.worker_archive(change_manifest=change)) as archive:
                with self.assertRaises(RuntimeError): PACK.verify_tool_worker_archive(archive, "win-x64")
        with zipfile.ZipFile(self.worker_archive(extra={"bgcs.contracts.dll": b"case alias"})) as archive:
            with self.assertRaisesRegex(RuntimeError, "case-colliding"):
                PACK.verify_tool_worker_archive(archive, "win-x64")

    def test_product_verification_rejects_stale_tool_worker_before_success_receipt(self):
        output = self.create_packages()
        with zipfile.ZipFile(output / "BindGen-CS.0.0.0-test.nupkg", "w") as archive:
            archive.writestr("toolsettings.json", "pointer fixture")
        self.worker_archive(path=output / "BindGen-CS.win-x64.0.0.0-test.nupkg",
                            extra={"Newtonsoft.Json.dll": b"retired dependency"})
        with self.assertRaisesRegex(RuntimeError, "locked file set"):
            PACK.verify_packages(output, "0.0.0-test", False, "win-x64")

    def run_execution_output_fixture(self, exit_code):
        script = ("import importlib.util,sys; "
                  f"spec=importlib.util.spec_from_file_location('pack',{str(ROOT / 'eng/pack.py')!r}); "
                  "pack=importlib.util.module_from_spec(spec); spec.loader.exec_module(pack); "
                  f"pack.execute(sys.executable,['-c',\"import sys; print('compiler stdout'); "
                  f"print('compiler stderr',file=sys.stderr); sys.exit({exit_code})\"])" )
        return subprocess.run([sys.executable, "-c", script], cwd=ROOT, capture_output=True,
                              text=True, encoding="utf-8", timeout=30)

    def test_successful_child_compiler_output_survives_no_window_execution(self):
        result = self.run_execution_output_fixture(0)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("compiler stdout", result.stdout)
        self.assertIn("compiler stderr", result.stderr)

    def test_failed_child_preserves_real_compiler_diagnostics_and_exit_failure(self):
        result = self.run_execution_output_fixture(7)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("compiler stdout", result.stdout)
        self.assertIn("compiler stderr", result.stderr)
        self.assertIn("non-zero exit status 7", result.stderr)

    def accepted_parser_fixture(self):
        spec = importlib.util.spec_from_file_location("parser_fixture_for_package_controls", ROOT / "scripts/test-parser-resource-provenance.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        fixture = module.ParserResourceProvenanceTests("test_complete_x64_digest_and_source_provenance_is_accepted")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        PACK.ROOT = fixture.root
        return fixture

    def capture_product_builds(self):
        capture = PACK.ROOT / "captured-builds.jsonl"
        # A real Python child captures each launcher argv/environment. No compiler or product is executed.
        (PACK.ROOT / "build").write_text(
            "import json,os,sys\n"
            f"with open({str(capture)!r}, 'a', encoding='utf-8') as stream:\n"
            " stream.write(json.dumps({'arguments':sys.argv[1:], 'parserRoot':os.environ.get('BGCS_CLANG_RUNTIME_DIR'),"
            "'sentinel':os.environ.get('BGCS_PACKAGE_ENV_CONTROL')})+'\\n')\n", encoding="utf-8")
        return capture

    def test_accepted_explicit_parser_root_reaches_only_worker_when_parent_has_no_runtime_override(self):
        fixture = self.accepted_parser_fixture()
        capture = self.capture_product_builds()
        with mock.patch.dict(os.environ, {"BGCS_PACKAGE_ENV_CONTROL": "parent-unchanged"}):
            os.environ.pop("BGCS_CLANG_RUNTIME_DIR", None)
            before = dict(os.environ)
            PACK.build_products(sys.executable, "Release", "0.0.0-control", fixture.directory, "c" * 40)
            self.assertEqual(before, dict(os.environ))
        calls = [json.loads(line) for line in capture.read_text(encoding="utf-8").splitlines()]
        self.assertEqual(3, len(calls))
        self.assertEqual([None, str(fixture.directory.resolve()), None], [call["parserRoot"] for call in calls])
        self.assertEqual(["parent-unchanged"] * 3, [call["sentinel"] for call in calls])
        self.assertEqual("src/BGCS.Frontend.Clang.Worker/BGCS.Frontend.Clang.Worker.csproj", calls[1]["arguments"][0])
        self.assertTrue(all(call["arguments"][-1] == "-p:Version=0.0.0-control" for call in calls))

    def test_accepted_explicit_parser_overrides_worker_ambient_value_without_changing_other_children(self):
        fixture = self.accepted_parser_fixture()
        capture = self.capture_product_builds()
        with mock.patch.dict(os.environ, {"BGCS_CLANG_RUNTIME_DIR": "caller-native-root"}):
            before = dict(os.environ)
            PACK.build_products(sys.executable, "Release", "0.0.0-control", fixture.directory, "c" * 40)
            self.assertEqual(before, dict(os.environ))
        calls = [json.loads(line) for line in capture.read_text(encoding="utf-8").splitlines()]
        self.assertEqual(["caller-native-root", str(fixture.directory.resolve()), "caller-native-root"],
                         [call["parserRoot"] for call in calls])

    def test_without_explicit_parser_all_builds_keep_the_callers_runtime_selection(self):
        capture = self.capture_product_builds()
        with mock.patch.dict(os.environ, {"BGCS_CLANG_RUNTIME_DIR": "caller-native-root"}):
            before = dict(os.environ)
            PACK.build_products(sys.executable, "Release", "0.0.0-control", None, "c" * 40)
            self.assertEqual(before, dict(os.environ))
        calls = [json.loads(line) for line in capture.read_text(encoding="utf-8").splitlines()]
        self.assertEqual(["caller-native-root"] * 3, [call["parserRoot"] for call in calls])

    def test_managed_child_removes_every_platform_case_without_changing_parent_or_native_discovery(self):
        for index, names in enumerate((("Platform",), ("PLATFORM",), ("pLaTfOrM",), ("Platform", "PLATFORM", "platform"))):
            capture = self.root / ("platform-environment-" + str(index) + ".json")
            command = ("import json,os; from pathlib import Path; "
                f"Path({str(capture)!r}).write_text(json.dumps({{"
                "'platformEntries':{key:value for key,value in os.environ.items() if key.casefold()=='platform'},"
                "'vc':os.environ.get('VCINSTALLDIR'),'sdk':os.environ.get('WindowsSdkDir'),"
                "'include':os.environ.get('INCLUDE'),'compiler':os.environ.get('CXX'),"
                "'parser':os.environ.get('BGCS_CLANG_RUNTIME_DIR')}),encoding='utf-8')")
            with self.subTest(names=names), mock.patch.dict(os.environ, {
                "VCINSTALLDIR": "retained-vc", "WindowsSdkDir": "retained-sdk", "INCLUDE": "retained-native-headers",
                "CXX": "retained-compiler", **dict.fromkeys(names, "x64")
            }):
                before = dict(os.environ)
                PACK.execute(sys.executable, ["-c", command], environment_overrides={"BGCS_CLANG_RUNTIME_DIR": "accepted-explicit-root"})
                self.assertEqual(before, dict(os.environ))
                actual = json.loads(capture.read_text(encoding="utf-8"))
                self.assertEqual({"platformEntries": {}, "vc": "retained-vc", "sdk": "retained-sdk",
                    "include": "retained-native-headers", "compiler": "retained-compiler", "parser": "accepted-explicit-root"}, actual)

    def test_invalid_or_stale_parser_provenance_rejects_before_any_build_process(self):
        fixture = self.accepted_parser_fixture()
        capture = self.capture_product_builds()
        with self.assertRaisesRegex(RuntimeError, "current BGCS revision"):
            PACK.build_products(sys.executable, "Release", "0.0.0-control", fixture.directory, "d" * 40)
        self.assertFalse(capture.exists())
        (fixture.directory / "libclang.dylib").write_bytes(b"changed native input")
        with self.assertRaisesRegex(RuntimeError, "changed after its actual build"):
            PACK.build_products(sys.executable, "Release", "0.0.0-control", fixture.directory, "c" * 40)
        self.assertFalse(capture.exists())

    def prepare_resource_notices(self):
        source = self.root / 'distribution/third-party/english-words'
        shutil.copytree(ROOT / 'distribution/third-party/english-words', source)
        resource = self.root / 'src/BGCS.CSharp/Resources/en_EN.txt'
        resource.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / 'src/BGCS.CSharp/Resources/en_EN.txt', resource)
        shutil.copyfile(ROOT / 'THIRD-PARTY-NOTICES.md', self.root / 'THIRD-PARTY-NOTICES.md')
        return source

    def test_resource_distribution_keeps_exact_license_provenance_and_attribution(self):
        source = self.prepare_resource_notices()
        output = self.root / 'notices'
        PACK.copy_resource_notices(output)
        for name in ('LICENSE.md', 'embedded-resource-provenance.json'):
            self.assertEqual((source / name).read_bytes(), (output / 'resources/english-words' / name).read_bytes())
        notice = output / 'THIRD-PARTY-NOTICES.md'
        self.assertEqual((ROOT / 'THIRD-PARTY-NOTICES.md').read_bytes(), notice.read_bytes())
        original = notice.read_text(encoding='utf-8').split('## HexaGen\n', 1)[1].split('```text\n', 1)[1].split('\n```', 1)[0] + '\n'
        self.assertEqual('14c7f3bdb1cb325dffb0ccca7288a8edd0338231615a50c8321fadb64d143453',
                         hashlib.sha256(original.encode('utf-8')).hexdigest())
        self.assertFalse((output / 'source').exists())

    def test_resource_distribution_rejects_source_transform_license_and_path_drift(self):
        source = self.prepare_resource_notices()
        original = json.loads((source / 'embedded-resource-provenance.json').read_text())
        for fault in ('resource-hash', 'upstream-hash', 'upstream-length', 'transform', 'words', 'license-hash', 'license-path'):
            value = copy.deepcopy(original)
            if fault == 'resource-hash': value['sha256'] = '0' * 64
            elif fault == 'upstream-hash': value['sourceSha256'] = '0' * 64
            elif fault == 'upstream-length': value['sourceBytes'] += 1
            elif fault == 'transform': value['transformation'] = 'guessed'
            elif fault == 'words': value['words'] += 1
            elif fault == 'license-hash': value['license']['sha256'] = '0' * 64
            else: value['license']['path'] = '../outside-license'
            (source / 'embedded-resource-provenance.json').write_text(json.dumps(value))
            with self.subTest(fault=fault), self.assertRaises(RuntimeError):
                PACK.copy_resource_notices(self.root / ('rejected-' + fault))
            self.assertFalse((self.root / ('rejected-' + fault)).exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
