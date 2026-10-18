"""Repository corpus syntax checks consume source manifests and explicit fixture inputs."""
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("corpus_gate", Path(__file__).with_name("test-corpus.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
upstream_spec = importlib.util.spec_from_file_location("upstream_gate", Path(__file__).with_name("test-upstream-native.py"))
upstream = importlib.util.module_from_spec(upstream_spec)
upstream_spec.loader.exec_module(upstream)


class CorpusBridgeInputs(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "Generated"
        self.root.mkdir()
        self.source = {"relativePath": "host/native/src/assertions.cpp", "mediaType": "text/x-c++", "digest": {"value": "source"}}
        self.header = {"relativePath": "host/native/include/bridge.h", "mediaType": "text/x-c-header", "digest": {"value": "header"}}
        self.receipt = {"relativePath": "host/native/metadata/bridge.manifest.json", "mediaType": "application/json"}
        for item in (self.source, self.header, self.receipt):
            path = self.root / item["relativePath"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("/* exact fixture */", encoding="utf-8")
        self.native = {"schemaVersion": 1, "targetId": "host", "languageStandard": "c++23", "sources": [self.source], "headers": [self.header]}
        self.manifest = {"artifacts": [self.source, self.header, self.receipt]}
        self.compiler = Path(self.temporary.name) / "clang++"
        self.compiler.write_bytes(b"fixture-compiler-not-executed")
        self.request = {"targets": [{"id": "host", "settings": {"compilerPath": str(self.compiler)}}],
                        "source": {"includeDirectories": [str(self.root / "user headers")], "defines": ["INPUT=42"], "arguments": ["-fno-exceptions"]}}
        self.calls = []

    def run_fixture(self):
        (self.root / self.receipt["relativePath"]).write_text(json.dumps(self.native), encoding="utf-8")
        return gate.compile_bridge(self.root, self.manifest, self.request, lambda args, name: self.calls.append((args, name)))

    def test_source_only_manifest_uses_explicit_compiler_and_request_inputs(self):
        with patch.dict(os.environ, {}, clear=True):
            result = self.run_fixture()
        command = self.calls[0][0]
        self.assertIn("-std=c++23", command)
        self.assertIn("-fsyntax-only", command)
        self.assertIn("-DINPUT=42", command)
        self.assertIn("-I" + self.request["source"]["includeDirectories"][0], command)
        self.assertIn("-I" + str(self.root / "host/native/include"), command)
        self.assertIn("-fno-exceptions", command)
        self.assertEqual(str(self.root / self.source["relativePath"]), command[-1])
        self.assertEqual(1, result["translationUnits"])
        self.assertFalse(result["nativeLinked"])
        self.assertFalse(result["upstreamInvoked"])

    def test_msvc_sdk_paths_are_explicit_and_do_not_depend_on_include_environment(self):
        self.compiler = self.root / "cl.exe"
        self.compiler.write_bytes(b"fixture-msvc-not-executed")
        self.request["targets"][0]["settings"]["compilerPath"] = str(self.compiler)
        self.request["source"]["arguments"] = []
        for relative in ("vc/include", "vc/lib/x64", "sdk/Include/1.0/ucrt", "sdk/Include/1.0/shared", "sdk/Include/1.0/um", "sdk/Include/1.0/winrt", "sdk/Lib/1.0/ucrt/x64", "sdk/Lib/1.0/um/x64"):
            (self.root / relative).mkdir(parents=True)
        environment = {"VCToolsInstallDir": str(self.root / "vc"), "WindowsSdkDir": str(self.root / "sdk"), "WindowsSDKVersion": "1.0/", "VSCMD_ARG_TGT_ARCH": "x64"}
        with patch.dict(os.environ, environment, clear=True):
            self.run_fixture()
        command = self.calls[0][0]
        self.assertIn("/Zs", command)
        self.assertIn("/I" + str(self.root / "vc/include"), command)
        self.assertIn("/I" + str(self.root / "sdk/Include/1.0/ucrt"), command)
        self.assertIn("/DINPUT=42", command)

    def test_missing_or_mismatched_manifest_sources_never_compile(self):
        for change in (lambda: self.native.update(sources=[]), lambda: self.native.update(sources=[dict(self.source, digest={"value": "other"})]), lambda: self.manifest.update(artifacts=[self.source, self.header])):
            with self.subTest(change=change):
                change()
                with self.assertRaises(RuntimeError):
                    self.run_fixture()
                self.assertEqual([], self.calls)

    def test_missing_explicit_compiler_and_foreign_or_multiple_targets_are_refused(self):
        for targets in ([], [{"id": "foreign"}], [{"id": "host"}, {"id": "host"}]):
            with self.subTest(targets=targets), patch.dict(os.environ, {}, clear=True):
                self.request["targets"] = targets
                with self.assertRaises(RuntimeError):
                    self.run_fixture()
                self.assertEqual([], self.calls)

    def publish_owned_fixture(self, include_header=True):
        self.source["digest"]["value"] = hashlib.sha256((self.root / self.source["relativePath"]).read_bytes()).hexdigest()
        if include_header:
            self.header["digest"]["value"] = hashlib.sha256((self.root / self.header["relativePath"]).read_bytes()).hexdigest()
        else:
            (self.root / self.header["relativePath"]).unlink()
            self.native["headers"] = []
        metadata = self.root / self.receipt["relativePath"]
        metadata.write_text(json.dumps(self.native), encoding="utf-8")
        self.receipt["digest"] = {"value": hashlib.sha256(metadata.read_bytes()).hexdigest()}
        artifacts = [self.source, self.receipt] + ([self.header] if include_header else [])
        (self.root / ".bgcs-artifacts.json").write_text(json.dumps({"schemaVersion": 1, "libraryId": "bimg", "artifacts": artifacts}), encoding="utf-8")

    def test_actual_text_x_c_header_is_owned_header_not_a_compilation_unit(self):
        self.header["mediaType"] = "text/x-c"
        self.publish_owned_fixture()
        sources, directories = upstream.generated_native_inputs(self.root, "bimg", required=True)
        self.assertEqual([(self.root / self.source["relativePath"]).as_posix()], sources)
        self.assertEqual([(self.root / self.header["relativePath"]).parent.as_posix()], directories)

    def test_upstream_requires_owned_header_without_a_directory_wildcard_fallback(self):
        self.publish_owned_fixture(include_header=False)
        with self.assertRaisesRegex(RuntimeError, "complete validated generated bridge sources and headers"):
            upstream.generated_native_inputs(self.root, "bimg", required=True)

    def test_header_digest_mismatch_is_refused_before_upstream_compilation(self):
        self.header["mediaType"] = "text/x-c"
        self.publish_owned_fixture()
        (self.root / self.header["relativePath"]).write_text("/* changed */", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "missing or modified"):
            upstream.generated_native_inputs(self.root, "bimg", required=True)


if __name__ == "__main__":
    unittest.main()
