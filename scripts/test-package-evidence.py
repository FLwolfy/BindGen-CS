#!/usr/bin/env python3
"""Exercise package provenance rejection with real Git trees and archive bytes."""
from __future__ import annotations

import importlib.util
import contextlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
import zipfile

from acceptance_evidence import digest, source_identity, package_version, discover_test_projects
from cpp_acceptance_fixture import execution_identity

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("package_evidence_verifier", ROOT / "eng/verify-packages.py")
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


class PackageEvidenceTests(unittest.TestCase):
    def test_failed_child_emits_bounded_stdout_and_separate_stderr_without_losing_logs(self):
        output = io.StringIO()
        command = [sys.executable, "-c", "import sys; print('discarded-prefix-'+'x'*20000); print('stdout-tail-marker'); print('stderr-tail-marker',file=sys.stderr); sys.exit(7)"]
        with contextlib.redirect_stderr(output), self.assertRaisesRegex(RuntimeError, "failed-child exited 7"):
            VERIFIER.run_process(command, self.repository, self.packages, "failed-child", separate_stderr=True)
        diagnostic = output.getvalue()
        self.assertIn("stdout-tail-marker", diagnostic)
        self.assertIn("stderr-tail-marker", diagnostic)
        self.assertNotIn("discarded-prefix", diagnostic)
        self.assertLess(len(diagnostic), 17000)
        self.assertIn("discarded-prefix", (self.packages / "failed-child.log").read_text())
        self.assertIn("stderr-tail-marker", (self.packages / "failed-child.stderr.log").read_text())

    def consumer_outputs(self):
        work = self.repository / "artifacts/temporary/package-consumers-owned-fixture"
        (work / "sdk/bin").mkdir(parents=True)
        (work / "sdk/obj").mkdir()
        (work / "sdk/bin/Consumer.dll").write_bytes(b"completed executable")
        (work / "sdk/native.h").write_text("int value(void);", encoding="utf-8")
        (work / "plugin").mkdir()
        (work / "plugin/Plugin.dll").write_bytes(b"retained CLI plugin")
        return work

    def test_completed_consumer_outputs_release_only_owned_copies_and_retain_audit(self):
        work = self.consumer_outputs()
        with mock.patch.object(VERIFIER, "ROOT", self.repository), mock.patch.object(VERIFIER, "check_free_space"):
            VERIFIER.release_consumer_outputs(work, self.packages, "sdk-fixture", ("sdk/bin", "sdk/obj"))
        self.assertFalse((work / "sdk/bin").exists())
        self.assertFalse((work / "sdk/obj").exists())
        self.assertEqual(b"retained CLI plugin", (work / "plugin/Plugin.dll").read_bytes())
        self.assertEqual("int value(void);", (work / "sdk/native.h").read_text(encoding="utf-8"))
        audit = json.loads((self.packages / "released-sdk-fixture.json").read_text())
        self.assertTrue(audit["complete"])
        self.assertEqual(len(b"completed executable"), sum(item["bytes"] for item in audit["outputs"]))
        self.assertTrue(all(item["released"] for item in audit["outputs"]))

    def test_consumer_release_refuses_plugins_traversal_and_another_workspace(self):
        work = self.consumer_outputs()
        with mock.patch.object(VERIFIER, "ROOT", self.repository), mock.patch.object(VERIFIER, "check_free_space"):
            for paths in (("plugin",), ("sdk/../plugin",), ("../../sdk/bin",), ("sdk/bin", "sdk/bin")):
                with self.subTest(paths=paths), self.assertRaises(ValueError):
                    VERIFIER.release_consumer_outputs(work, self.packages, "refused", paths)
            with self.assertRaises(RuntimeError):
                VERIFIER.release_consumer_outputs(work / "sdk", self.packages, "refused", ("sdk/bin",))
        self.assertTrue((work / "sdk/bin/Consumer.dll").is_file())
        self.assertTrue((work / "plugin/Plugin.dll").is_file())
        self.assertFalse((self.packages / "released-refused.json").exists())

    def test_consumer_release_rejects_reparse_metadata_before_removing_any_output(self):
        work = self.consumer_outputs()
        original = Path.lstat
        linked = work / "sdk/bin/Consumer.dll"
        def metadata(path, *args, **kwargs):
            value = original(path, *args, **kwargs)
            if path == linked:
                return SimpleNamespace(st_mode=value.st_mode, st_dev=value.st_dev, st_ino=value.st_ino,
                    st_size=value.st_size, st_file_attributes=0x400)
            return value
        with mock.patch.object(VERIFIER, "ROOT", self.repository), mock.patch.object(VERIFIER, "check_free_space"), \
                mock.patch.object(Path, "lstat", metadata):
            with self.assertRaisesRegex(RuntimeError, "linked paths"):
                VERIFIER.release_consumer_outputs(work, self.packages, "refused-link", ("sdk/obj", "sdk/bin"))
        self.assertTrue((work / "sdk/obj").is_dir())
        self.assertTrue(linked.is_file())
        self.assertFalse((self.packages / "released-refused-link.json").exists())

    def test_private_version_binds_the_exact_commit_and_dirty_source_without_becoming_formal(self):
        props = self.repository / 'Directory.Build.props'
        props.write_text('<Project><PropertyGroup><Version>4.1.1</Version></PropertyGroup></Project>', encoding='utf-8')
        first_source = source_identity(self.repository)
        first = package_version(self.repository)
        self.assertEqual('4.1.1-ci.g' + first_source['revision'][:12] + '.s' + first_source['checkedOutSourceSha256'][:16], first)
        self.assertEqual(first, package_version(self.repository))
        self.assertTrue(first_source['workingTreeDirty'])
        (self.repository / 'source.txt').write_text('different reviewed source\n', encoding='utf-8')
        self.assertNotEqual(first, package_version(self.repository))
        self.assertEqual('4.1.1', package_version(self.repository, declared=True))
        props.write_text('<Project><PropertyGroup><Version>4.1.1-ci.local</Version></PropertyGroup></Project>', encoding='utf-8')
        self.assertRegex(package_version(self.repository, declared=True), r'^4\.1\.1-ci\.g[0-9a-f]{12}\.s[0-9a-f]{16}$')
        props.write_text('<Project><PropertyGroup><Version>4.0.1</Version></PropertyGroup></Project>', encoding='utf-8')
        with self.assertRaises(RuntimeError): package_version(self.repository)

    def test_complete_test_discovery_includes_nested_extensions_and_excludes_generated_projects(self):
        test = '<Project><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /></ItemGroup></Project>'
        included = ['tests/Root/Root.csproj', 'tests/extensions/Frontend.Clang/Frontend.csproj', 'tests/extensions/Targets.Desktop/Targets.csproj']
        for name in included + ['tests/Root/obj/generated/Hidden.csproj', 'tests/extensions/bin/Hidden.csproj']:
            path = self.repository / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(test, encoding='utf-8')
        library = self.repository / 'tests/fixtures/Library/Library.csproj'; library.parent.mkdir(parents=True)
        library.write_text('<Project />', encoding='utf-8')
        self.assertEqual(sorted(included), [path.relative_to(self.repository).as_posix() for path in discover_test_projects(self.repository)])

    def test_real_release_workflow_requires_all_optional_products_and_symbols_without_extra_archives(self):
        workflow = (ROOT / '.github/workflows/publish-bgcs-runtime-nuget.yml').read_text(encoding='utf-8')
        block = workflow.split('      - name: Validate release package set before signing or upload', 1)[1].split('      - name:', 1)[0]
        script = re.search(r"          python - <<'PY'\n(.*?)\n          PY", block, re.DOTALL)
        self.assertIsNotNone(script)
        code = '\n'.join(line.removeprefix('          ') for line in script.group(1).splitlines())
        manifest = self.repository / 'distribution/manifests/product-packages.json'; manifest.parent.mkdir(parents=True)
        manifest.write_text(json.dumps(VERIFIER.PRODUCT_MANIFEST), encoding='utf-8')
        target = self.repository / 'artifacts/nuget'; target.mkdir(parents=True)
        for package in self.packages.glob('*.nupkg'): (target / package.name).write_bytes(package.read_bytes())
        for package in self.packages.glob('*.snupkg'): (target / package.name).write_bytes(package.read_bytes())
        def invoke():
            return subprocess.run([sys.executable, '-c', code], cwd=self.repository,
                env={**os.environ, 'VERSION': '0.0.0-test'}, text=True, capture_output=True)
        self.assertEqual(0, invoke().returncode)
        optional = target / 'BGCS.Extension.BrowserWasm.0.0.0-test.nupkg'; content = optional.read_bytes(); optional.unlink()
        self.assertNotEqual(0, invoke().returncode)
        optional.write_bytes(content)
        symbols = target / 'BGCS.Extension.NativeAot.0.0.0-test.snupkg'; content = symbols.read_bytes(); symbols.unlink()
        self.assertNotEqual(0, invoke().returncode)
        symbols.write_bytes(content); (target / 'unrelated.0.0.0-test.nupkg').write_bytes(b'extra')
        self.assertNotEqual(0, invoke().returncode)

    def test_external_consumer_boundary_stops_all_three_parent_repository_imports(self):
        from installed_custom_target import isolate_consumer_root
        parent = self.repository / 'parent'
        child = parent / 'ordinary-consumer'
        parent.mkdir()
        names = ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')
        for name in names:
            (parent / name).write_text('<Project><Import Project="repository-only.targets" /></Project>', encoding='utf-8')
        isolate_consumer_root(child)
        for name in names:
            self.assertEqual('<Project />\n', (child / name).read_text(encoding='utf-8'))
            self.assertIn('repository-only.targets', (parent / name).read_text(encoding='utf-8'))
        with self.assertRaises(RuntimeError):
            isolate_consumer_root(child)

    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT):
            raise RuntimeError("The disposable provenance test root escaped the workspace.")
        self.temporary = tempfile.TemporaryDirectory(prefix="package-evidence-", dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.repository = Path(self.temporary.name)
        self.hooks = self.repository / "empty-git-hooks"
        self.hooks.mkdir()
        self.original_root = VERIFIER.ROOT
        VERIFIER.ROOT = self.repository
        self.addCleanup(setattr, VERIFIER, "ROOT", self.original_root)
        self.git("init", "--quiet")
        (self.repository / ".gitignore").write_text("packages/\n", encoding="utf-8")
        (self.repository / "source.txt").write_text("reviewed source\n", encoding="utf-8")
        self.git("add", ".")
        self.git("-c", "user.name=BGCS Evidence Tests", "-c", "user.email=evidence@example.invalid", "commit", "--quiet", "-m", "test(evidence): establish isolated source")
        self.identity = source_identity(self.repository)
        self.packages = self.repository / "packages"
        self.packages.mkdir()
        self.archive = self.packages / "BGCS.0.0.0-test.nupkg"
        manifest = VERIFIER.PRODUCT_MANIFEST
        names = {f"{name}.0.0.0-test.nupkg" for name in manifest["products"] + manifest["optionalProducts"] + manifest["technicalPackages"]}
        names |= {f"BindGen-CS.{rid}.0.0.0-test.nupkg" for rid in manifest["toolRuntimeIdentifiers"]}
        names |= {f"{name}.0.0.0-test.snupkg" for name in manifest["symbolProducts"]}
        for name in names:
            with zipfile.ZipFile(self.packages / name, "w") as package:
                package.writestr("proof.txt", "actual archive payload for " + name)
        self.receipt = {"schemaVersion": 1, "version": "0.0.0-test", "completeProductSet": True,
                        "sourceRevision": self.identity["revision"], "sourceWorktreeDirty": False,
                        "checkedOutSourceSha256": self.identity["checkedOutSourceSha256"],
                        "sourceRevisionStable": True, "sourceBytesStable": True,
                        "sourceAtCompletion": {"sourceWorktreeDirty": False},
                        "packages": [{"file": path.name, "bytes": path.stat().st_size,
                                      "sha256": digest(path)} for path in sorted(self.packages.iterdir())]}

    def test_standard_worker_content_copy_preserves_paths_bytes_and_complete_set(self):
        package = self.packages / 'content.nupkg'
        output = self.packages / 'consumer'
        files = {'bgcs/worker-assets/worker-common.zip': b'locked common archive',
                 'bgcs/worker-assets/manifests/win-x64.json': b'locked host manifest'}
        with zipfile.ZipFile(package, 'w') as archive:
            for name, data in files.items(): archive.writestr('contentFiles/any/net10.0/' + name, data)
        for name, data in files.items():
            path = output / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
        VERIFIER.verify_sdk_content_copy(package, output)
        archive_path = output / 'bgcs/worker-assets/worker-common.zip'
        archive_path.write_bytes(b'changed common archive')
        with self.assertRaises(RuntimeError): VERIFIER.verify_sdk_content_copy(package, output)
        archive_path.write_bytes(files['bgcs/worker-assets/worker-common.zip'])
        extra = output / 'bgcs/worker-assets/stale.dll'; extra.write_bytes(b'undeclared')
        with self.assertRaises(RuntimeError): VERIFIER.verify_sdk_content_copy(package, output)
        extra.unlink(); archive_path.unlink()
        with self.assertRaises(RuntimeError): VERIFIER.verify_sdk_content_copy(package, output)

    def git(self, *arguments):
        subprocess.run(["git", "-c", "core.hooksPath=" + str(self.hooks), "-c", "commit.gpgSign=false",
                        "-c", "init.templateDir=" + str(self.hooks), *arguments],
                       cwd=self.repository, check=True, capture_output=True)

    def verify(self):
        (self.packages / "package-verification.json").write_text(json.dumps(self.receipt), encoding="utf-8")
        return VERIFIER.verify_package_receipt(self.packages, "0.0.0-test", self.identity["revision"], True)

    def test_exact_clean_source_and_archive_are_accepted(self):
        self.assertTrue(self.verify()["physicalPackageDigestsVerified"])

    def test_source_digest_and_during_pack_change_are_rejected(self):
        for field, value in (("checkedOutSourceSha256", "0" * 64), ("sourceBytesStable", False)):
            with self.subTest(field=field):
                original = self.receipt[field]
                self.receipt[field] = value
                with self.assertRaisesRegex(RuntimeError, "exact source revision"):
                    self.verify()
                self.receipt[field] = original

    def test_already_dirty_tree_edits_change_the_byte_identity(self):
        source = self.repository / "source.txt"
        source.write_text("first local edit\n", encoding="utf-8")
        first = source_identity(self.repository)
        source.write_text("second local edit\n", encoding="utf-8")
        second = source_identity(self.repository)
        self.assertTrue(first["workingTreeDirty"] and second["workingTreeDirty"])
        self.assertEqual(first["revision"], second["revision"])
        self.assertNotEqual(first["checkedOutSourceSha256"], second["checkedOutSourceSha256"])

    def test_archive_bytes_changed_after_pack_are_rejected(self):
        with self.archive.open("ab") as stream:
            stream.write(b"changed after packaging")
        with self.assertRaisesRegex(RuntimeError, "physical package changed"):
            self.verify()

    def test_consistent_receipt_cannot_label_a_missing_rid_as_complete(self):
        omitted = self.packages / "BindGen-CS.osx-x64.0.0.0-test.nupkg"
        omitted.unlink()
        self.receipt["packages"] = [entry for entry in self.receipt["packages"] if entry["file"] != omitted.name]
        with self.assertRaisesRegex(RuntimeError, "declared physical product, RID and symbol"):
            self.verify()

    def test_dependent_generator_assembly_changes_the_execution_identity(self):
        closure = self.packages / "execution"
        closure.mkdir()
        tool = closure / "BGCS.Tool.dll"
        dependency = closure / "BGCS.CSharp.dll"
        runtime = self.packages / "BGCS.Runtime.dll"
        tool.write_bytes(b"unchanged tool entry")
        dependency.write_bytes(b"first backend bytes")
        runtime.write_bytes(b"runtime bytes")
        first = execution_identity(self.repository, tool, runtime)
        dependency.write_bytes(b"changed backend bytes")
        second = execution_identity(self.repository, tool, runtime)
        self.assertEqual(first["generatorEntrySha256"], second["generatorEntrySha256"])
        self.assertEqual(2, first["executionAssetCount"])
        self.assertNotEqual(first["executionClosureSha256"], second["executionClosureSha256"])

    def test_clean_source_seal_survives_real_managed_source_iteration(self):
        source_at_start = source_identity(self.repository)
        consumer = self.packages / "consumer"
        consumer.mkdir()
        for name in ("First.cs", "Second.cs"):
            (consumer / name).write_text("partial class Consumer {}")
        compiled_paths = []
        for managed_source in consumer.glob("*.cs"):
            compiled_paths.append(str(managed_source))
        self.assertEqual(2, len(compiled_paths))
        report = {"success": False}
        VERIFIER.record_source_completion(report, source_at_start, source_identity(self.repository), require_clean=True)
        self.assertTrue(report["sourceBytesStable"])
        self.assertFalse(report["sourceAtCompletion"]["sourceWorktreeDirty"])
        self.assertEqual(source_at_start["revision"], report["sourceAtCompletion"]["sourceRevision"])

    def test_actual_source_edit_fails_the_final_seal_even_in_partial_mode(self):
        initial = source_identity(self.repository)
        (self.repository / "source.txt").write_text("changed during consumption\n")
        report = {"success": True}
        with self.assertRaisesRegex(RuntimeError, "verifier source changed"):
            VERIFIER.record_source_completion(report, initial, source_identity(self.repository), require_clean=False)
        self.assertFalse(report["success"])
        self.assertFalse(report["sourceBytesStable"])

    def test_new_clean_commit_cannot_replace_the_original_consumption_revision(self):
        initial = source_identity(self.repository)
        (self.repository / "source.txt").write_text("new committed source\n")
        self.git("add", "source.txt")
        self.git("-c", "user.name=BGCS Evidence Tests", "-c", "user.email=evidence@example.invalid",
                 "commit", "--quiet", "-m", "test(evidence): change source revision")
        final = source_identity(self.repository)
        self.assertFalse(final["workingTreeDirty"])
        self.assertNotEqual(initial["revision"], final["revision"])
        report = {"success": True}
        with self.assertRaises(RuntimeError):
            VERIFIER.record_source_completion(report, initial, final, require_clean=True)
        self.assertFalse(report["success"])

    def test_real_changed_source_final_seal_exits_nonzero_with_unsuccessful_receipt(self):
        receipt = self.packages / "final-seal.json"
        script = ("import importlib.util,json,pathlib,sys; "
                  f"sys.path.insert(0,{str(ROOT / 'scripts')!r}); "
                  f"spec=importlib.util.spec_from_file_location('verifier',{str(ROOT / 'eng/verify-packages.py')!r}); "
                  "verifier=importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier); "
                  f"root=pathlib.Path({str(self.repository)!r}); "
                  "initial=verifier.source_identity(root); "
                  "(root/'source.txt').write_text('real source mutation'); report={'success':True};\n"
                  "try: verifier.record_source_completion(report,initial,verifier.source_identity(root),require_clean=False)\n"
                  f"finally: pathlib.Path({str(receipt)!r}).write_text(json.dumps(report))\n")
        result = subprocess.run([sys.executable, "-c", script], cwd=ROOT, capture_output=True,
                                text=True, encoding="utf-8", timeout=30)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("verifier source changed", result.stderr)
        actual = json.loads(receipt.read_text())
        self.assertFalse(actual["success"])
        self.assertFalse(actual["sourceBytesStable"])

    def test_initial_dirty_source_is_never_complete_certification(self):
        (self.repository / "source.txt").write_text("uncommitted source\n")
        initial = source_identity(self.repository)
        report = {"success": True}
        with self.assertRaises(RuntimeError):
            VERIFIER.record_source_completion(report, initial, source_identity(self.repository), require_clean=True)
        self.assertTrue(report["sourceBytesStable"])
        self.assertFalse(report["success"])

    def test_plugin_copies_the_exact_installed_host_contract_and_preserves_inputs(self):
        host = self.packages / "host"
        plugin = self.packages / "plugin"
        host.mkdir()
        plugin.mkdir()
        original = b"exact shared installed host Contracts fixture"
        (host / "BGCS.Core.dll").write_bytes(original)
        VERIFIER.copy_installed_plugin_contract(host, plugin)
        VERIFIER.copy_installed_plugin_contract(host, plugin)
        self.assertEqual(original, (plugin / "BGCS.Core.dll").read_bytes())
        self.assertEqual(original, (host / "BGCS.Core.dll").read_bytes())

    def test_plugin_cannot_keep_different_contracts_or_invent_a_missing_host_asset(self):
        host = self.packages / "host"
        plugin = self.packages / "plugin"
        host.mkdir()
        plugin.mkdir()
        with self.assertRaises(FileNotFoundError):
            VERIFIER.copy_installed_plugin_contract(host, plugin)
        self.assertFalse((plugin / "BGCS.Core.dll").exists())
        (host / "BGCS.Core.dll").write_bytes(b"installed exact host")
        (plugin / "BGCS.Core.dll").write_bytes(b"contradictory private copy")
        with self.assertRaisesRegex(RuntimeError, "differs from its exact installed SDK host"):
            VERIFIER.copy_installed_plugin_contract(host, plugin)
        self.assertEqual(b"contradictory private copy", (plugin / "BGCS.Core.dll").read_bytes())


if __name__ == "__main__":
    unittest.main(verbosity=2)
