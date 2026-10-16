"""Prepare independent fixtures through BGCS 3.0's public, neutral bridge pipeline."""

import json
from pathlib import Path
import shutil
import importlib.util
import platform
import os
import hashlib
import subprocess

from acceptance_evidence import digest as file_digest, publication, source_identity

MODES = ("DllImport", "LibraryImport", "FunctionTable")
MODE_CHECKS = ("scalar", "wide-integer", "wide-enum", "native-long", "bool", "layout", "bitfields",
               "buffer", "capacity", "alias-ref", "alias-in-out", "alias-record-field", "alias-span-ref",
               "handle", "callback", "null-callback", "managed-callback", "managed-callback-exception", "release",
               "cpp-construction", "cpp-method", "cpp-inheritance", "cpp-release")
TABLE_CHECKS = ("missing-symbol", "function-table-context", "failed-initialization", "borrowed-context", "borrowed-storage")
AGGREGATE_CHECKS = ("aggregate-small", "aggregate-pair", "aggregate-mixed", "aggregate-large", "aggregate-union",
                    "aggregate-nested", "aggregate-floats", "aggregate-doubles", "aggregate-inline-array", "aggregate-packed",
                    "aggregate-reverse-callback", "aggregate-single-float", "aggregate-single-double")


def tool_command(dotnet, tool):
    return [dotnet, str(tool)] if Path(tool).suffix.lower() == ".dll" else [str(tool)]


def execution_identity(root, tool, runtime):
    """Record real executed bytes and fixture revision without equating a dirty checkpoint with release evidence."""
    def digest(path):
        with Path(path).open("rb") as source:
            return hashlib.file_digest(source, "sha256").hexdigest()
    source = source_identity(root)
    identity = {"sourceRevision": source["revision"], "sourceWorktreeDirty": source["workingTreeDirty"],
                "checkedOutSourceSha256": source["checkedOutSourceSha256"],
                "generatorEntrySha256": digest(tool), "runtimeAssemblySha256": digest(runtime)}
    managed_entries = list(Path(tool).parent.rglob("BGCS.Tool.dll")) if Path(tool).suffix.lower() != ".dll" else [Path(tool)]
    if len(managed_entries) == 1:
        execution_root = managed_entries[0].parent
        identity["generatorAssemblySha256"] = digest(managed_entries[0])
        assets = [{"path": path.relative_to(execution_root).as_posix(), "sha256": digest(path)}
                  for path in sorted(execution_root.rglob("*")) if path.is_file()]
        payload = json.dumps(assets, sort_keys=True, separators=(",", ":")).encode("utf-8")
        identity["executionClosureSha256"] = hashlib.sha256(payload).hexdigest()
        identity["executionAssetCount"] = len(assets)
        worker_manifest = execution_root / "bgcs/worker/worker-assets.manifest.json"
        if worker_manifest.is_file(): identity["workerAssetManifestSha256"] = digest(worker_manifest)
    return identity


def stage_repository_generator(root, work, configuration):
    """Use the same exact managed/native closure logic as product distribution."""
    spec = importlib.util.spec_from_file_location("bgcs_pack_closure", root / "eng/pack.py")
    pack = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(pack)
    machine = platform.machine().lower()
    if machine not in ("amd64", "x86_64", "arm64", "aarch64"):
        raise RuntimeError("The acceptance generator host architecture has no certified parser closure.")
    rid = ("win" if os.name == "nt" else "osx" if platform.system() == "Darwin" else "linux") + "-" + ("arm64" if machine in ("arm64", "aarch64") else "x64")
    tool_root = work / "generator"
    pack.execution_closure("src/BGCS.Tool", configuration, "BGCS.Tool", tool_root)
    worker = tool_root / "bgcs/worker"
    pack.execution_closure("src/BGCS.Frontend.Clang.Worker", configuration, "BGCS.Frontend.Clang.Worker", worker)
    supplemental = os.environ.get("BGCS_OSX_X64_PACKAGE_RUNTIME_DIR") or os.environ.get("BGCS_CLANG_RUNTIME_DIR")
    native = pack.native_closure(pack.graph("src/BGCS.Frontend.Clang.Worker"), rid, Path(supplemental) if supplemental else None)
    for name, path in native.items():
        destination = worker / "runtimes" / rid / "native" / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, destination)
    entries = [pack.artifact(path.relative_to(worker).as_posix(), path) for path in sorted(worker.rglob("*")) if path.is_file()]
    (worker / "worker-assets.manifest.json").write_text(json.dumps({"schemaVersion": 1, "hostRid": rid,
        "parserIdentity": "ClangSharp/20.1.2.4;LLVM/20", "artifacts": entries}, indent=2), encoding="utf-8")
    return tool_root / "BGCS.Tool.dll"


