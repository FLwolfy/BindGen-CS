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
from acceptance_evidence import digest


ROOT = Path(__file__).resolve().parent.parent
MODES = cpp_acceptance_fixture.MODES


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


def prepare_aggregates(work):
    """Add real C ABI transformations to the fixture's explicitly owned export table."""
    directory = work / "AggregateAbi"
    shutil.copytree(ROOT / "tests/fixtures/AggregateAbi", directory)
    header = directory / "aggregates.h"
    symbols = sorted(set(re.findall(r"\b(bgcs_aggregate_\w+)\s*\(", header.read_text(encoding="utf-8"))))
    if len(symbols) != len(cpp_acceptance_fixture.AGGREGATE_CHECKS):
        raise RuntimeError("The complete native aggregate operation fixture was not declared.")
    native = work / "Native/api.c"
    source = '#include "' + header.as_posix() + '"\n' + native.read_text(encoding="utf-8")
    marker = "    #undef BGCS_FIXTURE_SYMBOL"
    if source.count(marker) != 1:
        raise RuntimeError("The fixture has no unique owned native export-table insertion point.")
    source = source.replace(marker, "\n".join("    BGCS_FIXTURE_SYMBOL(" + symbol + ")" for symbol in symbols) + "\n" + marker)
    native.write_text(source, encoding="utf-8")
    return {"header": header, "source": directory / "aggregates.c", "probe": directory / "probe.c", "symbols": symbols}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH") or shutil.which("dotnet"))
    parser.add_argument("--compiler")
    parser.add_argument("--tool-path", type=Path)
    parser.add_argument("--runtime-path", type=Path)
    parser.add_argument("--local-checkpoint", action="store_true", help="Mark the selected tool/runtime as private repository binaries rather than installed products.")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--configuration", default="Release", choices=("Debug", "Release"))
    parser.add_argument("--keep-build-output", action="store_true")
    parser.add_argument("--inject-native-error", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/native-aot-acceptance")
    args = parser.parse_args()
    if not args.dotnet or not Path(args.dotnet).is_file():
        parser.error("Pass --dotnet with an SDK that supports NativeAOT for the current host.")
    if bool(args.tool_path) != bool(args.runtime_path):
        parser.error("--tool-path and --runtime-path must identify the same installed product set together.")
    if args.local_checkpoint and not args.tool_path:
        parser.error("--local-checkpoint requires an explicit private tool/runtime pair.")
    helper = load_process_helpers()
    dotnet = str(Path(args.dotnet).resolve())
    work = args.output.resolve() / uuid.uuid4().hex
    work.mkdir(parents=True)
    report = {"success": False, "host": platform.platform(), "managedRuntime": "dotnet.native-aot", "modes": list(MODES),
              "injectedNativeError": args.inject_native_error, "installedProducts": bool(args.tool_path) and not args.local_checkpoint,
              "privateRepositoryCheckpoint": args.local_checkpoint,
              "wideEnumPresentation": "generator-configured UInt64 enum; friendly and raw native uint64 typedef round trips"}
    environment = helper.acceptance_environment(dotnet)
    print(f"NativeAOT acceptance evidence: {work}", flush=True)
    try:
        minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(2 * 1024 ** 3)))
        if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
            raise RuntimeError("NativeAOT compilation requires its declared positive free-space floor.")
        compiler = find_compiler(args.compiler)
        report["compiler"] = compiler
        (work / "global.json").write_text(json.dumps({"sdk": {
            "version": "10.0.402", "rollForward": "latestPatch", "allowPrerelease": False}}), encoding="utf-8")
        report["dotnetSdkVersion"] = helper.run([dotnet, "--version"], work, work / "dotnet-version.log", environment).strip()
        if report["dotnetSdkVersion"] != "10.0.402":
            raise RuntimeError("The native invocation gate requires an actual .NET 10 SDK.")
        report["nativeCompilerVersion"] = helper.run([compiler, "--version"], work, work / "native-compiler-version.log", environment).strip()
        shutil.copytree(ROOT / "tests/fixtures/NativeApi", work / "Native")
        if args.inject_native_error:
            source = work / "Native/api.c"
            source.write_text(source.read_text(encoding="utf-8").replace("return left + right;", "return left + right + 1;"), encoding="utf-8")
        if args.tool_path:
            tool, runtime = args.tool_path.resolve(), args.runtime_path.resolve()
            if not tool.is_file() or not runtime.is_file():
                raise RuntimeError("The explicitly selected installed tool/runtime product is missing.")
        else:
            if not args.no_build:
                for name, directory in (("BGCS.Tool", "src/BGCS.Tool"), ("BGCS.Frontend.Clang.Worker", "extensions/frontends/BGCS.Frontend.Clang.Worker"), ("BGCS.Runtime", "src/BGCS.Runtime"), ("BGCS.Extension.BrowserWasm", "extensions/BGCS.Extension.BrowserWasm"), ("BGCS.Extension.NativeAot", "extensions/BGCS.Extension.NativeAot")):
                    helper.build_project(dotnet, ROOT / directory / (name + ".csproj"), work,
                                         work / (name + "-build.log"), environment, args.configuration)
            tool = cpp_acceptance_fixture.stage_repository_generator(ROOT, work, args.configuration)
            runtime = ROOT / f"src/BGCS.Runtime/bin/{args.configuration}/net10.0/BGCS.Runtime.dll"
        report.update(cpp_acceptance_fixture.execution_identity(ROOT, tool, runtime))
        shutil.copyfile(runtime, work / "BGCS.Runtime.dll")
        bridge = cpp_acceptance_fixture.prepare(
            ROOT, work, dotnet, tool, compiler, environment, helper.run, configuration=args.configuration)
        aggregates = prepare_aggregates(work)
        report["aggregateFixtureInputs"] = {path.name: digest(path) for path in (aggregates["header"], aggregates["source"], aggregates["probe"])}
        bridge_source = bridge["source"]
        architecture = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
        rid = ("win" if os.name == "nt" else "osx" if platform.system() == "Darwin" else "linux") + "-" + architecture
        library = "api.dll" if os.name == "nt" else "libapi.dylib" if platform.system() == "Darwin" else "libapi.so"
        native_object = work / ("native-api.obj" if os.name == "nt" else "native-api.o")
        c_command = [compiler, "-std=c17", "-DBUILD_SHARED=1", "-c", str(work / "Native/api.c"),
                     "-o", str(native_object)]
        if os.name != "nt":
            c_command.append("-fPIC")
        helper.run(c_command, work, work / "native-c-build.log", environment)
        aggregate_object = work / ("native-aggregates.obj" if os.name == "nt" else "native-aggregates.o")
        aggregate_command = [compiler, "-std=c17", "-c", str(aggregates["source"]), "-o", str(aggregate_object)]
        if os.name != "nt":
            aggregate_command.append("-fPIC")
        helper.run(aggregate_command, work, work / "native-aggregate-build.log", environment)
        native_probe = work / ("aggregate-probe.exe" if os.name == "nt" else "aggregate-probe")
        helper.run([compiler, "-std=c17", str(aggregates["probe"]), str(aggregate_object), "-o", str(native_probe)],
                   work, work / "native-aggregate-probe-build.log", environment)
        native_result = json.loads(helper.run([str(native_probe)], work, work / "native-aggregate-probe-invocation.log", environment))
        if not native_result.get("success") or native_result.get("checks") != len(cpp_acceptance_fixture.AGGREGATE_CHECKS) or not native_result.get("signature"):
            raise RuntimeError("The independent native aggregate comparison probe did not complete all transformed-value checks.")
        report["aggregateNativeOracle"] = {**native_result, "probeSha256": digest(native_probe)}
        command = [cpp_acceptance_fixture.cxx_compiler(compiler), "-std=c++23", "-shared",
                   str(native_object), str(aggregate_object), str(bridge_source), "-I" + str(bridge["header"].parent),
                   "-I" + str(work / "CppFacade"), "-DBUILD_SHARED=1", "-o", str(work / library)]
        if os.name == "nt":
            # Export the same active declarations that this compiler sees. The
            # source header also contains browser-only protocols whose symbols
            # do not exist in a desktop object.
            export_input = work / "native-export-declarations.c"
            export_input.write_text('#include "' + (work / "Native/api.h").as_posix() + '"\n', encoding="utf-8")
            active_declarations = helper.run(
                [compiler, "-std=c17", "-DBUILD_SHARED=1", "-E", "-P", str(export_input)],
                work, work / "native-export-declarations.log", environment)
            exports = sorted(set(re.findall(r"\b(bgcs_\w+)\s*\(", active_declarations)))
            if not exports:
                raise RuntimeError("The native compiler produced no active fixture export declarations.")
            report["nativeExportDeclarations"] = {
                "compiler": compiler, "preprocessedSha256": digest(work / "native-export-declarations.log"), "exports": exports}
            command += ["-Wl,/EXPORT:" + symbol for symbol in exports]
        else:
            command.append("-fPIC")
        helper.run(command, work, work / "native-build.log", environment)
        for mode in MODES:
            config = cpp_acceptance_fixture.binding_configuration(mode, "NativeAotAcceptance", compiler, bridge)
            config["source"]["headers"].append(str(aggregates["header"]))
            config["api"]["names"] = {**config["api"]["names"], **{
                symbol: "".join(part.capitalize() for part in symbol.removeprefix("bgcs_").split("_"))
                for symbol in aggregates["symbols"]}}
            path = work / (mode + ".json")
            path.write_text(json.dumps(config, indent=2), encoding="utf-8")
            helper.run(cpp_acceptance_fixture.tool_command(dotnet, tool) + ["generate", str(path)], work,
                       work / (mode + "-generation.log"), environment)
        report["generatedAbiManifests"] = [cpp_acceptance_fixture.generated_abi_identity(work, mode) for mode in MODES]
        if len({item["nativeTargetId"] for item in report["generatedAbiManifests"]}) != 1 or any(
                item["nativePointerSize"] != 8 for item in report["generatedAbiManifests"]):
            raise RuntimeError("NativeAOT import modes must use the same resolved 64-bit native target.")
        cpp_acceptance_fixture.write_consumer(ROOT, work, "NativeAotAcceptance", 8, aggregates=True)
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
        expected = cpp_acceptance_fixture.expected_checks(aggregates=True)
        checks = invocation_report.get("checks", [])
        if not invocation_report.get("success") or len(checks) != len(expected) or set(checks) != expected:
            raise RuntimeError("NativeAOT invocation did not complete every required check.")
        signatures = invocation_report.get("aggregateSignatures", {})
        if set(signatures) != set(MODES) or any(value != native_result["signature"] for value in signatures.values()):
            raise RuntimeError("Every NativeAOT import mode must reproduce the independent native aggregate transformation results.")
        report["aggregateNativeComparisonPassed"] = True
        report["success"] = True
        print(f"Passed: {len(checks)} checks across {len(MODES)} import modes.", flush=True)
    except Exception as error:
        report["error"] = str(error)
        print(f"FAILED: {error}", flush=True)
    finally:
        path = work / "report.json"
        path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"Report: {path}", flush=True)
        if not args.keep_build_output:
            cpp_acceptance_fixture.cleanup_build_outputs(work)
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
