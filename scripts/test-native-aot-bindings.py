#!/usr/bin/env python3
"""Generate bindings and invoke the independent native C and C++ fixtures from a NativeAOT executable."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import shutil
import uuid
import cpp_acceptance_fixture


ROOT = Path(__file__).resolve().parent.parent
MODES = ("DllImport", "LibraryImport", "FunctionTable")


def load_process_helpers():
    spec = importlib.util.spec_from_file_location("wasm_acceptance", ROOT / "scripts/test-wasm-bindings.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def find_compiler(requested):
    candidates = (requested, os.environ.get("CC"), shutil.which("clang"), shutil.which("cc"),
                  str(Path(os.environ.get("PROGRAMFILES", "C:/Program Files")) / "LLVM/bin/clang.exe"))
    for value in candidates:
        if value and Path(value).is_file():
            return str(Path(value).resolve())
    raise RuntimeError("Pass --compiler with a C compiler that supports the current host target.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH") or shutil.which("dotnet"))
    parser.add_argument("--compiler")
    parser.add_argument("--inject-native-error", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/native-aot-acceptance")
    args = parser.parse_args()
    if not args.dotnet or not Path(args.dotnet).is_file():
        parser.error("Pass --dotnet with an SDK that supports NativeAOT for the current host.")
    helper = load_process_helpers()
    dotnet = str(Path(args.dotnet).resolve())
    work = args.output.resolve() / uuid.uuid4().hex
    work.mkdir(parents=True)
    report = {"success": False, "host": platform.platform(), "modes": list(MODES),
              "injectedNativeError": args.inject_native_error}
    environment = helper.acceptance_environment(dotnet)
    print(f"NativeAOT acceptance evidence: {work}", flush=True)
    try:
        compiler = find_compiler(args.compiler)
        report["compiler"] = compiler
        (work / "global.json").write_text(json.dumps({"sdk": {
            "version": "9.0.100", "rollForward": "latestFeature"}}), encoding="utf-8")
        shutil.copytree(ROOT / "tests/fixtures/NativeApi", work / "Native")
        if args.inject_native_error:
            source = work / "Native/api.c"
            source.write_text(source.read_text(encoding="utf-8").replace("return left + right;", "return left + right + 1;"), encoding="utf-8")
        tool = helper.build_project(dotnet, ROOT / "src/BGCS.Tool/BGCS.Tool.csproj", work,
                                    work / "generator-build.log", environment)
        runtime = helper.build_project(dotnet, ROOT / "src/BGCS.Runtime/BGCS.Runtime.csproj", work,
                                       work / "runtime-build.log", environment)
        shutil.copyfile(runtime, work / "BGCS.Runtime.dll")
        bridge_source, _ = cpp_acceptance_fixture.prepare(
            ROOT, work, dotnet, tool, compiler, environment, helper.run)
        architecture = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
        rid = ("win" if os.name == "nt" else "osx" if platform.system() == "Darwin" else "linux") + "-" + architecture
        library = "api.dll" if os.name == "nt" else "libapi.dylib" if platform.system() == "Darwin" else "libapi.so"
        native_object = work / ("native-api.obj" if os.name == "nt" else "native-api.o")
        c_command = [compiler, "-std=c17", "-DBUILD_SHARED=1", "-c", str(work / "Native/api.c"),
                     "-o", str(native_object)]
        if os.name != "nt":
            c_command.append("-fPIC")
        helper.run(c_command, work, work / "native-c-build.log", environment)
        command = [cpp_acceptance_fixture.cxx_compiler(compiler), "-std=c++17", "-shared",
                   str(native_object), str(bridge_source), "-I" + str(work / "Bridge/include"),
                   "-I" + str(work / "CppFacade"), "-DBUILD_SHARED=1", "-o", str(work / library)]
        if os.name == "nt":
            exports = sorted(set(re.findall(r"\b(bgcs_\w+)\s*\(", (work / "Native/api.h").read_text(encoding="utf-8"))))
            command += ["-Wl,/EXPORT:" + symbol for symbol in exports]
        else:
            command.append("-fPIC")
        helper.run(command, work, work / "native-build.log", environment)
        for mode in MODES:
            config = {"preset": "host-c", "apiName": "NativeApi", "namespace": "NativeAotAcceptance." + mode,
                      "libName": "api", "entryFiles": ["Native/api.h", "Bridge/include/Classes.h", "Bridge/include/common.h"], "outputPath": "Generated/" + mode,
                      "compilerPath": compiler, "importType": mode, "useCustomContext": mode == "FunctionTable",
                      "parseSystemIncludes": False, "parseMacros": False, "parseComments": False,
                      "generateExtensions": False, "delegatesAsVoidPointer": False, "generateHandles": True,
                      "generateRuntimeSource": False, "mergeGeneratedFilesToSingleFile": True,
                      "singleFileOutputName": "Bindings.cs",
                      "typeMappings": {"BgcsWideFlags": "BgcsWideFlags"},
                      "ignoredTypedefs": ["BgcsWideFlags"],
                      "customEnums": [{"cppName": "BgcsWideFlags", "name": "BgcsWideFlags",
                                       "baseType": "ulong", "items": [
                                           {"cppName": "BGCS_WIDE_HIGH_BIT", "name": "HighBit",
                                            "cppValue": "1ULL << 60", "value": "1UL << 60"}]}]}
            path = work / (mode + ".json")
            path.write_text(json.dumps(config, indent=2), encoding="utf-8")
            helper.run([dotnet, str(tool), "generate", str(path)], work,
                       work / (mode + "-generation.log"), environment)
        template = (ROOT / "tests/native-aot/Program.cs.in").read_text(encoding="utf-8")
        invocation = (ROOT / "tests/wasm/Invocation.cs.in").read_text(encoding="utf-8")
        runners = "\n".join(invocation.replace("__MODE__", mode) for mode in MODES)
        (work / "Program.cs").write_text(template.replace("__RUNNERS__", runners), encoding="utf-8")
        shutil.copyfile(ROOT / "tests/native-aot/Consumer.project.xml", work / "Consumer.csproj")
        published = work / "published"
        helper.run([dotnet, "publish", "Consumer.csproj", "-c", "Release", "-r", rid, "-o", str(published),
                    "--disable-build-servers", "-m:1", "-nodeReuse:false"], work, work / "publish.log", environment)
        shutil.copyfile(work / library, published / library)
        executable = published / ("BGCS.NativeAot.Acceptance.exe" if os.name == "nt" else "BGCS.NativeAot.Acceptance")
        if not executable.is_file() or (published / "BGCS.NativeAot.Acceptance.runtimeconfig.json").exists():
            raise RuntimeError("The published consumer is not a standalone NativeAOT executable.")
        invocation_log = work / "invocation.log"
        try:
            raw = helper.run([str(executable)], published, invocation_log, environment)
        except RuntimeError:
            # The consumer returns a structured failure before exiting unsuccessfully.
            # Preserve that native call evidence without accepting the process failure.
            try:
                report["invocation"] = json.loads(invocation_log.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError) as error:
                report["invocationError"] = f"Invalid consumer report in {invocation_log}: {error}"
            raise
        invocation_report = json.loads(raw)
        report["invocation"] = invocation_report
        expected = {mode + ":" + check for mode in MODES for check in (
            "scalar", "wide-integer", "wide-enum", "native-long", "bool", "layout", "bitfields", "buffer", "capacity", "handle", "callback", "null-callback", "release",
            "cpp-construction", "cpp-method", "cpp-inheritance", "cpp-release")}
        expected.update(("missing-symbol", "function-table-context", "failed-initialization", "borrowed-context", "borrowed-storage"))
        checks = invocation_report.get("checks", [])
        if not invocation_report.get("success") or len(checks) != len(expected) or set(checks) != expected:
            raise RuntimeError("NativeAOT invocation did not complete every required check.")
        report["success"] = True
        print(f"Passed: {len(checks)} checks across {len(MODES)} import modes.", flush=True)
    except Exception as error:
        report["error"] = str(error)
        print(f"FAILED: {error}", flush=True)
    finally:
        path = work / "report.json"
        path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"Report: {path}", flush=True)
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