def target_configuration(compiler, sysroot=None, cpp=False):
    settings = {"compilerPath": cxx_compiler(compiler) if cpp else str(compiler)}
    target = {"id": "host", "settings": settings}
    if sysroot:
        target["id"] = "emscripten-wasm32-emscripten"
        settings.update(sysRoot=str(sysroot), systemIncludeDirectories=[str(sysroot / "include")],
                        cxxSystemIncludeDirectories=[str(sysroot / "include/c++/v1")])
    return target


def prepare(root, work, dotnet, tool, compiler, environment, run, sysroot=None):
    shutil.copytree(root / "tests/fixtures/CppFacade", work / "CppFacade")
    config = {
        "schemaVersion": 1,
        "library": {"id": "cpp_facade", "nativeName": "api", "bridgeExportPrefix": "acceptance_cpp"},
        "source": {"language": "cpp", "languageStandard": "c++23", "headers": ["CppFacade/counter.hpp"],
                   "parseComments": False, "parseMacros": False, "includeSystemDeclarations": False},
        "targets": [target_configuration(compiler, sysroot, cpp=True)],
        "cache": {"enabled": False}, "output": {"root": "Bridge"},
    }
    path = work / "bridge.json"
    path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    run(tool_command(dotnet, tool) + ["bridge", str(path)], work, work / "bridge-generation.log", environment)
    models = list((work / "Bridge").glob("*/native/metadata/bridge.module.json"))
    if len(models) != 1:
        raise RuntimeError("Bridge generation must produce exactly one neutral facade model.")
    def identifiers(value):
        if isinstance(value, dict):
            if set(value) == {"value"}: return value["value"]
            return {key: identifiers(item) for key, item in value.items()}
        if isinstance(value, list): return [identifiers(item) for item in value]
        return value
    module = identifiers(json.loads(models[0].read_text(encoding="utf-8-sig")))
    if module.get("modelRevision") != 1:
        raise RuntimeError("The fixture reader requires bridge model revision 1.")
    declarations = {item["id"]: item for item in module["source"]["declarations"]}
    types = {item["id"]: item for item in module["source"]["types"]}

    def native_name(native_type):
        return declarations.get(types.get(native_type, {}).get("declaration"), {}).get("qualifiedName")

    names, operations = {}, {}
    wanted = {("construct", "BridgeCounter"): "BridgeCounterCreate", ("destroy", "BridgeCounter"): "BridgeCounterDestroy",
              ("construct", "BridgeBaseB"): "BridgeBaseBCreate", ("destroy", "BridgeBaseB"): "BridgeBaseBDestroy"}
    for export in module["exports"]:
        operation = export["operation"]
        kind = operation["kind"]
        if isinstance(kind, int):
            kind = ("freeCall", "staticCall", "instanceCall", "construct", "destroy", "baseCast", "checkedDerivedCast", "standardValue", "defaultConstruct")[kind]
        if kind == "defaultConstruct": kind = "construct"
        owner = native_name(operation.get("ownerType"))
        declaration = declarations.get(operation.get("declaration"), {})
        logical_name = wanted.get((kind, owner))
        if declaration.get("qualifiedName") in ("BridgeCounter::Add", "BridgeCounter::Read", "BridgeCounter::LiveCount"):
            logical_name = "BridgeCounter" + declaration["name"]
        cast_name = native_name(operation.get("castTarget"))
        if kind == "baseCast" and owner == "BridgeCounter" and cast_name == "BridgeBaseB":
            logical_name = "BridgeCounterAsBridgeBaseB"
        if kind == "checkedDerivedCast" and owner == "BridgeBaseB" and cast_name == "BridgeCounter":
            logical_name = "BridgeCounterFromBridgeBaseB"
        if logical_name:
            if logical_name in operations:
                raise RuntimeError("Ambiguous required facade operation: " + logical_name)
            operations[logical_name], names[export["name"]] = export, logical_name
    required = set(wanted.values()) | {"BridgeCounterAdd", "BridgeCounterRead", "BridgeCounterLiveCount",
                                     "BridgeCounterAsBridgeBaseB", "BridgeCounterFromBridgeBaseB"}
    if set(operations) != required:
        raise RuntimeError("Neutral bridge omitted required facade operations: " + str(sorted(required - set(operations))))
    bridge_types = {item["id"]: item for item in module["types"]}
    for item in module["types"]:
        if item["kind"] in ("handle", 2):
            names[item["cName"]] = native_name(item["nativeType"])
    names[module["exportPrefix"] + "_error"] = "BridgeError"
    names[module["exportPrefix"] + "_error_release"] = "BridgeErrorRelease"
    names[module["exportPrefix"] + "_error_message"] = "BridgeErrorMessage"
    target_root = models[0].parent.parent.parent
    header = target_root / ("native/include/" + module["exportPrefix"] + ".h")
    source = target_root / ("native/src/" + module["exportPrefix"] + ".cpp")
    if not header.is_file() or not source.is_file():
        raise RuntimeError("Neutral bridge artifact paths differ from the declared native library stem.")
    symbols = [item["name"] for item in module["exports"]]
    symbols += [module["exportPrefix"] + "_error_message", module["exportPrefix"] + "_error_release"]
    native_source = work / "Native/api.c"
    text = '#include "' + header.as_posix() + '"\n' + native_source.read_text(encoding="utf-8")
    text = text.replace("    #undef BGCS_FIXTURE_SYMBOL",
                        "\n".join("    BGCS_FIXTURE_SYMBOL(" + symbol + ")" for symbol in symbols)
                        + "\n    #undef BGCS_FIXTURE_SYMBOL")
    native_source.write_text(text, encoding="utf-8")
    return {"source": source, "header": header, "symbols": symbols, "names": names,
            "operations": operations, "types": bridge_types}


