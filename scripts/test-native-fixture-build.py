"""Pure command controls for repository-owned native consumption fixtures."""
import unittest
import tempfile
from pathlib import Path
from native_fixture_build import commands, msvc_sdk_inputs


class FixtureCommands(unittest.TestCase):
    def test_languages_includes_defines_and_external_link_inputs_are_explicit(self):
        result = commands("clang++", [Path("native.c"), Path("bridge.cpp")], [Path("headers with spaces")], Path("out/libsample.so"),
                          defines=("VALUE=7",), libraries=("-lm",))
        self.assertIn("-std=c17", result[0])
        self.assertIn("-std=c++23", result[1])
        self.assertIn("-Iheaders with spaces", result[0])
        self.assertIn("-DVALUE=7", result[0])
        self.assertEqual(["clang++", "-shared", "out/0.o", "out/1.o", "-lm", "-o", "out/libsample.so"], [item.replace("\\", "/") for item in result[2]])

    def test_msvc_compiles_each_language_and_links_only_objects(self):
        result = commands("cl.exe", [Path("native.c"), Path("bridge.cpp")], [], Path("out/sample.dll"))
        self.assertIn("/std:c17", result[0])
        self.assertIn("/std:c++latest", result[1])
        self.assertIn("/LD", result[2])
        self.assertIn("/IMPLIB:" + str(Path("out/sample.lib")), result[2])
        self.assertFalse(any(value.endswith((".c", ".cpp")) for value in result[2]))

    def test_empty_source_set_is_rejected(self):
        with self.assertRaises(ValueError):
            commands("clang++", [], [], Path("unused.so"))

    def test_explicit_msvc_sdk_paths_work_without_include_or_lib_environment(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for relative in ("vc/include", "vc/lib/x64", "sdk/Include/1.0/ucrt", "sdk/Include/1.0/shared", "sdk/Include/1.0/um", "sdk/Include/1.0/winrt", "sdk/Lib/1.0/ucrt/x64", "sdk/Lib/1.0/um/x64"):
                (root / relative).mkdir(parents=True)
            environment = {"VCToolsInstallDir": str(root / "vc"), "WindowsSdkDir": str(root / "sdk"), "WindowsSDKVersion": "1.0/", "VSCMD_ARG_TGT_ARCH": "x64"}
            before = dict(environment)
            includes, libraries = msvc_sdk_inputs(environment)
            self.assertEqual(before, environment)
            self.assertEqual(5, len(includes))
            self.assertEqual(3, len(libraries))
            plan = commands("cl.exe", [Path("native.c")], includes, Path("out/native.dll"), library_directories=tuple(libraries))[-1]
            self.assertGreater(plan.index("/LIBPATH:" + str(libraries[0])), plan.index("/link"))
            (root / "vc/lib/x64").rmdir()
            with self.assertRaises(FileNotFoundError):
                msvc_sdk_inputs(environment)

    def test_msvc_sdk_requires_explicit_identity_without_path_search(self):
        with self.assertRaises(RuntimeError):
            msvc_sdk_inputs({"INCLUDE": "ambient", "LIB": "ambient"})


if __name__ == "__main__":
    unittest.main()