def binding_configuration(mode, namespace, compiler, bridge, sysroot=None):
    value_parameters = [
        {"function": function, "parameter": parameter, "direction": direction,
         "lifetime": "call", "retainsPointer": False, "allowsNull": False}
        for function, parameter, direction in (
            ("bgcs_alias_ref", "first", "inputOutput"), ("bgcs_alias_ref", "second", "inputOutput"),
            ("bgcs_alias_in_out", "input", "input"), ("bgcs_alias_in_out", "output", "output"),
            ("bgcs_alias_record", "record", "inputOutput"), ("bgcs_alias_record", "field", "inputOutput"),
            ("bgcs_alias_span", "value", "inputOutput"))]
    configuration = {
        "schemaVersion": 1,
        "library": {"id": "native_" + mode.lower(), "nativeName": "api"},
        "source": {"language": "c", "languageStandard": "c17", "headers": ["Native/api.h", str(bridge["header"])],
                   "parseComments": False, "parseMacros": False, "includeSystemDeclarations": False},
        "targets": [target_configuration(compiler, sysroot)],
        "api": {"name": "NativeApi", "namespace": namespace + "." + mode, "style": "instance",
                "importMode": mode[0].lower() + mode[1:], "framework": "net10.0",
                "runtime": "browserWasm" if sysroot else "nativeAot", "names": bridge["names"],
                "enums": [{"name": "WideFlags", "underlyingType": "uInt64", "isFlags": True,
                           "values": [{"name": "HighBit", "value": str(1 << 60)},
                                      {"name": "AddedBit", "value": str(1 << 59)},
                                      {"name": "TopBit", "value": str(1 << 63)},
                                      {"name": "All", "value": str((1 << 64) - 1)}]}],
                "enumMappings": {"BgcsWideFlags": "WideFlags"}},
        "semantics": {"valueParameters": value_parameters,
            "bufferParameters": [{"function": "bgcs_alias_span", "parameter": "buffer", "lengthParameter": "length",
                "lengthUnit": "elements", "direction": "inputOutput", "lifetime": "call", "retainsPointer": False, "allowsNull": False}],
            "callbackParameters": [
            {"function": "bgcs_invoke_callback", "parameter": "callback", "lifetime": "call", "retained": False,
             "threading": "callerThread", "userDataParameter": "state", "callableUserDataIndex": 2, "failureReturn": "-1"}]},
        "runtime": {"mode": "package"},
        "output": {"root": "Generated/" + mode, "layout": "singleFile", "singleFileName": "Bindings.cs"},
        "cache": {"enabled": False},
    }
    if sysroot:
        # Each assertion is the fixture's actual single-thread event-loop protocol,
        # not a claim that arbitrary native unregister/cancel functions are safe.
        for function, parameter, state_index, unregister, lifetime in (
                ("bgcs_event_register", "callback", 1, "bgcs_event_unregister", "registration"),
                ("bgcs_event_async_start", "completion", 2, "bgcs_event_async_stop", "operation")):
            callback = {"function": function, "parameter": parameter, "lifetime": lifetime, "retained": True,
                        "threading": "eventLoop", "userDataParameter": "state", "callableUserDataIndex": state_index,
                        "unregisterFunction": unregister, "unregisterQuiescence": "noActiveNativeInvocations",
                        "registration": {"kind": "returnedHandle", "unregisterHandleParameter": "handle",
                            "nullResultQuiescence": "noActiveNativeInvocations", "releaseThreadDomain": "eventLoop",
                            "unregisterIsNonBlocking": True}}
            if lifetime == "registration":
                callback["failureReturn"] = "-7"
            configuration["semantics"]["callbackParameters"].append(callback)
        configuration["semantics"]["bufferParameters"].append({
            "function": "bgcs_event_async_start", "parameter": "values", "lengthParameter": "length",
            "lengthUnit": "elements", "direction": "inputOutput", "lifetime": "operation",
            "retainsPointer": True, "allowsNull": False})
        configuration["semantics"]["asyncOperations"] = [{
            "function": "bgcs_event_async_start", "completionCallback": "completion",
            "completionQuiescence": "noNewInvocations", "resultArgumentIndex": 0, "statusArgumentIndex": 1,
            "successStatus": "0", "cancellationStatus": "1", "cancelFunction": "bgcs_event_async_cancel",
            "cancelHandleParameter": "handle", "cancellationIsNonBlocking": True,
            "cancellationQuiescence": "unknown", "retainedParameters": ["values"]}]
    return configuration


def expected_checks(aggregates=False):
    checks = MODE_CHECKS + AGGREGATE_CHECKS if aggregates else MODE_CHECKS
    return {mode + ":" + check for mode in MODES for check in checks} | set(TABLE_CHECKS)


def expected_lifecycle_checks():
    """Separate browser lifecycle obligations; the existing 74 raw checks stay mandatory."""
    checks = ("retained-close", "retained-exception", "retained-self-close", "retained-context-close",
              "async-native-frame", "async-cancel-request", "async-cancel-ack", "async-inline-completion",
              "async-duplicate-completion", "async-context-close", "native-quiescence")
    return {mode + ":" + check for mode in MODES for check in checks}


def generated_abi_identity(work, mode):
    """Use the verified published ABI metadata, rather than the request's host alias."""
    output = work / "Generated" / mode
    manifest = publication(output, "native_" + mode.lower())
    entries = [item for item in manifest["artifacts"] if item["relativePath"].endswith("/metadata/abi.manifest.json")]
    if len(entries) != 1:
        raise RuntimeError("Each import mode must publish exactly one owned ABI manifest.")
    path = output / entries[0]["relativePath"]
    module = json.loads(path.read_text(encoding="utf-8"))["module"]
    return {"mode": mode, "nativeTargetId": module["targetId"], "nativePointerSize": module["pointerSize"],
            "nativeByteOrder": module["byteOrder"], "abiManifestSha256": file_digest(path)}


def write_consumer(root, work, namespace, pointer_size, aggregates=False):
    invocation = (root / "tests/wasm/Invocation.cs.in").read_text(encoding="utf-8")
    if pointer_size not in (4, 8):
        raise RuntimeError("The acceptance fixture requires a proven 32-bit or 64-bit pointer target.")
    runners = "\n".join(invocation.replace("__MODE__", mode).replace("__SIZE_TYPE__", "uint" if pointer_size == 4 else "ulong") for mode in MODES)
    if aggregates:
        if pointer_size != 8:
            raise RuntimeError("Aggregate by-value acceptance requires a separately certified 64-bit desktop profile.")
        aggregate_invocation = (root / "tests/native-aot/Aggregates.cs.in").read_text(encoding="utf-8")
        runners += "\n" + "\n".join(aggregate_invocation.replace("__MODE__", mode) for mode in MODES)
    template = (root / "tests/wasm/Program.cs.in").read_text(encoding="utf-8")
    browser = namespace == "WasmAcceptance"
    text = template.replace("__NAMESPACE__", namespace)
    text = text.replace("__BROWSER_USING__", "using System.Runtime.InteropServices.JavaScript;" if browser else "")
    text = text.replace("__BROWSER_EXPORT__", "[JSExport]\n    public static string GetReport() => m_report;" if browser else "")
    text = text.replace("__ENTRY_POINT__", "private static global::System.Threading.Tasks.Task<int> Main() => BrowserLifecycleRunner.RunAsync(RunBaseline, FinishLifecycle);" if browser
                        else "private static int Main() => RunBaseline();")
    text = text.replace("__BASELINE_PRINT__", "" if browser else "Console.WriteLine(m_report);")
    text = text.replace("__BROWSER_LIFECYCLE_REPORT__", "internal static FunctionTable.NativeApi CreateLifecycleTable() => FunctionTable.NativeApi.FromProvider(new SymbolProvider());\n"
                        "    private static void FinishLifecycle(bool success, string? error, string[] checks) { m_lifecycleChecks = checks; m_report = SerializeReport(success, error); Console.WriteLine(m_report); }" if browser else "")
    text = text.replace("__MAIN_ASSERTION__", 'Assert(sizeof(nint) == 4, "The consumer must execute inside wasm32.");' if browser else "")
    text = text.replace("__AGGREGATE_DIRECT_CHECKS__", "RunAggregatesDllImport(dll); RunAggregatesLibraryImport(library);" if aggregates else "")
    text = text.replace("__AGGREGATE_TABLE_CHECKS__", "RunAggregatesFunctionTable(api);" if aggregates else "")
    text = text.replace("__STATIC_ADAPTER_CHECK__", 'using (var linked = FunctionTable.NativeApi.FromStatic()) Assert(linked.Raw.BgcsAdd(3, 4) == 7, "The generated static adapter resolves the complete native table.");' if browser else "")
    (work / "Program.cs").write_text(text.replace("__RUNNERS__", runners), encoding="utf-8")
    if browser:
        lifecycle = (root / "tests/wasm/Lifecycle.cs.in").read_text(encoding="utf-8")
        modes = (root / "tests/wasm/LifecycleMode.cs.in").read_text(encoding="utf-8")
        lifecycle = lifecycle.replace("__MODE_FACTORIES__", "\n".join(modes.replace("__MODE__", mode)
            .replace("__FACTORY__", "Program.CreateLifecycleTable()" if mode == "FunctionTable" else mode + ".NativeApi.Create()") for mode in MODES))
        (work / "Lifecycle.cs").write_text(lifecycle.replace("__NAMESPACE__", namespace), encoding="utf-8")


def cleanup_build_outputs(work):
    """Keep evidence, inputs and generated source while bounding large compiler/browser staging."""
    base = work.resolve()
    for name in ("generator", "published", "bin", "obj", "browser-profile"):
        path = work / name
        if not path.exists(): continue
        if path.is_symlink() or not path.resolve().is_relative_to(base):
            raise RuntimeError("Refusing to clean an acceptance staging path outside its owned run: " + str(path))
        shutil.rmtree(path)


def cxx_compiler(compiler):
    compiler = Path(compiler)
    stem = compiler.stem
    if stem in ("clang++", "g++", "c++"):
        return str(compiler)
    name = "clang++" if stem == "clang" else "g++" if stem in ("gcc", "cc") else None
    candidate = compiler.with_name(name + compiler.suffix) if name else None
    if candidate and candidate.is_file():
        return str(candidate)
    raise RuntimeError("The independent C++ fixture requires clang++ or g++ beside the selected C compiler.")
